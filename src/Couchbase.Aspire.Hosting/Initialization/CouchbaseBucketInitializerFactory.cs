using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Couchbase.Aspire.Hosting.Api;

namespace Couchbase.Aspire.Hosting.Initialization;

internal interface ICouchbaseBucketInitializerFactory
{
    CouchbaseBucketInitializer Create(CouchbaseBucketResource resource, string httpClientName);
}

internal sealed class CouchbaseBucketInitializerFactory(
    IHttpClientFactory httpClientFactory,
    DistributedApplicationExecutionContext executionContext,
    ResourceLoggerService resourceLoggerService,
    ResourceNotificationService resourceNotificationService,
    IDistributedApplicationEventing eventing)
    : ICouchbaseBucketInitializerFactory
{
    public CouchbaseBucketInitializer Create(CouchbaseBucketResource resource, string httpClientName)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(httpClientName);

        return new CouchbaseBucketInitializer(resource,
            new CouchbaseApi(resource.Parent, httpClientFactory.CreateClient(httpClientName)),
            executionContext,
            resourceLoggerService,
            resourceNotificationService,
            eventing);
    }
}
