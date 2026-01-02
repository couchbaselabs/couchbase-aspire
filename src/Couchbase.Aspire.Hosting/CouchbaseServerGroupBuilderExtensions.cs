using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public static class CouchbaseServerGroupBuilderExtensions
{
    public static IResourceBuilder<CouchbaseServerGroupResource> AddServerGroup(this IResourceBuilder<CouchbaseClusterResource> builder,
        [ResourceName] string name,
        CouchbaseServices services = CouchbaseServices.Default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (services == CouchbaseServices.Default)
        {
            services = CouchbaseServices.Data | CouchbaseServices.Query | CouchbaseServices.Index;
        }

        var serverGroup = new CouchbaseServerGroupResource(name, builder.Resource, services);
        builder.Resource.AddServerGroup(name, serverGroup);

        var serverGroupBuilder = builder.ApplicationBuilder.AddResource(serverGroup)
            .WithInitialState(new()
            {
                ResourceType = "CouchbaseServerGroup",
                CreationTimeStamp = DateTime.UtcNow,
                State = KnownResourceStates.Active,
                Properties =
                [
                    new(CustomResourceKnownProperties.Source, "Couchbase")
                ]
            })
            .WithParentRelationship(builder)
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

            if (!builder.Resource.Parent.HasPrimaryServer())
            {
                // Find a new initial node
                var newPrimaryServer = builder.Resource.Parent.Servers.FirstOrDefault(p => p.Services.HasFlag(CouchbaseServices.Data));
                if (newPrimaryServer is not null)
                {
                    builder.ApplicationBuilder.CreateResourceBuilder(newPrimaryServer).WithPrimaryServerConfiguration();
                }
            }
        }
        else
        {
            for (int i=builder.Resource.Servers.Count; i < replicas; i++)
            {
                builder.AddServer($"{builder.Resource.Name}-{i}");
            }
        }

        return builder;
    }
}
