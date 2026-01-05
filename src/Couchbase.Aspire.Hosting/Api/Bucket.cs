using System.Text.Json.Serialization;

namespace Couchbase.Aspire.Hosting.Api;

internal sealed class Bucket
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("nodes")]
    public List<BucketNode>? Nodes { get; set; }
}

internal sealed class BucketNode
{
    public const string HealthyStatus = "healthy";

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
