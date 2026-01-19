using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Couchbase.Aspire.Hosting.Api;
using Couchbase.Aspire.Hosting.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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

        if (builder.ExecutionContext.IsRunMode)
        {
            builder.Services.TryAddSingleton<CouchbaseNodeCertificateProvider>();
            builder.Services.TryAddSingleton<CouchbaseClusterOrchestrator>();
            builder.Services.TryAddSingleton<ICouchbaseApiService, CouchbaseApiService>();
            builder.Services.TryAddSingleton<CouchbaseOrchestratorEvents>();

            if (!builder.Services.Any(p => p.ImplementationType == typeof(CouchbaseOrchestratorService)))
            {
                // Our orchestrator must be registered before the built-in Aspire orchestrators, otherwise it won't
                // execute until after all resources are started.
                builder.Services.Insert(0, ServiceDescriptor.Singleton<IHostedService, CouchbaseOrchestratorService>());
            }
        }

        // don't use special characters in the password, since it goes into a URI
        var passwordParameter = password?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"{name}-password", special: false);

        var cluster = new CouchbaseClusterResource(name, clusterName?.Resource, userName?.Resource, passwordParameter);

        string? connectionString = null;
        builder.Eventing.Subscribe<ConnectionStringAvailableEvent>(cluster, async (@event, ct) =>
        {
            // Use the URI, not the connection string, since it is applied directly to ClusterOptions
            // The URI doesn't include the Aspire extensions for authentication
            connectionString = await cluster.UriExpression.GetValueAsync(ct).ConfigureAwait(false);

            if (connectionString is null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{cluster.Name}' resource but the connection string was null.");
            }
        });

        builder.Eventing.Subscribe<BeforeStartEvent>(async (@event, ct) =>
        {
            if (cluster.TryGetAnnotationsOfType<ExplicitStartupAnnotation>(out var _) is true)
            {
                // Delay server startup until explicitly started
                foreach (var server in cluster.Servers)
                {
                    server.Annotations.Add(new ExplicitStartupAnnotation());
                }
            }
        });

        CouchbaseApiService.AddHttpClient(builder.Services, cluster);

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
                        options.WithX509CertificateFactory(certificationAuthority);
                    }

                    // Only need one connection per node for health checks
                    options.NumKvConnections = 1;
                    options.MaxKvConnections = 1;

                    return await Cluster.ConnectAsync(options).WaitAsync(ct).ConfigureAwait(false);
                },
                serviceRequirementsFactory: _ => cluster.GetHealthCheckServiceRequirements(),
                name: healthCheckKey);

        var clusterBuilder = builder.AddResource(cluster)
            .WithInitialState(new()
            {
                ResourceType = "CouchbaseCluster",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.NotStarted,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, "Couchbase"),
                ],
            })
            .WithIconName("DatabaseMultiple")
            .WithHealthCheck(healthCheckKey)
            .WithUrls(context =>
            {
                const string displayText = "Web Console";
                if (context.Urls.Any(p => p.DisplayText == displayText))
                {
                    // Don't add again on restart
                    return;
                }

                // The primary server and the management endpoint for that server change as the application model
                // is being built, so defer adding the web console URL until this callback is invoked.
                var endpoint = cluster.GetPrimaryServer()?.GetManagementEndpoint();
                if (endpoint is not null)
                {
                    context.Urls.Add(new ResourceUrlAnnotation
                    {
                        Url = "/",
                        DisplayText = displayText,
                        Endpoint = endpoint,
                    });
                }
            })
            .OnInitializeResource(async (resource, @event, ct) =>
            {
                // Ensure we display the Docker image as the Source for the cluster

                var server = resource.GetPrimaryServer();
                if (server is not null)
                {
                    _ = Task.Run(async () =>
                    {
                        var rns = @event.Services.GetRequiredService<ResourceNotificationService>();

                        await foreach (var @event in rns.WatchAsync(ct).ConfigureAwait(false))
                        {
                            if (@event.Resource == server)
                            {
                                var serverSource = @event.Snapshot.Properties
                                   .FirstOrDefault(p => p.Name == "container.image")?
                                   .Value;

                                if (serverSource is not null)
                                {
                                    await rns.PublishUpdateAsync(resource, s =>
                                    {
                                        var currentSource = s.Properties.FirstOrDefault(p => p.Name == CustomResourceKnownProperties.Source);

                                        if (serverSource != currentSource?.Value)
                                        {
                                            return s with
                                            {
                                                Properties =
                                                    (currentSource is not null ? s.Properties.Remove(currentSource) : s.Properties)
                                                    .Add(new(CustomResourceKnownProperties.Source, serverSource))
                                            };
                                        }

                                        return s;
                                    });
                                }
                            }
                        }
                    }, ct);
                }
            })
            .WithCommand(KnownResourceCommands.StartCommand, "Start", async (context) =>
            {
                var orchestrator = context.ServiceProvider.GetRequiredService<CouchbaseClusterOrchestrator>();

                await orchestrator.StartResourceAsync(cluster, context.CancellationToken).ConfigureAwait(false);

                return CommandResults.Success();
            }, new CommandOptions
            {
                UpdateState = context =>
                {
                    var state = context.ResourceSnapshot.State?.Text;
                    if (state == KnownResourceStates.Starting || state == KnownResourceStates.RuntimeUnhealthy || string.IsNullOrEmpty(state))
                    {
                        return ResourceCommandState.Disabled;
                    }
                    else if (KnownResourceStates.TerminalStates.Contains(state) || state == KnownResourceStates.NotStarted ||
                        state == KnownResourceStates.Waiting || state == "Unknown")
                    {
                        return ResourceCommandState.Enabled;
                    }
                    else
                    {
                        return ResourceCommandState.Hidden;
                    }
                },
                IconName = "Play",
                IconVariant = IconVariant.Filled,
                IsHighlighted = true
            })
            .WithCommand(KnownResourceCommands.StopCommand, "Stop", async (context) =>
            {
                var orchestrator = context.ServiceProvider.GetRequiredService<CouchbaseClusterOrchestrator>();

                await orchestrator.StopResourceAsync(cluster, context.CancellationToken).ConfigureAwait(false);

                return CommandResults.Success();
            }, new CommandOptions
            {
                UpdateState = context =>
                {
                    var state = context.ResourceSnapshot.State?.Text;
                    if (state == KnownResourceStates.Stopping)
                    {
                        return ResourceCommandState.Disabled;
                    }
                    else if (state == KnownResourceStates.Running)
                    {
                        return ResourceCommandState.Enabled;
                    }
                    else
                    {
                        return ResourceCommandState.Hidden;
                    }
                },
                IconName = "Stop",
                IconVariant = IconVariant.Filled,
                IsHighlighted = true,
            });

        // Add the default single node server group
        clusterBuilder.AddServerGroup($"{name}-server", isDefaultServerGroup: true);

        return clusterBuilder;
    }

    /// <summary>
    /// Specify the Couchbase services to be enabled on this cluster. Only applies if no server groups are added.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="services">The services to be enabled.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithServices(this IResourceBuilder<CouchbaseClusterResource> builder,
        CouchbaseServices services)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var serverGroup = builder.Resource.ServerGroups.Values.FirstOrDefault(p => p.IsDefaultServerGroup);
        if (serverGroup is null)
        {
            throw new InvalidOperationException("Services may only be set on the Couchbase cluster when no service groups are added. Use WithServices on the server group instead.");
        }

        builder.ApplicationBuilder.CreateResourceBuilder(serverGroup).WithServices(services);

        return builder;
    }

    public static IResourceBuilder<CouchbaseClusterResource> WithSettings(this IResourceBuilder<CouchbaseClusterResource> builder, Action<CouchbaseClusterSettingsCallbackContext> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSettings);

        return builder.WithAnnotation(new CouchbaseClusterSettingsCallbackAnnotation(configureSettings));
    }

    public static IResourceBuilder<CouchbaseClusterResource> WithSettings(this IResourceBuilder<CouchbaseClusterResource> builder, Func<CouchbaseClusterSettingsCallbackContext, Task> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSettings);

        return builder.WithAnnotation(new CouchbaseClusterSettingsCallbackAnnotation(configureSettings));
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

        if (!builder.Resource.TryGetLastAnnotation<CouchbasePortsAnnotation>(out var annotation))
        {
            annotation = new CouchbasePortsAnnotation { ManagementPort = port };
            builder.WithAnnotation(annotation);
        }
        else
        {
            annotation.ManagementPort = port;
        }

        if (builder.Resource.GetPrimaryServer() is CouchbaseServerResource primaryServer)
        {
            annotation.ApplyToServer(builder.ApplicationBuilder.CreateResourceBuilder(primaryServer));
        }

        return builder;
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

        if (!builder.Resource.TryGetLastAnnotation<CouchbasePortsAnnotation>(out var annotation))
        {
            annotation = new CouchbasePortsAnnotation { SecureManagementPort = port };
            builder.WithAnnotation(annotation);
        }
        else
        {
            annotation.SecureManagementPort = port;
        }

        if (builder.Resource.GetPrimaryServer() is CouchbaseServerResource primaryServer)
        {
            annotation.ApplyToServer(builder.ApplicationBuilder.CreateResourceBuilder(primaryServer));
        }

        return builder;
    }

    private static IResourceBuilder<CouchbaseClusterResource> WithContainerImage(this IResourceBuilder<CouchbaseClusterResource> builder,
        Action<CouchbaseContainerImageAnnotation> callback)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(callback);

        if (!builder.Resource.TryGetLastAnnotation<CouchbaseContainerImageAnnotation>(out var annotation))
        {
            annotation = new CouchbaseContainerImageAnnotation();
            builder.WithAnnotation(annotation);
        }

        callback(annotation);

        return builder.UpdateExistingServers();
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

        return builder.WithContainerImage(annotation => annotation.ImageRegistry = registry);
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

        return builder.WithContainerImage(annotation => {
            annotation.Image = image;
            annotation.ImageTag = tag;
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

        return builder.WithContainerImage(annotation => annotation.ImageTag = tag);
    }

    /// <summary>
    /// Select the edition of Couchbase Server.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="edition">The edition of Couchbase Server.</param>
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

        // Note: WithContainerImage invokes UpdateExistingServers
        builder
            .WithAnnotation(new CouchbaseEditionAnnotation { Edition = edition }, ResourceAnnotationMutationBehavior.Replace)
            .WithContainerImage(annotation =>
            {
                var tag = annotation.ImageTag;
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
                        annotation.ImageTag = $"{(edition == CouchbaseEdition.Enterprise ? EnterpriseTagPrefix : CommunityTagPrefix)}{tag}";
                    }
                }
                else
                {
                    annotation.ImageTag = $"{(edition == CouchbaseEdition.Enterprise ? EnterpriseTagPrefix : CommunityTagPrefix)}{CouchbaseContainerImageTags.Tag}";
                }
            });

        return builder;
    }

    /// <summary>
    /// Select the index storage mode of Couchbase Server.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase cluster.</param>
    /// <param name="mode">The index storage mode. If <c>null</c>, selects a default based on the Couchbase edition.</param>
    /// <returns>The <see cref="IResourceBuilder{CouchbaseClusterResource}"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithIndexStorageMode(this IResourceBuilder<CouchbaseClusterResource> builder, CouchbaseIndexStorageMode? mode)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithAnnotation(new CouchbaseIndexStorageModeAnnotation { Mode = mode }, ResourceAnnotationMutationBehavior.Replace);
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

        if (builder.Resource.TryGetLastAnnotation<ContainerLifetimeAnnotation>(out var lifetimeAnnotation) &&
            lifetimeAnnotation.Lifetime == ContainerLifetime.Persistent)
        {
            ThrowLifetimeIncompatibleException();
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

        if (lifetime == ContainerLifetime.Persistent && builder.Resource.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>())
        {
            ThrowLifetimeIncompatibleException();
        }

        return builder
            .WithAnnotation(new ContainerLifetimeAnnotation { Lifetime = lifetime }, ResourceAnnotationMutationBehavior.Replace)
            .UpdateExistingServers();
    }

    /// <summary>
    /// Adds a named volume for each Couchbase server container resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="nameFactory">Factory which defines the name of the volume for each server. Defaults to an auto-generated name based on the application and resource names.</param>
    /// <returns>The <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<CouchbaseClusterResource> WithDataVolumes(this IResourceBuilder<CouchbaseClusterResource> builder,
        Func<IResourceBuilder<CouchbaseServerResource>, string?>? nameFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithAnnotation(new CouchbaseDataVolumeAnnotation { VolumeNameFactory = nameFactory })
            .UpdateExistingServers();
    }

    internal static IResourceBuilder<CouchbaseClusterResource> UpdatePrimaryServer(this IResourceBuilder<CouchbaseClusterResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var primaryServer = builder.Resource.GetPrimaryServer();
        if (primaryServer is not null && !primaryServer.GetCouchbaseServices().HasFlag(CouchbaseServices.Data))
        {
            // Primary server no longer has data service, remove primary server configuration
            if (primaryServer.TryGetAnnotationsOfType<CouchbasePrimaryServerAnnotation>(out var annotations))
            {
                foreach (var annotation in annotations)
                {
                    primaryServer.Annotations.Remove(annotation);
                }
            }

            // Re-apply dynamic configuration
            builder.ApplicationBuilder.CreateResourceBuilder(primaryServer).ApplyDynamicConfiguration();

            primaryServer = null;
        }

        if (primaryServer is null)
        {
            // Find a new primary server
            primaryServer = builder.Resource.Servers.FirstOrDefault(p => p.GetCouchbaseServices().HasFlag(CouchbaseServices.Data));
            if (primaryServer is not null)
            {
                builder.ApplicationBuilder.CreateResourceBuilder(primaryServer)
                    .WithAnnotation<CouchbasePrimaryServerAnnotation>()
                    .ApplyDynamicConfiguration();
            }
        }

        return builder;
    }

    private static IResourceBuilder<CouchbaseClusterResource> UpdateExistingServers(this IResourceBuilder<CouchbaseClusterResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var server in builder.Resource.Servers)
        {
            var serverBuilder = builder.ApplicationBuilder.CreateResourceBuilder(server);
            serverBuilder.ApplyDynamicConfiguration();
        }

        return builder;
    }

    [DoesNotReturn]
    private static void ThrowLifetimeIncompatibleException()
    {
        throw new InvalidOperationException("Persistent container lifetime is not compatible with a custom root certification authority.");
    }
}
