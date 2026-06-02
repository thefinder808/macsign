using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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
