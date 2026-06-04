using System;
using System.IO;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class UpdatePrefsTests
{
    [Fact]
    public void Defaults_autoCheckOn_noSkip()
    {
        var p = new AppPrefs();
        Assert.True(p.AutoCheckUpdates);
        Assert.Null(p.LastUpdateCheckUtc);
        Assert.Null(p.SkippedVersion);
    }

    [Fact]
    public void RoundTrips_throughStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macsign-upd-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(dir);
        var data = new AppData();
        data.Preferences.AutoCheckUpdates = false;
        data.Preferences.LastUpdateCheckUtc = "2026-06-04T00:00:00Z";
        data.Preferences.SkippedVersion = "1.2.0";
        store.Save(data);

        var loaded = new SettingsStore(dir).Load();
        Assert.False(loaded.Preferences.AutoCheckUpdates);
        Assert.Equal("2026-06-04T00:00:00Z", loaded.Preferences.LastUpdateCheckUtc);
        Assert.Equal("1.2.0", loaded.Preferences.SkippedVersion);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void LegacyFile_withoutUpdateFields_loadsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "macsign-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"Preferences\":{\"Theme\":\"Dark\"}}");
        var loaded = new SettingsStore(dir).Load();
        Assert.True(loaded.Preferences.AutoCheckUpdates);   // absent → initializer default
        Directory.Delete(dir, true);
    }
}
