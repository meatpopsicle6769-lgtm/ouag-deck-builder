using System.Text.RegularExpressions;
using Galaxy.Core.Cards;
using Galaxy.Core.Engine;

namespace Galaxy.Core.Abilities;

public sealed partial class AbilityCompiler
{
    /// <summary>Compiles an effect sentence (or several joined by ". ", "then", "and"). Null if nothing matched.</summary>
    private Action<Ctx>? Effect(string body, out bool full)
    {
        full = true;
        var actions = new List<Action<Ctx>>();
        foreach (var raw in Regex.Split(body, @"\. (?:then )?|, then |; | then (?=give|get|summon|sweeten|improve|double)"))
        {
            var part = Regex.Replace(raw.Trim().TrimEnd('.'), "^then ", "");
            if (part.Length == 0) continue;
            if (Part(part) is { } one) { actions.Add(one); continue; }

            // "X and Y": accept only if at least one side is understood.
            var halves = Regex.Split(part, " and (?=give|get|summon|sweeten|cast|discover|improve|deal|trigger|paint|then)");
            var any = false;
            foreach (var half in halves.Length > 1 ? halves : [])
            {
                if (Part(Regex.Replace(half, "^then ", "")) is { } h) { actions.Add(h); any = true; }
                else full = false;
            }
            if (!any) full = false;
        }
        if (actions.Count == 0) return null;
        return actions.Count == 1 ? actions[0] : c => { foreach (var a in actions) a(c); };
    }

    private Action<Ctx>? Part(string p)
    {
        Match m;
        if (p.StartsWith("you get ")) p = p[4..];

        if ((m = Regex.Match(p, $@"^improve (?:this|this ability|this buff|this bonus)(?: (?:buff|ability|bonus))? by {STAT}$")).Success)
        {
            var s = ParseStat(m.Groups[1].Value);
            cc.UsesImprove = true;
            return c => c.Holder.Improve += s;
        }

        // Stat buffs.
        if ((m = Regex.Match(p, $@"^give (.+?) {STAT}$")).Success && Target(m.Groups[1].Value) is { } give)
        { var s = ParseStat(m.Groups[2].Value); return c => give(c, s); }
        if ((m = Regex.Match(p, $@"^(.+?) (?:gets?|gains?|has|have) {STAT}$")).Success && Target(m.Groups[1].Value) is { } gets)
        { var s = ParseStat(m.Groups[2].Value); return c => gets(c, s); }
        if ((m = Regex.Match(p, $@"^{STAT}$")).Success)
        { var s = ParseStat(m.Groups[1].Value); return c => c.Buff(c.Me, s); }

        if (p is "double my stats" or "tribble my stats")
        {
            var k = p.StartsWith("double") ? 1 : 2;
            return c => { if (c.Me is { } me) c.G.BuffRaw(me, c.G.StatsOf(c.Owner, me, c.MeSlot, c.Fight, c.MeF) * k); };
        }
        if (p is "double my attack" or "triple my attack")
        {
            var k = p.StartsWith("double") ? 1 : 2;
            return c => { if (c.Me is { } me) c.G.BuffRaw(me, new Stats(c.G.StatsOf(c.Owner, me, c.MeSlot, c.Fight, c.MeF).Atk * k, 0)); };
        }
        if (p == "double the attack of your characters")
            return c => { foreach (var e in c.Mine.ToList()) if (e.Fighter is { } f) f.Temp += new Stats(c.G.StatsOf(c.Owner, e.Unit, e.Slot, c.Fight, f).Atk, 0); };
        if (p == "double the attack and health of the next character you recruit")
            return c => c.Owner.NextRecruitMultiplier *= 2;

        // Candy, shops, hearts.
        if ((m = Regex.Match(p, @"^sweeten your candy(?: (twice|\d+)(?: times)?)?$")).Success)
        { var n = m.Groups[1].Success ? Number(m.Groups[1].Value) : 1; return c => c.G.Sweeten(c.Owner, n); }
        if ((m = Regex.Match(p, @"^get (\w+) (?:bonus|new) shops?$")).Success)
        { var n = Number(m.Groups[1].Value); return c => c.Owner.BonusShops += n; }
        if (p == "break a heart") return c => c.G.CrackHeart(c.Owner);
        if (p is "repair a cracked heart" or "heal a cracked heart") return c => c.G.HealHeart(c.Owner);
        if (p == "gain an additional heart") return c => { c.Owner.Hearts++; c.Owner.MaxHearts++; };
        if (p == "you attack first this fight") return c => c.Owner.AttacksFirstNextFight = true;

        // Treasures.
        if ((m = Regex.Match(p, $@"^get (\w+) random (?:({RARITY}) )?treasures?$")).Success)
        {
            var n = Number(m.Groups[1].Value);
            var level = m.Groups[2].Success ? RarityLevel(m.Groups[2].Value) : -1;
            return c => { for (var i = 0; i < n; i++) c.G.GainRandomTreasure(c.Owner, level); };
        }
        if ((m = Regex.Match(p, $@"^discover an? (?:({RARITY}) )?treasure(?: in addition to the bonus shop| as well| instead| of your shop's level)?$")).Success)
        {
            var level = m.Groups[1].Success ? RarityLevel(m.Groups[1].Value) : -1;
            return c => c.G.DiscoverTreasure(c.Owner, level >= 0 ? level : c.Me?.Card.RarityLevel ?? c.G.ShopLevel(c.Owner), c.Me);
        }

        // Summons (fight only).
        if ((m = Regex.Match(p, @"^summon (\w+) (\d+)/(\d+) (.+?)$")).Success && Token(m.Groups[4].Value) is { } tokens)
        {
            var n = Number(m.Groups[1].Value);
            var stats = new Stats(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            return c => { for (var i = 0; i < n; i++) c.G.Summon(c, tokens[i % tokens.Count], stats); };
        }
        if ((m = Regex.Match(p, @"^summon (a|two|three) random (\w+?)(?: characters?)?$")).Success)
        {
            var n = Number(m.Groups[1].Value);
            var word = m.Groups[2].Value;
            if (Faction(word) is { } type) return c => { for (var i = 0; i < n; i++) c.G.SummonRandom(c, card => card.HasFaction(type)); };
            if (RarityLevel(word) is >= 0 and var level) return c => { for (var i = 0; i < n; i++) c.G.SummonRandom(c, card => card.RarityLevel == level, ignoreShopLevel: true); };
            return null;
        }
        if ((m = Regex.Match(p, @"^summon (.+)$")).Success && cards.Find(m.Groups[1].Value) is { IsCharacter: true } named)
            return c => c.G.Summon(c, named, null);

        // Next shop.
        if ((m = Regex.Match(p, @"^your next shop is (?:of )?all (\w+?)(?: characters)?$")).Success)
        {
            var word = m.Groups[1].Value;
            if (Faction(word) is { } type) return c => c.Owner.NextShopFaction = type;
            if (RarityLevel(word) is >= 0 and var level) return c => c.Owner.NextShopMaxRarity = level;
            return null;
        }

        // Spells.
        if (p == "cast a random spell") return c => c.G.CastRandomSpell(c.Owner, null);
        if (p is "get a candy spell" or "get a bonus candy spell") return c => c.G.CastRandomSpell(c.Owner, s => s.HasFaction("Candy"));
        if (p is "get a random spell" or "cast a spell") return c => c.G.CastRandomSpell(c.Owner, null);
        if ((m = Regex.Match(p, @"^(?:get|cast) (?:an? )?(.+)$")).Success && FindNamed(m.Groups[1].Value) is { } gained)
            return gained.IsSpell ? c => c.G.CastSpell(c.Owner, gained) : c => c.G.GainTreasure(c.Owner, gained);
        if ((m = Regex.Match(p, @"^deal (\d+) damage to an enemy$")).Success)
        {
            var n = int.Parse(m.Groups[1].Value);
            cc.Combat = true;
            return c => c.Fight?.Damage(c.Pick(c.Theirs.Where(e => e.Fighter is not null).ToList()).Fighter, n, c.MeF);
        }
        if (p == "shrink the highest attack enemy to 1 health")
        {
            cc.Combat = true;
            return c =>
            {
                var target = c.Theirs.Where(e => e.Fighter is not null).OrderByDescending(e => c.Fight!.Stats(e.Fighter!).Atk).FirstOrDefault().Fighter;
                if (target is not null) c.Fight!.SetHealth(target, 1);
            };
        }
        if (p == "ko the character that ko'd me")
            return c => { if (c.ItF is { Alive: true } killer) c.Fight?.Kill(killer, null); };

        // Buff-source improvements.
        if ((m = Regex.Match(p, $@"^improve (?:your buffs from|the buffs from your|your) (spells|treasures|characters|treasure buffs) by {STAT}(?: for the rest of the game)?$")).Success
            || (m = Regex.Match(p, $@"^(?:your )?buffs from (?:your )?(fae|spells) give an additional {STAT}(?: for the rest of the game)?$")).Success)
        {
            var s = ParseStat(m.Groups[2].Value);
            return m.Groups[1].Value switch
            {
                "spells" => c => c.Owner.SpellBuffBonus += s,
                "fae" => c => c.Owner.FaeBuffBonus += s,
                "characters" => c => c.Owner.CharacterBuffBonus += s,
                _ => c => c.Owner.TreasureBuffBonus += s,
            };
        }

        // Paint.
        if ((m = Regex.Match(p, $@"^paint a (random )?slot ({COLORS}|a random color)$")).Success)
        {
            var color = Color(m.Groups[2].Value);
            var random = m.Groups[1].Success;
            if (!random) cc.Targeted = cc.Card.IsSpell;
            return c =>
            {
                var paint = color != Paint.None ? color : c.Pick(new[] { Paint.Red, Paint.Blue, Paint.Yellow });
                var slot = !random && c.TargetSlot >= 0 ? c.TargetSlot : c.G.Rng.Next(Board.Size);
                c.G.PaintSlot(c.Owner, slot, paint);
            };
        }

        // Keywords.
        if ((m = Regex.Match(p, @"^give a character treasure hoard(?: (\d+))?$")).Success)
        {
            var n = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1;
            cc.Targeted = true;
            return c => { if (c.It is { } u) u.ExtraHoard += n; };
        }
        if ((m = Regex.Match(p, @"^turn a (bronze|silver) character (silver|golden) and give it " + STAT + "$")).Success)
        {
            var from = m.Groups[1].Value == "bronze" ? Tier.Bronze : Tier.Silver;
            var s = ParseStat(m.Groups[3].Value);
            cc.Targeted = true;
            return c => { if (c.It is { } u && u.Tier == from) { c.G.Promote(c.Owner, u, viaShop: false); c.Buff(u, s); } };
        }
        return More(p);
    }

    private Card? FindNamed(string name)
    {
        name = name.Trim();
        var card = cards.Find(name) ?? cards.Find(name.Replace("dragon claw", "dragon's claw")) ?? cards.Find(name.Replace("dragon's claw", "dragon claw"));
        return card is not null && (card.IsSpell || card.IsTreasure) ? card : null;
    }

    /// <summary>Token cards by the name used in summon text ("Bot Pilot", "Monkeys").</summary>
    private List<Card>? Token(string name)
    {
        name = name.Trim();
        if (cards.Find(name) is { IsCharacter: true } exact) return [exact];
        var stem = name.TrimEnd('s');
        var list = cards.Characters.Where(c => c.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) && c.Pool == "token").ToList();
        return list.Count > 0 ? list : null;
    }

    // ---------------------------------------------------------------- who a buff goes to

    private Action<Ctx, Stats>? Target(string t)
    {
        t = t.Trim();
        static void Units(Ctx c, Stats s, IEnumerable<Entry> entries) { foreach (var e in entries.ToList()) c.Buff(e.Unit, s); }
        static void Slots(Ctx c, Stats s, IEnumerable<int> slots) { foreach (var i in slots.ToList()) c.BuffSlot(i, s); }

        switch (t)
        {
            case "me": case "i":
                return (c, s) => c.Buff(c.Me, s);
            case "my slot":
                return (c, s) => c.BuffSlot(c.MeSlot, s);
            case "its slot":
                return (c, s) => c.BuffSlot(c.ItSlot, s);
            case "me and my neighbors":
                return (c, s) => Units(c, s, c.Mine.Where(e => e.Slot == c.MeSlot || Board.Neighbors(c.MeSlot).Contains(e.Slot)));
            case "my neighbors":
                return (c, s) => Units(c, s, c.Mine.Where(e => Board.Neighbors(c.MeSlot).Contains(e.Slot)));
            case "your characters": case "each of your characters": case "all your characters": case "all of your characters":
            case "me and your other characters":
                return (c, s) => Units(c, s, c.Mine);
            case "your other characters": case "the rest of your characters":
                return (c, s) => Units(c, s, c.Mine.Where(e => !ReferenceEquals(e.Unit, c.Me)));
            case "your slots": case "each of your slots": case "all your slots": case "all of your slots":
                return (c, s) => Slots(c, s, Enumerable.Range(0, Board.Size));
            case "all three front row slots": case "your front row slots":
                return (c, s) => Slots(c, s, Enumerable.Range(0, Board.FrontSlots));
            case "both your back slots": case "your back row slots":
                return (c, s) => Slots(c, s, Enumerable.Range(Board.FrontSlots, Board.Size - Board.FrontSlots));
            case "your first slot":
                return (c, s) => c.BuffSlot(0, s);
            case "a random slot": case "one of your slots":
                return (c, s) => c.BuffSlot(c.G.Rng.Next(Board.Size), s);
            case "two random slots":
                return (c, s) => Slots(c, s, Enumerable.Range(0, Board.Size).OrderBy(_ => c.G.Rng.Next()).Take(2));
            case "your shop": case "characters in your shop": case "the characters in your shop":
                return (c, s) => c.Owner.NextShopBuff += c.G.Modify(c, s);
            case "it": case "them": case "that character":
                return (c, s) => c.Buff(c.It, s);
            case "it and me": case "both of us":
                return (c, s) => { c.Buff(c.It, s); c.Buff(c.Me, s); };
            case "the characters in front of me": case "characters in front of me":
                return (c, s) => Units(c, s, c.Mine.Where(e => Board.InFrontOf(c.MeSlot).Contains(e.Slot)));
            case "the characters behind me":
                return (c, s) => Units(c, s, c.Mine.Where(e => Board.Behind(c.MeSlot).Contains(e.Slot)));
            case "a character":
                cc.Targeted = true;
                return (c, s) => c.Buff(c.It, s);
            case "a slot":
                cc.Targeted = true;
                return (c, s) => c.BuffSlot(c.TargetSlot, s);
            case "a random character":
                return (c, s) => c.Buff(c.Pick(c.Mine.ToList()).Unit, s);
            case "another one of your characters": case "another character":
                return (c, s) => c.Buff(c.Pick(c.Mine.Where(e => !ReferenceEquals(e.Unit, c.Me)).ToList()).Unit, s);
            case "me and another one of your characters":
                return (c, s) => { c.Buff(c.Me, s); c.Buff(c.Pick(c.Mine.Where(e => !ReferenceEquals(e.Unit, c.Me)).ToList()).Unit, s); };
            case "it an additional": case "it an extra":
                return (c, s) => c.G.BuffSlotRaw(c.Owner, c.TargetSlot, s);
            case "enemy characters":
                return (c, s) => { foreach (var e in c.Theirs.ToList()) c.G.BuffRaw(e.Unit, s); };
            case "the next character you recruit":
                return (c, s) => c.Owner.NextRecruitBuff += c.G.Modify(c, s);
        }

        Match m;
        if ((m = Regex.Match(t, @"^(?:all )?(?:of )?your (\w+) everywhere$")).Success && Faction(m.Groups[1].Value) is { } everywhere)
            return (c, s) => { foreach (var u in c.Owner.Units.Values.Where(u => c.Is(u, everywhere)).ToList()) c.Buff(u, s); };
        if ((m = Regex.Match(t, @"^your (\w+?)s?' slots$")).Success && Faction(m.Groups[1].Value) is { } slotType)
            return (c, s) => Slots(c, s, c.Mine.Where(e => c.Is(e.Unit, slotType)).Select(e => e.Slot));
        if ((m = Regex.Match(t, @"^one of your (\w+)$")).Success && Faction(m.Groups[1].Value) is { } oneOf)
            return (c, s) => c.Buff(c.Pick(c.Mine.Where(e => c.Is(e.Unit, oneOf)).ToList()).Unit, s);
        if ((m = Regex.Match(t, @"^(?:all )?(?:of )?your (\w+?)(?: characters)?$")).Success && Faction(m.Groups[1].Value) is { } type)
            return (c, s) => Units(c, s, c.Mine.Where(e => c.Is(e.Unit, type)));
        return MoreTargets(t);
    }
}
