using Couchbase.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Client;

internal sealed class BucketProvider(IClusterProvider clusterProvider, string bucketName) : INamedBucketProvider
{
    // The default implementation of IClusterProvider also implements IBucketProvider
    private readonly IBucketProvider? _bucketProvider = clusterProvider as IBucketProvider;

    public string BucketName => bucketName;

    public ValueTask<IBucket> GetBucketAsync()
    {
        if (_bucketProvider is not null)
        {
            // Fast path for the default implementation of IClusterProvider
            return _bucketProvider.GetBucketAsync(bucketName);
        }
        else
        {
            // Fallback
            return GetBucketManualAsync(bucketName);
        }
    }

    private async ValueTask<IBucket> GetBucketManualAsync(string bucketName)
    {
        var cluster = await clusterProvider.GetClusterAsync().ConfigureAwait(false);
        return await cluster.BucketAsync(bucketName).ConfigureAwait(false);
    }
}
