using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer is { } dt && dt.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not VerifyViewModel vm) return;
        var path = e.DataTransfer?.TryGetFiles()?
            .Select(f => f.Path.LocalPath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(path)) vm.VerifyPath(path!);
    }
}
