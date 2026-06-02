namespace MacSign.Signing;

/// <summary>
/// Options for a signing run. <see cref="Secret"/> (the PFX password) is supplied
/// transiently for a single run and is NEVER persisted, logged, or placed on a
/// command line.
/// </summary>
public sealed record SigningOptions
{
    public CertMode CertMode { get; init; } = CertMode.Pfx;

    // ── PFX mode ──────────────────────────────────────────────────────────────
    /// <summary>Path to the <c>.pfx</c>/<c>.p12</c> file.</summary>
    public string? PfxPath { get; init; }

    // ── PKCS#11 / HSM mode (requires the MacSign.Signing.Pkcs11 backend) ───────
    /// <summary>Path to the PKCS#11 module (the vendor's <c>.so</c>/<c>.dylib</c>).</summary>
    public string? Pkcs11ModulePath { get; init; }

    /// <summary>Optional certificate thumbprint to disambiguate when the token holds several.</summary>
    public string? Pkcs11CertThumbprint { get; init; }

    // ── Azure Trusted Signing mode (requires the MacSign.Signing.Azure backend) ──
    /// <summary>Account endpoint, e.g. <c>https://eus.codesigning.azure.net</c> (scheme optional).</summary>
    public string? TrustedSigningEndpoint { get; init; }

    /// <summary>The Trusted Signing (code signing) account name.</summary>
    public string? TrustedSigningAccount { get; init; }

    /// <summary>The certificate profile name to sign with.</summary>
    public string? TrustedSigningProfile { get; init; }

    /// <summary>
    /// Optional pre-fetched access token (scope <c>https://codesigning.azure.net</c>).
    /// When null, Azure.Identity's <c>DefaultAzureCredential</c> acquires one. Transient.
    /// </summary>
    public string? TrustedSigningAccessToken { get; init; }

    // ── Shared ─────────────────────────────────────────────────────────────────
    /// <summary>Signature description (SpcSpOpusInfo program name). Optional.</summary>
    public string? Description { get; init; }

    /// <summary>Signature URL (SpcSpOpusInfo more-info link). Optional.</summary>
    public string? Url { get; init; }

    /// <summary>Sign every signable file in the source folder, not just the setup file.</summary>
    public bool SignAllSignableFiles { get; init; }

    /// <summary>
    /// RFC3161 timestamp server URL (e.g. <c>http://timestamp.digicert.com</c>).
    /// When set, the signature is timestamped so it stays valid after the
    /// certificate expires. Empty/null skips timestamping.
    /// </summary>
    public string? TimestampUrl { get; init; }

    /// <summary>
    /// The credential secret — for PFX mode, the file's password. Transient; never
    /// persisted. May be null for a password-less PFX.
    /// </summary>
    public string? Secret { get; init; }
}
