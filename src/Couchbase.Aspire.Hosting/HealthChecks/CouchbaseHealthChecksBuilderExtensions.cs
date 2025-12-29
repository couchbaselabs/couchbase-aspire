using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.Aspire.Hosting.HealthChecks;

internal static class CouchbaseHealthChecksBuilderExtensions
{
    private const string NAME = "couchbase";

    public static IHealthChecksBuilder AddCouchbase(this IHealthChecksBuilder builder,
        Func<IServiceProvider, CancellationToken, Task<ICluster>>? clusterFactory = null,
        Func<IServiceProvider, ServiceType>? pingServiceTypesFactory = null,
        Func<IServiceProvider, string>? bucketNameFactory = null,
        string? name = null,
        HealthStatus? failureStatus = default,
        IEnumerable<string>? tags = default,
        TimeSpan? timeout = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name ?? NAME,
            sp => Factory(sp, clusterFactory, pingServiceTypesFactory, bucketNameFactory),
            failureStatus,
            tags,
            timeout));

        static CouchbaseHealthCheck Factory(
            IServiceProvider sp,
            Func<IServiceProvider, CancellationToken, Task<ICluster>>? clusterFactory,
            Func<IServiceProvider, ServiceType>? pingServiceTypesFactory,
            Func<IServiceProvider, string>? bucketNameFactory)
        {
            clusterFactory ??= static (sp, _) => sp.GetRequiredService<IClusterProvider>().GetClusterAsync().AsTask();
            ServiceType pingServiceTypes = pingServiceTypesFactory?.Invoke(sp) ?? ServiceType.KeyValue;
            string? bucketName = bucketNameFactory?.Invoke(sp);

            return new(sp, clusterFactory, pingServiceTypes, bucketName);
        }
    }
}
