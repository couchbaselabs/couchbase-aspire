using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Couchbase.Aspire.Hosting.Api;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting.Orchestration;

internal sealed class CouchbaseClusterOrchestrator
{
    private const string CouchbaseServerInitializedPropertyName = "Couchbase Server Initialized";

    private readonly ICouchbaseApiService _apiService;
    private readonly DistributedApplicationModel _model;
    private readonly DistributedApplicationExecutionContext _executionContext;
    private readonly ResourceNotificationService _resourceNotificationService;
    private readonly ResourceLoggerService _resourceLoggerService;
    private readonly ResourceCommandService _resourceCommandService;
    private readonly IDistributedApplicationEventing _eventing;
    private readonly CouchbaseOrchestratorEvents _orchestratorEvents;
    private readonly ILogger<CouchbaseClusterOrchestrator> _logger;
    private readonly CancellationTokenSource _shutdownCancellation = new();

    private int _stopped;

    public CouchbaseClusterOrchestrator(
        ICouchbaseApiService apiService,
        DistributedApplicationModel model,
        DistributedApplicationExecutionContext executionContext,
        ResourceNotificationService resourceNotificationService,
        ResourceLoggerService resourceLoggerService,
        ResourceCommandService resourceCommandService,
        IDistributedApplicationEventing eventing,
        CouchbaseOrchestratorEvents orchestratorEvents,
        ILogger<CouchbaseClusterOrchestrator> logger)
    {
        _apiService = apiService;
        _model = model;
        _executionContext = executionContext;
        _resourceNotificationService = resourceNotificationService;
        _resourceLoggerService = resourceLoggerService;
        _resourceCommandService = resourceCommandService;
        _eventing = eventing;
        _orchestratorEvents = orchestratorEvents;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            WatchResourceEvents();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Cancellation received during orchestrator startup.");
            _shutdownCancellation.Cancel();
        }
        catch
        {
            _shutdownCancellation.Cancel();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _stopped, 1, 0) != 0)
        {
            return; // Already stopped/stop in progress.
        }

        _shutdownCancellation.Cancel();
    }

    private void WatchResourceEvents()
    {
        _eventing.Subscribe<InitializeResourceEvent>(async (@event, ct) =>
        {
            if (@event.Resource is CouchbaseClusterResource cluster)
            {
                await StartResourceCoreAsync(cluster, StartClusterAsync, isExplicitStart: false, ct).ConfigureAwait(false);
            }
        });

        _eventing.Subscribe<ResourceReadyEvent>(async (@event, ct) =>
        {
            if (@event.Resource is CouchbaseServerResource server)
            {
                // Start the cluster if not already started
                await InitializeServerAsync(server, ct).ConfigureAwait(false);
            }
        });

        _eventing.Subscribe<ResourceStoppedEvent>(async (@event, ct) =>
        {
            if (@event.Resource is CouchbaseServerResource server)
            {
                // Clear then initialized flag
                await SetServerInitializedPropertyAsync(server, false).ConfigureAwait(false);
            }
        });

        _orchestratorEvents.Subscribe<OnCouchbaseResourceStartingEvent>(async (@event, ct) =>
        {
            var beforeResourceStartedEvent = new BeforeResourceStartedEvent(@event.Resource, _executionContext.ServiceProvider);
            await _eventing.PublishAsync(beforeResourceStartedEvent, ct).ConfigureAwait(false);

            await PublishUpdateToHierarchyAsync(@event.Resource, (_, s) => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Starting,
            }).ConfigureAwait(false);

            if (@event.Resource is CouchbaseClusterResource cluster)
            {
                // Start the buckets in the cluster. Run in the background to avoid blocking the cluster start.
                foreach (var bucket in cluster.Buckets.Values)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await StartResourceAsync(bucket, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            // Ignore
                        }
                        catch (Exception ex)
                        {
                            var resourceLogger = _resourceLoggerService.GetLogger(bucket);
                            resourceLogger.LogError(ex, "Failed to start Couchbase bucket '{BucketName}'.", bucket.BucketName);
                        }
                    }, ct);
                }
            }
        });

        _orchestratorEvents.Subscribe<OnCouchbaseResourceStartedEvent>(async (@event, ct) =>
        {
            List<EnvironmentVariableSnapshot>? addEnvVars = null;
            if (@event.Resource is CouchbaseClusterResource cluster)
            {
                // These are useful for logging into the console, the only way we can display them on the dashboard currently is via environment variables
                addEnvVars = [
                    new("CB_USERNAME", await cluster.UserNameReference.GetValueAsync(ct).ConfigureAwait(false), true),
                    new("CB_PASSWORD", await cluster.PasswordParameter.GetValueAsync(ct).ConfigureAwait(false), true)
                ];
            }

            // Since this is a custom resource, we must publish these events manually to trigger URLs, connection strings,
            // and health checks.
            await _eventing.PublishAsync(new ResourceEndpointsAllocatedEvent(@event.Resource, _executionContext.ServiceProvider), ct)
                .ConfigureAwait(false);
            await _eventing.PublishAsync(new ConnectionStringAvailableEvent(@event.Resource, _executionContext.ServiceProvider), ct)
                .ConfigureAwait(false);

            await PublishUpdateToHierarchyAsync(@event.Resource, (r, s) => s with
            {
                State = KnownResourceStates.Running,
                EnvironmentVariables = addEnvVars is not null && r == @event.Resource
                    ? [
                        .. s.EnvironmentVariables.Where(p => !addEnvVars.Any(q => q.Name == p.Name)),
                        .. addEnvVars
                    ]
                    : s.EnvironmentVariables,
                Urls = [.. s.Urls.Select(p =>
                    p.Name is CouchbaseEndpointNames.Management or CouchbaseEndpointNames.ManagementSecure
                        ? p with { IsInactive = false }
                        : p)],
            }).ConfigureAwait(false);
        });

        _orchestratorEvents.Subscribe<OnCouchbaseResourceStoppingEvent>(async (@event, ct) =>
        {
            await PublishUpdateToHierarchyAsync(@event.Resource, (_, s) => s with
            {
                State = KnownResourceStates.Stopping,
                Urls = [],
            }).ConfigureAwait(false);

            if (@event.Resource is CouchbaseClusterResource cluster)
            {
                // Stop the buckets in the cluster
                await Task.WhenAll(cluster.Buckets.Values.Select(bucket =>
                    StopResourceAsync(bucket, ct))).ConfigureAwait(false);
            }
        });

        _orchestratorEvents.Subscribe<OnCouchbaseResourceStoppedEvent>(async (@event, ct) =>
        {
            await PublishUpdateToHierarchyAsync(@event.Resource, (r, s) => s with
            {
                StopTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Exited,
                ExitCode = r == @event.Resource ? @event.ExitCode : 0,
            }).ConfigureAwait(false);

            if (_resourceNotificationService.TryGetCurrentState(@event.Resource.Name, out var currentResourceEvent))
            {
                await _eventing.PublishAsync(new ResourceStoppedEvent(@event.Resource, _executionContext.ServiceProvider, currentResourceEvent), ct).ConfigureAwait(false);
            }
        });
    }

    private async Task StartResourceCoreAsync<T>(T resource, Func<T, ILogger, bool, CancellationToken, Task> callback, bool isExplicitStart, CancellationToken cancellationToken)
        where T : ICouchbaseCustomResource
    {
        var resourceLogger = _resourceLoggerService.GetLogger(resource);

        if (_resourceNotificationService.TryGetCurrentState(resource.Name, out var resourceEvent))
        {
            if (resourceEvent.Snapshot.State?.Text == KnownResourceStates.Running ||
                resourceEvent.Snapshot.State?.Text == KnownResourceStates.Starting)
            {
                // Already started or starting
                resourceLogger.LogWarning("Couchbase resource '{ResourceName}' is already in state '{ResourceState}'.", resource.Name, resourceEvent.Snapshot.State);
                return;
            }
        }

        if (!isExplicitStart)
        {
            var explicitStartup = resource.TryGetAnnotationsOfType<ExplicitStartupAnnotation>(out _) is true;
            if (explicitStartup)
            {
                // Don't startup automatically if explicit startup is requested
                return;
            }
        }

        // Note: This event publish is blocking if there are dependencies, and this resource enters a waiting state
        await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStartingEvent(resource), cancellationToken).ConfigureAwait(false);
        try
        {
            await callback(resource, resourceLogger, isExplicitStart, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            resourceLogger.LogError(ex, "Failed to start Couchbase resource '{ResourceName}'.", resource.Name);
        }
    }

    public async Task StartResourceAsync(ICouchbaseCustomResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        switch (resource)
        {
            case CouchbaseClusterResource cluster:
                await StartResourceCoreAsync(cluster, StartClusterAsync, isExplicitStart: true, cancellationToken).ConfigureAwait(false);
                break;
            case CouchbaseBucketBaseResource bucket:
                await StartResourceCoreAsync(bucket, CreateBucketAsync, isExplicitStart: true, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentException("Unsupported resource type.", nameof(resource));
        }
    }

    private async Task StopResourceCoreAsync<T>(T resource, Func<T, ILogger, CancellationToken, Task> callback, CancellationToken cancellationToken)
        where T : ICouchbaseCustomResource
    {
        var resourceLogger = _resourceLoggerService.GetLogger(resource);

        if (_resourceNotificationService.TryGetCurrentState(resource.Name, out var resourceEvent))
        {
            if (KnownResourceStates.TerminalStates.Contains(resourceEvent.Snapshot.State?.Text) ||
                resourceEvent.Snapshot.State == KnownResourceStates.Stopping)
            {
                // Already stopped or stopping
                resourceLogger.LogWarning("Couchbase resource '{ResourceName}' is already in state '{ResourceState}'.", resource.Name, resourceEvent.Snapshot.State);
                return;
            }
        }

        await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStoppingEvent(resource), cancellationToken).ConfigureAwait(false);
        try
        {
            await callback(resource, resourceLogger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            resourceLogger.LogError(ex, "Failed to stop Couchbase resource '{ResourceName}'.", resource.Name);
        }
    }

    public async Task StopResourceAsync(ICouchbaseCustomResource resource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        switch (resource)
        {
            case CouchbaseClusterResource cluster:
                await StopResourceCoreAsync(cluster, StopClusterAsync, cancellationToken).ConfigureAwait(false);
                break;
            case CouchbaseBucketBaseResource bucket:
                await StopResourceCoreAsync(bucket, StopBucketAsync, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentException("Unsupported resource type.", nameof(resource));
        }
    }

    private Task StartClusterAsync(CouchbaseClusterResource cluster, ILogger resourceLogger, bool isExplicitStart, CancellationToken cancellationToken)
    {
        // Run the intialization process as a background task
        _ = Task.Run(async () =>
        {
            try
            {
                var primaryServer = cluster.GetPrimaryServer();
                if (primaryServer is null)
                {
                    throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
                }

                if (isExplicitStart)
                {
                    // Start all servers in the cluster. This is only done on explicit start because on Aspire startup
                    // the servers are started automatically by the container orchestrator. Ideally this orchestrator would
                    // always control server lifecycle, but there is currently no event from the Aspire DCP executor to indicate
                    // when it's safe to start the containers.

                    await Task.WhenAll(cluster.Servers.Select(async p => {
                        if (_resourceNotificationService.TryGetCurrentState(p.Name, out var currentResourceEvent))
                        {
                            var state = currentResourceEvent.Snapshot.State?.Text;
                            if (KnownResourceStates.TerminalStates.Contains(state) || state == KnownResourceStates.NotStarted)
                            {
                                // Server is stopped, start it

                                await _resourceCommandService.ExecuteCommandAsync(p, KnownResourceCommands.StartCommand, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                    })).ConfigureAwait(false);
                }

                // Wait for the initial node before we consider the init to be running
                resourceLogger.LogInformation("Waiting for resource {ResourceName} to be running...", primaryServer.Name);
                await WaitForServerInitializationAsync(primaryServer, cancellationToken).ConfigureAwait(false);

                var api = _apiService.GetApi(cluster);

                await _resourceNotificationService.PublishUpdateAsync(cluster, s => s with
                {
                    State = new ResourceStateSnapshot("Initializing", KnownResourceStateStyles.Info)
                }).ConfigureAwait(false);

                // Initialize the cluster on the primary node
                await InitializeClusterAsync(api, primaryServer, cancellationToken).ConfigureAwait(false);

                // Get existing cluster nodes
                var pool = await api.GetClusterNodesAsync(primaryServer, cancellationToken).ConfigureAwait(false);
                var existingNodes = pool.Nodes.Select(p => p.Hostname).ToList();

                // Initialize additional nodes in parallel
                List<Task<bool>> additionalNodeTasks = [];
                foreach (var server in cluster.Servers)
                {
                    if (server != primaryServer)
                    {
                        additionalNodeTasks.Add(AddNodeAsync(api, primaryServer, server, existingNodes, cancellationToken));
                    }
                }

                // If there are additional nodes, perform a rebalance to activate them
                if (additionalNodeTasks.Count > 0)
                {
                    await Task.WhenAll(additionalNodeTasks);

                    if (additionalNodeTasks.Any(p => p.Result == true))
                    {
                        await _resourceNotificationService.PublishUpdateAsync(cluster, s => s with
                        {
                            State = new ResourceStateSnapshot("Rebalancing", KnownResourceStateStyles.Info)
                        }).ConfigureAwait(false);

                        // Nodes were added
                        await RebalanceAsync(api, primaryServer, cancellationToken).ConfigureAwait(false);
                    }
                }

                await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStartedEvent(cluster), cancellationToken).ConfigureAwait(false);

                resourceLogger.LogInformation("Initialized cluster '{ClusterName}'.", cluster.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                resourceLogger.LogError(ex, "Failed to initialize Couchbase cluster '{ClusterName}'.", cluster.Name);

                // Indicate the cluster failed to start
                await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStoppedEvent(cluster) { ExitCode = 1 }, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private Task StopClusterAsync(CouchbaseClusterResource cluster, ILogger resourceLogger, CancellationToken cancellationToken)
    {
        _ =  Task.Run(async () =>
        {
            try
            {
                // Stop all servers in the cluster
                await Task.WhenAll(cluster.Servers.Select(async p =>
                {
                    await _resourceCommandService.ExecuteCommandAsync(p, KnownResourceCommands.StopCommand, cancellationToken)
                        .ConfigureAwait(false);

                    await _resourceNotificationService.WaitForResourceAsync(p.Name, KnownResourceStates.TerminalStates, cancellationToken)
                        .ConfigureAwait(false);
                })).ConfigureAwait(false);

                await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStoppedEvent(cluster), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ignore
                return;
            }
            catch (Exception ex)
            {
                resourceLogger.LogWarning(ex, "Error stopping Couchbase cluster '{ClusterName}'.", cluster.Name);
                return;
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public async Task InitializeClusterAsync(ICouchbaseApi api, CouchbaseServerResource primaryServer, CancellationToken cancellationToken = default)
    {
        var poolExists = await api.GetDefaultPoolAsync(primaryServer, preferInsecure: true, cancellationToken);
        if (poolExists)
        {
            // Cluster is already initialized
            return;
        }

        var resourceLogger = _resourceLoggerService.GetLogger(primaryServer.Cluster);
        resourceLogger.LogInformation("Initializing cluster '{ClusterName}' on node '{NodeName}'...", primaryServer.Cluster.Name, primaryServer.Name);

        var settings = await primaryServer.Cluster.GetClusterSettingsAsync(_executionContext, cancellationToken).ConfigureAwait(false);

        await api.InitializeClusterAsync(primaryServer, settings, cancellationToken).ConfigureAwait(false);
    }

    /// <returns><c>true</c> if the node was added, <c>false</c> if it is already part of the cluster.</returns>
    private async Task<bool> AddNodeAsync(ICouchbaseApi api, CouchbaseServerResource primaryServer, CouchbaseServerResource addServer,
        List<string> existingNodes, CancellationToken cancellationToken)
    {
        var resourceLogger = _resourceLoggerService.GetLogger(primaryServer.Cluster);
        resourceLogger.LogInformation("Waiting for resource {ResourceName} to be running...", addServer.Name);

        await WaitForServerInitializationAsync(addServer, cancellationToken).ConfigureAwait(false);

        var added = false;
        if (!existingNodes.Contains($"{addServer.NodeName}:8091"))
        {
            resourceLogger.LogInformation("Adding node {NodeName} to cluster '{ClusterName}'...", addServer.Name, primaryServer.Cluster.Name);

            await api.AddNodeAsync(primaryServer, addServer.NodeName, addServer.Services, cancellationToken).ConfigureAwait(false);

            added = true;
        }

        return added;
    }

    private async Task RebalanceAsync(ICouchbaseApi api, CouchbaseServerResource server, CancellationToken cancellationToken)
    {
        var resourceLogger = _resourceLoggerService.GetLogger(server.Cluster);
        resourceLogger.LogInformation("Rebalancing cluster '{ClusterName}'...", server.Cluster.Name);

        var knownNodes = server.Cluster.Servers.Select(p => p.NodeName).ToList();

        await api.RebalanceAsync(server, knownNodes, cancellationToken).ConfigureAwait(false);

        // Wait for the rebalance to complete
        RebalanceStatus? status;
        do
        {
            status = await api.GetRebalanceProgressAsync(server, cancellationToken).ConfigureAwait(false);
            if (status.Status == RebalanceStatus.StatusNone)
            {
                break;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        } while (true);

        resourceLogger.LogInformation("Rebalance complete for cluster '{ClusterName}'.", server.Cluster.Name);
    }

    private Task CreateBucketAsync(CouchbaseBucketBaseResource bucket, ILogger resourceLogger, bool isExplicitStart, CancellationToken cancellationToken)
    {
        // Run the intialization process as a background task
        _ = Task.Run(async () =>
        {
            try
            {
                using var createBucketCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Observe the bucket state in the background, stopping the creation if it enters a terminal state
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var state = await _resourceNotificationService.WaitForResourceAsync(bucket.Name,
                            KnownResourceStates.TerminalStates.Concat([KnownResourceStates.Stopping, KnownResourceStates.Running]),
                            cancellationToken).ConfigureAwait(false);

                        if (state != KnownResourceStates.Running)
                        {
                            // Bucket entered a terminal state before creation could complete, cancel the creation
                            createBucketCancellation.Cancel();
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Ignore
                    }
                    catch (Exception ex)
                    {
                        resourceLogger.LogError(ex, "Error observing Couchbase bucket '{BucketName}' state.", bucket.BucketName);
                        createBucketCancellation.Cancel();
                    }
                }, cancellationToken);

                if (bucket is CouchbaseSampleBucketResource sampleBucket)
                {
                    await InitializeSampleBucketAsync(sampleBucket, resourceLogger, createBucketCancellation.Token).ConfigureAwait(false);
                }
                else if (bucket is CouchbaseBucketResource standardBucket)
                {
                    await InitializeBucketAsync(standardBucket, resourceLogger, createBucketCancellation.Token).ConfigureAwait(false);
                }

                await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStartedEvent(bucket), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Couchbase bucket '{BucketName}'.", bucket.BucketName);

                await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStoppedEvent(bucket) { ExitCode = 1 }, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task StopBucketAsync(CouchbaseBucketBaseResource bucket, ILogger resourceLogger, CancellationToken cancellationToken)
    {
        // The bucket is effectively stopped by the cluster stopping, so simply mark the bucket resource as stopped.
        await _orchestratorEvents.PublishAsync(new OnCouchbaseResourceStoppedEvent(bucket), cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeBucketAsync(CouchbaseBucketResource bucket, ILogger resourceLogger, CancellationToken cancellationToken = default)
    {
        var node = bucket.Parent.GetPrimaryServer();
        if (node is null)
        {
            throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
        }

        resourceLogger.LogInformation("Creating bucket '{BucketName}'...", bucket.BucketName);

        var api = _apiService.GetApi(bucket.Parent);

        var bucketInfo = await api.GetBucketAsync(node, bucket.BucketName, cancellationToken).ConfigureAwait(false);
        if (bucketInfo is not null)
        {
            resourceLogger.LogInformation("Bucket '{BucketName}' already exists.", bucket.BucketName);
        }
        else
        {
            var settings = await bucket.GetBucketSettingsAsync(_executionContext, cancellationToken).ConfigureAwait(false);
            await api.CreateBucketAsync(node, bucket.BucketName, settings, cancellationToken).ConfigureAwait(false);
        }

        // Wait for bucket to be healthy
        resourceLogger.LogInformation("Waiting for bucket '{BucketName}' to be healthy...", bucket.BucketName);
        while (!cancellationToken.IsCancellationRequested)
        {
            bucketInfo = await api.GetBucketAsync(node, bucket.BucketName, cancellationToken).ConfigureAwait(false);
            if (bucketInfo?.Nodes?.All(p => p.Status == BucketNode.HealthyStatus) ?? false)
            {
                break;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        resourceLogger.LogInformation("Created bucket '{BucketName}'.", bucket.BucketName);
    }

    private async Task InitializeSampleBucketAsync(CouchbaseSampleBucketResource bucket, ILogger resourceLogger, CancellationToken cancellationToken = default)
    {
        var server = bucket.Parent.GetPrimaryServer();
        if (server is null)
        {
            throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
        }

        resourceLogger.LogInformation("Creating sample bucket '{BucketName}'...", bucket.BucketName);

        var api = _apiService.GetApi(bucket.Parent);

        var bucketInfo = await api.GetBucketAsync(server, bucket.BucketName, cancellationToken).ConfigureAwait(false);
        if (bucketInfo is not null)
        {
            resourceLogger.LogInformation("Bucket '{BucketName}' already exists.", bucket.BucketName);
        }
        else
        {
            var response = await api.CreateSampleBucketAsync(server, bucket.BucketName, cancellationToken).ConfigureAwait(false);
            var taskId = response.Tasks.FirstOrDefault()?.TaskId;

            if (!string.IsNullOrEmpty(taskId))
            {
                await _resourceNotificationService.PublishUpdateAsync(bucket, s => s with
                {
                    State = new ResourceStateSnapshot("Loading", KnownResourceStateStyles.Info),
                });

                // Wait for the sample bucket creation task to complete. The bucket health checks will
                // pass before the documents are fully loaded in the bucket.
                while (!cancellationToken.IsCancellationRequested)
                {
                    var tasks = await api.GetClusterTasksAsync(server, cancellationToken).ConfigureAwait(false);

                    var sampleBucketTask = tasks.FirstOrDefault(p => p.TaskId == taskId);
                    if (sampleBucketTask is null)
                    {
                        // When the task is complete, it's simply no longer listed
                        break;
                    }

                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        resourceLogger.LogInformation("Created sample bucket '{BucketName}'.", bucket.BucketName);
    }

    /// <summary>
    /// Applies general server initialization which applies to all servers and which may be applied
    /// before the server is added to the cluster. This is done during the ResourceReadyEvent for each server
    /// so it can run in parallel across multiple servers.
    /// </summary>
    private async Task InitializeServerAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default)
    {
        var resourceLogger = _resourceLoggerService.GetLogger(server.Cluster);
        var api = _apiService.GetApi(server.Cluster);

        if (server.Cluster.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>())
        {
            resourceLogger.LogInformation("Loading node {NodeName} certificates...", server.Name);

            await api.LoadTrustedCAsAsync(server, cancellationToken).ConfigureAwait(false);
            await api.ReloadCertificateAsync(server, cancellationToken).ConfigureAwait(false);
        }

        resourceLogger.LogInformation("Setting node {NodeName} alternate addresses...", server.Name);

        var hostname = await server.Host.GetValueAsync(cancellationToken).ConfigureAwait(false);

        if (!server.TryGetEndpoints(out var endpoints))
        {
            throw new InvalidOperationException("Failed to get node endpoints.");
        }

        var ports = new Dictionary<string, string>();
        foreach (var endpoint in endpoints)
        {
            if (CouchbaseEndpointNames.EndpointNameServiceMappings.TryGetValue(endpoint.Name, out var serviceName))
            {
                var port = await new EndpointReference(server, endpoint).Property(EndpointProperty.Port)
                    .GetValueAsync(cancellationToken).ConfigureAwait(false);
                if (port is not null)
                {
                    ports.Add(serviceName, port);
                }
            }
        }

        await api.SetupAlternateAddressesAsync(server, hostname!, ports, cancellationToken).ConfigureAwait(false);

        // Mark the server as initialized
        await SetServerInitializedPropertyAsync(server, true).ConfigureAwait(false);
    }

    private async Task SetServerInitializedPropertyAsync(CouchbaseServerResource server, bool initialized)
    {
        await _resourceNotificationService.PublishUpdateAsync(server, s => s with
        {
            Properties = [
                ..s.Properties.Where(p => p.Name != CouchbaseServerInitializedPropertyName),
                new(CouchbaseServerInitializedPropertyName, initialized)
            ]
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Wait for a server to enter the running state and be initialized.
    /// </summary>
    private async Task WaitForServerInitializationAsync(CouchbaseServerResource serverResource, CancellationToken cancellationToken)
    {
        // WatchAsync will immediately emit the current state
        await foreach (var resourceEvent in _resourceNotificationService.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(resourceEvent.Resource.Name, serverResource.Name, StringComparison.OrdinalIgnoreCase) &&
                resourceEvent.Snapshot.State == KnownResourceStates.Running)
            {
                var initializedProperty = resourceEvent.Snapshot.Properties
                    .FirstOrDefault(p => p.Name == CouchbaseServerInitializedPropertyName);
                if (initializedProperty?.Value is true)
                {
                    // Server is initialized
                    return;
                }
            }
        }

        throw new OperationCanceledException("The server was not initialized.");
    }

    private async Task PublishUpdateToHierarchyAsync(ICouchbaseCustomResource resource, Func<ICouchbaseCustomResource, CustomResourceSnapshot, CustomResourceSnapshot> updateFunc)
    {
        await _resourceNotificationService.PublishUpdateAsync(resource, s => updateFunc(resource, s)).ConfigureAwait(false);

        var childResources = _model.Resources
            .OfType<IResourceWithParent>()
            .Where(p => p.Parent == resource)
            .OfType<ICouchbaseCustomResource>();

        foreach (var childResource in childResources)
        {
            await PublishUpdateToHierarchyAsync(childResource, updateFunc).ConfigureAwait(false);
        }
    }
}
