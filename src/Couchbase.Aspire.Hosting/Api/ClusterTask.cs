using System.Text.Json.Serialization;

namespace Couchbase.Aspire.Hosting.Api;

internal sealed class ClusterTask
{
    [JsonPropertyName("task_id")]
    public string? TaskId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}
