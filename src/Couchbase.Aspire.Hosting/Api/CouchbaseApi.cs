using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Couchbase.KeyValue;
using Polly;
using Polly.Timeout;

namespace Couchbase.Aspire.Hosting.Api;

internal sealed class CouchbaseApi(CouchbaseClusterResource cluster, HttpClient httpClient) : ICouchbaseApi
{
    private const int DefaultBucketMemoryQuotaMegabytes = 100;

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

    public async Task<bool> GetDefaultPoolAsync(CouchbaseServerResource server, bool preferInsecure = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var response = await SendRequestAsync(server.GetManagementEndpoint(preferInsecure),
            HttpMethod.Get,
            "/pools/default",
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            // Cluster is already initialized
            return true;
        }
        else if (response.StatusCode != HttpStatusCode.NotFound)
        {
            // Not found indicates the cluster requires initialization, anything else is a true error
            await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public async Task InitializeClusterAsync(CouchbaseServerResource server, CouchbaseClusterSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(settings);

        var quotas = settings.MemoryQuotas ?? new();

        var parameters = new Dictionary<string, string?>()
        {
            { "username", await cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "password", await cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "clusterName", await cluster.ClusterNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "hostname", server.NodeName },
            { "memoryQuota", quotas.DataServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "queryMemoryQuota", quotas.QueryServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "indexMemoryQuota", quotas.IndexServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "ftsMemoryQuota", quotas.FtsServiceMegabytes.ToString(CultureInfo.InvariantCulture) },
            { "indexerStorageMode", GetEnumValueString(cluster.GetIndexStorageMode()) },
            { "services", BuildServicesString(server.GetCouchbaseServices()) },
            { "port", "SAME" },
        };

        if (cluster.GetCouchbaseEdition() == CouchbaseEdition.Enterprise)
        {
            // These parameters are only supported on Enterprise edition
            parameters.Add("cbasMemoryQuota", quotas.AnalyticsServiceMegabytes.ToString(CultureInfo.InvariantCulture));
            parameters.Add("eventingMemoryQuota", quotas.EventingServiceMegabytes.ToString(CultureInfo.InvariantCulture));
            parameters.Add("nodeEncryption", "on");
        }

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            "/clusterInit",
            new FormUrlEncodedContent(parameters),
            authenticated: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Pool> GetClusterNodesAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Get,
            "/pools/nodes",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        var pool = await response.Content.ReadFromJsonAsync<Pool>(cancellationToken).ConfigureAwait(false);

        return pool!;
    }

    public async Task AddNodeAsync(CouchbaseServerResource server, string hostname, CouchbaseServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNullOrEmpty(hostname);

        var parameters = new Dictionary<string, string?>
        {
            { "user", await cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "password", await cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false) },
            { "hostname", hostname },
            { "services", BuildServicesString(services) },
        };

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            "/controller/addNode",
            new FormUrlEncodedContent(parameters),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NodeServices> GetNodeServicesAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Get,
            "pools/default/nodeServices",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        var nodeServicesResponse = await response.Content.ReadFromJsonAsync<NodeServicesResponse>(cancellationToken).ConfigureAwait(false);
        return nodeServicesResponse!.NodesExt.First(p => p.ThisNode);
    }

    public async Task SetupAlternateAddressesAsync(CouchbaseServerResource server, string hostname, Dictionary<string, string> ports,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(hostname);
        ArgumentNullException.ThrowIfNull(ports);

        var parameters = new Dictionary<string, string?>()
        {
            { "hostname", hostname },
        };

        foreach (var (serviceName, port) in ports)
        {
            parameters.Add(serviceName, port);
        }

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Put,
            "/node/controller/setupAlternateAddresses/external",
            new FormUrlEncodedContent(parameters),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task LoadTrustedCAsAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        // Don't use the secure endpoint until we load certificates we trust
        var response = await SendRequestAsync(server.GetManagementEndpoint(preferInsecure: true),
            HttpMethod.Post,
            "/node/controller/loadTrustedCAs",
            authenticated: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadCertificateAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        // Don't use the secure endpoint until we load certificates we trust
        var response = await SendRequestAsync(server.GetManagementEndpoint(preferInsecure: true),
            HttpMethod.Post,
            "/node/controller/reloadCertificate",
            authenticated: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task RebalanceAsync(CouchbaseServerResource server, List<string> knownNodes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(knownNodes);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            "/controller/rebalance",
            new FormUrlEncodedContent(
            [
                new("knownNodes", string.Join(',', knownNodes.Select(p => $"ns_1@{p}"))),
            ]),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RebalanceStatus> GetRebalanceProgressAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Get,
            "/pools/default/rebalanceProgress",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<RebalanceStatus>(cancellationToken).ConfigureAwait(false);

        return result!;
    }

    public async Task<Bucket?> GetBucketAsync(CouchbaseServerResource server, string bucketName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(bucketName);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Get,
            $"/pools/default/buckets/{bucketName}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            // Bucket exists
            return await response.Content.ReadFromJsonAsync<Bucket>(cancellationToken).ConfigureAwait(false);
        }
        else if (response.StatusCode != HttpStatusCode.NotFound)
        {
            // Not found indicates the cluster requires initialization, anything else is a true error
            await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task CreateBucketAsync(CouchbaseServerResource server, string bucketName, CouchbaseBucketSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(bucketName);
        ArgumentNullException.ThrowIfNull(settings);

        var parameters = new Dictionary<string, string?>
        {
            { "name", bucketName },
            { "bucketType", GetEnumValueString(settings.BucketType) },
            { "ramQuota", (settings.MemoryQuotaMegabytes ?? DefaultBucketMemoryQuotaMegabytes).ToString(CultureInfo.InvariantCulture) }
        };

        if (settings.Replicas is int replicas)
        {
            parameters.Add("replicaNumber", replicas.ToString(CultureInfo.InvariantCulture));
        }
        if (settings.FlushEnabled is bool flushEnabled)
        {
            parameters.Add("flushEnabled", flushEnabled ? "1" : "0");
        }
        if (settings.StorageBackend is Management.Buckets.StorageBackend storageBackend)
        {
            parameters.Add("storageBackend", GetEnumValueString(storageBackend));
        }
        if (settings.CompressionMode is Management.Buckets.CompressionMode compressionMode)
        {
            parameters.Add("compressionMode", GetEnumValueString(compressionMode));
        }
        if (settings.ConflictResolutionType is Management.Buckets.ConflictResolutionType conflictResolutionType)
        {
            parameters.Add("conflictResolutionType", GetEnumValueString(conflictResolutionType));
        }
        if (settings.MinimumDurabilityLevel is DurabilityLevel durabilityLevel)
        {
            parameters.Add("durabilityMinLevel", GetEnumValueString(durabilityLevel));
        }
        if (settings.EvictionPolicy is Management.Buckets.EvictionPolicyType evictionPolicy)
        {
            parameters.Add("evictionPolicy", GetEnumValueString(evictionPolicy));
        }
        if (settings.MaximumTimeToLiveSeconds is int maxTtl)
        {
            parameters.Add("maxTTL", maxTtl.ToString(CultureInfo.InvariantCulture));
        }

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            "/pools/default/buckets",
            new FormUrlEncodedContent(parameters),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SampleBucketResponse> CreateSampleBucketAsync(CouchbaseServerResource server, string bucketName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(bucketName);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            "/sampleBuckets/install",
            JsonContent.Create<List<string>>([bucketName]),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync<SampleBucketResponse>(cancellationToken).ConfigureAwait(false))!;
    }

    public async Task FlushBucketAsync(CouchbaseServerResource server, string bucketName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(bucketName);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            $"/pools/default/buckets/{bucketName}/controller/doFlush",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScopesResponse> GetScopesAsync(CouchbaseServerResource server, string bucketName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(bucketName);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Get,
            $"/pools/default/buckets/{bucketName}/scopes",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync<ScopesResponse>(cancellationToken).ConfigureAwait(false))!;
    }

    public async Task CreateScopeAsync(CouchbaseServerResource server, string bucketName, string scopeName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(bucketName);
        ArgumentException.ThrowIfNullOrEmpty(scopeName);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            $"/pools/default/buckets/{bucketName}/scopes",
            content: new FormUrlEncodedContent([new("name", scopeName)]),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateCollectionAsync(CouchbaseServerResource server, string bucketName, string scopeName, string collectionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentException.ThrowIfNullOrEmpty(bucketName);
        ArgumentException.ThrowIfNullOrEmpty(scopeName);
        ArgumentException.ThrowIfNullOrEmpty(collectionName);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Post,
            $"/pools/default/buckets/{bucketName}/scopes/{scopeName}/collections",
            content: new FormUrlEncodedContent([new("name", collectionName)]),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<ClusterTask>> GetClusterTasksAsync(CouchbaseServerResource server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        var response = await SendRequestAsync(server.GetManagementEndpoint(),
            HttpMethod.Get,
            $"/pools/default/tasks",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        return (await response.Content.ReadFromJsonAsync<List<ClusterTask>>(cancellationToken).ConfigureAwait(false)) ?? [];
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
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

    private async ValueTask<AuthenticationHeaderValue> GetAuthenticationHeaderAsync(CancellationToken cancellationToken = default)
    {
        if (_authenticationHeader is not AuthenticationHeaderValue header)
        {
            var username = await cluster.UserNameReference.GetValueAsync(cancellationToken).ConfigureAwait(false);
            var password = await cluster.PasswordParameter.GetValueAsync(cancellationToken).ConfigureAwait(false);

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            header = new AuthenticationHeaderValue("Basic", credentials);

            _authenticationHeader = header;
        }

        return header;
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

    private static async ValueTask ThrowOnFailureAsync(HttpResponseMessage message, CancellationToken cancellationToken = default)
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
        AppendIfSet(CouchbaseServices.Search, "fts");
        AppendIfSet(CouchbaseServices.Analytics, "cbas");
        AppendIfSet(CouchbaseServices.Eventing, "eventing");
        AppendIfSet(CouchbaseServices.Backup, "backup");

        return servicesString.ToString();
    }

    private static string GetEnumValueString<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var valueString = Enum.GetName(value);
        if (valueString is not null)
        {
            var fieldInfo = typeof(TEnum).GetField(valueString, BindingFlags.Public | BindingFlags.Static);
            if (fieldInfo is not null)
            {
                var attribute = fieldInfo.GetCustomAttribute<DescriptionAttribute>();
                if (!string.IsNullOrEmpty(attribute?.Description))
                {
                    return attribute.Description;
                }
            }
        }

        return (valueString ?? value.ToString()).ToLowerInvariant();
    }
}
