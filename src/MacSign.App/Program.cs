using Avalonia;
using MacSign.App.Services;

namespace MacSign.App;

internal static class Program
{
    // Avalonia desktop entry point. Keep this minimal — see App for setup.
    [STAThread]
    public static void Main(string[] args)
    {
        // A Finder/Dock-launched app inherits only the minimal launchd PATH, hiding a
        // Homebrew `az` from Azure.Identity's AzureCliCredential. Restore the tool dirs
        // before anything signs so Azure Trusted Signing can find `az login`'s token.
        CliPath.EnsureToolPath();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
