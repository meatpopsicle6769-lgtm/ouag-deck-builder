using Galaxy.Core.Abilities;
using Galaxy.Core.Cards;

namespace Galaxy.Core.Engine;

/// <summary>A card currently in play for a player, with its compiled text and position.</summary>
public readonly record struct ActiveHolder(Holder H, CompiledCard C, int Slot, Fighter? F);

public sealed partial class Game
{
    // ---------------------------------------------------------------- what is in play

    /// <summary>The player's characters: the fight's living fighters during a fight, otherwise the board.</summary>
    public IEnumerable<Entry> BoardOf(Player p, Fight? fight)
    {
        if (fight is not null) return fight.Entries(p.Index);
        return p.Occupied.Select(s => new Entry(s.Unit!, s.Index, null));
    }

    /// <summary>Captains, Treasures and characters on the board: everything whose text is "on".</summary>
    public IEnumerable<ActiveHolder> Active(Player p, Fight? fight = null)
    {
        fight ??= Current;
        foreach (var a in p.Captains) yield return new ActiveHolder(a, lib.Get(a.Card), -1, null);
        foreach (var a in p.Treasures) yield return new ActiveHolder(a, lib.Get(a.Card), -1, null);
        foreach (var e in BoardOf(p, fight)) yield return new ActiveHolder(e.Unit, Compiled(e.Unit), e.Slot, e.Fighter);
    }

    public bool HasType(Player p, Unit u, string faction)
    {
        var card = u.Card;
        if (card.HasFaction(faction) || card.HasFaction("All")) return true;
        if (u.ExtraTypes?.Contains(faction) == true || (u.Candy && faction == "Candy")) return true;
        foreach (var a in p.Captains.Concat(p.Treasures))
        {
            var cc = lib.Get(a.Card);
            if (cc.AllTypes) return true;
            foreach (var (from, to) in cc.TypeGrants)
                if (to == faction && card.HasFaction(from)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- stats

    private Stats SumBonus(Player p, Stats start, Func<CompiledCard, Stats> pick)
    {
        foreach (var h in Active(p)) start += pick(h.C);
        return start;
    }

    public Stats SpellBonus(Player p) => SumBonus(p, p.SpellBuffBonus, c => c.SpellBonus);
    public Stats FaeBonus(Player p) => SumBonus(p, p.FaeBuffBonus, c => c.FaeBonus);
    public Stats TreasureBonus(Player p) => SumBonus(p, p.TreasureBuffBonus, c => c.TreasureBonus);

    private Stats Candy(Player p, CompiledCard c) => Stats.One * (p.Sweetness * c.Supersweet);

    /// <summary>Applies treasure-buff and Candy bonuses to an always-on bonus from <paramref name="h"/>.</summary>
    private Stats Scale(Player p, ActiveHolder h, Stats s)
    {
        if (s.IsZero) return s;
        var bonus = h.H.Improve;
        if (h.C.Card.IsTreasure) bonus += TreasureBonus(p);
        if (h.C.IsCandy) bonus += Candy(p, h.C);
        return s.Improve(bonus);
    }

    /// <summary>Current Attack/Health of a character, before damage. slot -1 = not on the board.</summary>
    public Stats StatsOf(Player p, Unit u, int slot, Fight? fight = null, Fighter? f = null)
    {
        fight ??= Current;
        var cc = Compiled(u);
        var s = u.Base + TierBonus(u.Tier) + u.Buff + (f?.Temp ?? default);
        if (slot >= 0) s += p.Slots[slot].Buff;
        var hoard = cc.Hoard + u.ExtraHoard;

        foreach (var h in Active(p, fight))
        {
            var a = new AuraCtx(this, p, h.H, h.Slot, u, slot, fight);
            foreach (var aura in h.C.UnitAuras) s += Scale(p, h, aura(a));
            if (slot >= 0) foreach (var aura in h.C.SlotAuras) s += Scale(p, h, aura(p, slot));
            foreach (var grant in h.C.HoardGrants) hoard += grant(a);
            if (!h.C.Support.IsZero && h.Slot >= Board.FrontSlots && !ReferenceEquals(h.H, u) && Board.InFrontOf(h.Slot).Contains(slot))
            {
                var bonus = h.H.Improve;
                if (h.C.Card.HasFaction("Fae")) bonus += FaeBonus(p);
                if (h.C.IsCandy) bonus += Candy(p, h.C);
                s += h.C.Support.Improve(bonus);
                hoard += h.C.SupportHoard;
            }
        }

        var self = new AuraCtx(this, p, u, slot, u, slot, fight);
        foreach (var aura in cc.SelfAuras) s += aura(self);
        if (hoard > 0) s += Stats.One * (hoard * TreasureCount(p));
        return new Stats(Math.Max(0, s.Atk), s.Hp);
    }

    // ---------------------------------------------------------------- buffs

    /// <summary>Adds the owner's bonuses for this kind of buff source (spell, Fae, treasure, Candy, improvements).</summary>
    public Stats Modify(Ctx c, Stats s)
    {
        var p = c.Owner;
        var source = c.Source.Card;
        var bonus = ReferenceEquals(c.Holder.Card, source) ? c.Holder.Improve : default;
        if (source.IsSpell) bonus += SpellBonus(p);
        else if (source.IsTreasure) bonus += TreasureBonus(p);
        else if (source.IsCharacter)
        {
            bonus += p.CharacterBuffBonus;
            if (source.HasFaction("Fae")) bonus += FaeBonus(p);
        }
        if (c.Source.IsCandy || c.Holder is Unit { Candy: true }) bonus += Candy(p, c.Source);
        return s.Improve(bonus);
    }

    /// <summary>Permanent buff on a character. Buffs stay for the rest of the game.</summary>
    public void Buff(Ctx c, Unit u, Stats s)
    {
        s = Modify(c, s);
        var target = Compiled(u);
        if (target.Cursed) return;
        s *= target.BuffMultiplier;
        u.Buff += s;
        Log?.Add($"  {u.Card.Name} gets +{s.Atk}/+{s.Hp} ({c.Source.Card.Name}).");
    }

    public void BuffRaw(Unit u, Stats s)
    {
        if (!Compiled(u).Cursed) u.Buff += s;
    }

    /// <summary>Permanent buff on a board slot; it applies to whoever stands there.</summary>
    public void BuffSlot(Ctx c, int slot, Stats s)
    {
        if (slot is < 0 or >= Board.Size) return;
        s = Modify(c, s);
        c.Owner.Slots[slot].Buff += s;
        Log?.Add($"  {c.Owner.Name} slot {slot + 1} gets +{s.Atk}/+{s.Hp} ({c.Source.Card.Name}).");
        Fire(c.Owner, Trigger.SlotBuffed, targetSlot: slot);
    }

    public void BuffSlotRaw(Player p, int slot, Stats s)
    {
        if (slot is >= 0 and < Board.Size) p.Slots[slot].Buff += s;
    }

    // ---------------------------------------------------------------- triggers

    private int ExtraTriggers(Player p, Trigger t)
    {
        var n = 0;
        foreach (var h in Active(p)) n += h.C.ExtraTriggers.GetValueOrDefault(t);
        return n;
    }

    private int Run(Player p, Holder h, CompiledCard cc, int slot, Fighter? f, Trigger t,
        Unit? it = null, Fighter? itF = null, int amount = 0, int targetSlot = -1)
    {
        var ran = 0;
        foreach (var ability in cc.Abilities)
            if (ability.Trigger == t) ran += RunOne(p, h, cc, ability, slot, f, it, itF, amount, targetSlot);
        return ran;
    }

    private int RunOne(Player p, Holder h, CompiledCard source, Ability ability, int slot, Fighter? f,
        Unit? it, Fighter? itF, int amount, int targetSlot)
    {
        if (depth >= MaxEffectDepth) return 0;
        var c = NewCtx(p, h, source, slot, f, it, itF, amount, targetSlot);
        if (ability.When is not null && !ability.When(c)) return 0;
        depth++;
        try { ability.Run(c); }
        finally { depth--; }
        return 1;
    }

    private Ctx NewCtx(Player p, Holder h, CompiledCard source, int slot, Fighter? f, Unit? it, Fighter? itF, int amount, int targetSlot) => new()
    {
        G = this, Owner = p, Holder = h, Source = source, Fight = Current, MeF = f, MeSlot = slot,
        It = it, ItF = itF, Amount = amount, TargetSlot = targetSlot,
    };

    /// <summary>Fires a trigger on everything the player has in play.</summary>
    public void Fire(Player p, Trigger t, Unit? it = null, Fighter? itF = null, int amount = 0, int targetSlot = -1)
    {
        if (depth >= MaxEffectDepth) return;
        var repeats = t == Trigger.FightStart ? 1 + ExtraTriggers(p, t) : 1;
        foreach (var h in Active(p).ToList())
        {
            if (h.F is { Alive: false }) continue;
            for (var i = 0; i < repeats; i++) Run(p, h.H, h.C, h.Slot, h.F, t, it, itF, amount, targetSlot);
            if (h.H is Unit u) CheckQuest(p, u, h.C, h.Slot, h.F, t, it, itF, amount, targetSlot);
        }
    }

    /// <summary>Fires a trigger on one character (its own abilities plus any granted to it). Returns how many ran.</summary>
    public int FireSelf(Player p, Unit u, int slot, Fighter? f, Trigger t, Unit? it = null, Fighter? itF = null, int amount = 0)
    {
        if (depth >= MaxEffectDepth) return 0;
        var cc = Compiled(u);
        var granted = new List<(Ability Ability, CompiledCard Source)>();
        foreach (var h in Active(p).ToList())
            foreach (var g in h.C.Granted)
                if (g.Ability.Trigger == t && g.AppliesTo(new AuraCtx(this, p, h.H, h.Slot, u, slot, Current)))
                    granted.Add((g.Ability, h.C));

        var ran = 0;
        var repeats = 1 + (cc.Abilities.Any(a => a.Trigger == t) || granted.Count > 0 ? ExtraTriggers(p, t) : 0);
        for (var i = 0; i < repeats; i++)
        {
            ran += Run(p, u, cc, slot, f, t, it, itF, amount);
            foreach (var (ability, source) in granted) ran += RunOne(p, u, source, ability, slot, f, it, itF, amount, -1);
        }
        if (slot >= 0) CheckQuest(p, u, cc, slot, f, t, it, itF, amount, -1);
        return ran;
    }

    private void CheckQuest(Player p, Unit u, CompiledCard cc, int slot, Fighter? f, Trigger t, Unit? it, Fighter? itF, int amount, int targetSlot)
    {
        if (cc.Quest is not { } quest || u.QuestDone || Array.IndexOf(quest.Triggers, t) < 0) return;
        if (!quest.Step(NewCtx(p, u, cc, slot, f, it, itF, amount, targetSlot))) return;
        u.QuestDone = true;
        p.Inc("quests");
        Say($"{p.Name}'s {u.Card.Name} completes its quest.");
        DiscoverTreasure(p, u.Card.RarityLevel, u);
        Fire(p, Trigger.QuestCompleted, it: u);
    }

    // ---------------------------------------------------------------- summons

    /// <summary>Creates a character mid-fight. Outside a fight, or with a full board, the summon fails.</summary>
    public Fighter? Summon(Ctx c, Card card, Stats? stats)
    {
        if (Current is not { } fight) return null;
        var p = c.Owner;
        if (!p.Units.TryGetValue(card.Cid, out var unit))
            p.Units[card.Cid] = unit = new Unit { Card = card, BaseOverride = stats };
        else if (fight.Entries(p.Index).Any(e => ReferenceEquals(e.Unit, unit)))
            unit = new Unit { Card = card, BaseOverride = unit.BaseOverride ?? stats, Buff = unit.Buff, Tier = unit.Tier };

        var prefer = c.MeF is { Alive: false } ? c.MeSlot : -1;
        var fighter = fight.Summon(p.Index, unit, prefer);
        if (fighter is null) return null;
        Say($"  {p.Name} summons {card.Name} {fight.Stats(fighter)} into slot {fighter.Slot + 1}.");
        c.It = unit;
        c.ItF = fighter;
        Fire(p, Trigger.Summon, it: unit, itF: fighter);
        return fighter;
    }

    public Fighter? SummonRandom(Ctx c, Func<Card, bool> filter, bool ignoreShopLevel = false)
    {
        var level = ShopLevel(c.Owner);
        var options = c.Owner.CharacterPool.Where(card => filter(card) && (ignoreShopLevel || card.RarityLevel <= level)).ToList();
        return options.Count == 0 ? null : Summon(c, options[Rng.Next(options.Count)], null);
    }
}
