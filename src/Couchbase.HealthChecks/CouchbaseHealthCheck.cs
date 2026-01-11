using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

/// <summary>
/// Base type for a Couchbase health check.
/// </summary>
/// <param name="clusterFactory">Factory to obtain the <see cref="ICluster" /> instance.</param>
public abstract class CouchbaseHealthCheck(
    Func<CancellationToken, ValueTask<ICluster>> clusterFactory)
    : IHealthCheck
{
    // When running the tests locally during development, don't re-attempt
    // as it prolongs the time it takes to run the tests.
    private const int MAX_CONNECTION_ATTEMPTS
#if DEBUG
        = 1;
#else
        = 2;
#endif

    private readonly Func<CancellationToken, ValueTask<ICluster>> _clusterFactory = clusterFactory ?? throw new ArgumentNullException(nameof(clusterFactory));
    private ICluster? _cluster;

    /// <summary>
    /// Requirements to enforce for each service type.
    /// </summary>
    /// <value>
    /// Defaults to requiring 1 healthy node and allowing no unhealthy nodes for <see cref="ServiceType.KeyValue"/>.
    /// </value>
    public Dictionary<ServiceType, List<ICouchbaseServiceHealthRequirement>> ServiceRequirements
    {
        get => field ??= CreateDefaultServiceRequirements();
        set
        {
#if NET6_0_OR_GREATER
            ArgumentNullException.ThrowIfNull(value);
#else
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }
#endif

            field = value;
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
    private protected async ValueTask<HealthCheckResult> ParseReportAsync(HealthCheckContext context, IDictionary<string, IEnumerable<IEndpointDiagnostics>> services,
        CancellationToken cancellationToken = default)
    {
        foreach (var service in ServiceRequirements)
        {
            var serviceName = GetServiceName(service.Key);
            if (serviceName is not null)
            {
                if (!services.TryGetValue(serviceName, out var endpoints))
                {
                    // No endpoints for this service
                    endpoints = [];
                }

                foreach (var requirement in service.Value)
                {
                    var result = await requirement.ValidateAsync(context, service.Key, endpoints, cancellationToken).ConfigureAwait(false);
                    if (result.Status != HealthStatus.Healthy)
                    {
                        return result;
                    }
                }
            }
        }

        return HealthCheckResult.Healthy();
    }

    /// <summary>
    /// Builds a default set of requirements for the <see cref="ServiceRequirements"/> property.
    /// </summary>
    /// <returns>A new dictionary with the default requirements.</returns>
    public static Dictionary<ServiceType, List<ICouchbaseServiceHealthRequirement>> CreateDefaultServiceRequirements() =>
        new()
        {
            { ServiceType.KeyValue, [CouchbaseServiceHealthNodeRequirement.AllNodesHealthy] },
        };

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
}
