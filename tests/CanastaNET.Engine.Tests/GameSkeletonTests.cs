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

        Assert.Equal("CanastaNET core rules ready.", engine.GetStartupMessage());
        Assert.Equal(TurnPhase.AwaitingDraw, round.TurnPhase);
        Assert.Equal(1, round.CurrentPlayerIndex);
        Assert.Equal("East", round.CurrentPlayer.Name);
        Assert.Equal(42, round.Setup.ShuffleSeed);
        Assert.Equal(1, round.Setup.FirstPlayerIndex);
        Assert.Equal(0, round.Setup.DealerIndex);
        Assert.Equal(11, round.Setup.CardsPerPlayer);
        Assert.Equal(4, round.Players.Count);
        Assert.Equal(2, round.Teams.Count);
        Assert.All(round.Players, player => Assert.Equal(11, player.Hand.Count));
        Assert.Equal([0, 2], round.Teams[0].PlayerIndexes);
        Assert.Equal([1, 3], round.Teams[1].PlayerIndexes);
        Assert.All(round.Teams, team =>
        {
            Assert.Empty(team.Melds);
            Assert.Equal(0, team.StartingScore);
        });
        Assert.Single(round.DiscardPile);
        Assert.Equal(round.DiscardPile.TopCard, round.Setup.OpeningDiscardPile.TopCard);
        Assert.Equal(63, round.StockPile.Count);
        Assert.Equal(63, round.Setup.InitialStockCount);
        Assert.Equal(0, round.CurrentTurnMeldPoints);
        Assert.False(round.CurrentTurnStartedWithOpenTeam);

        var allCards = round.Players.SelectMany(player => player.Hand)
            .Concat(round.StockPile)
            .Concat(round.DiscardPile)
            .ToArray();

        Assert.Equal(108, allCards.Length);
        Assert.Equal(108, allCards.Select(card => card.InstanceId).Distinct().Count());
    }

    [Fact]
    public void StartRound_RespectsCustomPlayerOrderTeamCountDealerAndStartingScores()
    {
        var engine = new GameEngine();
        var configuration = new GameConfiguration(
            ["Ava", "Ben", "Cara", "Dax", "Elle", "Finn"],
            teamCount: 3,
            dealerIndex: 4,
            cardsPerPlayer: 5,
            teamStartingScores: [120, 340, -50]);

        var round = engine.StartRound(configuration, seed: 7);

        Assert.Equal(["Ava", "Ben", "Cara", "Dax", "Elle", "Finn"], round.Players.Select(player => player.Name));
        Assert.Equal(5, round.CurrentPlayerIndex);
        Assert.Equal([0, 3], round.Teams[0].PlayerIndexes);
        Assert.Equal([1, 4], round.Teams[1].PlayerIndexes);
        Assert.Equal([2, 5], round.Teams[2].PlayerIndexes);
        Assert.Equal([120, 340, -50], round.Teams.Select(team => team.StartingScore));
        Assert.All(round.Players, player => Assert.Equal(5, player.Hand.Count));
    }

    [Fact]
    public void StartRound_CanUseDealerStartHouseRule()
    {
        var engine = new GameEngine();
        var configuration = new GameConfiguration(
            ["North", "East", "South", "West"],
            dealerIndex: 2,
            houseRules: new HouseRuleOptions(RoundStartPlayerRule.DealerStartsRound));

        var round = engine.StartRound(configuration, seed: 9);

        Assert.Equal(2, round.CurrentPlayerIndex);
        Assert.Equal("South", round.CurrentPlayer.Name);
        Assert.Equal(RoundStartPlayerRule.DealerStartsRound, round.Configuration.HouseRules.RoundStartPlayerRule);
        Assert.Equal(2, round.Setup.FirstPlayerIndex);
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
        Assert.Equal(0, afterDiscard.CurrentTurnMeldPoints);
        Assert.False(afterDiscard.CurrentTurnStartedWithOpenTeam);
    }

    [Fact]
    public void DrawFromDiscardPile_TakesFrozenPileWithTwoNaturalMatches()
    {
        var engine = new GameEngine();
        var kingHearts = CreateCard(1, CardRank.King, CardSuit.Hearts);
        var kingSpades = CreateCard(2, CardRank.King, CardSuit.Spades);
        var fourClubs = CreateCard(3, CardRank.Four, CardSuit.Clubs);
        var joker = Card.CreateJoker(20, 1);
        var nineClubs = CreateCard(21, CardRank.Nine, CardSuit.Clubs);
        var kingDiamonds = CreateCard(22, CardRank.King, CardSuit.Diamonds);
        var round = CreateRoundState(
            [new PlayerState(0, "North", 0, [kingHearts, kingSpades, fourClubs]), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], [], startingScore: -100), new TeamState(1, [1], [])],
            discardPile: [joker, nineClubs, kingDiamonds],
            turnPhase: TurnPhase.AwaitingDraw);

        var updatedRound = engine.DrawFromDiscardPile(round, [kingHearts, kingSpades]);

        Assert.Equal(TurnPhase.AwaitingDiscard, updatedRound.TurnPhase);
        Assert.True(updatedRound.DiscardPile.IsEmpty);
        Assert.Equal(3, updatedRound.CurrentPlayer.Hand.Count);
        Assert.Equal([fourClubs, joker, nineClubs], updatedRound.CurrentPlayer.Hand);
        Assert.Single(updatedRound.Teams[0].Melds);
        Assert.Equal(CardRank.King, updatedRound.Teams[0].Melds[0].Rank);
        Assert.Equal(3, updatedRound.Teams[0].Melds[0].Cards.Count);
        Assert.Equal(30, updatedRound.CurrentTurnMeldPoints);
    }

    [Fact]
    public void Meld_RejectsOpeningBelowInitialRequirement()
    {
        var engine = new GameEngine();
        var cards = new[]
        {
            CreateCard(1, CardRank.King, CardSuit.Hearts),
            CreateCard(2, CardRank.King, CardSuit.Spades),
            CreateCard(3, CardRank.King, CardSuit.Diamonds)
        };
        var round = CreateRoundState(
            [new PlayerState(0, "North", 0, cards), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], [], startingScore: 0), new TeamState(1, [1], [])]);

        var error = Assert.Throws<GameRuleViolationException>(() => engine.Meld(round, cards));

        Assert.Contains("at least 50 points", error.Message);
    }

    [Fact]
    public void Meld_RejectsWildCardHeavyMelds()
    {
        var engine = new GameEngine();
        var cards = new[]
        {
            CreateCard(1, CardRank.Seven, CardSuit.Hearts),
            CreateCard(2, CardRank.Seven, CardSuit.Spades),
            CreateCard(3, CardRank.Two, CardSuit.Clubs),
            Card.CreateJoker(4, 1)
        };
        var round = CreateRoundState(
            [new PlayerState(0, "North", 0, cards), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], [], startingScore: -100), new TeamState(1, [1], [])]);

        var error = Assert.Throws<GameRuleViolationException>(() => engine.Meld(round, cards));

        Assert.Contains("as many or more wild cards", error.Message);
    }

    [Fact]
    public void Meld_GoingOutCreatesRoundSummaryAndBonuses()
    {
        var engine = new GameEngine();
        var aces = new[]
        {
            CreateCard(1, CardRank.Ace, CardSuit.Clubs),
            CreateCard(2, CardRank.Ace, CardSuit.Diamonds),
            CreateCard(3, CardRank.Ace, CardSuit.Hearts),
            CreateCard(4, CardRank.Ace, CardSuit.Spades),
            CreateCard(5, CardRank.Ace, CardSuit.Clubs, deckNumber: 2),
            CreateCard(6, CardRank.Ace, CardSuit.Diamonds, deckNumber: 2),
            CreateCard(7, CardRank.Ace, CardSuit.Hearts, deckNumber: 2)
        };
        var round = CreateRoundState(
            [new PlayerState(0, "North", 0, aces), new PlayerState(1, "South", 1, [])],
            [new TeamState(0, [0], [], startingScore: -100), new TeamState(1, [1], [])]);

        var completedRound = engine.Meld(round, aces);

        Assert.True(completedRound.IsCompleted);
        Assert.Equal(TurnPhase.Completed, completedRound.TurnPhase);
        Assert.NotNull(completedRound.Summary);
        Assert.Equal(RoundEndReason.PlayerWentOut, completedRound.Summary!.EndReason);
        Assert.Equal(0, completedRound.Summary.EndedByPlayerIndex);
        Assert.Equal(1, completedRound.Summary.CompletedTurnCount);

        var winningTeam = Assert.Single(completedRound.Summary.TeamResults, result => result.TeamIndex == 0);
        Assert.True(winningTeam.WentOut);
        Assert.Equal(140, winningTeam.MeldPoints);
        Assert.Equal(1, winningTeam.NaturalCanastaCount);
        Assert.Equal(500, winningTeam.CanastaBonus);
        Assert.Equal(100, winningTeam.GoingOutBonus);
        Assert.Equal(100, winningTeam.ConcealedHandBonus);
        Assert.Equal(0, winningTeam.HandPenalty);
        Assert.Equal(840, winningTeam.RoundScore);
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
    public void DrawFromStock_CompletesRoundWhenStockIsEmptyAndProducesScores()
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
        Assert.Equal(2, completedRound.Summary.TeamResults.Count);
        Assert.All(completedRound.Summary.TeamResults, result => Assert.Equal(50, result.InitialMeldRequirement));
    }

    [Fact]
    public void GameConfiguration_RejectsUnevenTeamLayouts()
    {
        var error = Assert.Throws<ArgumentException>(() => new GameConfiguration(["North", "East", "South"], teamCount: 2));

        Assert.Contains("Player count must divide evenly", error.Message);
    }

    [Fact]
    public void CliWelcomeTextIncludesCliStatus()
    {
        var text = CliApplication.CreateWelcomeText(new GameEngine());

        Assert.Contains("Welcome to CanastaNET!", text);
        Assert.Contains("The CLI supports interactive match setup, turn actions, state inspection, shared-terminal workflows, and regression scripts.", text);
        Assert.Contains("Use launch options like `--seed 42`", text);
        Assert.Contains("Stock/discard draw flow with frozen discard handling", text);
        Assert.Contains("Type `help`", text);
    }

    private static GameRoundState CreateRoundState(
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<TeamState> teams,
        IEnumerable<Card>? stockPile = null,
        IEnumerable<Card>? discardPile = null,
        TurnPhase turnPhase = TurnPhase.AwaitingDiscard,
        int currentPlayerIndex = 0,
        int completedTurnCount = 0,
        int currentTurnMeldPoints = 0,
        bool currentTurnStartedWithOpenTeam = false,
        RoundSummaryData? summary = null)
    {
        var playerArray = players.ToArray();
        var teamArray = teams.ToArray();
        var discardCards = (discardPile ?? [CreateCard(999, CardRank.Five, CardSuit.Clubs)]).ToArray();
        var stockCards = (stockPile ?? []).ToArray();
        var cardsPerPlayer = playerArray.Any()
            ? Math.Max(playerArray.Max(player => player.Hand.Count), 1)
            : 1;
        var configuration = new GameConfiguration(
            playerArray.Select(player => player.Name),
            teamCount: teamArray.Length,
            cardsPerPlayer: cardsPerPlayer,
            teamStartingScores: teamArray.Select(team => team.StartingScore));

        return new GameRoundState(
            configuration,
            new RoundSetupData(7, currentPlayerIndex, configuration.DealerIndex, cardsPerPlayer, discardCards, stockCards.Length),
            playerArray,
            teamArray,
            new DeckState(stockCards),
            new DiscardPileState(discardCards),
            configuration.DealerIndex,
            currentPlayerIndex,
            turnPhase,
            completedTurnCount,
            summary,
            currentTurnMeldPoints,
            currentTurnStartedWithOpenTeam);
    }

    private static Card CreateCard(int instanceId, CardRank rank, CardSuit suit, int deckNumber = 1) =>
        new(instanceId, rank, suit, deckNumber);
}
