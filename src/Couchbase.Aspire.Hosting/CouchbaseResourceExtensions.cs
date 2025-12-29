using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting.Initialization;

namespace Couchbase.Aspire.Hosting;

internal static class CouchbaseResourceExtensions
{
    public static CouchbaseClusterInitializerResource? GetClusterInitializer(this CouchbaseClusterResource cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        if (cluster.TryGetLastAnnotation<CouchbaseClusterInitializerAnnotation>(out var annotation))
        {
            return annotation.Initializer;
        }

        return null;
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
}
