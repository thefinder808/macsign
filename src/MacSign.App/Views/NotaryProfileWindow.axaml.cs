using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class NotaryProfileWindow : Window
{
    public NotaryProfileWindow()
    {
        InitializeComponent();
        var vm = new NotaryProfileViewModel();
        DataContext = vm;
        // On success, close the dialog returning the created profile name.
        vm.Succeeded += () => Close(vm.CreatedProfileName);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
