using System.Text.RegularExpressions;
using Galaxy.Core.Cards;
using Galaxy.Core.Engine;

namespace Galaxy.Core.Abilities;

/// <summary>Second batch of text patterns: counters, turn schedules, transforms and less common targets.</summary>
public sealed partial class AbilityCompiler
{
    /// <summary>Condition that is true every <paramref name="every"/>-th time it is checked (state lives on the holder).</summary>
    private static Func<Ctx, bool> EveryNth(string key, int every, Func<Ctx, bool>? counts = null) =>
        c => (counts is null || counts(c)) && c.Holder.Inc(key) % Math.Max(1, every) == 0;

    private static readonly (string Pattern, Func<Match, (Trigger?, Func<Ctx, bool>?, string)> Build)[] MoreTriggerRules =
    [
        (@"^when you lose or tie a fight,? (.+)$", m => T(Trigger.FightNotWon, m.Groups[1].Value)),
        (@"^if you have fewer characters than the enemy when i attack,? (?:first )?(.+)$",
            m => T(Trigger.Attack, m.Groups[1].Value, c => c.Mine.Count() < c.Theirs.Count())),
        (@"^when you cast your second spell (?:each|in a) turn,? (.+)$",
            m => T(Trigger.SpellCast, m.Groups[1].Value, c => c.Owner.Count("spellsThisTurn") == 2)),
        (@"^when you cast a spell directly on a character,? (.+)$", m => T(Trigger.SpellCast, m.Groups[1].Value, c => c.It is not null)),
        (@"^when(?:ever)? you discover a treasure,? (.+)$", m => T(Trigger.TreasureDiscovered, m.Groups[1].Value)),
        (@"^when you (?:collect|get) your first treasure each turn,? (.+)$",
            m => T(Trigger.TreasureCollected, m.Groups[1].Value, c => c.Owner.Count("treasuresThisTurn") == 1)),
        (@"^on turn (\w+),? (.+)$", m => { var n = Number(m.Groups[1].Value); return T(Trigger.TurnStart, m.Groups[2].Value, c => c.G.Turn == n); }),
        (@"^starting on turn (\w+),? (.+?)(?: each turn)?$",
            m => { var n = Number(m.Groups[1].Value); return T(Trigger.TurnStart, m.Groups[2].Value, c => c.G.Turn >= n); }),
        (@"^every other turn,? (.+)$", m => T(Trigger.TurnStart, m.Groups[1].Value, c => c.G.Turn % 2 == 0)),
        (@"^every (\d+) turns,? (.+)$", m => { var n = Number(m.Groups[1].Value); return T(Trigger.TurnStart, m.Groups[2].Value, c => c.G.Turn % n == 0); }),
        (@"^(.+) every (\d+) turns$", m => { var n = Number(m.Groups[2].Value); return T(Trigger.TurnStart, m.Groups[1].Value, c => c.G.Turn % n == 0); }),
        (@"^you (get a bonus shop) when you level up$", m => T(Trigger.TurnStart, m.Groups[1].Value, c => c.G.LeveledUpThisTurn)),
        (@"^when one of your hearts (?:is|are) emptied,? (.+)$", m => T(Trigger.HeartLost, m.Groups[1].Value)),
        (@"^cast (\w+) spells to (.+)$", m => T(Trigger.SpellCast, m.Groups[2].Value, EveryNth("spells", Number(m.Groups[1].Value)))),
        (@"^every (\d+) cards you pick,? (.+)$", m => T(Trigger.Pick, m.Groups[2].Value, EveryNth("picks", Number(m.Groups[1].Value)))),
        (@"^(?:every|after you recruit) (\d+) (\w+)(?: you (?:pick|recruit))?,? (.+)$", m => KnownType(m.Groups[2].Value)
            ? T(Trigger.RecruitOther, m.Groups[3].Value, EveryNth("recruits", Number(m.Groups[1].Value), TypeIs(m.Groups[2].Value))) : NoMatch),
        (@"^after (\w+) of your (\w+) are ko'd,? (.+)$", m => KnownType(m.Groups[2].Value)
            ? T(Trigger.AllyKO, m.Groups[3].Value, EveryNth("kos", Number(m.Groups[1].Value), TypeIs(m.Groups[2].Value))) : NoMatch),
        (@"^the first time (?:that )?you recruit an? (\w+) each turn,? (.+)$", m => KnownType(m.Groups[1].Value)
            ? T(Trigger.RecruitOther, m.Groups[2].Value, And(TypeIs(m.Groups[1].Value), c =>
            {
                if (c.Holder.Count("firstRecruitTurn") == c.G.Turn) return false;
                c.Holder.Counters["firstRecruitTurn"] = c.G.Turn;
                return true;
            })) : NoMatch),
    ];

    private bool MorePassive(string line)
    {
        Match m;
        if (Regex.IsMatch(line, @"^when you sweeten your candy, sweeten it (?:an extra time|again|twice)$")) { cc.ExtraSweeten = 1; return true; }
        if (line == "i don't take damage from the first hit each fight") { cc.FirstHitImmune = true; return true; }
        if ((m = Regex.Match(line, @"^your (\w+?)(?: characters)? have (ranged|sneak)$")).Success && UnitFilter(m.Groups[1].Value) is { } who)
        {
            cc.KeywordGrants.Add((who, m.Groups[2].Value));
            return true;
        }
        if ((m = Regex.Match(line, $@"^your (\w+) have {STAT} for each (\w+) you have$")).Success
            && Faction(m.Groups[1].Value) is { } type && Faction(m.Groups[3].Value) is { } counted)
        {
            var s = ParseStat(m.Groups[2].Value);
            cc.UnitAuras.Add(a => a.G.HasType(a.P, a.U, type) ? s * a.G.BoardOf(a.P, a.Fight).Count(e => a.G.HasType(a.P, e.Unit, counted)) : default);
            return true;
        }
        if ((m = Regex.Match(line, @"^your (\w+) are also (.+)$")).Success && Faction(m.Groups[1].Value) is { } from)
        {
            var added = Regex.Split(m.Groups[2].Value, @",? and |, ").Select(Faction).Where(f => f is not null).ToList();
            if (added.Count == 0) return false;
            foreach (var to in added) cc.TypeGrants.Add((from, to!));
            return true;
        }
        return false;
    }

    private static Func<Ctx, int>? CountFor(string what)
    {
        var m = Regex.Match(what, @"^(\w+) you have$");
        if (m.Success && Faction(m.Groups[1].Value) is { } type) return c => c.Mine.Count(e => c.Is(e.Unit, type));
        return ForEach(what) is { } perPlayer ? c => perPlayer(c.Owner) : null;
    }

    private static Stats Current(Ctx c, Unit? u, Fighter? f, int slot) =>
        u is null ? default : c.G.StatsOf(f?.Owner ?? c.Owner, u, slot, c.Fight, f);

    private Action<Ctx>? More(string p)
    {
        Match m;

        // Conditions wrapped around another effect.
        if ((m = Regex.Match(p, @"^if it was an? (\w+), (.+)$")).Success && Faction(m.Groups[1].Value) is { } wasType && Part(m.Groups[2].Value) is { } ifType)
            return c => { if (c.Is(c.It, wasType)) ifType(c); };
        if ((m = Regex.Match(p, @"^if you lost your last fight, (.+)$")).Success && Part(m.Groups[1].Value) is { } ifLost)
            return c => { if (c.Owner.Count("lostLast") > 0) ifLost(c); };
        if ((m = Regex.Match(p, @"^if i haven't attacked this fight, (.+)$")).Success && Part(m.Groups[1].Value) is { } ifIdle)
            return c => { if (c.MeF is { Attacks: 0 }) ifIdle(c); };

        // Stat buffs with a computed size.
        if ((m = Regex.Match(p, $@"^give (.+?) {STAT} for each (.+)$")).Success && Target(m.Groups[1].Value) is { } perTarget && CountFor(m.Groups[3].Value) is { } count)
        { var s = ParseStat(m.Groups[2].Value); return c => { var n = count(c); if (n > 0) perTarget(c, s * n); }; }
        if ((m = Regex.Match(p, $@"^give {STAT} to (.+)$")).Success && Target(m.Groups[2].Value) is { } to)
        { var s = ParseStat(m.Groups[1].Value); return c => to(c, s); }
        if ((m = Regex.Match(p, @"^give (.+?) (double )?my (?:attack and health|stats)$")).Success && Target(m.Groups[1].Value) is { } mine)
        { var k = m.Groups[2].Success ? 2 : 1; return c => mine(c, Current(c, c.Me, c.MeF, c.MeSlot) * k); }
        if ((m = Regex.Match(p, @"^gain (double )?the stats of the ko'd character$")).Success)
        { var k = m.Groups[1].Success ? 2 : 1; return c => c.Buff(c.Me, Current(c, c.It, c.ItF, c.ItF?.Slot ?? -1) * k); }
        if (p == "i gain attack equal to the attacker's current health")
            return c => { if (c.ItF is { } a && c.Me is { } me && c.Fight is { } fight) c.G.BuffRaw(me, new Stats(Math.Max(0, fight.Health(a)), 0)); };
        if (p == "my attack and health become the higher of the two")
            return c =>
            {
                if (c.Me is not { } me) return;
                var s = Current(c, me, c.MeF, c.MeSlot);
                var top = Math.Max(s.Atk, s.Hp);
                c.G.BuffRaw(me, new Stats(top - s.Atk, top - s.Hp));
            };
        if (p == "double its buff")
            return c => { if (c.TargetSlot >= 0) c.G.BuffSlotRaw(c.Owner, c.TargetSlot, c.Owner.Slots[c.TargetSlot].Buff); };
        if ((m = Regex.Match(p, $@"^give your shop {STAT} for the rest of the game$")).Success)
        { var s = ParseStat(m.Groups[1].Value); return c => c.Owner.ShopAura += c.G.Modify(c, s); }

        // Fight effects.
        if (p == "i deal a million bonus damage")
            return c => { if (c.MeF is { } f && f.Temp.Atk < 1_000_000) f.Temp += new Stats(1_000_000, 0); };
        if ((m = Regex.Match(p, @"^deal (\d+) damage to each enemy$")).Success)
        {
            var n = int.Parse(m.Groups[1].Value);
            return c => { foreach (var e in c.Theirs.ToList()) c.Fight?.Damage(e.Fighter, n, c.MeF); };
        }
        if (p == "reduce my attacker's health to 1") return c => { if (c.ItF is { Alive: true } a) c.Fight?.SetHealth(a, 1); };
        if (p == "summon the ko'd character") return c => { if (c.It is { } prey) c.G.Summon(c, prey.Card, null); };
        if (p == "summon a copy of me") return c => { if (c.Me is { } me) c.G.Summon(c, me.Card, null); };
        if (p == "ko the largest enemy character and the smallest friendly character")
            return c =>
            {
                if (c.Fight is not { } fight) return;
                var foe = c.Theirs.OrderByDescending(e => fight.Stats(e.Fighter!).Total).FirstOrDefault().Fighter;
                var friend = c.Mine.OrderBy(e => fight.Stats(e.Fighter!).Total).FirstOrDefault().Fighter;
                if (foe is not null) fight.Kill(foe, null);
                if (friend is not null) fight.Kill(friend, null);
            };

        // Treasures and characters gained.
        if ((m = Regex.Match(p, @"^get a (?:random )?treasure of (?:my prey's|the attacker's|their) rarity$")).Success)
            return c => { if (c.It is { } other) c.G.GainRandomTreasure(c.Owner, Math.Max(0, other.Card.RarityLevel)); };
        if ((m = Regex.Match(p, $@"^get an? ({RARITY}) treasure(?: also| as well)?$")).Success)
        { var level = RarityLevel(m.Groups[1].Value); return c => c.G.GainRandomTreasure(c.Owner, level); }
        if ((m = Regex.Match(p, $@"^your opponent gets an? ({RARITY}) treasure$")).Success)
        { var level = RarityLevel(m.Groups[1].Value); return c => c.G.GainRandomTreasure(c.Enemy, level); }
        if ((m = Regex.Match(p, @"^get (?:an|two) additional (.+?)s?$")).Success && FindNamed(m.Groups[1].Value) is { IsTreasure: true } extra)
        { var n = p.StartsWith("get two") ? 2 : 1; return c => { for (var i = 0; i < n; i++) c.G.GainTreasure(c.Owner, extra); }; }
        if ((m = Regex.Match(p, @"^(?:you )?start with (.+)$")).Success && FindNamed(m.Groups[1].Value) is { IsTreasure: true } starting)
            return c => c.G.GainTreasure(c.Owner, starting);
        if (p == "get a random character of your shop's rarity")
            return c => c.It = c.G.GainRandomCharacter(c.Owner, c.G.ShopLevel(c.Owner));

        // Transforms.
        if ((m = Regex.Match(p, @"^transform (a character|me) into a random (?:bronze )?character (one rarity higher|of your shop's rarity)$")).Success)
        {
            var self = m.Groups[1].Value == "me";
            var higher = m.Groups[2].Value.StartsWith("one");
            if (!self) cc.Targeted = true;
            return c =>
            {
                var u = self ? c.Me : c.It;
                if (u is not null) c.G.TransformRandom(c.Owner, u, higher ? u.Card.RarityLevel + 1 : c.G.ShopLevel(c.Owner));
            };
        }
        if ((m = Regex.Match(p, @"^i transform into a character (one rarity|two rarities) higher than your shop$")).Success)
        {
            var up = m.Groups[1].Value.StartsWith("one") ? 1 : 2;
            return c => { if (c.Me is { } me) c.G.TransformRandom(c.Owner, me, c.G.ShopLevel(c.Owner) + up); };
        }
        if ((m = Regex.Match(p, $@"^transform me into a random ({RARITY}) character$")).Success)
        {
            var level = RarityLevel(m.Groups[1].Value);
            return c => { if (c.Me is { } me) c.G.TransformRandom(c.Owner, me, level); };
        }
        return null;
    }

    private Action<Ctx, Stats>? MoreTargets(string t)
    {
        static void Units(Ctx c, Stats s, IEnumerable<Entry> entries) { foreach (var e in entries.ToList()) c.Buff(e.Unit, s); }
        Match m;
        switch (t)
        {
            case "me and a random other character":
                return (c, s) => { c.Buff(c.Me, s); c.Buff(c.Pick(c.Mine.Where(e => !ReferenceEquals(e.Unit, c.Me)).ToList()).Unit, s); };
            case "me and my prey":
                return (c, s) => { c.Buff(c.Me, s); if (c.It is { } prey) c.G.BuffRaw(prey, s); };
            case "the slots behind it":
                return (c, s) => { foreach (var i in Board.Behind(c.ItSlot)) c.BuffSlot(i, s); };
            case "your smallest character":
                return (c, s) => c.Buff(c.Mine.OrderBy(e => Current(c, e.Unit, e.Fighter, e.Slot).Total).FirstOrDefault().Unit, s);
            case "your lowest attack character":
                return (c, s) => c.Buff(c.Mine.OrderBy(e => Current(c, e.Unit, e.Fighter, e.Slot).Atk).FirstOrDefault().Unit, s);
            case "a character and all of your characters that share a type with it":
                cc.Targeted = true;
                return (c, s) =>
                {
                    if (c.It is not { } it) return;
                    Units(c, s, c.Mine.Where(e => ReferenceEquals(e.Unit, it) || e.Unit.Card.Factions.Any(f => c.Is(it, f))));
                };
        }
        if ((m = Regex.Match(t, @"^your (silver|golden|bronze) characters(?: an additional)?$")).Success)
        {
            var tier = m.Groups[1].Value switch { "silver" => Tier.Silver, "golden" => Tier.Golden, _ => Tier.Bronze };
            return (c, s) => Units(c, s, c.Mine.Where(e => e.Unit.Tier == tier));
        }
        if ((m = Regex.Match(t, @"^your (\w+) and (\w+)$")).Success && Faction(m.Groups[1].Value) is { } a && Faction(m.Groups[2].Value) is { } b)
            return (c, s) => Units(c, s, c.Mine.Where(e => c.Is(e.Unit, a) || c.Is(e.Unit, b)));
        if ((m = Regex.Match(t, @"^(\w+) in your shop$")).Success && Faction(m.Groups[1].Value) is { } shopType)
            return (c, s) => c.Owner.NextShopTypedBuffs.Add((shopType, c.G.Modify(c, s)));
        return null;
    }
}
