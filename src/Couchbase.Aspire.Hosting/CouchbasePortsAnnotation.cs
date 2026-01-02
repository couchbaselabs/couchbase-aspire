using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

internal sealed class CouchbasePortsAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Static management port for the Couchbase cluster.
    /// </summary>
    public int? ManagementPort { get; set; }

    /// <summary>
    /// Static secure management port for the Couchbase cluster.
    /// </summary>
    public int? SecureManagementPort { get; set; }

    internal void ApplyToServer(IResourceBuilder<CouchbaseServerResource> server)
    {
        server.WithEndpoint(CouchbaseEndpointNames.Management, endpoint => endpoint.Port = ManagementPort,
            createIfNotExists: false);
        server.WithEndpoint(CouchbaseEndpointNames.ManagementSecure, endpoint => endpoint.Port = SecureManagementPort,
            createIfNotExists: false);
    }
}
