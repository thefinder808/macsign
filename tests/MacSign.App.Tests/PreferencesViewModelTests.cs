using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class PreferencesViewModelTests
{
    private static PreferencesViewModel Make(out AppData data, out SettingsStore store, out FakeRunner runner,
        UpdateService? updates = null)
    {
        store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-pref-" + Guid.NewGuid().ToString("N")));
        data = new AppData();
        runner = new FakeRunner();
        return new PreferencesViewModel(data, store, runner, updates);
    }

    [Fact]
    public void Changing_theme_persists_and_flips_active_flag()
    {
        var vm = Make(out _, out var store, out _);
        vm.Theme = "Dark";
        Assert.True(vm.IsDarkTheme);
        Assert.False(vm.IsSystemTheme);
        Assert.Equal("Dark", store.Load().Preferences.Theme);
    }

    [Fact]
    public void Changing_signing_defaults_persists()
    {
        var vm = Make(out _, out var store, out _);
        vm.DefaultTimestampUrl = "http://tsa.example/ts";
        vm.TimestampByDefault = false;
        var p = store.Load().Preferences;
        Assert.Equal("http://tsa.example/ts", p.DefaultTimestampUrl);
        Assert.False(p.TimestampByDefault);
    }

    [Fact]
    public void SetCap_numeric_sets_value_and_active_flag()
    {
        var vm = Make(out _, out _, out _);
        vm.SetCapCommand.Execute("100");
        Assert.Equal(100, vm.ActivityKeepLast);
        Assert.True(vm.IsCap100);
    }

    [Fact]
    public void SetCap_unlimited_sets_zero_persists_and_raises_CapChanged()
    {
        var vm = Make(out _, out var store, out _);
        bool raised = false; vm.CapChanged += () => raised = true;
        vm.SetCapCommand.Execute("Unlimited");
        Assert.Equal(0, vm.ActivityKeepLast);
        Assert.True(vm.IsCapUnlimited);
        Assert.Equal(0, store.Load().Preferences.ActivityKeepLast);
        Assert.True(raised);
    }

    [Fact]
    public void ClearHistory_raises_event()
    {
        var vm = Make(out _, out _, out _);
        bool raised = false; vm.ClearHistoryRequested += () => raised = true;
        vm.ClearHistoryCommand.Execute(null);
        Assert.True(raised);
    }

    [Fact]
    public async Task Reveal_invokes_open_dash_R_with_settings_path()
    {
        var vm = Make(out _, out var store, out var runner);
        await vm.RevealSettingsCommand.ExecuteAsync(null);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("/usr/bin/open", call.File);
        Assert.Equal(new[] { "-R", store.FilePath }, call.Args);
    }

    [Fact]
    public void Reset_is_two_step_then_raises_ResetRequested()
    {
        var vm = Make(out _, out _, out _);
        bool raised = false; vm.ResetRequested += () => raised = true;

        vm.ResetAllCommand.Execute(null);    // arm
        Assert.True(vm.ConfirmReset);
        Assert.False(raised);

        vm.ResetAllCommand.Execute(null);    // fire
        Assert.False(vm.ConfirmReset);
        Assert.True(raised);
    }

    [Fact]
    public void ReloadFromData_rereads_without_persisting_or_raising()
    {
        var vm = Make(out var data, out _, out _);
        bool capRaised = false; vm.CapChanged += () => capRaised = true;
        data.Preferences.Theme = "Light";
        data.Preferences.ActivityKeepLast = 500;

        vm.ReloadFromData();

        Assert.Equal("Light", vm.Theme);
        Assert.Equal(500, vm.ActivityKeepLast);
        Assert.True(vm.IsCap500);
        Assert.False(capRaised);   // side effects suppressed during reload
    }

    // ── Auto-updates section ────────────────────────────────────────────────

    private const string OldJson = """
    {
      "tag_name": "v0.0.1",
      "html_url": "https://github.com/thefinder808/macsign/releases/tag/v0.0.1",
      "body": "",
      "assets": []
    }
    """;

    private const string NewJson = """
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
    public void AutoCheckUpdates_persists()
    {
        // FakeHttp-backed UpdateService so the VM can never make a live network call.
        var vm = Make(out _, out var store, out _,
            new UpdateService(new HttpClient(new FakeHttp())));

        // flip off and verify it round-trips through SettingsStore
        vm.AutoCheckUpdates = false;

        Assert.False(store.Load().Preferences.AutoCheckUpdates);
    }

    [Fact]
    public async Task CheckNow_noUpdate_setsUpToDate()
    {
        var svc = new UpdateService(FakeHttp.ClientReturning(OldJson));
        var vm = Make(out _, out _, out _, svc);

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.Contains("up to date", vm.UpdateStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckNow_updateAvailable_raisesEvent()
    {
        var svc = new UpdateService(FakeHttp.ClientReturning(NewJson));
        var vm = Make(out _, out _, out _, svc);

        UpdateInfo? received = null;
        vm.UpdateAvailable += info => received = info;

        await vm.CheckNowCommand.ExecuteAsync(null);

        Assert.NotNull(received);
        Assert.Equal("9.9.9", received!.Version);
        Assert.Contains("9.9.9", vm.UpdateStatus, StringComparison.OrdinalIgnoreCase);
    }
}
