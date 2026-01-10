using System.Text;
using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

/// <summary>
/// Base type for a Couchbase health check.
/// </summary>
public abstract class CouchbaseHealthCheck : IHealthCheck
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
    private ICluster? _cluster;

    /// <summary>
    /// List of services to check.
    /// </summary>
    protected ServiceType[] ServiceTypes { get; }

    /// <summary>
    /// The minimum number of healthy nodes per service required to report healthy.
    /// </summary>
    /// <value>
    /// Defaults to <c>1</c> for all services.
    /// </value>
    public Dictionary<ServiceType, int> MinimumHealthyNodesByService { get; set; } = [];

    /// <summary>
    /// The maximum number of unhealthy nodes per service allowed to report healthy.
    /// </summary>
    /// <value>
    /// Defaults to <c>0</c> for <see cref="ServiceType.KeyValue"/> and unlimited for other services.
    /// </value>
    public Dictionary<ServiceType, int> MaximumUnhealthyNodesByService { get; set; } = [];

    /// <summary>
    /// Creates a new CouchbaseHealthCheck instance.
    /// </summary>
    /// <param name="clusterFactory">Factory to obtain the <see cref="ICluster" /> instance.
    /// <param name="serviceTypes"/>List of services to check. If <c>null</c>, defaults to <see cref="ServiceType.KeyValue"/>.</param>
    protected CouchbaseHealthCheck(
        Func<CancellationToken, ValueTask<ICluster>> clusterFactory,
        ServiceType[]? serviceTypes = null)
    {
        if (clusterFactory is null)
        {
            throw new ArgumentNullException(nameof(clusterFactory));
        }

        _clusterFactory = clusterFactory;

        // Make a clone of the array to ensure immutability
        ServiceTypes = serviceTypes is not null
            ? [..serviceTypes]
            : [ServiceType.KeyValue];

        foreach (var service in ServiceTypes)
        {
            MinimumHealthyNodesByService[service] = 1;
            MaximumUnhealthyNodesByService[service] = service == ServiceType.KeyValue ? 0 : int.MaxValue;
        }
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

                    lastResult = await PerformCheckAsync(context, cluster, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Performs the actual health check logic given a connected Couchbase cluster.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cluster">The connected Couchbase cluster.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The health check result.</returns>
    protected abstract Task<HealthCheckResult> PerformCheckAsync(HealthCheckContext context, ICluster cluster, CancellationToken cancellationToken);

    // Default implementation for parsing reports
    private protected HealthCheckResult ParseReport(HealthCheckContext context, IDictionary<string, IEnumerable<IEndpointDiagnostics>> services)
    {
        foreach (var serviceType in ServiceTypes)
        {
            var serviceName = GetServiceName(serviceType);
            if (serviceName is not null)
            {
                if (!services.TryGetValue(serviceName, out var endpoints))
                {
                    // No endpoints for this service
                    endpoints = [];
                }

                var result = ValidateServiceEndpoints(context, serviceType, endpoints);
                if (result.Status != HealthStatus.Healthy)
                {
                    return result;
                }
            }
        }

        return HealthCheckResult.Healthy();
    }

    private HealthCheckResult ValidateServiceEndpoints(HealthCheckContext context, ServiceType serviceType,
        IEnumerable<IEndpointDiagnostics> endpoints)
    {
        var allNodes = new HashSet<string>();
        var healthyNodes = new HashSet<string>();

        foreach (var endpoint in endpoints)
        {
            if (endpoint.Remote is not null)
            {
                allNodes.Add(endpoint.Remote);

                if (endpoint.State is ServiceState.Connected or ServiceState.Ok ||
                    (endpoint.State is null && endpoint.EndpointState is EndpointState.Connected))
                {
                    healthyNodes.Add(endpoint.Remote);
                }
            }
        }

        var minimumHealthyNodes = MinimumHealthyNodesByService.TryGetValue(serviceType, out var minHealthy)
            ? minHealthy
            : 1;

        if (healthyNodes.Count < minimumHealthyNodes)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                BuildFailedResultMessage(serviceType, allNodes, healthyNodes));
        }

        var maximumUnhealthyNodes = MinimumHealthyNodesByService.TryGetValue(serviceType, out var maxUnhealthy)
            ? maxUnhealthy
            : int.MaxValue;

        if ((allNodes.Count - healthyNodes.Count) > maximumUnhealthyNodes)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                BuildFailedResultMessage(serviceType, allNodes, healthyNodes));
        }

        return HealthCheckResult.Healthy();
    }

    private static string? GetServiceName(ServiceType serviceType)
    {
        return serviceType switch
        {
            ServiceType.KeyValue => "kv",
            ServiceType.Query => "n1ql",
            ServiceType.Search => "fts",
            ServiceType.Analytics => "cbas",
            ServiceType.Views => "views",
            _ => null
        };
    }

    private static string BuildFailedResultMessage(ServiceType serviceType, HashSet<string> allNodes, HashSet<string> healthyNodes)
    {
        var builder = new StringBuilder();
        builder.Append("Couchbase health check failed for service ");
        builder.Append(serviceType);

        if (allNodes.Count > 0)
        {
            builder.Append(" for nodes ");

            var enumerator = allNodes.Except(healthyNodes).GetEnumerator();
            try
            {
                if (enumerator.MoveNext())
                {
                    builder.Append(enumerator.Current);

                    while (enumerator.MoveNext())
                    {
                        builder.Append(", ");
                        builder.Append(enumerator.Current);
                    }
                }
            }
            finally
            {
                enumerator?.Dispose();
            }

            builder.Append('.');
        }
        else
        {
            builder.Append(", no nodes available.");
        }

        return builder.ToString();
    }
}
