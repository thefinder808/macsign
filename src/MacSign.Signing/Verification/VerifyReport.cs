namespace MacSign.Signing.Verification;

/// <summary>The result of verifying a file's Authenticode signature.</summary>
public sealed record VerifyReport
{
    /// <summary>True if the file carries a signature.</summary>
    public bool IsSigned { get; init; }

    /// <summary>
    /// True if the signature is intact: the file is unmodified since signing AND the
    /// signer's signature verifies. Independent of chain trust — this is the verdict
    /// MacSign can assert authoritatively on macOS.
    /// </summary>
    public bool SignatureValid { get; init; }

    public string? SignerSubject { get; init; }
    public string? SignerIssuer { get; init; }
    public string? SignerSerialNumber { get; init; }

    /// <summary>The RFC3161 timestamp time, if the signature is timestamped.</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>True only if the certificate chain builds to a trusted root on this OS.</summary>
    public bool ChainTrusted { get; init; }

    /// <summary>Human note about chain status (e.g. the macOS "Microsoft roots absent" caveat).</summary>
    public string? ChainNote { get; init; }

    /// <summary>Set when the file couldn't be parsed/verified at all.</summary>
    public string? Error { get; init; }

    public static VerifyReport Unsigned() => new() { IsSigned = false };
    public static VerifyReport Failed(string error) => new() { IsSigned = false, Error = error };
}
