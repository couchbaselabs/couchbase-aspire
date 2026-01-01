using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Hosting;

internal static class CouchbaseResourceExtensions
{
    public static CouchbaseClusterInitializerResource? GetClusterInitializerResource(this CouchbaseClusterResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        if (cluster.TryGetLastAnnotation<CouchbaseClusterInitializerAnnotation>(out var annotation))
        {
            return annotation.Initializer;
        }

        return null;
    }

    public static CouchbaseClusterInitializer GetClusterInitializer(this CouchbaseClusterInitializerResource initializerResource,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(initializerResource);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return serviceProvider.GetRequiredKeyedService<CouchbaseClusterInitializer>(initializerResource);
    }

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

    public static bool IsInitialNode(this CouchbaseServerResource server) =>
        server.HasAnnotationOfType<CouchbaseInitialNodeAnnotation>();

    public static EndpointReference GetManagementEndpoint(this CouchbaseServerResource server, bool preferInsecure = false) =>
        !preferInsecure && server.Cluster.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>()
            ? server.GetEndpoint(CouchbaseEndpointNames.ManagementSecure)
            : server.GetEndpoint(CouchbaseEndpointNames.Management);
}
