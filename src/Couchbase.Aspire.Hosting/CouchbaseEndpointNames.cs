using System.Collections.Frozen;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public static class CouchbaseEndpointNames
{
    private static readonly FrozenSet<string> s_SecureEndpointNames = new[]
    {
        ManagementSecure,
        DataSecure,
        ViewsSecure,
        QuerySecure,
        FtsSecure,
        AnalyticsSecure,
        EventingSecure,
        BackupSecure
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps endpoint names to their corresponding service names for alternate port mappings.
    /// </summary>
    internal static FrozenDictionary<string, string> EndpointNameServiceMappings { get; } =
        ((IEnumerable<KeyValuePair<string, string>>)[
            new(CouchbaseEndpointNames.Management, "mgmt"),
            new(CouchbaseEndpointNames.ManagementSecure, "mgmtSSL"),
            new(CouchbaseEndpointNames.Data, "kv"),
            new(CouchbaseEndpointNames.DataSecure, "kvSSL"),
            new(CouchbaseEndpointNames.Views, "capi"),
            new(CouchbaseEndpointNames.ViewsSecure, "capiSSL"),
            new(CouchbaseEndpointNames.Query, "n1ql"),
            new(CouchbaseEndpointNames.QuerySecure, "n1qlSSL"),
            new(CouchbaseEndpointNames.Fts, "fts"),
            new(CouchbaseEndpointNames.FtsSecure, "ftsSSL"),
            new(CouchbaseEndpointNames.Analytics, "cbas"),
            new(CouchbaseEndpointNames.AnalyticsSecure, "cbasSSL"),
            new(CouchbaseEndpointNames.Eventing, "eventingAdminPort"),
            new(CouchbaseEndpointNames.EventingSecure, "eventingSSL"),
            new(CouchbaseEndpointNames.EventingDebug, "eventingDebug"),
            new(CouchbaseEndpointNames.Backup, "backupAPI"),
            new(CouchbaseEndpointNames.BackupSecure, "backupAPIHTTPS"),
        ]).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public const string Analytics = "analytics";
    public const string AnalyticsSecure = "analytics-secure";

    public const string Backup = "backup";
    public const string BackupSecure = "backup-secure";

    public const string Data = "data";
    public const string DataSecure = "data-secure";

    public const string Eventing = "eventing";
    public const string EventingSecure = "eventing-secure";
    public const string EventingDebug = "eventingdebug";

    public const string Fts = "fts";
    public const string FtsSecure = "fts-secure";

    public const string Management = "management";
    public const string ManagementSecure = "management-secure";

    public const string Query = "query";
    public const string QuerySecure = "query-secure";

    public const string Views = "views";
    public const string ViewsSecure = "views-secure";

    public static bool IsSecureEndpoint(EndpointReference endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return IsSecureEndpoint(endpoint.EndpointName);
    }

    public static bool IsSecureEndpoint(EndpointAnnotation endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return IsSecureEndpoint(endpoint.Name);
    }

    private static bool IsSecureEndpoint(string endpointName) => s_SecureEndpointNames.Contains(endpointName);
}
