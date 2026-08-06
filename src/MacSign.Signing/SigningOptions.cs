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

    /// <summary>
    /// The Microsoft Entra tenant (directory) ID to authenticate against. Leave null to let
    /// the credential use whichever tenant it defaults to. Set it when the account that holds
    /// the signing role lives in a different tenant than your everyday sign-in — otherwise the
    /// token is issued for the wrong tenant and the service answers 403 no matter which roles
    /// you assign.
    /// </summary>
    public string? TrustedSigningTenantId { get; init; }

    /// <summary>Which identity signs. See <see cref="TrustedSigningCredentialSource"/>.</summary>
    public TrustedSigningCredentialSource TrustedSigningCredentialSource { get; init; }
        = TrustedSigningCredentialSource.Default;

    /// <summary>
    /// A serialized Azure.Identity <c>AuthenticationRecord</c> from a previous browser sign-in,
    /// required by <see cref="TrustedSigningCredentialSource.InteractiveBrowser"/>. It holds no
    /// token — only the account's username, tenant, and home-account ID — so it is safe to
    /// persist; the tokens themselves live in the OS keychain. Not a secret, but it does name
    /// the operator, so <see cref="PrintMembers"/> shows presence rather than content.
    /// </summary>
    public string? TrustedSigningAuthRecord { get; init; }

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
    /// list so those are redacted while the non-secret fields stay useful for diagnostics.
    /// <see cref="TrustedSigningAuthRecord"/> is redacted too — it carries no token, but it does
    /// name the signed-in operator. This list is an allow-list: a new property is invisible in
    /// <c>ToString()</c> until it is added here, so add every field deliberately.
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
        b.Append(", TrustedSigningTenantId = ").Append(TrustedSigningTenantId);
        b.Append(", TrustedSigningCredentialSource = ").Append(TrustedSigningCredentialSource);
        b.Append(", Description = ").Append(Description);
        b.Append(", Url = ").Append(Url);
        b.Append(", SignAllSignableFiles = ").Append(SignAllSignableFiles);
        b.Append(", TimestampUrl = ").Append(TimestampUrl);
        b.Append(", Secret = ").Append(Secret is null ? "(null)" : "(set)");
        b.Append(", TrustedSigningAccessToken = ").Append(TrustedSigningAccessToken is null ? "(null)" : "(set)");
        b.Append(", TrustedSigningAuthRecord = ").Append(TrustedSigningAuthRecord is null ? "(null)" : "(set)");
        return true;
    }
}
