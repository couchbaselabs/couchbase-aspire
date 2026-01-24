using Couchbase;
using Couchbase.Aspire.Client;
using Couchbase.Aspire.Client.DistributedCaching;
using Couchbase.Extensions.Caching;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for adding Couchbase distributed caching services to an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class AspireCouchbaseDistributedCacheExtensions
{
    /// <summary>
    /// Adds Couchbase distributed caching services, <see cref="IDistributedCache"/> and <see cref="ICouchbaseCache"/>, in the services
    /// provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">A name used to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="CouchbaseClientSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClusterOptions">An optional method that can be used for customizing the <see cref="ClusterOptions"/>. It's invoked after the options are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire:Couchbase:Client" section.</remarks>
    public static void AddCouchbaseDistributedCache(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<CouchbaseClientSettings>? configureSettings = null,
        Action<ClusterOptions>? configureClusterOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        builder.AddCouchbaseClientBuilder(connectionName, configureSettings, configureClusterOptions)
            .WithDistributedCache();
    }

    /// <summary>
    /// Configures the Couchbase client to also provide distributed caching services through <see cref="IDistributedCache"/>
    /// and <see cref="ICouchbaseCache"/>.
    /// </summary>
    /// <param name="builder">The <see cref="AspireCouchbaseClientBuilder"/> to configure.</param>
    /// <param name="configureOptions">An optional method that can be used for customizing the <see cref="CouchbaseCacheOptions"/>.</param>
    /// <returns>The <see cref="AspireCouchbaseClientBuilder"/> for method chaining.</returns>
    /// <example>
    /// The following example creates an IDistributedCache service using the Couchbase client connection named "couchbase".
    /// <code lang="csharp">
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// builder.AddCouchbaseClientBuilder("couchbase")
    ///        .WithDistributedCache();
    /// </code>
    /// The created IDistributedCache service can then be resolved from an IServiceProvider:
    /// <code lang="csharp">
    /// IServiceProvider serviceProvider = builder.Services.BuildServiceProvider();
    ///
    /// var cache = serviceProvider.GetRequiredService&lt;IDistributedCache&gt;();
    /// </code>
    /// </example>
    public static AspireCouchbaseClientBuilder WithDistributedCache(
        this AspireCouchbaseClientBuilder builder,
        Action<CouchbaseCacheOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HostBuilder.Services.TryAddSingleton<ICouchbaseCacheBucketProvider>(sp =>
        {
            // Resolve the appropriate IBucketProvider based on the service key

            var key = builder.ServiceKey;
            var bucketProvider = key is null
                ? sp.GetRequiredService<IBucketProvider>()
                : sp.GetRequiredKeyedService<IBucketProvider>(key);

            var options = sp.GetRequiredService<IOptions<CouchbaseCacheOptions>>();

            return new AspireCouchbaseCacheBucketProvider(bucketProvider, options);
        });

        builder.HostBuilder.Services.AddDistributedCouchbaseCache(options =>
        {
            // Default to the bucket name from the Couchbase client settings, if provided
            if (builder.Settings.BucketName is string bucketName)
            {
                options.BucketName = bucketName;
            }

            // Allow further configuration
            configureOptions?.Invoke(options);
        });

        builder.HostBuilder.Services.AddSingleton<IHybridCacheSerializerFactory, CouchbaseCacheSerializerFactory>();

        return builder;
    }

#if NET9_0_OR_GREATER

    /// <summary>
    /// Adds Couchbase distributed caching services, <see cref="IDistributedCache"/> and <see cref="ICouchbaseCache"/>, and
    /// hybrid caching services, <see cref="HybridCache"/>, in the services provided by the <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">A name used to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="CouchbaseClientSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClusterOptions">An optional method that can be used for customizing the <see cref="ClusterOptions"/>. It's invoked after the options are read from the configuration.</param>
    /// <param name="configureHybridCacheOptions">An optional method that can be used for customizing the <see cref="HybridCacheOptions"/>.</param>
    /// <remarks>Reads the configuration from "Aspire:Couchbase:Client" section.</remarks>
    public static void AddCouchbaseHybridCache(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<CouchbaseClientSettings>? configureSettings = null,
        Action<ClusterOptions>? configureClusterOptions = null,
        Action<HybridCacheOptions>? configureHybridCacheOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        builder.AddCouchbaseClientBuilder(connectionName, configureSettings, configureClusterOptions)
            .WithHybridCache();
    }

    /// <summary>
    /// Configures the Couchbase client to also provide distributed caching services through <see cref="IDistributedCache"/>
    /// and <see cref="ICouchbaseCache"/> and hybrid caching through <see cref="HybridCache"/>.
    /// </summary>
    /// <param name="builder">The <see cref="AspireCouchbaseClientBuilder"/> to configure.</param>
    /// <param name="configureDistributedCacheOptions">An optional method that can be used for customizing the <see cref="CouchbaseCacheOptions"/>.</param>
    /// <param name="configureHybridCacheOptions">An optional method that can be used for customizing the <see cref="HybridCacheOptions"/>.</param>
    /// <returns>The <see cref="AspireCouchbaseClientBuilder"/> for method chaining.</returns>
    /// <example>
    /// The following example creates an IDistributedCache and HybridCache service using the Couchbase client connection named "couchbase".
    /// <code lang="csharp">
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// builder.AddCouchbaseClientBuilder("couchbase")
    ///        .WithHybridCache();
    /// </code>
    /// The created HybridCache service can then be resolved from an IServiceProvider:
    /// <code lang="csharp">
    /// IServiceProvider serviceProvider = builder.Services.BuildServiceProvider();
    ///
    /// var cache = serviceProvider.GetRequiredService&lt;HybridCache&gt;();
    /// </code>
    /// </example>
    public static AspireCouchbaseClientBuilder WithHybridCache(
        this AspireCouchbaseClientBuilder builder,
        Action<CouchbaseCacheOptions>? configureDistributedCacheOptions = null,
        Action<HybridCacheOptions>? configureHybridCacheOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithDistributedCache(configureDistributedCacheOptions);

        builder.HostBuilder.Services
            .AddHybridCache(options =>
            {
                options.MaximumKeyLength = 250; // Maximum Couchbase key size
                options.MaximumPayloadBytes = 20 * 1024 * 1024; // Maximum 20MB Couchbase document size
                options.DisableCompression = true; // Prefer Snappy compression built into the Couchbase SDK

                configureHybridCacheOptions?.Invoke(options);
            })
            .AddSerializerFactory<CouchbaseCacheSerializerFactory>();

        return builder;
    }

#endif
}
