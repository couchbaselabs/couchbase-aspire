using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Couchbase.Aspire.Client;

/// <summary>
/// Provides a builder for configuring Couchbase client services in an Aspire application.
/// </summary>
/// <param name="hostBuilder">The <see cref="IHostApplicationBuilder"/> with which services are being registered.</param>
/// <param name="settings">The <see cref="CouchbaseClientSettings"/> to configure the Couchbase client.</param>
/// <param name="serviceKey">The service key used to register the <see cref="IClusterProvider"/> service, if any.</param>
public sealed class AspireCouchbaseClientBuilder(
    IHostApplicationBuilder hostBuilder,
    CouchbaseClientSettings settings,
    string? serviceKey)
{
    /// <summary>
    /// Gets the <see cref="IHostApplicationBuilder"/> with which services are being registered.
    /// </summary>
    public IHostApplicationBuilder HostBuilder { get; } = hostBuilder ?? throw new ArgumentNullException(nameof(hostBuilder));

    /// <summary>
    /// Gets the <see cref="CouchbaseClientSettings"/> used to configure the Couchbase client.
    /// </summary>
    public CouchbaseClientSettings Settings { get; } = settings;

    /// <summary>
    /// Gets the service key used to register the <see cref="IClusterProvider"/> service, if any.
    /// </summary>
    public string? ServiceKey { get; } = serviceKey;
}
