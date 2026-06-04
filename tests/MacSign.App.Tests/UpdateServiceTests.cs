using System.Runtime.InteropServices;
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
}
