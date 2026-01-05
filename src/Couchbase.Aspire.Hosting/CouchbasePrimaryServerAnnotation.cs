using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Marks the initial Couchbase server node used for cluster initialization.
/// </summary>
internal sealed class CouchbasePrimaryServerAnnotation : IResourceAnnotation
{
}
