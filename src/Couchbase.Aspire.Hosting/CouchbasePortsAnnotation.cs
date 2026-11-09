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

    /// <summary>
    /// Static data (KV) port for Couchbase data service.
    /// </summary>
    public int? DataPort { get; set; }

    /// <summary>
    /// Static secure data (KV) port for Couchbase data service when using TLS.
    /// </summary>
    public int? DataSecurePort { get; set; }

    internal void ApplyToServer(IResourceBuilder<CouchbaseServerResource> server)
    {
        server.WithEndpoint(CouchbaseEndpointNames.Management, endpoint => endpoint.Port = ManagementPort,
            createIfNotExists: false);
        server.WithEndpoint(CouchbaseEndpointNames.ManagementSecure, endpoint => endpoint.Port = SecureManagementPort,
            createIfNotExists: false);
        // Apply static ports for data endpoints if provided
        server.WithEndpoint(CouchbaseEndpointNames.Data, endpoint => endpoint.Port = DataPort,
            createIfNotExists: false);
        server.WithEndpoint(CouchbaseEndpointNames.DataSecure, endpoint => endpoint.Port = DataSecurePort,
            createIfNotExists: false);
    }
}
