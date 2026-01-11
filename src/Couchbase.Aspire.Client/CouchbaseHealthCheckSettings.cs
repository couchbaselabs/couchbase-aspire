using Couchbase.HealthChecks;

namespace Couchbase.Aspire.Client;

public sealed class CouchbaseHealthCheckSettings
{
    /// <summary>
    /// Gets or sets the type of health check to perform.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="CouchbaseHealthCheckType.Active"/>.
    /// </value>
    public CouchbaseHealthCheckType Type { get; set; } = CouchbaseHealthCheckType.Active;

    /// <summary>
    /// Gets or sets the minimum number of healthy nodes required for each service.
    /// </summary>
    public Dictionary<ServiceType, int> MinimumHealthyNodes { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum number of unhealthy nodes allowed for each service.
    /// </summary>
    public Dictionary<ServiceType, int> MaximumUnhealthyNodes { get; set; } = [];
}
