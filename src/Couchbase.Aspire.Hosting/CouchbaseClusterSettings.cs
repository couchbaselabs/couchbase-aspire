using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Represents the settings for a Couchbase cluster.
/// </summary>
public class CouchbaseClusterSettings
{
    /// <summary>
    /// Edition of Couchbase Server.
    /// </summary>
    public CouchbaseEdition Edition { get; set; } = CouchbaseEdition.Enterprise;

    /// <summary>
    /// Static management port for the Couchbase cluster.
    /// </summary>
    public int? ManagementPort { get; set; }

    /// <summary>
    /// Static secure management port for the Couchbase cluster.
    /// </summary>
    public int? SecureManagementPort { get; set; }

    /// <summary>
    /// Per-node memory quotas for the Couchbase services.
    /// </summary>
    public CouchbaseMemoryQuotas? MemoryQuotas { get; set; }

    internal List<Action<IResourceBuilder<CouchbaseServerResource>>> ContainerConfigurationCallbacks { get; } = [];
}

/// <summary>
/// Per-node memory quotas for the Couchbase services.
/// </summary>
[DebuggerDisplay("{DebuggerToString(),nq}")]
public class CouchbaseMemoryQuotas
{
    public int DataServiceMegabytes { get; set; } = 1024;
    public int QueryServiceMegabytes { get; set; } = 1024;
    public int IndexServiceMegabytes { get; set; } = 1024;
    public int FtsServiceMegabytes { get; set; } = 1024;
    public int AnalyticsServiceMegabytes { get; set; } = 1024;
    public int EventingServiceMegabytes { get; set; } = 1024;

    private string DebuggerToString() =>
        $"Data = {DataServiceMegabytes}, Query = {QueryServiceMegabytes}, Index = {IndexServiceMegabytes}, Fts = {FtsServiceMegabytes}, Analytics = {AnalyticsServiceMegabytes}, Eventing = {EventingServiceMegabytes}";
}


/// <summary>
/// Represents a callback context for settings associated with a cluster.
/// </summary>
/// <param name="executionContext">The execution context for this invocation of the AppHost.</param>
/// <param name="settings">The settings associated with this execution.</param>
/// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>

public class CouchbaseClusterSettingsCallbackContext(
    DistributedApplicationExecutionContext executionContext,
    CouchbaseClusterSettings? settings = null,
    CancellationToken cancellationToken = default)
{
    private readonly IResource? _resource;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnvironmentCallbackContext"/> class.
    /// </summary>
    /// <param name="executionContext">The execution context for this invocation of the AppHost.</param>
    /// <param name="resource">The resource associated with this callback context.</param>
    /// <param name="settings">The settings associated with this execution.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public CouchbaseClusterSettingsCallbackContext(
        DistributedApplicationExecutionContext executionContext,
        IResource resource,
        CouchbaseClusterSettings? settings = null,
        CancellationToken cancellationToken = default)
        : this(executionContext, settings, cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        _resource = resource;
    }

    public CouchbaseClusterSettings Settings { get; set; } = settings ?? new();

    /// <summary>
    /// Gets the CancellationToken associated with the callback context.
    /// </summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>
    /// The resource associated with this callback context.
    /// </summary>
    /// <remarks>
    /// This will be set to the resource in all cases where .NET Aspire invokes the callback.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the CouchbaseMemoryQuotasCallbackContext was created without a specified resource.</exception>
    public IResource Resource => _resource ?? throw new InvalidOperationException($"{nameof(Resource)} is not set. This callback context is not associated with a resource.");

    /// <summary>
    /// Gets the execution context associated with this invocation of the AppHost.
    /// </summary>
    public DistributedApplicationExecutionContext ExecutionContext { get; } = executionContext ?? throw new ArgumentNullException(nameof(executionContext));
}

/// <summary>
/// Represents an annotation that provides a callback to modify the Couchbase cluster settings.
/// </summary>
public class CouchbaseClusterSettingsCallbackAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CouchbaseClusterSettingsCallbackAnnotation"/> class with the specified callback.
    /// </summary>
    /// <param name="callback">The callback to be invoked.</param>
    public CouchbaseClusterSettingsCallbackAnnotation(Action<CouchbaseClusterSettingsCallbackContext> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        Callback = context =>
        {
            callback(context);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CouchbaseClusterSettingsCallbackAnnotation"/> class with the specified callback.
    /// </summary>
    /// <param name="callback">The callback to be invoked.</param>
    public CouchbaseClusterSettingsCallbackAnnotation(Func<CouchbaseClusterSettingsCallbackContext, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        Callback = callback;
    }

    /// <summary>
    /// Gets or sets the callback action to be executed when the server is being built.
    /// </summary>
    public Func<CouchbaseClusterSettingsCallbackContext, Task> Callback { get; private set; }
}
