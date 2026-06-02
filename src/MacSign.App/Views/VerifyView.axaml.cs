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
        EmptyCard.PointerPressed += (_, _) => PickAndVerify();
        // Keyboard activation: the empty card is focusable, so Space/Enter triggers it too.
        EmptyCard.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { PickAndVerify(); e.Handled = true; }
        };
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void PickAndVerify()
    {
        if (DataContext is VerifyViewModel vm) vm.PickAndVerifyCommand.Execute(null);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool ok = e.DataTransfer is { } dt && dt.Contains(DataFormat.File);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DragHighlight.Set(EmptyCard, "dragover", ok);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DragHighlight.Set(EmptyCard, "dragover", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DragHighlight.Set(EmptyCard, "dragover", false);
        if (DataContext is not VerifyViewModel vm) return;
        var path = e.DataTransfer?.TryGetFiles()?
            .Select(f => f.Path.LocalPath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(path)) _ = vm.VerifyPathAsync(path!);
    }
}
