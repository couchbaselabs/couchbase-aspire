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

internal sealed class NodeServices
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = null!;

    [JsonPropertyName("services")]
    public Dictionary<string, int>? Services { get; set; }

    [JsonPropertyName("thisNode")]
    public bool ThisNode { get; set; }
}

internal sealed class NodeServicesResponse
{
    [JsonPropertyName("nodesExt")]
    public List<NodeServices> NodesExt { get; set; } = null!;
}
