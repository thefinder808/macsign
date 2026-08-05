using System;
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
    public AzureSignInData AzureSignIn { get; set; } = new();
}

/// <summary>
/// The Azure account the user picked through the browser, remembered so they sign in once
/// rather than every launch.
/// <para>
/// Stored as the five named account fields rather than an opaque blob, deliberately: the
/// repo's rule is that persisted data holds no secrets <i>by construction</i>, and that only
/// stays checkable if a reviewer can read what is written. The tokens live in the OS keychain,
/// never here.
/// </para>
/// <para>
/// A sign-in belongs to a person, not to a certificate profile — two profiles in the same
/// tenant share one — which is why this hangs off <see cref="AppData"/> rather than
/// <see cref="ProfileData"/>.
/// </para>
/// </summary>
public sealed class AzureSignInData
{
    public string? Username { get; set; }
    public string? TenantId { get; set; }
    public string? HomeAccountId { get; set; }
    public string? ClientId { get; set; }
    public string? Authority { get; set; }

    /// <summary>A sign-in we can actually replay: the account id is what identifies it.
    /// Not persisted — it's derived, and the point of storing named fields is that what lands
    /// in settings.json is exactly the account, with nothing else to interpret.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsSignedIn =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(HomeAccountId);

    /// <summary>
    /// Whether this sign-in satisfies a profile that pins <paramref name="tenantId"/>. A blank
    /// request means "no constraint". A mismatch must read as <b>signed out</b>, never as a
    /// silent fallback to whichever account we happen to hold — signing as an identity the
    /// user did not ask for is the whole bug this feature exists to fix.
    /// </summary>
    public bool MatchesTenant(string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId) ||
        string.Equals(TenantId ?? "", tenantId.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the record Azure.Identity handed back. Unreadable input yields a
    /// signed-out instance rather than throwing: this runs at startup on a file users edit.</summary>
    public static AzureSignInData FromRecordJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AzureSignInData();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new AzureSignInData
            {
                Username = Field(root, "username"),
                Authority = Field(root, "authority"),
                HomeAccountId = Field(root, "homeAccountId"),
                TenantId = Field(root, "tenantId"),
                ClientId = Field(root, "clientId"),
            };
        }
        catch
        {
            return new AzureSignInData();
        }
    }

    /// <summary>Rebuilds the record for the signing engine, or null when signed out.</summary>
    public string? ToRecordJson() =>
        !IsSignedIn ? null : System.Text.Json.JsonSerializer.Serialize(new
        {
            username = Username,
            authority = Authority,
            homeAccountId = HomeAccountId,
            tenantId = TenantId,
            clientId = ClientId,
            version = "1.0",
        });

    private static string? Field(System.Text.Json.JsonElement root, string name) =>
        root.ValueKind == System.Text.Json.JsonValueKind.Object &&
        root.TryGetProperty(name, out var v) &&
        v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()
            : null;
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

    /// <summary>Entra tenant to authenticate against — a GUID or a domain. Null = unpinned.</summary>
    public string? TenantId { get; set; }

    /// <summary>"Default" (az login / env) or "InteractiveBrowser". String, not the enum, to
    /// match how <see cref="CredMode"/> is persisted. <b>Nullable on purpose</b>: null means
    /// "this profile predates the field" (or isn't Azure), which <c>ApplyProfile</c> must be
    /// able to tell apart from an explicit "Default".</summary>
    public string? CredentialSource { get; set; }

    public bool Timestamp { get; set; }
    public string? TimestampUrl { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? LastUsedIso { get; set; }

    /// <summary>Identity is key material only — description, URL, and timestamping are
    /// settings <i>on</i> a credential, not part of what makes two profiles "the same"
    /// credential. Used to match an incoming save against an existing profile so re-saving
    /// with different options updates it instead of stacking a duplicate.</summary>
    public bool SameCredentialAs(ProfileData other) =>
        Same(CredMode, other.CredMode) && CredMode switch
        {
            "Pfx" => Same(PfxPath, other.PfxPath),
            "Pkcs11" => Same(ModulePath, other.ModulePath) && Same(Thumbprint, other.Thumbprint),
            _ => Same(Account, other.Account) && Same(Profile, other.Profile)
                 && Same(Endpoint, other.Endpoint),
        };

    private static bool Same(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
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
