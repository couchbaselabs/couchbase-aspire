using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
/// <param name="parent">The parent Couchbase cluster resource that hosts this bucket.</param>
public abstract class CouchbaseBucketBaseResource(string name, string bucketName, CouchbaseClusterResource parent)
    : Resource(name), IResourceWithWaitSupport, IResourceWithConnectionString, ICouchbaseCustomResource
{
    /// <summary>
    /// Gets the parent Couchbase Server container resource.
    /// </summary>
    public CouchbaseClusterResource Parent { get; } = parent ?? throw new ArgumentNullException(nameof(parent));

    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string BucketName { get; } = ThrowIfNullOrEmpty(bucketName);

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
    public ReferenceExpression ConnectionStringExpression => Parent.BuildConnectionString(BucketName);

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
        Parent.CombineProperties([
            new("BucketName", BucketNameExpression)
        ]);

    private static string ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(argument, paramName);
        return argument;
    }
}
