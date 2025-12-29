using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting.Initialization;

public class CouchbaseClusterInitializerResource : Resource, IResourceWithWaitSupport
{
    public CouchbaseClusterInitializerResource(string name, CouchbaseClusterResource parent) : base(name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Parent = parent;
    }

    public CouchbaseClusterResource Parent { get; }
}
