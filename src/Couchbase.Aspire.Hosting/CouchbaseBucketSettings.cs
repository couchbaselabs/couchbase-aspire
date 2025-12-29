using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Management.Buckets;

namespace Couchbase.Aspire.Hosting;

public class CouchbaseBucketSettings
{
    public BucketType BucketType { get; set; } = BucketType.Couchbase;

    public int? MemoryQuotaMegabytes { get; set; }

    public int? Replicas { get; set; }
}

/// <summary>
/// Represents a callback context for quotas associated with a publisher.
/// </summary>
/// <param name="executionContext">The execution context for this invocation of the AppHost.</param>
/// <param name="settings">The settings associated with this execution.</param>
/// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>

public class CouchbaseBucketSettingsCallbackContext(
    DistributedApplicationExecutionContext executionContext,
    CouchbaseBucketSettings? settings = null,
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
    public CouchbaseBucketSettingsCallbackContext(
        DistributedApplicationExecutionContext executionContext,
        IResource resource,
        CouchbaseBucketSettings? settings = null,
        CancellationToken cancellationToken = default)
        : this(executionContext, settings, cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        _resource = resource;
    }

    public CouchbaseBucketSettings Settings { get; set; } = settings ?? new();

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
/// Represents an annotation that provides a callback to modify the couchbase memory quotas.
/// </summary>
public class CouchbaseBucketSettingsCallbackAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CouchbaseClusterSettingsCallbackAnnotation"/> class with the specified callback.
    /// </summary>
    /// <param name="callback">The callback to be invoked.</param>
    public CouchbaseBucketSettingsCallbackAnnotation(Action<CouchbaseBucketSettingsCallbackContext> callback)
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
    public CouchbaseBucketSettingsCallbackAnnotation(Func<CouchbaseBucketSettingsCallbackContext, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        Callback = callback;
    }

    /// <summary>
    /// Gets or sets the callback action to be executed when the server is being built.
    /// </summary>
    public Func<CouchbaseBucketSettingsCallbackContext, Task> Callback { get; private set; }
}
