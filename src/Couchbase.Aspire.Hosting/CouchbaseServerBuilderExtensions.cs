using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

internal static class CouchbaseServerBuilderExtensions
{
    public static IResourceBuilder<CouchbaseServerResource> AddServer(this IResourceBuilder<CouchbaseServerGroupResource> builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var cluster = builder.Resource.Parent;

        var server = new CouchbaseServerResource(name, builder.Resource);
        builder.Resource.AddServer(name, server);

        var serverBuilder = builder.ApplicationBuilder.AddResource(server)
            .WithParentRelationship(builder)
            .WithImage(CouchbaseContainerImageTags.Image, CouchbaseContainerImageTags.Tag)
            .WithImageRegistry(CouchbaseContainerImageTags.Registry)
            .WithIconName("Server")
            .WithEndpoint(targetPort: 8091, name: CouchbaseEndpointNames.Management, scheme: "http")
            .WithEndpoint(targetPort: 18091, name: CouchbaseEndpointNames.ManagementSecure, scheme: "https")
            .WithUrlForEndpoint(CouchbaseEndpointNames.ManagementSecure, UrlDetailsOnly);

        var services = server.Services;
        if (services.HasFlag(CouchbaseServices.Data))
        {
            serverBuilder
                .WithEndpoint(targetPort: 11210, name: CouchbaseEndpointNames.Data, scheme: "couchbase")
                .WithEndpoint(targetPort: 11207, name: CouchbaseEndpointNames.DataSecure, scheme: "couchbases")
                .WithEndpoint(targetPort: 8092, name: CouchbaseEndpointNames.Views, scheme: "http")
                .WithEndpoint(targetPort: 18092, name: CouchbaseEndpointNames.ViewsSecure, scheme: "https")
                .WithUrlForEndpoint(CouchbaseEndpointNames.Data, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.DataSecure, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.Views, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.ViewsSecure, UrlDetailsOnly);
        }

        if (services.HasFlag(CouchbaseServices.Query))
        {
            serverBuilder
                .WithEndpoint(targetPort: 8093, name: CouchbaseEndpointNames.Query, scheme: "http")
                .WithEndpoint(targetPort: 18093, name: CouchbaseEndpointNames.QuerySecure, scheme: "https")
                .WithUrlForEndpoint(CouchbaseEndpointNames.Query, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.QuerySecure, UrlDetailsOnly);
        }

        if (services.HasFlag(CouchbaseServices.Fts))
        {
            serverBuilder
                .WithEndpoint(targetPort: 8094, name: CouchbaseEndpointNames.Fts, scheme: "http")
                .WithEndpoint(targetPort: 18094, name: CouchbaseEndpointNames.FtsSecure, scheme: "https")
                .WithUrlForEndpoint(CouchbaseEndpointNames.Fts, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.FtsSecure, UrlDetailsOnly);
        }

        if (services.HasFlag(CouchbaseServices.Analytics))
        {
            serverBuilder
                .WithEndpoint(targetPort: 8095, name: CouchbaseEndpointNames.Analytics, scheme: "http")
                .WithEndpoint(targetPort: 18095, name: CouchbaseEndpointNames.AnalyticsSecure, scheme: "https")
                .WithUrlForEndpoint(CouchbaseEndpointNames.Analytics, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.AnalyticsSecure, UrlDetailsOnly);
        }

        if (services.HasFlag(CouchbaseServices.Eventing))
        {
            serverBuilder
                .WithEndpoint(targetPort: 8096, name: CouchbaseEndpointNames.Eventing, scheme: "http")
                .WithEndpoint(targetPort: 18096, name: CouchbaseEndpointNames.EventingSecure, scheme: "https")
                .WithEndpoint(targetPort: 9140, name: CouchbaseEndpointNames.EventingDebug)
                .WithUrlForEndpoint(CouchbaseEndpointNames.Eventing, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.EventingSecure, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.EventingDebug, UrlDetailsOnly);
        }

        if (services.HasFlag(CouchbaseServices.Backup))
        {
            serverBuilder
                .WithEndpoint(targetPort: 8097, name: CouchbaseEndpointNames.Backup, scheme: "http")
                .WithEndpoint(targetPort: 18097, name: CouchbaseEndpointNames.BackupSecure, scheme: "https")
                .WithUrlForEndpoint(CouchbaseEndpointNames.Backup, UrlDetailsOnly)
                .WithUrlForEndpoint(CouchbaseEndpointNames.BackupSecure, UrlDetailsOnly);
        }

        return serverBuilder;
    }

    private static void UrlDetailsOnly(ResourceUrlAnnotation annotation) => annotation.DisplayLocation = UrlDisplayLocation.DetailsOnly;
}
