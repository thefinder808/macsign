using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class UpdateWindow : Window
{
    // Parameterless ctor satisfies the Avalonia XAML loader (AVLN3001).
    // Always use the parameterized overload at call sites.
    public UpdateWindow() { InitializeComponent(); }

    public UpdateWindow(UpdateViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        // When the install succeeds, shut down the running app — the detached helper
        // will relaunch the new version once this process exits.
        vm.InstallStarted += () =>
            (Avalonia.Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private void OnLater(object? sender, RoutedEventArgs e) => Close();

    private void OnSkip(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenReleasePage(object? sender, RoutedEventArgs e)
    {
        var url = (DataContext as UpdateViewModel)?.ReleaseUrl;
        if (string.IsNullOrWhiteSpace(url)) return;   // no-op if the release URL is blank
        try { _ = Launcher.LaunchUriAsync(new Uri(url)); } catch { /* best-effort */ }
    }
}
