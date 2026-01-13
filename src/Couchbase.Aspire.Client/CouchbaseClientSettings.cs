using System.Text.RegularExpressions;

namespace Couchbase.Aspire.Client;

public sealed partial class CouchbaseClientSettings
{
    private const string ConnectionStringRegexPattern = @"^(?<scheme>[^:]+)://(?:(?<username>[^\n@:]+)(?:\:(?<password>[^\n@]*))?@)?(?<hosts>[^\n?/]+)(?:/(?<bucket>[^\n?/]+)?)?(?:\?(?<params>.+))?";

#if NET8_0_OR_GREATER
    [GeneratedRegex(ConnectionStringRegexPattern, RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringRegex();
#else
    private static Regex? _connectionStringRegex;
    private static Regex ConnectionStringRegex() => _connectionStringRegex ??= new Regex(ConnectionStringRegexPattern, RegexOptions.CultureInvariant);
#endif

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
    /// Gets or sets the bucket name to connect to.
    /// </summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// Gets or sets settings related to health checks.
    /// </summary>
    public CouchbaseHealthCheckSettings? HealthChecks { get; set; }

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

    internal void ApplyConnectionString(string connectionString)
    {
        // Couchbase SDK doesn't handle username/password/bucket in the connection string, so extract them

        var match = ConnectionStringRegex().Match(connectionString);
        if (!match.Success)
        {
            ConnectionString = connectionString;
            return;
        }

        if (match.Groups["username"] is { Success: true, Value: string username })
        {
            Username = username;
        }
        if (match.Groups["password"] is { Success: true, Value: string password })
        {
            Password = password;
        }
        if (match.Groups["bucket"] is { Success: true, Value: string bucketName })
        {
            BucketName = bucketName;
        }

        if (match.Groups["params"] is { Success: true, Value: string queryParams })
        {
            ConnectionString = $"{match.Groups["scheme"].Value}://{match.Groups["hosts"].Value}?{queryParams}";
        }
        else
        {
            ConnectionString = $"{match.Groups["scheme"].Value}://{match.Groups["hosts"].Value}";
        }
    }
}
