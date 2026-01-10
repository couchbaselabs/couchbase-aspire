namespace Couchbase.HealthChecks;

/// <summary>
/// Type of health check to perform.
/// </summary>
public enum CouchbaseHealthCheckType
{
    /// <summary>
    /// An active ping to the Couchbase services. This is more robust but also slower and more resource intensive.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Passively observes the cluster diagnostics report. This is faster and less resource intensive, but may be less accurate.
    /// </summary>
    Passive
}
