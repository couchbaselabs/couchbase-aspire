using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Couchbase.Aspire.Hosting.Orchestration;
using Microsoft.Extensions.DependencyInjection;

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
        builder.Resource.AddServer(server);

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
            .WithNodeCertificate()
            .ExcludeFromManifest();

        // Add ports for insecure services only, secure services are added dynamically based on the
        // Couchbase edition in WithClusterConfiguration

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

        // Apply common configuration from the cluster, including secure endpoints
        serverBuilder.WithClusterConfiguration();

        // This must be done after applying secure endpoints
        if (services.HasFlag(CouchbaseServices.Data) && !server.Cluster.HasPrimaryServer())
        {
            serverBuilder.WithPrimaryServerConfiguration();
        }

        return serverBuilder;
    }

    internal static IResourceBuilder<CouchbaseServerResource> WithPrimaryServerConfiguration(this IResourceBuilder<CouchbaseServerResource> builder)
    {
        builder.WithAnnotation<CouchbasePrimaryServerAnnotation>();

        if (builder.Resource.Cluster.TryGetLastAnnotation<CouchbasePortsAnnotation>(out var portsAnnotation))
        {
            portsAnnotation.ApplyToServer(builder);
        }

        return builder;
    }

    internal static IResourceBuilder<CouchbaseServerResource> WithClusterConfiguration(this IResourceBuilder<CouchbaseServerResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var cluster = builder.Resource.Cluster;
        if (cluster.TryGetLastAnnotation<ContainerLifetimeAnnotation>(out var containerLifetime))
        {
            builder.WithLifetime(containerLifetime.Lifetime);
        }

        if (cluster.TryGetLastAnnotation<CouchbaseContainerImageAnnotation>(out var image))
        {
            image.ApplyToServer(builder);
        }

        if (cluster.TryGetLastAnnotation<CouchbaseDataVolumeAnnotation>(out var dataVolume))
        {
            dataVolume.ApplyToServer(builder);
        }

        if (cluster.TryGetLastAnnotation<CouchbaseEditionAnnotation>(out var edition))
        {
            edition.ApplyToServer(builder);
        }
        else
        {
            // Apply the default edition
            CouchbaseEditionAnnotation.ApplyToServer(builder, CouchbaseEdition.Enterprise);
        }

        return builder;
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
