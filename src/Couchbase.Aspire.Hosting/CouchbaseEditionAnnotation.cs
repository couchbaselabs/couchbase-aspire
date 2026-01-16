using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

internal sealed class CouchbaseEditionAnnotation : IResourceAnnotation
{
    public CouchbaseEdition Edition { get; set; } = CouchbaseEdition.Enterprise;

    internal void ApplyToServer(IResourceBuilder<CouchbaseServerResource> builder) =>
        ApplyToServer(builder, Edition);

    internal static void ApplyToServer(IResourceBuilder<CouchbaseServerResource> builder, CouchbaseEdition edition)
    {
        switch (edition)
        {
            case CouchbaseEdition.Enterprise:
                var services = builder.Resource.GetCouchbaseServices();

                builder.WithEndpoint(CouchbaseEndpointNames.ManagementSecure, endpoint =>
                {
                    endpoint.Port = null; // Clear setting from CouchbasePortsAnnotation, it will be reapplied
                    endpoint.TargetPort = 18091;
                    endpoint.UriScheme = "https";
                });

                if (services.HasFlag(CouchbaseServices.Data))
                {
                    builder.WithEndpoint(CouchbaseEndpointNames.DataSecure, endpoint =>
                    {
                        endpoint.TargetPort = 11207;
                        endpoint.UriScheme = "couchbases";
                    });

                    builder.WithEndpoint(CouchbaseEndpointNames.ViewsSecure, endpoint =>
                    {
                        endpoint.TargetPort = 18092;
                        endpoint.UriScheme = "https";
                    });
                }
                else
                {
                    builder.WithoutEndpoints(CouchbaseEndpointNames.DataSecure, CouchbaseEndpointNames.ViewsSecure);
                }

                if (services.HasFlag(CouchbaseServices.Query))
                {
                    builder.WithEndpoint(CouchbaseEndpointNames.QuerySecure, endpoint =>
                    {
                        endpoint.TargetPort = 18093;
                        endpoint.UriScheme = "https";
                    });
                }
                else
                {
                    builder.WithoutEndpoints(CouchbaseEndpointNames.QuerySecure);
                }

                if (services.HasFlag(CouchbaseServices.Search))
                {
                    builder.WithEndpoint(CouchbaseEndpointNames.FtsSecure, endpoint =>
                    {
                        endpoint.TargetPort = 18094;
                        endpoint.UriScheme = "https";
                    });
                }
                else
                {
                    builder.WithoutEndpoints(CouchbaseEndpointNames.FtsSecure);
                }

                if (services.HasFlag(CouchbaseServices.Analytics))
                {
                    builder.WithEndpoint(CouchbaseEndpointNames.AnalyticsSecure, endpoint =>
                    {
                        endpoint.TargetPort = 18095;
                        endpoint.UriScheme = "https";
                    });
                }
                else
                {
                    builder.WithoutEndpoints(CouchbaseEndpointNames.AnalyticsSecure);
                }

                if (services.HasFlag(CouchbaseServices.Eventing))
                {
                    builder.WithEndpoint(CouchbaseEndpointNames.EventingSecure, endpoint =>
                    {
                        endpoint.TargetPort = 18096;
                        endpoint.UriScheme = "https";
                    });
                }
                else
                {
                    builder.WithoutEndpoints(CouchbaseEndpointNames.EventingSecure);
                }

                if (services.HasFlag(CouchbaseServices.Backup))
                {
                    builder.WithEndpoint(CouchbaseEndpointNames.BackupSecure, endpoint =>
                    {
                        endpoint.TargetPort = 18097;
                        endpoint.UriScheme = "https";
                    });
                }
                else
                {
                    builder.WithoutEndpoints(CouchbaseEndpointNames.BackupSecure);
                }
                break;

            default:
                // Community Edition does not support secure endpoints, remove any that exist
                var i = 0;
                while (i < builder.Resource.Annotations.Count)
                {
                    if (builder.Resource.Annotations[i] is EndpointAnnotation endpoint &&
                        CouchbaseEndpointNames.IsSecureEndpoint(endpoint))
                    {
                        builder.Resource.Annotations.RemoveAt(i);
                    }
                    else
                    {
                        i++;
                    }
                }
                break;
        }
    }
}
