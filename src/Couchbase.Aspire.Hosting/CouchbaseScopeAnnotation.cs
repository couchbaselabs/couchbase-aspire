using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Indicates a scope to be created for a Couchbase bucket.
/// </summary>
/// <param name="scopeName">Name of the scope.</param>
public class CouchbaseScopeAnnotation(string scopeName) : IResourceAnnotation
{
    /// <summary>
    /// Name of the scope.
    /// </summary>
    public string ScopeName { get; } = ThrowHelpers.ThrowIfNullOrEmpty(scopeName);

    /// <summary>
    /// List of collection names to be created within the scope.
    /// </summary>
    public List<string> CollectionNames
    {
        get => field ??= [];
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            field = value;
        }
    }
}
