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

    // The PowerShell SIP GUID {603BCC1F-4B59-4E08-B724-D2C6297EF351}, raw bytes.
    private static ReadOnlySpan<byte> PowerShellSipGuid =>
        [0x1F, 0xCC, 0x3B, 0x60, 0x59, 0x4B, 0x08, 0x4E, 0xB7, 0x24, 0xD2, 0xC6, 0x29, 0x7E, 0xF3, 0x51];

    /// <summary>The <c>SpcIndirectDataContent</c> for a PowerShell script (data = SpcSipInfo).</summary>
    public static byte[] BuildScriptIndirectData(ReadOnlySpan<byte> fileDigest) =>
        BuildSipIndirectData(BuildPowerShellSipInfo(), fileDigest);

    /// <summary>
    /// <c>SpcSipInfo ::= SEQUENCE { INTEGER 65536, OCTET STRING guid, INTEGER 0 ×5 }</c>
    /// — the fixed PowerShell SIP descriptor (mirrors osslsigncode/SignTool).
    /// </summary>
    private static byte[] BuildPowerShellSipInfo()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteInteger(65536);
            w.WriteOctetString(PowerShellSipGuid);
            for (int i = 0; i < 5; i++)
                w.WriteInteger(0);
        }
        return w.Encode();
    }

    // The MSI SIP GUID {F1100C00-0000-0000-C000-000000000046}, exact on-the-wire bytes.
    private static ReadOnlySpan<byte> MsiSipGuid =>
        [0xF1, 0x10, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46];

    /// <summary>The <c>SpcIndirectDataContent</c> for an MSI (data = SpcSipInfo, magic 1).</summary>
    public static byte[] BuildMsiIndirectData(ReadOnlySpan<byte> fileDigest) =>
        BuildSipIndirectData(BuildMsiSipInfo(), fileDigest);

    private static byte[] BuildMsiSipInfo()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            w.WriteInteger(1);
            w.WriteOctetString(MsiSipGuid);
            for (int i = 0; i < 5; i++)
                w.WriteInteger(0);
        }
        return w.Encode();
    }

    private static byte[] BuildSipIndirectData(byte[] sipInfo, ReadOnlySpan<byte> fileDigest)
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            using (w.PushSequence())
            {
                w.WriteObjectIdentifier(AuthenticodeOids.SpcSipInfo);
                w.WriteEncodedValue(sipInfo);
            }
            using (w.PushSequence())
            {
                using (w.PushSequence())
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
