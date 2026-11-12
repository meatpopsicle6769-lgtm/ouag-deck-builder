using System.Text.Json;
using System.Text.Json.Serialization;

namespace Galaxy.Core.Cards;

/// <summary>Attack/Health pair.</summary>
public readonly record struct Stats(int Atk, int Hp)
{
    public static readonly Stats One = new(1, 1);
    public static Stats operator +(Stats a, Stats b) => new(a.Atk + b.Atk, a.Hp + b.Hp);
    public static Stats operator *(Stats a, int k) => new(a.Atk * k, a.Hp * k);
    public bool IsZero => Atk == 0 && Hp == 0;
    public int Total => Atk + Hp;
    /// <summary>Adds <paramref name="bonus"/> only to the components this buff actually has.</summary>
    public Stats Improve(Stats bonus) => new(Atk != 0 ? Atk + bonus.Atk : 0, Hp != 0 ? Hp + bonus.Hp : 0);
    public override string ToString() => $"{Atk}/{Hp}";
}

public static class CardTypes
{
    public const string Captain = "captain";
    public const string Character = "character";
    public const string Spell = "spell";
    public const string Treasure = "treasure";
}

public static class Rarities
{
    public static readonly string[] Order = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Secret Rare"];
    public const int Legendary = 4;
    public const int SecretRare = 5;

    /// <summary>0 = Common ... 5 = Secret Rare, -1 = no rarity.</summary>
    public static int Level(string rarity) => Array.IndexOf(Order, rarity);
    public static int Level(string word, bool ignoreCase) =>
        Array.FindIndex(Order, r => r.Equals(word, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
}

/// <summary>One card as stored in the data/*.json files.</summary>
public sealed class Card
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("rarity")] public string Rarity { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("set")] public string Set { get; set; } = "";
    [JsonPropertyName("pool")] public string Pool { get; set; } = "";
    [JsonPropertyName("cid")] public string Cid { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("goldText")] public string? GoldText { get; set; }
    [JsonPropertyName("attack")] public int? Attack { get; set; }
    [JsonPropertyName("health")] public int? Health { get; set; }
    [JsonPropertyName("factions")] public List<string> Factions { get; set; } = [];

    /// <summary>Any fields not modelled above, so nothing in the data is hidden.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public bool IsType(string type) => string.Equals(Type, type, StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsCharacter => IsType(CardTypes.Character);
    [JsonIgnore] public bool IsCaptain => IsType(CardTypes.Captain);
    [JsonIgnore] public bool IsSpell => IsType(CardTypes.Spell);
    [JsonIgnore] public bool IsTreasure => IsType(CardTypes.Treasure);
    [JsonIgnore] public bool IsShared => string.Equals(Pool, "shared", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public int RarityLevel => Rarities.Level(Rarity);
    [JsonIgnore] public Stats BaseStats => new(Attack ?? 0, Health ?? 0);

    public bool HasFaction(string faction) => Factions.Contains(faction, StringComparer.OrdinalIgnoreCase);

    /// <summary>Deck rule: a deck holds Characters that are NOT in the shared card pool.</summary>
    [JsonIgnore] public bool IsDeckEligible => IsCharacter && !IsShared;

    /// <summary>Every property of the card as label/value pairs, for the details panel.</summary>
    public IEnumerable<KeyValuePair<string, string>> Properties()
    {
        yield return new("Name", Name);
        yield return new("Type", Type);
        yield return new("Rarity", Rarity);
        if (Attack is not null) yield return new("Attack / Health", BaseStats.ToString());
        if (Factions.Count > 0) yield return new("Factions", string.Join(", ", Factions));
        yield return new("Set", Set);
        yield return new("Pool", Pool);
        yield return new("Card ID", Cid);
        yield return new("Key", Key);
        yield return new("Text", Text);
        if (!string.IsNullOrEmpty(GoldText)) yield return new("Gold text", GoldText);
        if (Extra is null) yield break;
        foreach (var (name, value) in Extra)
            yield return new(name, value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText());
    }

    public override string ToString() => Name;
}
