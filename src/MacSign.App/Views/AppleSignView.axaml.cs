using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class AppleSignView : UserControl
{
    private AppleSignViewModel? _observed;

    public AppleSignView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Follow the live log: re-scroll to the bottom whenever new output streams in.
        DataContextChanged += OnDataContextChanged;
        // Populate signing identities the first time the screen is shown.
        Loaded += (_, _) =>
        {
            if (DataContext is AppleSignViewModel { Identities.Count: 0 } vm)
                vm.RefreshIdentitiesCommand.Execute(null);
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observed is not null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
        _observed = DataContext as AppleSignViewModel;
        if (_observed is not null) _observed.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppleSignViewModel.LogText)) return;
        // Post at Background so the scroll runs AFTER the text block re-measures with the
        // new content — otherwise ScrollToEnd would use the stale (shorter) extent.
        Dispatcher.UIThread.Post(LogScroll.ScrollToEnd, DispatcherPriority.Background);
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

    private async void OnSetupNotaryProfile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not AppleSignViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var name = await new NotaryProfileWindow().ShowDialog<string?>(owner);
        if (!string.IsNullOrWhiteSpace(name)) vm.NotaryProfile = name!;
    }
}
