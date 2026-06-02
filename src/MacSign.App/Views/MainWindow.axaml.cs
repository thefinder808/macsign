using Avalonia.Controls;
using MacSign.App.ViewModels;

namespace MacSign.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
