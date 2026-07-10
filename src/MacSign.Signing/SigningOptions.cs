namespace MacSign.Signing;

/// <summary>
/// Options for a signing run. <see cref="Secret"/> (the PFX password) is supplied
/// transiently for a single run and is never persisted or logged by MacSign. Prefer the
/// CLI's <c>--password-env</c> (an environment variable) over a plaintext argument so the
/// secret doesn't land in shell history or the process list.
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
    /// RFC3161 timestamp server URL (e.g. <c>http://timestamp.digicert.com</c>), or a
    /// comma-separated list tried in order so one TSA outage doesn't fail the sign. When set,
    /// the signature is timestamped so it stays valid after the certificate expires.
    /// Empty/null skips timestamping.
    /// </summary>
    public string? TimestampUrl { get; init; }

    /// <summary>
    /// The credential secret — for PFX mode, the file's password. Transient; never persisted
    /// or logged by MacSign. May be null for a password-less PFX.
    /// </summary>
    public string? Secret { get; init; }

    /// <summary>
    /// A record's synthesized <c>ToString()</c> prints every property — which would put the
    /// <see cref="Secret"/> and <see cref="TrustedSigningAccessToken"/> in cleartext if an
    /// options instance were ever interpolated into a log line or exception. Override the member
    /// list so those two are redacted while the non-secret fields stay useful for diagnostics.
    /// </summary>
    private bool PrintMembers(System.Text.StringBuilder b)
    {
        b.Append("CertMode = ").Append(CertMode);
        b.Append(", PfxPath = ").Append(PfxPath);
        b.Append(", Pkcs11ModulePath = ").Append(Pkcs11ModulePath);
        b.Append(", Pkcs11CertThumbprint = ").Append(Pkcs11CertThumbprint);
        b.Append(", TrustedSigningEndpoint = ").Append(TrustedSigningEndpoint);
        b.Append(", TrustedSigningAccount = ").Append(TrustedSigningAccount);
        b.Append(", TrustedSigningProfile = ").Append(TrustedSigningProfile);
        b.Append(", Description = ").Append(Description);
        b.Append(", Url = ").Append(Url);
        b.Append(", SignAllSignableFiles = ").Append(SignAllSignableFiles);
        b.Append(", TimestampUrl = ").Append(TimestampUrl);
        b.Append(", Secret = ").Append(Secret is null ? "(null)" : "(set)");
        b.Append(", TrustedSigningAccessToken = ").Append(TrustedSigningAccessToken is null ? "(null)" : "(set)");
        return true;
    }
}
