using System.Text.Json;
using Galaxy.Core.Abilities;
using Galaxy.Core.Cards;
using Galaxy.Core.Engine;

// Command-line runner for the Galaxy simulator. Run with no arguments for help.
return Cli.Run(args);

internal static class Cli
{
    private const string Help = """
        Galaxy simulator

          premade     [--captain KEY]                                 List the premade decks from guide.galaxy.fun.
          simulate    --deck-a FILE --deck-b FILE [--games N] [--seed N] [--agent-a greedy|random] [--agent-b ...] [--json]
          play        --deck-a FILE --deck-b FILE [--seed N]          Play one game and print the full log.
          random-deck [--seed N] [--name TEXT] [--out FILE]           Write a random legal deck as JSON.
          coverage    [--type character|captain|spell|treasure] [--unparsed]
                                                                      Show how much card text the engine implements.

        Common options:
          --data DIR        Folder with the card JSON files (default: the repository's data folder).
          --deck-size N     Characters per deck (default 10).
          --max-turns N     Turn limit before a game is called a draw (default 40).
          --finals-rule     Every loss empties a whole heart (default: losing to fewer than 3 survivors costs half a heart).
          --no-front-row    Any enemy can be attacked (front row does not protect the back row).

        Instead of a FILE, a deck can be premade:captain or premade:captain/Deck Name (e.g. premade:apollo/Starter).
        A deck file looks like: { "name": "My deck", "captain": "apollo", "characters": ["aladdin", "...10 keys or names"] }
        """;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Help);
            return 0;
        }

        try
        {
            var o = new Options(args.Skip(1).ToArray());
            var cards = o.Text("data") is { } dir ? new CardCatalog(dir) : CardCatalog.LoadDefault();
            if (cards.All.Count == 0) throw new InvalidOperationException($"No card data found in '{cards.DataDirectory}'. Use --data DIR.");
            var rules = new GameRules
            {
                DeckSize = o.Int("deck-size", 10),
                MaxTurns = o.Int("max-turns", 40),
                LossAlwaysEmptiesHeart = o.Flag("finals-rule"),
                FrontRowProtectsBackRow = !o.Flag("no-front-row"),
            };

            switch (args[0])
            {
                case "simulate": return Simulate(o, cards, rules);
                case "play": return Play(o, cards, rules);
                case "random-deck": return RandomDeck(o, cards, rules);
                case "coverage": return CoverageReport(o, cards);
                case "premade": return Premade(o, cards);
                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.\n\n{Help}");
                    return 2;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or JsonException or FormatException or KeyNotFoundException)
        {
            Console.Error.WriteLine("Error: " + e.Message);
            return 1;
        }
    }

    private static Func<IAgent> Agent(string? name) => name?.ToLowerInvariant() switch
    {
        null or "greedy" => () => new GreedyAgent(),
        "random" => () => new RandomAgent(),
        _ => throw new ArgumentException($"Unknown agent '{name}'. Use greedy or random."),
    };

    /// <summary>A deck file, or "premade:captain" / "premade:captain/Deck Name" for one of the site's premade decks.</summary>
    private static Deck LoadDeck(Options o, string name, CardCatalog cards)
    {
        var spec = o.Text(name) ?? throw new ArgumentException($"--{name} is required (a deck file or premade:captain[/Deck Name]).");
        if (!spec.StartsWith("premade:", StringComparison.OrdinalIgnoreCase)) return Deck.Load(spec);
        return new PremadeDecks(cards).Find(spec[8..]) ?? throw new ArgumentException($"No premade deck '{spec[8..]}'. Run the 'premade' command to list them.");
    }

    private static int Premade(Options o, CardCatalog cards)
    {
        var decks = new PremadeDecks(cards);
        foreach (var d in o.Text("captain") is { } captain ? decks.For(captain) : decks.All)
            Console.WriteLine($"premade:{d.Captain}/{d.Name}   ({d.Characters.Count} cards)");
        return 0;
    }

    private static int Simulate(Options o, CardCatalog cards, GameRules rules)
    {
        var (a, b) = (LoadDeck(o, "deck-a", cards), LoadDeck(o, "deck-b", cards));
        var games = o.Int("games", 1000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var s = Simulator.Run(cards, a, b, games, o.Int("seed", 0), rules, Agent(o.Text("agent-a")), Agent(o.Text("agent-b")));
        watch.Stop();

        if (o.Flag("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                deckA = a.Name, deckB = b.Name, s.Games, s.Seed, s.WinsA, s.WinsB, s.Draws,
                s.WinRateA, s.WinRateB, s.DrawRate, s.AverageTurns, s.AverageHeartsLeftA, s.AverageHeartsLeftB,
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return 0;
        }

        Console.WriteLine($"{games} games, seed {s.Seed}, {watch.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"  A  {a.Name,-24} wins {s.WinsA,7}  ({s.WinRateA:P1})   avg hearts left {s.AverageHeartsLeftA:F2}");
        Console.WriteLine($"  B  {b.Name,-24} wins {s.WinsB,7}  ({s.WinRateB:P1})   avg hearts left {s.AverageHeartsLeftB:F2}");
        Console.WriteLine($"     {"draws",-24}      {s.Draws,7}  ({s.DrawRate:P1})");
        Console.WriteLine($"  average game length: {s.AverageTurns:F1} turns");
        return 0;
    }

    private static int Play(Options o, CardCatalog cards, GameRules rules)
    {
        var result = Simulator.PlayOne(cards, LoadDeck(o, "deck-a", cards), LoadDeck(o, "deck-b", cards), o.Int("seed", 0), rules,
            Agent(o.Text("agent-a"))(), Agent(o.Text("agent-b"))(), log: true);
        foreach (var line in result.Log!) Console.WriteLine(line);
        return 0;
    }

    private static int RandomDeck(Options o, CardCatalog cards, GameRules rules)
    {
        var deck = Deck.Random(cards, new Random(o.Int("seed", Environment.TickCount)), rules.DeckSize, o.Text("name"));
        if (o.Text("out") is { } path)
        {
            deck.Save(path);
            Console.WriteLine($"Wrote {path}");
        }
        else Console.WriteLine(deck.ToJson());
        return 0;
    }

    private static int CoverageReport(Options o, CardCatalog cards)
    {
        var report = AbilityLibrary.For(cards).CoverageReport().Where(c => o.Text("type") is not { } t || c.Card.IsType(t)).ToList();
        foreach (var group in report.GroupBy(c => c.Card.Type).OrderBy(g => g.Key))
        {
            var withText = group.Where(c => c.Parsed.Count + c.PartlyParsed.Count + c.Unparsed.Count > 0).ToList();
            Console.WriteLine($"{group.Key,-10} {group.Count(),4} cards | no text {group.Count() - withText.Count,3} | full {withText.Count(c => c.Coverage == Coverage.Full),3}"
                + $" | partial {withText.Count(c => c.Coverage == Coverage.Partial),3} | none {withText.Count(c => c.Coverage == Coverage.None),3}");
        }
        if (o.Flag("unparsed"))
            foreach (var c in report.Where(c => c.Unparsed.Count + c.PartlyParsed.Count > 0).OrderBy(c => c.Card.Type).ThenBy(c => c.Card.Name))
                Console.WriteLine($"[{c.Coverage}] {c.Card.Type} {c.Card.Name}: {string.Join(" | ", c.PartlyParsed.Select(l => "(partly) " + l).Concat(c.Unparsed))}");
        return 0;
    }
}

/// <summary>Minimal "--name value" / "--flag" parser.</summary>
internal sealed class Options
{
    private readonly Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

    public Options(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
            values[args[i][2..]] = hasValue ? args[++i] : null;
        }
    }

    public bool Flag(string name) => values.ContainsKey(name);
    public string? Text(string name) => values.GetValueOrDefault(name);
    public int Int(string name, int fallback) =>
        Text(name) is { } v ? int.TryParse(v, out var n) ? n : throw new ArgumentException($"--{name} needs a number.") : fallback;
}
