using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Represents a Couchbase bucket resource within a Couchbase cluster, providing access to connection
/// information and bucket metadata for integration and testing scenarios.
/// </summary>
/// <param name="name">The unique name of the resource instance.</param>
/// <param name="bucket">The parent Couchbase bucket resource.</param>
public class CouchbaseIndexManagerResource(string name, CouchbaseBucketResource bucket)
    : ContainerResource(name)
{
    /// <summary>
    /// Gets the parent Couchbase bucket resource.
    /// </summary>
    public CouchbaseBucketResource Bucket { get; } = bucket ?? throw new ArgumentNullException(nameof(bucket));
}
