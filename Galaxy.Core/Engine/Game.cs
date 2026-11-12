using Galaxy.Core.Abilities;
using Galaxy.Core.Cards;

namespace Galaxy.Core.Engine;

public sealed record GameResult(int Winner, int Turns, double[] Hearts, int[] FightsWon, int Ties, IReadOnlyList<string>? Log)
{
    public bool IsDraw => Winner < 0;
}

/// <summary>One two-player game: alternating shop phases and fights until a player runs out of hearts.</summary>
public sealed partial class Game
{
    private const int MaxShopsPerTurn = 12;
    private const int MaxEffectDepth = 12;

    private readonly AbilityLibrary lib;
    private int depth;

    public CardCatalog Cards { get; }
    public GameRules Rules { get; }
    public Random Rng { get; }
    public Player[] Players { get; }
    public int Turn { get; private set; }
    public Fight? Current { get; private set; }
    public List<string>? Log { get; }

    public Game(CardCatalog cards, Deck deckA, Deck deckB, GameRules? rules = null, int seed = 0,
        IAgent? agentA = null, IAgent? agentB = null, bool log = false)
    {
        Cards = cards;
        Rules = rules ?? new GameRules();
        Rng = new Random(seed);
        Log = log ? [] : null;
        lib = AbilityLibrary.For(cards);
        Players = [NewPlayer(0, deckA, agentA ?? new GreedyAgent()), NewPlayer(1, deckB, agentB ?? new GreedyAgent())];
    }

    public CompiledCard Compiled(Card card, bool golden = false) => lib.Get(card, golden);
    public CompiledCard Compiled(Unit unit) => lib.Get(unit.Card, unit.Tier == Tier.Golden);
    public void Say(string message) => Log?.Add(message);

    // ---------------------------------------------------------------- setup

    private Player NewPlayer(int index, Deck deck, IAgent agent)
    {
        var problems = deck.Validate(Cards, Rules.DeckSize, Rules.MaxDeckSize);
        if (problems.Count > 0) throw new ArgumentException($"Deck '{deck.Name}' is not valid: {string.Join(" ", problems)}");

        var p = new Player { Index = index, Name = $"P{index + 1} ({deck.Name})", Deck = deck, Agent = agent };
        var captain = Cards.Get(deck.Captain);
        p.Captains.Add(new Attachment { Card = captain });
        p.StartingDeck.AddRange(deck.Characters.Select(Cards.Get));

        // Card pool = shared Characters + this player's own deck.
        p.CharacterPool.AddRange(Cards.Characters.Where(c => c.IsShared));
        p.CharacterPool.AddRange(p.StartingDeck);

        // Candy spells and Paintbrushes only show up if the deck or Captain cares about them.
        var texts = p.StartingDeck.Append(captain).ToList();
        var candy = texts.Any(c => c.HasFaction("Candy") || c.Text.Contains("Candy", StringComparison.OrdinalIgnoreCase));
        bool Cares(string color) => texts.Any(c => c.Text.Contains(color) || c.Text.Contains("paint", StringComparison.OrdinalIgnoreCase));
        p.SpellPool.AddRange(Cards.Spells.Where(s => s.IsShared && s.RarityLevel >= 0
            && (!s.HasFaction("Candy") || candy)
            && (!s.Name.EndsWith("Paintbrush") || Cares(s.Name.Split(' ')[0]))));

        p.Hearts = p.MaxHearts = Math.Max(Rules.StartingHearts, lib.Get(captain).StartHearts);
        return p;
    }

    // ---------------------------------------------------------------- main loop

    public GameResult Play()
    {
        int[] wins = [0, 0];
        var ties = 0;
        foreach (var p in Players)
        {
            Say($"{p.Name}: Captain {p.Captains[0].Card.Name}, {HeartText(p)}.");
            foreach (var captain in p.Captains.ToList()) Run(p, captain, lib.Get(captain.Card), -1, null, Trigger.GameStart);
        }

        for (Turn = 1; Turn <= Rules.MaxTurns; Turn++)
        {
            Say($"--- Turn {Turn} (shop level: {Rarities.Order[ShopLevel(Players[0])]}) ---");
            foreach (var p in Players) ShopPhase(p);

            Current = new Fight(this);
            var winner = Current.Run();
            var survivors = winner < 0 ? 0 : Current.Entries(winner).Count();
            Current = null;

            if (winner < 0)
            {
                ties++;
                Say("Fight tied. No hearts lost.");
                foreach (var p in Players) Fire(p, Trigger.FightNotWon);
            }
            else
            {
                wins[winner]++;
                var (won, lost) = (Players[winner], Players[1 - winner]);
                if (won.Cracked > 0) won.Cracked--;
                if (Rules.LossAlwaysEmptiesHeart || survivors >= 3) LoseHeart(lost); else CrackHeart(lost);
                lost.Inc("fightsLost");
                lost.Counters["lostLast"] = 1;
                won.Counters["lostLast"] = 0;
                Say($"{won.Name} wins the fight with {survivors} left. {lost.Name} has {HeartText(lost)} left.");
                Fire(lost, Trigger.FightLost);
                Fire(lost, Trigger.FightNotWon);
                Fire(won, Trigger.FightWon);
            }

            var alive = Players.Where(p => p.Hearts > 0).ToList();
            if (alive.Count < 2)
            {
                var champion = alive.Count == 1 ? alive[0].Index : -1;
                Say(champion >= 0 ? $"{Players[champion].Name} wins the game on turn {Turn}." : "Both players are out of hearts: draw.");
                return new GameResult(champion, Turn, [Players[0].Hearts - Players[0].Cracked * 0.5, Players[1].Hearts - Players[1].Cracked * 0.5], wins, ties, Log);
            }
        }
        Say($"No winner after {Rules.MaxTurns} turns: draw.");
        return new GameResult(-1, Rules.MaxTurns, [Players[0].Hearts - Players[0].Cracked * 0.5, Players[1].Hearts - Players[1].Cracked * 0.5], wins, ties, Log);
    }

    /// <summary>True on the turns the shop gains a rarity (3, 5, 7, 9 with default rules).</summary>
    public bool LeveledUpThisTurn =>
        Turn > 1 && (Turn - 1) % Math.Max(1, Rules.TurnsPerShopLevel) == 0 && (Turn - 1) / Math.Max(1, Rules.TurnsPerShopLevel) <= Rarities.Legendary;

    public int ShopLevel(Player p) => Math.Min(Rarities.Legendary, (Turn - 1) / Math.Max(1, Rules.TurnsPerShopLevel));

    // ---------------------------------------------------------------- shop phase

    private void ShopPhase(Player p)
    {
        p.Counters["spellsThisTurn"] = 0;
        p.Counters["treasuresThisTurn"] = 0;
        Fire(p, Trigger.TurnStart);

        var regular = Rules.ShopsPerTurn;
        for (var shops = 0; shops < MaxShopsPerTurn && (regular > 0 || p.BonusShops > 0); shops++)
        {
            if (regular > 0) regular--; else p.BonusShops--;
            RunShop(p);
        }
        p.BonusShops = 0;
    }

    private void RunShop(Player p)
    {
        var offers = MakeShop(p);
        if (offers.Count == 0) return;

        foreach (var offer in offers.Where(o => !o.IsSpell))
        {
            var compiled = lib.Get(offer.Card);
            if (compiled.Abilities.Any(a => a.Trigger == Trigger.ChanceEncounter))
                Run(p, offer.Existing ?? new Unit { Card = offer.Card }, compiled, -1, null, Trigger.ChanceEncounter);
        }

        var choice = p.Agent.ChooseShop(this, p, offers);
        if (choice.Offer < 0 || choice.Offer >= offers.Count)
        {
            Say($"{p.Name} skips a shop.");
            return;
        }

        var picked = offers[choice.Offer];
        p.Inc("picks");
        if (picked.IsSpell) CastSpell(p, picked.Card);
        else Recruit(p, picked, choice.Slot);
        Fire(p, Trigger.Pick);
    }

    public List<Offer> MakeShop(Player p)
    {
        var level = ShopLevel(p);
        var maxRarity = p.NextShopMaxRarity >= 0 ? Math.Min(level, p.NextShopMaxRarity) : level;
        var pool = p.CharacterPool.Where(c => !p.RemovedFromPool.Contains(c.Cid) && c.RarityLevel <= maxRarity).ToList();
        if (p.NextShopFaction is { } faction)
        {
            var typed = pool.Where(c => c.HasFaction(faction) || c.HasFaction("All")).ToList();
            if (typed.Count > 0) pool = typed;
        }

        var offers = new List<Offer>();
        for (var i = 0; i < Rules.CharactersPerShop && pool.Count > 0; i++)
        {
            var at = Rng.Next(pool.Count);
            var card = pool[at];
            pool.RemoveAt(at);
            var existing = p.Units.GetValueOrDefault(card.Cid);
            var offer = new Offer
            {
                Card = card,
                Existing = existing,
                Tier = existing is null || existing.Copies == 0 ? Tier.Bronze : existing.Copies == 1 ? Tier.Silver : Tier.Golden,
            };
            offer.Bonus = p.ShopAura + p.NextShopBuff;
            foreach (var (type, buff) in p.NextShopTypedBuffs)
                if (card.HasFaction(type) || card.HasFaction("All")) offer.Bonus += buff;
            foreach (var h in Active(p))
                foreach (var aura in h.C.ShopAuras)
                    offer.Bonus += Scale(p, h, aura(this, p, offer));
            offer.Shown = card.BaseStats + TierBonus(offer.Tier) + (existing?.Buff ?? default) + offer.Bonus;
            offers.Add(offer);
        }

        if (Turn >= Rules.SpellsFromTurn)
        {
            var spells = p.SpellPool.Where(s => s.RarityLevel <= level).ToList();
            if (spells.Count > 0) offers.Add(new Offer { Card = spells[Rng.Next(spells.Count)] });
        }

        p.NextShopBuff = default;
        p.NextShopTypedBuffs.Clear();
        p.NextShopFaction = null;
        p.NextShopMaxRarity = -1;
        return offers;
    }

    private static Stats TierBonus(Tier tier) => Stats.One * (int)tier;

    private void Recruit(Player p, Offer offer, int slot)
    {
        var u = offer.Existing;
        if (u is null) p.Units[offer.Card.Cid] = u = new Unit { Card = offer.Card };
        u.Copies++;
        var silvered = u.Copies == 2 && u.Tier == Tier.Bronze;
        var goldened = u.Copies >= 3 && u.Tier != Tier.Golden;

        u.Buff += offer.Bonus + p.NextRecruitBuff;
        p.NextRecruitBuff = default;
        if (p.NextRecruitMultiplier > 1)
        {
            u.Buff += (u.Base + u.Buff) * (p.NextRecruitMultiplier - 1);
            p.NextRecruitMultiplier = 1;
        }

        // A Silver/Golden copy replaces the copy already on the board; otherwise the agent chose a slot or the bank.
        var at = p.SlotOf(u);
        if (at < 0)
        {
            if (slot < 0 && !p.BoardFull && !p.Bank.Contains(u)) slot = Array.FindIndex(p.Slots, s => s.Unit is null);
            if (slot is >= 0 and < Board.Size)
            {
                var replaced = p.Slots[slot].Unit;
                p.Slots[slot].Unit = u;
                p.Bank.Remove(u);
                at = slot;
                Say($"{p.Name} recruits {u.Card.Name} {offer.Shown} into slot {slot + 1}" + (replaced is null ? "." : $", replacing {replaced.Card.Name}."));
            }
            else
            {
                if (!p.Bank.Contains(u)) p.Bank.Add(u);
                Say($"{p.Name} banks {u.Card.Name}.");
            }
        }
        else Say($"{p.Name} recruits another {u.Card.Name}.");

        p.Inc("recruits");
        foreach (var f in u.Card.Factions) p.Inc("recruited:" + f);

        FireSelf(p, u, at, null, Trigger.Recruit);
        Fire(p, Trigger.RecruitOther, it: u);

        if (silvered)
        {
            Promote(p, u, viaShop: true);
            p.BonusShops++;
        }
        else if (goldened)
        {
            Promote(p, u, viaShop: true);
            DiscoverTreasure(p, u.Card.RarityLevel, u);
        }
    }

    /// <summary>Bronze -> Silver or Silver -> Golden. Spells that promote do not grant the shop bonus.</summary>
    public void Promote(Player p, Unit u, bool viaShop)
    {
        if (u.Tier == Tier.Golden) return;
        u.Tier++;
        u.Copies = Math.Max(u.Copies, (int)u.Tier + 1);
        var slot = p.SlotOf(u);
        Say($"{p.Name}'s {u.Card.Name} is now {u.Tier}" + (viaShop ? "." : " (no shop bonus)."));
        FireSelf(p, u, slot, null, Trigger.Silvered);
        if (u.Tier != Tier.Golden) return;
        p.RemovedFromPool.Add(u.Card.Cid);
        p.Inc("goldened");
        FireSelf(p, u, slot, null, Trigger.Goldened);
        Fire(p, Trigger.AllyGoldened, it: u);
    }

    // ---------------------------------------------------------------- treasures

    public void DiscoverTreasure(Player p, int rarityLevel, Unit? finder)
    {
        if (finder is not null && Compiled(finder).TreasureFind is { } special && !finder.TreasureFindUsed)
        {
            finder.TreasureFindUsed = true;
            GainTreasure(p, Cards.Get(special));
            return;
        }
        var options = TreasuresOf(rarityLevel);
        if (options.Count == 0) return;
        Rng.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(options));
        options = options.Take(3).ToList();
        var pick = p.Agent.ChooseTreasure(this, p, options);
        GainTreasure(p, options[Math.Clamp(pick, 0, options.Count - 1)]);
        Fire(p, Trigger.TreasureDiscovered);
    }

    public void GainRandomTreasure(Player p, int rarityLevel)
    {
        var options = rarityLevel >= 0
            ? TreasuresOf(rarityLevel)
            : Cards.Treasures.Where(t => t.IsShared && t.RarityLevel >= 0 && t.RarityLevel <= ShopLevel(p)).ToList();
        if (options.Count > 0) GainTreasure(p, options[Rng.Next(options.Count)]);
    }

    private List<Card> TreasuresOf(int rarityLevel)
    {
        for (var level = Math.Clamp(rarityLevel, 0, Rarities.SecretRare); level >= 0; level--)
        {
            var list = Cards.Treasures.Where(t => t.IsShared && t.RarityLevel == level).ToList();
            if (list.Count > 0) return list;
        }
        return [];
    }

    public void GainTreasure(Player p, Card treasure)
    {
        var owned = new Attachment { Card = treasure };
        p.Treasures.Add(owned);
        p.Inc("treasures");
        p.Inc("treasuresThisTurn");
        Say($"{p.Name} gets the treasure {treasure.Name}.");
        Run(p, owned, lib.Get(treasure), -1, null, Trigger.Collected);
        Fire(p, Trigger.TreasureCollected);
    }

    public int TreasureCount(Player p) => p.Treasures.Count;

    // ---------------------------------------------------------------- spells

    public void CastSpell(Player p, Card spell)
    {
        var compiled = lib.Get(spell);
        p.Inc("spells");
        p.Inc("spellsThisTurn");

        if (compiled.Combat && Current is null)
        {
            p.QueuedCombatSpells.Add(spell);
            Say($"{p.Name} casts {spell.Name} (resolves at the start of the fight).");
            Fire(p, Trigger.SpellCast);
            return;
        }

        var slot = -1;
        if (compiled.Targeted) slot = Math.Clamp(p.Agent.ChooseSpellTarget(this, p, spell), 0, Board.Size - 1);
        var target = BoardOf(p, Current).FirstOrDefault(e => e.Slot == slot);
        Say($"{p.Name} casts {spell.Name}" + (target.Unit is null ? "." : $" on {target.Unit.Card.Name}."));
        Run(p, new Attachment { Card = spell }, compiled, -1, null, Trigger.Cast, target.Unit, target.Fighter, 0, slot);

        if (target.Unit is not null) FireSelf(p, target.Unit, slot, target.Fighter, Trigger.SpellCastOnMe);
        Fire(p, Trigger.SpellCast, it: target.Unit, targetSlot: slot);
        foreach (var s in p.Slots.Where(s => s.Paint.HasFlag(Paint.Blue)))
            s.Buff += Stats.One * PaintStrength(s.Paint);
    }

    internal void ResolveQueuedSpells(Player p)
    {
        foreach (var spell in p.QueuedCombatSpells.ToList())
            Run(p, new Attachment { Card = spell }, lib.Get(spell), -1, null, Trigger.Cast);
        p.QueuedCombatSpells.Clear();
    }

    public void CastRandomSpell(Player p, Func<Card, bool>? filter)
    {
        var level = ShopLevel(p);
        var spells = Cards.Spells.Where(s => s.IsShared && s.RarityLevel >= 0 && s.RarityLevel <= level && (filter is null || filter(s))).ToList();
        if (spells.Count == 0 && filter is not null) spells = Cards.Spells.Where(s => s.IsShared && filter(s)).ToList();
        if (spells.Count > 0) CastSpell(p, spells[Rng.Next(spells.Count)]);
    }

    // ---------------------------------------------------------------- hearts, candy, paint

    /// <summary>Hearts as text, counting a cracked heart as half: "2.5 hearts".</summary>
    public static string HeartText(Player p) => $"{p.Hearts - p.Cracked * 0.5:0.#} hearts";

    public void LoseHeart(Player p)
    {
        if (p.Hearts <= 0) return;
        if (p.Cracked > 0) p.Cracked--;
        p.Hearts--;
        Fire(p, Trigger.HeartLost);
    }

    /// <summary>Puts a random Character of the given rarity straight onto the board (or the bank if it is full).</summary>
    public Unit? GainRandomCharacter(Player p, int rarityLevel)
    {
        var options = p.CharacterPool.Where(c => c.RarityLevel == rarityLevel && !p.RemovedFromPool.Contains(c.Cid)).ToList();
        if (options.Count == 0) return null;
        var card = options[Rng.Next(options.Count)];
        var existing = p.Units.GetValueOrDefault(card.Cid);
        var offer = new Offer { Card = card, Existing = existing, Shown = card.BaseStats + (existing?.Buff ?? default) };
        Recruit(p, offer, Array.FindIndex(p.Slots, s => s.Unit is null));
        return p.Units[card.Cid];
    }

    /// <summary>Transforms a character into a random one of a rarity. Tier and buffs carry over.</summary>
    public void TransformRandom(Player p, Unit u, int rarityLevel)
    {
        rarityLevel = Math.Clamp(rarityLevel, 0, Rarities.SecretRare);
        var options = Cards.Characters.Where(c => c.RarityLevel == rarityLevel && c.Pool != "token" && c.Cid != u.Card.Cid
            && (c.IsShared || p.StartingDeck.Contains(c))).ToList();
        if (options.Count == 0) options = Cards.Characters.Where(c => c.RarityLevel == rarityLevel && c.IsShared).ToList();
        if (options.Count == 0) return;
        var card = options[Rng.Next(options.Count)];
        var into = new Unit
        {
            Card = card, Tier = u.Tier, Copies = u.Copies, Buff = u.Buff, Improve = u.Improve, ExtraHoard = u.ExtraHoard,
            GrantedRanged = u.GrantedRanged, GrantedSneak = u.GrantedSneak, Candy = u.Candy, ExtraTypes = u.ExtraTypes,
        };
        p.Units[card.Cid] = into;
        p.RemovedFromPool.Add(u.Card.Cid);
        foreach (var s in p.Slots.Where(s => ReferenceEquals(s.Unit, u))) s.Unit = into;
        var banked = p.Bank.IndexOf(u);
        if (banked >= 0) p.Bank[banked] = into;
        Current?.Replace(u, into);
        Say($"  {p.Name}'s {u.Card.Name} transforms into {card.Name}.");
        Fire(p, Trigger.Transformed, it: into);
    }

    /// <summary>Cracks a full heart; an already-cracked heart is emptied instead.</summary>
    public void CrackHeart(Player p)
    {
        if (p.Cracked > 0) LoseHeart(p);
        else if (p.Hearts > 0) p.Cracked++;
    }

    public void HealHeart(Player p)
    {
        if (p.Cracked > 0) p.Cracked--;
    }

    public void Sweeten(Player p, int times)
    {
        p.Sweetness += times * (1 + Active(p).Sum(h => h.C.ExtraSweeten));
        Fire(p, Trigger.Sweetened);
    }

    private static int PaintStrength(Paint paint) => int.PopCount((int)paint) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };

    /// <summary>Painting gives the slot +1/+1 and the colour; a slot holds at most two colours.</summary>
    public void PaintSlot(Player p, int slot, Paint color)
    {
        if (slot is < 0 or >= Board.Size) return;
        var s = p.Slots[slot];
        s.Buff += Stats.One;
        if (!s.Paint.HasFlag(color) && int.PopCount((int)s.Paint) < 2) s.Paint |= color;
        p.Inc("paints");
        Say($"{p.Name} paints slot {slot + 1} {color}.");
    }

    /// <summary>Red and Yellow slots improve the abilities of the character standing on them each fight.</summary>
    internal void ApplyPaint(Player p)
    {
        foreach (var s in p.Occupied)
        {
            var strength = PaintStrength(s.Paint);
            if (s.Paint.HasFlag(Paint.Red)) s.Unit!.Improve += new Stats(strength, 0);
            if (s.Paint.HasFlag(Paint.Yellow)) s.Unit!.Improve += new Stats(0, strength);
        }
    }
}
