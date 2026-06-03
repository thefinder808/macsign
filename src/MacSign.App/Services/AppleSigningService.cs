using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MacSign.App.Services;

/// <summary>A code-signing identity in the login keychain (SHA-1 + display name).</summary>
public sealed record SigningIdentity(string Sha1, string Name);

/// <summary>What kind of artifact a path is: a .app bundle (directory), a .dmg
/// disk image (file), or something we don't sign.</summary>
public enum AppleTargetKind { App, Dmg, Unsupported }

/// <summary>Read-only verification report for a signed .app/.dmg.</summary>
public sealed record AppleSignReport(AppleTargetKind Kind, bool Valid, string? Signer,
    string? TeamId, bool HardenedRuntime, bool Stapled, bool GatekeeperAccepted, string Log);

/// <summary>Result of a notarizability pre-flight: a pass flag + a human list of
/// problems (empty when Ok) + the captured log.</summary>
public sealed record PreflightResult(bool Ok, IReadOnlyList<string> Problems, string Log)
{
    /// <summary>True only when the target is a .dmg whose contents are repairable by
    /// signing the apps inside (mounted OK, ≥1 .app found, ≥1 signing-kind problem).
    /// Drives the "Sign contents &amp; continue" recovery on the Mac apps screen.</summary>
    public bool CanSignContents { get; init; }
}

/// <summary>The outcome of one Apple operation: a human title/detail + the full log.</summary>
public sealed record AppleOpResult(bool Success, string Title, string Detail, string Log)
{
    public static AppleOpResult Ok(string title, string detail, string log) => new(true, title, detail, log);
    public static AppleOpResult Fail(string title, string detail, string log) => new(false, title, detail, log);
}

/// <summary>
/// How to authenticate to Apple's notary service: either a keychain profile name
/// (preferred — references credentials stored in the keychain, so the name itself
/// is not a secret) or an App Store Connect API key. Profile wins when both set.
/// </summary>
public sealed record NotarizeCreds
{
    public string? KeychainProfile { get; init; }
    public string? ApiKeyPath { get; init; }
    public string? ApiKeyId { get; init; }
    public string? ApiIssuer { get; init; }

    public bool HasKeychainProfile => !string.IsNullOrWhiteSpace(KeychainProfile);
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKeyPath)
        && !string.IsNullOrWhiteSpace(ApiKeyId) && !string.IsNullOrWhiteSpace(ApiIssuer);
    public bool IsComplete => HasKeychainProfile || HasApiKey;
}

/// <summary>
/// Drives Apple's own command-line tools (codesign / xcrun notarytool /
/// xcrun stapler / spctl / security / ditto) to sign, notarize and staple a
/// .app bundle or a .dmg disk image. It does NOT reimplement Apple code signing —
/// it orchestrates the canonical tooling. Every call goes through an
/// <see cref="IProcessRunner"/> with an argv list (no shell), and signing is gated
/// on an identity allow-list parsed from the keychain, so user input never reaches
/// a shell or an unchecked --sign.
/// </summary>
public sealed class AppleSigningService
{
    // Pinned absolute tool paths so a hijacked PATH can't substitute a trojan tool.
    private const string Codesign = "/usr/bin/codesign";
    private const string Security = "/usr/bin/security";
    private const string Xcrun = "/usr/bin/xcrun";
    private const string Ditto = "/usr/bin/ditto";
    private const string Spctl = "/usr/sbin/spctl";
    private const string Hdiutil = "/usr/bin/hdiutil";

    private readonly IProcessRunner _runner;

    public AppleSigningService(IProcessRunner? runner = null) => _runner = runner ?? new ProcessRunner();

    /// <summary>Classify a target path: a .app bundle (a directory), a .dmg disk
    /// image (a file), or unsupported.</summary>
    public static AppleTargetKind Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return AppleTargetKind.Unsupported;
        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
            return AppleTargetKind.App;
        if (path.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            return AppleTargetKind.Dmg;
        return AppleTargetKind.Unsupported;
    }

    // e.g.   1) ABCD…(40 hex) "Developer ID Application: Name (TEAMID)"
    private static readonly Regex IdentityLine =
        new(@"^\s*\d+\)\s+([0-9A-Fa-f]{40})\s+""(.+)""\s*$", RegexOptions.Compiled);

    /// <summary>List code-signing identities via <c>security find-identity</c>.
    /// This is the only <c>security</c> call — it lists signing identities (never
    /// scans keychain profiles).</summary>
    public async Task<IReadOnlyList<SigningIdentity>> ListIdentitiesAsync(CancellationToken ct)
    {
        var r = await _runner.RunAsync(Security,
            new[] { "find-identity", "-v", "-p", "codesigning" }, null, ct);
        var list = new List<SigningIdentity>();
        if (!r.Success) return list;
        foreach (var line in r.StdOut.Split('\n'))
        {
            var m = IdentityLine.Match(line);
            if (m.Success) list.Add(new SigningIdentity(m.Groups[1].Value, m.Groups[2].Value));
        }
        return list;
    }

    public async Task<AppleOpResult> SignAsync(string targetPath, SigningIdentity identity,
        string? entitlementsPath, bool hardenedRuntime, bool deep,
        IProgress<string>? log, CancellationToken ct)
    {
        var kind = Classify(targetPath);
        if (kind == AppleTargetKind.Unsupported)
            return AppleOpResult.Fail("Unsupported target", "Choose a .app bundle or a .dmg file.", "");
        // Entitlements / hardened-runtime / deep only apply to a .app; ignored for a .dmg.
        if (kind == AppleTargetKind.App && entitlementsPath is not null &&
            (!File.Exists(entitlementsPath) || !entitlementsPath.EndsWith(".plist", StringComparison.OrdinalIgnoreCase)))
            return AppleOpResult.Fail("Bad entitlements", "Entitlements must be an existing .plist file.", "");

        // Allow-list the chosen identity against the live keychain. User-provided
        // text never reaches --sign; we sign by the canonical, unambiguous SHA-1.
        var known = await ListIdentitiesAsync(ct);
        var match = known.FirstOrDefault(i => i.Sha1.Equals(identity.Sha1, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return AppleOpResult.Fail("Unknown identity",
                "That signing identity isn’t in the keychain. Refresh and pick again.", "");

        var args = new List<string> { "--force" };
        if (kind == AppleTargetKind.App)
        {
            if (hardenedRuntime) { args.Add("--options"); args.Add("runtime"); }
            args.Add("--timestamp");
            if (deep) args.Add("--deep");
            if (entitlementsPath is not null) { args.Add("--entitlements"); args.Add(entitlementsPath); }
        }
        else // Dmg: a disk image is signed as a flat blob — no runtime/deep/entitlements.
        {
            args.Add("--timestamp");
        }
        args.Add("--sign"); args.Add(match.Sha1);
        args.Add(targetPath);

        var r = await _runner.RunAsync(Codesign, args, log, ct);
        return Outcome(r, "Signed", $"codesign as {match.Name}", "Signing");
    }

    /// <summary>Integrity verify — confirms the signature/seal (passes before
    /// notarization). Gatekeeper acceptance is a separate, later step
    /// (<see cref="AssessAsync"/>).</summary>
    public async Task<AppleOpResult> VerifyAsync(string targetPath, IProgress<string>? log, CancellationToken ct)
    {
        var kind = Classify(targetPath);
        var args = kind == AppleTargetKind.App
            ? new[] { "--verify", "--deep", "--strict", "--verbose=2", targetPath }
            : new[] { "--verify", "--strict", "--verbose=2", targetPath };
        var r = await _runner.RunAsync(Codesign, args, log, ct);
        return Outcome(r, "Verified", "Signature is valid.", "Verification");
    }

    /// <summary>Best-effort Gatekeeper assessment (spctl). Meaningful only after a
    /// successful staple — a signed-but-unnotarized artifact is expected to be
    /// rejected, so callers treat this as informational, never a hard gate.</summary>
    public async Task<AppleOpResult> AssessAsync(string targetPath, IProgress<string>? log, CancellationToken ct)
    {
        var kind = Classify(targetPath);
        var args = kind == AppleTargetKind.App
            ? new[] { "--assess", "--type", "exec", "-vv", targetPath }
            : new[] { "--assess", "--type", "open", "--context", "context:primary-signature", "-v", targetPath };
        var r = await _runner.RunAsync(Spctl, args, log, ct);
        return Outcome(r, "Gatekeeper accepted", "spctl accepts the artifact.", "Assessment");
    }

    public async Task<AppleOpResult> NotarizeAsync(string targetPath, NotarizeCreds creds,
        IProgress<string>? log, CancellationToken ct)
    {
        if (!creds.IsComplete)
            return AppleOpResult.Fail("Notary credentials missing",
                "Enter a keychain profile or an API key.", "");
        var kind = Classify(targetPath);
        if (kind == AppleTargetKind.Unsupported)
            return AppleOpResult.Fail("Unsupported target", "Choose a .app bundle or a .dmg file.", "");

        // notarytool takes a .dmg directly; a .app must be zipped first (preserving
        // the bundle). The zip is a throwaway, cleaned up in the finally.
        string submitPath = targetPath;
        string? tempZip = null;
        try
        {
            if (kind == AppleTargetKind.App)
            {
                tempZip = Path.Combine(Path.GetTempPath(),
                    $"macsign-notarize-{Path.GetFileNameWithoutExtension(targetPath)}.zip");
                log?.Report($"Zipping {Path.GetFileName(targetPath)}…");
                var dz = await _runner.RunAsync(Ditto,
                    new[] { "-c", "-k", "--keepParent", targetPath, tempZip }, log, ct);
                if (dz.Canceled) return AppleOpResult.Fail("Canceled", "Notarization was canceled.", dz.StdErr);
                if (!dz.Success) return AppleOpResult.Fail("Zip failed", FirstLine(dz.StdErr), dz.StdErr + dz.StdOut);
                submitPath = tempZip;
            }

            var args = new List<string> { "notarytool", "submit", submitPath, "--wait" };
            if (creds.HasKeychainProfile)
            {
                args.Add("--keychain-profile"); args.Add(creds.KeychainProfile!);
            }
            else
            {
                args.Add("--key"); args.Add(creds.ApiKeyPath!);
                args.Add("--key-id"); args.Add(creds.ApiKeyId!);
                args.Add("--issuer"); args.Add(creds.ApiIssuer!);
            }

            log?.Report("Submitting to Apple notary service (this can take a few minutes)…");
            var nt = await _runner.RunAsync(Xcrun, args, log, ct);
            var combined = nt.StdOut + nt.StdErr;
            if (nt.Canceled) return AppleOpResult.Fail("Canceled", "Notarization was canceled.", combined);

            // notarytool --wait exits 0 only on Accepted; confirm the status line too.
            bool accepted = nt.Success &&
                combined.Contains("status: Accepted", StringComparison.OrdinalIgnoreCase);
            if (!accepted)
            {
                var id = SubmissionId(combined);
                var hint = id is null ? "" : $" Submission id {id} — run `xcrun notarytool log {id}` for details.";
                var why = combined.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
                    ? "Apple rejected the upload." : FirstLine(nt.StdErr);
                return AppleOpResult.Fail("Notarization failed", why + hint, combined);
            }
            return AppleOpResult.Ok("Notarized", "Apple accepted the submission.", combined);
        }
        finally
        {
            try { if (tempZip is not null && File.Exists(tempZip)) File.Delete(tempZip); } catch { /* best-effort */ }
        }
    }

    public async Task<AppleOpResult> StapleAsync(string targetPath, IProgress<string>? log, CancellationToken ct)
    {
        var st = await _runner.RunAsync(Xcrun, new[] { "stapler", "staple", targetPath }, log, ct);
        if (st.Canceled) return AppleOpResult.Fail("Canceled", "Stapling was canceled.", st.StdErr);
        if (!st.Success) return AppleOpResult.Fail("Staple failed", FirstLine(st.StdErr + st.StdOut), st.StdOut + st.StdErr);

        var v = await _runner.RunAsync(Xcrun, new[] { "stapler", "validate", targetPath }, log, ct);
        if (v.Canceled) return AppleOpResult.Fail("Canceled", "Stapling was canceled.", v.StdErr);
        return v.Success
            ? AppleOpResult.Ok("Stapled", "Ticket stapled and validated.", st.StdOut + v.StdOut)
            : AppleOpResult.Fail("Validate failed", FirstLine(v.StdErr + v.StdOut), v.StdOut + v.StdErr);
    }

    /// <summary>Read-only report on a signed .app/.dmg — signer, Team ID, Hardened
    /// Runtime, notarization ticket, and Gatekeeper acceptance. For a .dmg this
    /// reports the disk image's OWN state (not its contents).</summary>
    public async Task<AppleSignReport> InspectAsync(string path, CancellationToken ct)
    {
        var kind = Classify(path);
        var d = await _runner.RunAsync(Codesign, new[] { "-d", "-vvv", path }, null, ct);
        var verify = await _runner.RunAsync(Codesign,
            kind == AppleTargetKind.App
                ? new[] { "--verify", "--deep", "--strict", "--verbose=2", path }
                : new[] { "--verify", "--strict", "--verbose=2", path }, null, ct);
        var staple = await _runner.RunAsync(Xcrun, new[] { "stapler", "validate", path }, null, ct);
        var spArgs = kind == AppleTargetKind.App
            ? new[] { "--assess", "--type", "exec", "-vv", path }
            : new[] { "--assess", "--type", "open", "--context", "context:primary-signature", "-v", path };
        var spctl = await _runner.RunAsync(Spctl, spArgs, null, ct);

        var info = d.StdOut + "\n" + d.StdErr;   // codesign -d writes to stderr
        bool hardened = info.Contains("flags=", StringComparison.Ordinal)
            && info.Contains("(runtime)", StringComparison.Ordinal);
        return new AppleSignReport(kind, verify.Success,
            Match(info, @"^Authority=(.+)$"), Match(info, @"^TeamIdentifier=(.+)$"),
            hardened, staple.Success, spctl.Success,
            info + "\n" + verify.StdErr + verify.StdOut + "\n" + staple.StdOut + staple.StdErr + "\n" + spctl.StdOut + spctl.StdErr);
    }

    /// <summary>Heuristic notarizability pre-flight (notarytool is the authority).
    /// For a .app: deep-verify + Hardened-Runtime check. For a .dmg: mount read-only,
    /// check each .app bundle inside, always detach.</summary>
    public async Task<PreflightResult> PreflightAsync(string path, IProgress<string>? log, CancellationToken ct)
    {
        var kind = Classify(path);
        var problems = new List<string>();
        var logbuf = new System.Text.StringBuilder();
        bool dmgHadApps = false;

        if (kind == AppleTargetKind.App)
        {
            await CheckBundleAsync(path, problems, logbuf, log, ct);
        }
        else if (kind == AppleTargetKind.Dmg)
        {
            var mount = Path.Combine(Path.GetTempPath(), "macsign-preflight-" + Guid.NewGuid().ToString("N"));
            log?.Report($"Mounting {Path.GetFileName(path)} to inspect its contents…");
            var att = await _runner.RunAsync(Hdiutil,
                new[] { "attach", "-nobrowse", "-readonly", "-mountpoint", mount, path }, log, ct);
            if (att.Canceled) return new PreflightResult(false, new[] { "Pre-flight was canceled." }, att.StdErr);
            if (!att.Success) return new PreflightResult(false, new[] { "Could not mount the .dmg to inspect it." }, att.StdErr + att.StdOut);
            try
            {
                var apps = Directory.Exists(mount)
                    ? Directory.EnumerateDirectories(mount, "*.app").ToList()
                    : new List<string>();
                dmgHadApps = apps.Count > 0;
                if (apps.Count == 0) problems.Add("No .app bundle found inside the .dmg to check.");
                foreach (var a in apps) await CheckBundleAsync(a, problems, logbuf, log, ct);
            }
            finally
            {
                await _runner.RunAsync(Hdiutil, new[] { "detach", mount, "-force" }, null, ct);
                try { if (Directory.Exists(mount)) Directory.Delete(mount); } catch { /* best-effort */ }
            }
        }
        else
        {
            problems.Add("Choose a .app bundle or a .dmg file.");
        }

        return new PreflightResult(problems.Count == 0, problems, logbuf.ToString())
        {
            CanSignContents = kind == AppleTargetKind.Dmg && dmgHadApps && problems.Count > 0,
        };
    }

    /// <summary>The signing state of a bundle used by BOTH PreflightAsync (to build
    /// the problem list) and SignDmgContentsAsync (to decide which apps to re-sign),
    /// so detection and repair can't drift: deep-verify result, the Hardened-Runtime
    /// flag, and the verify stderr for messaging.</summary>
    private async Task<(bool VerifyOk, bool Hardened, string VerifyErr)> BundleStateAsync(
        string appPath, IProgress<string>? log, CancellationToken ct)
    {
        var v = await _runner.RunAsync(Codesign,
            new[] { "--verify", "--deep", "--strict", "--verbose=2", appPath }, log, ct);
        var d = await _runner.RunAsync(Codesign, new[] { "-d", "--verbose", appPath }, null, ct);
        var info = d.StdOut + d.StdErr;
        bool hardened = info.Contains("flags=", StringComparison.Ordinal)
            && info.Contains("(runtime)", StringComparison.Ordinal);
        return (v.Success, hardened, v.StdErr);
    }

    private async Task CheckBundleAsync(string appPath, List<string> problems,
        System.Text.StringBuilder logbuf, IProgress<string>? log, CancellationToken ct)
    {
        var name = Path.GetFileName(appPath);
        var (verifyOk, hardened, verifyErr) = await BundleStateAsync(appPath, log, ct);
        logbuf.AppendLine($"$ codesign --verify --deep {name}").AppendLine(verifyErr);
        if (!verifyOk) problems.Add($"{name}: nested code is unsigned or invalid ({FirstLine(verifyErr)}).");
        if (!hardened) problems.Add($"{name}: Hardened Runtime not enabled (required for notarization).");
    }

    /// <summary>Sign the unsigned/broken .app bundle(s) INSIDE a .dmg, then re-seal the
    /// image in place: convert to read-write, mount, sign only the apps that fail the
    /// pre-flight checks (with the given entitlements + Hardened Runtime + deep), detach,
    /// recompress, and atomically swap over the original. Reconverting invalidates the
    /// DMG's OWN signature, so the caller must (re-)sign the .dmg after this returns.
    /// Only top-level *.app bundles are touched (matching PreflightAsync).</summary>
    public async Task<AppleOpResult> SignDmgContentsAsync(string dmgPath, SigningIdentity identity,
        string? entitlementsPath, IProgress<string>? log, CancellationToken ct)
    {
        if (Classify(dmgPath) != AppleTargetKind.Dmg)
            return AppleOpResult.Fail("Unsupported target", "Choose a .dmg file.", "");

        var dir = Path.GetDirectoryName(Path.GetFullPath(dmgPath))!;
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var rw = Path.Combine(dir, $".macsign-rw-{stamp}.dmg");      // read-write working copy
        var outDmg = Path.Combine(dir, $".macsign-out-{stamp}.dmg"); // recompressed result (same dir → atomic move)
        var mount = Path.Combine(Path.GetTempPath(), "macsign-sign-" + stamp);
        bool attached = false;
        try
        {
            log?.Report("Converting the disk image to read-write…");
            var c1 = await _runner.RunAsync(Hdiutil,
                new[] { "convert", dmgPath, "-format", "UDRW", "-o", rw }, log, ct);
            if (c1.Canceled) return AppleOpResult.Fail("Canceled", "Signing contents was canceled.", c1.StdErr);
            if (!c1.Success) return AppleOpResult.Fail("Convert failed",
                "Could not convert the .dmg to read-write (is it encrypted?).", c1.StdErr + c1.StdOut);

            // Grow the working image so the bytes a signature adds will fit on a tight volume.
            // Best-effort: if it can't grow, signing may still succeed (or fail ENOSPC — see plan limitations).
            // Guard: convert produces <rw> in real runs; skip resize when it's absent (e.g. fakes/no-op).
            if (File.Exists(rw))
            {
                long targetMb = new FileInfo(rw).Length / (1024 * 1024) + 64;
                await _runner.RunAsync(Hdiutil, new[] { "resize", "-size", $"{targetMb}m", rw }, log, ct);
            }

            log?.Report("Mounting the disk image…");
            var att = await _runner.RunAsync(Hdiutil,
                new[] { "attach", "-nobrowse", "-owners", "on", "-readwrite", "-mountpoint", mount, rw }, log, ct);
            if (att.Canceled) return AppleOpResult.Fail("Canceled", "Signing contents was canceled.", att.StdErr);
            if (!att.Success) return AppleOpResult.Fail("Mount failed",
                "Could not mount the read-write image.", att.StdErr + att.StdOut);
            attached = true;

            var apps = Directory.Exists(mount)
                ? Directory.EnumerateDirectories(mount, "*.app").ToList()
                : new List<string>();
            if (apps.Count == 0)
                return AppleOpResult.Fail("Nothing to sign", "No .app bundle found inside the .dmg.", att.StdOut);

            int signed = 0;
            foreach (var app in apps)
            {
                var name = Path.GetFileName(app);
                // Skip-probe only — don't stream this verify to the live log (pre-flight does that).
                var (verifyOk, hardened, _) = await BundleStateAsync(app, null, ct);
                if (verifyOk && hardened) { log?.Report($"{name}: already signed — skipping."); continue; }
                log?.Report($"Signing {name} inside the image…");
                var sr = await SignAsync(app, identity, entitlementsPath, hardenedRuntime: true, deep: true, log, ct);
                if (!sr.Success) return AppleOpResult.Fail("Contents signing failed", $"{name}: {sr.Detail}", sr.Log);
                signed++;
            }

            // Detach BEFORE recompressing — hdiutil convert fails on a mounted image.
            await _runner.RunAsync(Hdiutil, new[] { "detach", mount, "-force" }, null, ct);
            attached = false;

            log?.Report("Recompressing the disk image…");
            var c2 = await _runner.RunAsync(Hdiutil,
                new[] { "convert", rw, "-format", "UDZO", "-o", outDmg }, log, ct);
            if (c2.Canceled) return AppleOpResult.Fail("Canceled", "Signing contents was canceled.", c2.StdErr);
            if (!c2.Success || !File.Exists(outDmg)) return AppleOpResult.Fail("Recompress failed",
                "Could not recompress the signed image.", c2.StdErr + c2.StdOut);

            File.Move(outDmg, dmgPath, overwrite: true); // same-volume move — atomic on the same filesystem; copy+delete across volumes
            return AppleOpResult.Ok("Contents signed",
                signed == 0
                    ? $"All apps inside {Path.GetFileName(dmgPath)} were already signed; re-sealed the image."
                    : $"Signed {signed} app(s) inside {Path.GetFileName(dmgPath)} and re-sealed the image.",
                c1.StdOut + c2.StdOut);
        }
        finally
        {
            if (attached) await _runner.RunAsync(Hdiutil, new[] { "detach", mount, "-force" }, null, ct);
            try { if (File.Exists(rw)) File.Delete(rw); } catch { /* best-effort */ }
            try { if (File.Exists(outDmg)) File.Delete(outDmg); } catch { /* best-effort */ }
            try { if (Directory.Exists(mount)) Directory.Delete(mount); } catch { /* best-effort */ }
        }
    }

    private static string? Match(string s, string pattern)
    {
        var m = Regex.Match(s, pattern, RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static AppleOpResult Outcome(ProcessResult r, string okTitle, string okDetail, string verb) =>
        r.Canceled ? AppleOpResult.Fail("Canceled", $"{verb} was canceled.", r.StdErr)
        : r.Success ? AppleOpResult.Ok(okTitle, okDetail, r.StdOut + r.StdErr)
        : AppleOpResult.Fail($"{verb} failed", FirstLine(r.StdErr + r.StdOut), r.StdOut + r.StdErr);

    private static string FirstLine(string s)
    {
        if (!string.IsNullOrWhiteSpace(s))
            foreach (var line in s.Split('\n'))
                if (line.Trim() is { Length: > 0 } t) return t;
        return "See log for details.";
    }

    private static readonly Regex SubmissionIdRegex =
        new(@"\bid:\s*([0-9a-fA-F-]{36})", RegexOptions.Compiled);

    private static string? SubmissionId(string s)
    {
        var m = SubmissionIdRegex.Match(s);
        return m.Success ? m.Groups[1].Value : null;
    }
}
