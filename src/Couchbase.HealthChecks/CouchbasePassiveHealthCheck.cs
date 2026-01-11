using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

/// <summary>
/// Couchbase health check that passively observes the cluster diagnostics report.
/// </summary>
/// <param name="clusterFactory">Factory to obtain the <see cref="ICluster" /> instance.</param>
public class CouchbasePassiveHealthCheck(
    Func<CancellationToken, ValueTask<ICluster>> clusterFactory)
    : CouchbaseHealthCheck(clusterFactory)
{
    /// <inheritdoc />
    protected override async Task<HealthCheckResult> PerformCheckAsync(HealthCheckContext context, ICluster cluster, CancellationToken cancellationToken)
    {
        var diagnosticReport = await cluster.DiagnosticsAsync().ConfigureAwait(false);

        return ParseReport(context, diagnosticReport);
    }

    /// <summary>
    /// Analyzes the diagnostic report and converts it to a <see cref="HealthCheckResult"/>.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="diagnosticReport">The diagnostic report.</param>
    /// <returns>The health check result.</returns>
    protected virtual HealthCheckResult ParseReport(HealthCheckContext context, IDiagnosticsReport diagnosticReport) =>
        ParseReport(context, diagnosticReport.Services);
}
