using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace Couchbase.Aspire.Hosting.Initialization;

internal sealed class CouchbaseClusterInitializer(
    CouchbaseClusterInitializerResource initializer,
    DistributedApplicationExecutionContext executionContext,
    HttpClient httpClient,
    ILogger logger,
    ResourceNotificationService resourceNotificationService)
{
    private static readonly FrozenDictionary<string, string> EndpointNameServiceMappings =
        ((IEnumerable<KeyValuePair<string, string>>)[
            new(CouchbaseEndpointNames.Management, "mgmt"),
            new(CouchbaseEndpointNames.ManagementSecure, "mgmtSSL"),
            new(CouchbaseEndpointNames.Data, "kv"),
            new(CouchbaseEndpointNames.DataSecure, "kvSSL"),
            new(CouchbaseEndpointNames.Views, "capi"),
            new(CouchbaseEndpointNames.ViewsSecure, "capiSSL"),
            new(CouchbaseEndpointNames.Query, "n1ql"),
            new(CouchbaseEndpointNames.QuerySecure, "n1qlSSL"),
            new(CouchbaseEndpointNames.Fts, "fts"),
            new(CouchbaseEndpointNames.FtsSecure, "ftsSSL"),
            new(CouchbaseEndpointNames.Analytics, "cbas"),
            new(CouchbaseEndpointNames.AnalyticsSecure, "cbasSSL"),
            new(CouchbaseEndpointNames.Eventing, "eventingAdminPort"),
            new(CouchbaseEndpointNames.EventingSecure, "eventingSSL"),
            new(CouchbaseEndpointNames.EventingDebug, "eventingDebug"),
            new(CouchbaseEndpointNames.Backup, "backupAPI"),
            new(CouchbaseEndpointNames.BackupSecure, "backupAPIHTTPS"),
        ]).ToFrozenDictionary();

    private static readonly ResiliencePipeline<HttpResponseMessage> RetryPolicy = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRetry(new()
        {
            ShouldHandle = e => ValueTask.FromResult(e.Outcome switch
            {
                { Result.StatusCode: >= HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
                { Exception: { } exception } when exception is HttpRequestException or TimeoutRejectedException => true,
                _ => false,
            }),
            MaxRetryAttempts = 60,
            BackoffType = DelayBackoffType.Constant,
            Delay = TimeSpan.FromSeconds(1),
        })
        .Build();

    private AuthenticationHeaderValue? _authenticationHeader;

    private CouchbaseClusterResource Cluster => initializer.Parent;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var exitCode = 0;

        try
        {
            var initialNode = Cluster.GetPrimaryServer();
            if (initialNode is null)
            {
                throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
            }

            // Wait for the initial node before we consider the init to be running
            logger.LogInformation("Waiting for resource {ResourceName} to be running...", initialNode.Name);
            await resourceNotificationService.WaitForResourceAsync(initialNode.Name, KnownResourceStates.Running, cancellationToken).ConfigureAwait(false);

            // Mark the init as running
            await resourceNotificationService.PublishUpdateAsync(initializer, s => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Running,
            });

            // Mark the cluster as starting
            await resourceNotificationService.PublishUpdateAsync(Cluster, s => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Starting,
            });

            // Initialize the cluster on the primary node
            await InitializeClusterAsync(initialNode, cancellationToken).ConfigureAwait(false);

            // Set primary node alternate addresses
            await SetNodeAlternateAddresses(initialNode, cancellationToken).ConfigureAwait(false);

            // Get existing cluster nodes
            var existingNodes = await GetClusterNodesAsync(initialNode, cancellationToken).ConfigureAwait(false);

            // Initialize additional nodes in parallel
            List<Task<bool>> additionalNodeTasks = [];
            foreach (var node in Cluster.Servers)
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
            await resourceNotificationService.PublishUpdateAsync(Cluster, s => s with
            {
                State = KnownResourceStates.Running,
            });

            logger.LogInformation("Initialized cluster '{ClusterName}'.", Cluster.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to initialize Couchbase cluster '{ClusterName}'.", Cluster.Name);

            exitCode = 1;

            // Indicate the cluster failed to start
            await resourceNotificationService.PublishUpdateAsync(Cluster, s => s with
            {
                State = KnownResourceStates.FailedToStart,
            });
        }
        finally
        {
            // Mark the init resource as exited
            await resourceNotificationService.PublishUpdateAsync(initializer, s => s with
            {
                StopTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Exited,
                ExitCode = exitCode,
            });
        }
    }

    public async Task InitializeClusterAsync(CouchbaseServerResource initialNode, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(initialNode.GetManagementEndpoint(preferInsecure: true),
            HttpMethod.Get,
            "/pools/default",
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            // Cluster is already initialized
            return;
        }
        else if (response.StatusCode != HttpStatusCode.NotFound)
        {
            // Not found indicates the cluster requires initialization, anything else is a true error
            await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        // Load certificates before any other operations
        await LoadNodeCertificatesAsync(initialNode, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Initializing cluster '{ClusterName}' on node '{NodeName}'...", Cluster.Name, initialNode.Name);

        var settings = await Cluster.GetClusterSettingsAsync(executionContext, cancellationToken).ConfigureAwait(false);

        var quotas = settings.MemoryQuotas ?? new();

        var dictionary = new Dictionary<string, string?>()
        {
            { "username", await Cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "password", await Cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "clusterName", await Cluster.ClusterNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "hostname", initialNode.NodeName },
            { "memoryQuota", quotas.DataServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "queryMemoryQuota", quotas.QueryServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "indexMemoryQuota", quotas.IndexServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "ftsMemoryQuota", quotas.FtsServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "services", BuildServicesString(initialNode.Services) },
            { "port", "SAME" },
        };

        if (Cluster.GetCouchbaseEdition() == CouchbaseEdition.Enterprise)
        {
            // These parameters are only supported on Enterprise edition
            dictionary.Add("cbasMemoryQuota", quotas.AnalyticsServiceMegabytes.ToString(CultureInfo.InvariantCulture));
            dictionary.Add("eventingMemoryQuota", quotas.EventingServiceMegabytes.ToString(CultureInfo.InvariantCulture));
            dictionary.Add("nodeEncryption", "on");
        }

        response = await SendRequestAsync(initialNode.GetManagementEndpoint(),
            HttpMethod.Post,
            "/clusterInit",
            new FormUrlEncodedContent(dictionary),
            authenticated: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<string>> GetClusterNodesAsync(CouchbaseServerResource initialNode, CancellationToken cancellationToken = default)
    {
        var endpoint = initialNode.GetManagementEndpoint();

        var response = await SendRequestAsync(endpoint,
            HttpMethod.Get,
            "/pools/nodes",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        var pool = await response.Content.ReadFromJsonAsync<Pool>(cancellationToken).ConfigureAwait(false);

        return pool!.Nodes.Select(p => p.Hostname).ToList() ?? [];
    }

    /// <returns><c>true</c> if the node was added, <c>false</c> if it is already part of the cluster.</returns>
    private async Task<bool> AddNodeAsync(CouchbaseServerResource initialNode, CouchbaseServerResource addNode, List<string> existingNodes,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Waiting for resource {ResourceName} to be running...", addNode.Name);
        await resourceNotificationService.WaitForResourceAsync(addNode.Name, KnownResourceStates.Running, cancellationToken).ConfigureAwait(false);

        var added = false;
        if (!existingNodes.Contains($"{addNode.NodeName}:8091"))
        {
            // If the node isn't fully started, the request to add the node may fail, so wait for a 404 from /pools/default
            var response = await SendRequestAsync(addNode.GetManagementEndpoint(preferInsecure: true),
                HttpMethod.Get,
                "/pools/default",
                authenticated: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
            }

            // Load certificates on the node first
            await LoadNodeCertificatesAsync(addNode, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Adding node {NodeName} to cluster '{ClusterName}'...", addNode.Name, Cluster.Name);

            response = await SendRequestAsync(initialNode.GetManagementEndpoint(),
                HttpMethod.Post,
                "/controller/addNode",
                new FormUrlEncodedContent(
                [
                    new("user", await Cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false)),
                    new("password", await Cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false)),
                    new("hostname", addNode.NodeName),
                    new("services", BuildServicesString(addNode.Services)),
                ]),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

            added = true;
        }

        await SetNodeAlternateAddresses(addNode, cancellationToken).ConfigureAwait(false);

        return added;
    }

    public async Task SetNodeAlternateAddresses(CouchbaseServerResource node, CancellationToken cancellationToken)
    {
        logger.LogInformation("Setting node {NodeName} alternate addresses...", node.Name);

        var dictionary = new Dictionary<string, string?>()
        {
            { "hostname", await node.Host.GetValueAsync(cancellationToken).ConfigureAwait(false) },
        };

        if (!node.TryGetEndpoints(out var endpoints))
        {
            throw new InvalidOperationException("Failed to get node endpoints.");
        }

        foreach (var endpoint in endpoints)
        {
            if (EndpointNameServiceMappings.TryGetValue(endpoint.Name, out var serviceName))
            {
                var port = await new EndpointReference(node, endpoint).Property(EndpointProperty.Port)
                    .GetValueAsync(cancellationToken).ConfigureAwait(false);
                dictionary.Add(serviceName, port);
            }
        }

        var response = await SendRequestAsync(node.GetManagementEndpoint(),
            HttpMethod.Put,
            "/node/controller/setupAlternateAddresses/external",
            new FormUrlEncodedContent(dictionary),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task LoadNodeCertificatesAsync(CouchbaseServerResource node, CancellationToken cancellationToken)
    {
        if (!node.Cluster.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>())
        {
            // No certificates to load
            return;
        }

        logger.LogInformation("Loading node {NodeName} certificates...", node.Name);

        // Don't use the secure endpoint until we load certificates we trust
        var endpoint = node.GetManagementEndpoint(preferInsecure: true);

        var response = await SendRequestAsync(endpoint,
            HttpMethod.Post,
            "/node/controller/loadTrustedCAs",
            authenticated: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        response = await SendRequestAsync(endpoint,
            HttpMethod.Post,
            "/node/controller/reloadCertificate",
            authenticated: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task RebalanceAsync(CouchbaseServerResource initialNode, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Rebalancing cluster '{ClusterName}'...", Cluster.Name);

        var endpoint = initialNode.GetManagementEndpoint();

        var response = await SendRequestAsync(endpoint,
            HttpMethod.Post,
            "/controller/rebalance",
            new FormUrlEncodedContent(
            [
                new("knownNodes", string.Join(',', Cluster.Servers.Select(p => $"ns_1@{p.NodeName}"))),
            ]),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        // Wait for the rebalance to complete
        RebalanceStatus? status;
        var uri = await CreateUriAsync(endpoint, "/pools/default/rebalanceProgress", cancellationToken).ConfigureAwait(false);
        do
        {
            response = await SendRequestAsync(endpoint,
                HttpMethod.Get,
                "/pools/default/rebalanceProgress",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

            status = await response.Content.ReadFromJsonAsync<RebalanceStatus>(cancellationToken).ConfigureAwait(false);
            if (status?.Status == "none")
            {
                break;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        } while (true);

        logger.LogInformation("Rebalance complete for cluster '{ClusterName}'.", Cluster.Name);
    }

    internal async Task<HttpResponseMessage> SendRequestAsync(
        EndpointReference endpoint,
        HttpMethod method,
        string path,
        HttpContent? content = null,
        bool authenticated = true,
        bool autoRetry = true,
        CancellationToken cancellationToken = default)
    {
        var uri = await CreateUriAsync(endpoint, path, cancellationToken).ConfigureAwait(false);

        AuthenticationHeaderValue? authenticationHeader = null;
        if (authenticated)
        {
            authenticationHeader = await GetAuthenticationHeaderAsync(cancellationToken).ConfigureAwait(false);
        }

        ValueTask<HttpResponseMessage> SendAsync(CancellationToken ct)
        {
            var request = new HttpRequestMessage(method, uri)
            {
                Headers =
                {
                    Authorization = authenticationHeader
                },
                Content = content
            };

            return new ValueTask<HttpResponseMessage>(httpClient.SendAsync(request, ct));
        }

        if (autoRetry)
        {
            return await RetryPolicy.ExecuteAsync(SendAsync, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return await SendAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async ValueTask ThrowOnFailureAsync(HttpResponseMessage message, CancellationToken cancellationToken = default)
    {
        if (!message.IsSuccessStatusCode)
        {
            // Try to read an error message from the response
            var errorMessage = await message.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                throw new InvalidOperationException($"{message.StatusCode}: {errorMessage}");
            }
            else
            {
                throw new InvalidOperationException($"Request failed with status code {message.StatusCode}.");
            }
        }
    }

    private static async Task<Uri> CreateUriAsync(EndpointReference endpoint, string path, CancellationToken cancellationToken = default)
    {
        var baseUri = await endpoint.GetValueAsync(cancellationToken).ConfigureAwait(false);
        if (baseUri is null)
        {
            throw new InvalidOperationException($"Endpoint '{endpoint.EndpointName}' on resource '{endpoint.Resource.Name}' does not have a valid URI.");
        }

        return new UriBuilder(baseUri)
        {
            Path = path
        }.Uri;
    }

    private async ValueTask<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(CancellationToken cancellationToken = default)
    {
        if (_authenticationHeader is not AuthenticationHeaderValue header)
        {
            var username = await Cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false);
            var password = await Cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false);

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            header = new AuthenticationHeaderValue("Basic", credentials);

            _authenticationHeader = header;
        }

        return header;
    }

    private static string BuildServicesString(CouchbaseServices services)
    {
        var servicesString = new StringBuilder();

        void AppendIfSet(CouchbaseServices testService, string toAppend)
        {
            if (services.HasFlag(testService))
            {
                if (servicesString.Length > 0)
                {
                    servicesString.Append(',');
                }

                servicesString.Append(toAppend);
            }
        }

        AppendIfSet(CouchbaseServices.Data, "kv");
        AppendIfSet(CouchbaseServices.Query, "n1ql");
        AppendIfSet(CouchbaseServices.Index, "index");
        AppendIfSet(CouchbaseServices.Fts, "fts");
        AppendIfSet(CouchbaseServices.Analytics, "cbas");
        AppendIfSet(CouchbaseServices.Eventing, "eventing");
        AppendIfSet(CouchbaseServices.Backup, "backup");

        return servicesString.ToString();
    }

    private sealed class RebalanceStatus
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }
    }

    private sealed class Pool
    {
        [JsonPropertyName("nodes")]
        public List<Node> Nodes { get; set; } = null!;
    }

    private sealed class Node
    {
        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = null!;
    }
}
