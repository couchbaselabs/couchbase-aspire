using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

public class CouchbaseHealthCheck : IHealthCheck
{
    // When running the tests locally during development, don't re-attempt
    // as it prolongs the time it takes to run the tests.
    private const int MAX_CONNECTION_ATTEMPTS
#if DEBUG
        = 1;
#else
        = 2;
#endif

    private readonly Func<CancellationToken, ValueTask<ICluster>> _clusterFactory;
    private readonly bool _activePing;
    private readonly ServiceType[] _serviceTypes;
    private readonly HashSet<ServiceType> _serviceTypesSet;
    private readonly string? _bucketName;

    private ICluster? _cluster;

    public CouchbaseHealthCheck(
        Func<CancellationToken, ValueTask<ICluster>> clusterFactory,
        bool activePing = true,
        ServiceType[]? serviceTypes = null,
        string? bucketName = null)
    {
        if (clusterFactory is null)
        {
            throw new ArgumentNullException(nameof(clusterFactory));
        }

        _clusterFactory = clusterFactory;
        _activePing = activePing;
        _bucketName = bucketName;

        // Make a clone of the array to ensure immutability, and also build a HashSet for faster lookups
        _serviceTypes = serviceTypes is not null
            ? [..serviceTypes]
            : [ServiceType.KeyValue];
        _serviceTypesSet = [.._serviceTypes];
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            HealthCheckResult lastResult = default;

            for (int attempt = 1; attempt <= MAX_CONNECTION_ATTEMPTS; attempt++)
            {
                try
                {
                    var cluster = _cluster ??= await _clusterFactory(cancellationToken).ConfigureAwait(false);

                    if (_activePing)
                    {
                        lastResult = await PerformPingAsync(context, cluster, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        lastResult = await PerformDiagnosticsAsync(context, cluster).ConfigureAwait(false);
                    }

                    if (lastResult.Status == HealthStatus.Healthy)
                    {
                        return lastResult;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (MAX_CONNECTION_ATTEMPTS == attempt)
                    {
                        throw;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            return lastResult;
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }

    private async Task<HealthCheckResult> PerformPingAsync(HealthCheckContext context, ICluster cluster, CancellationToken cancellationToken)
    {
        var pingOptions = new PingOptions()
            .ServiceTypes(_serviceTypes)
            .CancellationToken(cancellationToken);

        IPingReport pingReport;
        if (_bucketName is not null)
        {
            var bucket = await cluster.BucketAsync(_bucketName).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

            pingReport = await bucket.PingAsync(pingOptions).ConfigureAwait(false);
        }
        else
        {
            pingReport = await cluster.PingAsync(pingOptions).ConfigureAwait(false);
        }

        return ParseReport(context, pingReport.Services);
    }

    private async Task<HealthCheckResult> PerformDiagnosticsAsync(HealthCheckContext context, ICluster cluster)
    {
        var diagnosticReport = await cluster.DiagnosticsAsync().ConfigureAwait(false);

        return ParseReport(context, diagnosticReport.Services);
    }

    private HealthCheckResult ParseReport(HealthCheckContext context, IEnumerable<KeyValuePair<string, IEnumerable<IEndpointDiagnostics>>> services)
    {
        foreach (var serviceReport in services)
        {
            if (TryGetServiceType(serviceReport.Key, out var serviceType) && _serviceTypesSet.Contains(serviceType))
            {
                var result = ValidateServiceEndpoints(context, serviceType, serviceReport.Value);
                if (result.Status != HealthStatus.Healthy)
                {
                    return result;
                }
            }
        }

        return HealthCheckResult.Healthy();
    }

    private static HealthCheckResult ValidateServiceEndpoints(HealthCheckContext context, ServiceType serviceType,
        IEnumerable<IEndpointDiagnostics> endpoints)
    {
        var hasValidEndpoint = false;
        foreach (var endpointGroup in endpoints.GroupBy(GetEndpointAddress))
        {
            var groupHasValidEndpoint = false;
            foreach (var endpoint in endpointGroup)
            {
                if (endpoint.State is ServiceState.Connected or ServiceState.Ok)
                {
                    groupHasValidEndpoint = true;
                    break;
                }
            }

            if (groupHasValidEndpoint)
            {
                hasValidEndpoint = true;
            }
            else if (serviceType == ServiceType.KeyValue)
            {
                // Consider unhealthy if any single node has no connections to K/V
                return new HealthCheckResult(context.Registration.FailureStatus,
                    $"Couchbase health check failed for service {serviceType}.");
            }
        }

        if (!hasValidEndpoint)
        {
            // For all other services, consider unhealthy if no connected endpoints exist
            return new HealthCheckResult(context.Registration.FailureStatus,
                $"Couchbase health check failed for service {serviceType}.");
        }

        return HealthCheckResult.Healthy();
    }

    private static string? GetEndpointAddress(IEndpointDiagnostics endpoint)
    {
        if (endpoint.Remote is null)
        {
            return null;
        }

        var index = endpoint.Remote.LastIndexOf(':');
        return index > 0 ? endpoint.Remote.Substring(0, index) : endpoint.Remote;
    }

    private static bool TryGetServiceType(string serviceName, out ServiceType serviceType)
    {
        switch (serviceName)
        {
            case "kv":
                serviceType = ServiceType.KeyValue;
                return true;
            case "n1ql":
                serviceType = ServiceType.Query;
                return true;
            case "fts":
                serviceType = ServiceType.Search;
                return true;
            case "cbas":
                serviceType = ServiceType.Analytics;
                return true;
            case "views":
                serviceType = ServiceType.Views;
                return true;
            default:
                serviceType = default;
                return false;
        }
    }
}
