using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace MacSign.App.Services;

/// <summary>File-picker helpers. The dialog is a UI concern, so view-models call
/// these rather than touching StorageProvider directly.</summary>
public static class FileDialogs
{
    private static readonly string[] SignablePatterns = { "*.exe", "*.dll", "*.sys", "*.msi", "*.ps1" };

    public static async Task<IReadOnlyList<string>> PickSignablesAsync()
    {
        var top = MainTopLevel();
        if (top is null) return [];
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files to sign",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Signable files") { Patterns = SignablePatterns },
            },
        });
        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public static async Task<string?> PickOneAsync(string title, string[] patterns)
    {
        var top = MainTopLevel();
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Files") { Patterns = patterns } },
        });
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private static TopLevel? MainTopLevel() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
