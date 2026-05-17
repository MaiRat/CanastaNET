using System;
using System.Linq;
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

    private void StartPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string presetName })
        {
            return;
        }

        workspace.SelectedSetupPresetName = presetName;

        if (workspace.ApplySetupPresetCommand.CanExecute(null))
        {
            workspace.ApplySetupPresetCommand.Execute(null);
        }

        if (workspace.StartMatchCommand.CanExecute(null))
        {
            workspace.StartMatchCommand.Execute(null);
        }
    }

    private void ShowRulesHelp_Click(object sender, RoutedEventArgs e)
    {
        var rulesHelpText = string.Join(
            Environment.NewLine + Environment.NewLine,
            workspace.RulesHelpLines.Select((line, index) => $"{index + 1}. {line}"));

        MessageBox.Show(
            this,
            rulesHelpText,
            "Quick rules help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
