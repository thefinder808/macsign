using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
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

    // The classic Data/DataFormats API is pinned-stable on Avalonia 11.3; the
    // newer DataTransfer API is the eventual migration. Suppress the deprecation
    // narrowly rather than switch a drop path we can't interactively retest here.
#pragma warning disable CS0618
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not SignViewModel vm) return;
        var files = e.Data.GetFiles();
        if (files is null) return;

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        if (paths.Count > 0) vm.AddPaths(paths);
    }
#pragma warning restore CS0618
}
