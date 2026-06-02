using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MacSign.Signing.Azure;

/// <summary>
/// Parses the <c>signingCertificate</c> the Trusted Signing sign response returns. The
/// live service double-base64-encodes a DER PKCS#7 chain — <c>base64(base64(DER))</c> —
/// while self-signed test fixtures use PEM. Both (and a single base64 layer, or a bare
/// DER cert) are handled by unwrapping base64 layers until PEM or DER is reached.
/// </summary>
internal static class CertificateChain
{
    public static List<X509Certificate2> Parse(string value)
    {
        value = value.Trim();

        for (int layer = 0; layer < 4; layer++)
        {
            if (value.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
            {
                var pem = new X509Certificate2Collection();
                pem.ImportFromPem(value);
                return [.. pem];
            }

            if (!TryDecodeBase64(value, out var decoded))
                break;

            if (decoded.Length > 0 && decoded[0] == 0x30) // DER SEQUENCE → PKCS#7 or a bare cert
                return FromDer(decoded);

            value = Encoding.ASCII.GetString(decoded).Trim(); // another base64 / PEM layer
        }

        throw new InvalidOperationException("Unrecognized signingCertificate format from Trusted Signing.");
    }

    private static List<X509Certificate2> FromDer(byte[] der)
    {
        try
        {
            var signed = new SignedCms();
            signed.Decode(der);
            if (signed.Certificates.Count > 0)
                return [.. signed.Certificates];
        }
        catch (CryptographicException)
        {
            // Not a PKCS#7 — fall through to a single DER certificate.
        }

        return [X509CertificateLoader.LoadCertificate(der)];
    }

    private static bool TryDecodeBase64(string value, out byte[] decoded)
    {
        try
        {
            decoded = Convert.FromBase64String(string.Concat(value.Where(c => !char.IsWhiteSpace(c))));
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }
}
