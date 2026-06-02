using System.Formats.Asn1;

namespace MacSign.Signing.Cms;

/// <summary>
/// DER-encodes the Microsoft SPC ASN.1 structures the BCL has no typed classes
/// for. Encoding mirrors what <c>osslsigncode</c>/SignTool emit so that
/// <c>signtool verify</c> accepts the result.
/// </summary>
internal static class SpcEncoder
{
    /// <summary>
    /// The full <c>SpcIndirectDataContent</c> (the CMS encapsulated content) for a PE image:
    /// <code>
    /// SEQUENCE {
    ///   SEQUENCE { OID SpcPeImageData, SpcPeImageData },   -- data
    ///   SEQUENCE { AlgorithmIdentifier, OCTET STRING }     -- messageDigest (the file hash)
    /// }
    /// </code>
    /// </summary>
    public static byte[] BuildPeIndirectData(ReadOnlySpan<byte> fileDigest)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            // data : SpcAttributeTypeAndOptionalValue
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(AuthenticodeOids.SpcPeImageData);
                w.WriteEncodedValue(BuildSpcPeImageData());
            }
            // messageDigest : DigestInfo
            using (w.PushSequence())
            {
                using (w.PushSequence()) // AlgorithmIdentifier
                {
                    w.WriteObjectIdentifier(AuthenticodeOids.Sha256);
                    w.WriteNull();
                }
                w.WriteOctetString(fileDigest);
            }
        }
        return w.Encode();
    }

    /// <summary>
    /// <c>SpcPeImageData ::= SEQUENCE { flags BIT STRING, file [0] EXPLICIT SpcLink }</c>,
    /// where the file link is the classic obsolete moniker:
    /// <c>file [2] EXPLICIT { SpcString unicode [0] IMPLICIT BMPString "&lt;&lt;&lt;Obsolete&gt;&gt;&gt;" }</c>.
    /// Page hashes are deliberately omitted (parity with osslsigncode's default).
    /// </summary>
    private static byte[] BuildSpcPeImageData()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteBitString(ReadOnlySpan<byte>.Empty); // flags (empty)
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))   // file [0] EXPLICIT
            using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true)))   // SpcLink file [2] EXPLICIT
            {
                // SpcString unicode [0] IMPLICIT BMPString
                w.WriteCharacterString(
                    UniversalTagNumber.BMPString,
                    "<<<Obsolete>>>",
                    new Asn1Tag(TagClass.ContextSpecific, 0));
            }
        }
        return w.Encode();
    }

    /// <summary>
    /// <c>SpcStatementType ::= SEQUENCE OF OBJECT IDENTIFIER</c>, value =
    /// individualCodeSigning. Returned as the single attribute value DER.
    /// </summary>
    public static byte[] StatementTypeValue()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
            w.WriteObjectIdentifier(AuthenticodeOids.IndividualCodeSigning);
        return w.Encode();
    }

    /// <summary>
    /// <c>SpcSpOpusInfo ::= SEQUENCE { programName [0] EXPLICIT SpcString OPTIONAL,
    /// moreInfo [1] EXPLICIT SpcLink OPTIONAL }</c>. Returned as the single attribute value DER.
    /// </summary>
    public static byte[] OpusInfoValue(string? programName, string? url)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            if (!string.IsNullOrEmpty(programName))
            {
                using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true))) // programName [0] EXPLICIT
                    w.WriteCharacterString(
                        UniversalTagNumber.BMPString, programName,
                        new Asn1Tag(TagClass.ContextSpecific, 0)); // SpcString unicode [0] IMPLICIT
            }
            if (!string.IsNullOrEmpty(url))
            {
                using (w.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true))) // moreInfo [1] EXPLICIT
                    w.WriteCharacterString(
                        UniversalTagNumber.IA5String, url,
                        new Asn1Tag(TagClass.ContextSpecific, 0)); // SpcLink url [0] IMPLICIT
            }
        }
        return w.Encode();
    }
}
