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

## Desktop UI

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

### Desktop UX notes

- The WPF shell now includes richer `File`, `Navigate`, `Settings`, and `Help` menus for quicker table navigation and card-view adjustments.
- Playing cards are rendered with dependency-free WPF templates (suit glyphs, accent colors, point badges, deck ribbons, and compact-spacing settings).
- No additional third-party/NuGet UI package was added for the card refresh because modern maintained WPF playing-card-specific packages are limited; the upgraded artwork stays within the existing project dependencies.

### Manual verification checklist

On a Windows machine, launch the desktop app and verify:

- the top menu supports file actions, tab navigation, and card-view settings without disrupting the active match
- the desktop opens focused on the player-hands tab, with a concise legal-next-action prompt instead of always-on descriptive copy
- the left rail keeps setup and snapshot controls tucked into collapsed sections until they are needed
- the setup panel can start a seeded match with custom players and scoring options
- Ctrl+N / Ctrl+S / Ctrl+O / Ctrl+D trigger the expected start, save, load, and draw workflows
- the snapshot panel can save the current match, load it again, and populate recent-match history entries
- the table overview tab reflects the current player, turn phase, stock count, discard top card, and legal commands
- the player hands tab highlights the active hand with the upgraded playing-card visuals and only reveals round-action buttons when they are contextually needed
- the melds and discard pile tabs show the upgraded playing-card visuals and stay synchronized after each action
- the Settings menu toggles point badges, deck ribbons, and compact card spacing across hands, melds, and discard views
- the rules help tab provides onboarding reminders for draw, meld, discard, and snapshot workflows
- invalid actions show validation feedback from the engine, and `Next round` becomes available after a completed round
