namespace Galaxy.Core.Engine;

/// <summary>Tunable rules. Defaults follow guide.galaxy.fun for a 2-player ("finals") game.</summary>
public sealed class GameRules
{
    /// <summary>Characters per deck. The guide says 12; this project's deck editor uses 10.</summary>
    public int DeckSize { get; set; } = 10;
    public int StartingHearts { get; set; } = 3;
    public int ShopsPerTurn { get; set; } = 2;
    public int CharactersPerShop { get; set; } = 3;
    /// <summary>First turn on which a Spell is added to each shop.</summary>
    public int SpellsFromTurn { get; set; } = 3;
    /// <summary>The shop gains a rarity every this many turns (turns 1, 3, 5, 7, 9).</summary>
    public int TurnsPerShopLevel { get; set; } = 2;
    /// <summary>Safety stop; a game still running after this many turns is a draw.</summary>
    public int MaxTurns { get; set; } = 40;
    /// <summary>Safety stop for a single fight; reaching it makes the fight a tie.</summary>
    public int MaxAttacksPerFight { get; set; } = 400;
    /// <summary>
    /// False (default): losing to 3 or more surviving enemies empties a heart, losing to fewer only cracks one
    /// (half a heart); winning a fight repairs a cracked heart.
    /// True (the guide's rule once only 2 players remain): every lost fight empties a heart.
    /// </summary>
    public bool LossAlwaysEmptiesHeart { get; set; }
    /// <summary>Smallest and largest legal deck. The deck editor builds 10; the site's premade decks have 12.</summary>
    public int MaxDeckSize { get; set; } = 12;
    /// <summary>Front row characters protect the back row (guide rule). False = any enemy can be hit.</summary>
    public bool FrontRowProtectsBackRow { get; set; } = true;
}
