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

        return builder.ApplicationBuilder.AddResource(server)
            .WithParentRelationship(builder)
            .WithImage(CouchbaseContainerImageTags.Image, CouchbaseContainerImageTags.Tag)
            .WithImageRegistry(CouchbaseContainerImageTags.Registry)
            .WithIconName("Server")
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
                            // Don't show secure URLs if the cluster isn't configured for TLS, the
                            // certificate won't be trusted.
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
                                // Don't show secure URLs if the cluster isn't configured for TLS, the
                                // certificate won't be trusted.
                                context.Urls.Remove(url);
                            }
                            break;
                    }
                }
            })
            .WithNodeCertificate()
            .ExcludeFromManifest()
            .ApplyDynamicConfiguration();
    }

    private static IResourceBuilder<CouchbaseServerResource> ApplyInsecureEndpoints(this IResourceBuilder<CouchbaseServerResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Add ports for insecure services only, secure services are added dynamically based on the
        // Couchbase edition in ApplyDynamicConfiguration.
        var services = builder.Resource.GetCouchbaseServices();

        builder.WithEndpoint(CouchbaseEndpointNames.Management, endpoint =>
        {
            endpoint.Port = null; // Clear setting from CouchbasePortsAnnotation, it will be reapplied
            endpoint.TargetPort = 8091;
            endpoint.UriScheme = "http";
        });

        if (services.HasFlag(CouchbaseServices.Data))
        {
            builder.WithEndpoint(CouchbaseEndpointNames.Data, endpoint =>
            {
                endpoint.TargetPort = 11210;
                endpoint.UriScheme = "couchbase";
            });
            builder.WithEndpoint(CouchbaseEndpointNames.Views, endpoint =>
            {
                endpoint.TargetPort = 8092;
                endpoint.UriScheme = "http";
            });
        }
        else
        {
            builder.WithoutEndpoints(CouchbaseEndpointNames.Data, CouchbaseEndpointNames.Views);
        }

        if (services.HasFlag(CouchbaseServices.Query))
        {
            builder.WithEndpoint(CouchbaseEndpointNames.Query, endpoint =>
            {
                endpoint.TargetPort = 8093;
                endpoint.UriScheme = "http";
            });
        }
        else
        {
            builder.WithoutEndpoints(CouchbaseEndpointNames.Query);
        }

        if (services.HasFlag(CouchbaseServices.Search))
        {
            builder.WithEndpoint(CouchbaseEndpointNames.Fts, endpoint =>
            {
                endpoint.TargetPort = 8094;
                endpoint.UriScheme = "http";
            });
        }
        else
        {
            builder.WithoutEndpoints(CouchbaseEndpointNames.Fts);
        }

        if (services.HasFlag(CouchbaseServices.Analytics))
        {
            builder.WithEndpoint(CouchbaseEndpointNames.Analytics, endpoint =>
            {
                endpoint.TargetPort = 8095;
                endpoint.UriScheme = "http";
            });
        }
        else
        {
            builder.WithoutEndpoints(CouchbaseEndpointNames.Analytics);
        }

        if (services.HasFlag(CouchbaseServices.Eventing))
        {
            builder.WithEndpoint(CouchbaseEndpointNames.Eventing, endpoint =>
            {
                endpoint.TargetPort = 8096;
                endpoint.UriScheme = "http";
            });
            builder.WithEndpoint(CouchbaseEndpointNames.EventingDebug, endpoint =>
            {
                endpoint.TargetPort = 9140;
            });
        }
        else
        {
            builder.WithoutEndpoints(CouchbaseEndpointNames.Eventing, CouchbaseEndpointNames.EventingDebug);
        }

        if (services.HasFlag(CouchbaseServices.Backup))
        {
            builder.WithEndpoint(CouchbaseEndpointNames.Backup, endpoint =>
            {
                endpoint.TargetPort = 8097;
                endpoint.UriScheme = "http";
            });
        }
        else
        {
            builder.WithoutEndpoints(CouchbaseEndpointNames.Backup);
        }

        return builder;
    }

    internal static IResourceBuilder<T> WithoutEndpoints<T>(this IResourceBuilder<T> builder,
        params ReadOnlySpan<string> endpointNames)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Resource.TryGetEndpoints(out var endpoints))
        {
            foreach (var name in endpointNames)
            {
                var endpoint = endpoints.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (endpoint is not null)
                {
                    builder.Resource.Annotations.Remove(endpoint);
                }
            }
        }

        return builder;
    }

    internal static IResourceBuilder<CouchbaseServerResource> ApplyDynamicConfiguration(this IResourceBuilder<CouchbaseServerResource> builder)
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

        builder.ApplyInsecureEndpoints();

        if (cluster.TryGetLastAnnotation<CouchbaseEditionAnnotation>(out var edition))
        {
            edition.ApplyToServer(builder);
        }
        else
        {
            // Apply the default edition
            CouchbaseEditionAnnotation.ApplyToServer(builder, CouchbaseEdition.Enterprise);
        }

        // Finally, apply any port overrides to the endpoints configured above
        if (builder.Resource.IsPrimaryServer())
        {
            if (cluster.TryGetLastAnnotation<CouchbasePortsAnnotation>(out var portsAnnotation))
            {
                portsAnnotation.ApplyToServer(builder);
            }
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
