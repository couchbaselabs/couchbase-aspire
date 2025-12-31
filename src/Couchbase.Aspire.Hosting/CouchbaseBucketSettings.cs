using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.KeyValue;
using Couchbase.Management.Buckets;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Settings associated with a Couchbase bucket.
/// </summary>
public class CouchbaseBucketSettings
{
    /// <summary>
    /// Gets or sets the type of bucket.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="BucketType.Couchbase"/>.
    /// </value>
    public BucketType BucketType { get; set; } = BucketType.Couchbase;

    /// <summary>
    /// Gets or sets the RAM quota for the bucket in megabytes.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to 100MB.
    /// </value>
    public int? MemoryQuotaMegabytes { get; set; }

    /// <summary>
    /// Gets or sets the number of replicas for the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically 1.
    /// </value>
    public int? Replicas { get; set; }

    /// <summary>
    /// Gets or sets if flush is enabled for the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically <c>false</c>.
    /// </value>
    public bool? FlushEnabled { get; set; }

    /// <summary>
    /// Gets or sets the storage backend for the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically <see cref="StorageBackend.Couchstore"/>.
    /// </value>
    /// <remarks>
    /// Note that only <see cref="StorageBackend.Couchstore"/> is supported for Community Edition.
    /// </remarks>
    public StorageBackend? StorageBackend { get; set; }

    /// <summary>
    /// Gets or sets the compression mode for the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically <see cref="CompressionMode.Passive"/>.
    /// </value>
    public CompressionMode? CompressionMode { get; set; }

    /// <summary>
    /// Gets or sets the conflict resolution type for the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically <see cref="ConflictResolutionType.SequenceNumber"/>.
    /// </value>
    public ConflictResolutionType? ConflictResolutionType { get; set; }

    /// <summary>
    /// Gets or sets the minimum durability level for the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically <see cref="DurabilityLevel.None"/>.
    /// </value>
    public DurabilityLevel? MinimumDurabilityLevel { get; set; }

    /// <summary>
    /// Gets or sets the eviction policy for the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically <see cref="EvictionPolicyType.ValueOnly"/> for
    /// Couchbase buckets or <see cref="EvictionPolicyType.NoEviction"/> for ephemeral buckets.
    /// </value>
    public EvictionPolicyType? EvictionPolicy { get; set; }

    /// <summary>
    /// Gets or sets the maximum TTL for documents in the bucket.
    /// </summary>
    /// <value>
    /// If <c>null</c>, defaults to the cluster's default, typically 0 (no expiration).
    /// </value>
    public int? MaximumTimeToLiveSeconds { get; set; }
}

/// <summary>
/// Represents a callback context for settings associated with a Couchbase bucket.
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
    /// Initializes a new instance of the <see cref="CouchbaseBucketSettingsCallbackContext"/> class.
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

    /// <summary>
    /// Settings associated with a Couchbase bucket.
    /// </summary>
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
/// Represents an annotation that provides a callback to modify the couchbase bucket settings.
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
    /// Gets or sets the callback action to be executed when the bucket is being built.
    /// </summary>
    public Func<CouchbaseBucketSettingsCallbackContext, Task> Callback { get; private set; }
}
