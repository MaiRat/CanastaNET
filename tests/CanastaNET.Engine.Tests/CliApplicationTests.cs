using CanastaNET.Cli;
using CanastaNET.Engine;

namespace CanastaNET.Engine.Tests;

public class CliApplicationTests
{
    [Fact]
    public void Run_ShowsSeededSetupStatusAndInspectionCommands()
    {
        var output = RunCli(
            args: ["--seed", "42"],
            commands:
            [
                "status",
                "hand",
                "teams",
                "melds",
                "discard-pile",
                "scores",
                "legal",
                "exit"
            ]);

        Assert.Contains("A new match is ready.", output);
        Assert.Contains("Shuffle seed: 42", output);
        Assert.Contains("Current round status:", output);
        Assert.Contains("Hand for East", output);
        Assert.Contains("Teams:", output);
        Assert.Contains("Melds:", output);
        Assert.Contains("Discard pile (1 cards):", output);
        Assert.Contains("Scores:", output);
        Assert.Contains("Legal commands for AwaitingDraw:", output);
    }

    [Fact]
    public void Run_InvalidActionReturnsEngineValidationAndLegalCommands()
    {
        var engine = new GameEngine();
        var currentPlayerCardId = engine.StartRound(seed: 42).CurrentPlayer.Hand[0].InstanceId;

        var output = RunCli(
            args: ["--seed", "42"],
            commands:
            [
                $"discard {currentPlayerCardId}",
                "exit"
            ]);

        Assert.Contains("Error: East must draw before discarding.", output);
        Assert.Contains("- draw stock: Draw the top card from stock.", output);
    }

    [Fact]
    public void Run_SupportsTurnActionsAndRoundProgressionCommands()
    {
        var engine = new GameEngine();
        var configuration = new GameConfiguration(
            ["North", "South"],
            teamCount: 2,
            cardsPerPlayer: 26,
            deckCount: 1);
        var round = engine.StartRound(configuration, seed: 1);
        var discardCardId = engine.DrawFromStock(round).CurrentPlayer.Hand[0].InstanceId;

        var output = RunCli(
            args: [],
            commands:
            [
                "new-match --players North,South --team-count 2 --cards-per-player 26 --deck-count 1 --seed 1",
                "draw stock",
                $"discard {discardCardId}",
                "draw stock",
                "next-round --seed 11",
                "exit"
            ]);

        Assert.Contains("Started a new match.", output);
        Assert.Contains("Applied `draw stock`.", output);
        Assert.Contains("Applied `discard", output);
        Assert.Contains("Round complete:", output);
        Assert.Contains("stock pile is empty", output);
        Assert.Contains("Use `next-round` to continue the match.", output);
        Assert.Contains("Started the next round.", output);
        Assert.Contains("Shuffle seed: 11", output);
    }

    private static string RunCli(string[] args, string[] commands)
    {
        using var input = new StringReader(string.Join(Environment.NewLine, commands) + Environment.NewLine);
        using var output = new StringWriter();

        var exitCode = CliApplication.Run(args, input, output);

        Assert.Equal(0, exitCode);
        return output.ToString();
    }
}
