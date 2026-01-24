using Couchbase.Extensions.Caching;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Couchbase.Aspire.Client.DistributedCaching;

internal sealed class AspireCouchbaseCacheBucketProvider(
    IBucketProvider bucketProvider,
    IOptions<CouchbaseCacheOptions> options)
    : NamedBucketProvider(bucketProvider, options.Value.BucketName), ICouchbaseCacheBucketProvider;
