using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace MacSign.Signing.Azure;

/// <summary>
/// Parses the <c>signingCertificate</c> the Trusted Signing sign response returns —
/// a PEM chain (leaf first) or, defensively, base64 of a PKCS#7 chain / single DER cert.
/// </summary>
internal static class CertificateChain
{
    public static List<X509Certificate2> Parse(string value)
    {
        value = value.Trim();

        if (value.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            var pem = new X509Certificate2Collection();
            pem.ImportFromPem(value); // preserves PEM block order (leaf first)
            return [.. pem];
        }

        var bytes = Convert.FromBase64String(value);

        // A base64 chain is almost always a PKCS#7 (certs-only SignedData).
        try
        {
            var signed = new SignedCms();
            signed.Decode(bytes);
            if (signed.Certificates.Count > 0)
                return [.. signed.Certificates];
        }
        catch (CryptographicException)
        {
            // Not PKCS#7 — fall through to a single DER certificate.
        }

        return [X509CertificateLoader.LoadCertificate(bytes)];
    }
}
