using System.Collections.Frozen;
using Couchbase.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Client;

internal sealed class BucketProvider(IClusterProvider clusterProvider, Dictionary<string, string> bucketNameMap) : IBucketProvider
{
    private readonly FrozenDictionary<string, string> _bucketNameMap = bucketNameMap.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // The default implementation of IClusterProvider also implements IBucketProvider
    private readonly IBucketProvider? _bucketProvider = clusterProvider as IBucketProvider;

    public ValueTask<IBucket> GetBucketAsync(string bucketName)
    {
        if (_bucketNameMap.TryGetValue(bucketName, out var mappedName))
        {
            // This is a logical bucket name, switch to the physical bucket name
            bucketName = mappedName;
        }

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

    void IDisposable.Dispose()
    {
    }

    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
}
