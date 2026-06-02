using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class VerifyView : UserControl
{
    public VerifyView()
    {
        InitializeComponent();
        EmptyCard.PointerPressed += (_, _) =>
        {
            if (DataContext is VerifyViewModel vm) vm.PickAndVerifyCommand.Execute(null);
        };
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

#pragma warning disable CS0618 // classic Data/DataFormats API — pinned-stable on Avalonia 11.3
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not VerifyViewModel vm) return;
        var path = e.Data.GetFiles()?
            .Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(path)) vm.VerifyPath(path!);
    }
#pragma warning restore CS0618
}
