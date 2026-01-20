using Aspire;
using Couchbase.Compression.Snappier;
using Couchbase.Extensions.DependencyInjection;
using Couchbase.Extensions.Metrics.Otel;
using Couchbase.Extensions.Tracing.Otel.Tracing;
using Couchbase.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Couchbase.Aspire.Client;

/// <summary>
/// Extension methods for connection to a Couchbase cluster.
/// </summary>
public static class AspireCouchbaseExtensions
{
    private const string DefaultConfigSectionName = "Aspire:Couchbase:Client";

    /// <summary>
    /// Registers <see cref="IClusterProvider"/> as a singleton in the services provided by the <paramref name="builder"/>.
    /// Enables retries, corresponding health check, logging, and telemetry.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="connectionName">A name used to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="CouchbaseClientSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClusterOptions">An optional method that can be used for customizing the <see cref="ClusterOptions"/>. It's invoked after the options are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire:Couchbase:Client" section.</remarks>
    public static void AddCouchbaseClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<CouchbaseClientSettings>? configureSettings = null,
        Action<ClusterOptions>? configureClusterOptions = null)
        => AddCouchbaseClient(builder, configureSettings, configureClusterOptions, connectionName, serviceKey: null);

    /// <summary>
    /// Registers <see cref="IClusterProvider"/> as a keyed singleton for the given <paramref name="name"/> in the services provided by the <paramref name="builder"/>.
    /// Enables retries, corresponding health check, logging, and telemetry.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="name">The name of the component, which is used as the <see cref="ServiceDescriptor.ServiceKey"/> of the service and also to retrieve the connection string from the ConnectionStrings configuration section.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="CouchbaseClientSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClusterOptions">An optional method that can be used for customizing the <see cref="ClusterOptions"/>. It's invoked after the options are read from the configuration.</param>
    /// <remarks>Reads the configuration from "Aspire:Couchbase:Client:{name}" section.</remarks>
    public static void AddKeyedCouchbaseClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<CouchbaseClientSettings>? configureSettings = null,
        Action<ClusterOptions>? configureClusterOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        AddCouchbaseClient(builder, configureSettings, configureClusterOptions, connectionName: name, serviceKey: name);
    }

    private static void AddCouchbaseClient(
        IHostApplicationBuilder builder,
        Action<CouchbaseClientSettings>? configureSettings,
        Action<ClusterOptions>? configureClusterOptions,
        string connectionName,
        string? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        var configSection = builder.Configuration.GetSection(DefaultConfigSectionName);
        var namedConfigSection = configSection.GetSection(connectionName);

        var settings = new CouchbaseClientSettings();
        configSection.Bind(settings);
        namedConfigSection.Bind(settings);

        if (builder.Configuration.GetConnectionString(connectionName) is string connectionString)
        {
            settings.ApplyConnectionString(connectionString);
        }

        configureSettings?.Invoke(settings);

        void ConfigureClusterOptions(ClusterOptions options)
        {
            if (settings.DisableTracing)
            {
                options.TracingOptions.WithEnabled(false);
            }
            else
            {
                options.TracingOptions
                    .WithEnabled(true)
                    .WithTracer(new OpenTelemetryRequestTracer());
            }

            // Disable the built-in log-based metrics to avoid duplication with OpenTelemetry
            options.LoggingMeterOptions.Enabled(false);

            // Enable compression on the wire by default
            options.WithSnappyCompression();

            var configurationOptionsSection = configSection.GetSection("ClusterOptions");
            var namedConfigurationOptionsSection = namedConfigSection.GetSection("ClusterOptions");
            configurationOptionsSection.Bind(options);
            namedConfigurationOptionsSection.Bind(options);

            // the connection string from settings should win over the one from the ClusterOptions section
            if (!string.IsNullOrEmpty(settings.ConnectionString))
            {
                options.ConnectionString = settings.ConnectionString;
            }
            if (!string.IsNullOrEmpty(settings.Username))
            {
                options.UserName = settings.Username;
            }
            if (!string.IsNullOrEmpty(settings.Password))
            {
                options.Password = settings.Password;
            }

            configureClusterOptions?.Invoke(options);
        }

        if (serviceKey is null)
        {
            builder.Services.AddCouchbase(ConfigureClusterOptions);
            builder.Services.TryAddSingleton<ICouchbaseClientProvider>(sp =>
                new CouchbaseClientProvider(sp.GetRequiredService<IClusterProvider>(), settings.BucketName));
        }
        else
        {
            builder.Services.AddKeyedCouchbase(serviceKey, ConfigureClusterOptions);
            builder.Services.TryAddKeyedSingleton<ICouchbaseClientProvider>(serviceKey, (sp, serviceKey) =>
                new CouchbaseClientProvider(sp.GetRequiredKeyedService<IClusterProvider>(serviceKey), settings.BucketName));
        }

        if (!settings.DisableTracing)
        {
            builder.Services.AddOpenTelemetry()
                .WithTracing(traceBuilder =>
                    traceBuilder
                        .AddCouchbaseInstrumentation());
        }

        if (!settings.DisableMetrics)
        {
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metricBuilder =>
                    metricBuilder
                        .AddCouchbaseInstrumentation(options => options.ExcludeLegacyMetrics = true));
        }

        if (!settings.DisableHealthChecks)
        {
            // Build the requirements once to avoid rebuilding them on every health check
            var serviceRequirements = CouchbaseHealthCheck.CreateDefaultServiceRequirements();
            if (settings.HealthChecks is CouchbaseHealthCheckSettings healthCheckSettings)
            {
                // Overrides the defaults for minimum/maximum nodes if specified
                if (healthCheckSettings.MinimumHealthyNodes is { Count: > 0 })
                {
                    ApplyNodeRequirement(serviceRequirements, healthCheckSettings.MinimumHealthyNodes,
                        (requirement, value) => requirement.MinimumHealthyNodes = value);
                }

                if (healthCheckSettings.MaximumUnhealthyNodes is { Count: > 0 })
                {
                    ApplyNodeRequirement(serviceRequirements, healthCheckSettings.MaximumUnhealthyNodes,
                        (requirement, value) => requirement.MaximumUnhealthyNodes = value);
                }
            }

            builder.TryAddHealthCheck(new HealthCheckRegistration(
                serviceKey is null ? "Couchbase.Client" : $"Couchbase.Client_{connectionName}",
                sp =>
                {
                    try
                    {
                        // if the IClusterProvider can't be resolved, make a health check that will fail
                        var clusterProvider = serviceKey is null ? sp.GetRequiredService<IClusterProvider>() : sp.GetRequiredKeyedService<IClusterProvider>(serviceKey);

                        ValueTask<ICluster> ClusterFactory(CancellationToken ct)
                        {
                            var clusterTask = clusterProvider.GetClusterAsync();

                            // Avoid the expense of allocating a Task<T> on the heap if the ValueTask<T> is already complete
                            // or if the caller isn't providing a cancellation token. In both cases WaitAsync is unnecessary.
                            if (clusterTask.IsCompleted || !ct.CanBeCanceled)
                            {
                                return clusterTask;
                            }

                            return new ValueTask<ICluster>(clusterTask.AsTask().WaitAsync(ct));
                        }

                        CouchbaseHealthCheck healthCheck = settings.HealthChecks?.Type switch
                        {
                            CouchbaseHealthCheckType.Passive => new CouchbasePassiveHealthCheck(ClusterFactory),
                            _ => new CouchbaseActiveHealthCheck(ClusterFactory, settings.BucketName)
                        };

                        healthCheck.ServiceRequirements = serviceRequirements;

                        return healthCheck;
                    }
                    catch (Exception ex)
                    {
                        return new FailedHealthCheck(ex);
                    }
                },
                failureStatus: default,
                tags: default));
        }
    }

    private static void ApplyNodeRequirement(
        Dictionary<ServiceType, List<ICouchbaseServiceHealthRequirement>> serviceRequirements,
        Dictionary<ServiceType, int> values,
        Action<CouchbaseServiceHealthNodeRequirement, int> applyValue)
    {
        foreach (var service in values)
        {
            if (!serviceRequirements.TryGetValue(service.Key, out var requirements))
            {
                requirements = [];
                serviceRequirements.Add(service.Key, requirements);
            }

            var nodeRequirement = requirements.OfType<CouchbaseServiceHealthNodeRequirement>().FirstOrDefault();
            if (nodeRequirement is null)
            {
                nodeRequirement = new CouchbaseServiceHealthNodeRequirement();
                requirements.Add(nodeRequirement);
            }

            applyValue(nodeRequirement, service.Value);
        }
    }

    private sealed class FailedHealthCheck(Exception ex) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, exception: ex));
        }
    }
}
