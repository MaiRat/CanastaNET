using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CanastaNET.Desktop.Core;

namespace CanastaNET.Desktop;

public partial class MainWindow : Window
{
    private const double MaxResponsiveWidth = 1680d;
    private const double MaxResponsiveHeight = 1080d;

    private readonly CanastaWorkspaceViewModel workspace = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = workspace;
        Loaded += Window_Loaded;
        SizeChanged += Window_SizeChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => UpdateResponsiveLayoutMetrics();

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged && !e.HeightChanged)
        {
            return;
        }

        UpdateResponsiveLayoutMetrics();
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

    private void UpdateResponsiveLayoutMetrics()
    {
        var widthRatio = NormalizeDimension(ActualWidth, MinWidth, MaxResponsiveWidth);
        var heightRatio = NormalizeDimension(ActualHeight, MinHeight, MaxResponsiveHeight);
        Resources["TopSeatCardLaneHeight"] = Interpolate(152d, 168d, heightRatio);
        Resources["SideSeatColumnWidth"] = new GridLength(Interpolate(196d, 220d, widthRatio));

        Resources["DefaultHorizontalHandOverlapMargin"] = new Thickness(0d, 0d, Interpolate(-92d, -84d, widthRatio), 0d);
        Resources["CompactHorizontalHandOverlapMargin"] = new Thickness(0d, 0d, Interpolate(-100d, -92d, widthRatio), 0d);
        Resources["DefaultVerticalHandOverlapMargin"] = new Thickness(0d, 0d, 0d, Interpolate(-140d, -132d, heightRatio));
        Resources["CompactVerticalHandOverlapMargin"] = new Thickness(0d, 0d, 0d, Interpolate(-148d, -140d, heightRatio));
        Resources["VerticalHandScrollPadding"] = new Thickness(0d, 0d, 0d, Interpolate(68d, 76d, heightRatio));

        var seatSideMargin = Interpolate(8d, 14d, widthRatio);
        Resources["TopSeatMargin"] = new Thickness(seatSideMargin, 6d, seatSideMargin, 8d);
        Resources["BottomSeatMargin"] = new Thickness(seatSideMargin, 8d, seatSideMargin, 0d);
    }

    private static double NormalizeDimension(double value, double minimum, double maximum)
    {
        var safeValue = Math.Max(value, minimum);
        return Math.Clamp((safeValue - minimum) / (maximum - minimum), 0d, 1d);
    }

    private static double Interpolate(double minimum, double maximum, double ratio) => minimum + ((maximum - minimum) * ratio);
}
