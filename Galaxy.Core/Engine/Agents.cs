using Galaxy.Core.Cards;

namespace Galaxy.Core.Engine;

/// <summary>Which shop offer to take (-1 = none) and where to put a Character (-1 = bank).</summary>
public readonly record struct ShopChoice(int Offer, int Slot)
{
    public static readonly ShopChoice Skip = new(-1, -1);
}

/// <summary>Makes a player's decisions. Implement this to script your own strategy.</summary>
public interface IAgent
{
    ShopChoice ChooseShop(Game game, Player me, IReadOnlyList<Offer> offers);
    /// <summary>Index of the Treasure to keep from a treasure shop.</summary>
    int ChooseTreasure(Game game, Player me, IReadOnlyList<Card> options);
    /// <summary>Board slot (0-4) a targeted Spell is cast on.</summary>
    int ChooseSpellTarget(Game game, Player me, Card spell);
}

/// <summary>Simple stat-based play: upgrade copies first, fill the board, then replace the weakest character.</summary>
public sealed class GreedyAgent : IAgent
{
    public ShopChoice ChooseShop(Game game, Player me, IReadOnlyList<Offer> offers)
    {
        var characters = Enumerable.Range(0, offers.Count).Where(i => !offers[i].IsSpell).ToList();
        var spell = Enumerable.Range(0, offers.Count).Where(i => offers[i].IsSpell).DefaultIfEmpty(-1).First();

        // A Silver or Golden copy is always worth taking: it brings a bonus shop or a treasure.
        var upgrades = characters.Where(i => offers[i].Tier != Tier.Bronze).ToList();
        if (upgrades.Count > 0)
        {
            var pick = upgrades.OrderByDescending(i => offers[i].Tier).ThenByDescending(i => offers[i].Shown.Total).First();
            var at = me.SlotOf(offers[pick].Existing!);
            return new ShopChoice(pick, at >= 0 || me.BoardFull ? at : EmptySlot(game, me, offers[pick].Card));
        }

        if (characters.Count == 0) return spell >= 0 ? new ShopChoice(spell, -1) : ShopChoice.Skip;
        var best = characters.OrderByDescending(i => offers[i].Shown.Total).First();
        if (!me.BoardFull) return new ShopChoice(best, EmptySlot(game, me, offers[best].Card));

        var weakest = me.Slots.OrderBy(s => game.StatsOf(me, s.Unit!, -1).Total).First();
        if (offers[best].Shown.Total > game.StatsOf(me, weakest.Unit!, -1).Total) return new ShopChoice(best, weakest.Index);
        return spell >= 0 ? new ShopChoice(spell, -1) : new ShopChoice(best, -1);
    }

    public int ChooseTreasure(Game game, Player me, IReadOnlyList<Card> options) => 0;

    public int ChooseSpellTarget(Game game, Player me, Card spell) =>
        me.Occupied.OrderByDescending(s => game.StatsOf(me, s.Unit!, s.Index).Total).Select(s => s.Index).DefaultIfEmpty(0).First();

    private static int EmptySlot(Game game, Player me, Card card)
    {
        var compiled = game.Compiled(card);
        var backFirst = compiled.Ranged || !compiled.Support.IsZero || card.Text.StartsWith("Support", StringComparison.OrdinalIgnoreCase);
        int[] order = backFirst ? [3, 4, 0, 1, 2] : [0, 1, 2, 3, 4];
        return order.First(i => me.Slots[i].Unit is null);
    }
}

/// <summary>Picks uniformly at random; useful as a baseline.</summary>
public sealed class RandomAgent : IAgent
{
    public ShopChoice ChooseShop(Game game, Player me, IReadOnlyList<Offer> offers)
    {
        if (offers.Count == 0) return ShopChoice.Skip;
        var pick = game.Rng.Next(offers.Count);
        if (offers[pick].IsSpell) return new ShopChoice(pick, -1);
        var empty = me.Slots.Where(s => s.Unit is null).Select(s => s.Index).ToList();
        return new ShopChoice(pick, empty.Count > 0 ? empty[game.Rng.Next(empty.Count)] : game.Rng.Next(-1, Board.Size));
    }

    public int ChooseTreasure(Game game, Player me, IReadOnlyList<Card> options) => game.Rng.Next(options.Count);
    public int ChooseSpellTarget(Game game, Player me, Card spell) => game.Rng.Next(Board.Size);
}
