using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Couchbase.Aspire.Hosting.Api;
using Couchbase.KeyValue;
using Couchbase.Management.Buckets;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Extensions for building Couchbase bucket resources.
/// </summary>
public static class CouchbaseBucketBuilderExtensions
{
    /// <summary>
    /// Adds a bucket to the Couchbase cluster.
    /// </summary>
    /// <param name="builder">The cluster builder.</param>
    /// <param name="name">The name of the bucket resource.</param>
    /// <param name="bucketName">The name of the bucket. If <c>null</c>, the bucket name will default to the resource name.</param>
    /// <returns>The resource builder for the bucket.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> AddBucket(this IResourceBuilder<CouchbaseClusterResource> builder,
        [ResourceName] string name,
        string? bucketName = null) =>
        builder.AddBucket<CouchbaseBucketResource>(name, bucketName);

    /// <summary>
    /// Adds a sample bucket to the Couchbase cluster.
    /// </summary>
    /// <param name="builder">The cluster builder.</param>
    /// <param name="name">The name of the bucket resource.</param>
    /// <param name="bucketName">The name of the sample bucket. Typically <c>travel-sample</c>, <c>beer-sample</c>, or <c>gamesim-sample</c>.</param>
    /// <returns>The resource builder for the bucket.</returns>
    public static IResourceBuilder<CouchbaseSampleBucketResource> AddSampleBucket(this IResourceBuilder<CouchbaseClusterResource> builder,
        [ResourceName] string name,
        string bucketName)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucketName);

        return builder.AddBucket<CouchbaseSampleBucketResource>(name, bucketName);
    }

    /// <summary>
    /// Adds a bucket to the Couchbase cluster.
    /// </summary>
    /// <param name="builder">The cluster builder.</param>
    /// <param name="name">The name of the bucket resource.</param>
    /// <param name="bucketName">The name of the bucket. If <c>null</c>, the bucket name will default to the resource name.</param>
    /// <returns>The resource builder for the bucket.</returns>
    private static IResourceBuilder<T> AddBucket<T>(this IResourceBuilder<CouchbaseClusterResource> builder,
        [ResourceName] string name,
        string? bucketName = null)
        where T : CouchbaseBucketBaseResource, ICouchbaseBucketResource<T>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        bucketName ??= name;

        var bucket = T.Create(name, bucketName, builder.Resource);
        builder.Resource.AddBucket(name, bucket);

        string? connectionString = null;
        builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(bucket.Cluster, async (@event, ct) =>
        {
            // Use the URI, not the connection string, since it is applied directly to ClusterOptions
            // The URI doesn't include the Aspire extensions for authentication
            connectionString = await bucket.Cluster.UriExpression.GetValueAsync(ct).ConfigureAwait(false);

            if (connectionString is null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{bucket.Cluster.Name}' resource but the connection string was null.");
            }
        });

        var healthCheckKey = $"{name}_check";
        builder.ApplicationBuilder.Services.AddHealthChecks()
            .AddCouchbase(
                async (sp, ct) => {
                    var options = new ClusterOptions()
                        .WithConnectionString(connectionString ?? throw new InvalidOperationException("Connection string is unavailable"));

                    var cluster = bucket.Cluster;
                    options.UserName = await cluster.UserNameReference.GetValueAsync(ct).ConfigureAwait(false);
                    options.Password = await cluster.PasswordParameter.GetValueAsync(ct).ConfigureAwait(false);

                    var certificationAuthority = cluster.GetClusterCertificationAuthority();
                    if (certificationAuthority is { TrustCertificate: true })
                    {
                        options.WithX509CertificateFactory(certificationAuthority);
                    }

                    // Only need one connection per node for health checks
                    options.NumKvConnections = 1;
                    options.MaxKvConnections = 1;

                    return await Cluster.ConnectAsync(options).WaitAsync(ct).ConfigureAwait(false);
                },
                bucketNameFactory: _ => bucket.BucketName,
                serviceRequirementsFactory: _ => bucket.Cluster.GetHealthCheckServiceRequirements(),
                name: healthCheckKey);

        return builder.ApplicationBuilder
            .AddResource(bucket)
            .WithParentRelationship(builder)
            .WithIconName("Database")
            .WithHealthCheck(healthCheckKey)
            .WithInitialState(new()
            {
                ResourceType = "CouchbaseBucket",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.NotStarted,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, "Couchbase")
                ]
            })
            .WaitForStart(builder);
    }

    /// <summary>
    /// Add a synchronous callback to configure bucket settings.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="settingsCallback">Callback to customize the settings.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithSettings(this IResourceBuilder<CouchbaseBucketResource> builder, Action<CouchbaseBucketSettingsCallbackContext> settingsCallback)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settingsCallback);

        return builder.WithAnnotation(new CouchbaseBucketSettingsCallbackAnnotation(settingsCallback));
    }

    /// <summary>
    /// Add an asynchronous callback to configure bucket settings.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="settingsCallback">Callback to customize the settings.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithSettings(this IResourceBuilder<CouchbaseBucketResource> builder, Func<CouchbaseBucketSettingsCallbackContext, Task> settingsCallback)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settingsCallback);

        return builder.WithAnnotation(new CouchbaseBucketSettingsCallbackAnnotation(settingsCallback));
    }

    /// <summary>
    /// Sets the type of the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="bucketType">The type of the bucket.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithBucketType(this IResourceBuilder<CouchbaseBucketResource> builder, BucketType bucketType)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.BucketType = bucketType;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Sets the memory quota of the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="quotaMegabytes">The quota in megabytes. If <c>null</c>, defaults to 100MB.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithMemoryQuota(this IResourceBuilder<CouchbaseBucketResource> builder, int? quotaMegabytes)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.MemoryQuotaMegabytes = quotaMegabytes;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// sets the number of replicas for the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="replicas">The number of replicas. If <c>null</c>, defaults to the cluster's default, typically 1.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithReplicas(this IResourceBuilder<CouchbaseBucketResource> builder, int? replicas)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.Replicas = replicas;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Sets if flush is enabled for the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="enabled">If flush is enabled. If <c>null</c>, defaults to the cluster's default, typically <c>false</c>.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithFlushEnabled(this IResourceBuilder<CouchbaseBucketResource> builder, bool? enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithSettings(context =>
        {
            context.Settings.FlushEnabled = enabled;
            return Task.CompletedTask;
        });

        const string flushCommandName = "flush-bucket";
        if (enabled ?? false)
        {
            // Add a command to flush the bucket
            builder.WithCommand(flushCommandName, "Flush",
                async (context) =>
                {
                    var apiService = context.ServiceProvider.GetRequiredService<ICouchbaseApiService>();
                    var api = apiService.GetApi(builder.Resource.Cluster);

                    var server = builder.Resource.Cluster.GetPrimaryServer();
                    if (server is null)
                    {
                        return new ExecuteCommandResult
                        {
                            Success = false,
                            ErrorMessage = "No available server to flush the bucket."
                        };
                    }

                    await api.FlushBucketAsync(server, builder.Resource.BucketName, context.CancellationToken).ConfigureAwait(false);

                    // Wait for bucket to be healthy
                    while (true)
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();

                        var bucketInfo = await api.GetBucketAsync(server, builder.Resource.BucketName, context.CancellationToken).ConfigureAwait(false);
                        if (bucketInfo?.Nodes?.All(p => p.Status == BucketNode.HealthyStatus) ?? false)
                        {
                            break;
                        }

                        await Task.Delay(250, context.CancellationToken).ConfigureAwait(false);
                    }

                    return new ExecuteCommandResult { Success = true };
                },
                new CommandOptions
                {
                    UpdateState = context =>
                    {
                        var state = context.ResourceSnapshot.State?.Text;
                        return state == KnownResourceStates.Running
                            ? ResourceCommandState.Enabled
                            : ResourceCommandState.Hidden;
                    },
                    IconName = "DeleteLines",
                    IsHighlighted = true,
                    ConfirmationMessage = $"Flushing bucket '{builder.Resource.BucketName}', are you sure?",
                });
        }
        else
        {
            // Remove the command if it was previously added
            var existingCommand = builder.Resource.Annotations
                .OfType<ResourceCommandAnnotation>()
                .FirstOrDefault(p => p.Name == flushCommandName);

            if (existingCommand is not null)
            {
                builder.Resource.Annotations.Remove(existingCommand);
            }
        }

        return builder;
    }

    /// <summary>
    /// Sets the storage backend for the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="backend">The storage backend. If <c>null</c>, defaults to the cluster's default, typically <see cref="StorageBackend.Couchstore"/>.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    /// <remarks>
    /// Note that only <see cref="StorageBackend.Couchstore"/> is supported for Community Edition.
    /// </remarks>
    public static IResourceBuilder<CouchbaseBucketResource> WithStorageBackend(this IResourceBuilder<CouchbaseBucketResource> builder, StorageBackend? backend)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.StorageBackend = backend;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Sets the compression mode for the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="mode">The compression mode. If <c>null</c>, defaults to the cluster's default, typically <see cref="CompressionMode.Passive"/>.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithCompressionMode(this IResourceBuilder<CouchbaseBucketResource> builder, CompressionMode? mode)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.CompressionMode = mode;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Sets the conflict resolution type for the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="type">The conflict resolution type. If <c>null</c>, defaults to the cluster's default, typically <see cref="ConflictResolutionType.SequenceNumber"/>.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithConflictResolutionType(this IResourceBuilder<CouchbaseBucketResource> builder, ConflictResolutionType? type)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.ConflictResolutionType = type;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Sets the minimum durability level for the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="minimumLevel">The minimum durability level. If <c>null</c>, defaults to the cluster's default, typically <see cref="DurabilityLevel.None"/>.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithMinimumDurabilityLevel(this IResourceBuilder<CouchbaseBucketResource> builder, DurabilityLevel? minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.MinimumDurabilityLevel = minimumLevel;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Sets the eviction policy for the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="policyType">
    /// Eviction policy. If <c>null</c>, defaults to the cluster's default, typically <see cref="EvictionPolicyType.ValueOnly"/>
    /// for Couchbase buckets or <see cref="EvictionPolicyType.NoEviction"/> for ephemeral buckets.
    /// </param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithEvictionPolicy(this IResourceBuilder<CouchbaseBucketResource> builder, EvictionPolicyType? policyType)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.EvictionPolicy = policyType;
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Sets the maximum TTL for documents in the bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="maximumTtlSeconds">Time to live, in seconds. If <c>null</c>, defaults to the cluster's default, typically 0 (no expiration).</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithMaximumTimeToLive(this IResourceBuilder<CouchbaseBucketResource> builder, int? maximumTtlSeconds)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.MaximumTimeToLiveSeconds = maximumTtlSeconds;
            return Task.CompletedTask;
        });
    }
}
