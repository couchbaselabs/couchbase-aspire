using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Couchbase.Aspire.Hosting;
using Couchbase.KeyValue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting.Initialization;

internal class CouchbaseBucketInitializer(
    CouchbaseBucketResource bucket,
    DistributedApplicationExecutionContext executionContext,
    ILogger logger,
    ResourceNotificationService resourceNotificationService,
    IDistributedApplicationEventing eventing)
{
    private const int DefaultMemoryQuotaMegabytes = 100;

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

            logger.LogInformation("Initialized Couchbase bucket '{BucketName}'.", bucket.BucketName);

            // Since this is a custom resource, we must publish these events manually to trigger URLs, connection strings,
            // and health checks.
            await eventing.PublishAsync(new ResourceEndpointsAllocatedEvent(bucket, executionContext.ServiceProvider), cancellationToken)
                .ConfigureAwait(false);
            await eventing.PublishAsync(new ConnectionStringAvailableEvent(bucket, executionContext.ServiceProvider), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to initialize Couchbase bucket '{BucketName}'.", bucket.BucketName);

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
        var node = bucket.Parent.Servers.Where(CouchbaseResourceExtensions.IsInitialNode).FirstOrDefault();
        if (node is null)
        {
            throw new InvalidOperationException("Couchbase cluster must have at least one server with the data service.");
        }

        logger.LogInformation("Creating bucket '{BucketName}'...", bucket.BucketName);

        var initializer = bucket.Parent.GetClusterInitializerResource()?.GetClusterInitializer(executionContext.ServiceProvider);
        if (initializer is null)
        {
            throw new InvalidOperationException("Couchbase cluster initializer annotation not found.");
        }

        var endpoint = node.GetManagementEndpoint();

        var response = await initializer.SendRequestAsync(endpoint,
            HttpMethod.Get,
            $"/pools/default/buckets/{bucket.BucketName}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            // We're expecting 404 not found. If 200 OK the bucket already exists and we can exit.
            await CouchbaseClusterInitializer.ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Bucket '{BucketName}' already exists.", bucket.BucketName);
            return;
        }

        // Create the bucket

        var settings = await bucket.GetBucketSettingsAsync(executionContext, cancellationToken).ConfigureAwait(false);

        var dictionary = new Dictionary<string, string?>
        {
            { "name", bucket.BucketName },
            { "bucketType", GetEnumValueString(settings.BucketType) },
            { "ramQuota", (settings.MemoryQuotaMegabytes ?? DefaultMemoryQuotaMegabytes).ToString(CultureInfo.InvariantCulture) }
        };

        if (settings.Replicas is int replicas)
        {
            dictionary.Add("replicaNumber", replicas.ToString(CultureInfo.InvariantCulture));
        }
        if (settings.FlushEnabled is bool flushEnabled)
        {
            dictionary.Add("flushEnabled", flushEnabled ? "1" : "0");
        }
        if (settings.StorageBackend is Management.Buckets.StorageBackend storageBackend)
        {
            dictionary.Add("storageBackend", GetEnumValueString(storageBackend));
        }
        if (settings.CompressionMode is Management.Buckets.CompressionMode compressionMode)
        {
            dictionary.Add("compressionMode", GetEnumValueString(compressionMode));
        }
        if (settings.ConflictResolutionType is Management.Buckets.ConflictResolutionType conflictResolutionType)
        {
            dictionary.Add("conflictResolutionType", GetEnumValueString(conflictResolutionType));
        }
        if (settings.MinimumDurabilityLevel is DurabilityLevel durabilityLevel)
        {
            dictionary.Add("durabilityMinLevel", GetEnumValueString(durabilityLevel));
        }
        if (settings.EvictionPolicy is Management.Buckets.EvictionPolicyType evictionPolicy)
        {
            dictionary.Add("evictionPolicy", GetEnumValueString(evictionPolicy));
        }
        if (settings.MaximumTimeToLiveSeconds is int maxTtl)
        {
            dictionary.Add("maxTTL", maxTtl.ToString(CultureInfo.InvariantCulture));
        }

        response = await initializer.SendRequestAsync(endpoint,
            HttpMethod.Post,
            "/pools/default/buckets",
            new FormUrlEncodedContent(dictionary),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await CouchbaseClusterInitializer.ThrowOnFailureAsync(response, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Created bucket '{BucketName}'.", bucket.BucketName);
    }

    private static string GetEnumValueString<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var valueString = Enum.GetName(value);
        if (valueString is not null)
        {
            var fieldInfo = typeof(TEnum).GetField(valueString, BindingFlags.Public | BindingFlags.Static);
            if (fieldInfo is not null)
            {
                var attribute = fieldInfo.GetCustomAttribute<DescriptionAttribute>();
                if (!string.IsNullOrEmpty(attribute?.Description))
                {
                    return attribute.Description;
                }
            }
        }

        return (valueString ?? value.ToString()).ToLowerInvariant();
    }
}
