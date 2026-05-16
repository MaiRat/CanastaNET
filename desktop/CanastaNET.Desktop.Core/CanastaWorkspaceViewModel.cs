using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CanastaNET.Engine;

namespace CanastaNET.Desktop.Core;

public sealed class CanastaWorkspaceViewModel : ObservableObject
{
    private static readonly SetupPresetViewModel[] BuiltInSetupPresets =
    [
        new(
            "Classic 4-player table",
            "Standard four-player setup for a full table with deterministic seeds for quick regression play.",
            "North, East, South, West",
            TeamCount: 2,
            DealerIndex: 0,
            DeckCount: 2,
            CardsPerPlayer: 11,
            WinningScore: 5000,
            SeedText: "42",
            NextRoundSeedText: "43"),
        new(
            "Quick duo practice",
            "Two-player practice match with a lower winning score for faster onboarding loops.",
            "North, South",
            TeamCount: 2,
            DealerIndex: 0,
            DeckCount: 2,
            CardsPerPlayer: 11,
            WinningScore: 1500,
            SeedText: "7",
            NextRoundSeedText: "8"),
        new(
            "Long match rematch",
            "Keep the default table but shorten the path to repeatable rematches by pre-populating the next-round seed.",
            "North, East, South, West",
            TeamCount: 2,
            DealerIndex: 1,
            DeckCount: 2,
            CardsPerPlayer: 11,
            WinningScore: 3000,
            SeedText: "99",
            NextRoundSeedText: "100")
    ];

    private static readonly string[] RulesHelpChecklist =
    [
        "Each turn starts with a draw. Use the legal action prompt if you are unsure whether stock or discard is available.",
        "Teams must satisfy their current opening meld requirement before laying down their first meld.",
        "Wild cards can extend melds, but natural cards are still required to anchor them.",
        "Ending a turn requires discarding exactly one card unless the round is already complete.",
        "Use save/load snapshots to pause a match, reproduce bugs, or replay edge-case rounds."
    ];

    private readonly GameEngine engine = new();
    private readonly DelegateCommand startMatchCommand;
    private readonly DelegateCommand applySetupPresetCommand;
    private readonly DelegateCommand drawStockCommand;
    private readonly DelegateCommand drawDiscardCommand;
    private readonly DelegateCommand meldCommand;
    private readonly DelegateCommand discardCommand;
    private readonly DelegateCommand endTurnCommand;
    private readonly DelegateCommand nextRoundCommand;
    private readonly DelegateCommand saveMatchCommand;
    private readonly DelegateCommand loadMatchCommand;
    private readonly DelegateCommand loadRecentMatchCommand;

    private GameMatchState matchState;
    private string? feedbackMessage;
    private string? nextActionPrompt;
    private string? selectedMeldRankName;
    private string? selectedSetupPresetName;
    private string matchFilePath = Path.Combine(Path.GetTempPath(), "canasta-match.json");
    private string? selectedRecentMatchPath;

    public CanastaWorkspaceViewModel()
    {
        Setup = MatchSetupViewModel.CreateDefault();
        SetupPresets = Array.AsReadOnly(BuiltInSetupPresets);
        RulesHelpLines = Array.AsReadOnly(RulesHelpChecklist);
        AvailableMeldRanks = Array.AsReadOnly(Card.NonJokerRanks.Select(rank => rank.ToString()).ToArray());
        matchState = engine.StartMatch();

        startMatchCommand = new DelegateCommand(StartMatch);
        applySetupPresetCommand = new DelegateCommand(ApplySelectedSetupPreset, CanApplySelectedSetupPreset);
        drawStockCommand = new DelegateCommand(() => ApplyRoundCommand(GameCommand.DrawFromStock(), "Drew the top stock card."), CanInteractWithCurrentRound);
        drawDiscardCommand = new DelegateCommand(DrawFromDiscardPile, CanInteractWithCurrentRound);
        meldCommand = new DelegateCommand(MeldSelectedCards, CanMeldSelectedCards);
        discardCommand = new DelegateCommand(() => DiscardSelectedCard("Discarded the selected card."), CanDiscardSelectedCard);
        endTurnCommand = new DelegateCommand(() => DiscardSelectedCard("Ended the turn by discarding the selected card."), CanDiscardSelectedCard);
        nextRoundCommand = new DelegateCommand(StartNextRound, () => matchState.CanStartNextRound);
        saveMatchCommand = new DelegateCommand(SaveMatchSnapshot, CanUseMatchFilePath);
        loadMatchCommand = new DelegateCommand(LoadMatchSnapshot, CanUseMatchFilePath);
        loadRecentMatchCommand = new DelegateCommand(LoadSelectedRecentMatch, CanLoadSelectedRecentMatch);
        SelectedSetupPresetName = SetupPresets[0].Name;
        RefreshPresentation("Desktop workspace ready.");
    }

    public MatchSetupViewModel Setup { get; }

    public IReadOnlyList<SetupPresetViewModel> SetupPresets { get; }

    public IReadOnlyList<string> RulesHelpLines { get; }

    public IReadOnlyList<string> AvailableMeldRanks { get; }

    public string? SelectedSetupPresetName
    {
        get => selectedSetupPresetName;
        set
        {
            if (SetProperty(ref selectedSetupPresetName, string.IsNullOrWhiteSpace(value) ? null : value))
            {
                OnPropertyChanged(nameof(SelectedSetupPresetDescription));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string SelectedSetupPresetDescription =>
        SetupPresets.FirstOrDefault(preset => string.Equals(preset.Name, SelectedSetupPresetName, StringComparison.Ordinal))?.Description
        ?? "Choose a preset to prefill the setup panel with a ready-to-play configuration.";

    public string? SelectedMeldRankName
    {
        get => selectedMeldRankName;
        set => SetProperty(ref selectedMeldRankName, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public string? FeedbackMessage
    {
        get => feedbackMessage;
        private set => SetProperty(ref feedbackMessage, value);
    }

    public string? NextActionPrompt
    {
        get => nextActionPrompt;
        private set => SetProperty(ref nextActionPrompt, value);
    }

    public string MatchFilePath
    {
        get => matchFilePath;
        set
        {
            if (SetProperty(ref matchFilePath, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string? SelectedRecentMatchPath
    {
        get => selectedRecentMatchPath;
        set
        {
            if (SetProperty(ref selectedRecentMatchPath, string.IsNullOrWhiteSpace(value) ? null : value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public TableOverviewViewModel TableOverview { get; private set; } = TableOverviewViewModel.Empty;

    public IReadOnlyList<PlayerHandViewModel> PlayerHands { get; private set; } = [];

    public IReadOnlyList<TeamMeldsViewModel> TeamMelds { get; private set; } = [];

    public DiscardPileViewModel DiscardPile { get; private set; } = DiscardPileViewModel.Empty;

    public ScoreSummaryViewModel ScoreSummary { get; private set; } = ScoreSummaryViewModel.Empty;

    public IReadOnlyList<LegalCommandViewModel> LegalCommands { get; private set; } = [];

    public IReadOnlyList<RecentMatchEntryViewModel> RecentMatches { get; private set; } = [];

    public ICommand StartMatchCommand => startMatchCommand;

    public ICommand ApplySetupPresetCommand => applySetupPresetCommand;

    public ICommand DrawStockCommand => drawStockCommand;

    public ICommand DrawDiscardCommand => drawDiscardCommand;

    public ICommand MeldCommand => meldCommand;

    public ICommand DiscardCommand => discardCommand;

    public ICommand EndTurnCommand => endTurnCommand;

    public ICommand NextRoundCommand => nextRoundCommand;

    public ICommand SaveMatchCommand => saveMatchCommand;

    public ICommand LoadMatchCommand => loadMatchCommand;

    public ICommand LoadRecentMatchCommand => loadRecentMatchCommand;

    private bool CanInteractWithCurrentRound() => !TableOverview.IsRoundComplete;

    private bool CanApplySelectedSetupPreset() => !string.IsNullOrWhiteSpace(SelectedSetupPresetName);

    private bool CanMeldSelectedCards() => !TableOverview.IsRoundComplete && GetSelectedCurrentPlayerCardIds().Count > 0;

    private bool CanDiscardSelectedCard() => !TableOverview.IsRoundComplete && GetSelectedCurrentPlayerCardIds().Count == 1;

    private bool CanUseMatchFilePath() => !string.IsNullOrWhiteSpace(MatchFilePath);

    private bool CanLoadSelectedRecentMatch() => !string.IsNullOrWhiteSpace(SelectedRecentMatchPath);

    private void StartMatch()
    {
        try
        {
            var configuration = Setup.CreateConfiguration();
            matchState = engine.StartMatch(configuration, Setup.ParseSeed(), Setup.WinningScore);
            RefreshPresentation("Started a new desktop match.");
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            FeedbackMessage = exception.Message;
            RaiseCommandCanExecuteChanged();
        }
    }

    private void ApplySelectedSetupPreset()
    {
        var preset = SetupPresets.FirstOrDefault(candidate => string.Equals(candidate.Name, SelectedSetupPresetName, StringComparison.Ordinal));
        if (preset is null)
        {
            FeedbackMessage = "Choose a setup preset before applying it.";
            RaiseCommandCanExecuteChanged();
            return;
        }

        preset.Apply(Setup);
        MatchFilePath = Path.Combine(Path.GetTempPath(), preset.DefaultSnapshotFileName);
        FeedbackMessage = $"Applied the `{preset.Name}` preset.";
        RaiseCommandCanExecuteChanged();
    }

    private void DrawFromDiscardPile()
    {
        var selectedCardIds = GetSelectedCurrentPlayerCardIds();
        var command = selectedCardIds.Count == 0
            ? GameCommand.DrawFromDiscardPile()
            : GameCommand.DrawFromDiscardPile(selectedCardIds);

        ApplyRoundCommand(command, "Drew from the discard pile.");
    }

    private void MeldSelectedCards()
    {
        var selectedCardIds = GetSelectedCurrentPlayerCardIds();
        if (selectedCardIds.Count == 0)
        {
            FeedbackMessage = "Select one or more cards from the current player's hand to meld.";
            RaiseCommandCanExecuteChanged();
            return;
        }

        CardRank? targetRank = null;
        if (!string.IsNullOrWhiteSpace(SelectedMeldRankName))
        {
            targetRank = Enum.Parse<CardRank>(SelectedMeldRankName, ignoreCase: true);
        }

        ApplyRoundCommand(GameCommand.Meld(selectedCardIds, targetRank), "Applied the meld command.");
    }

    private void DiscardSelectedCard(string successMessage)
    {
        var selectedCardIds = GetSelectedCurrentPlayerCardIds();
        if (selectedCardIds.Count != 1)
        {
            FeedbackMessage = "Select exactly one card from the current player's hand to discard.";
            RaiseCommandCanExecuteChanged();
            return;
        }

        ApplyRoundCommand(GameCommand.Discard(selectedCardIds[0]), successMessage);
    }

    private void StartNextRound()
    {
        try
        {
            matchState = engine.StartNextRound(matchState, Setup.ParseNextRoundSeed());
            RefreshPresentation("Started the next round.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            FeedbackMessage = exception.Message;
            RaiseCommandCanExecuteChanged();
        }
    }

    private void SaveMatchSnapshot()
    {
        try
        {
            var savedPath = MatchSnapshotStorage.SaveMatch(engine, matchState, MatchFilePath);
            RememberRecentMatch(savedPath, "Saved snapshot");
            FeedbackMessage = $"Saved the current match snapshot to `{savedPath}`.";
            RaiseCommandCanExecuteChanged();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            FeedbackMessage = $"Unable to save the match snapshot: {exception.Message}";
            RaiseCommandCanExecuteChanged();
        }
    }

    private void LoadMatchSnapshot()
    {
        try
        {
            var (loadedState, loadedPath) = MatchSnapshotStorage.LoadMatch(engine, MatchFilePath);
            matchState = loadedState;
            RememberRecentMatch(loadedPath, "Loaded snapshot");
            RefreshPresentation($"Loaded a match snapshot from `{loadedPath}`.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            FeedbackMessage = $"Unable to load the match snapshot: {exception.Message}";
            RaiseCommandCanExecuteChanged();
        }
    }

    private void LoadSelectedRecentMatch()
    {
        if (string.IsNullOrWhiteSpace(SelectedRecentMatchPath))
        {
            FeedbackMessage = "Select a recent match entry before loading it.";
            RaiseCommandCanExecuteChanged();
            return;
        }

        MatchFilePath = SelectedRecentMatchPath;
        LoadMatchSnapshot();
    }

    private void ApplyRoundCommand(GameCommand command, string successMessage)
    {
        var result = engine.Apply(matchState, command);
        matchState = result.MatchState;

        var feedback = result.Succeeded
            ? successMessage
            : $"{result.ErrorMessage}{Environment.NewLine}{FormatLegalCommands(result.LegalCommands)}";

        RefreshPresentation(feedback, result.LegalCommands);
    }

    private void RefreshPresentation(string? message, RoundCommandCatalog? legalCommands = null)
    {
        var snapshot = engine.CreateSnapshot(matchState);
        var catalog = legalCommands ?? engine.GetLegalCommands(matchState.CurrentRound);
        var currentPlayerSelections = new Dictionary<int, bool>();

        FeedbackMessage = message;
        TableOverview = TableOverviewViewModel.Create(snapshot);
        OnPropertyChanged(nameof(TableOverview));

        PlayerHands = snapshot.CurrentRound.Players
            .Select(player => PlayerHandViewModel.Create(
                player,
                isCurrentPlayer: player.PlayerIndex == snapshot.CurrentRound.CurrentPlayerIndex,
                selectionChanged: RaiseCommandCanExecuteChanged,
                currentPlayerSelections))
            .ToArray();
        OnPropertyChanged(nameof(PlayerHands));

        TeamMelds = snapshot.CurrentRound.Teams
            .Select(team => TeamMeldsViewModel.Create(team, snapshot.CurrentRound.Players))
            .ToArray();
        OnPropertyChanged(nameof(TeamMelds));

        DiscardPile = DiscardPileViewModel.Create(snapshot.CurrentRound.DiscardPile);
        OnPropertyChanged(nameof(DiscardPile));

        ScoreSummary = ScoreSummaryViewModel.Create(snapshot);
        OnPropertyChanged(nameof(ScoreSummary));

        LegalCommands = catalog.Commands.Select(command => LegalCommandViewModel.Create(command)).ToArray();
        OnPropertyChanged(nameof(LegalCommands));
        NextActionPrompt = CreateNextActionPrompt(catalog);

        SelectedMeldRankName = null;
        RaiseCommandCanExecuteChanged();
    }

    private List<int> GetSelectedCurrentPlayerCardIds()
    {
        var currentHand = PlayerHands.SingleOrDefault(player => player.IsCurrentPlayer);
        return currentHand is null
            ? []
            : currentHand.Cards.Where(card => card.IsSelected).Select(card => card.InstanceId).ToList();
    }

    private void RaiseCommandCanExecuteChanged()
    {
        drawStockCommand.RaiseCanExecuteChanged();
        drawDiscardCommand.RaiseCanExecuteChanged();
        meldCommand.RaiseCanExecuteChanged();
        discardCommand.RaiseCanExecuteChanged();
        endTurnCommand.RaiseCanExecuteChanged();
        nextRoundCommand.RaiseCanExecuteChanged();
        applySetupPresetCommand.RaiseCanExecuteChanged();
        saveMatchCommand.RaiseCanExecuteChanged();
        loadMatchCommand.RaiseCanExecuteChanged();
        loadRecentMatchCommand.RaiseCanExecuteChanged();
    }

    private static string FormatLegalCommands(RoundCommandCatalog catalog)
    {
        if (catalog.Commands.Count == 0)
        {
            return "No legal commands are available.";
        }

        return string.Join(
            Environment.NewLine,
            new[] { $"Legal commands for {catalog.TurnPhase}:" }
                .Concat(catalog.Commands.Select(command => $"- {FormatCommandText(command.Command)}: {command.Description}")));
    }

    private void RememberRecentMatch(string path, string action)
    {
        var fullPath = Path.GetFullPath(path);
        var updated = new[] { RecentMatchEntryViewModel.Create(fullPath, action, TableOverview.RoundNumber, TableOverview.CurrentPlayerName, TableOverview.TurnPhase) }
            .Concat(RecentMatches.Where(entry => !string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToArray();

        RecentMatches = updated;
        OnPropertyChanged(nameof(RecentMatches));
        SelectedRecentMatchPath = fullPath;
    }

    private static string CreateNextActionPrompt(RoundCommandCatalog catalog)
    {
        if (catalog.Commands.Count == 0)
        {
            return "No legal next action is available from the current state.";
        }

        var suggestions = string.Join(", ", catalog.Commands.Take(3).Select(command => FormatCommandText(command.Command)));

        return catalog.TurnPhase switch
        {
            TurnPhase.AwaitingDraw => $"Legal next actions: {suggestions}. Start the turn by drawing before attempting a meld or discard.",
            TurnPhase.AwaitingDiscard => $"Legal next actions: {suggestions}. Finish the turn by discarding exactly one card when you are ready.",
            TurnPhase.Completed => "The round is complete. Review the summary and start the next round when the table is ready.",
            _ => $"Legal next actions: {suggestions}."
        };
    }

    internal static string FormatCommandText(GameCommand command) => command.CommandType switch
    {
        GameCommandType.DrawFromStock => "draw stock",
        GameCommandType.DrawFromDiscardPile when command.CardInstanceIds.Count == 0 => "draw discard",
        GameCommandType.DrawFromDiscardPile => $"draw discard {string.Join(' ', command.CardInstanceIds)}",
        GameCommandType.Meld when command.TargetRank is { } rank => $"meld {string.Join(' ', command.CardInstanceIds)} --rank {rank}",
        GameCommandType.Meld => $"meld {string.Join(' ', command.CardInstanceIds)}",
        GameCommandType.Discard => $"discard {command.CardInstanceIds.Single()}",
        _ => command.CommandType.ToString()
    };
}

public sealed class MatchSetupViewModel : ObservableObject
{
    private string playerNamesCsv = "North, East, South, West";
    private int teamCount = 2;
    private int dealerIndex;
    private int deckCount = 2;
    private int cardsPerPlayer = 11;
    private int winningScore = 5000;
    private string? seedText = "42";
    private string? nextRoundSeedText = "43";

    public string PlayerNamesCsv
    {
        get => playerNamesCsv;
        set => SetProperty(ref playerNamesCsv, value);
    }

    public int TeamCount
    {
        get => teamCount;
        set => SetProperty(ref teamCount, value);
    }

    public int DealerIndex
    {
        get => dealerIndex;
        set => SetProperty(ref dealerIndex, value);
    }

    public int DeckCount
    {
        get => deckCount;
        set => SetProperty(ref deckCount, value);
    }

    public int CardsPerPlayer
    {
        get => cardsPerPlayer;
        set => SetProperty(ref cardsPerPlayer, value);
    }

    public int WinningScore
    {
        get => winningScore;
        set => SetProperty(ref winningScore, value);
    }

    public string? SeedText
    {
        get => seedText;
        set => SetProperty(ref seedText, value);
    }

    public string? NextRoundSeedText
    {
        get => nextRoundSeedText;
        set => SetProperty(ref nextRoundSeedText, value);
    }

    public static MatchSetupViewModel CreateDefault() => new();

    public GameConfiguration CreateConfiguration()
    {
        var players = PlayerNamesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return new GameConfiguration(
            players,
            TeamCount,
            DealerIndex,
            DeckCount,
            CardsPerPlayer);
    }

    public int? ParseSeed() => ParseNullableInt(SeedText, nameof(SeedText));

    public int? ParseNextRoundSeed() => ParseNullableInt(NextRoundSeedText, nameof(NextRoundSeedText));

    private static int? ParseNullableInt(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException("Seed values must be integers when provided.", parameterName);
    }
}

public sealed class TableOverviewViewModel
{
    public static readonly TableOverviewViewModel Empty = new(
        roundNumber: 0,
        currentPlayerName: string.Empty,
        currentPlayerIndex: 0,
        currentPlayerTeamIndex: 0,
        turnPhase: string.Empty,
        completedTurns: 0,
        stockCount: 0,
        discardTopCard: "(empty)",
        currentTurnMeldPoints: 0,
        shuffleSeed: "random",
        isRoundComplete: false,
        roundMessage: null);

    private TableOverviewViewModel(
        int roundNumber,
        string currentPlayerName,
        int currentPlayerIndex,
        int currentPlayerTeamIndex,
        string turnPhase,
        int completedTurns,
        int stockCount,
        string discardTopCard,
        int currentTurnMeldPoints,
        string shuffleSeed,
        bool isRoundComplete,
        string? roundMessage)
    {
        RoundNumber = roundNumber;
        CurrentPlayerName = currentPlayerName;
        CurrentPlayerIndex = currentPlayerIndex;
        CurrentPlayerTeamIndex = currentPlayerTeamIndex;
        TurnPhase = turnPhase;
        CompletedTurns = completedTurns;
        StockCount = stockCount;
        DiscardTopCard = discardTopCard;
        CurrentTurnMeldPoints = currentTurnMeldPoints;
        ShuffleSeed = shuffleSeed;
        IsRoundComplete = isRoundComplete;
        RoundMessage = roundMessage;
    }

    public int RoundNumber { get; }

    public string CurrentPlayerName { get; }

    public int CurrentPlayerIndex { get; }

    public int CurrentPlayerTeamIndex { get; }

    public string TurnPhase { get; }

    public int CompletedTurns { get; }

    public int StockCount { get; }

    public string DiscardTopCard { get; }

    public int CurrentTurnMeldPoints { get; }

    public string ShuffleSeed { get; }

    public bool IsRoundComplete { get; }

    public string? RoundMessage { get; }

    public static TableOverviewViewModel Create(GameMatchSnapshot snapshot)
    {
        var round = snapshot.CurrentRound;
        var currentPlayer = round.Players[round.CurrentPlayerIndex];

        return new TableOverviewViewModel(
            snapshot.RoundHistory.Count + 1,
            currentPlayer.Name,
            currentPlayer.PlayerIndex,
            currentPlayer.TeamIndex,
            round.TurnPhase.ToString(),
            round.CompletedTurnCount,
            round.StockPile.Count,
            round.DiscardPile.Count == 0 ? "(empty)" : CardFormatter.Format(round.DiscardPile[^1]),
            round.CurrentTurnMeldPoints,
            round.Setup.ShuffleSeed?.ToString() ?? "random",
            round.TurnPhase == CanastaNET.Engine.TurnPhase.Completed,
            round.Summary?.Message);
    }
}

public sealed class PlayerHandViewModel
{
    private PlayerHandViewModel(int playerIndex, string name, int teamIndex, bool isCurrentPlayer, IReadOnlyList<CardItemViewModel> cards)
    {
        PlayerIndex = playerIndex;
        Name = name;
        TeamIndex = teamIndex;
        IsCurrentPlayer = isCurrentPlayer;
        Cards = cards;
    }

    public int PlayerIndex { get; }

    public string Name { get; }

    public int TeamIndex { get; }

    public bool IsCurrentPlayer { get; }

    public IReadOnlyList<CardItemViewModel> Cards { get; }

    public string Header => IsCurrentPlayer
        ? $"{Name} (current player, team {TeamIndex})"
        : $"{Name} (team {TeamIndex})";

    public static PlayerHandViewModel Create(
        PlayerSnapshot snapshot,
        bool isCurrentPlayer,
        Action selectionChanged,
        IReadOnlyDictionary<int, bool> selections)
    {
        var cards = snapshot.Hand
            .Select(card => new CardItemViewModel(card, isCurrentPlayer, selectionChanged)
            {
                IsSelected = selections.TryGetValue(card.InstanceId, out var selected) && selected
            })
            .ToArray();

        return new PlayerHandViewModel(snapshot.PlayerIndex, snapshot.Name, snapshot.TeamIndex, isCurrentPlayer, cards);
    }
}

public sealed class CardItemViewModel : ObservableObject
{
    private readonly Action selectionChanged;
    private bool isSelected;

    public CardItemViewModel(Card card, bool canSelect, Action selectionChanged)
    {
        InstanceId = card.InstanceId;
        DisplayText = CardFormatter.Format(card);
        PointValue = card.PointValue;
        IsWild = card.IsWild;
        CanSelect = canSelect;
        this.selectionChanged = selectionChanged;
    }

    public int InstanceId { get; }

    public string DisplayText { get; }

    public int PointValue { get; }

    public bool IsWild { get; }

    public bool CanSelect { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!CanSelect)
            {
                return;
            }

            if (SetProperty(ref isSelected, value))
            {
                selectionChanged();
            }
        }
    }
}

public sealed class TeamMeldsViewModel
{
    private TeamMeldsViewModel(int teamIndex, string members, int startingScore, IReadOnlyList<MeldViewModel> melds)
    {
        TeamIndex = teamIndex;
        Members = members;
        StartingScore = startingScore;
        Melds = melds;
    }

    public int TeamIndex { get; }

    public string Members { get; }

    public int StartingScore { get; }

    public IReadOnlyList<MeldViewModel> Melds { get; }

    public static TeamMeldsViewModel Create(TeamSnapshot snapshot, IReadOnlyList<PlayerSnapshot> players)
    {
        var members = string.Join(", ", snapshot.PlayerIndexes.Select(index => players.Single(player => player.PlayerIndex == index).Name));
        var melds = snapshot.Melds.Select(MeldViewModel.Create).ToArray();
        return new TeamMeldsViewModel(snapshot.TeamIndex, members, snapshot.StartingScore, melds);
    }
}

public sealed class MeldViewModel
{
    private MeldViewModel(string title, IReadOnlyList<string> cards)
    {
        Title = title;
        Cards = cards;
    }

    public string Title { get; }

    public IReadOnlyList<string> Cards { get; }

    public static MeldViewModel Create(MeldSnapshot snapshot)
    {
        var naturalCards = snapshot.Cards.Count(card => !card.IsWild);
        var wildCards = snapshot.Cards.Count - naturalCards;
        var rankLabel = snapshot.Cards.FirstOrDefault(card => !card.IsWild) is { InstanceId: > 0 } naturalCard
            ? naturalCard.Rank.ToString()
            : "Wild cards";
        var canastaLabel = snapshot.Cards.Count >= 7
            ? wildCards == 0 ? "Natural canasta" : "Mixed canasta"
            : "Meld";

        return new MeldViewModel(
            $"{rankLabel} - {canastaLabel} ({snapshot.Cards.Count} cards, {naturalCards} natural, {wildCards} wild)",
            snapshot.Cards.Select(CardFormatter.Format).ToArray());
    }
}

public sealed class DiscardPileViewModel
{
    public static readonly DiscardPileViewModel Empty = new(false, "(empty)", []);

    private DiscardPileViewModel(bool isFrozen, string topCard, IReadOnlyList<string> cards)
    {
        IsFrozen = isFrozen;
        TopCard = topCard;
        Cards = cards;
    }

    public bool IsFrozen { get; }

    public string TopCard { get; }

    public IReadOnlyList<string> Cards { get; }

    public static DiscardPileViewModel Create(IReadOnlyList<Card> discardPile) => new(
        discardPile.Any(card => card.IsWild),
        discardPile.Count == 0 ? "(empty)" : CardFormatter.Format(discardPile[^1]),
        discardPile.Reverse().Select(CardFormatter.Format).ToArray());
}

public sealed class ScoreSummaryViewModel
{
    public static readonly ScoreSummaryViewModel Empty = new(0, [], []);

    private ScoreSummaryViewModel(int winningScore, IReadOnlyList<TeamScoreViewModel> teamScores, IReadOnlyList<string> roundSummaryLines)
    {
        WinningScore = winningScore;
        TeamScores = teamScores;
        RoundSummaryLines = roundSummaryLines;
    }

    public int WinningScore { get; }

    public IReadOnlyList<TeamScoreViewModel> TeamScores { get; }

    public IReadOnlyList<string> RoundSummaryLines { get; }

    public static ScoreSummaryViewModel Create(GameMatchSnapshot snapshot)
    {
        var teamScores = snapshot.TeamScores
            .Select((score, index) => new TeamScoreViewModel(index, score, snapshot.CurrentRound.Teams[index].StartingScore))
            .ToArray();

        var summaryLines = snapshot.CurrentRound.Summary is null
            ? []
            : CreateSummaryLines(snapshot.CurrentRound.Summary, snapshot.TeamScores, snapshot.WinningTeamIndexes);

        return new ScoreSummaryViewModel(snapshot.WinningScore, teamScores, summaryLines);
    }

    private static string[] CreateSummaryLines(RoundSummaryData summary, IReadOnlyList<int> teamScores, IReadOnlyList<int> winningTeamIndexes)
    {
        var lines = new List<string>
        {
            $"End reason: {summary.EndReason}",
            $"Message: {summary.Message}",
            $"Completed turns: {summary.CompletedTurnCount}"
        };

        lines.AddRange(summary.TeamResults
            .OrderBy(result => result.TeamIndex)
            .Select(result =>
                $"Team {result.TeamIndex}: round {result.RoundScore}, total {teamScores[result.TeamIndex]}, meld points {result.MeldPoints}, hand penalty {result.HandPenalty}"));

        if (winningTeamIndexes.Count > 0)
        {
            lines.Add($"Winning teams: {string.Join(", ", winningTeamIndexes)}");
        }

        return lines.ToArray();
    }
}

public sealed record TeamScoreViewModel(int TeamIndex, int CurrentTotal, int RoundStartingScore)
{
    public string SummaryText => $"Team {TeamIndex}: current total {CurrentTotal}, round starting score {RoundStartingScore}";
}

public sealed record LegalCommandViewModel(string CommandText, string Description)
{
    public static LegalCommandViewModel Create(LegalGameCommand command) =>
        new(CanastaWorkspaceViewModel.FormatCommandText(command.Command), command.Description);
}

public sealed record SetupPresetViewModel(
    string Name,
    string Description,
    string PlayerNamesCsv,
    int TeamCount,
    int DealerIndex,
    int DeckCount,
    int CardsPerPlayer,
    int WinningScore,
    string? SeedText,
    string? NextRoundSeedText)
{
    public string DefaultSnapshotFileName => $"{Name.ToLowerInvariant().Replace(' ', '-').Replace("'", string.Empty)}.canasta.json";

    public void Apply(MatchSetupViewModel setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        setup.PlayerNamesCsv = PlayerNamesCsv;
        setup.TeamCount = TeamCount;
        setup.DealerIndex = DealerIndex;
        setup.DeckCount = DeckCount;
        setup.CardsPerPlayer = CardsPerPlayer;
        setup.WinningScore = WinningScore;
        setup.SeedText = SeedText;
        setup.NextRoundSeedText = NextRoundSeedText;
    }
}

public sealed record RecentMatchEntryViewModel(string Path, string Summary)
{
    public string DisplayText => $"{Summary} — {Path}";

    public static RecentMatchEntryViewModel Create(string path, string action, int roundNumber, string currentPlayerName, string turnPhase) =>
        new(path, $"{action}: round {roundNumber}, player {currentPlayerName}, phase {turnPhase}");
}

internal static class CardFormatter
{
    public static string Format(Card card) => $"#{card.InstanceId} {card} [{card.PointValue} pts]";
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class DelegateCommand : ICommand
{
    private readonly Action execute;
    private readonly Func<bool>? canExecute;

    public DelegateCommand(Action execute, Func<bool>? canExecute = null)
    {
        this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
