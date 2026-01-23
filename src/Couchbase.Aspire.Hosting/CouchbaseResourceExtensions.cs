using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.HealthChecks;

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

    public static CouchbaseIndexStorageMode GetIndexStorageMode(this CouchbaseClusterResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        CouchbaseIndexStorageMode? mode = null;
        if (cluster.TryGetLastAnnotation<CouchbaseIndexStorageModeAnnotation>(out var annotation))
        {
            mode = annotation.Mode;
        }

        return mode ?? (cluster.GetCouchbaseEdition() == CouchbaseEdition.Enterprise
            ? CouchbaseIndexStorageMode.Plasma
            : CouchbaseIndexStorageMode.ForestDB);
    }

    private static CouchbaseServices ApplyDefaults(this CouchbaseServices services) =>
        services == CouchbaseServices.Default
            ? CouchbaseServicesAnnotation.DefaultServices
            : services;

    private static CouchbaseServicesAnnotation? GetCouchbaseServicesAnnotation(this ICouchbaseCustomResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.TryGetLastAnnotation<CouchbaseServicesAnnotation>(out var annotation))
        {
            return annotation;
        }

        return null;
    }

    public static CouchbaseServices GetCouchbaseServices(this CouchbaseClusterResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        return (cluster.GetCouchbaseServicesAnnotation()?.Services ?? CouchbaseServices.Default).ApplyDefaults();
    }

    public static CouchbaseServices GetCouchbaseServices(this CouchbaseServerGroupResource serverGroup)
    {
        ArgumentNullException.ThrowIfNull(serverGroup);

        var annotation = serverGroup.GetCouchbaseServicesAnnotation() ?? serverGroup.Parent.GetCouchbaseServicesAnnotation();

        return (annotation?.Services ?? CouchbaseServices.Default).ApplyDefaults();
    }

    public static CouchbaseServices GetCouchbaseServices(this CouchbaseServerResource server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return server.Parent.GetCouchbaseServices();
    }

    public static Dictionary<ServiceType, List<ICouchbaseServiceHealthRequirement>> GetHealthCheckServiceRequirements(this CouchbaseClusterResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        // Always include the data service
        var enabledServices = CouchbaseServices.Data;
        foreach (var serverGroup in cluster.ServerGroups.Values)
        {
            enabledServices |= serverGroup.GetCouchbaseServices();
        }

        static Dictionary<ServiceType, List<ICouchbaseServiceHealthRequirement>> GetServiceList(CouchbaseServices services)
        {
            var result = new Dictionary<ServiceType, List<ICouchbaseServiceHealthRequirement>>();

            if (services.HasFlag(CouchbaseServices.Data))
            {
                // For data, require all nodes be healthy
                result.Add(ServiceType.KeyValue, [CouchbaseServiceHealthNodeRequirement.AllNodesHealthy]);
            }

            // For other services, only require a minimum of 1 healthy node
            if (services.HasFlag(CouchbaseServices.Query))
            {
                result.Add(ServiceType.Query, [CouchbaseServiceHealthNodeRequirement.OneHealthyNode]);
            }
            if (services.HasFlag(CouchbaseServices.Analytics))
            {
                result.Add(ServiceType.Analytics, [CouchbaseServiceHealthNodeRequirement.OneHealthyNode]);
            }
            if (services.HasFlag(CouchbaseServices.Search))
            {
                result.Add(ServiceType.Search, [CouchbaseServiceHealthNodeRequirement.OneHealthyNode]);
            }

            return result;
        }

        return GetServiceList(enabledServices);
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
