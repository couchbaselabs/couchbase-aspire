using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Couchbase.Aspire.Hosting.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Hosting.Initialization;

internal interface ICouchbaseClusterInitializerFactory
{
    CouchbaseClusterInitializer Create(CouchbaseClusterResource resource, string httpClientName);
}

internal sealed class CouchbaseClusterInitializerFactory(IServiceProvider serviceProvider) : ICouchbaseClusterInitializerFactory
{
    public CouchbaseClusterInitializer Create(CouchbaseClusterResource resource, string httpClientName)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(httpClientName);

        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName);

        return new CouchbaseClusterInitializer(resource,
            new CouchbaseApi(resource, httpClient),
            serviceProvider.GetRequiredService<DistributedApplicationExecutionContext>(),
            serviceProvider.GetRequiredService<ResourceLoggerService>(),
            serviceProvider.GetRequiredService<ResourceNotificationService>(),
            serviceProvider.GetRequiredService<IDistributedApplicationEventing>());
    }
}
