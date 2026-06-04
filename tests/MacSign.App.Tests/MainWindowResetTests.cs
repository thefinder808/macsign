using System;
using System.IO;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class MainWindowResetTests
{
    [Fact]
    public void ResetAll_clears_profiles_activity_and_resets_prefs()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-main-" + Guid.NewGuid().ToString("N")));
        var seed = new AppData();
        seed.Profiles.Add(new ProfileData { Name = "p" });
        seed.Activity.Add(new RunData { Status = "ok", WhenIso = "t" });
        seed.Preferences.Theme = "Dark";
        seed.Preferences.ActivityKeepLast = 500;
        store.Save(seed);

        var vm = new MainWindowViewModel(store);
        Assert.True(vm.Profiles.HasProfiles);
        Assert.False(vm.Activity.IsEmpty);

        vm.Preferences.ResetAllCommand.Execute(null);   // arm
        vm.Preferences.ResetAllCommand.Execute(null);   // fire → ResetAll

        Assert.Empty(vm.Profiles.Profiles);
        Assert.Empty(vm.Activity.Runs);
        Assert.Equal("System", vm.Preferences.Theme);
        Assert.Equal(50, vm.Preferences.ActivityKeepLast);

        var saved = store.Load();
        Assert.Empty(saved.Profiles);
        Assert.Empty(saved.Activity);
        Assert.Equal("System", saved.Preferences.Theme);
    }

    [Fact]
    public void Sign_defaults_are_seeded_from_prefs_at_construction()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-main-" + Guid.NewGuid().ToString("N")));
        var seed = new AppData();
        seed.Preferences.DefaultTimestampUrl = "http://tsa.example/ts";
        seed.Preferences.TimestampByDefault = false;
        store.Save(seed);

        var vm = new MainWindowViewModel(store);

        Assert.Equal("http://tsa.example/ts", vm.Sign.TimestampUrl);
        Assert.False(vm.Sign.TimestampEnabled);
    }
}
