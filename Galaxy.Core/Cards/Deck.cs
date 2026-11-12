using System.Text.Json;
using System.Text.Json.Serialization;

namespace Galaxy.Core.Cards;

/// <summary>A deck as saved to JSON: a Captain key and Character keys.</summary>
public sealed class Deck
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Deck";
    [JsonPropertyName("captain")] public string Captain { get; set; } = "";
    [JsonPropertyName("characters")] public List<string> Characters { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static Deck FromJson(string json) =>
        JsonSerializer.Deserialize<Deck>(json) ?? throw new FormatException("Deck JSON is empty.");
    public static Deck Load(string path) => FromJson(File.ReadAllText(path));
    public void Save(string path) => File.WriteAllText(path, ToJson());

    /// <summary>Returns the problems with this deck; empty means it is playable.</summary>
    public List<string> Validate(CardCatalog cards, int deckSize, int maxDeckSize = 0)
    {
        var errors = new List<string>();
        var captain = cards.Find(Captain);
        if (captain is null || !captain.IsCaptain) errors.Add($"'{Captain}' is not a Captain.");
        maxDeckSize = Math.Max(deckSize, maxDeckSize);
        if (Characters.Count < deckSize || Characters.Count > maxDeckSize)
            errors.Add($"A deck needs {(maxDeckSize == deckSize ? $"{deckSize}" : $"{deckSize} to {maxDeckSize}")} Characters, this one has {Characters.Count}.");
        foreach (var dup in Characters.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"'{dup.Key}' is in the deck more than once.");
        foreach (var key in Characters)
        {
            var c = cards.Find(key);
            if (c is null || !c.IsCharacter) errors.Add($"'{key}' is not a Character.");
            else if (!c.IsDeckEligible) errors.Add($"'{c.Name}' is in the shared pool and cannot be put in a deck.");
        }
        return errors;
    }

    /// <summary>A random legal deck, for quick simulations.</summary>
    public static Deck Random(CardCatalog cards, Random rng, int deckSize, string? name = null)
    {
        var eligible = cards.Characters.Where(c => c.IsDeckEligible).ToArray();
        rng.Shuffle(eligible);
        return new Deck
        {
            Name = name ?? "Random deck",
            Captain = cards.Captains[rng.Next(cards.Captains.Count)].Key,
            Characters = eligible.Take(deckSize).Select(c => c.Key).ToList(),
        };
    }
}
