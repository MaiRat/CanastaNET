using System.Windows;
using CanastaNET.Desktop.Core;

namespace CanastaNET.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new CanastaWorkspaceViewModel();
    }
}
