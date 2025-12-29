using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting.Initialization;

public class CouchbaseClusterInitializerAnnotation : IResourceAnnotation
{
    public CouchbaseClusterInitializerResource? Initializer { get; set; }
}
