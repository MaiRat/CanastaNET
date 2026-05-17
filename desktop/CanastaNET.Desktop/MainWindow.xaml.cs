using System.Windows;
using System.Windows.Controls;
using CanastaNET.Desktop.Core;

namespace CanastaNET.Desktop;

public partial class MainWindow : Window
{
    private readonly CanastaWorkspaceViewModel workspace = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = workspace;
    }

    private void NavigateToTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !int.TryParse(tag, out var selectedIndex))
        {
            return;
        }

        WorkspaceTabs.SelectedIndex = selectedIndex;
    }
}
