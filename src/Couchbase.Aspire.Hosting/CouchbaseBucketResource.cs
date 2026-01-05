using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Represents a Couchbase bucket resource within a Couchbase cluster, providing access to connection
/// information and bucket metadata for integration and testing scenarios.
/// </summary>
/// <param name="name">The unique name of the resource instance.</param>
/// <param name="bucketName">The name of the Couchbase bucket represented by this resource.</param>
/// <param name="parent">The parent Couchbase cluster resource that hosts this bucket.</param>
public class CouchbaseBucketResource(string name, string bucketName, CouchbaseClusterResource parent)
    : Resource(name), IResourceWithWaitSupport, ICouchbaseCustomResource
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

    private static string ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(argument, paramName);
        return argument;
    }
}
