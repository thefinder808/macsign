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
    public static async Task<byte[]> SignFileBytesAsync(
        ISignatureFormat format, ICredentialSigner credential, SigningOptions options,
        byte[] fileBytes, CancellationToken ct)
    {
        byte[] digest = format.ComputeDigest(fileBytes);
        byte[] spc = format.BuildSpcIndirectData(digest);
        byte[] pkcs7 = await new AuthenticodeCmsBuilder().BuildAsync(spc, credential, options, ct);
        return format.Embed(fileBytes, pkcs7);
    }
}
