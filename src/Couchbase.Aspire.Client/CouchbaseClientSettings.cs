using System.Collections.Generic;

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
    /// Gets or sets a mapping of bucket logical names to physical bucket names.
    /// </summary>
    public Dictionary<string, string> BucketNameMap { get; set; } = [];

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

    internal void FillBucketNameMap(string? maps)
    {
        if (string.IsNullOrEmpty(maps))
        {
            return;
        }

        Span<Range> parts = stackalloc Range[2];

#if NET9_0_OR_GREATER
        var altLookup = BucketNameMap.GetAlternateLookup<ReadOnlySpan<char>>();
#endif

        foreach (var map in maps.Split(','))
        {
            var partCount = map.Split(parts, '=', StringSplitOptions.TrimEntries);
            if (partCount == 2)
            {
                var logicalName = map.AsSpan()[parts[0]];
                var physicalName = map.AsSpan()[parts[1]];
                if (!logicalName.IsEmpty)
                {
                    if (physicalName.IsEmpty)
                    {
#if NET9_0_OR_GREATER
                        altLookup.Remove(logicalName);
#else
                        BucketNameMap.Remove(logicalName.ToString());
#endif
                    }
                    else
                    {
#if NET9_0_OR_GREATER
                        altLookup[logicalName] = physicalName.ToString();
#else
                        BucketNameMap[logicalName.ToString()] = physicalName.ToString();
#endif
                    }
                }
            }
        }
    }
}
