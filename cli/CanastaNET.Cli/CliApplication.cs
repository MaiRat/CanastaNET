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
            "This command-line app currently exposes the Milestone 3 match lifecycle and engine ergonomics foundation.",
            string.Empty,
            engine.CreateMockGameSummary(),
            string.Empty,
            "Next steps:",
            "- Add interactive CLI commands",
            "- Add full gameplay command loops on top of the command/result APIs",
            "- Expand operator tooling for scripted sessions and diagnostics");
    }
}
