# CanastaNET roadmap

This roadmap breaks the project into incremental milestones so the codebase can evolve from a skeleton into a full Canasta implementation without blocking future UI work.

## Priority scale

- **P0** - required for a playable and testable game
- **P1** - important for feature completeness, usability, or maintainability
- **P2** - follow-up improvements after the main game loop is stable

## Cross-cutting principles

- Keep game rules in the engine so CLI and UI clients consume the same behavior.
- Prefer deterministic engine APIs and fixtures so scoring, turn flow, and edge cases can be regression tested.
- Grow the CLI alongside the engine to provide a fast manual test harness before the WPF UI is complete.
- Define view models and presentation DTOs from stable engine state objects to keep UI platforms loosely coupled.

## Milestone 1 - Engine foundation (P0)

**Goal:** make the core engine capable of representing a legal Canasta round.

### Tasks

- Model cards, suits, ranks, jokers, decks, discard pile, hands, teams, and meld containers.
- Define immutable or tightly controlled game-state objects for setup, turn progression, and round summary data.
- Implement shuffle, deal, draw, stock exhaustion, discard, and turn-order rules.
- Establish rule configuration points for team count, player order, and any house-rule variations that may need support later.
- Add unit tests for setup, state transitions, and invalid moves.

### Dependencies

- None beyond the current skeleton.

## Milestone 2 - Core Canasta rules and scoring (P0)

**Goal:** enforce the rules needed to finish a round and score it correctly.

### Tasks

- Implement meld validation, natural/wild card limits, frozen discard handling, and initial meld requirements.
- Implement going out requirements, concealed hand handling, and end-of-round triggers.
- Score cards, melds, canastas, bonuses, penalties, and team totals.
- Add round result objects that expose why a round ended and how points were awarded.
- Expand automated tests to cover common and edge-case scoring scenarios.

### Dependencies

- Requires Milestone 1 state model and turn flow.

## Milestone 3 - Match lifecycle and engine ergonomics (P1)

**Goal:** support multi-round play and provide stable APIs for front ends.

### Repository coverage

- `GameMatchState` tracks cumulative team totals, dealer rotation, round history, and match completion.
- `GameCommand`, `GameCommandResult`, and `RoundCommandCatalog` provide command/result APIs with legal action discovery and error reporting.
- `GameRoundSnapshot` and `GameMatchSnapshot` provide serialization-friendly save/load DTOs for diagnostics and future clients.
- `GameTestFactory` adds reusable engine fixtures for scenario-focused tests.

### Tasks

- Track match totals, dealer rotation, round history, and victory conditions.
- Introduce command/result-style engine APIs for legal action discovery and error reporting.
- Provide serialization-friendly snapshots for save/load, diagnostics, and future multiplayer syncing.
- Add richer engine-level test helpers and fixtures to make scenario coverage easier to maintain.

### Dependencies

- Requires Milestone 2 rules and scoring.

## Milestone 4 - CLI gameplay loop (P0)

**Goal:** make the command-line app a complete playable and debugging surface.

### Repository coverage

- `CliApplication` now hosts an interactive command loop for seeded match setup, turn actions, state inspection, and round progression.
- The CLI surfaces engine validation errors together with currently legal follow-up commands for debugging and manual play.
- CLI-focused tests cover seeded startup, command parsing/dispatch, inspection output, and scripted round progression.

### Tasks

- Replace placeholder text with a command loop for game setup, turn actions, and round progression.
- Add commands to inspect hands, teams, discard pile, melds, scores, and legal actions.
- Support deterministic inputs such as seeded shuffles or scripted hands for testing.
- Provide clear validation/error messages when a user enters an illegal action.
- Add CLI-focused tests for parsing, command dispatch, and formatted output.

### Dependencies

- Requires Milestone 1 for state inspection.
- Milestone 2 is required before the CLI can support full legal gameplay.

### Validation criteria

- Run `dotnet test tests/CanastaNET.Engine.Tests` to verify engine and CLI command-loop coverage.

## Milestone 5 - CLI multiplayer and operator tooling (P1)

**Goal:** make the CLI useful for local multiplayer sessions and developer workflows.

### Tasks

- Support multiple human players sharing a terminal, with optional hidden-hand prompts or per-turn screen clearing.
- Add scripted or replayable sessions for regression testing.
- Add developer-oriented commands for loading fixtures, dumping engine state, and tracing decisions.
- Document CLI usage patterns for manual QA and rule verification.

### Dependencies

- Requires Milestone 4.

## Milestone 6 - WPF UI foundation (P1)

**Goal:** establish a desktop UI shell that can display and drive engine state safely.

### Tasks

- Create the WPF application structure, navigation shell, and shared styling resources.
- Define view models mapped from engine state snapshots rather than duplicating game logic in the UI.
- Implement screens for table overview, player hands, melds, discard pile, score summary, and setup flow.
- Add command binding and validation feedback for draw, meld, discard, and end-turn actions.
- Capture a small set of UI smoke tests where practical and document any manual verification steps.

### Dependencies

- Requires Milestone 3 stable engine-facing APIs.
- Benefits from Milestone 4 CLI learnings around user flows and validation messaging.

## Milestone 7 - WPF polish and user experience (P2)

**Goal:** make the desktop experience feel complete and approachable.

### Tasks

- Add animations, card layout improvements, keyboard shortcuts, and accessibility refinements.
- Improve onboarding with setup presets, rules help, and in-app prompts for legal next actions.
- Add save/load support and recent-match history if serialization work from Milestone 3 exists.
- Validate the UI against full-match playthroughs and edge-case rule interactions.

### Dependencies

- Requires Milestone 6.

## Milestone 8 - Additional UI platforms (P2)

**Goal:** prepare for future clients once the engine and desktop UI are stable.

### Options to evaluate

- **Web UI:** useful for easy distribution and remote play if the engine is exposed through an API or WebAssembly-friendly boundary.
- **.NET MAUI or Avalonia desktop/mobile UI:** useful if cross-platform support becomes a priority beyond Windows/WPF.
- **Service-backed multiplayer client:** useful if network play is desired after the local engine contract is proven.

### Preparation tasks

- Keep engine types UI-agnostic and serialization-friendly.
- Prefer shared application services/view models where platform-neutral logic can be reused.
- Document interface boundaries early so alternate clients can be added without moving rules out of the engine.

## Suggested implementation order

1. Milestone 1 - Engine foundation
2. Milestone 2 - Core Canasta rules and scoring
3. Milestone 4 - CLI gameplay loop
4. Milestone 3 - Match lifecycle and engine ergonomics
5. Milestone 5 - CLI multiplayer and operator tooling
6. Milestone 6 - WPF UI foundation
7. Milestone 7 - WPF polish and user experience
8. Milestone 8 - Additional UI platforms

This ordering keeps the engine authoritative, uses the CLI as the earliest full-game test surface, and delays UI-specific complexity until the underlying rules and workflows are stable.
