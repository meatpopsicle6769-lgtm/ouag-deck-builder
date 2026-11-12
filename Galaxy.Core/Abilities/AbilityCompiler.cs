using System.Text.RegularExpressions;
using Galaxy.Core.Cards;
using Galaxy.Core.Engine;

namespace Galaxy.Core.Abilities;

/// <summary>
/// Turns a card's rules text into abilities by matching known sentence patterns.
/// Anything that matches no pattern is recorded in <see cref="CompiledCard.Unparsed"/> and does nothing.
/// </summary>
public sealed partial class AbilityCompiler
{
    private const string STAT = @"(\+\d+/\+\d+|\+\d+ attack|\+\d+ health)";
    private const string KEYWORDS = "recruit|farewell|hunt|chance encounter|quest|prophecy|treasure find|support";
    private const string COLORS = "red|blue|yellow";
    private const string RARITY = "common|uncommon|rare|epic|legendary|secret rare";

    private static readonly Dictionary<string, string> FactionWords = new()
    {
        ["animal"] = "Animal", ["animals"] = "Animal", ["hero"] = "Hero", ["heroes"] = "Hero", ["pirate"] = "Pirate",
        ["pirates"] = "Pirate", ["fae"] = "Fae", ["mage"] = "Mage", ["mages"] = "Mage", ["toy"] = "Toy", ["toys"] = "Toy",
        ["villain"] = "Villain", ["villains"] = "Villain", ["dragon"] = "Dragon", ["dragons"] = "Dragon",
        ["celestial"] = "Celestial", ["celestials"] = "Celestial", ["candy"] = "Candy", ["candies"] = "Candy",
        ["tree"] = "Tree", ["trees"] = "Tree", ["giant"] = "Giant", ["giants"] = "Giant", ["paintkin"] = "Paintkin",
        ["artist"] = "Artist", ["artists"] = "Artist",
    };

    private static readonly Dictionary<string, int> NumberWords = new()
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["twice"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
    };

    private readonly CardCatalog cards;
    private readonly CompiledCard cc;

    private AbilityCompiler(CardCatalog cards, Card card)
    {
        this.cards = cards;
        cc = new CompiledCard { Card = card };
    }

    public static CompiledCard Compile(CardCatalog cards, Card card, bool golden = false)
    {
        var compiler = new AbilityCompiler(cards, card);
        foreach (var line in Lines(card, golden))
        {
            var before = compiler.Size;
            if (compiler.Line(line, null)) compiler.cc.Parsed.Add(line);
            else if (compiler.Size > before) compiler.cc.PartlyParsed.Add(line);
            else compiler.cc.Unparsed.Add(line);
        }
        return compiler.cc;
    }

    private int Size => cc.Abilities.Count + cc.Granted.Count + cc.UnitAuras.Count + cc.ShopAuras.Count + cc.TypeGrants.Count + (cc.Quest is null ? 0 : 1);

    // ---------------------------------------------------------------- text preparation

    private static string Norm(string s)
    {
        s = s.ToLowerInvariant().Replace('’', '\'').Replace('“', '"').Replace('”', '"');
        s = Regex.Replace(s, @"\([^)]*\)", " ");
        s = s.Replace("--", "—").Replace('–', '—');
        s = Regex.Replace(s, @"\s+", " ").Replace(" ,", ",").Trim();
        return s.TrimEnd('.', ' ');
    }

    private static (string Prefix, string Body) SplitPrefix(string line)
    {
        var m = Regex.Match(line, $@"^((?:(?:{COLORS}) )?(?:{KEYWORDS})(?:(?:,| and|, and) (?:{KEYWORDS}))*)\s*(?:—|-|:)\s*(.*)$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : ("", line);
    }

    private static string Skeleton(string body) => Regex.Replace(Regex.Replace(body, @"\d+", "#"), @"[^a-z#]", "");

    /// <summary>The card's lines; for Golden copies, lines whose gold text has the same shape use the gold numbers.</summary>
    private static List<string> Lines(Card card, bool golden)
    {
        // A new sentence that starts with its own trigger is its own line.
        static List<string> Split(string text) => text.Split('\n').Select(Norm)
            .SelectMany(l => Regex.Split(l, @"\. (?=when |whenever |after you )")).Select(Norm).Where(l => l.Length > 0).ToList();
        var lines = Split(card.Text);
        if (!golden || string.IsNullOrWhiteSpace(card.GoldText)) return lines;
        var gold = Split(card.GoldText).Select(l => SplitPrefix(l).Body).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var (prefix, body) = SplitPrefix(lines[i]);
            var match = gold.FirstOrDefault(g => Skeleton(g) == Skeleton(body));
            if (match is not null) lines[i] = prefix.Length > 0 ? $"{prefix} — {match}" : match;
        }
        return lines;
    }

    private static Stats ParseStat(string s)
    {
        var m = Regex.Match(s, @"\+(\d+)/\+(\d+)");
        if (m.Success) return new Stats(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        m = Regex.Match(s, @"\+(\d+) (attack|health)");
        var n = int.Parse(m.Groups[1].Value);
        return m.Groups[2].Value == "attack" ? new Stats(n, 0) : new Stats(0, n);
    }

    private static string? Faction(string word) => FactionWords.GetValueOrDefault(word.Trim());
    private static int Number(string word) => NumberWords.TryGetValue(word, out var n) ? n : int.TryParse(word, out n) ? n : 1;
    private static int RarityLevel(string word) => Rarities.Level(word, ignoreCase: true);
    private static Paint Color(string word) => word switch { "red" => Paint.Red, "blue" => Paint.Blue, "yellow" => Paint.Yellow, _ => Paint.None };

    private void Add(Trigger t, Action<Ctx> run, string text, Func<Ctx, bool>? when)
        => cc.Abilities.Add(new Ability { Trigger = t, Run = run, Text = text, When = when });

    private static Func<Ctx, bool>? And(Func<Ctx, bool>? a, Func<Ctx, bool>? b) =>
        a is null ? b : b is null ? a : c => a(c) && b(c);

    // ---------------------------------------------------------------- one line of rules text

    private bool Line(string line, Func<Ctx, bool>? cond)
    {
        Match m;

        // "Yellow — ..." : only while my slot is painted that colour.
        if ((m = Regex.Match(line, $@"^({COLORS}) (?:— )?(.+)$")).Success && (line.Contains('—') || m.Groups[2].Value.StartsWith("hunt")))
        {
            var color = Color(m.Groups[1].Value);
            return Line(m.Groups[2].Value, And(cond, c => c.MeSlot >= 0 && c.Owner.Slots[c.MeSlot].Paint.HasFlag(color)));
        }

        if (cond is null && Passive(line)) return true;

        var (prefix, body) = SplitPrefix(line);
        if (prefix.Length > 0)
        {
            if (prefix == "quest") return QuestLine(body);
            if (prefix == "prophecy") return false;
            if (prefix == "treasure find")
            {
                var treasure = cards.Find(body);
                if (treasure is null || !treasure.IsTreasure) return false;
                cc.TreasureFind = treasure.Key;
                return true;
            }
            if (prefix == "support") return SupportLine(body);
            if (body.StartsWith("when ")) return Line(body, cond);

            var effect = Effect(body, out var full);
            if (effect is null) return false;
            foreach (var word in Regex.Split(prefix, @",? and |, "))
            {
                var trigger = word switch
                {
                    "recruit" => Trigger.Recruit, "farewell" => Trigger.Farewell, "hunt" => Trigger.Hunt,
                    _ => Trigger.ChanceEncounter,
                };
                Add(trigger, effect, line, cond);
            }
            return full;
        }

        foreach (var (pattern, build) in MoreTriggerRules.Concat(TriggerRules))
        {
            if (!(m = Regex.Match(line, pattern)).Success) continue;
            var (trigger, when, rest) = build(m);
            if (trigger is null) return false;
            var effect = Effect(rest, out var full);
            if (effect is null) return false;
            Add(trigger.Value, effect, line, And(cond, when));
            return full;
        }

        // No trigger words: what the card does when it is played / collected / the game starts.
        var own = cc.Card.IsSpell ? Trigger.Cast : cc.Card.IsTreasure ? Trigger.Collected : cc.Card.IsCaptain ? Trigger.GameStart : (Trigger?)null;
        if (own is null) return false;
        var action = Effect(line, out var complete);
        if (action is null) return false;
        Add(own.Value, action, line, cond);
        return complete;
    }

    // ---------------------------------------------------------------- triggers

    private static (Trigger?, Func<Ctx, bool>?, string) T(Trigger t, string rest, Func<Ctx, bool>? when = null) => (t, when, rest);
    private static readonly (Trigger?, Func<Ctx, bool>?, string) NoMatch = (null, null, "");

    private static Func<Ctx, bool>? TypeIs(string word, string? orWord = null)
    {
        if (word is "characters" or "character") return null;
        var a = Faction(word);
        var b = orWord is null ? null : Faction(orWord);
        if (a is null) return _ => false;
        return c => c.Is(c.It, a) || (b is not null && c.Is(c.It, b));
    }

    private static bool KnownType(string word) => word is "characters" or "character" || Faction(word) is not null;

    private static readonly (string Pattern, Func<Match, (Trigger?, Func<Ctx, bool>?, string)> Build)[] TriggerRules =
    [
        (@"^each fight,? (?:and whenever you cast a spell directly on me, )?(.+)$", m => T(Trigger.FightStart, m.Groups[1].Value)),
        (@"^(?:each turn|at the start of (?:each|your) turn),? (.+)$", m => T(Trigger.TurnStart, m.Groups[1].Value)),
        (@"^when i attack,? (.+)$", m => T(Trigger.Attack, m.Groups[1].Value)),
        (@"^when i'm attacked,? (.+)$", m => T(Trigger.Attacked, m.Groups[1].Value)),
        (@"^when i survive damage,? (.+)$", m => T(Trigger.SurviveDamage, m.Groups[1].Value)),
        (@"^when i take damage,? (.+)$", m => T(Trigger.TakeDamage, m.Groups[1].Value)),
        (@"^i gain (.+) when one of your other (\w+) is ko'd$", m => KnownType(m.Groups[2].Value)
            ? T(Trigger.AllyKO, m.Groups[1].Value, And(TypeIs(m.Groups[2].Value), c => !ReferenceEquals(c.It, c.Me))) : NoMatch),
        (@"^when (?:you cast a spell|a spell is cast) directly on me,? (.+)$", m => T(Trigger.SpellCastOnMe, m.Groups[1].Value)),
        (@"^when you (?:cast|play) (?:a|any) spell,? (.+)$", m => T(Trigger.SpellCast, m.Groups[1].Value)),
        (@"^when(?:ever)? you summon a character(?: in combat| during a fight)?,? (.+)$", m => T(Trigger.Summon, m.Groups[1].Value)),
        (@"^when(?:ever)? one of your (other )?(\w+) attacks,? (?:first )?(.+)$", m => KnownType(m.Groups[2].Value)
            ? T(Trigger.AllyAttack, m.Groups[3].Value, And(TypeIs(m.Groups[2].Value), m.Groups[1].Success ? c => !ReferenceEquals(c.It, c.Me) : null)) : NoMatch),
        (@"^when one of your (other )?(\w+) is ko'd,? (.+)$", m => KnownType(m.Groups[2].Value)
            ? T(Trigger.AllyKO, m.Groups[3].Value, And(TypeIs(m.Groups[2].Value), m.Groups[1].Success ? c => !ReferenceEquals(c.It, c.Me) : null)) : NoMatch),
        (@"^when any character is ko'd,? (.+)$", m => T(Trigger.AnyKO, m.Groups[1].Value)),
        (@"^when you recruit me or another (\w+),? (.+)$", m => KnownType(m.Groups[1].Value)
            ? T(Trigger.RecruitOther, m.Groups[2].Value, TypeIs(m.Groups[1].Value)) : NoMatch),
        (@"^when you recruit (an?|another) (\w+)(?: or (\w+))?,? (.+)$", m => KnownType(m.Groups[2].Value)
            ? T(Trigger.RecruitOther, m.Groups[4].Value, And(TypeIs(m.Groups[2].Value, m.Groups[3].Success ? m.Groups[3].Value : null),
                m.Groups[1].Value == "another" ? c => !ReferenceEquals(c.It, c.Me) : null)) : NoMatch),
        (@"^when you silver or golden me,? (.+)$", m => T(Trigger.Silvered, m.Groups[1].Value)),
        (@"^when you silver me,? (.+)$", m => T(Trigger.Silvered, m.Groups[1].Value, c => c.Me?.Tier == Tier.Silver)),
        (@"^when you golden me,? (.+)$", m => T(Trigger.Goldened, m.Groups[1].Value)),
        (@"^when you golden a character,? (.+)$", m => T(Trigger.AllyGoldened, m.Groups[1].Value)),
        (@"^when (?:you hunt|(?:any|one) of your characters (?:successfully )?hunts?),? (.+)$", m => T(Trigger.AllyHunt, m.Groups[1].Value)),
        (@"^when(?:ever)? you (?:collect|get|discover|find) a treasure,? (.+)$", m => T(Trigger.TreasureCollected, m.Groups[1].Value)),
        (@"^(?:after|when) you lose a fight,? (.+)$", m => T(Trigger.FightLost, m.Groups[1].Value)),
        (@"^when you win a fight,? (.+)$", m => T(Trigger.FightWon, m.Groups[1].Value)),
        (@"^when you complete a quest,? (.+)$", m => T(Trigger.QuestCompleted, m.Groups[1].Value)),
        (@"^when you pick a card,? (.+)$", m => T(Trigger.Pick, m.Groups[1].Value)),
        (@"^when you buff (?:a slot|one or more slots),? (.+)$", m => T(Trigger.SlotBuffed, m.Groups[1].Value)),
        (@"^when my slot is buffed,? (.+)$", m => T(Trigger.SlotBuffed, m.Groups[1].Value, c => c.TargetSlot == c.MeSlot)),
    ];

    // ---------------------------------------------------------------- always-on text

    private bool Passive(string line)
    {
        Match m;
        switch (line)
        {
            case "ranged": cc.Ranged = true; return true;
            case "sneak": cc.Sneak = true; return true;
            case "sneak, ranged": cc.Sneak = cc.Ranged = true; return true;
            case "cursed": cc.Cursed = true; return true;
            case "i'm too cowardly to attack": cc.CantAttack = true; return true;
            case "you get the first attack": cc.FirstAttack = true; return true;
            case "your characters count as all types": cc.AllTypes = true; return true;
            case "your first character has ranged": cc.FirstCharacterRanged = true; return true;
            case "double your hunt abilities": cc.ExtraTriggers[Trigger.Hunt] = 1; return true;
            case "your recruit abilities trigger twice": cc.ExtraTriggers[Trigger.Recruit] = 1; return true;
            case "double your abilities that happen at the start of fights":
            case "your 'each fight' abilities happen an extra times":
                cc.ExtraTriggers[Trigger.FightStart] = 1; return true;
            case "your recruit, hunt, and farewell abilities are all doubled":
                cc.ExtraTriggers[Trigger.Recruit] = cc.ExtraTriggers[Trigger.Hunt] = cc.ExtraTriggers[Trigger.Farewell] = 1; return true;
        }

        if (MorePassive(line)) return true;
        if ((m = Regex.Match(line, @"^treasure hoard(?: (\d+))?$")).Success)
        { cc.Hoard = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1; return true; }
        if ((m = Regex.Match(line, @"^supersweet (\d+)$")).Success) { cc.Supersweet = int.Parse(m.Groups[1].Value); return true; }
        if ((m = Regex.Match(line, @"^buffs on me are (doubled|tripled)$")).Success)
        { cc.BuffMultiplier = m.Groups[1].Value == "doubled" ? 2 : 3; return true; }
        if ((m = Regex.Match(line, @"^you start with (\d+) hearts$")).Success) { cc.StartHearts = int.Parse(m.Groups[1].Value); return true; }

        // "I have +6/+6 for each Animal you have recruited this game."
        if ((m = Regex.Match(line, $@"^i have {STAT} for each (.+)$")).Success)
        {
            var count = ForEach(m.Groups[2].Value);
            if (count is null) return false;
            var s = ParseStat(m.Groups[1].Value);
            cc.SelfAuras.Add(a => s * count(a.P));
            return true;
        }
        if ((m = Regex.Match(line, $@"^your characters have {STAT} for each time you've sweetened your candy$")).Success)
        { var s = ParseStat(m.Groups[1].Value); cc.UnitAuras.Add(a => s * a.P.Sweetness); return true; }

        // Buff-source modifiers.
        if ((m = Regex.Match(line, $@"^(?:your )?buffs from (?:your )?spells give an (?:additional|extra) {STAT}$")).Success)
        { cc.SpellBonus = ParseStat(m.Groups[1].Value); return true; }
        if ((m = Regex.Match(line, $@"^buffs from fae give an additional {STAT}$")).Success)
        { cc.FaeBonus = ParseStat(m.Groups[1].Value); return true; }
        if ((m = Regex.Match(line, $@"^your (?:treasure buffs (?:give an additional|are improved by)|treasures give an additional) {STAT}$")).Success)
        { cc.TreasureBonus = ParseStat(m.Groups[1].Value); return true; }

        // Slot statics.
        if ((m = Regex.Match(line, $@"^(?:all )?your slots have {STAT}$")).Success)
        { var s = ParseStat(m.Groups[1].Value); cc.SlotAuras.Add((_, _) => s); return true; }
        if ((m = Regex.Match(line, $@"^your first slot has {STAT}$")).Success)
        { var s = ParseStat(m.Groups[1].Value); cc.SlotAuras.Add((_, i) => i == 0 ? s : default); return true; }
        if ((m = Regex.Match(line, $@"^your back row slots have {STAT}$")).Success)
        { var s = ParseStat(m.Groups[1].Value); cc.SlotAuras.Add((_, i) => i >= Board.FrontSlots ? s : default); return true; }

        // Shop statics.
        if ((m = Regex.Match(line, $@"^(?:(bronze) )?characters in your shop have {STAT}$")).Success)
        {
            var s = ParseStat(m.Groups[2].Value);
            var bronzeOnly = m.Groups[1].Success;
            cc.ShopAuras.Add((_, _, o) => !bronzeOnly || o.Tier == Tier.Bronze ? s : default);
            return true;
        }
        if ((m = Regex.Match(line, $@"^(\w+) in your shop have {STAT}$")).Success && Faction(m.Groups[1].Value) is { } shopType)
        {
            var s = ParseStat(m.Groups[2].Value);
            cc.ShopAuras.Add((_, _, o) => o.Card.HasFaction(shopType) || o.Card.HasFaction("All") ? s : default);
            return true;
        }

        // Unit statics: "Your characters have +1/+1", "Your Golden characters have +3/+3", "Your Toys have +4 Attack and are Villains".
        if ((m = Regex.Match(line, $@"^your (\w+?)(?: characters)? have {STAT}(?: and are (\w+))?$")).Success)
        {
            var who = UnitFilter(m.Groups[1].Value);
            if (who is null) return false;
            var s = ParseStat(m.Groups[2].Value);
            cc.UnitAuras.Add(a => who(a) ? s : default);
            if (m.Groups[3].Success && Faction(m.Groups[1].Value) is { } from && Faction(m.Groups[3].Value) is { } to) cc.TypeGrants.Add((from, to));
            return true;
        }
        if ((m = Regex.Match(line, @"^(?:all of )?your (\w+?)(?: characters)? have treasure hoard(?: (\d+))?$")).Success
            || (m = Regex.Match(line, @"^give your (\w+?)(?: characters)? treasure hoard(?: (\d+))?$")).Success)
        {
            var who = UnitFilter(m.Groups[1].Value);
            if (who is null) return false;
            var n = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
            cc.HoardGrants.Add(a => who(a) ? n : 0);
            return true;
        }

        // Granted abilities: Your Pirates have "Hunt — Give my slot +3/+3."
        if ((m = Regex.Match(line, "^(?:all )?(?:give )?your (.+?) (?:have|has) \"(.+?)\"?$")).Success)
        {
            Func<AuraCtx, bool>? who = m.Groups[1].Value switch
            {
                "first character" => a => a.Slot == 0,
                "front row" or "front row characters" => a => a.Slot is >= 0 and < Board.FrontSlots,
                "heroes and villains" => a => a.G.HasType(a.P, a.U, "Hero") || a.G.HasType(a.P, a.U, "Villain"),
                var w => UnitFilter(w.Replace(" characters", "")),
            };
            if (who is null) return false;
            var inner = new AbilityCompiler(cards, cc.Card);
            if (!inner.Line(Norm(m.Groups[2].Value), null) || inner.cc.Abilities.Count == 0) return false;
            foreach (var ability in inner.cc.Abilities) cc.Granted.Add(new Granted(who, ability));
            return true;
        }
        return false;
    }

    private static Func<AuraCtx, bool>? UnitFilter(string word) => word switch
    {
        "characters" or "character" => _ => true,
        "golden" => a => a.U.Tier == Tier.Golden,
        "silver" => a => a.U.Tier == Tier.Silver,
        "support" => a => !a.G.Compiled(a.U).Support.IsZero,
        "hunt" => a => a.G.Compiled(a.U).Abilities.Any(x => x.Trigger == Trigger.Hunt),
        _ => Faction(word) is { } f ? a => a.G.HasType(a.P, a.U, f) : null,
    };

    private static Func<Player, int>? ForEach(string what)
    {
        Match m;
        if (what == "heart you're missing") return p => p.MissingHearts;
        if (what == "fight you've lost this game") return p => p.Count("fightsLost");
        if (what is "character you've goldened this game") return p => p.Count("goldened");
        if (what == "quest you've completed") return p => p.Count("quests");
        if (what == "spell you've cast this game") return p => p.Count("spells");
        if (Regex.IsMatch(what, @"^time (?:you've hunted this game|your characters have successfully hunted)$")) return p => p.Count("hunts");
        if ((m = Regex.Match(what, @"^(\w+) you have recruited this game$")).Success && Faction(m.Groups[1].Value) is { } f)
            return p => p.Count("recruited:" + f);
        if ((m = Regex.Match(what, @"^of your (\w+) characters that were ko'd this game$")).Success && Faction(m.Groups[1].Value) is { } k)
            return p => p.Count("ko:" + k);
        return null;
    }

    private bool SupportLine(string body)
    {
        Match m;
        if ((m = Regex.Match(body, $@"^{STAT}( and treasure hoard)?$")).Success)
        {
            cc.Support = ParseStat(m.Groups[1].Value);
            if (m.Groups[2].Success) cc.SupportHoard = 1;
            return true;
        }
        static bool InFront(Ctx c, int slot) => Board.InFrontOf(c.MeSlot).Contains(slot);
        if ((m = Regex.Match(body, $@"^each fight, give (?:the )?characters in front of me {STAT}$")).Success)
        {
            var s = ParseStat(m.Groups[1].Value);
            Add(Trigger.FightStart, c => { foreach (var e in c.Mine.Where(e => InFront(c, e.Slot)).ToList()) c.Buff(e.Unit, s); }, body, null);
            return true;
        }
        if ((m = Regex.Match(body, $@"^when a character in front of me (attacks|is attacked), give it {STAT}$")).Success)
        {
            var s = ParseStat(m.Groups[2].Value);
            Add(m.Groups[1].Value == "attacks" ? Trigger.AllyAttack : Trigger.AllyAttacked, c => c.Buff(c.It, s), body, c => InFront(c, c.ItSlot));
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- quests

    private bool QuestLine(string body)
    {
        Match m;
        Quest Q(Func<Ctx, bool> step, params Trigger[] triggers) => new() { Triggers = triggers, Step = step, Text = body };
        Func<Ctx, bool> Times(int goal) => c => c.Holder.Inc("quest") >= goal;

        if ((m = Regex.Match(body, @"^recruit (\w+) (\w+?)(?: characters)?$")).Success && Faction(m.Groups[2].Value) is { } type)
        {
            var goal = Number(m.Groups[1].Value);
            cc.Quest = Q(c => c.Is(c.It, type) && !ReferenceEquals(c.It, c.Me) && c.Holder.Inc("quest") >= goal, Trigger.RecruitOther);
        }
        else if ((m = Regex.Match(body, @"^i attack (\d+) times$")).Success) cc.Quest = Q(Times(Number(m.Groups[1].Value)), Trigger.Attack);
        else if ((m = Regex.Match(body, @"^i hunt (\d+) times$")).Success) cc.Quest = Q(Times(Number(m.Groups[1].Value)), Trigger.Hunt);
        else if ((m = Regex.Match(body, @"^i survive damage (\d+) times$")).Success) cc.Quest = Q(Times(Number(m.Groups[1].Value)), Trigger.SurviveDamage);
        else if ((m = Regex.Match(body, @"^cast (\d+) spells$")).Success) cc.Quest = Q(Times(Number(m.Groups[1].Value)), Trigger.SpellCast);
        else if ((m = Regex.Match(body, @"^collect (\d+) treasures$")).Success) cc.Quest = Q(Times(Number(m.Groups[1].Value)), Trigger.TreasureCollected);
        else if ((m = Regex.Match(body, @"^pick (\d+) cards from the shop$")).Success) cc.Quest = Q(Times(Number(m.Groups[1].Value)), Trigger.Pick);
        else if ((m = Regex.Match(body, @"^i see you successfully hunt (\d+) times$")).Success) cc.Quest = Q(Times(Number(m.Groups[1].Value)), Trigger.AllyHunt);
        else if (body == "lose a fight") cc.Quest = Q(_ => true, Trigger.FightLost);
        else if (Regex.IsMatch(body, @"^golden a(?:nother)? character$")) cc.Quest = Q(_ => true, Trigger.AllyGoldened);
        else if (body == "i see a character transform") cc.Quest = Q(_ => true, Trigger.Transformed);
        else if ((m = Regex.Match(body, @"^i take (\d+) damage$")).Success)
        {
            var goal = int.Parse(m.Groups[1].Value);
            cc.Quest = Q(c => c.Holder.Inc("quest", c.Amount) >= goal, Trigger.TakeDamage);
        }
        else if ((m = Regex.Match(body, @"^deal (\d+) damage$")).Success)
        {
            var goal = int.Parse(m.Groups[1].Value);
            cc.Quest = Q(c =>
            {
                if (!c.Holder.Counters.ContainsKey("questBase")) c.Holder.Counters["questBase"] = c.Owner.Count("damage");
                return c.Owner.Count("damage") - c.Holder.Count("questBase") >= goal;
            }, Trigger.FightStart, Trigger.AllyAttack);
        }
        else if ((m = Regex.Match(body, @"^(?:during a fight, )?i have (?:at least )?(\d+)(?: or more)? (attack or health|attack|health)(?: during a fight)?$")).Success)
        {
            var goal = int.Parse(m.Groups[1].Value);
            var which = m.Groups[2].Value;
            cc.Quest = Q(c =>
            {
                if (c.Fight is null || c.Me is null) return false;
                var s = c.G.StatsOf(c.Owner, c.Me, c.MeSlot, c.Fight, c.MeF);
                return which switch { "attack" => s.Atk >= goal, "health" => s.Hp >= goal, _ => s.Atk >= goal || s.Hp >= goal };
            }, Trigger.FightStart, Trigger.AllyAttack, Trigger.AllyAttacked);
        }
        else if (body == "have each of your slots buffed")
            cc.Quest = Q(c => c.Owner.Slots.All(s => !s.Buff.IsZero), Trigger.TurnStart, Trigger.FightStart, Trigger.SlotBuffed);
        else if ((m = Regex.Match(body, @"^have (\w+) (\w+)$")).Success && Faction(m.Groups[2].Value) is { } have)
        {
            var goal = Number(m.Groups[1].Value);
            cc.Quest = Q(c => c.Mine.Count(e => c.Is(e.Unit, have)) >= goal, Trigger.RecruitOther, Trigger.TurnStart, Trigger.FightStart);
        }
        return cc.Quest is not null;
    }
}
