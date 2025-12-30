namespace Couchbase.Aspire.Client;

public sealed class CouchbaseClientSettings
{
    /// <summary>
    /// Gets or sets the connection string of the Couchbase server to connect to.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the username for authenticating with the Couchbase server.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the password for authenticating with the Couchbase server.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the Couchbase health check is disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableHealthChecks { get; set; }

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the OpenTelemetry tracing is disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableTracing { get; set; }

    /// <summary>
    /// Gets or sets a boolean value that indicates whether the OpenTelemetry metrics are disabled or not.
    /// </summary>
    /// <value>
    /// The default value is <see langword="false"/>.
    /// </value>
    public bool DisableMetrics { get; set; }
}
