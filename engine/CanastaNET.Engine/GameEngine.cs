using System.Collections;
using System.Collections.ObjectModel;

namespace CanastaNET.Engine;

public sealed class GameEngine
{
    private static readonly string[] ImplementedSubsystems =
    [
        "Card, deck, hand, team, and meld modeling",
        "Controlled setup and round state snapshots",
        "Stock/discard draw flow with frozen discard handling",
        "Meld validation, going out rules, and round scoring"
    ];

    public string GetStartupMessage() => "CanastaNET core rules ready.";

    public IReadOnlyList<string> GetPlannedSubsystems() => ImplementedSubsystems;

    public string CreateMockGameSummary()
    {
        var implementedSubsystems = string.Join(
            Environment.NewLine,
            ImplementedSubsystems.Select(subsystem => $"- {subsystem}"));

        return string.Join(
            Environment.NewLine,
            GetStartupMessage(),
            "Implemented subsystems:",
            implementedSubsystems);
    }

    public GameRoundState StartRound(GameConfiguration? configuration = null, int? seed = null)
    {
        var roundConfiguration = configuration ?? GameConfiguration.CreateDefault();
        var deck = CreateDeck(roundConfiguration);
        Shuffle(deck, seed);

        var hands = Enumerable.Range(0, roundConfiguration.PlayerOrder.Count)
            .Select(_ => new List<Card>(roundConfiguration.CardsPerPlayer))
            .ToArray();

        var nextSeatToDeal = GetNextPlayerIndex(roundConfiguration.DealerIndex, roundConfiguration.PlayerOrder.Count);

        for (var cardNumber = 0; cardNumber < roundConfiguration.CardsPerPlayer; cardNumber++)
        {
            for (var seatOffset = 0; seatOffset < roundConfiguration.PlayerOrder.Count; seatOffset++)
            {
                var playerIndex = (nextSeatToDeal + seatOffset) % roundConfiguration.PlayerOrder.Count;
                hands[playerIndex].Add(deck[0]);
                deck.RemoveAt(0);
            }
        }

        var discardPile = new List<Card> { deck[0] };
        deck.RemoveAt(0);
        var firstPlayerIndex = GetFirstPlayerIndex(roundConfiguration);

        var players = roundConfiguration.PlayerOrder
            .Select((playerName, playerIndex) => new PlayerState(
                playerIndex,
                playerName,
                playerIndex % roundConfiguration.TeamCount,
                hands[playerIndex]))
            .ToArray();

        var teams = Enumerable.Range(0, roundConfiguration.TeamCount)
            .Select(teamIndex => new TeamState(
                teamIndex,
                players.Where(player => player.TeamIndex == teamIndex).Select(player => player.PlayerIndex),
                [],
                roundConfiguration.TeamStartingScores[teamIndex]))
            .ToArray();

        return new GameRoundState(
            roundConfiguration,
            new RoundSetupData(
                seed,
                firstPlayerIndex,
                roundConfiguration.DealerIndex,
                roundConfiguration.CardsPerPlayer,
                discardPile,
                deck.Count),
            players,
            teams,
            new DeckState(deck),
            new DiscardPileState(discardPile),
            roundConfiguration.DealerIndex,
            firstPlayerIndex,
            TurnPhase.AwaitingDraw,
            0,
            null,
            0,
            false);
    }

    public GameRoundState DrawFromStock(GameRoundState roundState)
    {
        ArgumentNullException.ThrowIfNull(roundState);
        EnsureRoundIsActive(roundState);

        if (roundState.TurnPhase != TurnPhase.AwaitingDraw)
        {
            throw new GameRuleViolationException($"{roundState.CurrentPlayer.Name} must discard before drawing again.");
        }

        if (roundState.StockPile.Count == 0)
        {
            return CreateCompletedRoundState(
                roundState,
                roundState.Players,
                roundState.Teams,
                roundState.StockPile,
                roundState.DiscardPile,
                RoundEndReason.StockExhausted,
                $"{roundState.CurrentPlayer.Name} cannot draw because the stock pile is empty.",
                roundState.CompletedTurnCount,
                endedByPlayerIndex: null,
                concealedHand: false,
                currentTurnMeldPoints: roundState.CurrentTurnMeldPoints,
                currentTurnStartedWithOpenTeam: roundState.CurrentTurnStartedWithOpenTeam);
        }

        var stockPile = roundState.StockPile.ToList();
        var drawnCard = stockPile[0];
        stockPile.RemoveAt(0);

        var currentPlayer = roundState.CurrentPlayer;
        var updatedHand = currentPlayer.Hand.ToList();
        updatedHand.Add(drawnCard);

        var players = roundState.Players.ToArray();
        players[roundState.CurrentPlayerIndex] = new PlayerState(
            currentPlayer.PlayerIndex,
            currentPlayer.Name,
            currentPlayer.TeamIndex,
            updatedHand);

        return new GameRoundState(
            roundState.Configuration,
            roundState.Setup,
            players,
            roundState.Teams,
            new DeckState(stockPile),
            roundState.DiscardPile,
            roundState.DealerIndex,
            roundState.CurrentPlayerIndex,
            TurnPhase.AwaitingDiscard,
            roundState.CompletedTurnCount,
            null,
            roundState.CurrentTurnMeldPoints,
            roundState.CurrentTurnStartedWithOpenTeam);
    }

    public GameRoundState DrawFromDiscardPile(GameRoundState roundState, IEnumerable<Card>? matchingCardsFromHand = null)
    {
        ArgumentNullException.ThrowIfNull(roundState);
        EnsureRoundIsActive(roundState);

        if (roundState.TurnPhase != TurnPhase.AwaitingDraw)
        {
            throw new GameRuleViolationException($"{roundState.CurrentPlayer.Name} must discard before drawing again.");
        }

        if (roundState.DiscardPile.IsEmpty)
        {
            throw new GameRuleViolationException("The discard pile is empty.");
        }

        var topCard = roundState.DiscardPile.TopCard;

        if (topCard.IsWild)
        {
            throw new GameRuleViolationException("Wild cards cannot be taken directly from the discard pile.");
        }

        var currentPlayer = roundState.CurrentPlayer;
        var currentTeam = roundState.Teams[currentPlayer.TeamIndex];
        var existingMeld = currentTeam.Melds.FirstOrDefault(meld => meld.Rank == topCard.Rank);
        var selectedCards = (matchingCardsFromHand ?? []).ToArray();
        var naturalMatches = selectedCards.Where(card => !card.IsWild).ToArray();

        if (selectedCards.Any(card => card.IsWild))
        {
            throw new GameRuleViolationException("Discard pile pickups must be opened with natural cards from hand.");
        }

        if (naturalMatches.Any(card => card.Rank != topCard.Rank))
        {
            throw new GameRuleViolationException($"Discard pile pickups must use cards matching {topCard.Rank}.");
        }

        if (roundState.DiscardPile.IsFrozen)
        {
            if (naturalMatches.Length != 2)
            {
                throw new GameRuleViolationException($"The frozen discard pile requires two natural {topCard.Rank} cards from hand.");
            }
        }
        else if (existingMeld is null && naturalMatches.Length < 2)
        {
            throw new GameRuleViolationException($"Taking the discard pile requires two natural {topCard.Rank} cards or an existing team meld.");
        }

        var updatedHand = RemoveCardsFromHand(currentPlayer.Hand, selectedCards);
        var retainedDiscardCards = roundState.DiscardPile.SkipLast(1);
        updatedHand.AddRange(retainedDiscardCards);

        var players = roundState.Players.ToArray();
        players[roundState.CurrentPlayerIndex] = new PlayerState(
            currentPlayer.PlayerIndex,
            currentPlayer.Name,
            currentPlayer.TeamIndex,
            updatedHand);

        var addedMeldCards = selectedCards.Concat([topCard]).ToArray();
        var teams = roundState.Teams.ToArray();
        teams[currentTeam.TeamIndex] = ApplyCardsToTeamMeld(currentTeam, addedMeldCards, roundState.CurrentTurnMeldPoints, targetRankOverride: topCard.Rank);
        var updatedTurnMeldPoints = roundState.CurrentTurnMeldPoints + GetCardPointTotal(addedMeldCards);

        if (updatedHand.Count == 0)
        {
            EnsureCanGoOut(teams[currentTeam.TeamIndex], currentPlayer.Name);

            return CreateCompletedRoundState(
                roundState,
                players,
                teams,
                roundState.StockPile,
                new DiscardPileState([]),
                RoundEndReason.PlayerWentOut,
                $"{currentPlayer.Name} took the discard pile and went out.",
                roundState.CompletedTurnCount + 1,
                currentPlayer.PlayerIndex,
                concealedHand: !roundState.CurrentTurnStartedWithOpenTeam,
                currentTurnMeldPoints: updatedTurnMeldPoints,
                currentTurnStartedWithOpenTeam: roundState.CurrentTurnStartedWithOpenTeam);
        }

        return new GameRoundState(
            roundState.Configuration,
            roundState.Setup,
            players,
            teams,
            roundState.StockPile,
            new DiscardPileState([]),
            roundState.DealerIndex,
            roundState.CurrentPlayerIndex,
            TurnPhase.AwaitingDiscard,
            roundState.CompletedTurnCount,
            null,
            updatedTurnMeldPoints,
            roundState.CurrentTurnStartedWithOpenTeam);
    }

    public GameRoundState Meld(GameRoundState roundState, IEnumerable<Card> cards) =>
        MeldCore(roundState, targetRankOverride: null, cards);

    public GameRoundState Meld(GameRoundState roundState, CardRank targetRank, IEnumerable<Card> cards) =>
        MeldCore(roundState, targetRank, cards);

    private GameRoundState MeldCore(GameRoundState roundState, CardRank? targetRankOverride, IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(roundState);
        ArgumentNullException.ThrowIfNull(cards);
        EnsureRoundIsActive(roundState);

        if (roundState.TurnPhase != TurnPhase.AwaitingDiscard)
        {
            throw new GameRuleViolationException($"{roundState.CurrentPlayer.Name} must draw before melding.");
        }

        var cardsToMeld = cards.ToArray();

        if (cardsToMeld.Length == 0)
        {
            throw new GameRuleViolationException("At least one card must be provided when melding.");
        }

        var currentPlayer = roundState.CurrentPlayer;
        var updatedHand = RemoveCardsFromHand(currentPlayer.Hand, cardsToMeld);

        var players = roundState.Players.ToArray();
        players[roundState.CurrentPlayerIndex] = new PlayerState(
            currentPlayer.PlayerIndex,
            currentPlayer.Name,
            currentPlayer.TeamIndex,
            updatedHand);

        var currentTeam = roundState.Teams[currentPlayer.TeamIndex];
        var teams = roundState.Teams.ToArray();
        teams[currentTeam.TeamIndex] = ApplyCardsToTeamMeld(currentTeam, cardsToMeld, roundState.CurrentTurnMeldPoints, targetRankOverride);
        var updatedTurnMeldPoints = roundState.CurrentTurnMeldPoints + GetCardPointTotal(cardsToMeld);

        if (updatedHand.Count == 0)
        {
            EnsureCanGoOut(teams[currentTeam.TeamIndex], currentPlayer.Name);

            return CreateCompletedRoundState(
                roundState,
                players,
                teams,
                roundState.StockPile,
                roundState.DiscardPile,
                RoundEndReason.PlayerWentOut,
                $"{currentPlayer.Name} went out.",
                roundState.CompletedTurnCount + 1,
                currentPlayer.PlayerIndex,
                concealedHand: !roundState.CurrentTurnStartedWithOpenTeam,
                currentTurnMeldPoints: updatedTurnMeldPoints,
                currentTurnStartedWithOpenTeam: roundState.CurrentTurnStartedWithOpenTeam);
        }

        return new GameRoundState(
            roundState.Configuration,
            roundState.Setup,
            players,
            teams,
            roundState.StockPile,
            roundState.DiscardPile,
            roundState.DealerIndex,
            roundState.CurrentPlayerIndex,
            TurnPhase.AwaitingDiscard,
            roundState.CompletedTurnCount,
            null,
            updatedTurnMeldPoints,
            roundState.CurrentTurnStartedWithOpenTeam);
    }

    public GameRoundState Discard(GameRoundState roundState, Card card)
    {
        ArgumentNullException.ThrowIfNull(roundState);
        EnsureRoundIsActive(roundState);

        if (roundState.TurnPhase != TurnPhase.AwaitingDiscard)
        {
            throw new GameRuleViolationException($"{roundState.CurrentPlayer.Name} must draw before discarding.");
        }

        var currentPlayer = roundState.CurrentPlayer;
        var updatedHand = currentPlayer.Hand.ToList();
        var handIndex = updatedHand.FindIndex(currentCard => currentCard.InstanceId == card.InstanceId);

        if (handIndex < 0)
        {
            throw new GameRuleViolationException($"{currentPlayer.Name} cannot discard a card that is not in hand.");
        }

        updatedHand.RemoveAt(handIndex);

        var players = roundState.Players.ToArray();
        players[roundState.CurrentPlayerIndex] = new PlayerState(
            currentPlayer.PlayerIndex,
            currentPlayer.Name,
            currentPlayer.TeamIndex,
            updatedHand);

        var discardPile = roundState.DiscardPile.ToList();
        discardPile.Add(card);
        var updatedDiscardPile = new DiscardPileState(discardPile);

        if (updatedHand.Count == 0)
        {
            EnsureCanGoOut(roundState.Teams[currentPlayer.TeamIndex], currentPlayer.Name);

            return CreateCompletedRoundState(
                roundState,
                players,
                roundState.Teams,
                roundState.StockPile,
                updatedDiscardPile,
                RoundEndReason.PlayerWentOut,
                $"{currentPlayer.Name} discarded the final card and went out.",
                roundState.CompletedTurnCount + 1,
                currentPlayer.PlayerIndex,
                concealedHand: !roundState.CurrentTurnStartedWithOpenTeam,
                currentTurnMeldPoints: roundState.CurrentTurnMeldPoints,
                currentTurnStartedWithOpenTeam: roundState.CurrentTurnStartedWithOpenTeam);
        }

        var nextPlayerIndex = GetNextPlayerIndex(roundState.CurrentPlayerIndex, roundState.Players.Count);
        var nextTeamIndex = roundState.Players[nextPlayerIndex].TeamIndex;

        return new GameRoundState(
            roundState.Configuration,
            roundState.Setup,
            players,
            roundState.Teams,
            roundState.StockPile,
            updatedDiscardPile,
            roundState.DealerIndex,
            nextPlayerIndex,
            TurnPhase.AwaitingDraw,
            roundState.CompletedTurnCount + 1,
            null,
            0,
            roundState.Teams[nextTeamIndex].Melds.Count > 0);
    }

    private static GameRoundState CreateCompletedRoundState(
        GameRoundState sourceRound,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<TeamState> teams,
        DeckState stockPile,
        DiscardPileState discardPile,
        RoundEndReason endReason,
        string message,
        int completedTurnCount,
        int? endedByPlayerIndex,
        bool concealedHand,
        int currentTurnMeldPoints,
        bool currentTurnStartedWithOpenTeam)
    {
        var summary = CreateRoundSummary(
            sourceRound.Configuration,
            players,
            teams,
            endReason,
            message,
            completedTurnCount,
            endedByPlayerIndex,
            concealedHand);

        return new GameRoundState(
            sourceRound.Configuration,
            sourceRound.Setup,
            players,
            teams,
            stockPile,
            discardPile,
            sourceRound.DealerIndex,
            sourceRound.CurrentPlayerIndex,
            TurnPhase.Completed,
            completedTurnCount,
            summary,
            currentTurnMeldPoints,
            currentTurnStartedWithOpenTeam);
    }

    private static RoundSummaryData CreateRoundSummary(
        GameConfiguration configuration,
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<TeamState> teams,
        RoundEndReason endReason,
        string message,
        int completedTurnCount,
        int? endedByPlayerIndex,
        bool concealedHand)
    {
        var wentOutTeamIndex = endedByPlayerIndex.HasValue ? players[endedByPlayerIndex.Value].TeamIndex : (int?)null;
        var concealedTeamIndex = concealedHand ? wentOutTeamIndex : null;

        var teamResults = teams
            .Select(team => CreateTeamRoundResult(
                configuration,
                players,
                team,
                wentOutTeamIndex,
                concealedTeamIndex))
            .OrderBy(result => result.TeamIndex)
            .ToArray();

        return new RoundSummaryData(endReason, message, completedTurnCount, teamResults, endedByPlayerIndex);
    }

    private static TeamRoundResultData CreateTeamRoundResult(
        GameConfiguration configuration,
        IReadOnlyList<PlayerState> players,
        TeamState team,
        int? wentOutTeamIndex,
        int? concealedTeamIndex)
    {
        var meldPoints = team.Melds.Sum(meld => meld.CardPointTotal);
        var naturalCanastaCount = team.Melds.Count(meld => meld.CanastaKind == CanastaKind.Natural);
        var mixedCanastaCount = team.Melds.Count(meld => meld.CanastaKind == CanastaKind.Mixed);
        var canastaBonus = (naturalCanastaCount * 500) + (mixedCanastaCount * 300);
        var goingOutBonus = team.TeamIndex == wentOutTeamIndex ? 100 : 0;
        var concealedHandBonus = team.TeamIndex == concealedTeamIndex ? 100 : 0;
        var handPenalty = players
            .Where(player => player.TeamIndex == team.TeamIndex)
            .SelectMany(player => player.Hand)
            .Sum(card => card.PointValue);

        return new TeamRoundResultData(
            team.TeamIndex,
            team.StartingScore,
            GetInitialMeldRequirement(team.StartingScore),
            meldPoints,
            naturalCanastaCount,
            mixedCanastaCount,
            canastaBonus,
            goingOutBonus,
            concealedHandBonus,
            handPenalty,
            meldPoints + canastaBonus + goingOutBonus + concealedHandBonus - handPenalty,
            team.TeamIndex == wentOutTeamIndex);
    }

    private static TeamState ApplyCardsToTeamMeld(
        TeamState team,
        IReadOnlyList<Card> cardsToAdd,
        int currentTurnMeldPoints,
        CardRank? targetRankOverride)
    {
        if (cardsToAdd.Count == 0)
        {
            throw new GameRuleViolationException("At least one card must be provided when melding.");
        }

        var targetRank = ResolveTargetRank(cardsToAdd, targetRankOverride);
        var melds = team.Melds.ToList();
        var existingMeldIndex = melds.FindIndex(meld => meld.Rank == targetRank);
        var combinedCards = existingMeldIndex >= 0
            ? melds[existingMeldIndex].Cards.Concat(cardsToAdd).ToArray()
            : cardsToAdd.ToArray();
        var updatedMeld = new MeldState(combinedCards);

        if (team.Melds.Count == 0)
        {
            var requiredOpeningPoints = GetInitialMeldRequirement(team.StartingScore);
            var updatedTurnValue = currentTurnMeldPoints + GetCardPointTotal(cardsToAdd);

            if (updatedTurnValue < requiredOpeningPoints)
            {
                throw new GameRuleViolationException($"Team {team.TeamIndex + 1} must open with at least {requiredOpeningPoints} points.");
            }
        }

        if (existingMeldIndex >= 0)
        {
            melds[existingMeldIndex] = updatedMeld;
        }
        else
        {
            melds.Add(updatedMeld);
        }

        return new TeamState(team.TeamIndex, team.PlayerIndexes, melds, team.StartingScore);
    }

    private static CardRank ResolveTargetRank(IReadOnlyList<Card> cards, CardRank? targetRankOverride)
    {
        if (targetRankOverride is { } explicitTarget && explicitTarget is CardRank.Two or CardRank.Joker)
        {
            throw new GameRuleViolationException("Wild cards cannot define a meld rank.");
        }

        var naturalRanks = cards
            .Where(card => !card.IsWild)
            .Select(card => card.Rank)
            .Distinct()
            .ToArray();

        if (naturalRanks.Length > 1)
        {
            throw new GameRuleViolationException("All natural cards in a meld must share the same rank.");
        }

        if (naturalRanks.Length == 1)
        {
            if (targetRankOverride is { } explicitTargetRank && explicitTargetRank != naturalRanks[0])
            {
                throw new GameRuleViolationException($"The provided cards do not match the requested {explicitTargetRank} meld.");
            }

            return naturalRanks[0];
        }

        if (targetRankOverride is { } rank)
        {
            return rank;
        }

        throw new GameRuleViolationException("Meld additions must include a natural card unless an existing meld rank is specified.");
    }

    private static void EnsureCanGoOut(TeamState team, string playerName)
    {
        if (!team.Melds.Any(meld => meld.IsCanasta))
        {
            throw new GameRuleViolationException($"{playerName} cannot go out without a canasta.");
        }
    }

    private static List<Card> RemoveCardsFromHand(HandState hand, IEnumerable<Card> cardsToRemove)
    {
        var updatedHand = hand.ToList();

        foreach (var card in cardsToRemove)
        {
            var handIndex = updatedHand.FindIndex(currentCard => currentCard.InstanceId == card.InstanceId);

            if (handIndex < 0)
            {
                throw new GameRuleViolationException("A card was selected that is not in the current player's hand.");
            }

            updatedHand.RemoveAt(handIndex);
        }

        return updatedHand;
    }

    private static int GetInitialMeldRequirement(int teamStartingScore) =>
        teamStartingScore switch
        {
            < 0 => 15,
            < 1500 => 50,
            < 3000 => 90,
            _ => 120
        };

    private static int GetCardPointTotal(IEnumerable<Card> cards) => cards.Sum(card => card.PointValue);

    private static void EnsureRoundIsActive(GameRoundState roundState)
    {
        if (roundState.IsCompleted)
        {
            throw new GameRuleViolationException("The round is already complete.");
        }
    }

    private static int GetNextPlayerIndex(int playerIndex, int playerCount) => (playerIndex + 1) % playerCount;

    private static int GetFirstPlayerIndex(GameConfiguration configuration) =>
        configuration.HouseRules.RoundStartPlayerRule switch
        {
            RoundStartPlayerRule.DealerStartsRound => configuration.DealerIndex,
            _ => GetNextPlayerIndex(configuration.DealerIndex, configuration.PlayerOrder.Count)
        };

    private static List<Card> CreateDeck(GameConfiguration configuration)
    {
        var totalCards = configuration.DeckCount * (52 + configuration.JokersPerDeck);
        var cardsRequiredForSetup = (configuration.PlayerOrder.Count * configuration.CardsPerPlayer) + 1;

        if (totalCards < cardsRequiredForSetup)
        {
            throw new InvalidOperationException(
                $"The configured deck does not contain enough cards to deal {configuration.CardsPerPlayer} cards to {configuration.PlayerOrder.Count} players and start the discard pile.");
        }

        var deck = new List<Card>(totalCards);
        var instanceId = 1;

        for (var deckNumber = 1; deckNumber <= configuration.DeckCount; deckNumber++)
        {
            foreach (var suit in Enum.GetValues<CardSuit>())
            {
                foreach (var rank in Card.NonJokerRanks)
                {
                    deck.Add(new Card(instanceId++, rank, suit, deckNumber));
                }
            }

            for (var jokerNumber = 0; jokerNumber < configuration.JokersPerDeck; jokerNumber++)
            {
                deck.Add(Card.CreateJoker(instanceId++, deckNumber));
            }
        }

        return deck;
    }

    private static void Shuffle(IList<Card> deck, int? seed)
    {
        var random = seed.HasValue ? new Random(seed.Value) : Random.Shared;

        for (var cardIndex = deck.Count - 1; cardIndex > 0; cardIndex--)
        {
            var swapIndex = random.Next(cardIndex + 1);
            (deck[cardIndex], deck[swapIndex]) = (deck[swapIndex], deck[cardIndex]);
        }
    }
}

public sealed class GameConfiguration
{
    public GameConfiguration(
        IEnumerable<string> playerOrder,
        int teamCount = 2,
        int dealerIndex = 0,
        int deckCount = 2,
        int cardsPerPlayer = 11,
        int jokersPerDeck = 2,
        HouseRuleOptions? houseRules = null,
        IEnumerable<int>? teamStartingScores = null)
    {
        ArgumentNullException.ThrowIfNull(playerOrder);

        var orderedPlayers = playerOrder
            .Select(playerName => playerName?.Trim())
            .ToArray();

        if (orderedPlayers.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(playerOrder), "At least two players are required.");
        }

        if (orderedPlayers.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Player names must be non-empty.", nameof(playerOrder));
        }

        if (orderedPlayers.Distinct(StringComparer.Ordinal).Count() != orderedPlayers.Length)
        {
            throw new ArgumentException("Player names must be unique to preserve turn order.", nameof(playerOrder));
        }

        if (teamCount < 1 || teamCount > orderedPlayers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(teamCount), "Team count must be between one and the number of players.");
        }

        if (orderedPlayers.Length % teamCount != 0)
        {
            throw new ArgumentException("Player count must divide evenly into the requested team count.", nameof(teamCount));
        }

        if (dealerIndex < 0 || dealerIndex >= orderedPlayers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(dealerIndex), "Dealer index must reference an existing player.");
        }

        if (deckCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(deckCount), "At least one deck is required.");
        }

        if (cardsPerPlayer < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cardsPerPlayer), "Players must be dealt at least one card.");
        }

        if (jokersPerDeck < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(jokersPerDeck), "Jokers per deck cannot be negative.");
        }

        var resolvedTeamStartingScores = (teamStartingScores ?? Enumerable.Repeat(0, teamCount)).ToArray();

        if (resolvedTeamStartingScores.Length != teamCount)
        {
            throw new ArgumentException("Starting scores must include exactly one entry per team.", nameof(teamStartingScores));
        }

        PlayerOrder = Array.AsReadOnly(orderedPlayers.Select(playerName => playerName!).ToArray());
        TeamCount = teamCount;
        DealerIndex = dealerIndex;
        DeckCount = deckCount;
        CardsPerPlayer = cardsPerPlayer;
        JokersPerDeck = jokersPerDeck;
        HouseRules = houseRules ?? HouseRuleOptions.CreateDefault();
        TeamStartingScores = Array.AsReadOnly(resolvedTeamStartingScores);
    }

    public IReadOnlyList<string> PlayerOrder { get; }

    public int TeamCount { get; }

    public int DealerIndex { get; }

    public int DeckCount { get; }

    public int CardsPerPlayer { get; }

    public int JokersPerDeck { get; }

    public HouseRuleOptions HouseRules { get; }

    public IReadOnlyList<int> TeamStartingScores { get; }

    public static GameConfiguration CreateDefault() => new(["North", "East", "South", "West"]);
}

public sealed class HouseRuleOptions
{
    public HouseRuleOptions(RoundStartPlayerRule roundStartPlayerRule = RoundStartPlayerRule.NextPlayerAfterDealer)
    {
        RoundStartPlayerRule = roundStartPlayerRule;
    }

    public RoundStartPlayerRule RoundStartPlayerRule { get; }

    public static HouseRuleOptions CreateDefault() => new();
}

public sealed class RoundSetupData
{
    public RoundSetupData(
        int? shuffleSeed,
        int firstPlayerIndex,
        int dealerIndex,
        int cardsPerPlayer,
        IEnumerable<Card> openingDiscardPile,
        int initialStockCount)
    {
        ArgumentNullException.ThrowIfNull(openingDiscardPile);

        if (firstPlayerIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstPlayerIndex));
        }

        if (dealerIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dealerIndex));
        }

        if (cardsPerPlayer < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cardsPerPlayer));
        }

        if (initialStockCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialStockCount));
        }

        ShuffleSeed = shuffleSeed;
        FirstPlayerIndex = firstPlayerIndex;
        DealerIndex = dealerIndex;
        CardsPerPlayer = cardsPerPlayer;
        OpeningDiscardPile = new DiscardPileState(openingDiscardPile);
        InitialStockCount = initialStockCount;
    }

    public int? ShuffleSeed { get; }

    public int FirstPlayerIndex { get; }

    public int DealerIndex { get; }

    public int CardsPerPlayer { get; }

    public DiscardPileState OpeningDiscardPile { get; }

    public int InitialStockCount { get; }
}

public sealed class GameRoundState
{
    public GameRoundState(
        GameConfiguration configuration,
        RoundSetupData setup,
        IEnumerable<PlayerState> players,
        IEnumerable<TeamState> teams,
        DeckState stockPile,
        DiscardPileState discardPile,
        int dealerIndex,
        int currentPlayerIndex,
        TurnPhase turnPhase,
        int completedTurnCount,
        RoundSummaryData? summary,
        int currentTurnMeldPoints,
        bool currentTurnStartedWithOpenTeam)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Setup = setup ?? throw new ArgumentNullException(nameof(setup));

        var roundPlayers = (players ?? throw new ArgumentNullException(nameof(players))).ToArray();
        var roundTeams = (teams ?? throw new ArgumentNullException(nameof(teams))).ToArray();
        ArgumentNullException.ThrowIfNull(stockPile);
        ArgumentNullException.ThrowIfNull(discardPile);

        if (roundPlayers.Length == 0)
        {
            throw new ArgumentException("Round state must contain at least one player.", nameof(players));
        }

        if (currentPlayerIndex < 0 || currentPlayerIndex >= roundPlayers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPlayerIndex));
        }

        if (dealerIndex < 0 || dealerIndex >= roundPlayers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(dealerIndex));
        }

        if (completedTurnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedTurnCount));
        }

        if (currentTurnMeldPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentTurnMeldPoints));
        }

        if (turnPhase == TurnPhase.Completed && summary is null)
        {
            throw new ArgumentException("Completed rounds must include a summary.", nameof(summary));
        }

        Players = Array.AsReadOnly(roundPlayers);
        Teams = Array.AsReadOnly(roundTeams);
        StockPile = stockPile;
        DiscardPile = discardPile;
        DealerIndex = dealerIndex;
        CurrentPlayerIndex = currentPlayerIndex;
        TurnPhase = turnPhase;
        CompletedTurnCount = completedTurnCount;
        Summary = summary;
        CurrentTurnMeldPoints = currentTurnMeldPoints;
        CurrentTurnStartedWithOpenTeam = currentTurnStartedWithOpenTeam;
    }

    public GameConfiguration Configuration { get; }

    public RoundSetupData Setup { get; }

    public IReadOnlyList<PlayerState> Players { get; }

    public IReadOnlyList<TeamState> Teams { get; }

    public DeckState StockPile { get; }

    public DiscardPileState DiscardPile { get; }

    public int DealerIndex { get; }

    public int CurrentPlayerIndex { get; }

    public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];

    public TurnPhase TurnPhase { get; }

    public int CompletedTurnCount { get; }

    public RoundSummaryData? Summary { get; }

    public int CurrentTurnMeldPoints { get; }

    public bool CurrentTurnStartedWithOpenTeam { get; }

    public bool IsCompleted => TurnPhase == TurnPhase.Completed;
}

public sealed class PlayerState
{
    public PlayerState(int playerIndex, string name, int teamIndex, IEnumerable<Card> hand)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Player name must be non-empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(hand);

        PlayerIndex = playerIndex;
        Name = name;
        TeamIndex = teamIndex;
        Hand = new HandState(hand);
    }

    public int PlayerIndex { get; }

    public string Name { get; }

    public int TeamIndex { get; }

    public HandState Hand { get; }
}

public sealed class TeamState
{
    public TeamState(int teamIndex, IEnumerable<int> playerIndexes, IEnumerable<MeldState> melds, int startingScore = 0)
    {
        ArgumentNullException.ThrowIfNull(playerIndexes);
        ArgumentNullException.ThrowIfNull(melds);

        TeamIndex = teamIndex;
        PlayerIndexes = Array.AsReadOnly(playerIndexes.ToArray());
        Melds = Array.AsReadOnly(melds.ToArray());
        StartingScore = startingScore;
    }

    public int TeamIndex { get; }

    public IReadOnlyList<int> PlayerIndexes { get; }

    public IReadOnlyList<MeldState> Melds { get; }

    public int StartingScore { get; }
}

public sealed class MeldState
{
    public MeldState(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        var meldCards = cards.ToArray();

        if (meldCards.Length < 3)
        {
            throw new GameRuleViolationException("A new meld must contain at least three cards.");
        }

        var naturalCards = meldCards.Where(card => !card.IsWild).ToArray();

        if (naturalCards.Length < 2)
        {
            throw new GameRuleViolationException("A meld must contain at least two natural cards.");
        }

        var naturalRanks = naturalCards.Select(card => card.Rank).Distinct().ToArray();

        if (naturalRanks.Length != 1)
        {
            throw new GameRuleViolationException("All natural cards in a meld must share the same rank.");
        }

        var wildCardCount = meldCards.Length - naturalCards.Length;

        if (wildCardCount > 3)
        {
            throw new GameRuleViolationException("A meld cannot contain more than three wild cards.");
        }

        if (wildCardCount >= naturalCards.Length)
        {
            throw new GameRuleViolationException("A meld cannot contain as many or more wild cards than natural cards.");
        }

        Cards = Array.AsReadOnly(meldCards);
        Rank = naturalRanks[0];
        NaturalCardCount = naturalCards.Length;
        WildCardCount = wildCardCount;
    }

    public IReadOnlyList<Card> Cards { get; }

    public CardRank Rank { get; }

    public int NaturalCardCount { get; }

    public int WildCardCount { get; }

    public bool HasWildCards => WildCardCount > 0;

    public bool IsCanasta => Cards.Count >= 7;

    public CanastaKind? CanastaKind
    {
        get
        {
            if (!IsCanasta)
            {
                return null;
            }

            return HasWildCards ? CanastaNET.Engine.CanastaKind.Mixed : CanastaNET.Engine.CanastaKind.Natural;
        }
    }

    public int CardPointTotal => Cards.Sum(card => card.PointValue);
}

public sealed class DeckState : IReadOnlyList<Card>
{
    private readonly ReadOnlyCollection<Card> cards;

    public DeckState(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        this.cards = Array.AsReadOnly(cards.ToArray());
    }

    public int Count => cards.Count;

    public Card this[int index] => cards[index];

    public bool IsEmpty => Count == 0;

    public IEnumerator<Card> GetEnumerator() => cards.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class DiscardPileState : IReadOnlyList<Card>
{
    private readonly ReadOnlyCollection<Card> cards;

    public DiscardPileState(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        this.cards = Array.AsReadOnly(cards.ToArray());
    }

    public int Count => cards.Count;

    public Card this[int index] => cards[index];

    public bool IsEmpty => cards.Count == 0;

    public Card TopCard => IsEmpty
        ? throw new InvalidOperationException("Cannot access TopCard because the discard pile is empty. Check IsEmpty before accessing TopCard.")
        : cards[^1];

    public bool IsFrozen => cards.Any(card => card.IsWild);

    public IEnumerator<Card> GetEnumerator() => cards.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class HandState : IReadOnlyList<Card>
{
    private readonly ReadOnlyCollection<Card> cards;

    public HandState(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        this.cards = Array.AsReadOnly(cards.ToArray());
    }

    public int Count => cards.Count;

    public Card this[int index] => cards[index];

    public bool ContainsInstance(int instanceId) => cards.Any(card => card.InstanceId == instanceId);

    public IEnumerator<Card> GetEnumerator() => cards.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class RoundSummaryData
{
    public RoundSummaryData(
        RoundEndReason endReason,
        string message,
        int completedTurnCount,
        IEnumerable<TeamRoundResultData> teamResults,
        int? endedByPlayerIndex)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Round summary message must be non-empty.", nameof(message));
        }

        if (completedTurnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedTurnCount));
        }

        var resolvedTeamResults = (teamResults ?? throw new ArgumentNullException(nameof(teamResults))).ToArray();

        if (resolvedTeamResults.Length == 0)
        {
            throw new ArgumentException("Round summaries must include at least one team result.", nameof(teamResults));
        }

        EndReason = endReason;
        Message = message;
        CompletedTurnCount = completedTurnCount;
        TeamResults = Array.AsReadOnly(resolvedTeamResults);
        EndedByPlayerIndex = endedByPlayerIndex;
    }

    public RoundEndReason EndReason { get; }

    public string Message { get; }

    public int CompletedTurnCount { get; }

    public IReadOnlyList<TeamRoundResultData> TeamResults { get; }

    public int? EndedByPlayerIndex { get; }
}

public sealed class TeamRoundResultData
{
    public TeamRoundResultData(
        int teamIndex,
        int startingScore,
        int initialMeldRequirement,
        int meldPoints,
        int naturalCanastaCount,
        int mixedCanastaCount,
        int canastaBonus,
        int goingOutBonus,
        int concealedHandBonus,
        int handPenalty,
        int roundScore,
        bool wentOut)
    {
        TeamIndex = teamIndex;
        StartingScore = startingScore;
        InitialMeldRequirement = initialMeldRequirement;
        MeldPoints = meldPoints;
        NaturalCanastaCount = naturalCanastaCount;
        MixedCanastaCount = mixedCanastaCount;
        CanastaBonus = canastaBonus;
        GoingOutBonus = goingOutBonus;
        ConcealedHandBonus = concealedHandBonus;
        HandPenalty = handPenalty;
        RoundScore = roundScore;
        WentOut = wentOut;
    }

    public int TeamIndex { get; }

    public int StartingScore { get; }

    public int InitialMeldRequirement { get; }

    public int MeldPoints { get; }

    public int NaturalCanastaCount { get; }

    public int MixedCanastaCount { get; }

    public int CanastaBonus { get; }

    public int GoingOutBonus { get; }

    public int ConcealedHandBonus { get; }

    public int HandPenalty { get; }

    public int RoundScore { get; }

    public bool WentOut { get; }
}

public sealed class GameRuleViolationException : InvalidOperationException
{
    public GameRuleViolationException(string message)
        : base(message)
    {
    }
}

public readonly record struct Card
{
    public static readonly CardRank[] NonJokerRanks =
    [
        CardRank.Ace,
        CardRank.Two,
        CardRank.Three,
        CardRank.Four,
        CardRank.Five,
        CardRank.Six,
        CardRank.Seven,
        CardRank.Eight,
        CardRank.Nine,
        CardRank.Ten,
        CardRank.Jack,
        CardRank.Queen,
        CardRank.King
    ];

    public Card(int instanceId, CardRank rank, CardSuit? suit, int deckNumber)
    {
        if (instanceId < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceId), "Card instances must be positive.");
        }

        if (deckNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(deckNumber), "Deck number must be positive.");
        }

        if (rank == CardRank.Joker && suit is not null)
        {
            throw new ArgumentException("Jokers cannot have a suit.", nameof(suit));
        }

        if (rank != CardRank.Joker && suit is null)
        {
            throw new ArgumentException("Non-jokers must have a suit.", nameof(suit));
        }

        InstanceId = instanceId;
        Rank = rank;
        Suit = suit;
        DeckNumber = deckNumber;
    }

    public int InstanceId { get; }

    public CardRank Rank { get; }

    public CardSuit? Suit { get; }

    public int DeckNumber { get; }

    public bool IsJoker => Rank == CardRank.Joker;

    public bool IsWild => IsJoker || Rank == CardRank.Two;

    public int PointValue => Rank switch
    {
        CardRank.Joker => 50,
        CardRank.Two or CardRank.Ace => 20,
        CardRank.Eight or CardRank.Nine or CardRank.Ten or CardRank.Jack or CardRank.Queen or CardRank.King => 10,
        _ => 5
    };

    public static Card CreateJoker(int instanceId, int deckNumber) => new(instanceId, CardRank.Joker, null, deckNumber);

    public override string ToString() => IsJoker ? $"Joker (Deck {DeckNumber})" : $"{Rank} of {Suit} (Deck {DeckNumber})";
}

public enum CardSuit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

public enum CardRank
{
    Ace = 1,
    Two = 2,
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    Joker = 14
}

public enum TurnPhase
{
    AwaitingDraw,
    AwaitingDiscard,
    Completed
}

public enum RoundEndReason
{
    StockExhausted,
    PlayerWentOut
}

public enum RoundStartPlayerRule
{
    NextPlayerAfterDealer,
    DealerStartsRound
}

public enum CanastaKind
{
    Natural,
    Mixed
}
