using System;
using System.IO;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class AppPrefsTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "macsign-prefs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Defaults_match_current_hardcoded_behavior()
    {
        var p = new AppData().Preferences;
        Assert.Equal("System", p.Theme);
        Assert.Equal("http://timestamp.digicert.com", p.DefaultTimestampUrl);
        Assert.True(p.TimestampByDefault);
        Assert.Equal(50, p.ActivityKeepLast);
    }

    [Fact]
    public void Preferences_round_trip_through_store()
    {
        var store = new SettingsStore(TempDir());
        var data = new AppData();
        data.Preferences.Theme = "Dark";
        data.Preferences.DefaultTimestampUrl = "http://tsa.example/ts";
        data.Preferences.TimestampByDefault = false;
        data.Preferences.ActivityKeepLast = 100;
        store.Save(data);

        var loaded = store.Load();
        Assert.Equal("Dark", loaded.Preferences.Theme);
        Assert.Equal("http://tsa.example/ts", loaded.Preferences.DefaultTimestampUrl);
        Assert.False(loaded.Preferences.TimestampByDefault);
        Assert.Equal(100, loaded.Preferences.ActivityKeepLast);
    }

    [Fact]
    public void Legacy_file_without_preferences_loads_defaults()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{\"Profiles\":[],\"Activity\":[]}");
        var loaded = new SettingsStore(dir).Load();
        Assert.NotNull(loaded.Preferences);
        Assert.Equal("System", loaded.Preferences.Theme);
        Assert.Equal(50, loaded.Preferences.ActivityKeepLast);
    }

    [Fact]
    public void File_with_explicit_null_subobjects_normalizes_to_defaults()
    {
        // A hand-edited file can carry an explicit null (vs. an absent key), which
        // would otherwise NRE a consumer reading through the sub-object.
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"),
            "{\"Profiles\":null,\"Activity\":null,\"AppleSign\":null,\"Preferences\":null}");
        var loaded = new SettingsStore(dir).Load();
        Assert.NotNull(loaded.Profiles);
        Assert.NotNull(loaded.Activity);
        Assert.NotNull(loaded.AppleSign);
        Assert.NotNull(loaded.Preferences);
        Assert.Equal("System", loaded.Preferences.Theme);
    }
}
