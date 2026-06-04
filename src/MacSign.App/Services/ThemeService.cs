using Avalonia;
using Avalonia.Styling;

namespace MacSign.App.Services;

/// <summary>Maps the persisted theme string to an Avalonia <see cref="ThemeVariant"/>
/// and applies it to the running app. "System" (or anything unknown) follows macOS.
/// <see cref="Apply"/> is null-safe so headless VM tests never touch a live app.</summary>
public static class ThemeService
{
    public static ThemeVariant ToVariant(string? theme) => theme switch
    {
        "Light" => ThemeVariant.Light,
        "Dark"  => ThemeVariant.Dark,
        _       => ThemeVariant.Default,   // "System" / null / unknown → follow the OS
    };

    public static void Apply(string? theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(theme);
    }
}
