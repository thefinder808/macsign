namespace MacSign.Signing.Formats;

/// <summary>
/// A per-file-format signer. Knows how to digest a file the Authenticode way,
/// splice an opaque PKCS#7 blob into it, and detect/extract an existing
/// signature. It is credential- and CMS-agnostic.
/// </summary>
internal interface ISignatureFormat
{
    /// <summary>True if this format handles <paramref name="path"/> (by extension).</summary>
    bool CanHandle(string path);

    /// <summary>The Authenticode digest of the file (SHA-256), with format-specific exclusions.</summary>
    byte[] ComputeDigest(byte[] fileBytes);

    /// <summary>The <c>SpcIndirectDataContent</c> DER that the CMS will sign, wrapping <paramref name="fileDigest"/>.</summary>
    byte[] BuildSpcIndirectData(byte[] fileDigest);

    /// <summary>Return a new byte[] with the assembled PKCS#7 blob embedded.</summary>
    byte[] Embed(byte[] fileBytes, byte[] pkcs7Der);

    /// <summary>Extract an existing embedded PKCS#7, if present.</summary>
    bool TryExtractSignature(byte[] fileBytes, out byte[] pkcs7Der);
}
