using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public class CouchbaseServerGroupResource : Resource, IResourceWithoutLifetime
{
    public CouchbaseServerGroupResource(string name, CouchbaseClusterResource parent, CouchbaseServices services) : base(name)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Parent = parent;
        Services = services;
    }

    /// <summary>
    /// Gets the parent Couchbase Server container resource.
    /// </summary>
    public CouchbaseClusterResource Parent { get; }

    public CouchbaseServices Services { get; }

    private readonly Dictionary<string, CouchbaseServerResource> _servers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A dictionary where the key is the resource name and the value is the bucket name.
    /// </summary>
    public IReadOnlyDictionary<string, CouchbaseServerResource> Servers => _servers;

    internal void AddServer(string name, CouchbaseServerResource server)
    {
        _servers.TryAdd(name, server);
    }
}
