namespace MacSign.Signing.Cms;

/// <summary>Object identifiers used by Authenticode.</summary>
internal static class AuthenticodeOids
{
    /// <summary>SPC_INDIRECT_DATA_OBJID — the encapsulated content type of an Authenticode CMS.</summary>
    public const string SpcIndirectDataContent = "1.3.6.1.4.1.311.2.1.4";

    /// <summary>SPC_PE_IMAGE_DATA_OBJID — the <c>data</c> member for PE images.</summary>
    public const string SpcPeImageData = "1.3.6.1.4.1.311.2.1.15";

    /// <summary>SPC_SIPINFO_OBJID — the <c>data</c> member for scripts (PowerShell, etc.).</summary>
    public const string SpcSipInfo = "1.3.6.1.4.1.311.2.1.30";

    /// <summary>SPC_SP_OPUS_INFO_OBJID — signed attribute carrying description + URL.</summary>
    public const string SpcSpOpusInfo = "1.3.6.1.4.1.311.2.1.12";

    /// <summary>SPC_STATEMENT_TYPE_OBJID — signed attribute.</summary>
    public const string SpcStatementType = "1.3.6.1.4.1.311.2.1.11";

    /// <summary>SPC_INDIVIDUAL_SP_KEY_PURPOSE_OBJID — the statement-type value we emit.</summary>
    public const string IndividualCodeSigning = "1.3.6.1.4.1.311.2.1.21";

    /// <summary>SHA-256 digest algorithm.</summary>
    public const string Sha256 = "2.16.840.1.101.3.4.2.1";

    /// <summary>szOID_RFC3161_counterSign — the unsigned attribute holding an RFC3161 timestamp token.</summary>
    public const string Rfc3161Timestamp = "1.3.6.1.4.1.311.3.3.1";
}
