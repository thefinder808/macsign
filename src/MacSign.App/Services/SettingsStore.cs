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
                return JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath)) ?? new AppData();
        }
        catch { /* corrupt/unreadable → start fresh */ }
        return new AppData();
    }

    public void Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
        }
        catch { /* best-effort */ }
    }
}
