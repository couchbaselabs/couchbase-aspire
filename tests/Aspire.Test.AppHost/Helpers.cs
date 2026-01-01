using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Aspire.Test.AppHost;

internal static class Helpers
{
    public static X509Certificate2 CreateCACertificate(string name, X509Certificate2? issuer = null)
    {
        // This wouldn't typically be done by a real Aspire use case because trusting the certificate
        // becomes problematic. However, for testing purposes we can create a self-signed CA certificate.

        var subjectName = new X500DistinguishedName($"CN={name}");

        var privateKey = RSA.Create(keySizeInBits: 2048);
        var request = new CertificateRequest(
            subjectName,
            privateKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            critical: true));

        var subjectIdentifier = new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false);
        request.CertificateExtensions.Add(subjectIdentifier);

        if (issuer is null)
        {
            var now = DateTimeOffset.UtcNow;
            return request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        }
        else
        {
            using var certificate = request.Create(issuer, issuer.NotBefore, issuer.NotAfter, Guid.NewGuid().ToByteArray());
            return certificate.CopyWithPrivateKey(privateKey);
        }
    }
}
