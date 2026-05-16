using CanastaNET.Engine;

namespace CanastaNET.Engine.Tests;

internal static class GameTestFactory
{
    public static Card CreateCard(int instanceId, CardRank rank, CardSuit suit, int deckNumber = 1) =>
        new(instanceId, rank, suit, deckNumber);

    public static GameRoundState CreateRoundState(
        IReadOnlyList<PlayerState> players,
        IReadOnlyList<TeamState> teams,
        IEnumerable<Card>? stockPile = null,
        IEnumerable<Card>? discardPile = null,
        TurnPhase turnPhase = TurnPhase.AwaitingDiscard,
        int currentPlayerIndex = 0,
        int completedTurnCount = 0,
        int currentTurnMeldPoints = 0,
        bool currentTurnStartedWithOpenTeam = false,
        RoundSummaryData? summary = null)
    {
        var playerArray = players.ToArray();
        var teamArray = teams.ToArray();
        var discardCards = (discardPile ?? [CreateCard(999, CardRank.Five, CardSuit.Clubs)]).ToArray();
        var stockCards = (stockPile ?? []).ToArray();
        var cardsPerPlayer = playerArray.Any()
            ? Math.Max(playerArray.Max(player => player.Hand.Count), 1)
            : 1;
        var configuration = new GameConfiguration(
            playerArray.Select(player => player.Name),
            teamCount: teamArray.Length,
            cardsPerPlayer: cardsPerPlayer,
            teamStartingScores: teamArray.Select(team => team.StartingScore));

        return new GameRoundState(
            configuration,
            new RoundSetupData(7, currentPlayerIndex, configuration.DealerIndex, cardsPerPlayer, discardCards, stockCards.Length),
            playerArray,
            teamArray,
            new DeckState(stockCards),
            new DiscardPileState(discardCards),
            configuration.DealerIndex,
            currentPlayerIndex,
            turnPhase,
            completedTurnCount,
            summary,
            currentTurnMeldPoints,
            currentTurnStartedWithOpenTeam);
    }

    public static GameMatchState CreateMatchState(
        GameRoundState currentRound,
        IEnumerable<int>? teamScores = null,
        IEnumerable<MatchRoundRecord>? roundHistory = null,
        int winningScore = 5000,
        IEnumerable<int>? winningTeamIndexes = null)
    {
        return new GameMatchState(
            currentRound.Configuration,
            winningScore,
            teamScores ?? currentRound.Configuration.TeamStartingScores,
            roundHistory ?? [],
            currentRound,
            winningTeamIndexes);
    }
}
