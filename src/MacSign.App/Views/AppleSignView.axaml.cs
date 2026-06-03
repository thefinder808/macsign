using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class AppleSignView : UserControl
{
    public AppleSignView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Populate signing identities the first time the screen is shown.
        Loaded += (_, _) =>
        {
            if (DataContext is AppleSignViewModel { Identities.Count: 0 } vm)
                vm.RefreshIdentitiesCommand.Execute(null);
        };
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool ok = e.DataTransfer is { } dt && dt.Contains(DataFormat.File);
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DragHighlight.Set(AppCard, "dragover", ok);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DragHighlight.Set(AppCard, "dragover", false);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DragHighlight.Set(AppCard, "dragover", false);
        if (DataContext is not AppleSignViewModel vm) return;
        var target = e.DataTransfer?.TryGetFiles()?
            .Select(f => f.Path.LocalPath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p)
                && (p.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrEmpty(target)) vm.SetTarget(target!);
    }
}
