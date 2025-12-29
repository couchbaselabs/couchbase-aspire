using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace Couchbase.Aspire.Hosting.Initialization;

internal sealed class CouchbaseClusterInitializer(
    CouchbaseClusterResource cluster,
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

    public static readonly ResiliencePipeline<HttpResponseMessage> RetryPolicy = new ResiliencePipelineBuilder<HttpResponseMessage>()
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var exitCode = 0;

        try
        {
            var initialNode = cluster.Servers.FirstOrDefault(CouchbaseResourceExtensions.IsInitialNode);
            if (initialNode is null)
            {
                throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
            }

            // Wait for the initial node before we consider the init to be running
            await resourceNotificationService.WaitForResourceAsync(initialNode.Name, KnownResourceStates.Running, cancellationToken).ConfigureAwait(false);

            // Mark the init as running
            await resourceNotificationService.PublishUpdateAsync(initializer, s => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Running,
            });

            // Mark the cluster as starting
            await resourceNotificationService.PublishUpdateAsync(cluster, s => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Starting,
            });

            // Begin initializing the primary cluster
            var authenticationHeader = await BuildAuthenticationHeaderAsync(cluster, cancellationToken).ConfigureAwait(false);
            await InitializeClusterAsync(initialNode, cancellationToken).ConfigureAwait(false);
            await SetNodeAlternateAddresses(initialNode, authenticationHeader, cancellationToken).ConfigureAwait(false);

            // Initialize additional nodes in parallel
            List<Task> additionalNodeTasks = [];
            foreach (var node in cluster.Servers)
            {
                if (node != initialNode)
                {
                    additionalNodeTasks.Add(AddNodeAsync(initialNode, node, authenticationHeader, cancellationToken));
                }
            }

            // If there are additional nodes, perform a rebalance to activate them
            if (additionalNodeTasks.Count > 0)
            {
                await Task.WhenAll(additionalNodeTasks);

                await RebalanceAsync(initialNode, authenticationHeader, cancellationToken).ConfigureAwait(false);
            }

            // Mark the cluster as running
            await resourceNotificationService.PublishUpdateAsync(cluster, s => s with
            {
                State = KnownResourceStates.Running,
            });

            logger.LogInformation("Initialized cluster '{ClusterName}'.", cluster.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to initialize Couchbase cluster '{ClusterName}'.", cluster.Name);

            exitCode = 1;

            // Indicate the cluster failed to start
            await resourceNotificationService.PublishUpdateAsync(cluster, s => s with
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
        logger.LogInformation("Initializing cluster '{ClusterName}' on node '{NodeName}'...", cluster.Name, initialNode.Name);

        var secureEndpoint = initialNode.GetEndpoint(CouchbaseEndpointNames.ManagementSecure);

        var response = await RetryPolicy.ExecuteAsync(async ct =>
        {
            var settings = await cluster.GetClusterSettingsAsync(executionContext, ct).ConfigureAwait(false);

            var quotas = settings.MemoryQuotas ?? new();

            var uri = await CreateUriAsync(secureEndpoint, "/clusterInit", ct).ConfigureAwait(false);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new FormUrlEncodedContent(
                [
                    new("username", await cluster.UserNameReference.GetValueAsync(ct).ConfigureAwait(false)),
                    new("password", await cluster.PasswordParameter.GetValueAsync(ct).ConfigureAwait(false)),
                    new("clusterName", await cluster.ClusterNameReference.GetValueAsync(ct).ConfigureAwait(false)),
                    new("hostname", initialNode.NodeName),
                    new("memoryQuota", quotas.DataServiceMegabytes.ToString(CultureInfo.InvariantCulture)),
                    new("queryMemoryQuota", quotas.QueryServiceMegabytes.ToString(CultureInfo.InvariantCulture)),
                    new("indexMemoryQuota", quotas.IndexServiceMegabytes.ToString(CultureInfo.InvariantCulture)),
                    new("ftsMemoryQuota", quotas.FtsServiceMegabytes.ToString(CultureInfo.InvariantCulture)),
                    new("eventingMemoryQuota", quotas.EventingServiceMegabytes.ToString(CultureInfo.InvariantCulture)),
                    new("cbasMemoryQuota", quotas.AnalyticsServiceMegabytes.ToString(CultureInfo.InvariantCulture)),
                    new("services", BuildServicesString(initialNode.Services)),
                    new("nodeEncryption", "on"),
                    new("port", "SAME"),
                ])
            };

            return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    public async Task AddNodeAsync(CouchbaseServerResource initialNode, CouchbaseServerResource addNode, AuthenticationHeaderValue authenticationHeader,
        CancellationToken cancellationToken = default)
    {
        await resourceNotificationService.WaitForResourceAsync(addNode.Name, KnownResourceStates.Running, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Adding node {NodeName} to cluster '{ClusterName}'...", addNode.Name, cluster.Name);

        var secureEndpoint = initialNode.GetEndpoint(CouchbaseEndpointNames.ManagementSecure);

        var response = await RetryPolicy.ExecuteAsync(async ct =>
        {
            var uri = await CreateUriAsync(secureEndpoint, "/controller/addNode", ct).ConfigureAwait(false);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers =
            {
                Authorization = authenticationHeader
            },
                Content = new FormUrlEncodedContent(
                [
                    new("user", await cluster.UserNameReference.GetValueAsync(ct).ConfigureAwait(false)),
                new("password", await cluster.PasswordParameter.GetValueAsync(ct).ConfigureAwait(false)),
                new("hostname", addNode.NodeName),
                new("services", BuildServicesString(addNode.Services)),
            ])
            };

            return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }

        await SetNodeAlternateAddresses(addNode, authenticationHeader, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetNodeAlternateAddresses(CouchbaseServerResource node, AuthenticationHeaderValue authenticationHeader, CancellationToken cancellationToken)
    {
        logger.LogInformation("Setting node {NodeName} alternate addresses...", node.Name);

        var secureEndpoint = node.GetEndpoint(CouchbaseEndpointNames.ManagementSecure);

        var response = await RetryPolicy.ExecuteAsync(async ct =>
        {
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

            var uri = await CreateUriAsync(secureEndpoint, "/node/controller/setupAlternateAddresses/external", ct).ConfigureAwait(false);
            var request = new HttpRequestMessage(HttpMethod.Put, uri)
            {
                Headers =
                {
                    Authorization = authenticationHeader
                },
                Content = new FormUrlEncodedContent(dictionary)
            };

            return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
    }

    public async Task RebalanceAsync(CouchbaseServerResource initialNode, AuthenticationHeaderValue authenticationHeader,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Rebalancing cluster '{ClusterName}'...", cluster.Name);

        var secureEndpoint = initialNode.GetEndpoint(CouchbaseEndpointNames.ManagementSecure);

        var response = await RetryPolicy.ExecuteAsync(async ct =>
        {
            var uri = await CreateUriAsync(secureEndpoint, "/controller/rebalance", ct).ConfigureAwait(false);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers =
                {
                    Authorization = authenticationHeader
                },
                Content = new FormUrlEncodedContent(
                [
                    new("knownNodes", string.Join(',', cluster.Servers.Select(p => $"ns_1@{p.NodeName}"))),
                ])
            };

            return await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Wait for the rebalance to complete
        RebalanceStatus? status;
        var uri = await CreateUriAsync(secureEndpoint, "/pools/default/rebalanceProgress", cancellationToken).ConfigureAwait(false);
        do
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers =
                {
                    Authorization = authenticationHeader
                }
            };

            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            status = await response.Content.ReadFromJsonAsync<RebalanceStatus>(cancellationToken).ConfigureAwait(false);
            if (status?.Status == "none")
            {
                break;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        } while (true);

        logger.LogInformation("Rebalance complete for cluster '{ClusterName}'.", cluster.Name);
    }

    public static async Task<Uri> CreateUriAsync(EndpointReference endpoint, string path, CancellationToken cancellationToken = default)
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

    public static async ValueTask<AuthenticationHeaderValue> BuildAuthenticationHeaderAsync(CouchbaseClusterResource cluster, CancellationToken cancellationToken = default)
    {
        var username = await cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false);
        var password = await cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new AuthenticationHeaderValue("Basic", credentials);
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
}
