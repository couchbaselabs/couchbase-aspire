using Couchbase;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to configure <see cref="CouchbaseHealthCheck"/>.
/// </summary>
public static class CouchbaseHealthChecksBuilderExtensions
{
    private const string NAME = "couchbase";

    /// <summary>
    /// Add a health check for Couchbase that pings a set of services and optionally confirms connection
    /// to a specificbucket returned by <paramref name="bucketNameFactory"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHealthChecksBuilder"/>.</param>
    /// <param name="clusterFactory">
    /// An optional factory to obtain <see cref="ICluster" /> instance.
    /// When not provided, <see cref="IClusterProvider" /> is simply resolved from <see cref="IServiceProvider"/>.
    /// </param>
    /// <param name="serviceTypesFactory">
    /// List of services to test. If <c>null</c> or if the callback returns <c>null</c>,
    /// the key/value service will be pinged.
    /// </param>
    /// <param name="bucketNameFactory">An optional factory to obtain the name of the bucket to connect.</param>
    /// <param name="activePing">Whether to perform an active ping or passive observation.</param>
    /// <param name="name">The health check name. Optional. If <c>null</c>, the type name 'couchbase' will be used for the name.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> that should be reported when the health check fails. Optional. If <c>null</c> then
    /// the default status of <see cref="HealthStatus.Unhealthy"/> will be reported.
    /// </param>
    /// <param name="tags">A list of tags that can be used to filter sets of health checks. Optional.</param>
    /// <param name="timeout">An optional <see cref="TimeSpan"/> representing the timeout of the check.</param>
    /// <returns>The specified <paramref name="builder"/>.</returns>
    public static IHealthChecksBuilder AddCouchbase(this IHealthChecksBuilder builder,
        Func<IServiceProvider, CancellationToken, Task<ICluster>>? clusterFactory = null,
        Func<IServiceProvider, ServiceType[]?>? serviceTypesFactory = null,
        Func<IServiceProvider, string>? bucketNameFactory = null,
        bool activePing = true,
        string? name = null,
        HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default,
        TimeSpan? timeout = default)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.Add(new HealthCheckRegistration(
            name ?? NAME,
            sp =>
            {
                // Cache the result from the cluster factory. When using IClusterProvider,
                // use the cache within the provider itself.
                ICluster? cluster = null;

                Func<CancellationToken, ValueTask<ICluster>> wrappedClusterFactory = clusterFactory is not null
                    ? async (ct) => cluster ??= await clusterFactory(sp, ct).ConfigureAwait(false)
                    : (ct) =>
                    {
                        var clusterTask = sp.GetRequiredService<IClusterProvider>().GetClusterAsync();

                        // Avoid the expensive of allocating a Task<T> on the heap if the ValueTask<T> is already complete
                        // or if the caller isn't providing a cancellation token. In both cases WaitAsync is unnecessary.
                        if (clusterTask.IsCompleted || !ct.CanBeCanceled)
                        {
                            return clusterTask;
                        }

                        return new ValueTask<ICluster>(clusterTask.AsTask().WaitAsync(ct));
                    };

                ServiceType[]? serviceTypes = serviceTypesFactory?.Invoke(sp);
                string? bucketName = bucketNameFactory?.Invoke(sp);

                return new CouchbaseHealthCheck(wrappedClusterFactory, activePing, serviceTypes, bucketName);
            },
            failureStatus,
            tags,
            timeout));
    }
}
