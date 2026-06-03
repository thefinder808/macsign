using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MacSign.App.Services;

namespace MacSign.App.Tests;

/// <summary>
/// Unit tests for <see cref="AppleSigningService"/> using a fake process runner.
/// These pin the security-critical behavior: exact argv per option, signing only
/// with an allow-listed identity, and zip-then-submit + temp cleanup on notarize.
/// </summary>
public class AppleSigningServiceTests
{
    private const string Sha = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
    private const string IdName = "Developer ID Application: Nathaniel Graham (Q6LRJQSA42)";

    private static string FindIdentityOutput =>
        $"  1) {Sha} \"{IdName}\"\n  2) 1111111111111111111111111111111111111111 \"Apple Development: someone (X)\"\n     2 valid identities found\n";

    private sealed class FakeRunner : IProcessRunner
    {
        public readonly List<(string File, List<string> Args)> Calls = new();
        public Func<string, IReadOnlyList<string>, ProcessResult> Respond =
            (_, _) => new ProcessResult(0, "", "", false);

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> args,
            IProgress<string>? onOutput, CancellationToken ct)
        {
            Calls.Add((fileName, args.ToList()));
            return Task.FromResult(Respond(fileName, args));
        }
    }

    private static string MakeApp()
    {
        var app = Path.Combine(Path.GetTempPath(),
            "macsign-tests-" + Guid.NewGuid().ToString("N"),
            "App" + Guid.NewGuid().ToString("N")[..6] + ".app");
        Directory.CreateDirectory(app);
        return app;
    }

    private static FakeRunner IdentityAware(Func<string, IReadOnlyList<string>, ProcessResult>? others = null)
    {
        var f = new FakeRunner();
        f.Respond = (file, args) =>
            file.EndsWith("security", StringComparison.Ordinal)
                ? new ProcessResult(0, FindIdentityOutput, "", false)
                : (others?.Invoke(file, args) ?? new ProcessResult(0, "", "", false));
        return f;
    }

    [Fact]
    public async Task ListIdentities_parses_find_identity_output()
    {
        var f = IdentityAware();
        var svc = new AppleSigningService(f);

        var ids = await svc.ListIdentitiesAsync(default);

        Assert.Equal(2, ids.Count);
        Assert.Equal(Sha, ids[0].Sha1);
        Assert.Equal(IdName, ids[0].Name);
    }

    [Fact]
    public async Task Sign_minimal_builds_expected_argv()
    {
        var app = MakeApp();
        var f = IdentityAware();
        var svc = new AppleSigningService(f);

        var r = await svc.SignAsync(app, new SigningIdentity(Sha, "ignored"),
            entitlementsPath: null, hardenedRuntime: false, deep: false, log: null, ct: default);

        Assert.True(r.Success);
        var codesign = f.Calls.Single(c => c.File.EndsWith("codesign", StringComparison.Ordinal));
        Assert.Equal(new[] { "--force", "--timestamp", "--sign", Sha, app }, codesign.Args);
    }

    [Fact]
    public async Task Sign_full_includes_hardened_deep_and_entitlements()
    {
        var app = MakeApp();
        var ent = Path.Combine(Path.GetDirectoryName(app)!, "app.entitlements.plist");
        File.WriteAllText(ent, "<plist/>");
        var f = IdentityAware();
        var svc = new AppleSigningService(f);

        var r = await svc.SignAsync(app, new SigningIdentity(Sha, "ignored"),
            entitlementsPath: ent, hardenedRuntime: true, deep: true, log: null, ct: default);

        Assert.True(r.Success);
        var codesign = f.Calls.Single(c => c.File.EndsWith("codesign", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "--force", "--options", "runtime", "--timestamp", "--deep",
                    "--entitlements", ent, "--sign", Sha, app },
            codesign.Args);
    }

    [Fact]
    public async Task Sign_rejects_identity_not_in_keychain()
    {
        var app = MakeApp();
        var f = IdentityAware();
        var svc = new AppleSigningService(f);

        var r = await svc.SignAsync(app, new SigningIdentity("0000000000000000000000000000000000000000", "spoofed"),
            entitlementsPath: null, hardenedRuntime: true, deep: true, log: null, ct: default);

        Assert.False(r.Success);
        Assert.Equal("Unknown identity", r.Title);
        // Crucially, codesign was NEVER invoked for an un-allow-listed identity.
        Assert.DoesNotContain(f.Calls, c => c.File.EndsWith("codesign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sign_passes_path_with_shell_metacharacters_as_one_argv_element()
    {
        var parent = Path.Combine(Path.GetTempPath(), "macsign-tests-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(parent, "Weird ; $(whoami) Name.app");
        Directory.CreateDirectory(app);
        var f = IdentityAware();
        var svc = new AppleSigningService(f);

        await svc.SignAsync(app, new SigningIdentity(Sha, "ignored"),
            null, hardenedRuntime: false, deep: false, log: null, ct: default);

        var codesign = f.Calls.Single(c => c.File.EndsWith("codesign", StringComparison.Ordinal));
        // The whole path is one opaque argv element — metacharacters are inert.
        Assert.Equal(app, codesign.Args[^1]);
    }

    [Fact]
    public async Task Sign_rejects_non_app_path()
    {
        var notApp = Path.Combine(Path.GetTempPath(), "macsign-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(notApp); // a directory, but not *.app
        var f = IdentityAware();
        var svc = new AppleSigningService(f);

        var r = await svc.SignAsync(notApp, new SigningIdentity(Sha, "x"), null, false, false, null, default);

        Assert.False(r.Success);
        Assert.DoesNotContain(f.Calls, c => c.File.EndsWith("codesign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Notarize_keychain_profile_zips_then_submits_and_cleans_up()
    {
        var app = MakeApp();
        var zip = Path.Combine(Path.GetTempPath(),
            $"macsign-notarize-{Path.GetFileNameWithoutExtension(app)}.zip");

        var f = new FakeRunner();
        f.Respond = (file, args) =>
        {
            if (file.EndsWith("ditto", StringComparison.Ordinal))
            {
                File.WriteAllText(args[^1], "zip");      // simulate the produced archive
                return new ProcessResult(0, "", "", false);
            }
            return new ProcessResult(0, "status: Accepted\n  id: 11111111-2222-3333-4444-555555555555\n", "", false);
        };
        var svc = new AppleSigningService(f);

        var r = await svc.NotarizeAsync(app,
            new NotarizeCreds { KeychainProfile = "my-notary-profile" }, log: null, ct: default);

        Assert.True(r.Success);
        var ditto = f.Calls.Single(c => c.File.EndsWith("ditto", StringComparison.Ordinal));
        Assert.Equal(new[] { "-c", "-k", "--keepParent", app, zip }, ditto.Args);
        var notary = f.Calls.Single(c => c.Args.Contains("notarytool"));
        Assert.Equal(new[] { "notarytool", "submit", zip, "--wait", "--keychain-profile", "my-notary-profile" }, notary.Args);
        Assert.False(File.Exists(zip)); // throwaway zip deleted
    }

    [Fact]
    public async Task Notarize_api_key_builds_key_args()
    {
        var app = MakeApp();
        var f = new FakeRunner
        {
            Respond = (file, args) => file.EndsWith("ditto", StringComparison.Ordinal)
                ? Touch(args[^1])
                : new ProcessResult(0, "status: Accepted\n", "", false),
        };
        var svc = new AppleSigningService(f);

        var r = await svc.NotarizeAsync(app, new NotarizeCreds
        {
            ApiKeyPath = "/keys/AuthKey_ABC.p8", ApiKeyId = "ABC123", ApiIssuer = "issuer-uuid",
        }, log: null, ct: default);

        Assert.True(r.Success);
        var notary = f.Calls.Single(c => c.Args.Contains("notarytool"));
        Assert.Contains("--key", notary.Args);
        Assert.Contains("/keys/AuthKey_ABC.p8", notary.Args);
        Assert.Contains("--key-id", notary.Args);
        Assert.Contains("ABC123", notary.Args);
        Assert.Contains("--issuer", notary.Args);
        Assert.Contains("issuer-uuid", notary.Args);
        Assert.DoesNotContain("--keychain-profile", notary.Args);
    }

    [Fact]
    public async Task Notarize_without_creds_fails_before_running_anything()
    {
        var app = MakeApp();
        var f = new FakeRunner();
        var svc = new AppleSigningService(f);

        var r = await svc.NotarizeAsync(app, new NotarizeCreds(), log: null, ct: default);

        Assert.False(r.Success);
        Assert.Empty(f.Calls);
    }

    private static ProcessResult Touch(string path)
    {
        File.WriteAllText(path, "zip");
        return new ProcessResult(0, "", "", false);
    }
}
