using System;
using System.IO;
using MacSign.App.Services;
using MacSign.App.ViewModels;
using Xunit;

namespace MacSign.App.Tests;

public class ActivityViewModelTests
{
    private static ActivityViewModel Make(int cap, out AppData data)
    {
        var store = new SettingsStore(Path.Combine(Path.GetTempPath(), "macsign-act-" + Guid.NewGuid().ToString("N")));
        data = new AppData();
        data.Preferences.ActivityKeepLast = cap;
        return new ActivityViewModel(data, store);
    }

    private static RunData Run() =>
        new() { FileCount = 1, Credential = "x", Detail = "d", Status = "ok", WhenIso = "t" };

    [Fact]
    public void Record_trims_to_configured_cap()
    {
        var vm = Make(2, out _);
        vm.Record(Run()); vm.Record(Run()); vm.Record(Run());
        Assert.Equal(2, vm.Runs.Count);
    }

    [Fact]
    public void Cap_zero_is_unlimited()
    {
        var vm = Make(0, out _);
        for (int i = 0; i < 60; i++) vm.Record(Run());
        Assert.Equal(60, vm.Runs.Count);
    }

    [Fact]
    public void ReTrim_enforces_a_lowered_cap()
    {
        var vm = Make(0, out var data);
        for (int i = 0; i < 10; i++) vm.Record(Run());
        data.Preferences.ActivityKeepLast = 3;
        vm.ReTrim();
        Assert.Equal(3, vm.Runs.Count);
    }

    [Fact]
    public void Clear_empties_history()
    {
        var vm = Make(50, out _);
        vm.Record(Run());
        vm.Clear();
        Assert.Empty(vm.Runs);
        Assert.True(vm.IsEmpty);
    }
}
