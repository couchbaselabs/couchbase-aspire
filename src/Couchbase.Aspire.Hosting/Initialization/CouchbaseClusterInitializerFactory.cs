using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting.Api;

namespace Couchbase.Aspire.Hosting.Initialization;

internal interface ICouchbaseClusterInitializerFactory
{
    CouchbaseClusterInitializer Create(CouchbaseClusterInitializerResource resource, string httpClientName);
}

internal sealed class CouchbaseClusterInitializerFactory(
    IHttpClientFactory httpClientFactory,
    DistributedApplicationExecutionContext executionContext,
    ResourceLoggerService resourceLoggerService,
    ResourceNotificationService resourceNotificationService) : ICouchbaseClusterInitializerFactory
{
    public CouchbaseClusterInitializer Create(CouchbaseClusterInitializerResource resource, string httpClientName)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(httpClientName);

        return new CouchbaseClusterInitializer(resource,
            new CouchbaseApi(resource.Parent, httpClientFactory.CreateClient(httpClientName)),
            executionContext,
            resourceLoggerService,
            resourceNotificationService);
    }
}
