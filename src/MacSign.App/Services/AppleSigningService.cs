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
