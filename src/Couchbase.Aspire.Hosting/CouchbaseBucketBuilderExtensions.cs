using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Couchbase.Aspire.Hosting.Initialization;
using Couchbase.Management.Buckets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting;

public static class CouchbaseBucketBuilderExtensions
{
    public static IResourceBuilder<CouchbaseBucketResource> AddBucket(this IResourceBuilder<CouchbaseClusterResource> builder,
        [ResourceName] string name,
        string? bucketName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        bucketName ??= name;

        builder.Resource.AddBucket(name, bucketName);
        var bucket = new CouchbaseBucketResource(name, bucketName, builder.Resource);

        string? connectionString = null;
        builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(bucket.Parent, async (@event, ct) =>
        {
            connectionString = await bucket.Parent.ConnectionStringExpression.GetValueAsync(ct).ConfigureAwait(false);

            if (connectionString is null)
            {
                throw new DistributedApplicationException($"ConnectionStringAvailableEvent was published for the '{bucket.Parent.Name}' resource but the connection string was null.");
            }
        });

        var healthCheckKey = $"{name}_check";
        builder.ApplicationBuilder.Services.AddHealthChecks()
            .AddCouchbase(
                async (sp, ct) => {
                    var options = new ClusterOptions()
                        .WithConnectionString(connectionString ?? throw new InvalidOperationException("Connection string is unavailable"));

                    var cluster = bucket.Parent;
                    options.UserName = await cluster.UserNameReference.GetValueAsync(ct);
                    options.Password = await cluster.PasswordParameter.GetValueAsync(ct);

                    return await Cluster.ConnectAsync(options).WaitAsync(ct).ConfigureAwait(false);
                },
                bucketNameFactory: _ => bucket.BucketName,
                name: healthCheckKey);

        var httpClientName = $"{name}-initializer-client";
        builder.ApplicationBuilder.Services.AddHttpClient(httpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });

        return builder.ApplicationBuilder
            .AddResource(bucket)
            .WithParentRelationship(builder)
            .WithIconName("Database")
            .WithHealthCheck(healthCheckKey)
            .WithInitialState(new()
            {
                ResourceType = "CouchbaseBucket",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Waiting,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, "Couchbase")
                ]
            })
            .OnInitializeResource((resource, @event, ct) =>
            {
                _ = Task.Run(async () =>
                {
                    var logger = @event.Services.GetRequiredService<ResourceLoggerService>()
                        .GetLogger(resource);

                    try
                    {
                        var initializer = new CouchbaseBucketInitializer(
                            resource,
                            builder.ApplicationBuilder.ExecutionContext,
                            @event.Services.GetRequiredService<IHttpClientFactory>().CreateClient(httpClientName),
                            logger,
                            @event.Services.GetRequiredService<ResourceNotificationService>(),
                            @event.Eventing);

                        await initializer.InitializeAsync(ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "An error occurred while initializing the Couchbase bucket.");
                        throw;
                    }
                }, ct);

                return Task.CompletedTask;
            });
    }

    public static IResourceBuilder<CouchbaseBucketResource> WithSettings(this IResourceBuilder<CouchbaseBucketResource> builder, CouchbaseBucketSettings settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);

        return builder.WithAnnotation(new CouchbaseBucketSettingsCallbackAnnotation(context =>
        {
            context.Settings = settings;
            return Task.CompletedTask;
        }));
    }

    public static IResourceBuilder<CouchbaseBucketResource> WithSettings(this IResourceBuilder<CouchbaseBucketResource> builder, Action<CouchbaseBucketSettingsCallbackContext> settingsCallback)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settingsCallback);

        return builder.WithAnnotation(new CouchbaseBucketSettingsCallbackAnnotation(settingsCallback));
    }

    public static IResourceBuilder<CouchbaseBucketResource> WithSettings(this IResourceBuilder<CouchbaseBucketResource> builder, Func<CouchbaseBucketSettingsCallbackContext, Task> settingsCallback)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settingsCallback);

        return builder.WithAnnotation(new CouchbaseBucketSettingsCallbackAnnotation(settingsCallback));
    }

    public static IResourceBuilder<CouchbaseBucketResource> WithBucketType(this IResourceBuilder<CouchbaseBucketResource> builder, BucketType bucketType)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.BucketType = bucketType;
            return Task.CompletedTask;
        });
    }

    public static IResourceBuilder<CouchbaseBucketResource> WithMemoryQuota(this IResourceBuilder<CouchbaseBucketResource> builder, int? quotaMegabytes)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.MemoryQuotaMegabytes = quotaMegabytes;
            return Task.CompletedTask;
        });
    }

    public static IResourceBuilder<CouchbaseBucketResource> WithReplicas(this IResourceBuilder<CouchbaseBucketResource> builder, int? replicas)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithSettings(context =>
        {
            context.Settings.Replicas = replicas;
            return Task.CompletedTask;
        });
    }
}
