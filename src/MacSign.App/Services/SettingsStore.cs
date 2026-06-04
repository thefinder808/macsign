using System;
using System.IO;
using System.Text.Json;

namespace MacSign.App.Services;

/// <summary>Persists <see cref="AppData"/> as JSON at
/// <c>~/Library/Application Support/MacSign/settings.json</c>. Never writes
/// secrets. All IO is best-effort — a load/save failure never crashes the app.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "MacSign");

    private readonly string _dir;
    public SettingsStore(string? dir = null) => _dir = dir ?? DefaultDir;

    /// <summary>Absolute path to the JSON settings file (used by the Reveal-in-Finder action).</summary>
    public string FilePath => Path.Combine(_dir, "settings.json");

    public AppData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return Normalize(JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath)) ?? new AppData());
        }
        catch
        {
            // Corrupt/unreadable → start fresh, but preserve the file for recovery instead of
            // silently discarding the user's profiles/activity/preferences.
            try { File.Move(FilePath, FilePath + ".bak", overwrite: true); } catch { /* best-effort */ }
        }
        return new AppData();
    }

    /// <summary>A hand-edited file can carry an explicit <c>null</c> for a sub-object
    /// (System.Text.Json keeps the initializer only when the key is *absent*), which
    /// would NRE a consumer reading through it. Coalesce every sub-object to a default.</summary>
    private static AppData Normalize(AppData d)
    {
        d.Profiles ??= new();
        d.Activity ??= new();
        d.AppleSign ??= new();
        d.Preferences ??= new();
        return d;
    }

    public void Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var json = JsonSerializer.Serialize(data, Options);

            // Write to a sibling temp then atomically rename, so a crash/full-disk mid-write
            // can't truncate settings.json (a torn file fails to parse and Load would reset it).
            var temp = FilePath + ".tmp";
            try
            {
                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(flushToDisk: true); // durability: bytes land before the rename commits
                }
                File.Move(temp, FilePath, overwrite: true); // same volume → atomic rename
            }
            finally
            {
                if (File.Exists(temp))
                    try { File.Delete(temp); } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }
}
