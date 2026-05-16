# CanastaNET

CanastaNET is a .NET/C# Canasta project with a Canasta game engine. The repository now provides explicit setup and round-state modeling, meld validation, frozen-discard handling, round-end scoring, multi-round match lifecycle APIs, serialization-friendly snapshots, a small command-line entry point, and matching automated tests so future milestones can build on a stable core.

## Project structure

- `engine/` - core game engine project and round-state APIs
- `cli/` - command-line app for inspecting the current engine foundation
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

3. Run the CLI skeleton:

   ```bash
   dotnet run --project cli/CanastaNET.Cli
   ```

4. Run the tests:

   ```bash
   dotnet test tests/CanastaNET.Engine.Tests
   ```
