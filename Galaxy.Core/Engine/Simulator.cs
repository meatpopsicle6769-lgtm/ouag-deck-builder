using Galaxy.Core.Cards;

namespace Galaxy.Core.Engine;

/// <summary>Totals over many games between two decks. "A" is the first deck, "B" the second.</summary>
public sealed record SimulationSummary(int Games, int WinsA, int WinsB, int Draws, double AverageTurns,
    double AverageHeartsLeftA, double AverageHeartsLeftB, int Seed)
{
    public double WinRateA => Games == 0 ? 0 : (double)WinsA / Games;
    public double WinRateB => Games == 0 ? 0 : (double)WinsB / Games;
    public double DrawRate => Games == 0 ? 0 : (double)Draws / Games;
}

/// <summary>Runs games without any UI. Game i uses seed + i, so a run can be repeated exactly.</summary>
public static class Simulator
{
    public static GameResult PlayOne(CardCatalog cards, Deck a, Deck b, int seed = 0, GameRules? rules = null,
        IAgent? agentA = null, IAgent? agentB = null, bool log = false) =>
        new Game(cards, a, b, rules, seed, agentA, agentB, log).Play();

    public static SimulationSummary Run(CardCatalog cards, Deck a, Deck b, int games, int seed = 0, GameRules? rules = null,
        Func<IAgent>? agentA = null, Func<IAgent>? agentB = null, bool parallel = true, CancellationToken cancel = default)
    {
        // Fail early, on the calling thread, if a deck is not legal.
        _ = new Game(cards, a, b, rules, seed);

        var results = new GameResult[games];
        void One(int i) => results[i] = PlayOne(cards, a, b, seed + i, rules, agentA?.Invoke(), agentB?.Invoke());
        if (parallel) Parallel.For(0, games, new ParallelOptions { CancellationToken = cancel }, One);
        else for (var i = 0; i < games; i++) { cancel.ThrowIfCancellationRequested(); One(i); }

        return new SimulationSummary(
            games,
            results.Count(r => r.Winner == 0),
            results.Count(r => r.Winner == 1),
            results.Count(r => r.IsDraw),
            games == 0 ? 0 : results.Average(r => r.Turns),
            games == 0 ? 0 : results.Average(r => r.Hearts[0]),
            games == 0 ? 0 : results.Average(r => r.Hearts[1]),
            seed);
    }
}
