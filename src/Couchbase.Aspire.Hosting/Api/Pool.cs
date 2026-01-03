using System.Text.Json.Serialization;

namespace Couchbase.Aspire.Hosting.Api;

internal sealed class Pool
{
    [JsonPropertyName("nodes")]
    public List<Node> Nodes { get; set; } = null!;
}

internal sealed class Node
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = null!;
}
