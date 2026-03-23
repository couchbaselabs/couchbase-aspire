using Couchbase.Extensions.Caching;
using Couchbase.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Client.DistributedCaching;

internal sealed class AspireCouchbaseCacheBucketProvider(
    IBucketProvider bucketProvider,
    CouchbaseCacheOptions options)
    : NamedBucketProvider(bucketProvider, options.BucketName), ICouchbaseCacheBucketProvider;
