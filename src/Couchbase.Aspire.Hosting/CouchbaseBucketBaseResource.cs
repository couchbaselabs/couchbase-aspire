using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

internal interface ICouchbaseBucketResource<TSelf> where TSelf : ICouchbaseBucketResource<TSelf>
{
    static abstract TSelf Create(string name, string bucketName, CouchbaseClusterResource parent);
}

/// <summary>
/// Represents a Couchbase bucket resource within a Couchbase cluster, providing access to connection
/// information and bucket metadata for integration and testing scenarios.
/// </summary>
/// <param name="name">The unique name of the resource instance.</param>
/// <param name="bucketName">The name of the Couchbase bucket represented by this resource.</param>
/// <param name="cluster">The parent Couchbase cluster resource that hosts this bucket.</param>
public abstract class CouchbaseBucketBaseResource(string name, string bucketName, CouchbaseClusterResource cluster)
    : Resource(name), IResourceWithWaitSupport, IResourceWithConnectionString, ICouchbaseCustomResource
{
    /// <summary>
    /// Gets the parent Couchbase Server container resource.
    /// </summary>
    public CouchbaseClusterResource Cluster { get; } = cluster ?? throw new ArgumentNullException(nameof(cluster));

    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string BucketName { get; } = ThrowHelpers.ThrowIfNullOrEmpty(bucketName);

    /// <summary>
    /// Gets the bucket name expression for the Couchbase bucket.
    /// </summary>
    public ReferenceExpression BucketNameExpression => ReferenceExpression.Create($"{BucketName}");

    /// <summary>
    /// Gets the connection string expression for the Couchbase bucket.
    /// </summary>
    /// <remarks>
    /// Format: <c>couchbase://{user}:{password}@{host}:{port}/{bucketName}</c>.
    /// </remarks>
    public ReferenceExpression ConnectionStringExpression => Cluster.BuildConnectionString(BucketName);

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
        Cluster.CombineProperties([
            new("BucketName", BucketNameExpression)
        ]);
}
