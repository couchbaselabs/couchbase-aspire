namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Represents a Couchbase sample bucket resource within a Couchbase cluster, providing access to connection
/// information and bucket metadata for integration and testing scenarios.
/// </summary>
/// <param name="name">The unique name of the resource instance.</param>
/// <param name="bucketName">The name of the Couchbase sample bucket represented by this resource.</param>
/// <param name="parent">The parent Couchbase cluster resource that hosts this bucket.</param>
public class CouchbaseSampleBucketResource(string name, string bucketName, CouchbaseClusterResource parent)
    : CouchbaseBucketBaseResource(name, bucketName, parent), ICouchbaseBucketResource<CouchbaseSampleBucketResource>
{
    public static CouchbaseSampleBucketResource Create(string name, string bucketName, CouchbaseClusterResource parent) =>
        new(name, bucketName, parent);
}
