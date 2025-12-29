using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public class CouchbaseBucketResource(string name, string bucketName, CouchbaseClusterResource parent)
    : Resource(name), IResourceWithConnectionString, IResourceWithWaitSupport
{
    /// <summary>
    /// Gets the parent Couchbase Server container resource.
    /// </summary>
    public CouchbaseClusterResource Parent { get; } = parent ?? throw new ArgumentNullException(nameof(parent));

    /// <summary>
    /// Gets the connection string expression for the Couchbase bucket.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression => Parent.BuildConnectionString(BucketName);

    /// <summary>
    /// Gets the connection URI expression for the Couchbase bucket.
    /// </summary>
    /// <remarks>
    /// Format: <c>couchbase://[user:password@]{host}:{port}/{bucket}</c>. The credential and query segments are included only when a password is configured.
    /// </remarks>
    public ReferenceExpression UriExpression => Parent.BuildConnectionString(BucketName);

    /// <summary>
    /// Gets the database name.
    /// </summary>
    public string BucketName { get; } = ThrowIfNullOrEmpty(bucketName);

    private static string ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(argument, paramName);
        return argument;
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties() =>
        Parent.CombineProperties([
            new("BucketName", ReferenceExpression.Create($"{BucketName}")),
            new("Uri", UriExpression),
        ]);
}
