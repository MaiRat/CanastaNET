using CanastaNET.Desktop.Core;
using CanastaNET.Engine;
using System.Text.RegularExpressions;

namespace CanastaNET.Engine.Tests;

public class DesktopWorkspaceViewModelTests
{
    [Fact]
    public void Workspace_MapsDefaultMatchIntoDesktopScreens()
    {
        var workspace = new CanastaWorkspaceViewModel();

        Assert.Equal(1, workspace.TableOverview.RoundNumber);
        Assert.Equal(TurnPhase.AwaitingDraw, workspace.TableOverview.TurnPhase);
        Assert.Equal(workspace.TableOverview.CurrentPlayerName, workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer).Name);
        Assert.NotNull(workspace.CurrentPlayerHand);
        Assert.All(workspace.WaitingPlayerHands, hand => Assert.False(hand.IsCurrentPlayer));
        Assert.NotEmpty(workspace.DiscardPile.Cards);
        Assert.NotNull(workspace.DiscardPile.TopCardVisual);
        Assert.Equal(workspace.ScoreSummary.TeamScores.Count, workspace.TeamMelds.Count);
        Assert.Contains(workspace.LegalCommands, command => command.CommandText == "draw stock");
        Assert.Equal("Draw from the stock or discard pile to start the turn.", workspace.NextActionPrompt);
        Assert.NotEmpty(workspace.RulesHelpLines);
        Assert.NotEmpty(workspace.SetupPresets);
        Assert.True(workspace.ShowDrawActions);
        Assert.True(workspace.ShowRoundActions);
        Assert.True(workspace.ShowNextActionPrompt);
        Assert.False(workspace.ShowMeldAction);
        Assert.False(workspace.ShowDiscardActions);
    }

    [Fact]
    public void DiscardCommand_ShowsValidationFeedbackWhenRoundStillAwaitsDraw()
    {
        var workspace = new CanastaWorkspaceViewModel();
        var currentHand = workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer);
        currentHand.Cards[0].IsSelected = true;

        Assert.True(workspace.DiscardCommand.CanExecute(null));

        workspace.DiscardCommand.Execute(null);

        Assert.Contains("must draw before discarding", workspace.FeedbackMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draw stock", workspace.FeedbackMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(workspace.LegalCommands, command => command.CommandText == "draw stock");
    }

    [Fact]
    public void Commands_CanDriveBasicTurnFlowFromTheDesktopWorkspace()
    {
        var workspace = new CanastaWorkspaceViewModel();
        workspace.Setup.SeedText = "7";
        workspace.StartMatchCommand.Execute(null);

        var activePlayerBeforeTurn = workspace.TableOverview.CurrentPlayerName;
        var handSizeBeforeDraw = workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer).Cards.Count;

        workspace.DrawStockCommand.Execute(null);

        Assert.Equal(TurnPhase.AwaitingDiscard, workspace.TableOverview.TurnPhase);
        Assert.Equal(handSizeBeforeDraw + 1, workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer).Cards.Count);
        Assert.True(workspace.ShowSelectionPrompt);
        Assert.Equal("Select cards to reveal meld and discard actions.", workspace.NextActionPrompt);

        var currentHand = workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer);
        currentHand.Cards[0].IsSelected = true;

        Assert.True(workspace.ShowMeldAction);
        Assert.True(workspace.ShowDiscardActions);
        Assert.False(workspace.ShowSelectionPrompt);
        Assert.Equal("Discard the selected card or meld it if the play is legal.", workspace.NextActionPrompt);

        workspace.EndTurnCommand.Execute(null);

        Assert.Equal(TurnPhase.AwaitingDraw, workspace.TableOverview.TurnPhase);
        Assert.NotEqual(activePlayerBeforeTurn, workspace.TableOverview.CurrentPlayerName);
        Assert.Contains("Ended the turn", workspace.FeedbackMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartMatch_ShowsValidationFeedbackForInvalidSeedInput()
    {
        var workspace = new CanastaWorkspaceViewModel();
        workspace.Setup.SeedText = "not-a-number";

        workspace.StartMatchCommand.Execute(null);

        Assert.Contains("Seed values must be integers", workspace.FeedbackMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplySetupPreset_PrefillsOnboardingConfiguration()
    {
        var workspace = new CanastaWorkspaceViewModel
        {
            SelectedSetupPresetName = "Quick duo practice"
        };

        workspace.ApplySetupPresetCommand.Execute(null);

        Assert.Equal("North, South", workspace.Setup.PlayerNamesCsv);
        Assert.Equal(1500, workspace.Setup.WinningScore);
        Assert.Equal("7", workspace.Setup.SeedText);
        Assert.Contains("Applied", workspace.FeedbackMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveAndLoadSnapshot_RestoresWorkspaceState_AndTracksRecentHistory()
    {
        var workspace = new CanastaWorkspaceViewModel();
        var snapshotPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.canasta.json");

        try
        {
            workspace.Setup.SeedText = "7";
            workspace.StartMatchCommand.Execute(null);
            workspace.DrawStockCommand.Execute(null);
            var savedTurnPhase = workspace.TableOverview.TurnPhase;
            var savedCurrentPlayer = workspace.TableOverview.CurrentPlayerName;
            workspace.MatchFilePath = snapshotPath;

            workspace.SaveMatchCommand.Execute(null);

            var currentHand = workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer);
            currentHand.Cards[0].IsSelected = true;
            workspace.EndTurnCommand.Execute(null);
            Assert.NotEqual(savedCurrentPlayer, workspace.TableOverview.CurrentPlayerName);

            workspace.LoadMatchCommand.Execute(null);

            Assert.Equal(savedTurnPhase, workspace.TableOverview.TurnPhase);
            Assert.Equal(savedCurrentPlayer, workspace.TableOverview.CurrentPlayerName);
            Assert.Contains(workspace.RecentMatches, entry => string.Equals(entry.Path, snapshotPath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(snapshotPath, workspace.SelectedRecentMatchPath);
            Assert.Contains("Loaded", workspace.FeedbackMessage!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }
        }
    }

    [Fact]
    public void CardVisuals_ExposeFormattedProperties()
    {
        var workspace = new CanastaWorkspaceViewModel();
        Assert.NotNull(workspace.CurrentPlayerHand);
        Assert.NotEmpty(workspace.CurrentPlayerHand!.Cards);

        var currentCard = workspace.CurrentPlayerHand.Cards.First();
        var discardTopCard = workspace.DiscardPile.TopCardVisual!;

        Assert.False(string.IsNullOrWhiteSpace(currentCard.RankText));
        Assert.False(string.IsNullOrWhiteSpace(currentCard.SuitSymbol));
        Assert.StartsWith("#FF", currentCard.AccentColor, StringComparison.Ordinal);
        Assert.StartsWith("#FF", currentCard.SurfaceColor, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(currentCard.DeckLabel));
        Assert.False(string.IsNullOrWhiteSpace(discardTopCard.CardTypeLabel));
    }

    [Fact]
    public void ViewSettings_DefaultToDetailedPresentation()
    {
        var workspace = new CanastaWorkspaceViewModel();

        Assert.True(workspace.ShowCardPointBadges);
        Assert.True(workspace.ShowDeckRibbons);
        Assert.False(workspace.UseCompactCardSpacing);
    }

    [Fact]
    public void ViewSettings_CanBeModified()
    {
        var workspace = new CanastaWorkspaceViewModel();

        workspace.ShowCardPointBadges = false;
        workspace.ShowDeckRibbons = false;
        workspace.UseCompactCardSpacing = true;

        Assert.False(workspace.ShowCardPointBadges);
        Assert.False(workspace.ShowDeckRibbons);
        Assert.True(workspace.UseCompactCardSpacing);
    }

    [Fact]
    public void TableSeats_PositionPlayersClockwiseAroundTheTable()
    {
        var workspace = new CanastaWorkspaceViewModel();
        var currentPlayerIndex = workspace.PlayerHands
            .Select((hand, index) => new { hand, index })
            .Single(entry => entry.hand.IsCurrentPlayer)
            .index;

        Assert.Same(workspace.CurrentPlayerHand, workspace.BottomSeatHand);
        Assert.Equal(workspace.PlayerHands[(currentPlayerIndex + 1) % workspace.PlayerHands.Count].Name, workspace.RightSeatHand!.Name);
        Assert.Equal(workspace.PlayerHands[(currentPlayerIndex + 2) % workspace.PlayerHands.Count].Name, workspace.TopSeatHand!.Name);
        Assert.Equal(workspace.PlayerHands[(currentPlayerIndex + 3) % workspace.PlayerHands.Count].Name, workspace.LeftSeatHand!.Name);
    }

    [Fact]
    public void TableSeats_HideSideSeats_ForTwoPlayerMatches()
    {
        var workspace = new CanastaWorkspaceViewModel
        {
            SelectedSetupPresetName = "Quick duo practice"
        };

        workspace.ApplySetupPresetCommand.Execute(null);
        workspace.StartMatchCommand.Execute(null);

        Assert.Equal(2, workspace.PlayerHands.Count);
        Assert.Same(workspace.CurrentPlayerHand, workspace.BottomSeatHand);
        Assert.NotNull(workspace.TopSeatHand);
        Assert.Null(workspace.LeftSeatHand);
        Assert.Null(workspace.RightSeatHand);
    }

    [Fact]
    public void MainWindowXaml_UsesOnlyValidHexColorTokenLengths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainWindowXamlPath = Path.Combine(repositoryRoot, "desktop", "CanastaNET.Desktop", "MainWindow.xaml");

        Assert.True(File.Exists(mainWindowXamlPath), $"Expected desktop XAML at {mainWindowXamlPath}.");

        var xaml = File.ReadAllText(mainWindowXamlPath);
        var colorTokens = Regex.Matches(xaml, @"#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b")
            .Select(match => match.Value)
            .ToArray();
        var invalidColorTokens = Regex.Matches(xaml, @"#[0-9A-Fa-f]+\b")
            .Select(match => match.Value)
            .Except(colorTokens, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(colorTokens);
        Assert.Empty(invalidColorTokens);
    }

    [Fact]
    public void MainWindowXaml_UsesOverlappedHandsAroundTheTable()
    {
        var xaml = File.ReadAllText(GetMainWindowXamlPath());

        Assert.Contains("x:Key=\"OverlappedHorizontalHandItemStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"OverlappedVerticalHandItemStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ScaleTransform ScaleX=\"0.94\" ScaleY=\"0.94\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ScaleTransform ScaleX=\"0.92\" ScaleY=\"0.92\" />", xaml, StringComparison.Ordinal);
        Assert.Matches(new Regex(
            "ItemsControl ItemsSource=\\\"\\{Binding TopSeatHand\\.Cards\\}\\\"[\\s\\S]*?VerticalAlignment=\\\"Bottom\\\"[\\s\\S]*?ItemContainerStyle=\\\"\\{StaticResource OverlappedHorizontalHandItemStyle\\}\\\"",
            RegexOptions.CultureInvariant),
            xaml);
        Assert.Matches(new Regex(
            "ItemsControl ItemsSource=\\\"\\{Binding LeftSeatHand\\.Cards\\}\\\"[\\s\\S]*?ItemContainerStyle=\\\"\\{StaticResource OverlappedVerticalHandItemStyle\\}\\\"[\\s\\S]*?<StackPanel Orientation=\\\"Vertical\\\"\\s*/>",
            RegexOptions.CultureInvariant),
            xaml);
        Assert.Matches(new Regex(
            "ItemsControl ItemsSource=\\\"\\{Binding RightSeatHand\\.Cards\\}\\\"[\\s\\S]*?ItemContainerStyle=\\\"\\{StaticResource OverlappedVerticalHandItemStyle\\}\\\"[\\s\\S]*?<StackPanel Orientation=\\\"Vertical\\\"\\s*/>",
            RegexOptions.CultureInvariant),
            xaml);
        Assert.Matches(new Regex(
            "ItemsControl ItemsSource=\\\"\\{Binding BottomSeatHand\\.Cards\\}\\\"[\\s\\S]*?ItemContainerStyle=\\\"\\{StaticResource OverlappedHorizontalHandItemStyle\\}\\\"",
            RegexOptions.CultureInvariant),
            xaml);
    }

    [Fact]
    public void MainWindowXaml_ExpandsSideSeatHandsVertically()
    {
        var xaml = File.ReadAllText(GetMainWindowXamlPath());
        Assert.Contains("<Setter Property=\"BorderThickness\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        var leftSeatSection = ExtractSection(
            xaml,
            "<Border Grid.Row=\"0\"\n                        Grid.RowSpan=\"3\"\n                        Grid.Column=\"0\">",
            "                </Border>");
        var rightSeatSection = ExtractSection(
            xaml,
            "<Border Grid.Row=\"0\"\n                        Grid.RowSpan=\"3\"\n                        Grid.Column=\"4\">",
            "                </Border>");

        Assert.Contains("<Grid Margin=\"0,0,18,0\">", leftSeatSection, StringComparison.Ordinal);
        Assert.Contains("Grid.RowSpan=\"3\"", leftSeatSection, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"Auto\" />", leftSeatSection, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" />", leftSeatSection, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", leftSeatSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"0,68,18,68\"", leftSeatSection, StringComparison.Ordinal);

        Assert.Contains("<Grid Margin=\"18,0,0,0\">", rightSeatSection, StringComparison.Ordinal);
        Assert.Contains("Grid.RowSpan=\"3\"", rightSeatSection, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"Auto\" />", rightSeatSection, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" />", rightSeatSection, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", rightSeatSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"18,68,0,68\"", rightSeatSection, StringComparison.Ordinal);
    }

    [Fact]
    public void AppXaml_UsesHighContrastMenuColors()
    {
        var xaml = File.ReadAllText(GetAppXamlPath());

        Assert.Contains("x:Key=\"MenuBarBackgroundColor\">#FF203244</Color>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MenuPopupBackgroundColor\">#FFF7FBFF</Color>", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MenuPopupTextColor\">#FF17212B</Color>", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{StaticResource MenuBarBackgroundBrush}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{StaticResource MenuPopupTextBrush}\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"Role\" Value=\"TopLevelHeader\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsHighlighted\" Value=\"True\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsEnabled\" Value=\"False\">", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowXaml_CentersStockAndDiscardPilesTogether()
    {
        var xaml = File.ReadAllText(GetMainWindowXamlPath());

        Assert.Contains("Header=\"Central piles\"", xaml, StringComparison.Ordinal);
        Assert.Matches(new Regex(
            "<Border Grid\\.Row=\\\"1\\\"[\\s\\S]*?Grid\\.Column=\\\"2\\\"[\\s\\S]*?BorderThickness=\\\"0\\\"",
            RegexOptions.CultureInvariant),
            xaml);
        Assert.Contains("Text=\"Stock pile\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Discard pile\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Face-down draw pile\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding DiscardPile.TopCardVisual}\"", xaml, StringComparison.Ordinal);
    }

    private static string GetMainWindowXamlPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        return Path.Combine(repositoryRoot, "desktop", "CanastaNET.Desktop", "MainWindow.xaml");
    }

    private static string GetAppXamlPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        return Path.Combine(repositoryRoot, "desktop", "CanastaNET.Desktop", "App.xaml");
    }

    private static string ExtractSection(string source, string startMarker, string endMarker)
    {
        var startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Expected to find start marker '{startMarker}'.");

        var endIndex = source.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Expected to find end marker '{endMarker}'.");

        return source[startIndex..(endIndex + endMarker.Length)];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CanastaNET.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
