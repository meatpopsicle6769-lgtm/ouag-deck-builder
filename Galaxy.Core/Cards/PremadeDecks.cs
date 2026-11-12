using System.Text.Json;
using System.Text.Json.Serialization;

namespace Galaxy.Core.Cards;

/// <summary>The Captains' premade decks from guide.galaxy.fun (data/premade_decks.json).</summary>
public sealed class PremadeDecks
{
    private sealed class FileShape
    {
        [JsonPropertyName("decks")] public List<Deck> Decks { get; set; } = [];
    }

    public IReadOnlyList<Deck> All { get; }

    public PremadeDecks(CardCatalog cards)
    {
        var path = Path.Combine(cards.DataDirectory, "premade_decks.json");
        All = File.Exists(path) ? JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path))?.Decks ?? [] : [];
    }

    public IEnumerable<Deck> For(string captainKey) =>
        All.Where(d => string.Equals(d.Captain, captainKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds "captain" (its Starter deck) or "captain/deck name".</summary>
    public Deck? Find(string spec)
    {
        var parts = spec.Split('/', 2);
        var decks = For(parts[0].Trim()).ToList();
        return parts.Length == 1
            ? decks.FirstOrDefault(d => d.Name == "Starter") ?? decks.FirstOrDefault()
            : decks.FirstOrDefault(d => string.Equals(d.Name, parts[1].Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
