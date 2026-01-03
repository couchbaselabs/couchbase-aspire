using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Couchbase.Aspire.Hosting.Api;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting.Initialization;

internal class CouchbaseBucketInitializer(
    CouchbaseBucketResource bucket,
    ICouchbaseApi api,
    DistributedApplicationExecutionContext executionContext,
    ResourceLoggerService resourceLoggerService,
    ResourceNotificationService resourceNotificationService,
    IDistributedApplicationEventing eventing)
{
    private readonly ILogger _logger = resourceLoggerService.GetLogger(bucket);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var exitCode = 0;

        try
        {
            // Wait for the cluster to start
            await resourceNotificationService.WaitForResourceAsync(bucket.Parent.Name, KnownResourceStates.Running, cancellationToken).ConfigureAwait(false);

            // Mark the bucket as starting
            await resourceNotificationService.PublishUpdateAsync(bucket, s => s with
            {
                StartTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Starting,
            });

            await InitializeBucketAsync(cancellationToken).ConfigureAwait(false);

            // Mark the bucket as running
            await resourceNotificationService.PublishUpdateAsync(bucket, s => s with
            {
                State = KnownResourceStates.Running,
            });

            _logger.LogInformation("Initialized Couchbase bucket '{BucketName}'.", bucket.BucketName);

            // Since this is a custom resource, we must publish these events manually to trigger URLs, connection strings,
            // and health checks.
            await eventing.PublishAsync(new ResourceEndpointsAllocatedEvent(bucket, executionContext.ServiceProvider), cancellationToken)
                .ConfigureAwait(false);
            await eventing.PublishAsync(new ConnectionStringAvailableEvent(bucket, executionContext.ServiceProvider), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to initialize Couchbase bucket '{BucketName}'.", bucket.BucketName);

            exitCode = 1;

            await resourceNotificationService.PublishUpdateAsync(bucket, s => s with
            {
                State = KnownResourceStates.Exited,
                ExitCode = exitCode
            });
        }
    }

    public async Task InitializeBucketAsync(CancellationToken cancellationToken = default)
    {
        var node = bucket.Parent.GetPrimaryServer();
        if (node is null)
        {
            throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
        }

        _logger.LogInformation("Creating bucket '{BucketName}'...", bucket.BucketName);

        var bucketExists = await api.GetBucketAsync(node, bucket.BucketName, cancellationToken).ConfigureAwait(false);
        if (bucketExists)
        {
            _logger.LogInformation("Bucket '{BucketName}' already exists.", bucket.BucketName);
            return;
        }

        var settings = await bucket.GetBucketSettingsAsync(executionContext, cancellationToken).ConfigureAwait(false);
        await api.CreateBucketAsync(node, bucket.BucketName, settings, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Created bucket '{BucketName}'.", bucket.BucketName);
    }
}
