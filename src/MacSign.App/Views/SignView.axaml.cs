using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MacSign.App.Services;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class SignView : UserControl
{
    public SignView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnRowKeyDown, RoutingStrategies.Bubble);
    }

    // Delete / Backspace on a focused file row removes it. Resolving the row's
    // item from e.Source (rather than per-row handlers) keeps this safe under
    // virtualization, and the FileItemViewModel guard means a focused inspector
    // text field keeps its own Delete/Backspace.
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Delete or Key.Back)) return;
        if (DataContext is not SignViewModel vm) return;
        if (e.Source is Control { DataContext: FileItemViewModel item })
        {
            vm.RemoveFileCommand.Execute(item);
            e.Handled = true;
        }
    }

    // Opening the browser lives in the view, not the view-model: the VM stays window-free,
    // matching how the Mac screen opens its notary-profile dialog.
    private async void OnAzureSignIn(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SignViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (vm.IsSigning) return;   // don't change identity mid-run

        var account = await new AzureSignInWindow(vm.TenantId).ShowDialog<AzureSignInData?>(owner);
        // Cancelled: keep whoever was signed in rather than silently signing the user out.
        if (account is not null) vm.ApplyAzureSignIn(account);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool ok = e.DataTransfer is { } dt && dt.Contains(DataFormat.File);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DragHighlight.Set(FilesCard, "dragover", ok);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DragHighlight.Set(FilesCard, "dragover", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DragHighlight.Set(FilesCard, "dragover", false);
        if (DataContext is not SignViewModel vm) return;
        var files = e.DataTransfer?.TryGetFiles();
        if (files is null) return;

        var paths = files
            .Select(f => f.Path.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths.Count > 0) _ = vm.AddPathsAsync(paths);
    }
}
