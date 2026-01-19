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

internal sealed class SampleBucketResponse
{
    [JsonPropertyName("tasks")]
    public List<SampleBucketTask> Tasks { get; set; } = [];
}

internal sealed class SampleBucketTask
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }
}

internal sealed class ScopesResponse
{
    [JsonPropertyName("scopes")]
    public List<Scope> Scopes { get; set; } = [];
}

internal sealed class Scope
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("collections")]
    public List<Collection> Collections { get; set; } = [];
}

internal sealed class Collection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}
