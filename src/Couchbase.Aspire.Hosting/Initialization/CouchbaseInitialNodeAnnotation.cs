using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting.Initialization;

/// <summary>
/// Marks the initial Couchbase server node used for cluster initialization.
/// </summary>
internal sealed class CouchbaseInitialNodeAnnotation : IResourceAnnotation
{
}
