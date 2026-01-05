using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging;

namespace Couchbase.Aspire.Hosting.Orchestration;

/// <summary>
/// Manages node certificates when using a custom certification authority.
/// </summary>
internal sealed class CouchbaseNodeCertificateProvider(
    ResourceLoggerService resourceLoggerService)
{
    /// <summary>
    /// If using a custom certification authority, generates and attaches a node certificate, private key,
    /// and the CA certificate to the Couchbase server container.
    /// </summary>
    /// <param name="server">Server to match with the node certificate.</param>
    /// <returns>Files to attach to the container.</returns>
    public IEnumerable<ContainerFileSystemItem> AttachNodeCertificate(CouchbaseServerResource server)
    {
        var certificationAuthority = server.Cluster.GetClusterCertificationAuthority();
        if (certificationAuthority?.Certificate is not X509Certificate2 caCertificate)
        {
            return [];
        }

        var logger = resourceLoggerService.GetLogger(server.Cluster);
        try
        {
            var (nodeCertificate, nodeKey) = CreateNodeCertificate(caCertificate, server.NodeName, logger);

            string rootCertificatePem = caCertificate.ExportCertificatePem();
            List<string> intermediateCertificatePems = [];

            var currentCertificate = caCertificate;
            while (currentCertificate.Issuer != currentCertificate.Subject)
            {
                // The certificate is not a root, self-signed certificate, advance to the parent
                currentCertificate = certificationAuthority.CertificateChain.FirstOrDefault(
                    p => p.Subject == currentCertificate.Issuer)
                    ?? throw new InvalidOperationException($"Could not find certificate '{currentCertificate.Issuer}' in the chain for cluster '{server.Cluster.Name}'.");

                intermediateCertificatePems.Add(rootCertificatePem);
                rootCertificatePem = currentCertificate.ExportCertificatePem();
            }

            return
            [
                new ContainerDirectory
                {
                    Name = "inbox",
                    Owner = 1000,
                    Group = 1000,
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserExecute,
                    Entries =
                    [
                        new ContainerDirectory
                        {
                            Name = "CA",
                            Mode = UnixFileMode.UserRead | UnixFileMode.UserExecute,
                            Entries =
                            [
                                new ContainerFile()
                                {
                                    Name = "ca.pem",
                                    Mode = UnixFileMode.UserRead,
                                    Contents = rootCertificatePem,
                                },
                            ]
                        },
                        new ContainerFile()
                        {
                            Name = "chain.pem",
                            Mode = UnixFileMode.UserRead,
                            Contents = string.Join('\n', [ nodeCertificate, ..intermediateCertificatePems, rootCertificatePem ]),
                        },
                        new ContainerFile()
                        {
                            Name = "pkey.key",
                            Mode = UnixFileMode.UserRead,
                            Contents = nodeKey,
                        },
                    ],
                },
            ];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating node certificate for '{NodeName}'.", server.NodeName);
            throw;
        }
    }

    private static (string certificateChain, string key) CreateNodeCertificate(X509Certificate2 caCertificate, string nodeName,
        ILogger logger)
    {
        var subjectName = new X500DistinguishedName($"CN={nodeName}");

        var privateKey = RSA.Create(keySizeInBits: 2048);
        var request = new CertificateRequest(
            subjectName,
            privateKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));

        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(caCertificate,
            includeKeyIdentifier: true, includeIssuerAndSerial: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([
            new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication")
        ], critical: false));

        var altNameBuilder = new SubjectAlternativeNameBuilder();
        altNameBuilder.AddDnsName(nodeName); // Internal within Docker
        altNameBuilder.AddDnsName("localhost"); // From the host machine
        request.CertificateExtensions.Add(altNameBuilder.Build());

        using var certificate = request.Create(caCertificate, caCertificate.NotBefore, caCertificate.NotAfter,
            Guid.NewGuid().ToByteArray());

        logger.LogInformation("Created node certificate for '{NodeName}'.", nodeName);

        return (certificate.ExportCertificatePem(), privateKey.ExportRSAPrivateKeyPem());
    }
}
