using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

/// <summary>
/// Couchbase health check that actively pings a set of services and optionally confirms connection to a specific bucket.
/// </summary>
/// <param name="clusterFactory">Factory to obtain the <see cref="ICluster" /> instance.</param>
/// <param name="bucketName">Optional bucket name to check.</param>
public class CouchbaseActiveHealthCheck(
    Func<CancellationToken, ValueTask<ICluster>> clusterFactory,
    string? bucketName = null)
    : CouchbaseHealthCheck(clusterFactory)
{
    /// <inheritdoc />
    protected override async Task<HealthCheckResult> PerformCheckAsync(HealthCheckContext context, ICluster cluster, CancellationToken cancellationToken)
    {
        var pingOptions = new PingOptions()
            .ServiceTypes([..ServiceTypes])
            .CancellationToken(cancellationToken);

        IPingReport pingReport;
        if (bucketName is not null)
        {
            var bucketTask = cluster.BucketAsync(bucketName);

            var bucket = bucketTask.IsCompleted
                ? await bucketTask
                : await bucketTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);

            pingReport = await bucket.PingAsync(pingOptions).ConfigureAwait(false);
        }
        else
        {
            pingReport = await cluster.PingAsync(pingOptions).ConfigureAwait(false);
        }

        return ParseReport(context, pingReport);
    }

    /// <summary>
    /// Analyzes the ping report and converts it to a <see cref="HealthCheckResult"/>.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="pingReport">The ping report.</param>
    /// <returns>The health check result.</returns>
    protected virtual HealthCheckResult ParseReport(HealthCheckContext context, IPingReport pingReport) =>
        ParseReport(context, pingReport.Services);
}
