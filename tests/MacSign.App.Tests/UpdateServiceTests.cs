using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("1.1.0",  "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.0", "1.0.0", false)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("garbage","1.0.0", false)]
    [InlineData("v1.1.0", "dev",   false)]   // dev build never auto-updates
    public void IsNewer_compares(string latest, string current, bool expected)
        => Assert.Equal(expected, UpdateService.IsNewer(latest, current));

    private static string HostArch() =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    [Fact]
    public void AssetFor_picksHostArch()
    {
        var names = new[] { "MacSign-1.1.0-osx-arm64.dmg", "MacSign-1.1.0-osx-x64.dmg" };
        Assert.Equal($"MacSign-1.1.0-osx-{HostArch()}.dmg", UpdateService.AssetNameFor(names, "1.1.0"));
    }

    [Fact]
    public void AssetFor_noMatch_returnsNull()
        => Assert.Null(UpdateService.AssetNameFor(new[] { "MacSign-1.1.0-win-x64.zip" }, "1.1.0"));

    [Fact]
    public void AssetFor_requiresExactName_ignoresStrayAssets()
    {
        var names = new[]
        {
            $"Evil-osx-{HostArch()}.dmg",                 // wrong product, right arch suffix
            $"MacSign-9.9.9-osx-{HostArch()}.dmg",        // the only exact match
            $"MacSign-9.9.8-osx-{HostArch()}.dmg",        // wrong version
        };
        Assert.Equal($"MacSign-9.9.9-osx-{HostArch()}.dmg", UpdateService.AssetNameFor(names, "9.9.9"));
    }

    [Fact]
    public void AssetFor_wrongVersionInName_returnsNull()
        => Assert.Null(UpdateService.AssetNameFor(new[] { $"MacSign-9.9.8-osx-{HostArch()}.dmg" }, "9.9.9"));

    private const string LatestJson = """
    {
      "tag_name": "v9.9.9",
      "html_url": "https://github.com/thefinder808/macsign/releases/tag/v9.9.9",
      "body": "Shiny new things.",
      "assets": [
        { "name": "MacSign-9.9.9-osx-arm64.dmg", "browser_download_url": "https://example.test/arm64.dmg" },
        { "name": "MacSign-9.9.9-osx-x64.dmg",   "browser_download_url": "https://example.test/x64.dmg" }
      ]
    }
    """;

    [Fact]
    public async Task CheckAsync_findsNewer_picksArchAsset()
    {
        var svc = new UpdateService(FakeHttp.ClientReturning(LatestJson));
        var r = await svc.CheckAsync(default);

        Assert.True(r.UpdateAvailable);
        Assert.Equal("9.9.9", r.Info!.Version);
        Assert.Equal("Shiny new things.", r.Info.ReleaseNotes);
        Assert.EndsWith(".dmg", r.Info.AssetUrl);
        Assert.Contains(RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64", r.Info.AssetName);
    }

    [Fact]
    public async Task CheckAsync_sameVersion_noUpdate()
    {
        var json = LatestJson.Replace("9.9.9", "0.0.1");   // older than the dev assembly version
        var svc = new UpdateService(FakeHttp.ClientReturning(json));
        var r = await svc.CheckAsync(default);
        Assert.False(r.UpdateAvailable);
        Assert.Null(r.Info);
    }

    [Fact]
    public async Task CheckAsync_httpError_returnsError_notThrow()
    {
        var http = new HttpClient(new FakeHttp { Respond = _ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) });
        var r = await new UpdateService(http).CheckAsync(default);
        Assert.False(r.UpdateAvailable);
        Assert.NotNull(r.Error);
    }

    // ---- VerifyAsync tests ----

    private static UpdateService SvcWith(FakeRunner r) =>
        new(new HttpClient(new FakeHttp()), new AppleSigningService(r), r);

    // A real temp .dmg file so Classify() recognises it as a disk image.
    private static string MakeDmg()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macsign-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dmg = Path.Combine(dir, "MacSign-9.9.9-osx-arm64.dmg");
        File.WriteAllText(dmg, "not-a-real-dmg");
        return dmg;
    }

    /// <summary>Simulates a real MacSign release: the .dmg CONTAINER is notarized +
    /// stapled but is NOT codesigned, while the .app INSIDE is Developer-ID signed +
    /// notarized. Routes by the target path's suffix (.dmg vs .app) so the trust gate
    /// is exercised against the inner app, not the container. This is the exact shape
    /// the old container-only VerifyAsync wrongly rejected.</summary>
    private const string GoodVersion = "9.9.9";

    private static FakeRunner ReleaseShapeRunner(string? innerTeam = UpdateService.ExpectedTeamId,
        bool innerVerifyOk = true, bool innerSpctlOk = true, bool dmgStapleOk = true,
        string? innerBundleId = UpdateService.ExpectedBundleId, string innerExe = "MacSign",
        string plistVersion = GoodVersion, string[]? appDirNames = null)
    {
        appDirNames ??= new[] { "MacSign.app" };
        var teamLine = innerTeam is null ? "" : $"TeamIdentifier={innerTeam}\n";
        var idLine = innerBundleId is null ? "" : $"Identifier={innerBundleId}\n";
        // codesign -d also emits the signed CodeDirectory Identifier + Executable lines.
        var appInfo = $"Executable=/Volumes/MacSign/MacSign.app/Contents/MacOS/{innerExe}\n" +
                      $"Authority=Developer ID Application: Test\n{idLine}{teamLine}flags=0x10000(runtime)\n";
        return new FakeRunner { Respond = (file, args) =>
        {
            var a = args.ToList();
            var target = a.Count > 0 ? a[^1] : "";
            bool onApp = target.EndsWith(".app", StringComparison.Ordinal);
            bool onDmg = target.EndsWith(".dmg", StringComparison.Ordinal);

            if (file.EndsWith("hdiutil") && a.Contains("attach"))
            {
                var mp = a[a.IndexOf("-mountpoint") + 1];
                foreach (var n in appDirNames) Directory.CreateDirectory(Path.Combine(mp, n));
                return new ProcessResult(0, "", "", false);
            }
            // PlistBuddy reads CFBundleShortVersionString from the inner app.
            if (file.EndsWith("PlistBuddy"))
                return new ProcessResult(0, plistVersion, "", false);
            // codesign -d: the inner app reports our identity; the container has none.
            if (file.EndsWith("codesign") && a.Contains("-d"))
                return onApp ? new ProcessResult(0, "", appInfo, false)
                             : new ProcessResult(1, "", "code object is not signed at all", false);
            if (file.EndsWith("codesign") && a.Contains("--verify"))
                return onApp ? new ProcessResult(innerVerifyOk ? 0 : 1, "", "", false)
                             : new ProcessResult(1, "", "code object is not signed at all", false);
            // The ticket lives on the .dmg, not the inner .app.
            if (file.EndsWith("xcrun") && a.Contains("stapler"))
                return onDmg ? new ProcessResult(dmgStapleOk ? 0 : 1, "", "", false)
                             : new ProcessResult(1, "", "does not have a ticket stapled", false);
            if (file.EndsWith("spctl"))
                return onApp ? new ProcessResult(innerSpctlOk ? 0 : 1, "", "", false)
                             : new ProcessResult(1, "", "no usable signature", false);
            return new ProcessResult(0, "", "", false); // detach, etc.
        }};
    }

    [Fact]
    public async Task VerifyAsync_acceptsRealReleaseShape_unsignedDmg_signedNotarizedInnerApp()
        => Assert.True(await SvcWith(ReleaseShapeRunner()).VerifyAsync(MakeDmg(), GoodVersion, default));

    [Theory]
    [InlineData("WRONGTEAM0", true,  true,  true)]   // inner app signed by a different Team ID
    [InlineData(null,         true,  true,  true)]   // inner app has no TeamIdentifier line
    [InlineData(UpdateService.ExpectedTeamId, false, true,  true)]   // inner app integrity fails
    [InlineData(UpdateService.ExpectedTeamId, true,  false, true)]   // inner app not notarized (Gatekeeper rejects)
    [InlineData(UpdateService.ExpectedTeamId, true,  true,  false)]  // the .dmg itself isn't stapled
    public async Task VerifyAsync_refuses_whenInnerAppOrDmgFails(string? team, bool v, bool sp, bool dmgStaple)
        => Assert.False(await SvcWith(ReleaseShapeRunner(team, v, sp, dmgStaple)).VerifyAsync(MakeDmg(), GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_refuses_whenInnerBundleIdIsWrong()
        => Assert.False(await SvcWith(ReleaseShapeRunner(innerBundleId: "com.evil.Clone"))
            .VerifyAsync(MakeDmg(), GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_refuses_whenInnerBundleIdMissing()
        => Assert.False(await SvcWith(ReleaseShapeRunner(innerBundleId: null))
            .VerifyAsync(MakeDmg(), GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_refuses_whenInnerExecutableIsWrong()
        => Assert.False(await SvcWith(ReleaseShapeRunner(innerExe: "NotMacSign"))
            .VerifyAsync(MakeDmg(), GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_refuses_whenAppDirNameIsWrong()
        => Assert.False(await SvcWith(ReleaseShapeRunner(appDirNames: new[] { "Imposter.app" }))
            .VerifyAsync(MakeDmg(), GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_refuses_whenMultipleTopLevelApps()
        => Assert.False(await SvcWith(ReleaseShapeRunner(appDirNames: new[] { "MacSign.app", "Extra.app" }))
            .VerifyAsync(MakeDmg(), GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_refuses_whenVersionDoesNotMatch()
        => Assert.False(await SvcWith(ReleaseShapeRunner(plistVersion: "1.2.3"))
            .VerifyAsync(MakeDmg(), GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_refuses_whenMountFails()
    {
        var f = new FakeRunner { Respond = (file, args) =>
            file.EndsWith("hdiutil") && args.Contains("attach")
                ? new ProcessResult(1, "", "hdiutil: attach failed", false)
                : new ProcessResult(0, "", "", false) };
        Assert.False(await SvcWith(f).VerifyAsync(MakeDmg(), GoodVersion, default));
    }

    [Fact]
    public async Task VerifyAsync_refuses_whenNoAppInsideDmg()
    {
        var f = new FakeRunner { Respond = (file, args) =>
        {
            var a = args.ToList();
            if (file.EndsWith("hdiutil") && a.Contains("attach"))
            { Directory.CreateDirectory(a[a.IndexOf("-mountpoint") + 1]); return new ProcessResult(0, "", "", false); }
            return new ProcessResult(0, "", "", false);
        }};
        Assert.False(await SvcWith(f).VerifyAsync(MakeDmg(), GoodVersion, default));
    }

    [Fact]
    public async Task VerifyAsync_refuses_whenNotADmg()
        => Assert.False(await SvcWith(ReleaseShapeRunner()).VerifyAsync("/tmp/does-not-exist.dmg", GoodVersion, default));

    [Fact]
    public async Task VerifyAsync_alwaysDetaches_afterInspectingInnerApp()
    {
        var f = ReleaseShapeRunner();
        await SvcWith(f).VerifyAsync(MakeDmg(), GoodVersion, default);
        Assert.Contains(f.Calls, c => c.File.EndsWith("hdiutil") && c.Args.Contains("detach"));
    }

    [Fact]
    public async Task DownloadAsync_writesTempDmg_andReportsProgress()
    {
        var bytes = new byte[4096];
        var http = new HttpClient(new FakeHttp { Respond = _ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(bytes) } });
        var svc = new UpdateService(http);
        var info = new UpdateInfo("9.9.9", "", "", "MacSign-9.9.9-osx-arm64.dmg", "https://example.test/a.dmg");

        var path = await svc.DownloadAsync(info, null, default);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.EndsWith(".dmg", path);
        Assert.Equal(4096, new FileInfo(path!).Length);
        File.Delete(path!);
    }

    private sealed class SyncProgress : IProgress<double>
    {
        public readonly List<double> Values = new();
        public void Report(double v) => Values.Add(v);
    }

    [Fact]
    public async Task DownloadAsync_reportsProgress_whenContentLengthKnown()
    {
        var bytes = new byte[4096];
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentLength = bytes.Length;   // make total known
        var http = new HttpClient(new FakeHttp { Respond = _ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content } });
        var prog = new SyncProgress();
        var info = new UpdateInfo("9.9.9", "", "", "MacSign-9.9.9-osx-arm64.dmg", "https://example.test/a.dmg");

        var path = await new UpdateService(http).DownloadAsync(info, prog, default);

        Assert.NotNull(path);
        Assert.NotEmpty(prog.Values);
        Assert.Equal(1.0, prog.Values[^1], 3);   // final report reaches 100%
        File.Delete(path!);
    }

    // ---- InstallAndRelaunchAsync tests ----

    [Fact]
    public void InstalledAppPath_resolvesTwoUpFromMacOS()
    {
        // …/MyApp.app/Contents/MacOS  ->  …/MyApp.app
        var baseDir = "/Applications/MacSign.app/Contents/MacOS";
        Assert.Equal("/Applications/MacSign.app", UpdateService.InstalledAppPathFrom(baseDir));
    }

    [Fact]
    public async Task InstallAndRelaunch_nonWritableDir_returnsRevealOutcome_doesNotQuit()
    {
        var svc = new UpdateService();
        // A bundle path whose parent is not writable (root-owned); install must refuse.
        var res = await svc.InstallAndRelaunchAsync("/tmp/whatever.dmg", GoodVersion,
            installedAppPath: "/usr/bin/MacSign.app", ct: default);
        Assert.False(res.Success);
        Assert.Contains("Applications", res.Detail);   // the "drag to Applications" fallback message
    }

    // Closes the verify→install TOCTOU: install re-runs the trust gate on the bundle it
    // mounts, so a .dmg swapped in the temp dir after VerifyAsync (here: the inner app is
    // signed by the wrong Team ID) is refused and never copied in or launched.
    [Fact]
    public async Task InstallAndRelaunch_reverifies_andRefuses_whenMountedBundleIsUntrusted()
    {
        var installDir = Path.Combine(Path.GetTempPath(), "macsign-inst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installDir);   // writable, so we get past DirWritable to the re-verify
        try
        {
            var f = ReleaseShapeRunner(innerTeam: "WRONGTEAM0");   // valid shape, wrong signer
            var res = await SvcWith(f).InstallAndRelaunchAsync(
                MakeDmg(), GoodVersion, Path.Combine(installDir, "MacSign.app"), default);

            Assert.False(res.Success);
            Assert.Contains("verification", res.Title, StringComparison.OrdinalIgnoreCase);
            // Refused BEFORE copying: no ditto (and therefore no staged bundle / relaunch).
            Assert.DoesNotContain(f.Calls, c => c.File.EndsWith("ditto"));
        }
        finally { try { Directory.Delete(installDir, true); } catch { } }
    }

    [Fact]
    public void BuildSwapScript_positionalArgs_crashSafe_noInterpolatedPaths()
    {
        var s = UpdateService.BuildSwapScript(4242);
        Assert.Contains("kill -0 4242", s);
        Assert.Contains("ditto \"$2\" \"$1.new\"", s);
        Assert.Contains("mv \"$1\" \"$1.old\"", s);        // rename old aside (crash-safe)
        Assert.Contains("mv \"$1.new\" \"$1\"", s);        // then move new in
        Assert.Contains("open \"$1\"", s);
        Assert.Contains("rm -f \"$0\"", s);                // self-delete
        Assert.DoesNotContain("/Applications", s);          // paths are NOT interpolated
        Assert.DoesNotContain("/tmp", s);
    }
}
