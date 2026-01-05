using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Hosting.Api;

internal interface ICouchbaseApiService
{
    ICouchbaseApi GetApi(CouchbaseClusterResource cluster);
}

internal sealed class CouchbaseApiService(IHttpClientFactory httpClientFactory) : ICouchbaseApiService
{
    public ICouchbaseApi GetApi(CouchbaseClusterResource cluster) =>
        new CouchbaseApi(cluster, httpClientFactory.CreateClient(GetHttpClientName(cluster)));

    internal static void AddHttpClient(IServiceCollection services, CouchbaseClusterResource cluster)
    {
        services.AddHttpClient(GetHttpClientName(cluster))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                SslOptions =
                {
                    // Trust the CA certificate, applicable
                    RemoteCertificateValidationCallback =
                        cluster.GetClusterCertificationAuthority() is { TrustCertificate: true } annotation
                            ? annotation.CreateValidationCallback()
                            : null
                }
            })
            .RemoveAllLoggers();
    }

    private static string GetHttpClientName(CouchbaseClusterResource cluster) => $"{cluster.Name}-client";
}
