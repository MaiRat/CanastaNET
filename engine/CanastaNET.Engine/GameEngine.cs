namespace CanastaNET.Engine;

public sealed class GameEngine
{
    private static readonly string[] PlannedSubsystems =
    [
        "Deck and discard pile management",
        "Player hands and teams",
        "Turn flow and scoring",
        "Round and match lifecycle"
    ];

    public string GetStartupMessage() => "CanastaNET engine skeleton ready.";

    public IReadOnlyList<string> GetPlannedSubsystems() => PlannedSubsystems;

    public string CreateMockGameSummary()
    {
        var plannedSubsystems = string.Join(
            Environment.NewLine,
            PlannedSubsystems.Select(subsystem => $"- {subsystem}"));

        return string.Join(
            Environment.NewLine,
            GetStartupMessage(),
            "Planned subsystems:",
            plannedSubsystems);
    }
}
