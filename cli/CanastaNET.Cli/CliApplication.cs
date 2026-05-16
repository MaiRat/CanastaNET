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
            "This command-line app currently exposes the Milestone 2 core rules and scoring engine.",
            string.Empty,
            engine.CreateMockGameSummary(),
            string.Empty,
            "Next steps:",
            "- Add interactive CLI commands",
            "- Track multi-round match totals and history",
            "- Expand save/load and diagnostics support");
    }
}
