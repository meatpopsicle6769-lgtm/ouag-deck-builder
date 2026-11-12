Only Human Comment - PoC Fable 5.2 build of deck editor and simulation engine built off single prompt and one update pass. Mostly untested but seems to work.
# ouag-deck-builder

Deck editor and game simulator for the Galaxy card game.

- `data/` - card data (with Attack/Health and factions), premade decks and rules text from guide.galaxy.fun.
- `DeckBuilder/` - ASP.NET Core (.NET 10) Blazor Server app: deck editor and Simulate page.
- `Galaxy.Core/` - the game engine, usable from any C# code.
- `Galaxy.Sim/` - command-line simulator.
- `Galaxy.Tests/` - engine tests.
- `docs/` - `RULES.md`, `SIMULATION.md` (how to run, what is and is not simulated), `ABILITY_COVERAGE.md`.

## Run the web app

```bash
dotnet run --project DeckBuilder
```

Then open http://localhost:5171.

## Run simulations from the command line

```bash
dotnet run --project Galaxy.Sim -- simulate --deck-a premade:apollo --deck-b premade:baldr --games 10000
```

## Run the tests

```bash
dotnet test
```
