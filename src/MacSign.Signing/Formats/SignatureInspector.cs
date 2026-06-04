using System.Security.Cryptography.Pkcs;

namespace MacSign.Signing.Formats;

/// <summary>Reads basic facts out of an embedded PKCS#7 signature blob.</summary>
internal static class SignatureInspector
{
    /// <summary>The signer's certificate subject, or null if it can't be determined.</summary>
    public static string? TrySubject(byte[] pkcs7Der)
    {
        try
        {
            var cms = new SignedCms();
            cms.Decode(pkcs7Der);
            if (cms.SignerInfos.Count == 0)
                return null;
            // The cert wraps a native handle; dispose it instead of leaking to finalization.
            using var cert = cms.SignerInfos[0].Certificate;
            return cert?.Subject;
        }
        catch
        {
            return null;
        }
    }
}
