using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Hosting;

internal static class CouchbaseServerBuilderExtensions
{
    public static IResourceBuilder<CouchbaseServerResource> AddServer(this IResourceBuilder<CouchbaseServerGroupResource> builder,
        [ResourceName] string name, CouchbaseClusterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var isEnterprise = settings.Edition == CouchbaseEdition.Enterprise;

        var cluster = builder.Resource.Parent;

        var server = new CouchbaseServerResource(name, builder.Resource, settings.Edition);
        builder.Resource.AddServer(name, server);

        var serverBuilder = builder.ApplicationBuilder.AddResource(server)
            .WithParentRelationship(builder)
            .WithImage(CouchbaseContainerImageTags.Image, CouchbaseContainerImageTags.Tag)
            .WithImageRegistry(CouchbaseContainerImageTags.Registry)
            .WithIconName("Server")
            .WithEndpoint(targetPort: 8091, name: CouchbaseEndpointNames.Management, scheme: "http")
            .WithUrls(context =>
            {
                var isSecure = server.Cluster.GetClusterCertificationAuthority() is not null;

                foreach (var url in context.Urls.ToList())
                {
                    switch (url.Endpoint?.EndpointName)
                    {
                        case CouchbaseEndpointNames.Management when isSecure:
                            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
                            break;
                        case CouchbaseEndpointNames.ManagementSecure when !isSecure:
                            context.Urls.Remove(url);
                            break;
                        case CouchbaseEndpointNames.Data or CouchbaseEndpointNames.Views or CouchbaseEndpointNames.Query or
                             CouchbaseEndpointNames.Fts or CouchbaseEndpointNames.Analytics or CouchbaseEndpointNames.Eventing or
                             CouchbaseEndpointNames.EventingDebug or CouchbaseEndpointNames.Backup:
                            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
                            break;
                        case CouchbaseEndpointNames.DataSecure or CouchbaseEndpointNames.ViewsSecure or CouchbaseEndpointNames.QuerySecure or
                             CouchbaseEndpointNames.FtsSecure or CouchbaseEndpointNames.AnalyticsSecure or CouchbaseEndpointNames.EventingSecure or
                             CouchbaseEndpointNames.BackupSecure:
                            if (isSecure)
                            {
                                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
                            }
                            else
                            {
                                context.Urls.Remove(url);
                            }
                            break;
                    }
                }
            })
            .WithNodeCertificate();

        var services = server.Services;
        if (services.HasFlag(CouchbaseServices.Data))
        {
            serverBuilder
                .WithEndpoint(targetPort: 11210, name: CouchbaseEndpointNames.Data, scheme: "couchbase")
                .WithEndpoint(targetPort: 8092, name: CouchbaseEndpointNames.Views, scheme: "http");
        }

        if (services.HasFlag(CouchbaseServices.Query))
        {
            serverBuilder.WithEndpoint(targetPort: 8093, name: CouchbaseEndpointNames.Query, scheme: "http");
        }

        if (services.HasFlag(CouchbaseServices.Fts))
        {
            serverBuilder.WithEndpoint(targetPort: 8094, name: CouchbaseEndpointNames.Fts, scheme: "http");
        }

        if (services.HasFlag(CouchbaseServices.Analytics))
        {
            serverBuilder.WithEndpoint(targetPort: 8095, name: CouchbaseEndpointNames.Analytics, scheme: "http");
        }

        if (services.HasFlag(CouchbaseServices.Eventing))
        {
            serverBuilder
                .WithEndpoint(targetPort: 8096, name: CouchbaseEndpointNames.Eventing, scheme: "http")
                .WithEndpoint(targetPort: 9140, name: CouchbaseEndpointNames.EventingDebug);
        }

        if (services.HasFlag(CouchbaseServices.Backup))
        {
            serverBuilder.WithEndpoint(targetPort: 8097, name: CouchbaseEndpointNames.Backup, scheme: "http");
        }

        if (settings.Edition == CouchbaseEdition.Enterprise)
        {
            serverBuilder.WithEndpoint(targetPort: 18091, name: CouchbaseEndpointNames.ManagementSecure, scheme: "https");

            if (services.HasFlag(CouchbaseServices.Data))
            {
                serverBuilder
                    .WithEndpoint(targetPort: 11207, name: CouchbaseEndpointNames.DataSecure, scheme: "couchbases")
                    .WithEndpoint(targetPort: 18092, name: CouchbaseEndpointNames.ViewsSecure, scheme: "https");
            }

            if (services.HasFlag(CouchbaseServices.Query))
            {
                serverBuilder.WithEndpoint(targetPort: 18093, name: CouchbaseEndpointNames.QuerySecure, scheme: "https");
            }

            if (services.HasFlag(CouchbaseServices.Fts))
            {
                serverBuilder.WithEndpoint(targetPort: 18094, name: CouchbaseEndpointNames.FtsSecure, scheme: "https");
            }

            if (services.HasFlag(CouchbaseServices.Analytics))
            {
                serverBuilder.WithEndpoint(targetPort: 18095, name: CouchbaseEndpointNames.AnalyticsSecure, scheme: "https");
            }

            if (services.HasFlag(CouchbaseServices.Eventing))
            {
                serverBuilder.WithEndpoint(targetPort: 18096, name: CouchbaseEndpointNames.EventingSecure, scheme: "https");
            }

            if (services.HasFlag(CouchbaseServices.Backup))
            {
                serverBuilder.WithEndpoint(targetPort: 18097, name: CouchbaseEndpointNames.BackupSecure, scheme: "https");
            }
        }

        return serverBuilder;
    }

    private static IResourceBuilder<CouchbaseServerResource> WithNodeCertificate(this IResourceBuilder<CouchbaseServerResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.WithContainerFiles("/opt/couchbase/var/lib/couchbase", async (context, ct) =>
            {
                var provider = context.ServiceProvider.GetRequiredService<CouchbaseNodeCertificateProvider>();

                return provider.AttachNodeCertificate(builder.Resource);
            });
        }

        return builder;
    }
}
