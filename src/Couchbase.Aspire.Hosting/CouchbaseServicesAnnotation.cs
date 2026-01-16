using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Couchbase services which can be enabled on a cluster.
/// </summary>
[Flags]
public enum CouchbaseServices
{
    /// <summary>
    /// Key/value data service, query service, and index service.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Key/value data service.
    /// </summary>
    Data = 1,

    /// <summary>
    /// Query service.
    /// </summary>
    Query = 2,

    /// <summary>
    /// Index service.
    /// </summary>
    Index = 4,

    /// <summary>
    /// Full-text search service.
    /// </summary>
    Search = 8,

    /// <summary>
    /// Analytics service.
    /// </summary>
    Analytics = 16,

    /// <summary>
    /// Eventing service.
    /// </summary>
    Eventing = 32,

    /// <summary>
    /// Backup service.
    /// </summary>
    Backup = 64,
}

/// <summary>
/// Stores the services to enable on a Couchbase resource.
/// </summary>
/// <param name="services"></param>
public class CouchbaseServicesAnnotation(CouchbaseServices services) : IResourceAnnotation
{
    internal const CouchbaseServices DefaultServices = CouchbaseServices.Data | CouchbaseServices.Query | CouchbaseServices.Index;

    /// <summary>
    /// The services to enable.
    /// </summary>
    public CouchbaseServices Services { get; set; } = services;
}
