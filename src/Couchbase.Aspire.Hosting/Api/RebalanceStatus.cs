using System.Text.Json.Serialization;

namespace Couchbase.Aspire.Hosting.Api;

internal sealed class RebalanceStatus
{
    public const string StatusNone = "none";

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
