using System.Collections.ObjectModel;

namespace CanastaNET.Engine;

public sealed class GameEngine
{
    private static readonly string[] ImplementedSubsystems =
    [
        "Card, deck, hand, team, and meld modeling",
        "Controlled round state snapshots",
        "Shuffle, deal, draw, discard, and turn progression",
        "Configurable player order, team count, and round setup"
    ];

    public string GetStartupMessage() => "CanastaNET engine foundation ready.";

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
                []))
            .ToArray();

        return new GameRoundState(
            roundConfiguration,
            players,
            teams,
            deck,
            discardPile,
            roundConfiguration.DealerIndex,
            nextSeatToDeal,
            TurnPhase.AwaitingDraw,
            0,
            null);
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
            return new GameRoundState(
                roundState.Configuration,
                roundState.Players,
                roundState.Teams,
                roundState.StockPile,
                roundState.DiscardPile,
                roundState.DealerIndex,
                roundState.CurrentPlayerIndex,
                TurnPhase.Completed,
                roundState.CompletedTurnCount,
                new RoundSummaryData(
                    RoundEndReason.StockExhausted,
                    $"{roundState.CurrentPlayer.Name} cannot draw because the stock pile is empty.",
                    roundState.CompletedTurnCount));
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
            players,
            roundState.Teams,
            stockPile,
            roundState.DiscardPile,
            roundState.DealerIndex,
            roundState.CurrentPlayerIndex,
            TurnPhase.AwaitingDiscard,
            roundState.CompletedTurnCount,
            null);
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
        var handIndex = updatedHand.FindIndex(currentCard => currentCard == card);

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

        return new GameRoundState(
            roundState.Configuration,
            players,
            roundState.Teams,
            roundState.StockPile,
            discardPile,
            roundState.DealerIndex,
            GetNextPlayerIndex(roundState.CurrentPlayerIndex, roundState.Players.Count),
            TurnPhase.AwaitingDraw,
            roundState.CompletedTurnCount + 1,
            null);
    }

    private static void EnsureRoundIsActive(GameRoundState roundState)
    {
        if (roundState.IsCompleted)
        {
            throw new GameRuleViolationException("The round is already complete.");
        }
    }

    private static int GetNextPlayerIndex(int playerIndex, int playerCount) => (playerIndex + 1) % playerCount;

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
        var random = seed is { } seededValue ? new Random(seededValue) : Random.Shared;

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
        int jokersPerDeck = 2)
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

        PlayerOrder = Array.AsReadOnly(orderedPlayers.Select(playerName => playerName!).ToArray());
        TeamCount = teamCount;
        DealerIndex = dealerIndex;
        DeckCount = deckCount;
        CardsPerPlayer = cardsPerPlayer;
        JokersPerDeck = jokersPerDeck;
    }

    public IReadOnlyList<string> PlayerOrder { get; }

    public int TeamCount { get; }

    public int DealerIndex { get; }

    public int DeckCount { get; }

    public int CardsPerPlayer { get; }

    public int JokersPerDeck { get; }

    public static GameConfiguration CreateDefault() => new(["North", "East", "South", "West"]);
}

public sealed class GameRoundState
{
    public GameRoundState(
        GameConfiguration configuration,
        IEnumerable<PlayerState> players,
        IEnumerable<TeamState> teams,
        IEnumerable<Card> stockPile,
        IEnumerable<Card> discardPile,
        int dealerIndex,
        int currentPlayerIndex,
        TurnPhase turnPhase,
        int completedTurnCount,
        RoundSummaryData? summary)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        var roundPlayers = (players ?? throw new ArgumentNullException(nameof(players))).ToArray();
        var roundTeams = (teams ?? throw new ArgumentNullException(nameof(teams))).ToArray();
        var stockCards = (stockPile ?? throw new ArgumentNullException(nameof(stockPile))).ToArray();
        var discardCards = (discardPile ?? throw new ArgumentNullException(nameof(discardPile))).ToArray();

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

        if (discardCards.Length == 0)
        {
            throw new ArgumentException("The discard pile must contain at least one up card.", nameof(discardPile));
        }

        if (completedTurnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedTurnCount));
        }

        if (turnPhase == TurnPhase.Completed && summary is null)
        {
            throw new ArgumentException("Completed rounds must include a summary.", nameof(summary));
        }

        Players = Array.AsReadOnly(roundPlayers);
        Teams = Array.AsReadOnly(roundTeams);
        StockPile = Array.AsReadOnly(stockCards);
        DiscardPile = Array.AsReadOnly(discardCards);
        DealerIndex = dealerIndex;
        CurrentPlayerIndex = currentPlayerIndex;
        TurnPhase = turnPhase;
        CompletedTurnCount = completedTurnCount;
        Summary = summary;
    }

    public GameConfiguration Configuration { get; }

    public IReadOnlyList<PlayerState> Players { get; }

    public IReadOnlyList<TeamState> Teams { get; }

    public IReadOnlyList<Card> StockPile { get; }

    public IReadOnlyList<Card> DiscardPile { get; }

    public int DealerIndex { get; }

    public int CurrentPlayerIndex { get; }

    public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];

    public TurnPhase TurnPhase { get; }

    public int CompletedTurnCount { get; }

    public RoundSummaryData? Summary { get; }

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
        Hand = Array.AsReadOnly(hand.ToArray());
    }

    public int PlayerIndex { get; }

    public string Name { get; }

    public int TeamIndex { get; }

    public IReadOnlyList<Card> Hand { get; }
}

public sealed class TeamState
{
    public TeamState(int teamIndex, IEnumerable<int> playerIndexes, IEnumerable<MeldState> melds)
    {
        ArgumentNullException.ThrowIfNull(playerIndexes);
        ArgumentNullException.ThrowIfNull(melds);

        TeamIndex = teamIndex;
        PlayerIndexes = Array.AsReadOnly(playerIndexes.ToArray());
        Melds = Array.AsReadOnly(melds.ToArray());
    }

    public int TeamIndex { get; }

    public IReadOnlyList<int> PlayerIndexes { get; }

    public IReadOnlyList<MeldState> Melds { get; }
}

public sealed class MeldState
{
    public MeldState(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        Cards = Array.AsReadOnly(cards.ToArray());
    }

    public IReadOnlyList<Card> Cards { get; }
}

public sealed class RoundSummaryData
{
    public RoundSummaryData(RoundEndReason endReason, string message, int completedTurnCount)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Round summary message must be non-empty.", nameof(message));
        }

        if (completedTurnCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedTurnCount));
        }

        EndReason = endReason;
        Message = message;
        CompletedTurnCount = completedTurnCount;
    }

    public RoundEndReason EndReason { get; }

    public string Message { get; }

    public int CompletedTurnCount { get; }
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
    StockExhausted
}
