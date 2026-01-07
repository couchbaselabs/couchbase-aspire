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

    public static ServiceType[] GetHealthCheckServiceTypes(this CouchbaseClusterResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        // Always include the data service
        var enabledServices = CouchbaseServices.Data;
        foreach (var serverGroup in cluster.ServerGroups.Values)
        {
            enabledServices |= serverGroup.Services;
        }

        static IEnumerable<ServiceType> GetServiceList(CouchbaseServices services)
        {
            if (services.HasFlag(CouchbaseServices.Data))
            {
                yield return ServiceType.KeyValue;
            }
            if (services.HasFlag(CouchbaseServices.Query))
            {
                yield return ServiceType.Query;
            }
            if (services.HasFlag(CouchbaseServices.Analytics))
            {
                yield return ServiceType.Analytics;
            }
            if (services.HasFlag(CouchbaseServices.Fts))
            {
                yield return ServiceType.Search;
            }
        }

        return [.. GetServiceList(enabledServices)];
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
