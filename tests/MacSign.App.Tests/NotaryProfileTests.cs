using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class StoreNotaryCredentialsTests
{
    private static string TempP8()
    {
        var p = Path.Combine(Path.GetTempPath(), "macsign-key-" + Guid.NewGuid().ToString("N") + ".p8");
        File.WriteAllText(p, "-----BEGIN PRIVATE KEY-----\nx\n-----END PRIVATE KEY-----");
        return p;
    }

    [Fact]
    public async Task Issues_store_credentials_argv_with_the_api_key()
    {
        var p8 = TempP8();
        var f = new FakeRunner();
        var r = await new AppleSigningService(f)
            .StoreNotaryCredentialsAsync("MyProfile", p8, "KEYID123", "issuer-uuid", null, default);

        Assert.True(r.Success);
        Assert.Contains(f.Calls, c => c.File.EndsWith("xcrun", StringComparison.Ordinal)
            && c.Args.SequenceEqual(new[] { "notarytool", "store-credentials", "MyProfile",
                "--key", p8, "--key-id", "KEYID123", "--issuer", "issuer-uuid" }));
        Assert.DoesNotContain(f.Calls, c => c.Args.Contains("--apple-id") || c.Args.Contains("--password"));
    }

    [Fact]
    public async Task Rejects_missing_p8_without_running_the_tool()
    {
        var f = new FakeRunner();
        var r = await new AppleSigningService(f)
            .StoreNotaryCredentialsAsync("P", "/nope/key.p8", "K", "I", null, default);

        Assert.False(r.Success);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Rejects_blank_fields_without_running_the_tool()
    {
        var f = new FakeRunner();
        var r = await new AppleSigningService(f)
            .StoreNotaryCredentialsAsync("", TempP8(), "K", "I", null, default);

        Assert.False(r.Success);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Surfaces_tool_failure()
    {
        var f = new FakeRunner { Respond = (_, _) => new ProcessResult(1, "", "Error: invalid issuer", false) };
        var r = await new AppleSigningService(f)
            .StoreNotaryCredentialsAsync("P", TempP8(), "K", "I", null, default);

        Assert.False(r.Success);
    }
}
