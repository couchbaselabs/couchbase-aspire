using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding a Couchbase MCP server to a Couchbase cluster.
/// </summary>
public static class CouchbaseMcpServerBuilderExtensions
{
    private const int McpPort = 9001;
    private const string McpPath = "/mcp";
    private const string CertificateDirectory = "/etc/couchbase-mcp/certs";
    private const string CaCertificateFileName = "ca.pem";

    /// <summary>
    /// Adds a Couchbase MCP server container connected to the cluster.
    /// </summary>
    /// <param name="builder">The cluster resource builder.</param>
    /// <param name="configureContainer">
    /// Optional callback to configure the container, for example to override the image, tag, registry,
    /// host port, or read-only mode.
    /// </param>
    /// <param name="containerName">
    /// Optional container name. Defaults to the cluster name with a "-mcp" suffix.
    /// </param>
    /// <returns>The cluster resource builder.</returns>
    /// <remarks>
    /// The container is registered with <c>WithMcpServer</c> on its default HTTP endpoint, so it is proxied
    /// through the Aspire MCP server. The host port is dynamic by default. Read-only mode is disabled by default.
    /// </remarks>
    /// <exception cref="InvalidOperationException">An MCP server was already added to this cluster.</exception>
    [Experimental("ASPIREMCP001")]
    public static IResourceBuilder<CouchbaseClusterResource> WithCouchbaseMcpServer(
        this IResourceBuilder<CouchbaseClusterResource> builder,
        Action<IResourceBuilder<CouchbaseMcpServerResource>>? configureContainer = null,
        string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var cluster = builder.Resource;

        if (builder.ApplicationBuilder.Resources.OfType<CouchbaseMcpServerResource>().Any(p => p.Cluster == cluster))
        {
            throw new InvalidOperationException($"An MCP server has already been added to Couchbase cluster '{cluster.Name}'.");
        }

        containerName ??= $"{cluster.Name}-mcp";

        var mcpResource = new CouchbaseMcpServerResource(containerName, cluster);

        var mcpBuilder = builder.ApplicationBuilder.AddResource(mcpResource)
            .WithImage(CouchbaseMcpServerImageTags.Image, CouchbaseMcpServerImageTags.Tag)
            .WithImageRegistry(CouchbaseMcpServerImageTags.Registry)
            .WithIconName("Bot")
            .WithHttpEndpoint(targetPort: McpPort)
            .WithMcpServer(path: McpPath)
            .WithEnvironment("CB_MCP_TRANSPORT", "http")
            .WithEnvironment("CB_MCP_HOST", "0.0.0.0")
            .WithEnvironment("CB_MCP_PORT", McpPort.ToString())
            .WithEnvironment("CB_MCP_READ_ONLY_MODE", "false")
            .WithEnvironment(context =>
            {
                // Built in a callback so the server list includes server groups added after this call.
                // UriExpression resolves network=default for container consumers and omits credentials,
                // which the MCP server takes separately.
                context.EnvironmentVariables["CB_CONNECTION_STRING"] = cluster.UriExpression;

                // Built in a callback so a certification authority added after this call is included.
                // The container does not use the host's trust store, so the CA is always supplied for TLS.
                if (cluster.GetClusterCertificationAuthority() is not null)
                {
                    context.EnvironmentVariables["CB_CA_CERT_PATH"] = $"{CertificateDirectory}/{CaCertificateFileName}";
                }
            })
            .WithContainerFiles(CertificateDirectory, (_, _) => Task.FromResult(GetCaCertificateFiles(cluster)))
            .WithEnvironment("CB_USERNAME", cluster.UserNameReference)
            .WithEnvironment("CB_PASSWORD", cluster.PasswordParameter)
            .WithParentRelationship(builder)
            .WaitFor(builder)
            .ExcludeFromManifest()
            .OnInitializeResource(async (resource, @event, _) =>
            {
                var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
                await notifications.PublishUpdateAsync(resource, s => s with { IsHidden = true }).ConfigureAwait(false);
            });

        configureContainer?.Invoke(mcpBuilder);

        return builder;
    }

    /// <summary>
    /// Configures whether the Couchbase MCP server runs in read-only mode.
    /// </summary>
    /// <param name="builder">The MCP server resource builder.</param>
    /// <param name="readOnly">True to block write operations. Defaults to true.</param>
    /// <returns>The MCP server resource builder.</returns>
    [Experimental("ASPIREMCP001")]
    public static IResourceBuilder<CouchbaseMcpServerResource> WithReadOnlyMode(
        this IResourceBuilder<CouchbaseMcpServerResource> builder,
        bool readOnly = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithEnvironment("CB_MCP_READ_ONLY_MODE", readOnly ? "true" : "false");
    }

    /// <summary>
    /// Returns the cluster's CA certificate and its chain as a PEM bundle, or nothing if the cluster does not use TLS.
    /// </summary>
    private static IEnumerable<ContainerFileSystemItem> GetCaCertificateFiles(CouchbaseClusterResource cluster)
    {
        if (cluster.GetClusterCertificationAuthority() is not { } certificationAuthority)
        {
            return [];
        }

        // The CA certificate is the first entry in the chain, followed by any intermediates and the root.
        // Only public certificates are exported, never the private key.
        var pem = string.Join('\n', certificationAuthority.CertificateChain
            .DistinctBy(p => p.Thumbprint)
            .Select(p => p.ExportCertificatePem()));

        return
        [
            new ContainerFile
            {
                Name = CaCertificateFileName,
                // Read-only. The MCP server image runs as a non-root user, so the public certificate must be world-readable.
                Mode = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                Contents = pem,
            },
        ];
    }
}
