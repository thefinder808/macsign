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

    // ── restore last-used credential at launch (defect 2) ──────────────────

    [Fact]
    public void RestoreLastCredential_restores_the_most_recently_used_profile()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-main-" + Guid.NewGuid().ToString("N")));
        var seed = new AppData();
        // Winner listed SECOND so a pass can't come from insertion order.
        seed.Profiles.Add(new ProfileData { Name = "older", CredMode = "Pfx", PfxPath = "/tmp/older.pfx", LastUsedIso = "2026-01-01T00:00:00-00:00" });
        seed.Profiles.Add(new ProfileData { Name = "newer", CredMode = "Pfx", PfxPath = "/tmp/newer.pfx", LastUsedIso = "2026-06-01T00:00:00-00:00" });
        store.Save(seed);

        var vm = new MainWindowViewModel(store);

        Assert.Equal("/tmp/newer.pfx", vm.Sign.PfxPath);
    }

    [Fact]
    public void RestoreLastCredential_does_nothing_when_no_profile_has_been_used()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-main-" + Guid.NewGuid().ToString("N")));
        var seed = new AppData();
        seed.Profiles.Add(new ProfileData { Name = "p", CredMode = "Pfx", PfxPath = "/tmp/p.pfx", LastUsedIso = null });
        store.Save(seed);

        var vm = new MainWindowViewModel(store);

        Assert.Equal("", vm.Sign.PfxPath);
    }

    [Fact]
    public void RestoreLastCredential_does_nothing_when_the_preference_is_off()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-main-" + Guid.NewGuid().ToString("N")));
        var seed = new AppData();
        seed.Preferences.RestoreLastCredential = false;
        seed.Profiles.Add(new ProfileData { Name = "p", CredMode = "Pfx", PfxPath = "/tmp/p.pfx", LastUsedIso = "2026-01-01T00:00:00-00:00" });
        store.Save(seed);

        var vm = new MainWindowViewModel(store);

        Assert.Equal("", vm.Sign.PfxPath);
    }

    [Fact]
    public void RestoreLastCredential_compares_instants_not_strings_across_a_DST_boundary()
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-main-" + Guid.NewGuid().ToString("N")));
        var seed = new AppData();
        // Loser listed FIRST so a degenerate always-equal comparer also fails this test.
        // Loser: 2026-11-01T01:45:00-04:00 == 05:45Z
        seed.Profiles.Add(new ProfileData { Name = "loser", CredMode = "Pfx", PfxPath = "/tmp/loser.pfx", LastUsedIso = "2026-11-01T01:45:00-04:00" });
        // Winner: 2026-11-01T01:30:00-05:00 == 06:30Z — a later instant despite a lower
        // ordinal string ("...01:30:00-05:00" < "...01:45:00-04:00" as text).
        seed.Profiles.Add(new ProfileData { Name = "winner", CredMode = "Pfx", PfxPath = "/tmp/winner.pfx", LastUsedIso = "2026-11-01T01:30:00-05:00" });
        store.Save(seed);

        var vm = new MainWindowViewModel(store);

        Assert.Equal("/tmp/winner.pfx", vm.Sign.PfxPath);
    }
}
