using CanastaNET.Cli;
using CanastaNET.Engine;

namespace CanastaNET.Engine.Tests;

public class GameSkeletonTests
{
    [Fact]
    public void StartRound_DealsCardsBuildsTeamsAndSetsFirstTurn()
    {
        var engine = new GameEngine();

        var round = engine.StartRound(seed: 42);

        Assert.Equal("CanastaNET engine foundation ready.", engine.GetStartupMessage());
        Assert.Equal(TurnPhase.AwaitingDraw, round.TurnPhase);
        Assert.Equal(1, round.CurrentPlayerIndex);
        Assert.Equal("East", round.CurrentPlayer.Name);
        Assert.Equal(4, round.Players.Count);
        Assert.Equal(2, round.Teams.Count);
        Assert.All(round.Players, player => Assert.Equal(11, player.Hand.Count));
        Assert.Equal([0, 2], round.Teams[0].PlayerIndexes);
        Assert.Equal([1, 3], round.Teams[1].PlayerIndexes);
        Assert.All(round.Teams, team => Assert.Empty(team.Melds));
        Assert.Single(round.DiscardPile);
        Assert.Equal(63, round.StockPile.Count);

        var allCards = round.Players.SelectMany(player => player.Hand)
            .Concat(round.StockPile)
            .Concat(round.DiscardPile)
            .ToArray();

        Assert.Equal(108, allCards.Length);
        Assert.Equal(108, allCards.Select(card => card.InstanceId).Distinct().Count());
    }

    [Fact]
    public void StartRound_RespectsCustomPlayerOrderTeamCountAndDealer()
    {
        var engine = new GameEngine();
        var configuration = new GameConfiguration(
            ["Ava", "Ben", "Cara", "Dax", "Elle", "Finn"],
            teamCount: 3,
            dealerIndex: 4,
            cardsPerPlayer: 5);

        var round = engine.StartRound(configuration, seed: 7);

        Assert.Equal(["Ava", "Ben", "Cara", "Dax", "Elle", "Finn"], round.Players.Select(player => player.Name));
        Assert.Equal(5, round.CurrentPlayerIndex);
        Assert.Equal([0, 3], round.Teams[0].PlayerIndexes);
        Assert.Equal([1, 4], round.Teams[1].PlayerIndexes);
        Assert.Equal([2, 5], round.Teams[2].PlayerIndexes);
        Assert.All(round.Players, player => Assert.Equal(5, player.Hand.Count));
    }

    [Fact]
    public void StartRound_SupportsMinimumTwoPlayerConfiguration()
    {
        var engine = new GameEngine();
        var configuration = new GameConfiguration(
            ["North", "South"],
            teamCount: 2,
            dealerIndex: 1,
            deckCount: 1);

        var round = engine.StartRound(configuration, seed: 5);

        Assert.Equal(2, round.Players.Count);
        Assert.Equal(2, round.Teams.Count);
        Assert.Equal(0, round.CurrentPlayerIndex);
        Assert.Equal("North", round.CurrentPlayer.Name);
        Assert.All(round.Players, player => Assert.Equal(11, player.Hand.Count));
        Assert.Equal([0], round.Teams[0].PlayerIndexes);
        Assert.Equal([1], round.Teams[1].PlayerIndexes);
        Assert.Equal(31, round.StockPile.Count);
    }

    [Fact]
    public void DrawAndDiscard_AdvanceTurnAndUpdatePiles()
    {
        var engine = new GameEngine();
        var round = engine.StartRound(seed: 42);

        var afterDraw = engine.DrawFromStock(round);
        var discardedCard = afterDraw.CurrentPlayer.Hand[0];
        var afterDiscard = engine.Discard(afterDraw, discardedCard);

        Assert.Equal(TurnPhase.AwaitingDiscard, afterDraw.TurnPhase);
        Assert.Equal(12, afterDraw.CurrentPlayer.Hand.Count);
        Assert.Equal(62, afterDraw.StockPile.Count);

        Assert.Equal(TurnPhase.AwaitingDraw, afterDiscard.TurnPhase);
        Assert.Equal(2, afterDiscard.CurrentPlayerIndex);
        Assert.Equal(1, afterDiscard.CompletedTurnCount);
        Assert.Equal(11, afterDiscard.Players[1].Hand.Count);
        Assert.Equal(2, afterDiscard.DiscardPile.Count);
        Assert.Equal(discardedCard, afterDiscard.DiscardPile[^1]);
    }

    [Fact]
    public void InvalidMoves_ThrowGameRuleViolationExceptions()
    {
        var engine = new GameEngine();
        var round = engine.StartRound(seed: 42);

        var beforeDrawingDiscardError = Assert.Throws<GameRuleViolationException>(() => engine.Discard(round, round.CurrentPlayer.Hand[0]));
        Assert.Contains("must draw before discarding", beforeDrawingDiscardError.Message);

        var afterDraw = engine.DrawFromStock(round);
        var doubleDrawError = Assert.Throws<GameRuleViolationException>(() => engine.DrawFromStock(afterDraw));
        Assert.Contains("must discard before drawing again", doubleDrawError.Message);

        var missingCard = round.Players[2].Hand[0];
        var discardMissingCardError = Assert.Throws<GameRuleViolationException>(() => engine.Discard(afterDraw, missingCard));
        Assert.Contains("cannot discard a card that is not in hand", discardMissingCardError.Message);
    }

    [Fact]
    public void DrawFromStock_CompletesRoundWhenStockIsEmpty()
    {
        var engine = new GameEngine();
        var configuration = new GameConfiguration(
            ["North", "South"],
            teamCount: 2,
            cardsPerPlayer: 26,
            deckCount: 1);
        var round = engine.StartRound(configuration, seed: 1);

        var afterDraw = engine.DrawFromStock(round);
        var afterDiscard = engine.Discard(afterDraw, afterDraw.CurrentPlayer.Hand[0]);
        var completedRound = engine.DrawFromStock(afterDiscard);

        Assert.True(completedRound.IsCompleted);
        Assert.Equal(TurnPhase.Completed, completedRound.TurnPhase);
        Assert.NotNull(completedRound.Summary);
        Assert.Equal(RoundEndReason.StockExhausted, completedRound.Summary!.EndReason);
        Assert.Contains("stock pile is empty", completedRound.Summary.Message);
        Assert.Equal(1, completedRound.Summary.CompletedTurnCount);
    }

    [Fact]
    public void GameConfiguration_RejectsUnevenTeamLayouts()
    {
        var error = Assert.Throws<ArgumentException>(() => new GameConfiguration(["North", "East", "South"], teamCount: 2));

        Assert.Contains("Player count must divide evenly", error.Message);
    }

    [Fact]
    public void CliWelcomeTextIncludesMilestoneOneStatus()
    {
        var text = CliApplication.CreateWelcomeText(new GameEngine());

        Assert.Contains("Welcome to CanastaNET!", text);
        Assert.Contains("Milestone 1 engine foundation", text);
        Assert.Contains("Controlled round state snapshots", text);
        Assert.Contains("Next steps:", text);
        Assert.Contains("- Expand CLI commands", text);
    }
}
