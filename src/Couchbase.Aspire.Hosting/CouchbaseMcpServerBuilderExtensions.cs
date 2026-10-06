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
            // UriExpression resolves network=default for container consumers and omits credentials,
            // which the MCP server takes separately.
            .WithEnvironment("CB_CONNECTION_STRING", cluster.UriExpression)
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
}
