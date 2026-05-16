using CanastaNET.Engine;

namespace CanastaNET.Engine.Tests;

public class MatchLifecycleTests
{
    [Fact]
    public void Apply_InvalidDiscardCommand_ReturnsErrorAndCurrentLegalCommands()
    {
        var engine = new GameEngine();
        var kingHearts = GameTestFactory.CreateCard(1, CardRank.King, CardSuit.Hearts);
        var round = GameTestFactory.CreateRoundState(
            [new PlayerState(0, "North", 0, [kingHearts]), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], []), new TeamState(1, [1], [])],
            turnPhase: TurnPhase.AwaitingDraw);

        var result = engine.Apply(round, GameCommand.Discard(kingHearts.InstanceId));

        Assert.False(result.Succeeded);
        Assert.Equal(round, result.RoundState);
        Assert.Contains("must draw before discarding", result.ErrorMessage);
        Assert.Contains(result.LegalCommands.Commands, command => command.Command.CommandType == GameCommandType.DrawFromStock);
    }

    [Fact]
    public void GetLegalCommands_SuggestsDiscardPickupWhenCurrentHandCanTakeFrozenPile()
    {
        var engine = new GameEngine();
        var kingHearts = GameTestFactory.CreateCard(1, CardRank.King, CardSuit.Hearts);
        var kingSpades = GameTestFactory.CreateCard(2, CardRank.King, CardSuit.Spades);
        var fourClubs = GameTestFactory.CreateCard(3, CardRank.Four, CardSuit.Clubs);
        var joker = Card.CreateJoker(20, 1);
        var nineClubs = GameTestFactory.CreateCard(21, CardRank.Nine, CardSuit.Clubs);
        var kingDiamonds = GameTestFactory.CreateCard(22, CardRank.King, CardSuit.Diamonds);
        var round = GameTestFactory.CreateRoundState(
            [new PlayerState(0, "North", 0, [kingHearts, kingSpades, fourClubs]), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], [], startingScore: -100), new TeamState(1, [1], [])],
            discardPile: [joker, nineClubs, kingDiamonds],
            turnPhase: TurnPhase.AwaitingDraw);

        var catalog = engine.GetLegalCommands(round);
        var discardPickup = Assert.Single(catalog.Commands, command => command.Command.CommandType == GameCommandType.DrawFromDiscardPile);

        Assert.Equal([kingHearts.InstanceId, kingSpades.InstanceId], discardPickup.Command.CardInstanceIds);
        Assert.Contains("frozen discard pile", discardPickup.Description);
    }

    [Fact]
    public void ApplyToMatch_UpdatesTotalsHistoryAndDealerRotationForNextRound()
    {
        var engine = new GameEngine();
        var aces = new[]
        {
            GameTestFactory.CreateCard(1, CardRank.Ace, CardSuit.Clubs),
            GameTestFactory.CreateCard(2, CardRank.Ace, CardSuit.Diamonds),
            GameTestFactory.CreateCard(3, CardRank.Ace, CardSuit.Hearts),
            GameTestFactory.CreateCard(4, CardRank.Ace, CardSuit.Spades),
            GameTestFactory.CreateCard(5, CardRank.Ace, CardSuit.Clubs, deckNumber: 2),
            GameTestFactory.CreateCard(6, CardRank.Ace, CardSuit.Diamonds, deckNumber: 2),
            GameTestFactory.CreateCard(7, CardRank.Ace, CardSuit.Hearts, deckNumber: 2)
        };
        var round = GameTestFactory.CreateRoundState(
            [new PlayerState(0, "North", 0, aces), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], [], startingScore: -100), new TeamState(1, [1], [])]);
        var match = GameTestFactory.CreateMatchState(round, teamScores: [-100, 0], winningScore: 2000);

        var result = engine.Apply(match, GameCommand.Meld(aces.Select(card => card.InstanceId)));

        Assert.True(result.Succeeded);
        Assert.True(result.MatchState.CurrentRound.IsCompleted);
        Assert.Equal([740, 0], result.MatchState.TeamScores);

        var historyEntry = Assert.Single(result.MatchState.RoundHistory);
        Assert.Equal(1, historyEntry.RoundNumber);
        Assert.Equal(round.DealerIndex, historyEntry.DealerIndex);
        Assert.Equal([740, 0], historyEntry.TeamScoresAfterRound);

        var nextRoundMatch = engine.StartNextRound(result.MatchState, seed: 11);

        Assert.Equal(1, nextRoundMatch.CurrentRound.DealerIndex);
        Assert.Equal([740, 0], nextRoundMatch.CurrentRound.Configuration.TeamStartingScores);
        Assert.False(nextRoundMatch.CurrentRound.IsCompleted);
    }

    [Fact]
    public void MatchAndRoundSnapshots_RoundTripSerializableState()
    {
        var engine = new GameEngine();
        var round = GameTestFactory.CreateRoundState(
            [new PlayerState(0, "North", 0, [GameTestFactory.CreateCard(1, CardRank.Five, CardSuit.Clubs)]), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], []), new TeamState(1, [1], [])],
            stockPile: [GameTestFactory.CreateCard(2, CardRank.Six, CardSuit.Hearts)],
            discardPile: [GameTestFactory.CreateCard(3, CardRank.Seven, CardSuit.Spades)],
            turnPhase: TurnPhase.AwaitingDraw);
        var summary = new RoundSummaryData(
            RoundEndReason.StockExhausted,
            "Round complete.",
            3,
            [new TeamRoundResultData(0, 0, 50, 0, 0, 0, 0, 0, 0, 5, -5, false), new TeamRoundResultData(1, 0, 50, 0, 0, 0, 0, 0, 0, 0, 0, false)],
            endedByPlayerIndex: null);
        var match = GameTestFactory.CreateMatchState(
            round,
            teamScores: [120, 340],
            roundHistory: [new MatchRoundRecord(1, 0, 7, summary, [120, 340])]);

        var roundSnapshot = engine.CreateSnapshot(round);
        var restoredRound = engine.LoadSnapshot(roundSnapshot);
        var matchSnapshot = engine.CreateSnapshot(match);
        var restoredMatch = engine.LoadSnapshot(matchSnapshot);

        Assert.Equal(round.CurrentPlayer.Name, restoredRound.CurrentPlayer.Name);
        Assert.Equal(round.StockPile.Select(card => card.InstanceId), restoredRound.StockPile.Select(card => card.InstanceId));
        Assert.Equal(round.DiscardPile.Select(card => card.InstanceId), restoredRound.DiscardPile.Select(card => card.InstanceId));
        Assert.Equal([120, 340], restoredMatch.TeamScores);
        Assert.Single(restoredMatch.RoundHistory);
        Assert.Equal("North", restoredMatch.CurrentRound.CurrentPlayer.Name);
    }

    [Fact]
    public void ApplyToMatch_CompletesMatchWhenWinningScoreIsReached()
    {
        var engine = new GameEngine();
        var aces = new[]
        {
            GameTestFactory.CreateCard(1, CardRank.Ace, CardSuit.Clubs),
            GameTestFactory.CreateCard(2, CardRank.Ace, CardSuit.Diamonds),
            GameTestFactory.CreateCard(3, CardRank.Ace, CardSuit.Hearts),
            GameTestFactory.CreateCard(4, CardRank.Ace, CardSuit.Spades),
            GameTestFactory.CreateCard(5, CardRank.Ace, CardSuit.Clubs, deckNumber: 2),
            GameTestFactory.CreateCard(6, CardRank.Ace, CardSuit.Diamonds, deckNumber: 2),
            GameTestFactory.CreateCard(7, CardRank.Ace, CardSuit.Hearts, deckNumber: 2)
        };
        var round = GameTestFactory.CreateRoundState(
            [new PlayerState(0, "North", 0, aces), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], [], startingScore: -100), new TeamState(1, [1], [])]);
        var match = GameTestFactory.CreateMatchState(round, teamScores: [-100, 0], winningScore: 500);

        var result = engine.Apply(match, GameCommand.Meld(aces.Select(card => card.InstanceId)));

        Assert.True(result.MatchState.IsCompleted);
        Assert.Equal([0], result.MatchState.WinningTeamIndexes);
        Assert.False(result.MatchState.CanStartNextRound);
    }
}
