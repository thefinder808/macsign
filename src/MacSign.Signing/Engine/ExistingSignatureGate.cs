using MacSign.Signing.Formats;

namespace MacSign.Signing.Engine;

/// <summary>
/// Decides whether a file is already signed (so we skip it rather than clobber an
/// existing signature). Native — replaces the old <c>osslsigncode verify</c> shell-out.
/// </summary>
internal static class ExistingSignatureGate
{
    public static bool IsSigned(ISignatureFormat format, byte[] fileBytes, out string? subject)
    {
        if (format.TryExtractSignature(fileBytes, out var pkcs7))
        {
            subject = SignatureInspector.TrySubject(pkcs7);
            return true;
        }
        subject = null;
        return false;
    }
}
