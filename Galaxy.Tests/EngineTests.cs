using Galaxy.Core.Abilities;
using Galaxy.Core.Cards;
using Galaxy.Core.Engine;

namespace Galaxy.Tests;

public class EngineTests
{
    private static readonly CardCatalog Cards = CardCatalog.LoadDefault();
    private static readonly PremadeDecks Premade = new(Cards);

    [Fact]
    public void Catalog_loads_every_card_with_stats()
    {
        Assert.Equal(344, Cards.Characters.Count);
        Assert.Equal(120, Cards.Captains.Count);
        Assert.Equal(63, Cards.Spells.Count);
        Assert.Equal(176, Cards.Treasures.Count);
        Assert.All(Cards.Characters, c => Assert.NotNull(c.Attack));
        Assert.Equal(new Stats(2, 1), Cards.Get("aladdin").BaseStats);
        Assert.Contains("Pirate", Cards.Get("aladdin").Factions);
    }

    [Fact]
    public void Every_premade_deck_is_legal()
    {
        var rules = new GameRules();
        Assert.Equal(450, Premade.All.Count);
        Assert.All(Premade.All, d => Assert.Empty(d.Validate(Cards, rules.DeckSize, rules.MaxDeckSize)));
    }

    [Fact]
    public void Shared_pool_characters_are_rejected_in_a_deck()
    {
        var deck = Deck.Random(Cards, new Random(1), 10);
        deck.Characters[0] = "zeus";
        Assert.Contains(deck.Validate(Cards, 10), e => e.Contains("shared pool"));
    }

    [Fact]
    public void Same_seed_gives_the_same_game()
    {
        var (a, b) = (Premade.Find("apollo")!, Premade.Find("baldr")!);
        var first = Simulator.PlayOne(Cards, a, b, seed: 42, log: true);
        var second = Simulator.PlayOne(Cards, a, b, seed: 42, log: true);
        Assert.Equal(first.Log, second.Log);
        Assert.Equal(first.Winner, second.Winner);
    }

    [Fact]
    public void Simulation_totals_add_up_and_are_repeatable()
    {
        var (a, b) = (Premade.Find("apollo")!, Premade.Find("hannibal")!);
        var one = Simulator.Run(Cards, a, b, games: 200, seed: 5);
        var two = Simulator.Run(Cards, a, b, games: 200, seed: 5, parallel: false);
        Assert.Equal(200, one.WinsA + one.WinsB + one.Draws);
        Assert.Equal(one, two);
    }

    [Fact]
    public void Every_premade_starter_deck_can_play_a_full_game()
    {
        var starters = Premade.All.Where(d => d.Name == "Starter").ToList();
        for (var i = 0; i < starters.Count; i++)
        {
            var result = Simulator.PlayOne(Cards, starters[i], starters[(i + 1) % starters.Count], seed: i);
            Assert.InRange(result.Turns, 1, new GameRules().MaxTurns);
        }
    }

    [Fact]
    public void Baldr_starts_with_four_hearts()
    {
        var game = new Game(Cards, Premade.Find("baldr")!, Premade.Find("apollo")!);
        Assert.Equal(4, game.Players[0].Hearts);
        Assert.Equal(3, game.Players[1].Hearts);
    }

    [Fact]
    public void Half_heart_rule_cracks_then_empties()
    {
        var game = new Game(Cards, Premade.Find("apollo")!, Premade.Find("baldr")!);
        var p = game.Players[0];
        game.CrackHeart(p);
        Assert.Equal((3, 1), (p.Hearts, p.Cracked));
        game.CrackHeart(p);
        Assert.Equal((2, 0), (p.Hearts, p.Cracked));
        game.CrackHeart(p);
        game.HealHeart(p);
        Assert.Equal((2, 0), (p.Hearts, p.Cracked));
    }

    [Theory]
    [InlineData("laser_shark", Coverage.Full)]
    [InlineData("puss_in_boots", Coverage.Full)]
    [InlineData("momma_bear", Coverage.Full)]
    [InlineData("giant_growth", Coverage.Full)]
    [InlineData("aphrodites_tear", Coverage.Full)]
    public void Common_text_patterns_compile(string key, Coverage expected) =>
        Assert.Equal(expected, AbilityLibrary.For(Cards).Get(Cards.Get(key)).Coverage);

    [Fact]
    public void Golden_copies_use_the_gold_text_numbers()
    {
        var lib = AbilityLibrary.For(Cards);
        var card = Cards.Get("jill_sunshine");            // Support-- +5 Attack, no gold text
        Assert.Equal(new Stats(5, 0), lib.Get(card).Support);
        var bear = Cards.Get("papa_bear");                // +4/+4 bronze, +8/+8 gold
        Assert.Contains("+4/+4", lib.Get(bear).Abilities[0].Text);
        Assert.Contains("+8/+8", lib.Get(bear, golden: true).Abilities[0].Text);
    }

    [Fact]
    public void Unknown_text_is_reported_not_guessed()
    {
        var compiled = AbilityLibrary.For(Cards).Get(Cards.Get("paul_bunyan"));
        Assert.Equal(Coverage.None, compiled.Coverage);
        Assert.Empty(compiled.Abilities);
    }
}
