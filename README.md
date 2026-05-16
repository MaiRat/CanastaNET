# CanastaNET

CanastaNET is a .NET/C# Canasta project with a Canasta game engine. The repository now provides explicit setup and round-state modeling, meld validation, frozen-discard handling, round-end scoring, multi-round match lifecycle APIs, serialization-friendly snapshots, a small command-line entry point, a WPF desktop shell, and matching automated tests so future milestones can build on a stable core.

## Project structure

- `engine/` - core game engine project and round-state APIs
- `cli/` - interactive command-line app for match setup, gameplay, and state inspection
- `desktop/` - WPF desktop shell and platform-neutral view-model layer built on engine snapshots
- `docs/` - project notes and future documentation space
- `tests/` - automated tests for engine behavior

## Roadmap

- See [`docs/roadmap.md`](docs/roadmap.md) for the current development roadmap covering the game engine, CLI, and planned desktop UI.

## Getting started

1. Install the .NET 10 SDK or newer.
2. Build the solution:

   ```bash
   dotnet build CanastaNET.slnx
   ```

3. Run the CLI:

   ```bash
   dotnet run --project cli/CanastaNET.Cli
   ```

   For a deterministic session, pass a shuffle seed:

   ```bash
   dotnet run --project cli/CanastaNET.Cli -- --seed 42
   ```

   For shared-terminal local multiplayer, hide hands until the active player is ready and optionally clear the terminal between turns:

   ```bash
   dotnet run --project cli/CanastaNET.Cli -- --seed 42 --shared-terminal --hidden-hands --clear-between-turns
   ```

   For replayable regression sessions, place CLI commands in a text file and run them with `--script`:

   ```text
   trace on
   draw stock
   status
   ```

   ```bash
   dotnet run --project cli/CanastaNET.Cli -- --seed 42 --script /tmp/canasta-session.txt
   ```

   Useful operator commands inside the CLI include:

   - `dump match /tmp/canasta-match.json`
   - `load match /tmp/canasta-match.json`
   - `dump round /tmp/canasta-round.json`
   - `load round /tmp/canasta-round.json`
   - `trace on`
   - `run-script /tmp/canasta-session.txt`

4. Run the tests:

   ```bash
   dotnet test tests/CanastaNET.Engine.Tests
   ```

## Desktop UI foundation

- Build the desktop shell:

  ```bash
  dotnet build desktop/CanastaNET.Desktop/CanastaNET.Desktop.csproj
  ```

- On Windows, run the WPF app:

  ```bash
  dotnet run --project desktop/CanastaNET.Desktop/CanastaNET.Desktop.csproj
  ```

- Desktop smoke coverage lives in `DesktopWorkspaceViewModelTests` and can be run with:

  ```bash
  dotnet test tests/CanastaNET.Engine.Tests --filter DesktopWorkspaceViewModelTests
  ```

### Manual verification checklist

On a Windows machine, launch the desktop app and verify:

- the setup panel can start a seeded match with custom players and scoring options
- the table overview tab reflects the current player, turn phase, stock count, discard top card, and legal commands
- the player hands tab allows selecting the current player's cards for draw/meld/discard workflows
- the melds, discard pile, and score summary tabs update after each action
- invalid actions show validation feedback from the engine, and `Next round` becomes available after a completed round
