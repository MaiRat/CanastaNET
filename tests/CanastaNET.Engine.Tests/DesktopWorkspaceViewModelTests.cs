using CanastaNET.Desktop.Core;

namespace CanastaNET.Engine.Tests;

public class DesktopWorkspaceViewModelTests
{
    [Fact]
    public void Workspace_MapsDefaultMatchIntoDesktopScreens()
    {
        var workspace = new CanastaWorkspaceViewModel();

        Assert.Equal(1, workspace.TableOverview.RoundNumber);
        Assert.Equal("AwaitingDraw", workspace.TableOverview.TurnPhase);
        Assert.Equal(workspace.TableOverview.CurrentPlayerName, workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer).Name);
        Assert.NotNull(workspace.CurrentPlayerHand);
        Assert.All(workspace.WaitingPlayerHands, hand => Assert.False(hand.IsCurrentPlayer));
        Assert.NotEmpty(workspace.DiscardPile.Cards);
        Assert.NotNull(workspace.DiscardPile.TopCardVisual);
        Assert.Equal(workspace.ScoreSummary.TeamScores.Count, workspace.TeamMelds.Count);
        Assert.Contains(workspace.LegalCommands, command => command.CommandText == "draw stock");
        Assert.Contains("draw stock", workspace.NextActionPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(workspace.RulesHelpLines);
        Assert.NotEmpty(workspace.SetupPresets);
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

        Assert.Equal("AwaitingDiscard", workspace.TableOverview.TurnPhase);
        Assert.Equal(handSizeBeforeDraw + 1, workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer).Cards.Count);

        var currentHand = workspace.PlayerHands.Single(hand => hand.IsCurrentPlayer);
        currentHand.Cards[0].IsSelected = true;
        workspace.EndTurnCommand.Execute(null);

        Assert.Equal("AwaitingDraw", workspace.TableOverview.TurnPhase);
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
    public void CardPresentation_ExposesStyledMetadata_ForTableArtwork()
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
    public void ViewSettings_DefaultToDetailedCardPresentation_AndCanBeChanged()
    {
        var workspace = new CanastaWorkspaceViewModel();

        Assert.True(workspace.ShowCardPointBadges);
        Assert.True(workspace.ShowDeckRibbons);
        Assert.False(workspace.UseCompactCardSpacing);

        workspace.ShowCardPointBadges = false;
        workspace.ShowDeckRibbons = false;
        workspace.UseCompactCardSpacing = true;

        Assert.False(workspace.ShowCardPointBadges);
        Assert.False(workspace.ShowDeckRibbons);
        Assert.True(workspace.UseCompactCardSpacing);
    }
}
