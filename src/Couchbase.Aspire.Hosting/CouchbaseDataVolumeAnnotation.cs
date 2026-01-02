using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

internal sealed class CouchbaseDataVolumeAnnotation : IResourceAnnotation
{
    private const string DataVolumePath = "/opt/couchbase/var";

    public Func<IResourceBuilder<CouchbaseServerResource>, string?>? VolumeNameFactory { get; set; }

    public void ApplyToServer(IResourceBuilder<CouchbaseServerResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var name = VolumeNameFactory?.Invoke(builder) ?? VolumeNameGenerator.Generate(builder, suffix: "data");

        if (builder.Resource.TryGetAnnotationsOfType<ContainerMountAnnotation>(out var annotations))
        {
            var existingAnnotation = annotations.FirstOrDefault(p => p.Type == ContainerMountType.Volume && p.Target == DataVolumePath);
            if (existingAnnotation is not null)
            {
                if (existingAnnotation.Source == name)
                {
                    // No change required
                    return;
                }

                // Remove the old annotation so we can add the new one
                builder.Resource.Annotations.Remove(existingAnnotation);
            }
        }

        builder.WithVolume(name, DataVolumePath, isReadOnly: false);
    }
}
