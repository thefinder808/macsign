using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
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

    /// <summary>Product identity the downloaded bundle must match — bound in addition to the
    /// Team ID so a different (even validly-signed) app by the same Developer ID can't install.
    /// The bundle id + executable come from the signed CodeDirectory; the .app dir name and
    /// asset name are structural. All four are checked.</summary>
    public const string ExpectedBundleId = "com.thefinder808.MacSign";
    public const string ExpectedExecutable = "MacSign";
    public const string ExpectedAppName = "MacSign.app";

    private const string Owner = "thefinder808", Repo = "macsign";

    private const string Hdiutil    = "/usr/bin/hdiutil";
    private const string Ditto      = "/usr/bin/ditto";
    private const string Xcrun      = "/usr/bin/xcrun";
    private const string PlistBuddy = "/usr/libexec/PlistBuddy";

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

    /// <summary>Pick the asset whose name EXACTLY matches our release naming for this version
    /// and host architecture (<c>MacSign-&lt;version&gt;-osx-&lt;arch&gt;.dmg</c>), or null. An exact
    /// match — not just an arch suffix — so a stray/misnamed asset can't be selected.</summary>
    public static string? AssetNameFor(IEnumerable<string> assetNames, string version)
    {
        var expected = $"MacSign-{version}-{ArchSuffix()}.dmg";
        return assetNames.FirstOrDefault(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase));
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

            var version = tag.TrimStart('v', 'V');
            var names = root.GetProperty("assets").EnumerateArray()
                .Select(a => a.GetProperty("name").GetString() ?? "").ToList();
            var assetName = AssetNameFor(names, version);
            if (assetName is null)
                return new UpdateCheckResult(false, null, "No matching .dmg asset for this Mac's architecture.");

            var asset = root.GetProperty("assets").EnumerateArray()
                .First(a => a.GetProperty("name").GetString() == assetName);
            var info = new UpdateInfo(
                Version: version,
                ReleaseNotes: root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                ReleaseUrl: root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
                AssetName: assetName,
                AssetUrl: asset.GetProperty("browser_download_url").GetString() ?? "");
            return new UpdateCheckResult(true, info, null);
        }
        catch (OperationCanceledException) { return new UpdateCheckResult(false, null, "Canceled."); }
        catch (Exception ex) { return new UpdateCheckResult(false, null, ex.Message); }
    }

    /// <summary>The trust gate. A download installs only if the <b>.app inside</b> the
    /// DMG is Developer-ID signed by our Team ID AND notarized, and the .dmg itself
    /// carries a stapled notarization ticket. Any failure ⇒ false ⇒ the caller never
    /// installs it.
    ///
    /// We verify the inner app, not the container: our release DMGs are notarized +
    /// stapled but the <i>image</i> is not codesigned (build-macos.sh codesigns the app
    /// and notarizes the .dmg), so inspecting the container's own signature rejects every
    /// legitimate release. <c>spctl --assess --type exec</c> accepts only notarized
    /// Developer ID code, so it proves both signing and notarization of the app; the app
    /// itself is not stapled (the ticket lives on the .dmg), so we require the .dmg's
    /// staple separately rather than the app's. Always detaches.</summary>
    public async Task<bool> VerifyAsync(string dmgPath, string expectedVersion, CancellationToken ct)
    {
        if (AppleSigningService.Classify(dmgPath) != AppleTargetKind.Dmg) return false;

        var mount = Path.Combine(Path.GetTempPath(), "macsign-verify-" + Guid.NewGuid().ToString("N"));
        bool attached = false;
        try
        {
            var att = await _runner.RunAsync(Hdiutil,
                new[] { "attach", "-nobrowse", "-readonly", "-mountpoint", mount, dmgPath }, null, ct);
            if (!att.Success) return false;
            attached = true;

            // Require EXACTLY ONE top-level .app named MacSign.app — no "first of many"
            // ambiguity, and the same invariant the install step enforces (so verify and
            // install can't pick different bundles).
            var apps = Directory.Exists(mount)
                ? Directory.EnumerateDirectories(mount, "*.app").ToList()
                : new List<string>();
            if (apps.Count != 1) return false;
            var app = apps[0];
            if (!string.Equals(Path.GetFileName(app), ExpectedAppName, StringComparison.Ordinal)) return false;

            return await IsTrustedReleaseAsync(app, dmgPath, expectedVersion, ct);
        }
        finally
        {
            if (attached) await _runner.RunAsync(Hdiutil, new[] { "detach", mount, "-force" }, null, ct);
            try { if (Directory.Exists(mount)) Directory.Delete(mount); } catch { /* best-effort */ }
        }
    }

    /// <summary>The trust gate applied to a mounted release. The <paramref name="app"/> must be
    /// Developer-ID signed by our Team ID AND notarized, carry our signed bundle id + executable
    /// and the advertised version, and the <paramref name="dmgPath"/> must be stapled. Shared by
    /// <see cref="VerifyAsync"/> and the install step so the bundle we install is re-checked
    /// against the exact same criteria the download was verified against.</summary>
    private async Task<bool> IsTrustedReleaseAsync(string app, string dmgPath, string expectedVersion, CancellationToken ct)
    {
        var r = await _apple.InspectAsync(app, ct);
        bool appTrusted = r.Valid
            && string.Equals(r.TeamId, ExpectedTeamId, StringComparison.Ordinal)
            && r.GatekeeperAccepted                                                   // notarized Developer ID
            && string.Equals(r.Identifier, ExpectedBundleId, StringComparison.Ordinal) // signed bundle id
            && string.Equals(Path.GetFileName(r.Executable ?? ""), ExpectedExecutable, StringComparison.Ordinal);

        // Bind to the advertised version: CFBundleShortVersionString must equal it. The
        // Info.plist is sealed by the (verified) signature, so this is tamper-evident.
        bool versionOk = string.Equals(
            await ReadShortVersionAsync(app, ct), expectedVersion, StringComparison.Ordinal);

        // Defense in depth: the .dmg must itself be a stapled release.
        var staple = await _runner.RunAsync(Xcrun, new[] { "stapler", "validate", dmgPath }, null, ct);
        return appTrusted && versionOk && staple.Success;
    }

    /// <summary>Read the bundle's <c>CFBundleShortVersionString</c> from its (signature-sealed)
    /// Info.plist via PlistBuddy. Returns null if it can't be read.</summary>
    private async Task<string?> ReadShortVersionAsync(string appPath, CancellationToken ct)
    {
        var plist = Path.Combine(appPath, "Contents", "Info.plist");
        var r = await _runner.RunAsync(PlistBuddy,
            new[] { "-c", "Print :CFBundleShortVersionString", plist }, null, ct);
        return r.Success ? r.StdOut.Trim() : null;
    }

    /// <summary>Download the asset to a temp .dmg, reporting 0..1 progress when the
    /// server sends a Content-Length. Returns the path, or null on failure (and the
    /// partial temp file is best-effort deleted so retries don't orphan).</summary>
    public async Task<string?> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        // A prior attempt whose verify/install bailed leaves its .dmg behind; clear those
        // first so abandoned downloads can't pile up (the successful install path deletes
        // its own DMG via the swap script).
        PruneStaleDownloads(Path.GetTempPath());

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

    /// <summary>Best-effort delete of leftover download artifacts (<c>macsign-update-*</c>)
    /// from prior, abandoned update attempts. Targets only the download prefix — mount points
    /// and staging dirs use a different prefix and are cleaned up by their own flows.</summary>
    public static void PruneStaleDownloads(string tempDir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(tempDir, "macsign-update-*"))
                TryDelete(f);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Resolve the .app bundle from the executable's base dir
    /// (…/Contents/MacOS → two levels up).</summary>
    public static string InstalledAppPathFrom(string baseDir)
        => Path.GetFullPath(Path.Combine(baseDir, "..", ".."));

    private static bool DirWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".macsign-write-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, ""); File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Mount the (already-verified) DMG, stage the new app, write a detached
    /// helper that waits for us to exit, atomically swaps the bundle, and relaunches.
    /// Returns a failure (without quitting) if the install dir isn't writable.</summary>
    public Task<AppleOpResult> InstallAndRelaunchAsync(string dmgPath, string expectedVersion, CancellationToken ct)
        => InstallAndRelaunchAsync(dmgPath, expectedVersion, InstalledAppPathFrom(AppContext.BaseDirectory), ct);

    // installedAppPath is injectable for tests.
    public async Task<AppleOpResult> InstallAndRelaunchAsync(string dmgPath, string expectedVersion, string installedAppPath, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(installedAppPath) ?? "/Applications";
        if (!DirWritable(parent))
            return AppleOpResult.Fail("Manual install needed",
                "MacSign can't write to its install folder. Drag the new MacSign to Applications to finish updating.", "");

        var stamp = Guid.NewGuid().ToString("N")[..8];
        var mount = Path.Combine(Path.GetTempPath(), $"macsign-upd-mnt-{stamp}");
        var staged = Path.Combine(Path.GetTempPath(), $"macsign-upd-app-{stamp}");
        var script = Path.Combine(Path.GetTempPath(), $"macsign-upd-{stamp}.sh");
        bool attached = false, launched = false;
        try
        {
            var att = await _runner.RunAsync(Hdiutil,
                new[] { "attach", "-nobrowse", "-readonly", "-mountpoint", mount, dmgPath }, null, ct);
            if (!att.Success) return AppleOpResult.Fail("Mount failed", FirstLine(att.StdErr + att.StdOut), att.StdErr);
            attached = true;

            // Same invariant as VerifyAsync: exactly one top-level MacSign.app — so the bundle
            // we install is the one that was verified, never a different "first of many".
            var apps = Directory.EnumerateDirectories(mount, "*.app").ToList();
            var src = apps.Count == 1
                && string.Equals(Path.GetFileName(apps[0]), ExpectedAppName, StringComparison.Ordinal)
                ? apps[0] : null;
            if (src is null) return AppleOpResult.Fail("Bad image", "The disk image must contain exactly one MacSign.app.", "");

            // Re-run the trust gate on the bundle we mounted HERE. VerifyAsync ran on a
            // separate mount of the temp .dmg, so re-verifying now binds the bytes we install
            // to the criteria we verified and closes the verify→install TOCTOU (a swapped .dmg
            // in the temp dir would be caught here instead of being copied in).
            if (!await IsTrustedReleaseAsync(src, dmgPath, expectedVersion, ct))
                return AppleOpResult.Fail("Verification failed",
                    "The update no longer passes verification and was not installed. Open the release page to update manually.", "");

            Directory.CreateDirectory(staged);
            var stagedApp = Path.Combine(staged, Path.GetFileName(src));
            var dit = await _runner.RunAsync(Ditto, new[] { src, stagedApp }, null, ct);
            if (!dit.Success) return AppleOpResult.Fail("Copy failed", FirstLine(dit.StdErr), dit.StdErr);

            var det = await _runner.RunAsync(Hdiutil, new[] { "detach", mount, "-force" }, null, ct);
            attached = !det.Success;   // if detach failed, the finally retries

            await File.WriteAllTextAsync(script, BuildSwapScript(Environment.ProcessId), ct);

            // Fire-and-forget detached — deliberately NOT through ProcessRunner (which kills
            // its tree on exit); this must outlive us. ArgumentList shell-escapes each path,
            // so no path can inject shell. It reparents to launchd when we quit.
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add(script);           // $0 (self-deletes)
            psi.ArgumentList.Add(installedAppPath); // $1
            psi.ArgumentList.Add(stagedApp);        // $2
            psi.ArgumentList.Add(staged);           // $3
            psi.ArgumentList.Add(dmgPath);          // $4
            System.Diagnostics.Process.Start(psi);
            launched = true;
            return AppleOpResult.Ok("Installing", "MacSign will relaunch on the new version.", "");
        }
        finally
        {
            if (attached) await _runner.RunAsync(Hdiutil, new[] { "detach", mount, "-force" }, null, ct);
            if (!launched)
            {
                try { if (Directory.Exists(staged)) Directory.Delete(staged, true); } catch { /* best-effort */ }
                try { if (File.Exists(script)) File.Delete(script); } catch { /* best-effort */ }
                try { if (File.Exists(dmgPath)) File.Delete(dmgPath); } catch { /* best-effort */ }
            }
        }
    }

    /// <summary>The detached swap+relaunch script. Paths are passed as positional args
    /// ($1=installed .app, $2=staged .app, $3=staged dir, $4=downloaded .dmg) so NOTHING
    /// is interpolated into the shell — only the integer pid is. Crash-safe: the old
    /// bundle is renamed aside before the new one moves in, with rollback on failure.</summary>
    public static string BuildSwapScript(int pid)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/sh");
        sb.AppendLine($"while kill -0 {pid} 2>/dev/null; do sleep 0.2; done");
        sb.AppendLine("/bin/rm -rf \"$1.new\" \"$1.old\"");
        sb.AppendLine("/usr/bin/ditto \"$2\" \"$1.new\" || exit 1");
        sb.AppendLine("if [ -e \"$1\" ]; then /bin/mv \"$1\" \"$1.old\"; fi");
        sb.AppendLine("if /bin/mv \"$1.new\" \"$1\"; then /bin/rm -rf \"$1.old\"; else [ -e \"$1.old\" ] && /bin/mv \"$1.old\" \"$1\"; exit 1; fi");
        sb.AppendLine("/bin/rm -rf \"$3\" \"$4\"");
        sb.AppendLine("/usr/bin/open \"$1\"");
        sb.AppendLine("/bin/rm -f \"$0\"");
        return sb.ToString();
    }

    private static string FirstLine(string s) => (s ?? "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
}
