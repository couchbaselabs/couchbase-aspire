using System.ComponentModel;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Couchbase index storage mode.
/// </summary>
public enum CouchbaseIndexStorageMode
{
    /// <summary>
    /// Uses Plasma storage, only compatible with Enterprise edition.
    /// </summary>
    [Description("plasma")]
    Plasma,

    /// <summary>
    /// Uses memory-optimized storage, only compatible with Enterprise edition.
    /// </summary>
    [Description("memory_optimized")]
    MemoryOptimized,

    /// <summary>
    /// Uses ForestDB storage, only compatible with Community edition.
    /// </summary>
    [Description("forestdb")]
    ForestDB
}

/// <summary>
/// Represents the index storage mode for a Couchbase cluster.
/// </summary>
public class CouchbaseIndexStorageModeAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Selected index storage mode. If <c>null</c>, uses the default based on the Couchbase Server edition.
    /// </summary>
    public CouchbaseIndexStorageMode? Mode { get; set; }
}
