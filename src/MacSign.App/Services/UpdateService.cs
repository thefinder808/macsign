using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MacSign.App.Services;

/// <summary>Immutable description of an available release.</summary>
public sealed record UpdateInfo(string Version, string ReleaseNotes, string ReleaseUrl,
                                string AssetName, string AssetUrl);

/// <summary>Outcome of a check: an update + its info, or none, or an error string.</summary>
public sealed record UpdateCheckResult(bool UpdateAvailable, UpdateInfo? Info, string? Error);

/// <summary>
/// The in-app updater: check GitHub Releases, download the right-arch DMG, verify it
/// is Developer-ID-signed (Team ID Q6LRJQSA42) + notarized, then install + relaunch.
/// Avalonia-free and fully test-seamed (HttpClient + AppleSigningService + IProcessRunner).
/// </summary>
public sealed class UpdateService
{
    /// <summary>The ONLY accepted signer. A downloaded DMG must be codesigned by this
    /// Developer ID Team ID (and notarized) or it is never installed — this is the root
    /// of trust. Team IDs are stable across cert renewals, so this survives cert rotation.</summary>
    public const string ExpectedTeamId = "Q6LRJQSA42";

    private const string Owner = "thefinder808", Repo = "macsign";

    private readonly HttpClient _http;
    private readonly AppleSigningService _apple;
    private readonly IProcessRunner _runner;

    public UpdateService(HttpClient? http = null, AppleSigningService? apple = null, IProcessRunner? runner = null)
    {
        _http = http ?? new HttpClient();
        _apple = apple ?? new AppleSigningService();
        _runner = runner ?? new ProcessRunner();
    }

    /// <summary>True iff <paramref name="latestTag"/> parses to a strictly greater
    /// version than <paramref name="current"/>. Tolerates a leading "v" and trailing
    /// pre-release/build metadata; returns false on any unparseable input (incl. "dev").</summary>
    public static bool IsNewer(string latestTag, string current)
    {
        var l = ParseVersion(latestTag);
        var c = ParseVersion(current);
        return l is not null && c is not null && l > c;
    }

    private static Version? ParseVersion(string s)
    {
        s = (s ?? "").Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        var core = new string(s.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
        if (core.Length == 0) return null;
        if (!core.Contains('.')) core += ".0";          // Version needs >=2 parts
        return Version.TryParse(core, out var v) ? v : null;
    }

    /// <summary>Pick the asset name matching the host architecture, or null.</summary>
    public static string? AssetNameFor(IEnumerable<string> assetNames)
    {
        var suffix = ArchSuffix() + ".dmg";
        return assetNames.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ArchSuffix() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "osx-arm64",
        Architecture.X64   => "osx-x64",
        _                  => "osx-x64",
    };

    /// <summary>Query the latest stable release (the endpoint excludes drafts +
    /// prereleases), compare to the running version, and pick the host-arch asset.
    /// Never throws — network/parse failures come back as an Error.</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("MacSign", AppInfo.Version));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new UpdateCheckResult(false, null, $"GitHub returned {(int)resp.StatusCode}.");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!IsNewer(tag, AppInfo.Version))
                return new UpdateCheckResult(false, null, null);

            var names = root.GetProperty("assets").EnumerateArray()
                .Select(a => a.GetProperty("name").GetString() ?? "").ToList();
            var assetName = AssetNameFor(names);
            if (assetName is null)
                return new UpdateCheckResult(false, null, "No matching .dmg asset for this Mac's architecture.");

            var asset = root.GetProperty("assets").EnumerateArray()
                .First(a => a.GetProperty("name").GetString() == assetName);
            var info = new UpdateInfo(
                Version: tag.TrimStart('v', 'V'),
                ReleaseNotes: root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                ReleaseUrl: root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
                AssetName: assetName,
                AssetUrl: asset.GetProperty("browser_download_url").GetString() ?? "");
            return new UpdateCheckResult(true, info, null);
        }
        catch (OperationCanceledException) { return new UpdateCheckResult(false, null, "Canceled."); }
        catch (Exception ex) { return new UpdateCheckResult(false, null, ex.Message); }
    }

    /// <summary>The trust gate. A DMG installs only if it is codesigned by our
    /// Developer ID Team ID AND notarized (stapled + Gatekeeper-accepted). Any failure
    /// ⇒ false ⇒ the caller never mounts/installs it. Reuses AppleSigningService.
    /// Checks the .dmg's own signature/notarization (the release image is
    /// Developer-ID-signed + notarized by CI); the install step extracts the app
    /// from that trusted image.</summary>
    public async Task<bool> VerifyAsync(string dmgPath, CancellationToken ct)
    {
        var r = await _apple.InspectAsync(dmgPath, ct);
        return r.Valid
            && string.Equals(r.TeamId, ExpectedTeamId, StringComparison.Ordinal)
            && r.Stapled
            && r.GatekeeperAccepted;
    }

    /// <summary>Download the asset to a temp .dmg, reporting 0..1 progress when the
    /// server sends a Content-Length. Returns the path, or null on failure (and the
    /// partial temp file is best-effort deleted so retries don't orphan).</summary>
    public async Task<string?> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        string? dest = null;
        try
        {
            dest = Path.Combine(Path.GetTempPath(),
                $"macsign-update-{Guid.NewGuid():N}-{info.AssetName}");
            using var resp = await _http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) { TryDelete(dest); return null; }

            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(dest);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total is > 0) progress?.Report((double)read / total.Value);
            }
            return dest;
        }
        catch { if (dest is not null) TryDelete(dest); return null; }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { /* best-effort */ } }
}
