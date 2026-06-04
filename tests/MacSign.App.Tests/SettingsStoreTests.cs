using System;
using System.IO;
using MacSign.App.Services;
using Xunit;

namespace MacSign.App.Tests;

public class SettingsStoreTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "macsign-store-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var dir = TempDir();
        var store = new SettingsStore(dir);
        store.Save(new AppData());

        Assert.True(File.Exists(Path.Combine(dir, "settings.json")));
        Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp"));
    }

    [Fact]
    public void Save_overwrites_existing_file_atomically()
    {
        var dir = TempDir();
        var store = new SettingsStore(dir);

        var first = new AppData();
        first.Preferences.Theme = "Light";
        store.Save(first);

        var second = new AppData();
        second.Preferences.Theme = "Dark";
        store.Save(second);

        Assert.Equal("Dark", store.Load().Preferences.Theme);
        Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp"));
    }

    [Fact]
    public void Corrupt_settings_file_is_preserved_as_bak_on_load()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ this is not valid json");

        var loaded = new SettingsStore(dir).Load();

        // Falls back to defaults rather than crashing...
        Assert.NotNull(loaded.Preferences);
        Assert.Equal("System", loaded.Preferences.Theme);
        // ...but the unreadable file is moved aside, not silently discarded.
        var bak = Path.Combine(dir, "settings.json.bak");
        Assert.True(File.Exists(bak));
        Assert.Equal("{ this is not valid json", File.ReadAllText(bak));
    }
}
