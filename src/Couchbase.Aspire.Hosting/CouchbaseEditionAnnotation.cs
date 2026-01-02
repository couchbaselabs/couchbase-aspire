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
                if (builder.Resource.GetEndpoints().Any(CouchbaseEndpointNames.IsSecureEndpoint))
                {
                    // Secure endpoints already exist
                    break;
                }

                var services = builder.Resource.Services;

                builder.WithEndpoint(targetPort: 18091, name: CouchbaseEndpointNames.ManagementSecure, scheme: "https");

                if (services.HasFlag(CouchbaseServices.Data))
                {
                    builder
                        .WithEndpoint(targetPort: 11207, name: CouchbaseEndpointNames.DataSecure, scheme: "couchbases")
                        .WithEndpoint(targetPort: 18092, name: CouchbaseEndpointNames.ViewsSecure, scheme: "https");
                }

                if (services.HasFlag(CouchbaseServices.Query))
                {
                    builder.WithEndpoint(targetPort: 18093, name: CouchbaseEndpointNames.QuerySecure, scheme: "https");
                }

                if (services.HasFlag(CouchbaseServices.Fts))
                {
                    builder.WithEndpoint(targetPort: 18094, name: CouchbaseEndpointNames.FtsSecure, scheme: "https");
                }

                if (services.HasFlag(CouchbaseServices.Analytics))
                {
                    builder.WithEndpoint(targetPort: 18095, name: CouchbaseEndpointNames.AnalyticsSecure, scheme: "https");
                }

                if (services.HasFlag(CouchbaseServices.Eventing))
                {
                    builder.WithEndpoint(targetPort: 18096, name: CouchbaseEndpointNames.EventingSecure, scheme: "https");
                }

                if (services.HasFlag(CouchbaseServices.Backup))
                {
                    builder.WithEndpoint(targetPort: 18097, name: CouchbaseEndpointNames.BackupSecure, scheme: "https");
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
