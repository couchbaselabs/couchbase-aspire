using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Aspire.Hosting;
using Couchbase.KeyValue;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Extensions for managing Couchbase bucket indices.
/// </summary>
public static class CouchbaseBucketBuilderExtensions
{
    /// <summary>
    /// Adds an index manager to the Couchbase bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="name">The name of the index manager resource.</param>
    /// <returns>The Couchbase index manager builder.</returns>
    public static IResourceBuilder<CouchbaseIndexManagerResource> AddIndexManager(this IResourceBuilder<CouchbaseBucketResource> builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var indexManager = new CouchbaseIndexManagerResource(name, builder.Resource);

        return builder.ApplicationBuilder.AddResource(indexManager)
            .WithParentRelationship(builder)
            .WithImage(CouchbaseIndexManagerImageTags.Image, CouchbaseIndexManagerImageTags.Tag)
            .WithImageRegistry(CouchbaseIndexManagerImageTags.Registry)
            .WithIconName("Index")
            .WithArgs(context =>
            {
                var cluster = builder.Resource.Cluster;

                context.Args.Add("-c");
                context.Args.Add(cluster.UriExpression);
                context.Args.Add("-u");
                context.Args.Add(cluster.UserNameReference);
                context.Args.Add("-p");
                context.Args.Add(cluster.PasswordParameter);
                context.Args.Add("sync");
                context.Args.Add("-f");
                context.Args.Add(builder.Resource.BucketNameExpression);
            })
            .WaitFor(builder);
    }

    /// <summary>
    /// Adds index definitions to the Couchbase index manager.
    /// </summary>
    /// <param name="builder">The Couchbase index manager builder.</param>
    /// <param name="paths">List of relative paths to the index definitions.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseIndexManagerResource> WithIndices(this IResourceBuilder<CouchbaseIndexManagerResource> builder,
        params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Length == 0)
        {
            return builder;
        }

        var bindCount = builder.Resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var annotations)
            ? annotations.Count()
            : 0;

        var bindPaths = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var attr = File.GetAttributes(path);
                var bindPath = attr.HasFlag(FileAttributes.Directory)
                    ? $"/definitions/{bindCount}"
                    : $"/definitions/{bindCount}/{Path.GetFileName(path)}";

                builder.WithBindMount(path, bindPath, isReadOnly: true);
                bindPaths.Add(bindPath);
                bindCount++;
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Index definition path '{path}' is invalid.", ex);
            }
        }

        builder
            .WithArgs(context =>
            {
                foreach (var bindPath in bindPaths)
                {
                    context.Args.Add(bindPath);
                }
            });

        return builder;
    }

    /// <summary>
    /// Adds an index manager to the Couchbase bucket.
    /// </summary>
    /// <param name="builder">The bucket builder.</param>
    /// <param name="paths">List of relative paths to the index definitions.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseBucketResource> WithIndices(this IResourceBuilder<CouchbaseBucketResource> builder,
        params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Length > 0)
        {
            builder.AddIndexManager($"{builder.Resource.Name}-index-manager")
                .WithIndices(paths);
        }

        return builder;
    }
}
