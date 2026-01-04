using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Couchbase.Aspire.Hosting.Api;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting.Initialization;

internal sealed class CouchbaseClusterInitializer(
    CouchbaseClusterResource cluster,
    ICouchbaseApi couchbaseApi,
    DistributedApplicationExecutionContext executionContext,
    ResourceLoggerService resourceLoggerService,
    ResourceNotificationService resourceNotificationService,
    IDistributedApplicationEventing eventing)
{
    private readonly ILogger _logger = resourceLoggerService.GetLogger(cluster);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var initialNode = cluster.GetPrimaryServer();
            if (initialNode is null)
            {
                throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
            }

            // Wait for the initial node before we consider the init to be running
            _logger.LogInformation("Waiting for resource {ResourceName} to be running...", initialNode.Name);
            await resourceNotificationService.WaitForResourceAsync(initialNode.Name, KnownResourceStates.Running, cancellationToken).ConfigureAwait(false);

            // Mark the cluster as starting
            await resourceNotificationService.PublishUpdateAsync(cluster, s => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Starting,
            });

            // Initialize the cluster on the primary node
            await InitializeClusterAsync(initialNode, cancellationToken).ConfigureAwait(false);

            // Set primary node alternate addresses
            await SetNodeAlternateAddresses(initialNode, cancellationToken).ConfigureAwait(false);

            // Get existing cluster nodes
            var pool = await couchbaseApi.GetClusterNodesAsync(initialNode, cancellationToken).ConfigureAwait(false);
            var existingNodes = pool.Nodes.Select(p => p.Hostname).ToList();

            // Initialize additional nodes in parallel
            List<Task<bool>> additionalNodeTasks = [];
            foreach (var node in cluster.Servers)
            {
                if (node != initialNode)
                {
                    additionalNodeTasks.Add(AddNodeAsync(initialNode, node, existingNodes, cancellationToken));
                }
            }

            // If there are additional nodes, perform a rebalance to activate them
            if (additionalNodeTasks.Count > 0)
            {
                await Task.WhenAll(additionalNodeTasks);

                if (additionalNodeTasks.Any(p => p.Result == true))
                {
                    // Nodes were added
                    await RebalanceAsync(initialNode, cancellationToken).ConfigureAwait(false);
                }
            }

            // Mark the cluster as running
            await MarkClusterRunningAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Initialized cluster '{ClusterName}'.", cluster.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to initialize Couchbase cluster '{ClusterName}'.", cluster.Name);

            // Indicate the cluster failed to start
            await resourceNotificationService.PublishUpdateAsync(cluster, s => s with
            {
                State = KnownResourceStates.FailedToStart,
                ExitCode = 1
            });
        }
    }

    public async Task InitializeClusterAsync(CouchbaseServerResource initialNode, CancellationToken cancellationToken = default)
    {
        var poolExists = await couchbaseApi.GetDefaultPoolAsync(initialNode, preferInsecure: true, cancellationToken);
        if (poolExists)
        {
            // Cluster is already initialized
            return;
        }

        // Load certificates before any other operations
        await LoadNodeCertificatesAsync(initialNode, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Initializing cluster '{ClusterName}' on node '{NodeName}'...", cluster.Name, initialNode.Name);

        var settings = await cluster.GetClusterSettingsAsync(executionContext, cancellationToken).ConfigureAwait(false);

        await couchbaseApi.InitializeClusterAsync(initialNode, settings, cancellationToken).ConfigureAwait(false);
    }

    /// <returns><c>true</c> if the node was added, <c>false</c> if it is already part of the cluster.</returns>
    private async Task<bool> AddNodeAsync(CouchbaseServerResource initialNode, CouchbaseServerResource addNode, List<string> existingNodes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Waiting for resource {ResourceName} to be running...", addNode.Name);
        await resourceNotificationService.WaitForResourceAsync(addNode.Name, KnownResourceStates.Running, cancellationToken).ConfigureAwait(false);

        var added = false;
        if (!existingNodes.Contains($"{addNode.NodeName}:8091"))
        {
            // If the node isn't fully started, the request to add the node may fail, so wait for a 404 from /pools/default
            await couchbaseApi.GetDefaultPoolAsync(addNode, preferInsecure: true, cancellationToken).ConfigureAwait(false);

            // Load certificates on the node first
            await LoadNodeCertificatesAsync(addNode, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Adding node {NodeName} to cluster '{ClusterName}'...", addNode.Name, cluster.Name);

            await couchbaseApi.AddNodeAsync(initialNode, addNode.NodeName, addNode.Services, cancellationToken).ConfigureAwait(false);

            added = true;
        }

        await SetNodeAlternateAddresses(addNode, cancellationToken).ConfigureAwait(false);

        return added;
    }

    public async Task SetNodeAlternateAddresses(CouchbaseServerResource node, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting node {NodeName} alternate addresses...", node.Name);

        var hostname = await node.Host.GetValueAsync(cancellationToken).ConfigureAwait(false);

        if (!node.TryGetEndpoints(out var endpoints))
        {
            throw new InvalidOperationException("Failed to get node endpoints.");
        }

        var ports = new Dictionary<string, string>();
        foreach (var endpoint in endpoints)
        {
            if (CouchbaseEndpointNames.EndpointNameServiceMappings.TryGetValue(endpoint.Name, out var serviceName))
            {
                var port = await new EndpointReference(node, endpoint).Property(EndpointProperty.Port)
                    .GetValueAsync(cancellationToken).ConfigureAwait(false);
                if (port is not null)
                {
                    ports.Add(serviceName, port);
                }
            }
        }

        await couchbaseApi.SetupAlternateAddressesAsync(node, hostname!, ports, cancellationToken).ConfigureAwait(false);
    }

    public async Task LoadNodeCertificatesAsync(CouchbaseServerResource node, CancellationToken cancellationToken)
    {
        if (!node.Cluster.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>())
        {
            // No certificates to load
            return;
        }

        _logger.LogInformation("Loading node {NodeName} certificates...", node.Name);

        await couchbaseApi.LoadTrustedCAsAsync(node, cancellationToken).ConfigureAwait(false);
        await couchbaseApi.ReloadCertificateAsync(node, cancellationToken).ConfigureAwait(false);
    }

    private async Task RebalanceAsync(CouchbaseServerResource initialNode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rebalancing cluster '{ClusterName}'...", cluster.Name);

        var knownNodes = cluster.Servers.Select(p => p.NodeName).ToList();

        await couchbaseApi.RebalanceAsync(initialNode, knownNodes, cancellationToken).ConfigureAwait(false);

        // Wait for the rebalance to complete
        RebalanceStatus? status;
        do
        {
            status = await couchbaseApi.GetRebalanceProgressAsync(initialNode, cancellationToken).ConfigureAwait(false);
            if (status.Status == RebalanceStatus.StatusNone)
            {
                break;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        } while (true);

        _logger.LogInformation("Rebalance complete for cluster '{ClusterName}'.", cluster.Name);
    }

    private async Task MarkClusterRunningAsync(CancellationToken cancellationToken = default)
    {
        var userName = await cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var password = await cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false);

        await resourceNotificationService.PublishUpdateAsync(cluster, s => s with
        {
            State = KnownResourceStates.Running,
            EnvironmentVariables = [
                // These are useful for logging into the console, the only way we can display them on the dashboard currently is via environment variables
                new("CB_USERNAME", userName, true),
                new("CB_PASSWORD", password, true)
            ]
        });

        // Since this is a custom resource, we must publish these events manually to trigger URLs, connection strings,
        // and health checks.
        await eventing.PublishAsync(new ResourceEndpointsAllocatedEvent(cluster, executionContext.ServiceProvider), cancellationToken)
            .ConfigureAwait(false);
        await eventing.PublishAsync(new ConnectionStringAvailableEvent(cluster, executionContext.ServiceProvider), cancellationToken)
            .ConfigureAwait(false);
    }
}
