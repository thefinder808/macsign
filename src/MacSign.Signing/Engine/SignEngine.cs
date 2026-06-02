using MacSign.Signing.Cms;
using MacSign.Signing.Credentials;
using MacSign.Signing.Formats;

namespace MacSign.Signing.Engine;

/// <summary>
/// The format-agnostic signing pipeline for a single file's bytes:
/// digest → build SPC → assemble CMS → embed. Pure (no I/O), so it's easily tested.
/// </summary>
internal static class SignEngine
{
    public static byte[] SignFileBytes(
        ISignatureFormat format, ICredentialSigner credential, SigningOptions options, byte[] fileBytes)
    {
        byte[] digest = format.ComputeDigest(fileBytes);
        byte[] spc = format.BuildSpcIndirectData(digest);
        byte[] pkcs7 = new AuthenticodeCmsBuilder().Build(spc, credential, options);
        return format.Embed(fileBytes, pkcs7);
    }
}
