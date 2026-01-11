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
    /// An optional factory to obtain the <see cref="ICluster" /> instance.
    /// When not provided, <see cref="IClusterProvider" /> is simply resolved from <see cref="IServiceProvider"/>.
    /// </param>
    /// <param name="serviceRequirementsFactory">
    /// Factory that defines service health requirements. If <c>null</c>, defaults to requiring 1 healthy data node and
    /// disallowing unhealthy data nodes.
    /// </param>
    /// <param name="bucketNameFactory">An optional factory to obtain the name of the bucket to connect. Only applies to active health checks.</param>
    /// <param name="healthCheckType">Whether to perform an active ping or passive observation.</param>
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
        Func<IServiceProvider, Dictionary<ServiceType, List<ICouchbaseServiceHealthRequirement>>>? serviceRequirementsFactory = null,
        Func<IServiceProvider, string>? bucketNameFactory = null,
        CouchbaseHealthCheckType healthCheckType = CouchbaseHealthCheckType.Active,
        string? name = null,
        HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default,
        TimeSpan? timeout = default)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }
        if (healthCheckType < CouchbaseHealthCheckType.Active || healthCheckType > CouchbaseHealthCheckType.Passive)
        {
            throw new ArgumentException($"Unsupported health check type: {healthCheckType}.", nameof(healthCheckType));
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

                CouchbaseHealthCheck healthCheck = healthCheckType switch
                {
                    CouchbaseHealthCheckType.Active =>
                        new CouchbaseActiveHealthCheck(wrappedClusterFactory, bucketNameFactory?.Invoke(sp)),
                    CouchbaseHealthCheckType.Passive =>
                        new CouchbasePassiveHealthCheck(wrappedClusterFactory),
                    _ => null! // Unreachable
                };

                var serviceRequirements = serviceRequirementsFactory?.Invoke(sp);
                if (serviceRequirements is not null)
                {
                    healthCheck.ServiceRequirements = serviceRequirements;
                }

                return healthCheck;
            },
            failureStatus,
            tags,
            timeout));
    }
}
