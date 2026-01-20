using System.Diagnostics.CodeAnalysis;
using Couchbase.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Client;

/// <summary>
/// Provides access to a Couchbase cluster and named bucket.
/// </summary>
/// <remarks>
/// <see cref="INamedBucketProvider.GetBucketAsync"/> will throw a <see cref="CouchbaseException"/>
/// if a bucket name is not configured for this client.
/// </remarks>
public interface ICouchbaseClientProvider : IClusterProvider, INamedBucketProvider
{
}

internal sealed class CouchbaseClientProvider(IClusterProvider clusterProvider, string? bucketName) : ICouchbaseClientProvider
{
    // The default implementation of IClusterProvider also implements IBucketProvider
    private readonly IBucketProvider? _bucketProvider = clusterProvider as IBucketProvider;

    public string BucketName => bucketName ?? "";

    public ValueTask<ICluster> GetClusterAsync() => clusterProvider.GetClusterAsync();

    public ValueTask<IBucket> GetBucketAsync()
    {
        if (string.IsNullOrEmpty(bucketName))
        {
            ThrowNoBucketNameException();
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

    // Don't dispose of the cluster provider, it's owned by the DI container and will
    // be disposed via that path.

    void IDisposable.Dispose()
    {
    }

    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;

    [DoesNotReturn]
    private static void ThrowNoBucketNameException()
    {
        throw new CouchbaseException("No bucket name was configured for this Couchbase client.");
    }
}
