using System.Text;
using CanastaNET.Engine;

namespace CanastaNET.Cli;

public static class CliApplication
{
    public static string CreateWelcomeText(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        return string.Join(
            Environment.NewLine,
            "Welcome to CanastaNET!",
            "The Milestone 4 CLI supports interactive match setup, turn actions, state inspection, and round progression.",
            "Use launch options like `--seed 42` or start a fresh session with `new-match --seed 42` for deterministic runs.",
            "Type `help` to see the available commands.",
            string.Empty,
            engine.CreateMockGameSummary());
    }

    public static int Run(string[] args, TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var session = CliSession.Start(args);

        output.WriteLine(CreateWelcomeText(session.Engine));
        output.WriteLine();
        output.WriteLine(session.FormatStartupStatus());

        while (true)
        {
            output.Write("> ");

            var commandLine = input.ReadLine();
            if (commandLine is null)
            {
                return 0;
            }

            var result = session.Execute(commandLine);
            if (!string.IsNullOrWhiteSpace(result.Output))
            {
                output.WriteLine(result.Output);
            }

            if (!result.ShouldContinue)
            {
                return 0;
            }
        }
    }
}

internal sealed class CliSession
{
    private static readonly string HelpText = string.Join(
        Environment.NewLine,
        "Commands:",
        "- help",
        "- status",
        "- hand [current|player-index|player-name]",
        "- teams",
        "- melds",
        "- discard-pile",
        "- scores",
        "- legal",
        "- draw stock",
        "- draw discard [matching-card-id ...]",
        "- meld <card-id ...> [--rank <rank>]",
        "- discard <card-id>",
        "- next-round [--seed <number>]",
        "- new-match [--seed <number>] [--winning-score <number>] [--players <comma-separated names>] [--team-count <number>] [--dealer <player-index>] [--cards-per-player <number>] [--deck-count <number>]",
        "- exit",
        string.Empty,
        "Examples:",
        "- draw stock",
        "- meld 12 18 27",
        "- draw discard 5 9",
        "- new-match --seed 42 --winning-score 3000",
        "- hand East");

    private GameMatchState matchState;

    private CliSession(GameEngine engine, GameMatchState matchState)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
    }

    public GameEngine Engine { get; }

    public static CliSession Start(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var engine = new GameEngine();
        var options = CliOptions.Parse(args);

        return new CliSession(engine, StartMatch(engine, options));
    }

    public string FormatStartupStatus()
    {
        var builder = new StringBuilder();
        builder.AppendLine("A new match is ready.");
        builder.AppendLine(FormatMatchConfiguration());
        builder.Append(FormatStatus());
        return builder.ToString();
    }

    public CliCommandResult Execute(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new CliCommandResult(true, "Enter a command or type `help`.");
        }

        var tokens = Tokenize(commandLine);
        if (tokens.Count == 0)
        {
            return new CliCommandResult(true, "Enter a command or type `help`.");
        }

        return tokens[0].ToLowerInvariant() switch
        {
            "help" => new CliCommandResult(true, HelpText),
            "status" => new CliCommandResult(true, FormatStatus()),
            "hand" => new CliCommandResult(true, FormatHand(tokens.Count > 1 ? string.Join(' ', tokens.Skip(1)) : null)),
            "teams" => new CliCommandResult(true, FormatTeams()),
            "melds" => new CliCommandResult(true, FormatMelds()),
            "discard-pile" => new CliCommandResult(true, FormatDiscardPile()),
            "scores" => new CliCommandResult(true, FormatScores()),
            "legal" => new CliCommandResult(true, FormatLegalCommands()),
            "show" => ExecuteShowCommand(tokens),
            "draw" => ExecuteDrawCommand(tokens),
            "meld" => ExecuteMeldCommand(tokens),
            "discard" => ExecuteDiscardCommand(tokens),
            "next-round" => ExecuteNextRoundCommand(tokens),
            "new-match" => ExecuteNewMatchCommand(tokens),
            "exit" or "quit" => new CliCommandResult(false, "Exiting CanastaNET CLI."),
            _ => new CliCommandResult(true, $"Unknown command `{tokens[0]}`. Type `help` for usage.")
        };
    }

    private static GameMatchState StartMatch(GameEngine engine, CliOptions options)
    {
        var configuration = options.CreateConfiguration();
        return engine.StartMatch(configuration, options.Seed, options.WinningScore ?? 5000);
    }

    private CliCommandResult ExecuteShowCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return new CliCommandResult(true, "Specify what to show: status, hand, teams, melds, discard-pile, scores, or legal.");
        }

        return tokens[1].ToLowerInvariant() switch
        {
            "status" => new CliCommandResult(true, FormatStatus()),
            "hand" => new CliCommandResult(true, FormatHand(tokens.Count > 2 ? string.Join(' ', tokens.Skip(2)) : null)),
            "teams" => new CliCommandResult(true, FormatTeams()),
            "melds" => new CliCommandResult(true, FormatMelds()),
            "discard-pile" or "discard" => new CliCommandResult(true, FormatDiscardPile()),
            "scores" => new CliCommandResult(true, FormatScores()),
            "legal" => new CliCommandResult(true, FormatLegalCommands()),
            _ => new CliCommandResult(true, $"Unknown show target `{tokens[1]}`.")
        };
    }

    private CliCommandResult ExecuteDrawCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
        {
            return new CliCommandResult(true, "Usage: draw stock | draw discard [matching-card-id ...]");
        }

        return tokens[1].ToLowerInvariant() switch
        {
            "stock" => ApplyCommand(GameCommand.DrawFromStock()),
            "discard" => ApplyCommand(CreateDiscardPickupCommand(tokens.Skip(2).ToArray())),
            _ => new CliCommandResult(true, $"Unknown draw source `{tokens[1]}`. Use `stock` or `discard`.")
        };
    }

    private CliCommandResult ExecuteMeldCommand(IReadOnlyList<string> tokens)
    {
        try
        {
            var cardIds = new List<int>();
            CardRank? targetRank = null;

            for (var index = 1; index < tokens.Count; index++)
            {
                if (string.Equals(tokens[index], "--rank", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= tokens.Count)
                    {
                        return new CliCommandResult(true, "Usage: meld <card-id ...> [--rank <rank>]");
                    }

                    targetRank = ParseCardRank(tokens[index + 1]);
                    index++;
                    continue;
                }

                cardIds.Add(ParseInt(tokens[index], "card id"));
            }

            if (cardIds.Count == 0)
            {
                return new CliCommandResult(true, "Usage: meld <card-id ...> [--rank <rank>]");
            }

            return ApplyCommand(GameCommand.Meld(cardIds, targetRank));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return new CliCommandResult(true, $"Unable to parse meld command: {exception.Message}");
        }
    }

    private CliCommandResult ExecuteDiscardCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count != 2)
        {
            return new CliCommandResult(true, "Usage: discard <card-id>");
        }

        try
        {
            return ApplyCommand(GameCommand.Discard(ParseInt(tokens[1], "card id")));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return new CliCommandResult(true, $"Unable to parse discard command: {exception.Message}");
        }
    }

    private CliCommandResult ExecuteNextRoundCommand(IReadOnlyList<string> tokens)
    {
        try
        {
            var seed = tokens.Count == 1
                ? (int?)null
                : ParseSingleIntegerOption(tokens.Skip(1).ToArray(), "--seed");

            matchState = Engine.StartNextRound(matchState, seed);
            return new CliCommandResult(true, string.Join(Environment.NewLine, "Started the next round.", FormatMatchConfiguration(), FormatStatus()));
        }
        catch (Exception exception) when (exception is ArgumentException or GameRuleViolationException or FormatException or OverflowException)
        {
            return new CliCommandResult(true, $"Unable to start the next round: {exception.Message}");
        }
    }

    private CliCommandResult ExecuteNewMatchCommand(IReadOnlyList<string> tokens)
    {
        try
        {
            var options = CliOptions.Parse(tokens.Skip(1).ToArray());
            matchState = StartMatch(Engine, options);
            return new CliCommandResult(true, string.Join(Environment.NewLine, "Started a new match.", FormatMatchConfiguration(), FormatStatus()));
        }
        catch (Exception exception) when (exception is ArgumentException or GameRuleViolationException or FormatException or OverflowException)
        {
            return new CliCommandResult(true, $"Unable to start a new match: {exception.Message}");
        }
    }

    private GameCommand CreateDiscardPickupCommand(IReadOnlyList<string> cardIdTokens)
    {
        if (cardIdTokens.Count > 0)
        {
            return GameCommand.DrawFromDiscardPile(cardIdTokens.Select(token => ParseInt(token, "matching card id")));
        }

        var legalDiscardPickups = Engine.GetLegalCommands(matchState.CurrentRound).Commands
            .Where(command => command.Command.CommandType == GameCommandType.DrawFromDiscardPile)
            .Select(command => command.Command)
            .ToArray();

        return legalDiscardPickups.Length switch
        {
            1 => legalDiscardPickups[0],
            0 => throw new ArgumentException("Discard pickup is not currently a legal action."),
            _ => throw new ArgumentException("Multiple discard pickups are legal. Specify the matching card ids explicitly.")
        };
    }

    private CliCommandResult ApplyCommand(GameCommand command)
    {
        var result = Engine.Apply(matchState, command);
        if (!result.Succeeded)
        {
            return new CliCommandResult(true, string.Join(Environment.NewLine, $"Error: {result.ErrorMessage}", FormatLegalCommands(result.LegalCommands)));
        }

        matchState = result.MatchState;

        var builder = new StringBuilder();
        builder.AppendLine($"Applied `{FormatCommand(command)}`.");

        if (matchState.CurrentRound.IsCompleted)
        {
            builder.Append(FormatRoundCompletion());
        }
        else
        {
            builder.Append(FormatStatus());
        }

        return new CliCommandResult(true, builder.ToString());
    }

    private string FormatMatchConfiguration()
    {
        var configuration = matchState.CurrentRound.Configuration;
        var roundNumber = matchState.RoundHistory.Count + 1;
        var playerList = string.Join(", ", configuration.PlayerOrder);

        return string.Join(
            Environment.NewLine,
            $"Round {roundNumber} configuration:",
            $"- Players: {playerList}",
            $"- Teams: {configuration.TeamCount}",
            $"- Dealer: {configuration.PlayerOrder[configuration.DealerIndex]} (index {configuration.DealerIndex})",
            $"- Cards per player: {configuration.CardsPerPlayer}",
            $"- Deck count: {configuration.DeckCount}",
            $"- Winning score: {matchState.WinningScore}",
            $"- Shuffle seed: {(matchState.CurrentRound.Setup.ShuffleSeed is { } seed ? seed : "random")}");
    }

    private string FormatStatus()
    {
        var round = matchState.CurrentRound;

        if (round.IsCompleted)
        {
            return FormatRoundCompletion();
        }

        return string.Join(
            Environment.NewLine,
            "Current round status:",
            $"- Current player: {round.CurrentPlayer.Name} (index {round.CurrentPlayerIndex}, team {round.CurrentPlayer.TeamIndex})",
            $"- Turn phase: {round.TurnPhase}",
            $"- Hand size: {round.CurrentPlayer.Hand.Count}",
            $"- Stock count: {round.StockPile.Count}",
            $"- Discard top: {(round.DiscardPile.Count > 0 ? FormatCard(round.DiscardPile.TopCard) : "empty")}",
            $"- Completed turns: {round.CompletedTurnCount}",
            $"- Current turn meld points: {round.CurrentTurnMeldPoints}",
            $"- Team scores: {string.Join(", ", matchState.TeamScores.Select((score, index) => $"Team {index}={score}"))}",
            string.Empty,
            FormatLegalCommands());
    }

    private string FormatRoundCompletion()
    {
        var summary = matchState.CurrentRound.Summary!;
        var builder = new StringBuilder();
        builder.AppendLine("Round complete:");
        builder.AppendLine($"- End reason: {summary.EndReason}");
        builder.AppendLine($"- Message: {summary.Message}");
        builder.AppendLine($"- Completed turns: {summary.CompletedTurnCount}");

        foreach (var result in summary.TeamResults.OrderBy(result => result.TeamIndex))
        {
            builder.AppendLine($"- Team {result.TeamIndex}: round score {result.RoundScore}, total {matchState.TeamScores[result.TeamIndex]}, meld points {result.MeldPoints}, hand penalty {result.HandPenalty}");
        }

        if (matchState.IsCompleted)
        {
            builder.AppendLine($"Match complete. Winning teams: {string.Join(", ", matchState.WinningTeamIndexes)}");
        }
        else if (matchState.CanStartNextRound)
        {
            builder.AppendLine("Use `next-round` to continue the match.");
        }

        builder.AppendLine();
        builder.Append(FormatScores());
        return builder.ToString();
    }

    private string FormatHand(string? selector)
    {
        var player = ResolvePlayer(selector);
        var handLines = player.Hand.Count == 0
            ? ["(empty)"]
            : player.Hand.Select(card => $"- {FormatCard(card)}");

        return string.Join(
            Environment.NewLine,
            $"Hand for {player.Name} (player {player.PlayerIndex}, team {player.TeamIndex}):",
            string.Join(Environment.NewLine, handLines));
    }

    private string FormatTeams()
    {
        var lines = matchState.CurrentRound.Teams.Select(team =>
        {
            var members = string.Join(", ", team.PlayerIndexes.Select(playerIndex => matchState.CurrentRound.Players[playerIndex].Name));
            return $"- Team {team.TeamIndex}: {members} | starting score {team.StartingScore} | meld count {team.Melds.Count}";
        });

        return string.Join(Environment.NewLine, new[] { "Teams:" }.Concat(lines));
    }

    private string FormatMelds()
    {
        var lines = new List<string> { "Melds:" };

        foreach (var team in matchState.CurrentRound.Teams)
        {
            lines.Add($"- Team {team.TeamIndex}:");

            if (team.Melds.Count == 0)
            {
                lines.Add("  (none)");
                continue;
            }

            foreach (var meld in team.Melds)
            {
                var cards = string.Join(", ", meld.Cards.Select(FormatCard));
                lines.Add($"  - {meld.Rank}: {cards}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatDiscardPile()
    {
        var discardPile = matchState.CurrentRound.DiscardPile;
        var lines = new List<string>
        {
            $"Discard pile ({discardPile.Count} cards):",
            discardPile.Count == 0
                ? "(empty)"
                : string.Join(Environment.NewLine, discardPile.Select(card => $"- {FormatCard(card)}"))
        };

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatScores()
    {
        var lines = new List<string> { "Scores:" };

        for (var teamIndex = 0; teamIndex < matchState.TeamScores.Count; teamIndex++)
        {
            var team = matchState.CurrentRound.Teams[teamIndex];
            lines.Add($"- Team {teamIndex}: current total {matchState.TeamScores[teamIndex]}, round starting score {team.StartingScore}");
        }

        lines.Add($"- Winning score target: {matchState.WinningScore}");
        return string.Join(Environment.NewLine, lines);
    }

    private string FormatLegalCommands() => FormatLegalCommands(Engine.GetLegalCommands(matchState.CurrentRound));

    private static string FormatLegalCommands(RoundCommandCatalog catalog)
    {
        if (catalog.Commands.Count == 0)
        {
            return "Legal commands: (none)";
        }

        var lines = catalog.Commands.Select(command => $"- {FormatCommand(command.Command)}: {command.Description}");
        return string.Join(Environment.NewLine, new[] { $"Legal commands for {catalog.TurnPhase}:" }.Concat(lines));
    }

    private PlayerState ResolvePlayer(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector) || string.Equals(selector, "current", StringComparison.OrdinalIgnoreCase))
        {
            return matchState.CurrentRound.CurrentPlayer;
        }

        if (int.TryParse(selector, out var playerIndex))
        {
            return matchState.CurrentRound.Players.Single(player => player.PlayerIndex == playerIndex);
        }

        return matchState.CurrentRound.Players.Single(player => string.Equals(player.Name, selector, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> Tokenize(string commandLine) =>
        commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string FormatCard(Card card) => $"#{card.InstanceId} {card} [{card.PointValue} pts]";

    private static string FormatCommand(GameCommand command)
    {
        var baseCommand = command.CommandType switch
        {
            GameCommandType.DrawFromStock => "draw stock",
            GameCommandType.DrawFromDiscardPile => command.CardInstanceIds.Count == 0
                ? "draw discard"
                : $"draw discard {string.Join(' ', command.CardInstanceIds)}",
            GameCommandType.Meld => $"meld {string.Join(' ', command.CardInstanceIds)}",
            GameCommandType.Discard => $"discard {command.CardInstanceIds.Single()}",
            _ => command.CommandType.ToString()
        };

        return command.TargetRank is { } targetRank
            ? $"{baseCommand} --rank {targetRank}"
            : baseCommand;
    }

    private static int ParseSingleIntegerOption(IReadOnlyList<string> tokens, string optionName)
    {
        if (tokens.Count != 2 || !string.Equals(tokens[0], optionName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Usage: next-round {optionName} <number>");
        }

        return ParseInt(tokens[1], optionName);
    }

    private static int ParseInt(string value, string label)
    {
        if (!int.TryParse(value, out var parsed))
        {
            throw new FormatException($"Expected an integer for {label}, but received `{value}`.");
        }

        return parsed;
    }

    private static CardRank ParseCardRank(string value)
    {
        if (Enum.TryParse<CardRank>(value, ignoreCase: true, out var rank))
        {
            return rank;
        }

        throw new ArgumentException($"`{value}` is not a valid card rank.");
    }
}

internal sealed class CliCommandResult
{
    public CliCommandResult(bool shouldContinue, string output)
    {
        ShouldContinue = shouldContinue;
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public bool ShouldContinue { get; }

    public string Output { get; }
}

internal sealed class CliOptions
{
    private CliOptions(
        IReadOnlyList<string>? players,
        int? teamCount,
        int? dealerIndex,
        int? deckCount,
        int? cardsPerPlayer,
        int? winningScore,
        int? seed)
    {
        Players = players;
        TeamCount = teamCount;
        DealerIndex = dealerIndex;
        DeckCount = deckCount;
        CardsPerPlayer = cardsPerPlayer;
        WinningScore = winningScore;
        Seed = seed;
    }

    public IReadOnlyList<string>? Players { get; }

    public int? TeamCount { get; }

    public int? DealerIndex { get; }

    public int? DeckCount { get; }

    public int? CardsPerPlayer { get; }

    public int? WinningScore { get; }

    public int? Seed { get; }

    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        IReadOnlyList<string>? players = null;
        int? teamCount = null;
        int? dealerIndex = null;
        int? deckCount = null;
        int? cardsPerPlayer = null;
        int? winningScore = null;
        int? seed = null;

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument `{token}`. Options must use `--name value` syntax.");
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for option `{token}`.");
            }

            var value = args[++index];

            switch (token)
            {
                case "--players":
                    players = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    break;
                case "--team-count":
                    teamCount = ParseInt(value, token);
                    break;
                case "--dealer":
                    dealerIndex = ParseInt(value, token);
                    break;
                case "--deck-count":
                    deckCount = ParseInt(value, token);
                    break;
                case "--cards-per-player":
                    cardsPerPlayer = ParseInt(value, token);
                    break;
                case "--winning-score":
                    winningScore = ParseInt(value, token);
                    break;
                case "--seed":
                    seed = ParseInt(value, token);
                    break;
                default:
                    throw new ArgumentException($"Unknown option `{token}`.");
            }
        }

        return new CliOptions(players, teamCount, dealerIndex, deckCount, cardsPerPlayer, winningScore, seed);
    }

    public GameConfiguration CreateConfiguration()
    {
        var defaultConfiguration = GameConfiguration.CreateDefault();

        return new GameConfiguration(
            Players ?? defaultConfiguration.PlayerOrder,
            TeamCount ?? defaultConfiguration.TeamCount,
            DealerIndex ?? defaultConfiguration.DealerIndex,
            DeckCount ?? defaultConfiguration.DeckCount,
            CardsPerPlayer ?? defaultConfiguration.CardsPerPlayer,
            defaultConfiguration.JokersPerDeck,
            defaultConfiguration.HouseRules);
    }

    private static int ParseInt(string value, string label)
    {
        if (!int.TryParse(value, out var parsed))
        {
            throw new FormatException($"Expected an integer for {label}, but received `{value}`.");
        }

        return parsed;
    }
}
