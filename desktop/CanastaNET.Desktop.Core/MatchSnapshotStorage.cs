using System.Text.Json;
using System.Text.Json.Serialization;
using CanastaNET.Engine;

namespace CanastaNET.Desktop.Core;

internal static class MatchSnapshotStorage
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SaveMatch(GameEngine engine, GameMatchState matchState, string path)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(matchState);

        var normalizedPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var snapshot = DesktopMatchSnapshotDocument.FromSnapshot(engine.CreateSnapshot(matchState));
        var json = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
        File.WriteAllText(normalizedPath, json);
        return normalizedPath;
    }

    public static (GameMatchState MatchState, string Path) LoadMatch(GameEngine engine, string path)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var normalizedPath = NormalizePath(path);
        var json = File.ReadAllText(normalizedPath);
        var snapshot = JsonSerializer.Deserialize<DesktopMatchSnapshotDocument>(json, SnapshotJsonOptions)
            ?? throw new JsonException("The match snapshot file did not contain a valid payload.");

        return (engine.LoadSnapshot(snapshot.ToSnapshot()), normalizedPath);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A snapshot path is required.", nameof(path));
        }

        return Path.GetFullPath(path);
    }
}

internal sealed record DesktopMatchSnapshotDocument(
    GameConfigurationDocument Configuration,
    int WinningScore,
    int[] TeamScores,
    MatchRoundRecordDocument[] RoundHistory,
    DesktopRoundSnapshotDocument CurrentRound,
    int[] WinningTeamIndexes)
{
    public static DesktopMatchSnapshotDocument FromSnapshot(GameMatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DesktopMatchSnapshotDocument(
            GameConfigurationDocument.FromConfiguration(snapshot.Configuration),
            snapshot.WinningScore,
            snapshot.TeamScores.ToArray(),
            snapshot.RoundHistory.Select(MatchRoundRecordDocument.FromRecord).ToArray(),
            DesktopRoundSnapshotDocument.FromSnapshot(snapshot.CurrentRound),
            snapshot.WinningTeamIndexes.ToArray());
    }

    public GameMatchSnapshot ToSnapshot() =>
        new(
            Configuration.ToConfiguration(),
            WinningScore,
            TeamScores,
            RoundHistory.Select(record => record.ToRecord()).ToArray(),
            CurrentRound.ToSnapshot(),
            WinningTeamIndexes);
}

internal sealed record DesktopRoundSnapshotDocument(
    GameConfigurationDocument Configuration,
    RoundSetupSnapshotDocument Setup,
    PlayerSnapshotDocument[] Players,
    TeamSnapshotDocument[] Teams,
    CardDocument[] StockPile,
    CardDocument[] DiscardPile,
    int DealerIndex,
    int CurrentPlayerIndex,
    TurnPhase TurnPhase,
    int CompletedTurnCount,
    RoundSummaryDataDocument? Summary,
    int CurrentTurnMeldPoints,
    bool CurrentTurnStartedWithOpenTeam)
{
    public static DesktopRoundSnapshotDocument FromSnapshot(GameRoundSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DesktopRoundSnapshotDocument(
            GameConfigurationDocument.FromConfiguration(snapshot.Configuration),
            RoundSetupSnapshotDocument.FromSnapshot(snapshot.Setup),
            snapshot.Players.Select(PlayerSnapshotDocument.FromSnapshot).ToArray(),
            snapshot.Teams.Select(TeamSnapshotDocument.FromSnapshot).ToArray(),
            snapshot.StockPile.Select(CardDocument.FromCard).ToArray(),
            snapshot.DiscardPile.Select(CardDocument.FromCard).ToArray(),
            snapshot.DealerIndex,
            snapshot.CurrentPlayerIndex,
            snapshot.TurnPhase,
            snapshot.CompletedTurnCount,
            snapshot.Summary is null ? null : RoundSummaryDataDocument.FromSummary(snapshot.Summary),
            snapshot.CurrentTurnMeldPoints,
            snapshot.CurrentTurnStartedWithOpenTeam);
    }

    public GameRoundSnapshot ToSnapshot() =>
        new(
            Configuration.ToConfiguration(),
            Setup.ToSnapshot(),
            Players.Select(player => player.ToSnapshot()).ToArray(),
            Teams.Select(team => team.ToSnapshot()).ToArray(),
            StockPile.Select(card => card.ToCard()).ToArray(),
            DiscardPile.Select(card => card.ToCard()).ToArray(),
            DealerIndex,
            CurrentPlayerIndex,
            TurnPhase,
            CompletedTurnCount,
            Summary?.ToSummary(),
            CurrentTurnMeldPoints,
            CurrentTurnStartedWithOpenTeam);
}

internal sealed record GameConfigurationDocument(
    string[] PlayerOrder,
    int TeamCount,
    int DealerIndex,
    int DeckCount,
    int CardsPerPlayer,
    int JokersPerDeck,
    HouseRuleOptionsDocument HouseRules,
    int[] TeamStartingScores)
{
    public static GameConfigurationDocument FromConfiguration(GameConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new GameConfigurationDocument(
            configuration.PlayerOrder.ToArray(),
            configuration.TeamCount,
            configuration.DealerIndex,
            configuration.DeckCount,
            configuration.CardsPerPlayer,
            configuration.JokersPerDeck,
            HouseRuleOptionsDocument.FromOptions(configuration.HouseRules),
            configuration.TeamStartingScores.ToArray());
    }

    public GameConfiguration ToConfiguration() =>
        new(
            PlayerOrder,
            TeamCount,
            DealerIndex,
            DeckCount,
            CardsPerPlayer,
            JokersPerDeck,
            HouseRules.ToOptions(),
            TeamStartingScores);
}

internal sealed record HouseRuleOptionsDocument(RoundStartPlayerRule RoundStartPlayerRule)
{
    public static HouseRuleOptionsDocument FromOptions(HouseRuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new HouseRuleOptionsDocument(options.RoundStartPlayerRule);
    }

    public HouseRuleOptions ToOptions() => new(RoundStartPlayerRule);
}

internal sealed record RoundSetupSnapshotDocument(
    int? ShuffleSeed,
    int FirstPlayerIndex,
    int DealerIndex,
    int CardsPerPlayer,
    CardDocument[] OpeningDiscardPile,
    int InitialStockCount)
{
    public static RoundSetupSnapshotDocument FromSnapshot(RoundSetupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new RoundSetupSnapshotDocument(
            snapshot.ShuffleSeed,
            snapshot.FirstPlayerIndex,
            snapshot.DealerIndex,
            snapshot.CardsPerPlayer,
            snapshot.OpeningDiscardPile.Select(CardDocument.FromCard).ToArray(),
            snapshot.InitialStockCount);
    }

    public RoundSetupSnapshot ToSnapshot() =>
        new(
            ShuffleSeed,
            FirstPlayerIndex,
            DealerIndex,
            CardsPerPlayer,
            OpeningDiscardPile.Select(card => card.ToCard()).ToArray(),
            InitialStockCount);
}

internal sealed record PlayerSnapshotDocument(int PlayerIndex, string Name, int TeamIndex, CardDocument[] Hand)
{
    public static PlayerSnapshotDocument FromSnapshot(PlayerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PlayerSnapshotDocument(snapshot.PlayerIndex, snapshot.Name, snapshot.TeamIndex, snapshot.Hand.Select(CardDocument.FromCard).ToArray());
    }

    public PlayerSnapshot ToSnapshot() => new(PlayerIndex, Name, TeamIndex, Hand.Select(card => card.ToCard()).ToArray());
}

internal sealed record MeldSnapshotDocument(CardDocument[] Cards)
{
    public static MeldSnapshotDocument FromSnapshot(MeldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MeldSnapshotDocument(snapshot.Cards.Select(CardDocument.FromCard).ToArray());
    }

    public MeldSnapshot ToSnapshot() => new(Cards.Select(card => card.ToCard()).ToArray());
}

internal sealed record TeamSnapshotDocument(int TeamIndex, int[] PlayerIndexes, MeldSnapshotDocument[] Melds, int StartingScore)
{
    public static TeamSnapshotDocument FromSnapshot(TeamSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new TeamSnapshotDocument(
            snapshot.TeamIndex,
            snapshot.PlayerIndexes.ToArray(),
            snapshot.Melds.Select(MeldSnapshotDocument.FromSnapshot).ToArray(),
            snapshot.StartingScore);
    }

    public TeamSnapshot ToSnapshot() =>
        new(
            TeamIndex,
            PlayerIndexes,
            Melds.Select(meld => meld.ToSnapshot()).ToArray(),
            StartingScore);
}

internal sealed record MatchRoundRecordDocument(
    int RoundNumber,
    int DealerIndex,
    int? ShuffleSeed,
    RoundSummaryDataDocument Summary,
    int[] TeamScoresAfterRound)
{
    public static MatchRoundRecordDocument FromRecord(MatchRoundRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new MatchRoundRecordDocument(
            record.RoundNumber,
            record.DealerIndex,
            record.ShuffleSeed,
            RoundSummaryDataDocument.FromSummary(record.Summary),
            record.TeamScoresAfterRound.ToArray());
    }

    public MatchRoundRecord ToRecord() =>
        new(
            RoundNumber,
            DealerIndex,
            ShuffleSeed,
            Summary.ToSummary(),
            TeamScoresAfterRound);
}

internal sealed record RoundSummaryDataDocument(
    RoundEndReason EndReason,
    string Message,
    int CompletedTurnCount,
    TeamRoundResultDataDocument[] TeamResults,
    int? EndedByPlayerIndex)
{
    public static RoundSummaryDataDocument FromSummary(RoundSummaryData summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new RoundSummaryDataDocument(
            summary.EndReason,
            summary.Message,
            summary.CompletedTurnCount,
            summary.TeamResults.Select(TeamRoundResultDataDocument.FromResult).ToArray(),
            summary.EndedByPlayerIndex);
    }

    public RoundSummaryData ToSummary() =>
        new(
            EndReason,
            Message,
            CompletedTurnCount,
            TeamResults.Select(result => result.ToResult()).ToArray(),
            EndedByPlayerIndex);
}

internal sealed record TeamRoundResultDataDocument(
    int TeamIndex,
    int StartingScore,
    int InitialMeldRequirement,
    int MeldPoints,
    int NaturalCanastaCount,
    int MixedCanastaCount,
    int CanastaBonus,
    int GoingOutBonus,
    int ConcealedHandBonus,
    int HandPenalty,
    int RoundScore,
    bool WentOut)
{
    public static TeamRoundResultDataDocument FromResult(TeamRoundResultData result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new TeamRoundResultDataDocument(
            result.TeamIndex,
            result.StartingScore,
            result.InitialMeldRequirement,
            result.MeldPoints,
            result.NaturalCanastaCount,
            result.MixedCanastaCount,
            result.CanastaBonus,
            result.GoingOutBonus,
            result.ConcealedHandBonus,
            result.HandPenalty,
            result.RoundScore,
            result.WentOut);
    }

    public TeamRoundResultData ToResult() =>
        new(
            TeamIndex,
            StartingScore,
            InitialMeldRequirement,
            MeldPoints,
            NaturalCanastaCount,
            MixedCanastaCount,
            CanastaBonus,
            GoingOutBonus,
            ConcealedHandBonus,
            HandPenalty,
            RoundScore,
            WentOut);
}

internal sealed record CardDocument(int InstanceId, CardRank Rank, CardSuit? Suit, int DeckNumber)
{
    public static CardDocument FromCard(Card card) => new(card.InstanceId, card.Rank, card.Suit, card.DeckNumber);

    public Card ToCard() => new(InstanceId, Rank, Suit, DeckNumber);
}
