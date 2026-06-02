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
            return cms.SignerInfos.Count > 0
                ? cms.SignerInfos[0].Certificate?.Subject
                : null;
        }
        catch
        {
            return null;
        }
    }
}
