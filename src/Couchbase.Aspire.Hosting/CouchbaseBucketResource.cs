namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Represents a Couchbase bucket resource within a Couchbase cluster, providing access to connection
/// information and bucket metadata for integration and testing scenarios.
/// </summary>
/// <param name="name">The unique name of the resource instance.</param>
/// <param name="bucketName">The name of the Couchbase bucket represented by this resource.</param>
/// <param name="parent">The parent Couchbase cluster resource that hosts this bucket.</param>
public class CouchbaseBucketResource(string name, string bucketName, CouchbaseClusterResource parent)
    : CouchbaseBucketBaseResource(name, bucketName, parent), ICouchbaseBucketResource<CouchbaseBucketResource>
{
    public static CouchbaseBucketResource Create(string name, string bucketName, CouchbaseClusterResource parent) =>
        new(name, bucketName, parent);
}
