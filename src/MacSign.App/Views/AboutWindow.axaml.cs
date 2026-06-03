using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MacSign.App.Services;

namespace MacSign.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "Version " + AppInfo.Version;
    }

    private void Open(string url)
    {
        try { _ = Launcher.LaunchUriAsync(new Uri(url)); } catch { /* best-effort */ }
    }

    private void OnGitHub(object? sender, RoutedEventArgs e) => Open("https://github.com/thefinder808/macsign");
    private void OnReleases(object? sender, RoutedEventArgs e) => Open("https://github.com/thefinder808/macsign/releases");
    private void OnCoffee(object? sender, RoutedEventArgs e) => Open("https://www.buymeacoffee.com/thefinder808");
}
