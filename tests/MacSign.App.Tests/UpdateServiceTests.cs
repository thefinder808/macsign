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
}
