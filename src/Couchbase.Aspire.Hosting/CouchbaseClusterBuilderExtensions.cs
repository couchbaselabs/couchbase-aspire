using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Couchbase.Aspire.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting;

public static partial class CouchbaseClusterBuilderExtensions
{
    private const string EnterpriseTagPrefix = "enterprise-";
    private const string CommunityTagPrefix = "community-";

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex VersionTagRegex();

    /// <summary>
    /// Adds a Couchbase server container to the application model.
    /// </summary>
    /// <remarks>
    /// This version of the package defaults to the <inheritdoc cref="CouchbaseContainerImageTags.Tag"/> tag of the <inheritdoc cref="CouchbaseContainerImageTags.Image"/> container image.
    /// </remarks>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource. This name will be used as the connection string name when referenced in a dependency.</param>
    /// <param name="clusterName">The parameter used to provide the cluster name. If <see langword="null"/> the resource name will be used.</param>
    /// <param name="userName">The parameter used to provide the user name for the RabbitMQ resource. If <see langword="null"/> a default value will be used.</param>
    /// <param name="password">The parameter used to provide the password for the RabbitMQ resource. If <see langword="null"/> a random password will be generated.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> AddCouchbase(this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource>? clusterName = null,
        IResourceBuilder<ParameterResource>? userName = null,
        IResourceBuilder<ParameterResource>? password = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        builder.Services.TryAddSingleton<CouchbaseNodeCertificateProvider>();

        // don't use special characters in the password, since it goes into a URI
        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password", special: false);

        var cluster = new CouchbaseClusterResource(name, clusterName?.Resource, userName?.Resource, passwordParameter);

        string? connectionString = null;
        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(cluster, async (@event, ct) =>
        {
            connectionString = await cluster.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);

            if (connectionString is null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{cluster.Name}' resource but the connection string was null.");
            }
        });

        builder.Eventing.Subscribe<InitializeResourceEvent>(async (@event, ct) =>
        {
            _ = Task.Run(async () =>
            {
                var rns = @event.Services.GetRequiredService<ResourceNotificationService>();

                await rns.WaitForResourceAsync(cluster.Name, KnownResourceStates.Running, ct)
                    .ConfigureAwait(false);

                // Since this is a custom resource, we must publish these events manually to trigger URLs, connection strings,
                // and health checks.
                await builder.Eventing.PublishAsync(new ResourceEndpointsAllocatedEvent(cluster, @event.Services), ct)
                    .ConfigureAwait(false);
                await builder.Eventing.PublishAsync(new ConnectionStringAvailableEvent(cluster, @event.Services), ct)
                    .ConfigureAwait(false);

                var userName = await cluster.UserNameReference.GetValueAsync(ct).ConfigureAwait(false);
                var password = await cluster.PasswordParameter.GetValueAsync(ct).ConfigureAwait(false);

                await rns.PublishUpdateAsync(cluster, s => s with
                {
                    Urls = [.. s.Urls.Select(p => p with { IsInactive = false })],
                    EnvironmentVariables = [
                        // These are useful for logging into the console, the only way we can display them on the dashboard currently is via environment variables
                        new("CB_USERNAME", userName, true),
                        new("CB_PASSWORD", password, true)
                    ]
                });
            }, ct);
        });

        if (builder.ExecutionContext.IsRunMode)
        {
            // Add servers based on the number of replicas
            builder.Eventing.Subscribe<BeforeStartEvent>(async (@event, ct) =>
            {
                var settings = await cluster.GetClusterSettingsAsync(builder.ExecutionContext, ct).ConfigureAwait(false);

                ContainerLifetimeAnnotation? containerLifetime = null;
                if (!cluster.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>())
                {
                    // Only apply container lifetimes if no custom CA is specified. If a custom CA is used the containers
                    // are recreated on every start, regardless of this setting, because the node certificates are not
                    // regenerated.
                    cluster.TryGetLastAnnotation(out containerLifetime);
                }

                var initialNodeFound = false;
                foreach (var serverGroup in cluster.ServerGroups.Values)
                {
                    var serverGroupBuilder = builder.CreateResourceBuilder(serverGroup);

                    var replicaCount = serverGroup.GetReplicaCount();
                    for (var i = 0; i < replicaCount; i++)
                    {
                        var server = serverGroupBuilder.AddServer($"{serverGroup.Name}-{i}", settings);

                        if (containerLifetime is not null)
                        {
                            server.WithLifetime(containerLifetime.Lifetime);
                        }

                        if (!initialNodeFound && serverGroup.Services.HasFlag(CouchbaseServices.Data))
                        {
                            initialNodeFound = true;
                            server.WithAnnotation<CouchbaseInitialNodeAnnotation>();

                            if (settings.ManagementPort is int managementPort)
                            {
                                // For the first server, set the static management port number, if configured
                                server.WithEndpoint(CouchbaseEndpointNames.Management, endpoint => endpoint.Port = managementPort);
                            }
                            if (settings.SecureManagementPort is int secureManagementPort && settings.Edition == CouchbaseEdition.Enterprise)
                            {
                                // For the first server, set the static management port number, if configured
                                server.WithEndpoint(CouchbaseEndpointNames.ManagementSecure, endpoint => endpoint.Port = secureManagementPort);
                            }
                        }

                        foreach (var configurationCallback in settings.ContainerConfigurationCallbacks)
                        {
                            configurationCallback(server);
                        }
                    }
                }
            });
        }

        var healthCheckKey = $"{name}_check";
        builder.Services.AddHealthChecks()
            .AddCouchbase(
                async (sp, ct) => {
                    var options = new ClusterOptions()
                        .WithConnectionString(connectionString ?? throw new InvalidOperationException("Connection string is unavailable"));

                    options.UserName = await cluster.UserNameReference.GetValueAsync(ct).ConfigureAwait(false);
                    options.Password = await cluster.PasswordParameter.GetValueAsync(ct).ConfigureAwait(false);

                    var certificationAuthority = cluster.GetClusterCertificationAuthority();
                    if (certificationAuthority is { TrustCertificate: true })
                    {
                        var callback = certificationAuthority.CreateValidationCallback();
                        options.HttpCertificateCallbackValidation = callback;
                        options.KvCertificateCallbackValidation = callback;
                    }

                    return await Cluster.ConnectAsync(options).WaitAsync(ct).ConfigureAwait(false);
                },
                name: healthCheckKey);

        int urlAdded = 0;
        var clusterBuilder = builder.AddResource(cluster)
            .WithInitialState(new()
            {
                ResourceType = "CouchbaseCluster",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Waiting,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, "Couchbase"),
                ],
            })
            .WithIconName("DatabaseMultiple")
            .WithHealthCheck(healthCheckKey)
            .WithUrls(context =>
            {
                if (context.ExecutionContext.IsRunMode && Interlocked.Exchange(ref urlAdded, 1) == 0)
                {
                    context.Urls.Add(new ResourceUrlAnnotation
                    {
                        Url = "/",
                        DisplayText = "Web Console",
                        Endpoint = cluster.Servers.FirstOrDefault(CouchbaseResourceExtensions.IsInitialNode)?.GetManagementEndpoint()
                    });
                }
            });

        if (builder.ExecutionContext.IsRunMode)
        {
            AddClusterInitializer(clusterBuilder);
        }

        return clusterBuilder;
    }

    public static IResourceBuilder<CouchbaseClusterResource> WithSettings(this IResourceBuilder<CouchbaseClusterResource> builder, Action<CouchbaseClusterSettingsCallbackContext> configureQuotas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureQuotas);

        builder.Resource.Annotations.Add(new CouchbaseClusterSettingsCallbackAnnotation(configureQuotas));

        return builder;
    }

    public static IResourceBuilder<CouchbaseClusterResource> WithSettings(this IResourceBuilder<CouchbaseClusterResource> builder, Func<CouchbaseClusterSettingsCallbackContext, Task> configureQuotas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureQuotas);

        builder.Resource.Annotations.Add(new CouchbaseClusterSettingsCallbackAnnotation(configureQuotas));

        return builder;
    }

    public static IResourceBuilder<CouchbaseClusterResource> WithMemoryQuotas(this IResourceBuilder<CouchbaseClusterResource> builder, CouchbaseMemoryQuotas? quotas)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.MemoryQuotas = quotas;
        });
    }

    /// <summary>
    /// Sets a static management port for the Couchbase cluster.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="port">Port number for the secure management endpoint, or <c>null</c> to assign a random port.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithManagementPort(this IResourceBuilder<CouchbaseClusterResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.ManagementPort = port;
        });
    }

    /// <summary>
    /// Sets a static secure management port for the Couchbase cluster.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="port">Port number for the secure management endpoint, or <c>null</c> to assign a random port.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithSecureManagementPort(this IResourceBuilder<CouchbaseClusterResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.SecureManagementPort = port;
        });
    }

    private static IResourceBuilder<CouchbaseClusterResource> WithContainerConfiguration(this IResourceBuilder<CouchbaseClusterResource> builder,
        Action<IResourceBuilder<CouchbaseServerResource>> callback)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(callback);

        return builder.WithSettings(context =>
        {
            context.Settings.ContainerConfigurationCallbacks.Add(callback);
        });
    }

    /// <summary>
    /// Allows overriding the image registry on Couchbase cluster.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="registry">Registry value.</param>
    /// <returns>The <see cref="IResourceBuilder{CouchbaseClusterResource}"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithImageRegistry(this IResourceBuilder<CouchbaseClusterResource> builder, string? registry)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithContainerConfiguration(serverBuilder =>
        {
            serverBuilder.WithImageRegistry(registry);
        });
    }

    /// <summary>
    /// Allows overriding the image on a Couchbase cluster.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="image">Image value.</param>
    /// <param name="tag">Tag value.</param>
    /// <returns>The <see cref="IResourceBuilder{CouchbaseClusterResource}"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithImage<T>(this IResourceBuilder<CouchbaseClusterResource> builder, string image, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(image);

        return builder.WithContainerConfiguration(serverBuilder =>
        {
            serverBuilder.WithImage(image, tag);
        });
    }

    /// <summary>
    /// Allows overriding the image tag on a Couchbase cluster.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="tag">Tag value.</param>
    /// <returns>The <see cref="IResourceBuilder{CouchbaseClusterResource}"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithImageTag(this IResourceBuilder<CouchbaseClusterResource> builder, string tag)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tag);

        return builder.WithContainerConfiguration(serverBuilder =>
        {
            serverBuilder.WithImageTag(tag);
        });
    }

    /// <summary>
    /// Select the edition of Couchbase Server.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="edition">The edition of Couchbase Serer.</param>
    /// <returns>The <see cref="IResourceBuilder{CouchbaseClusterResource}"/>.</returns>
    /// <remarks>
    /// If using custom image registries or tags, the tag will be prefixed with "enterprise-" or "community-"
    /// if the tag is a simple version number. To prevent this, set the custom image tag after setting the edition.
    /// </remarks>
    public static IResourceBuilder<CouchbaseClusterResource> WithCouchbaseEdition(this IResourceBuilder<CouchbaseClusterResource> builder, CouchbaseEdition edition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Enum.IsDefined(edition))
        {
            throw new ArgumentException($"{nameof(edition)} is not a valid Couchbase edition.", nameof(edition));
        }

        return builder
            .WithSettings(context =>
            {
                context.Settings.Edition = edition;
            })
            .WithContainerConfiguration(serverBuilder =>
            {
                if (serverBuilder.Resource.TryGetLastAnnotation<ContainerImageAnnotation>(out var annotation))
                {
                    var tag = annotation.Tag;
                    if (tag is not null)
                    {
                        // Remove the current prefix, if present
                        if (tag.StartsWith(EnterpriseTagPrefix))
                        {
                            tag = tag[EnterpriseTagPrefix.Length..];
                        }
                        else if (tag.StartsWith(CommunityTagPrefix))
                        {
                            tag = tag[CommunityTagPrefix.Length..];
                        }

                        // Add the prefix if the current tag is a simple version number
                        if (VersionTagRegex().IsMatch(tag))
                        {
                            annotation.Tag = $"{(edition == CouchbaseEdition.Enterprise ? EnterpriseTagPrefix : CommunityTagPrefix)}{tag}";
                        }
                    }
                }
            });
    }

    /// <summary>
    /// Enables TLS for cluster communications using a root certification authority.
    /// </summary>
    /// <param name="builder">Builder for the couchbase cluster.</param>
    /// <param name="caCertificate">Certification authority certificate to use. Must include a private key.</param>
    /// <param name="certificateChain">Optional certificate chain to include with the CA certificate.</param>
    /// <param name="trustCertificate">If the certificate must be explicitly trusted for initialization and health check operations.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    /// <remarks>
    /// The root certificate must be trusted by any application connecting to the cluster. It must also be trusted
    /// by the host running Aspire if <paramref name="trustCertificate"/> is <c>false</c>.
    /// </remarks>
    public static IResourceBuilder<CouchbaseClusterResource> WithRootCertificationAuthority(this IResourceBuilder<CouchbaseClusterResource> builder,
        X509Certificate2 caCertificate, X509Certificate2Collection? certificateChain = null, bool trustCertificate = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(caCertificate);

        if (!caCertificate.HasPrivateKey)
        {
            throw new InvalidOperationException($"{nameof(caCertificate)} must include a private key.");
        }

        var annotation = new CouchbaseCertificateAuthorityAnnotation(caCertificate)
        {
            TrustCertificate = trustCertificate
        };

        if (certificateChain is not null)
        {
            annotation.CertificateChain.AddRange(certificateChain);
        }

        return builder.WithAnnotation(annotation, ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>
    /// Sets the lifetime behavior of the Couchbase cluster.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="lifetime">The lifetime behavior of the Couchbase cluster. The defaults behavior is <see cref="ContainerLifetime.Session"/>.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <see cref="ContainerLifetime.Persistent"/> is not currently supported when supplying a custom CA certificate
    /// via <see cref="WithRootCertificationAuthority(IResourceBuilder{CouchbaseClusterResource}, X509Certificate2, X509Certificate2Collection?, bool)"/>.
    /// </remarks>
    public static IResourceBuilder<CouchbaseClusterResource> WithLifetime(this IResourceBuilder<CouchbaseClusterResource> builder,
        ContainerLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithAnnotation(new ContainerLifetimeAnnotation { Lifetime = lifetime }, ResourceAnnotationMutationBehavior.Replace);
    }

    private static void AddClusterInitializer(IResourceBuilder<CouchbaseClusterResource> cluster)
    {
        var initializerResource = new CouchbaseClusterInitializerResource(
            $"{cluster.Resource.Name}-init", cluster.Resource);

        cluster.WithAnnotation(new CouchbaseClusterInitializerAnnotation() { Initializer = initializerResource });

        var builder = cluster.ApplicationBuilder.AddResource(initializerResource)
            .WithInitialState(new()
            {
                ResourceType = "CouchbaseClusterInitializer",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Waiting,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, "Couchbase")
                ]
            })
            .WithParentRelationship(cluster);

        cluster.ApplicationBuilder.Services.AddHttpClient(initializerResource.Name)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                SslOptions =
                {
                    // Trust the CA certificate, applicable
                    RemoteCertificateValidationCallback =
                        cluster.Resource.GetClusterCertificationAuthority() is { TrustCertificate: true } annotation
                            ? annotation.CreateValidationCallback()
                            : null
                }
            })
            .RemoveAllLoggers();

        cluster.ApplicationBuilder.Services.AddKeyedSingleton(initializerResource,
            static (sp, key) =>
            {
                var initializerResource = (CouchbaseClusterInitializerResource)key;

                return new CouchbaseClusterInitializer(
                    initializerResource,
                    sp.GetRequiredService<DistributedApplicationExecutionContext>(),
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(initializerResource.Name),
                    sp.GetRequiredService<ResourceLoggerService>().GetLogger(initializerResource),
                    sp.GetRequiredService<ResourceNotificationService>());
            });

        cluster.ApplicationBuilder.Eventing.Subscribe<InitializeResourceEvent>(initializerResource, (@event, ct) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var initializer = initializerResource.GetClusterInitializer(@event.Services);

                    await initializer.InitializeAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var logger = @event.Services.GetRequiredService<ResourceLoggerService>()
                        .GetLogger(initializerResource);

                    logger.LogError(ex, "An error occurred while initializing the Couchbase cluster.");
                    throw;
                }
            }, ct);

            return Task.CompletedTask;
        });
    }
}
