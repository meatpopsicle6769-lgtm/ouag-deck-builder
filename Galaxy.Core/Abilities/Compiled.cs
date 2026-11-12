using Galaxy.Core.Cards;
using Galaxy.Core.Engine;

namespace Galaxy.Core.Abilities;

public enum Trigger
{
    // Fired on one specific character.
    Recruit, Farewell, Hunt, Attack, Attacked, SurviveDamage, TakeDamage, Silvered, Goldened, ChanceEncounter, SpellCastOnMe,
    // Fired on everything a player has in play (Captains, Treasures, characters on the board).
    FightStart, TurnStart, AllyAttack, AllyAttacked, AllyKO, AnyKO, SpellCast, RecruitOther, Summon, AllyHunt, AllyGoldened,
    TreasureCollected, TreasureDiscovered, FightWon, FightLost, FightNotWon, HeartLost, Transformed, Sweetened, QuestCompleted, Pick, SlotBuffed,
    // Fired on the card itself.
    Cast, Collected, GameStart,
}

/// <summary>Everything an effect needs to know about the moment it runs in.</summary>
public sealed class Ctx
{
    public required Game G { get; init; }
    public required Player Owner { get; init; }
    /// <summary>The card whose ability this is ("me").</summary>
    public required Holder Holder { get; init; }
    /// <summary>The card the ability text came from; differs from Holder for granted abilities.</summary>
    public required CompiledCard Source { get; init; }
    public Fight? Fight { get; init; }
    public Fighter? MeF { get; init; }
    public int MeSlot { get; init; } = -1;
    /// <summary>The other card in the event: recruited/summoned character, attacker, prey, spell target.</summary>
    public Unit? It { get; set; }
    public Fighter? ItF { get; set; }
    public int TargetSlot { get; set; } = -1;
    public int Amount { get; init; }

    public Unit? Me => Holder as Unit;
    public Player Enemy => G.Players[1 - Owner.Index];
    public IEnumerable<Entry> Mine => G.BoardOf(Owner, Fight);
    public IEnumerable<Entry> Theirs => G.BoardOf(Enemy, Fight);
    public int ItSlot => ItF?.Slot ?? (It is null ? -1 : Owner.SlotOf(It));
    public bool Is(Unit? u, string faction) => u is not null && G.HasType(Owner, u, faction);
    public void Buff(Unit? u, Stats s) { if (u is not null) G.Buff(this, u, s); }
    public void BuffSlot(int slot, Stats s) { if (slot >= 0) G.BuffSlot(this, slot, s); }
    public T? Pick<T>(IEnumerable<T> items)
    {
        var list = items as IReadOnlyList<T> ?? items.ToList();
        return list.Count == 0 ? default : list[G.Rng.Next(list.Count)];
    }
}

/// <summary>A character on a board (or in a fight) together with where it stands.</summary>
public readonly record struct Entry(Unit Unit, int Slot, Fighter? Fighter);

/// <summary>Inputs for a static bonus: holder H (at HSlot) is asked what it gives unit U at Slot.</summary>
public readonly record struct AuraCtx(Game G, Player P, Holder H, int HSlot, Unit U, int Slot, Fight? Fight);

public sealed class Ability
{
    public required Trigger Trigger { get; init; }
    public required Action<Ctx> Run { get; init; }
    public Func<Ctx, bool>? When { get; init; }
    public required string Text { get; init; }
}

/// <summary>An ability one card gives to other characters, e.g. Your Pirates have "Hunt — +10/+10".</summary>
public sealed record Granted(Func<AuraCtx, bool> AppliesTo, Ability Ability);

public sealed class Quest
{
    public required Trigger[] Triggers { get; init; }
    /// <summary>Advances the quest; returns true once it is complete.</summary>
    public required Func<Ctx, bool> Step { get; init; }
    public required string Text { get; init; }
}

public enum Coverage { None, Partial, Full }

/// <summary>A card's rules text turned into behaviour, plus a record of what could not be turned.</summary>
public sealed class CompiledCard
{
    public required Card Card { get; init; }
    public List<Ability> Abilities { get; } = [];
    public List<Granted> Granted { get; } = [];
    public Quest? Quest;
    public string? TreasureFind;

    // Keywords and self-only statics.
    public bool Ranged, Sneak, Cursed, CantAttack, Targeted, Combat, UsesImprove, FirstAttack, AllTypes, FirstCharacterRanged, FirstHitImmune;
    public int ExtraSweeten, SupportHoard;
    public List<(Func<AuraCtx, bool> Who, string Keyword)> KeywordGrants { get; } = [];
    public int Hoard, Supersweet = 1, BuffMultiplier = 1, StartHearts;
    public Stats Support;
    public List<Func<AuraCtx, Stats>> SelfAuras { get; } = [];

    // Statics this card gives to its owner's other cards.
    public List<Func<AuraCtx, Stats>> UnitAuras { get; } = [];
    public List<Func<Player, int, Stats>> SlotAuras { get; } = [];
    public List<Func<Game, Player, Offer, Stats>> ShopAuras { get; } = [];
    public List<Func<AuraCtx, int>> HoardGrants { get; } = [];
    public List<(string Type, string AlsoType)> TypeGrants { get; } = [];
    public Stats SpellBonus, FaeBonus, TreasureBonus;
    public Dictionary<Trigger, int> ExtraTriggers { get; } = new();

    /// <summary>Lines fully implemented, partly implemented, and not implemented at all.</summary>
    public List<string> Parsed { get; } = [];
    public List<string> PartlyParsed { get; } = [];
    public List<string> Unparsed { get; } = [];
    public Coverage Coverage =>
        Unparsed.Count + PartlyParsed.Count == 0 ? Coverage.Full
        : Parsed.Count + PartlyParsed.Count == 0 ? Coverage.None : Coverage.Partial;

    public bool IsCandy => Card.HasFaction("Candy");
}
