using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public class CouchbaseClusterResource : Resource, IResourceWithConnectionString, IResourceWithWaitSupport
{
    private const string DefaultUserName = "Administrator";

    public CouchbaseClusterResource(string name, ParameterResource? clusterName, ParameterResource? username,
        ParameterResource password) : base(name)
    {
        ArgumentNullException.ThrowIfNull(password);

        ClusterNameParameter = clusterName;
        UserNameParameter = username;
        PasswordParameter = password;
    }

    /// <summary>
    /// Gets the parameter that contains the Couchbase cluster name.
    /// </summary>
    public ParameterResource? ClusterNameParameter { get; }

    /// <summary>
    /// Gets a reference to the user name for the Couchbase server.
    /// </summary>
    /// <remarks>
    /// Returns the user name parameter if specified, otherwise returns the default user name "Administrator".
    /// </remarks>
    public ReferenceExpression ClusterNameReference =>
        ClusterNameParameter is not null ?
            ReferenceExpression.Create($"{ClusterNameParameter}") :
            ReferenceExpression.Create($"{Name}");

    /// <summary>
    /// Gets the parameter that contains the Couchbase server user name.
    /// </summary>
    public ParameterResource? UserNameParameter { get; }

    /// <summary>
    /// Gets a reference to the user name for the Couchbase server.
    /// </summary>
    /// <remarks>
    /// Returns the user name parameter if specified, otherwise returns the default user name "Administrator".
    /// </remarks>
    public ReferenceExpression UserNameReference =>
        UserNameParameter is not null ?
            ReferenceExpression.Create($"{UserNameParameter}") :
            ReferenceExpression.Create($"{DefaultUserName}");

    /// <summary>
    /// Gets the parameter that contains the Couchbase server password.
    /// </summary>
    public ParameterResource PasswordParameter { get; }

    /// <summary>
    /// Gets the connection URI expression for the Couchbase server.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression => BuildConnectionString();

    /// <summary>
    /// Gets the connection URI expression for the Couchbase server.
    /// </summary>
    /// <remarks>
    /// Format: <c>couchbase://{user}:{password}@{host}:{port}</c>.
    /// </remarks>
    public ReferenceExpression UriExpression => BuildConnectionString();

    internal ReferenceExpression BuildConnectionString(string? bucketName = null)
    {
        var useSsl = this.HasAnnotationOfType<CouchbaseCertificateAuthorityAnnotation>();

        var builder = new ReferenceExpressionBuilder();
        builder.AppendLiteral(useSsl ? "couchbases://" : "couchbase://");

        var servers = _serverGroups.Values
            .Where(p => p.Services.HasFlag(CouchbaseServices.Data))
            .SelectMany(p => p.Servers);

        var first = true;
        foreach (var server in servers)
        {
            var hostAndPort = useSsl ? server.DataSecureHostAndPort : server.DataHostAndPort;
            if (hostAndPort is not null)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    builder.AppendLiteral(",");
                }

                builder.AppendFormatted(hostAndPort);
            }
        }

        if (bucketName is not null)
        {
            builder.Append($"/{bucketName:uri}");
        }

        return builder.Build();
    }

    private readonly Dictionary<string, CouchbaseServerGroupResource> _serverGroups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A dictionary where the key is the resource name and the value is the bucket name.
    /// </summary>
    public IReadOnlyDictionary<string, CouchbaseServerGroupResource> ServerGroups => _serverGroups;

    internal void AddServerGroup(string name, CouchbaseServerGroupResource serverGroup)
    {
        _serverGroups.TryAdd(name, serverGroup);
    }

    public IEnumerable<CouchbaseServerResource> Servers => _serverGroups.Values.SelectMany(g => g.Servers);

    private readonly Dictionary<string, CouchbaseBucketResource> _buckets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A dictionary where the key is the resource name and the value is the bucket name.
    /// </summary>
    public IReadOnlyDictionary<string, CouchbaseBucketResource> Buckets => _buckets;

    internal void AddBucket(string name, CouchbaseBucketResource bucket)
    {
        _buckets.TryAdd(name, bucket);
    }

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Username", UserNameReference);
        yield return new("Password", ReferenceExpression.Create($"{PasswordParameter}"));
        yield return new("Uri", ConnectionStringExpression);

        if (_buckets.Count > 0)
        {
            var builder = new ReferenceExpressionBuilder();

            var first = true;
            foreach (var bucket in _buckets.Values)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    builder.AppendLiteral(",");
                }

                builder.AppendLiteral(bucket.Name);
                builder.AppendLiteral("=");
                builder.AppendFormatted(bucket.BucketNameExpression);
            }

            yield return new("BucketNameMap", builder.Build());
        }
    }
}
