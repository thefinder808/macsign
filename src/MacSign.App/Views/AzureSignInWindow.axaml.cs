using Avalonia.Controls;
using Avalonia.Interactivity;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class AzureSignInWindow : Window
{
    private readonly AzureSignInViewModel _vm;

    /// <summary>Parameterless overload exists only to satisfy the Avalonia XAML loader
    /// (AVLN3001) — always use the one taking a tenant.</summary>
    public AzureSignInWindow() : this(null) { }

    public AzureSignInWindow(string? tenantId)
    {
        InitializeComponent();
        _vm = new AzureSignInViewModel(tenantId);
        DataContext = _vm;
        // On success, close returning the account the user picked.
        _vm.Succeeded += () => Close(_vm.Result);
    }

    /// <summary>Cancel doubles as "abandon a sign-in that's waiting on the browser" — closing
    /// the browser tab otherwise leaves the dialog waiting forever.</summary>
    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _vm.CancelCommand.Execute(null);
        Close(null);
    }
}
