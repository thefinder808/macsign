using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MacSign.App.Views;

namespace MacSign.App;

public partial class App : Application
{
    public override void Initialize()
    {
        // Drives the macOS application menu (the bold item next to the Apple
        // logo). Kept in sync with CFBundleName in build-macos.sh's Info.plist.
        Name = "MacSign";

        // Enable the optional backends in the core engine (the core references
        // none of them — they self-register via these hooks). Mirrors
        // MacSign.Cli/Program.cs.
        MacSign.Signing.Pkcs11.Pkcs11Backend.Register();
        MacSign.Signing.Msi.MsiBackend.Register();
        MacSign.Signing.Azure.AzureBackend.Register();

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
