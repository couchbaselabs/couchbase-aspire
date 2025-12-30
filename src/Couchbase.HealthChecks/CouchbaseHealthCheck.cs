using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

public class CouchbaseHealthCheck(
    Func<CancellationToken, ValueTask<ICluster>> clusterFactory,
    ServiceType[]? pingServiceTypes = null,
    string? bucketName = null)
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

    private readonly ServiceType[] _pingServiceTypes = pingServiceTypes ?? [ServiceType.KeyValue];
    private ICluster? _cluster;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            for (int attempt = 1; attempt <= MAX_CONNECTION_ATTEMPTS; attempt++)
            {
                try
                {
                    var cluster = _cluster ??= await clusterFactory(cancellationToken).ConfigureAwait(false);

                    await cluster.PingAsync(new PingOptions()
                        .ServiceTypes(_pingServiceTypes)
                        .CancellationToken(cancellationToken));

                    if (bucketName is not null)
                    {
                        await cluster.BucketAsync(bucketName).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
                    }

                    break;
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

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
