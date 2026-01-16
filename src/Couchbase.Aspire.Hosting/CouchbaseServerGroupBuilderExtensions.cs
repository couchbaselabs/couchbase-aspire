using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public static class CouchbaseServerGroupBuilderExtensions
{
    public static IResourceBuilder<CouchbaseServerGroupResource> AddServerGroup(this IResourceBuilder<CouchbaseClusterResource> builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var serverGroup = new CouchbaseServerGroupResource(name, builder.Resource);
        builder.Resource.AddServerGroup(name, serverGroup);

        var serverGroupBuilder = builder.ApplicationBuilder.AddResource(serverGroup)
            .WithInitialState(new()
            {
                ResourceType = "CouchbaseServerGroup",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.NotStarted,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, "Couchbase")
                ]
            })
            .WithIconName("ServerMultiple")
            .WithReplicas(1);

        return serverGroupBuilder;
    }

    public static IResourceBuilder<CouchbaseServerGroupResource> WithReplicas(this IResourceBuilder<CouchbaseServerGroupResource> builder, int replicas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithAnnotation(new ReplicaAnnotation(replicas), ResourceAnnotationMutationBehavior.Replace);

        var removedServers = builder.Resource.RemoveExcessServers(replicas);
        if (removedServers.Count > 0)
        {
            // Number of replicas was lowered
            foreach (var server in removedServers)
            {
                builder.ApplicationBuilder.Resources.Remove(server);
            }
        }
        else
        {
            for (int i=builder.Resource.Servers.Count; i < replicas; i++)
            {
                builder.AddServer($"{builder.Resource.Name}-{i}");
            }
        }

        builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource.Parent)
            .UpdatePrimaryServer();

        return builder;
    }

    /// <summary>
    /// Specify the Couchbase services to be enabled on servers in this server group.
    /// </summary>
    /// <param name="builder">Builder for the Couchbase server group.</param>
    /// <param name="services">The services to be enabled.</param>
    /// <returns>The <paramref name="builder"/>.</returns>
    public static IResourceBuilder<CouchbaseServerGroupResource> WithServices(this IResourceBuilder<CouchbaseServerGroupResource> builder,
        CouchbaseServices services)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (services == CouchbaseServices.Default)
        {
            services = CouchbaseServicesAnnotation.DefaultServices;
        }

        if (builder.Resource.TryGetLastAnnotation<CouchbaseServicesAnnotation>(out var existingAnnotation))
        {
            if (existingAnnotation.Services == services)
            {
                // No change
                return builder;
            }

            existingAnnotation.Services = services;
        }
        else
        {
            builder.WithAnnotation(new CouchbaseServicesAnnotation(services));
        }

        builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource.Parent)
            .UpdatePrimaryServer();

        foreach (var server in builder.Resource.Servers)
        {
            builder.ApplicationBuilder.CreateResourceBuilder(server)
                .ApplyDynamicConfiguration();
        }

        return builder;
    }
}
