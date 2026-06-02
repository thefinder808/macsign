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
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer is { } dt && dt.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not SignViewModel vm) return;
        var files = e.DataTransfer?.TryGetFiles();
        if (files is null) return;

        var paths = files
            .Select(f => f.Path.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths.Count > 0) vm.AddPaths(paths);
    }
}
