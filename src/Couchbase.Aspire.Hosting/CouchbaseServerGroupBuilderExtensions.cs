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
            .WithIconName("ServerMultiple");

        return serverGroupBuilder;
    }

    public static IResourceBuilder<CouchbaseServerGroupResource> WithReplicas(this IResourceBuilder<CouchbaseServerGroupResource> builder, int replicas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithAnnotation(new ReplicaAnnotation(replicas));
        return builder;
    }
}
