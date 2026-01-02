using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Couchbase.Aspire.Hosting;

internal sealed class CouchbaseContainerImageAnnotation : IResourceAnnotation
{
    public string? ImageRegistry { get; set; }

    public string? Image { get; set; }

    public string? ImageTag { get; set; }

    internal void ApplyToServer(IResourceBuilder<CouchbaseServerResource> builder)
    {
        if (ImageRegistry is not null)
        {
            builder.WithImageRegistry(ImageRegistry);
        }

        if (Image is not null)
        {
            builder.WithImage(Image, ImageTag);
        }
        else if (ImageTag is not null)
        {
            builder.WithImageTag(ImageTag);
        }
    }
}
