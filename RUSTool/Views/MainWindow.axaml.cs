using Avalonia.Controls;
using RUSTool.ViewModels;

namespace RUSTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}