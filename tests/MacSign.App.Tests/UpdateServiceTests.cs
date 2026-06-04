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

    [Fact]
    public void AssetFor_picksHostArch()
    {
        var names = new[] { "MacSign-1.1.0-osx-arm64.dmg", "MacSign-1.1.0-osx-x64.dmg" };
        var want = RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? "MacSign-1.1.0-osx-arm64.dmg" : "MacSign-1.1.0-osx-x64.dmg";
        Assert.Equal(want, UpdateService.AssetNameFor(names));
    }

    [Fact]
    public void AssetFor_noMatch_returnsNull()
        => Assert.Null(UpdateService.AssetNameFor(new[] { "MacSign-1.1.0-win-x64.zip" }));

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

    private static FakeRunner GoodDmgRunner(string? teamId = UpdateService.ExpectedTeamId,
        bool verifyOk = true, bool stapleOk = true, bool spctlOk = true)
    {
        var teamLine = teamId is null ? "" : $"TeamIdentifier={teamId}\n";
        var dInfo = $"Authority=Developer ID Application: Test\n{teamLine}flags=0x10000(runtime)\n";
        return new FakeRunner { Respond = (file, args) =>
        {
            if (file.EndsWith("codesign") && args.Contains("-d"))      return new ProcessResult(0, "", dInfo, false);
            if (file.EndsWith("codesign") && args.Contains("--verify")) return new ProcessResult(verifyOk ? 0 : 1, "", "", false);
            if (file.EndsWith("xcrun") && args.Contains("stapler"))     return new ProcessResult(stapleOk ? 0 : 1, "", "", false);
            if (file.EndsWith("spctl"))                                 return new ProcessResult(spctlOk ? 0 : 1, "", "", false);
            return new ProcessResult(0, "", "", false);
        }};
    }

    private static UpdateService SvcWith(FakeRunner r) =>
        new(new HttpClient(new FakeHttp()), new AppleSigningService(r), r);

    [Fact]
    public async Task VerifyAsync_passes_whenOurTeamId_signed_notarized()
        => Assert.True(await SvcWith(GoodDmgRunner()).VerifyAsync("/tmp/x.dmg", default));

    [Theory]
    [InlineData("WRONGTEAM0", true,  true,  true)]   // not our Developer ID
    [InlineData(null, true,  true,  true)]   // codesign output has no TeamIdentifier line
    [InlineData(UpdateService.ExpectedTeamId, false, true,  true)]   // codesign integrity fails
    [InlineData(UpdateService.ExpectedTeamId, true,  false, true)]   // not stapled (not notarized)
    [InlineData(UpdateService.ExpectedTeamId, true,  true,  false)]  // Gatekeeper rejects
    public async Task VerifyAsync_refuses_whenAnyCheckFails(string? team, bool v, bool staple, bool sp)
        => Assert.False(await SvcWith(GoodDmgRunner(team, v, staple, sp)).VerifyAsync("/tmp/x.dmg", default));
}
