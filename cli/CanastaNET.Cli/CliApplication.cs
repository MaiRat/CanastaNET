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
            "This command-line app currently exposes the Milestone 1 engine foundation.",
            string.Empty,
            engine.CreateMockGameSummary(),
            string.Empty,
            "Next steps:",
            "- Implement meld rules and scoring",
            "- Add round-ending rules beyond stock exhaustion",
            "- Expand CLI commands");
    }
}
