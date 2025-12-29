using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Couchbase.Aspire.Hosting.HealthChecks;
using Couchbase.Aspire.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting;

public static class CouchbaseClusterBuilderExtensions
{
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

                var initialNodeFound = false;
                foreach (var serverGroup in cluster.ServerGroups.Values)
                {
                    var serverGroupBuilder = builder.CreateResourceBuilder(serverGroup);

                    var replicaCount = serverGroup.GetReplicaCount();
                    for (var i = 0; i < replicaCount; i++)
                    {
                        var server = serverGroupBuilder.AddServer($"{serverGroup.Name}-{i}");

                        if (!initialNodeFound && serverGroup.Services.HasFlag(CouchbaseServices.Data))
                        {
                            initialNodeFound = true;
                            server.WithAnnotation<CouchbaseInitialNodeAnnotation>();

                            if (settings.ManagementPort is { } managementPort)
                            {
                                // For the first server, set the static management port number, if configured
                                server.WithEndpoint(CouchbaseEndpointNames.Management, endpoint => endpoint.Port = managementPort);
                            }
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

                    options.UserName = await cluster.UserNameReference.GetValueAsync(ct);
                    options.Password = await cluster.PasswordParameter.GetValueAsync(ct);

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
                        Endpoint = cluster.Servers.FirstOrDefault(p => p.Services.HasFlag(CouchbaseServices.Data))?
                            .GetEndpoint(CouchbaseEndpointNames.Management)
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

    public static IResourceBuilder<CouchbaseClusterResource> WithManagementPort(this IResourceBuilder<CouchbaseClusterResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.ManagementPort = port;
        });
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

        var httpClientName = $"{cluster.Resource.Name}-initializer-client";
        cluster.ApplicationBuilder.Services.AddHttpClient(httpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

        cluster.ApplicationBuilder.Eventing.Subscribe<InitializeResourceEvent>(initializerResource, (@event, ct) =>
        {
            _ = Task.Run(async () =>
            {
                var logger = @event.Services.GetRequiredService<ResourceLoggerService>()
                    .GetLogger(initializerResource);

                try
                {
                    var initializer = new CouchbaseClusterInitializer(
                        cluster.Resource,
                        initializerResource,
                        cluster.ApplicationBuilder.ExecutionContext,
                        @event.Services.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName),
                        logger,
                        @event.Services.GetRequiredService<ResourceNotificationService>());

                    await initializer.InitializeAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "An error occurred while initializing the Couchbase cluster.");
                    throw;
                }
            }, ct);

            return Task.CompletedTask;
        });
    }
}
