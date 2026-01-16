using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public class CouchbaseServerGroupResource : Resource, ICouchbaseCustomResource, IResourceWithParent<CouchbaseClusterResource>
{
    public CouchbaseServerGroupResource(string name, CouchbaseClusterResource parent) : base(name)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Parent = parent;
    }

    internal CouchbaseServerGroupResource(string name, CouchbaseClusterResource parent, bool isDefaultServerGroup) : this(name, parent)
    {
        IsDefaultServerGroup = isDefaultServerGroup;
    }

    /// <summary>
    /// Gets the parent Couchbase Server container resource.
    /// </summary>
    public CouchbaseClusterResource Parent { get; }

    internal bool IsDefaultServerGroup { get; }

    private readonly List<CouchbaseServerResource> _servers = [];

    /// <summary>
    /// A list of servers in order of their replica number.
    /// </summary>
    public IReadOnlyList<CouchbaseServerResource> Servers => _servers;

    internal void AddServer(CouchbaseServerResource server)
    {
        _servers.Add(server);
    }

    /// <summary>
    /// Removes excess servers beyond the specified maximum number of replicas.
    /// </summary>
    /// <param name="maximumReplicas">Maximum replicas.</param>
    /// <returns>Servers that were removed.</returns>
    internal List<CouchbaseServerResource> RemoveExcessServers(int maximumReplicas)
    {
        if (_servers.Count <= maximumReplicas)
        {
            return [];
        }

        var removedServers = _servers.Skip(maximumReplicas).ToList();
        _servers.RemoveRange(maximumReplicas, _servers.Count - maximumReplicas);
        return removedServers;
    }
}
