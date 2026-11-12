using Galaxy.Core.Cards;

namespace DeckBuilder;

/// <summary>One deck being built in the editor.</summary>
public sealed class EditorDeck(string name)
{
    public string Name { get; } = name;
    public Card? Captain { get; set; }
    public List<Card> Characters { get; } = [];

    public Deck ToDeck() => new()
    {
        Name = Name,
        Captain = Captain?.Key ?? "",
        Characters = Characters.Select(c => c.Key).ToList(),
    };
}

/// <summary>The two decks of the current browser session, shared by the editor and the simulation page.</summary>
public sealed class DeckSession
{
    public const int DeckSize = 10;
    public EditorDeck[] Decks { get; } = [new("Deck A"), new("Deck B")];
    public int Active { get; set; }
    public EditorDeck Current => Decks[Active];
}
