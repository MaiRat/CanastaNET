using CanastaNET.Cli;
using CanastaNET.Engine;

namespace CanastaNET.Engine.Tests;

public class GameSkeletonTests
{
    [Fact]
    public void EngineSummaryListsPlannedSubsystems()
    {
        var engine = new GameEngine();

        var summary = engine.CreateMockGameSummary();

        Assert.Contains("CanastaNET engine skeleton ready.", summary);
        Assert.Contains("Deck and discard pile management", summary);
        Assert.Contains("Turn flow and scoring", summary);
    }

    [Fact]
    public void CliWelcomeTextIncludesNextSteps()
    {
        var text = CliApplication.CreateWelcomeText(new GameEngine());

        Assert.Contains("Welcome to CanastaNET!", text);
        Assert.Contains("This command-line app is a starting point", text);
        Assert.Contains("Next steps:", text);
        Assert.Contains("- Expand CLI commands", text);
    }
}
