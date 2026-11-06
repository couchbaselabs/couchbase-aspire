using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Represents a Couchbase MCP server container connected to a Couchbase cluster.
/// </summary>
/// <param name="name">The unique name of the resource instance.</param>
/// <param name="cluster">The parent Couchbase cluster resource.</param>
[Experimental("ASPIREMCP001")]
public class CouchbaseMcpServerResource(string name, CouchbaseClusterResource cluster)
    : ContainerResource(name)
{
    /// <summary>
    /// Gets the parent Couchbase cluster resource.
    /// </summary>
    public CouchbaseClusterResource Cluster { get; } = cluster ?? throw new ArgumentNullException(nameof(cluster));

    /// <summary>
    /// Gets the HTTP endpoint exposed by the MCP server.
    /// </summary>
    public EndpointReference McpEndpoint => field ??= new(this, "http");
}
