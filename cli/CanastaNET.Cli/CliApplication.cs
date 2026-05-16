using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            "The CLI supports interactive match setup, turn actions, state inspection, shared-terminal workflows, and regression scripts.",
            "Use launch options like `--seed 42`, `--shared-terminal`, or `--script path/to/commands.txt` for deterministic or multiplayer-friendly runs.",
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

        if (session.StartupScriptPath is { Length: > 0 } startupScriptPath)
        {
            var startupScriptResult = session.ExecuteScriptFile(startupScriptPath);
            if (!string.IsNullOrWhiteSpace(startupScriptResult.Output))
            {
                output.WriteLine();
                output.WriteLine(startupScriptResult.Output);
            }

            if (!startupScriptResult.ShouldContinue)
            {
                return 0;
            }
        }

        while (true)
        {
            var prePromptMessage = session.GetPrePromptMessage();
            if (!string.IsNullOrWhiteSpace(prePromptMessage))
            {
                output.WriteLine();
                output.WriteLine(prePromptMessage);
            }

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
    // ANSI clear-screen sequence for terminals that support VT100-style escape codes.
    private const string ClearScreenSequence = "\u001b[2J\u001b[H";
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string HelpText = string.Join(
        Environment.NewLine,
        "Commands:",
        "- help",
        "- status",
        "- hand [current|player-index|player-name] [reveal]",
        "- teams",
        "- melds",
        "- discard-pile",
        "- scores",
        "- legal",
        "- dump match|round [path]",
        "- load match|round <path>",
        "- trace [on|off|status]",
        "- run-script <path>",
        "- draw stock",
        "- draw discard [matching-card-id ...]",
        "- meld <card-id ...> [--rank <rank>]",
        "- discard <card-id>",
        "- next-round [--seed <number>]",
        "- new-match [--seed <number>] [--winning-score <number>] [--players <comma-separated names>] [--team-count <number>] [--dealer <player-index>] [--cards-per-player <number>] [--deck-count <number>] [--shared-terminal] [--hidden-hands] [--clear-between-turns] [--trace]",
        "- exit",
        string.Empty,
        "Examples:",
        "- draw stock",
        "- meld 12 18 27",
        "- draw discard 5 9",
        "- hand reveal",
        "- dump match /tmp/canasta-match.json",
        "- load round /tmp/canasta-round.json",
        "- run-script /tmp/canasta-session.txt",
        "- new-match --seed 42 --winning-score 3000",
        "- new-match --seed 42 --shared-terminal --hidden-hands",
        "- hand East reveal");

    private GameMatchState matchState;
    private string? lastPrePromptKey;

    private CliSession(GameEngine engine, GameMatchState matchState, CliOptions options)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
        StartupScriptPath = options?.ScriptPath;
        SharedTerminalMode = options?.SharedTerminalMode ?? false;
        HideHands = options?.HideHands ?? false;
        ClearBetweenTurns = options?.ClearBetweenTurns ?? false;
        TraceCommands = options?.TraceCommands ?? false;
    }

    public GameEngine Engine { get; }

    public string? StartupScriptPath { get; }

    private bool SharedTerminalMode { get; set; }

    private bool HideHands { get; set; }

    private bool ClearBetweenTurns { get; set; }

    private bool TraceCommands { get; set; }

    public static CliSession Start(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var engine = new GameEngine();
        var options = CliOptions.Parse(args);

        return new CliSession(engine, StartMatch(engine, options), options);
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
            "hand" => new CliCommandResult(true, FormatHand(tokens.Skip(1).ToArray())),
            "teams" => new CliCommandResult(true, FormatTeams()),
            "melds" => new CliCommandResult(true, FormatMelds()),
            "discard-pile" => new CliCommandResult(true, FormatDiscardPile()),
            "scores" => new CliCommandResult(true, FormatScores()),
            "legal" => new CliCommandResult(true, FormatLegalCommands()),
            "dump" => ExecuteDumpCommand(tokens),
            "load" => ExecuteLoadCommand(tokens),
            "trace" => ExecuteTraceCommand(tokens),
            "run-script" or "script" => ExecuteRunScriptCommand(tokens),
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
            "hand" => new CliCommandResult(true, FormatHand(tokens.Skip(2).ToArray())),
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
            SharedTerminalMode = options.SharedTerminalMode;
            HideHands = options.HideHands;
            ClearBetweenTurns = options.ClearBetweenTurns;
            TraceCommands = options.TraceCommands;
            lastPrePromptKey = null;
            matchState = StartMatch(Engine, options);
            return new CliCommandResult(true, string.Join(Environment.NewLine, "Started a new match.", FormatMatchConfiguration(), FormatStatus()));
        }
        catch (Exception exception) when (exception is ArgumentException or GameRuleViolationException or FormatException or OverflowException)
        {
            return new CliCommandResult(true, $"Unable to start a new match: {exception.Message}");
        }
    }

    public CliCommandResult ExecuteScriptFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new CliCommandResult(true, "Usage: run-script <path>");
        }

        try
        {
            var commands = File.ReadAllLines(path)
                .Select((line, index) => new ScriptCommand(index + 1, line.Trim()))
                .Where(command => !string.IsNullOrWhiteSpace(command.CommandLine))
                .Where(command => !command.CommandLine.StartsWith('#'))
                .ToArray();

            var builder = new StringBuilder();
            builder.AppendLine($"Running script `{path}`.");

            if (commands.Length == 0)
            {
                builder.Append("No commands were found.");
                return new CliCommandResult(true, builder.ToString());
            }

            foreach (var command in commands)
            {
                builder.AppendLine($"[{command.LineNumber}] > {command.CommandLine}");
                var result = Execute(command.CommandLine);
                if (!string.IsNullOrWhiteSpace(result.Output))
                {
                    builder.AppendLine(result.Output);
                }

                if (!result.ShouldContinue)
                {
                    return new CliCommandResult(false, builder.ToString().TrimEnd());
                }
            }

            return new CliCommandResult(true, builder.ToString().TrimEnd());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CliCommandResult(true, $"Unable to run script: {exception.Message}");
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
        var traceBeforeUpdate = TraceCommands;
        var result = Engine.Apply(matchState, command);
        if (!result.Succeeded)
        {
            var output = string.Join(Environment.NewLine, $"Error: {result.ErrorMessage}", FormatLegalCommands(result.LegalCommands));
            if (traceBeforeUpdate)
            {
                output = string.Join(Environment.NewLine, output, string.Empty, FormatTraceSnapshot(command, matchState.CurrentRound, result.LegalCommands));
            }

            return new CliCommandResult(true, output);
        }

        matchState = result.MatchState;
        lastPrePromptKey = null;

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

        if (traceBeforeUpdate)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(FormatTraceSnapshot(command, matchState.CurrentRound, Engine.GetLegalCommands(matchState.CurrentRound)));
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

    public string? GetPrePromptMessage()
    {
        if (!SharedTerminalMode || matchState.CurrentRound.IsCompleted)
        {
            return null;
        }

        var key = CreatePrePromptKey();
        if (string.Equals(key, lastPrePromptKey, StringComparison.Ordinal))
        {
            return null;
        }

        lastPrePromptKey = key;
        var lines = new List<string>();
        if (ClearBetweenTurns)
        {
            lines.Add(ClearScreenSequence);
        }

        lines.Add($"Shared terminal: pass control to {matchState.CurrentRound.CurrentPlayer.Name} (player {matchState.CurrentRound.CurrentPlayerIndex}).");
        if (HideHands)
        {
            lines.Add("Use `hand reveal` after the handoff to display the current player's cards.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string CreatePrePromptKey() =>
        $"{matchState.RoundHistory.Count}:{matchState.CurrentRound.CurrentPlayerIndex}:{matchState.CurrentRound.TurnPhase}:{matchState.CurrentRound.CompletedTurnCount}";

    private string FormatHand(IReadOnlyList<string> arguments)
    {
        var reveal = false;
        var selectorTokens = new List<string>();

        foreach (var argument in arguments)
        {
            if (string.Equals(argument, "reveal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "--reveal", StringComparison.OrdinalIgnoreCase))
            {
                reveal = true;
                continue;
            }

            selectorTokens.Add(argument);
        }

        var selector = selectorTokens.Count == 0 ? null : string.Join(' ', selectorTokens);
        var player = ResolvePlayer(selector);
        if (HideHands && !reveal)
        {
            var selectorSuffix = selectorTokens.Count == 0 ? string.Empty : $" {string.Join(' ', selectorTokens)}";
            return $"Hand for {player.Name} is hidden. Re-run `hand{selectorSuffix} reveal` to display cards.";
        }

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
            $"Discard pile ({discardPile.Count} {(discardPile.Count == 1 ? "card" : "cards")}):",
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

    private CliCommandResult ExecuteDumpCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count is < 2 or > 3)
        {
            return new CliCommandResult(true, "Usage: dump match|round [path]");
        }

        try
        {
            var json = tokens[1].ToLowerInvariant() switch
            {
                "match" => JsonSerializer.Serialize(CliMatchSnapshotDocument.FromSnapshot(Engine.CreateSnapshot(matchState)), SnapshotJsonOptions),
                "round" => JsonSerializer.Serialize(CliRoundSnapshotDocument.FromSnapshot(Engine.CreateSnapshot(matchState.CurrentRound)), SnapshotJsonOptions),
                _ => throw new ArgumentException($"Unknown dump target `{tokens[1]}`. Use `match` or `round`.")
            };

            if (tokens.Count == 2)
            {
                return new CliCommandResult(true, json);
            }

            File.WriteAllText(tokens[2], json);
            return new CliCommandResult(true, $"Wrote {tokens[1].ToLowerInvariant()} snapshot to `{tokens[2]}`.");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new CliCommandResult(true, $"Unable to dump state: {exception.Message}");
        }
    }

    private CliCommandResult ExecuteLoadCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count != 3)
        {
            return new CliCommandResult(true, "Usage: load match|round <path>");
        }

        try
        {
            var json = File.ReadAllText(tokens[2]);
            switch (tokens[1].ToLowerInvariant())
            {
                case "match":
                    var matchSnapshot = JsonSerializer.Deserialize<CliMatchSnapshotDocument>(json, SnapshotJsonOptions)
                        ?? throw new JsonException("The match snapshot file did not contain a valid payload.");
                    matchState = Engine.LoadSnapshot(matchSnapshot.ToSnapshot());
                    break;
                case "round":
                    var roundSnapshot = JsonSerializer.Deserialize<CliRoundSnapshotDocument>(json, SnapshotJsonOptions)
                        ?? throw new JsonException("The round snapshot file did not contain a valid payload.");
                    var roundState = Engine.LoadSnapshot(roundSnapshot.ToSnapshot());
                    matchState = new GameMatchState(
                        roundState.Configuration,
                        matchState.WinningScore,
                        roundState.Configuration.TeamStartingScores,
                        [],
                        roundState);
                    break;
                default:
                    return new CliCommandResult(true, $"Unknown load target `{tokens[1]}`. Use `match` or `round`.");
            }

            lastPrePromptKey = null;
            return new CliCommandResult(true, string.Join(Environment.NewLine, $"Loaded {tokens[1].ToLowerInvariant()} snapshot from `{tokens[2]}`.", FormatMatchConfiguration(), FormatStatus()));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new CliCommandResult(true, $"Unable to load snapshot: {exception.Message}");
        }
    }

    private CliCommandResult ExecuteTraceCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 1 || string.Equals(tokens[1], "status", StringComparison.OrdinalIgnoreCase))
        {
            return new CliCommandResult(true, FormatTraceStatus());
        }

        return tokens[1].ToLowerInvariant() switch
        {
            "on" => SetTraceCommands(true),
            "off" => SetTraceCommands(false),
            _ => new CliCommandResult(true, "Usage: trace [on|off|status]")
        };
    }

    private CliCommandResult SetTraceCommands(bool enabled)
    {
        TraceCommands = enabled;
        return new CliCommandResult(true, string.Join(Environment.NewLine, $"Command tracing {(enabled ? "enabled" : "disabled")}.", FormatTraceStatus()));
    }

    private string FormatTraceStatus()
    {
        var catalog = Engine.GetLegalCommands(matchState.CurrentRound);
        return string.Join(
            Environment.NewLine,
            $"Command trace: {(TraceCommands ? "on" : "off")}",
            FormatTraceSnapshot(null, matchState.CurrentRound, catalog));
    }

    private static string FormatTraceSnapshot(GameCommand? command, GameRoundState roundState, RoundCommandCatalog catalog)
    {
        var lines = new List<string>
        {
            "Trace snapshot:",
            $"- Attempted command: {(command is null ? "(status only)" : FormatCommand(command))}",
            $"- Current player: {roundState.CurrentPlayer.Name} (index {roundState.CurrentPlayerIndex}, team {roundState.CurrentPlayer.TeamIndex})",
            $"- Turn phase: {roundState.TurnPhase}",
            $"- Completed turns: {roundState.CompletedTurnCount}",
            $"- Round completed: {roundState.IsCompleted}",
            $"- Legal command count: {catalog.Commands.Count}"
        };

        lines.Add(FormatLegalCommands(catalog));
        return string.Join(Environment.NewLine, lines);
    }

    private CliCommandResult ExecuteRunScriptCommand(IReadOnlyList<string> tokens)
    {
        if (tokens.Count != 2)
        {
            return new CliCommandResult(true, "Usage: run-script <path>");
        }

        return ExecuteScriptFile(tokens[1]);
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
        int? seed,
        string? scriptPath,
        bool sharedTerminalMode,
        bool hideHands,
        bool clearBetweenTurns,
        bool traceCommands)
    {
        Players = players;
        TeamCount = teamCount;
        DealerIndex = dealerIndex;
        DeckCount = deckCount;
        CardsPerPlayer = cardsPerPlayer;
        WinningScore = winningScore;
        Seed = seed;
        ScriptPath = scriptPath;
        SharedTerminalMode = sharedTerminalMode;
        HideHands = hideHands;
        ClearBetweenTurns = clearBetweenTurns;
        TraceCommands = traceCommands;
    }

    public IReadOnlyList<string>? Players { get; }

    public int? TeamCount { get; }

    public int? DealerIndex { get; }

    public int? DeckCount { get; }

    public int? CardsPerPlayer { get; }

    public int? WinningScore { get; }

    public int? Seed { get; }

    public string? ScriptPath { get; }

    public bool SharedTerminalMode { get; }

    public bool HideHands { get; }

    public bool ClearBetweenTurns { get; }

    public bool TraceCommands { get; }

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
        string? scriptPath = null;
        var sharedTerminalMode = false;
        var hideHands = false;
        var clearBetweenTurns = false;
        var traceCommands = false;

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument `{token}`. Options must use `--name value` syntax.");
            }

            if (token is "--shared-terminal" or "--hidden-hands" or "--clear-between-turns" or "--trace")
            {
                switch (token)
                {
                    case "--shared-terminal":
                        sharedTerminalMode = true;
                        break;
                    case "--hidden-hands":
                        hideHands = true;
                        break;
                    case "--clear-between-turns":
                        clearBetweenTurns = true;
                        break;
                    case "--trace":
                        traceCommands = true;
                        break;
                }

                continue;
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
                case "--script":
                    scriptPath = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown option `{token}`.");
            }
        }

        return new CliOptions(players, teamCount, dealerIndex, deckCount, cardsPerPlayer, winningScore, seed, scriptPath, sharedTerminalMode, hideHands, clearBetweenTurns, traceCommands);
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

internal sealed record ScriptCommand(int LineNumber, string CommandLine);

internal sealed record CliMatchSnapshotDocument(
    GameConfigurationDocument Configuration,
    int WinningScore,
    int[] TeamScores,
    MatchRoundRecordDocument[] RoundHistory,
    CliRoundSnapshotDocument CurrentRound,
    int[] WinningTeamIndexes)
{
    public static CliMatchSnapshotDocument FromSnapshot(GameMatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new CliMatchSnapshotDocument(
            GameConfigurationDocument.FromConfiguration(snapshot.Configuration),
            snapshot.WinningScore,
            snapshot.TeamScores.ToArray(),
            snapshot.RoundHistory.Select(MatchRoundRecordDocument.FromRecord).ToArray(),
            CliRoundSnapshotDocument.FromSnapshot(snapshot.CurrentRound),
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

internal sealed record CliRoundSnapshotDocument(
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
    public static CliRoundSnapshotDocument FromSnapshot(GameRoundSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new CliRoundSnapshotDocument(
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
