using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

public class CouchbaseServerResource : ContainerResource, IResourceWithEnvironment, IResource
{
    public CouchbaseServerResource(string name, CouchbaseServerGroupResource parent) : base(name)
    {
        ArgumentNullException.ThrowIfNull(parent);

        Parent = parent;

        ManagementEndpoint = new EndpointReference(this, CouchbaseEndpointNames.Management);
        ManagementSecureEndpoint = new EndpointReference(this, CouchbaseEndpointNames.ManagementSecure);

        if (parent.Services.HasFlag(CouchbaseServices.Data))
        {
            DataEndpoint = new EndpointReference(this, CouchbaseEndpointNames.Data);
            DataSecureEndpoint = new EndpointReference(this, CouchbaseEndpointNames.DataSecure);
        }
    }

    public CouchbaseServerGroupResource Parent { get; }

    public CouchbaseClusterResource Cluster => Parent.Parent;

    public CouchbaseServices Services => Parent.Services;

    // There is currently no public API to get the node name, the value resolution process
    // simply hangs. The APIs used for resolving injected environment variables, where this value
    // is built, aren't public.
    public string NodeName => $"{Name}.dev.internal";

    public EndpointReference ManagementEndpoint { get; }

    public EndpointReference ManagementSecureEndpoint { get; }

    public EndpointReference? DataEndpoint { get; }

    public EndpointReference? DataSecureEndpoint { get; }

    /// <summary>
    /// Gets the host and port endpoint reference for this resource.
    /// </summary>
    public EndpointReferenceExpression Host => ManagementEndpoint.Property(EndpointProperty.Host);

    /// <summary>
    /// Gets the data host and port endpoint reference for this resource.
    /// </summary>
    public EndpointReferenceExpression? DataHostAndPort => DataEndpoint?.Property(EndpointProperty.HostAndPort);

    /// <summary>
    /// Gets the secure data host and port endpoint reference for this resource.
    /// </summary>
    public EndpointReferenceExpression? DataSecureHostAndPort => DataSecureEndpoint?.Property(EndpointProperty.HostAndPort);
}
