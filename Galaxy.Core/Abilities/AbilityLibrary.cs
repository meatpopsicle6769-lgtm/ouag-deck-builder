using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Galaxy.Core.Cards;

namespace Galaxy.Core.Abilities;

/// <summary>Compiles each card once per catalog and shares the result between games (thread-safe).</summary>
public sealed class AbilityLibrary
{
    private static readonly ConditionalWeakTable<CardCatalog, AbilityLibrary> Libraries = new();
    private readonly CardCatalog cards;
    private readonly ConcurrentDictionary<(string Cid, bool Golden), CompiledCard> cache = new();

    private AbilityLibrary(CardCatalog cards) => this.cards = cards;

    public static AbilityLibrary For(CardCatalog cards) => Libraries.GetValue(cards, c => new AbilityLibrary(c));

    public CompiledCard Get(Card card, bool golden = false) =>
        cache.GetOrAdd((card.Cid, golden), _ => AbilityCompiler.Compile(cards, card, golden));

    /// <summary>How much of every card's text the engine implements.</summary>
    public IEnumerable<CompiledCard> CoverageReport() => cards.All.Select(c => Get(c));
}
