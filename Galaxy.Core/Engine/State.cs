using Galaxy.Core.Abilities;
using Galaxy.Core.Cards;

namespace Galaxy.Core.Engine;

public enum Tier { Bronze, Silver, Golden }

[Flags] public enum Paint { None = 0, Red = 1, Blue = 2, Yellow = 4 }

/// <summary>Anything that can carry abilities and per-game ability state.</summary>
public abstract class Holder
{
    public required Card Card { get; init; }
    /// <summary>Accumulated "improve this ability by +X/+Y".</summary>
    public Stats Improve;
    public Dictionary<string, int> Counters { get; } = new();
    public int Count(string key) => Counters.GetValueOrDefault(key);
    public int Inc(string key, int by = 1) => Counters[key] = Count(key) + by;
}

/// <summary>A Captain or a Treasure a player owns.</summary>
public sealed class Attachment : Holder
{
    public bool IsCaptain => Card.IsCaptain;
}

/// <summary>A player's copy of a Character. Buffs live here, so they survive fights and replacement.</summary>
public sealed class Unit : Holder
{
    public Tier Tier;
    /// <summary>Copies recruited so far (1 = Bronze, 2 = Silver, 3 = Golden).</summary>
    public int Copies;
    public Stats Buff;
    /// <summary>Stats for summoned tokens such as "a 2/2 Bot Pilot".</summary>
    public Stats? BaseOverride;
    public int ExtraHoard;
    public bool GrantedRanged, GrantedSneak, Candy;
    public HashSet<string>? ExtraTypes;
    public bool QuestDone, TreasureFindUsed;
    public Stats Base => BaseOverride ?? Card.BaseStats;
    public override string ToString() => Card.Name;
}

public sealed class Slot
{
    public required int Index { get; init; }
    public Unit? Unit;
    public Stats Buff;
    public Paint Paint;
    public bool IsFront => Index < Board.FrontSlots;
}

/// <summary>Board geometry: slots 0-2 are the front row, 3-4 the back row.</summary>
public static class Board
{
    public const int Size = 5;
    public const int FrontSlots = 3;
    private static readonly int[][] Front = [[], [], [], [0, 1], [1, 2]];
    private static readonly int[][] Back = [[3], [3, 4], [4], [], []];
    private static readonly int[][] Adjacent = [[1], [0, 2], [1], [4], [3]];
    /// <summary>Slots a back-row slot supports.</summary>
    public static int[] InFrontOf(int slot) => slot is >= 0 and < Size ? Front[slot] : [];
    public static int[] Behind(int slot) => slot is >= 0 and < Size ? Back[slot] : [];
    public static int[] Neighbors(int slot) => slot is >= 0 and < Size ? Adjacent[slot] : [];
}

/// <summary>One choice in a shop.</summary>
public sealed class Offer
{
    public required Card Card { get; init; }
    /// <summary>The player's existing copy of this Character, if any.</summary>
    public Unit? Existing;
    public Tier Tier;
    /// <summary>Shop buffs on this offer; kept if it is recruited.</summary>
    public Stats Bonus;
    public Stats Shown;
    public bool IsSpell => Card.IsSpell;
}

public sealed class Player
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required Deck Deck { get; init; }
    public required IAgent Agent { get; init; }

    public int Hearts;
    /// <summary>How many of the remaining hearts are cracked.</summary>
    public int Cracked;
    public int MaxHearts;

    public Slot[] Slots { get; } = Enumerable.Range(0, Board.Size).Select(i => new Slot { Index = i }).ToArray();
    public List<Unit> Bank { get; } = [];
    public Dictionary<string, Unit> Units { get; } = new();
    public List<Attachment> Captains { get; } = [];
    public List<Attachment> Treasures { get; } = [];
    public List<Card> CharacterPool { get; } = [];
    public List<Card> SpellPool { get; } = [];
    public HashSet<string> RemovedFromPool { get; } = [];
    public List<Card> StartingDeck { get; } = [];

    // Permanent buff modifiers gained during the game.
    public Stats SpellBuffBonus, FaeBuffBonus, TreasureBuffBonus, CharacterBuffBonus, ShopAura;
    // One-shot effects waiting for the next shop / recruit / fight.
    public Stats NextShopBuff, NextRecruitBuff;
    public int NextRecruitMultiplier = 1;
    public string? NextShopFaction;
    public List<(string Faction, Stats Buff)> NextShopTypedBuffs { get; } = [];
    public int NextShopMaxRarity = -1;
    public bool AttacksFirstNextFight;
    public List<Card> QueuedCombatSpells { get; } = [];

    public int Sweetness;
    public int BonusShops;
    public Dictionary<string, int> Counters { get; } = new();
    public int Count(string key) => Counters.GetValueOrDefault(key);
    public int Inc(string key, int by = 1) => Counters[key] = Count(key) + by;

    public int MissingHearts => Math.Max(0, MaxHearts - Hearts);
    public IEnumerable<Slot> Occupied => Slots.Where(s => s.Unit is not null);
    public bool BoardFull => Slots.All(s => s.Unit is not null);
    public int SlotOf(Unit u) => Array.FindIndex(Slots, s => ReferenceEquals(s.Unit, u));
    public override string ToString() => Name;
}
