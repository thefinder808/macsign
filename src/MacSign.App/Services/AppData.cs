using System.Collections.Generic;

namespace MacSign.App.Services;

/// <summary>Everything MacSign persists. Crucially holds <b>no secrets</b> —
/// passwords/PINs/tokens are transient and re-entered each run.</summary>
public sealed class AppData
{
    public List<ProfileData> Profiles { get; set; } = new();
    public List<RunData> Activity { get; set; } = new();
    public AppleSignPrefs AppleSign { get; set; } = new();
    public AppPrefs Preferences { get; set; } = new();
}

/// <summary>App-wide preferences (no secrets). Defaults reproduce today's
/// hardcoded behaviour so existing installs see no change until a setting is touched.</summary>
public sealed class AppPrefs
{
    public string Theme { get; set; } = "System";                       // System | Light | Dark
    public string DefaultTimestampUrl { get; set; } = "http://timestamp.digicert.com";
    public bool   TimestampByDefault  { get; set; } = true;
    public bool   RestoreLastCredential { get; set; } = true;           // opt-out
    public int    ActivityKeepLast    { get; set; } = 50;               // 0 = unlimited

    // ── auto-updates (no secret) ──
    public bool    AutoCheckUpdates   { get; set; } = true;   // throttled on-launch check
    public string? LastUpdateCheckUtc { get; set; }           // ISO-8601; null = never checked
    public string? SkippedVersion     { get; set; }           // version the user chose to skip
}

/// <summary>Remembered (non-secret) preferences for the Mac-app signing screen.
/// Holds names + flags only — never key material; the API-key path is re-picked
/// each run, like passwords/PINs on the Sign screen.</summary>
public sealed class AppleSignPrefs
{
    public string? IdentitySha1 { get; set; }
    public string? NotaryProfile { get; set; }
    public bool HardenedRuntime { get; set; } = true;
    public bool Deep { get; set; } = true;
    public bool Notarize { get; set; }
    public bool Staple { get; set; } = true;
    public bool UseApiKey { get; set; }
}

/// <summary>A reusable credential + options preset (no secret).</summary>
public sealed class ProfileData
{
    public string Name { get; set; } = "";
    public string CredMode { get; set; } = "Azure"; // "Pfx" | "Pkcs11" | "Azure"
    public string? PfxPath { get; set; }
    public string? ModulePath { get; set; }
    public string? Thumbprint { get; set; }
    public string? Account { get; set; }
    public string? Profile { get; set; }
    public string? Endpoint { get; set; }
    public bool Timestamp { get; set; }
    public string? TimestampUrl { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? LastUsedIso { get; set; }
}

/// <summary>One recorded signing run (metadata only).</summary>
public sealed class RunData
{
    public int FileCount { get; set; }
    public string Credential { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Status { get; set; } = "ok"; // "ok" | "warn" | "fail"
    public string WhenIso { get; set; } = "";
}
