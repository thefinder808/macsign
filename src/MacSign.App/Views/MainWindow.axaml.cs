using System;
using Avalonia.Controls;
using Avalonia.Input;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
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
}
