using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;
        vm.ShowUpdate += OnShowUpdate;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
            vm.StartLaunchUpdateCheck();
    }

    private UpdateWindow? _updateWindow;
    private void OnShowUpdate(UpdateViewModel updateVm)
    {
        // If a window is already open for this same version, just bring it to front.
        if (_updateWindow is not null)
        {
            _updateWindow.Activate();
            return;
        }
        _updateWindow = new UpdateWindow(updateVm);
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.ShowDialog(this);
    }

    // The window extends under the title bar for the translucent sidebar look,
    // so there's no native title bar to grab — make the top strip drag the window.
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    // ── Edit menu: route to the focused text field (no-op when focus isn't a TextBox;
    //    the ⌘ shortcuts already work intrinsically — this is for menu completeness). ──
    private TextBox? FocusedTextBox() => FocusManager?.GetFocusedElement() as TextBox;
    private void OnEditCut(object? sender, EventArgs e) => FocusedTextBox()?.Cut();
    private void OnEditCopy(object? sender, EventArgs e) => FocusedTextBox()?.Copy();
    private void OnEditPaste(object? sender, EventArgs e) => FocusedTextBox()?.Paste();
    private void OnEditSelectAll(object? sender, EventArgs e) => FocusedTextBox()?.SelectAll();

    // ── Window menu ──
    private void OnWindowMinimize(object? sender, EventArgs e) => WindowState = WindowState.Minimized;
    private void OnWindowZoom(object? sender, EventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ── Help menu ──
    private void OnHelpGitHub(object? sender, EventArgs e) => OpenUrl("https://github.com/thefinder808/macsign");
    private void OnHelpIssue(object? sender, EventArgs e) => OpenUrl("https://github.com/thefinder808/macsign/issues/new");
    private void OnHelpReleases(object? sender, EventArgs e) => OpenUrl("https://github.com/thefinder808/macsign/releases");

    private AboutWindow? _aboutWindow;
    private void OnHelpAbout(object? sender, EventArgs e)
    {
        if (_aboutWindow is null)
        {
            _aboutWindow = new AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        }
        _aboutWindow.Show(this);
        _aboutWindow.Activate();
    }

    private void OpenUrl(string url)
    {
        try { _ = Launcher.LaunchUriAsync(new Uri(url)); } catch { /* best-effort */ }
    }
}
