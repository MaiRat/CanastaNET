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
            "This command-line app is a starting point for the Canasta game.",
            string.Empty,
            engine.CreateMockGameSummary(),
            string.Empty,
            "Next steps:",
            "- Implement game setup",
            "- Add turn handling",
            "- Expand CLI commands");
    }
}
