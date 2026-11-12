# Simulator

`Galaxy.Core` is the game engine (no UI). `Galaxy.Sim` runs it from the command line, and the web app's
**Simulate** page runs it in the browser. Rules follow https://guide.galaxy.fun (saved in `data/guide_rules.json`).

## Running

```bash
dotnet run --project Galaxy.Sim -- simulate --deck-a premade:apollo --deck-b premade:baldr --games 10000 --seed 1
```

```bash
dotnet run --project Galaxy.Sim -- play --deck-a a.json --deck-b b.json --seed 3
```

Other commands: `premade` (list premade decks), `random-deck`, `coverage`. Run with no arguments for all options
(`--json`, `--agent-a random`, `--finals-rule`, `--no-front-row`, `--max-turns`, `--deck-size`).

A deck is a file `{ "name": "...", "captain": "apollo", "characters": ["aladdin", ...] }` (keys or names), or
`premade:captain` / `premade:captain/Deck Name`. The Simulate page shows the JSON for the decks it used.

From C#:

```csharp
var cards = CardCatalog.LoadDefault();
var summary = Simulator.Run(cards, Deck.Load("a.json"), Deck.Load("b.json"), games: 10_000, seed: 1);
var oneGame = Simulator.PlayOne(cards, deckA, deckB, seed: 7, log: true);   // oneGame.Log has every step
```

Game `i` of a run uses `seed + i`, so results are repeatable. Implement `IAgent` to script your own play style.

## What is simulated

- **Setup**: 2 players, each a Captain and a deck. The card pool is the shared-pool Characters plus the player's own
  deck. Pools are not shared between players.
- **Turn**: each player gets 2 shops, then the two boards fight.
- **Shop**: 3 random Characters from the pool, plus 1 Spell from turn 3. Commons only on turns 1-2; Uncommon from
  turn 3, Rare 5, Epic 7, Legendary 9. Pick one card or skip.
- **Copies**: the 2nd copy recruited is Silver (+1/+1 and a bonus shop), the 3rd is Golden (+1/+1 more, uses the gold
  text, gives a treasure shop of its rarity, and leaves the pool). A new copy replaces the one already on the board.
- **Board**: 5 slots, 3 front and 2 back, plus a bank (recruit without placing, only when the board is full).
- **Fight**: a random player attacks first. Sides alternate; each side's characters attack in slot order. The target
  is a random enemy in the front row while one stands (Sneak goes for the back row first). Both deal damage; Ranged
  attackers take none. The fight ends when one side has nobody left.
- **Hearts**: 3 to start. Losing to 3+ survivors empties a heart; losing to fewer cracks one (half a heart), and a
  second crack empties it. Winning repairs a cracked heart. Out of hearts = eliminated.
- **Buffs** are permanent and follow the character; slot buffs stay on the slot. Damage is reset after each fight.
- **Keywords**: Recruit, Farewell, Hunt, Quest, Support, Ranged, Sneak, Treasure Hoard, Treasure Find, Chance
  Encounter, Candy sweetening / Supersweet, Paint, summons, transforms.
- **Treasures, Spells, Captains**: implemented where their text matches a known pattern.

## What is not simulated, and assumptions

- **Card text coverage is partial.** See `docs/ABILITY_COVERAGE.md` for the exact list. Unimplemented text does
  nothing, so win rates for decks that lean on those cards are understated.
- Boons, Prophecies, co-captains, "banish", repeat-cast effects and several one-off cards are not implemented.
- A Golden copy uses its gold text only when that text has the same wording as the normal text with different
  numbers; otherwise the Golden copy keeps the normal ability.
- Shop buffs ("give your shop +X/+Y") apply to the next shop's Characters and are kept only if recruited.
- Spells that hit an enemy (for example Lightning Bolt) are held and resolve at the start of the next fight.
- Support geometry is assumed: back slot 4 supports front slots 1-2, back slot 5 supports front slots 2-3.
  Neighbours are adjacent slots in the same row.
- There is no treasure cap (the guide does not give the number).
- A game still running after 40 turns, or a fight where nobody can deal damage, is a draw.
- Decks of 10 to 12 Characters are accepted: the deck editor builds 10, the site's premade decks have 12.
- The built-in `GreedyAgent` plays by stats only: take Silver/Golden copies, fill the board, replace the weakest
  character if the offer is bigger, otherwise cast the spell or bank. It does not plan around abilities.
