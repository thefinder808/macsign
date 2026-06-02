using System.Formats.Asn1;
using MacSign.Signing.Cms;

namespace MacSign.Signing.Verification;

/// <summary>Pulls the file digest out of an embedded <c>SpcIndirectDataContent</c>.</summary>
internal static class SpcDigest
{
    /// <summary>
    /// Read the SHA-256 file digest from the SPC content. Returns false for other
    /// algorithms (we only recompute SHA-256) or malformed input.
    /// </summary>
    public static bool TryReadSha256(byte[]? spcContent, out byte[] digest)
    {
        digest = [];
        if (spcContent is null)
            return false;
        try
        {
            // SpcIndirectDataContent ::= SEQUENCE { data ..., messageDigest DigestInfo }
            var outer = new AsnReader(spcContent, AsnEncodingRules.BER).ReadSequence();
            outer.ReadEncodedValue(); // skip 'data'
            var digestInfo = outer.ReadSequence();
            var algorithm = digestInfo.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != AuthenticodeOids.Sha256)
                return false;
            digest = digestInfo.ReadOctetString();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
