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

    // ── Shared ─────────────────────────────────────────────────────────────────
    /// <summary>Signature description (SpcSpOpusInfo program name). Optional.</summary>
    public string? Description { get; init; }

    /// <summary>Signature URL (SpcSpOpusInfo more-info link). Optional.</summary>
    public string? Url { get; init; }

    /// <summary>Sign every signable file in the source folder, not just the setup file.</summary>
    public bool SignAllSignableFiles { get; init; }

    /// <summary>
    /// RFC3161 timestamp server URL. Accepted but IGNORED in Phase 1 — timestamping
    /// lands in Phase 2.
    /// </summary>
    public string? TimestampUrl { get; init; }

    /// <summary>
    /// The credential secret — for PFX mode, the file's password. Transient; never
    /// persisted. May be null for a password-less PFX.
    /// </summary>
    public string? Secret { get; init; }
}
