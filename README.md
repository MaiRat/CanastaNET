# CanastaNET

CanastaNET is an initial .NET/C# skeleton for a Canasta card game. This repository currently provides a small engine project, a command-line entry point, and a matching test project so future issues can build out the actual game rules and gameplay flow.

## Project structure

- `engine/` - core game engine project and placeholder APIs
- `cli/` - command-line app for a simple mock run
- `docs/` - project notes and future documentation space
- `tests/` - automated tests for the current skeleton

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
