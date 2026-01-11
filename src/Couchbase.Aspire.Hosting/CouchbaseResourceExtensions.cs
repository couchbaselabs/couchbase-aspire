using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

internal static class CouchbaseResourceExtensions
{
    public static async Task<CouchbaseClusterSettings> GetClusterSettingsAsync(this CouchbaseClusterResource cluster, DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        var settingsContext = new CouchbaseClusterSettingsCallbackContext(executionContext, cancellationToken: cancellationToken);
        if (cluster.TryGetAnnotationsOfType<CouchbaseClusterSettingsCallbackAnnotation>(out var settingsAnnotations))
        {
            foreach (var settingsAnnotation in settingsAnnotations)
            {
                await settingsAnnotation.Callback(settingsContext).ConfigureAwait(false);
            }
        }

        return settingsContext.Settings;
    }

    public static CouchbaseEdition GetCouchbaseEdition(this CouchbaseClusterResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        if (cluster.TryGetLastAnnotation<CouchbaseEditionAnnotation>(out var annotation))
        {
            return annotation.Edition;
        }

        return CouchbaseEdition.Enterprise;
    }

    public static Dictionary<ServiceType, int> GetHealthCheckServiceTypes(this CouchbaseClusterResource cluster,
        bool maximumUnhealthy)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        // Always include the data service
        var enabledServices = CouchbaseServices.Data;
        foreach (var serverGroup in cluster.ServerGroups.Values)
        {
            enabledServices |= serverGroup.Services;
        }

        static Dictionary<ServiceType, int> GetServiceList(CouchbaseServices services, bool maximumUnhealthy)
        {
            var result = new Dictionary<ServiceType, int>();

            if (services.HasFlag(CouchbaseServices.Data))
            {
                // For data, require all nodes be healthy
                result[ServiceType.KeyValue] = maximumUnhealthy ? 0 : 1;
            }

            if (!maximumUnhealthy)
            {
                // For other services, only require a minimum of 1 healthy node
                if (services.HasFlag(CouchbaseServices.Query))
                {
                    result[ServiceType.Query] = 1;
                }
                if (services.HasFlag(CouchbaseServices.Analytics))
                {
                    result[ServiceType.Analytics] = 1;
                }
                if (services.HasFlag(CouchbaseServices.Fts))
                {
                    result[ServiceType.Search] = 1;
                }
            }

            return result;
        }

        return GetServiceList(enabledServices, maximumUnhealthy);
    }

    public static CouchbaseCertificateAuthorityAnnotation? GetClusterCertificationAuthority(this CouchbaseClusterResource cluster)
    {
        if (cluster.TryGetLastAnnotation<CouchbaseCertificateAuthorityAnnotation>(out var annotation) &&
            annotation.Certificate is not null)
        {
            return annotation;
        }

        return null;
    }

    public static async Task<CouchbaseBucketSettings> GetBucketSettingsAsync(this CouchbaseBucketResource cluster, DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken = default)
    {
        var settingsContext = new CouchbaseBucketSettingsCallbackContext(executionContext, cancellationToken: cancellationToken);
        if (cluster.TryGetAnnotationsOfType<CouchbaseBucketSettingsCallbackAnnotation>(out var settingsAnnotations))
        {
            foreach (var settingsAnnotation in settingsAnnotations)
            {
                await settingsAnnotation.Callback(settingsContext).ConfigureAwait(false);
            }
        }

        return settingsContext.Settings;
    }

    public static bool HasPrimaryServer(this CouchbaseClusterResource cluster) =>
        cluster.GetPrimaryServer() is not null;

    public static CouchbaseServerResource? GetPrimaryServer(this CouchbaseClusterResource cluster) =>
        cluster.Servers.FirstOrDefault(IsPrimaryServer);

    public static bool IsPrimaryServer(this CouchbaseServerResource server) =>
        server.HasAnnotationOfType<CouchbasePrimaryServerAnnotation>();

    public static EndpointReference GetManagementEndpoint(this CouchbaseServerResource server, bool preferInsecure = false) =>
        !preferInsecure && server.Cluster.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>()
            ? server.GetEndpoint(CouchbaseEndpointNames.ManagementSecure)
            : server.GetEndpoint(CouchbaseEndpointNames.Management);
}
