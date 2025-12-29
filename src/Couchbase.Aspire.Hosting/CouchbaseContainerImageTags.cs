namespace Couchbase.Aspire.Hosting;

internal static class CouchbaseContainerImageTags
{
    /// <remarks>docker.io</remarks>
    public const string Registry = "docker.io";

    /// <remarks>library/couchbase</remarks>
    public const string Image = "library/couchbase";

    /// <remarks>8.0.0</remarks>
    public const string Tag = "8.0.0";

    /// <remarks>community-<inheritdoc cref="Tag"/></remarks>
    public const string CommunityTag = $"community-{Tag}";
}
