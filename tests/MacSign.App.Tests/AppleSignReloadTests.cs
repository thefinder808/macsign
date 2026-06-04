using System;
using System.IO;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class AppleSignReloadTests
{
    private static SettingsStore TempStore() =>
        new(Path.Combine(Path.GetTempPath(), "macsign-areload-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void ReloadFromData_rereads_apple_prefs_from_shared_data()
    {
        var data = new AppData
        {
            AppleSign = new AppleSignPrefs
            {
                NotaryProfile = "prof-x", HardenedRuntime = false, Deep = false,
                Notarize = true, Staple = false, UseApiKey = true,
            }
        };
        var vm = new AppleSignViewModel(data, TempStore(), new AppleSigningService(new FakeRunner()));

        // Mutate live state away from data, then point data at defaults and reload.
        vm.Notarize = false; vm.UseApiKey = false; vm.NotaryProfile = "changed";
        data.AppleSign = new AppleSignPrefs();
        vm.ReloadFromData();

        Assert.Equal("my-notary-profile", vm.NotaryProfile);  // empty → default UI value
        Assert.True(vm.HardenedRuntime);                     // AppleSignPrefs default
        Assert.True(vm.Deep);
        Assert.False(vm.Notarize);
        Assert.True(vm.Staple);
        Assert.False(vm.UseApiKey);
    }
}
