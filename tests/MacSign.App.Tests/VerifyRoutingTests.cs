using System;
using System.IO;
using System.Threading.Tasks;
using MacSign.App.Services;
using MacSign.App.ViewModels;

namespace MacSign.App.Tests;

/// <summary>The Verify screen routes .app/.dmg to the codesign-based Mac report,
/// and everything else to the Authenticode engine.</summary>
public class VerifyRoutingTests
{
    private static string MakeApp()
    {
        var app = Path.Combine(Path.GetTempPath(), "macsign-verify-" + Guid.NewGuid().ToString("N"), "Demo.app");
        Directory.CreateDirectory(app);
        return app;
    }

    [Fact]
    public async Task App_routes_to_mac_report()
    {
        var app = MakeApp();
        const string dvvv =
            "Authority=Developer ID Application: Nathaniel Graham (Q6LRJQSA42)\n" +
            "TeamIdentifier=Q6LRJQSA42\nCodeDirectory flags=0x10000(runtime)\n";
        var f = new FakeRunner
        {
            Respond = (file, args) => file.EndsWith("codesign", StringComparison.Ordinal)
                ? new ProcessResult(0, "", dvvv, false)
                : new ProcessResult(0, "ok", "", false),
        };
        var vm = new VerifyViewModel(new AppleSigningService(f));

        await vm.VerifyPathAsync(app);

        Assert.True(vm.IsMacReport);
        Assert.Contains("Developer ID Application", vm.Signer);
        Assert.Equal("Q6LRJQSA42", vm.MacTeamId);
        Assert.True(vm.MacHardened);
        Assert.True(vm.HasReport);
    }

    [Fact]
    public async Task Authenticode_file_uses_engine_not_mac_path()
    {
        var dll = Path.Combine(Path.GetTempPath(), "macsign-verify-" + Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(dll, new byte[] { 0x4D, 0x5A, 0x00, 0x00 }); // junk "MZ"
        var vm = new VerifyViewModel(new AppleSigningService(new FakeRunner()));

        await vm.VerifyPathAsync(dll);

        Assert.False(vm.IsMacReport);
        Assert.True(vm.HasReport);
    }
}
