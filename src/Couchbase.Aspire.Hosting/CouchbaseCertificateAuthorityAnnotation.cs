using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Couchbase.Core.IO.Authentication.X509;

namespace Couchbase.Aspire.Hosting;

/// <summary>
/// Provides a custom certification authority for Couchbase clusters.
/// </summary>
public class CouchbaseCertificateAuthorityAnnotation(X509Certificate2 caCertificate) : IResourceAnnotation,
    ICertificateFactory
{
    /// <summary>
    /// CA certificate for the Couchbase cluster.
    /// </summary>
    public X509Certificate2 Certificate { get; } = caCertificate ?? throw new ArgumentNullException(nameof(caCertificate));

    /// <summary>
    /// Chain of additional certificates which support the CA certificate, up to and including the root certificate.
    /// Only required if the certificate is not a root certificate.
    /// </summary>
    public X509Certificate2Collection CertificateChain { get; } = [caCertificate];

    /// <summary>
    /// Gets or sets if the certificate must be explicitly trusted for initialization and health check operations.
    /// </summary>
    /// <value>
    /// Defaults to <c>false</c>.
    /// </value>
    public bool TrustCertificate { get; set; }

    X509Certificate2Collection ICertificateFactory.GetCertificates() => CertificateChain;

    internal RemoteCertificateValidationCallback CreateValidationCallback()
    {
        var certificate = Certificate;

        return (_, _, chain, errors) =>
        {
            if (errors == SslPolicyErrors.None)
            {
                // Valid based on default policies
                return true;
            }

            // Check for untrusted root but otherwise valid chain leading to our CA certificate
            return errors == SslPolicyErrors.RemoteCertificateChainErrors &&
                chain is not null &&
                chain.ChainStatus.All(s => s.Status is X509ChainStatusFlags.NoError or X509ChainStatusFlags.UntrustedRoot or X509ChainStatusFlags.PartialChain) &&
                chain.ChainElements.Any(p => p.Certificate.Thumbprint == certificate.Thumbprint);
        };
    }
}
