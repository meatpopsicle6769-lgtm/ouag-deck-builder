using System.Text.Json;

namespace Galaxy.Core.Cards;

/// <summary>Every card from a data folder, loaded once.</summary>
public sealed class CardCatalog
{
    private readonly Dictionary<string, Card> byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Card> byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Card> All { get; }
    public IReadOnlyList<Card> Captains { get; }
    public IReadOnlyList<Card> Characters { get; }
    public IReadOnlyList<Card> Spells { get; }
    public IReadOnlyList<Card> Treasures { get; }
    public IReadOnlyList<string> Rarities { get; }
    public string DataDirectory { get; }

    public CardCatalog(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        var all = new List<Card>();
        if (Directory.Exists(dataDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(dataDirectory, "*.json").Order())
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                // Card files are JSON arrays; the rules/mechanic files are objects and are skipped.
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                all.AddRange(doc.RootElement.Deserialize<List<Card>>() ?? []);
            }
        }

        All = all;
        Captains = OfType(CardTypes.Captain);
        Characters = OfType(CardTypes.Character);
        Spells = OfType(CardTypes.Spell);
        Treasures = OfType(CardTypes.Treasure);
        Rarities = all.Select(c => c.Rarity).Distinct().OrderBy(RarityRank).ToList();
        foreach (var c in all)
        {
            byKey[c.Key] = c;
            byName.TryAdd(c.Name, c);
        }
    }

    /// <summary>Finds the data folder next to the app, or by walking up to the repository's /data.</summary>
    public static CardCatalog LoadDefault()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(candidate, "characters.json"))) return new CardCatalog(candidate);
        }
        return new CardCatalog(Path.Combine(AppContext.BaseDirectory, "data"));
    }

    public Card? Find(string keyOrName) =>
        byKey.TryGetValue(keyOrName, out var c) || byName.TryGetValue(keyOrName, out c) ? c : null;

    public Card Get(string keyOrName) =>
        Find(keyOrName) ?? throw new KeyNotFoundException($"Unknown card '{keyOrName}'.");

    public static int RarityRank(string rarity) => rarity == "None" ? -1 : Cards.Rarities.Level(rarity) is var i and >= 0 ? i : 99;

    private List<Card> OfType(string type) =>
        All.Where(c => c.IsType(type)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
}
