using Galaxy.Core.Abilities;
using Galaxy.Core.Cards;

namespace Galaxy.Core.Engine;

/// <summary>A character inside one fight. Damage is forgotten after the fight; buffs are not.</summary>
public sealed class Fighter
{
    public required Unit Unit { get; set; }
    public required Player Owner { get; init; }
    public int Slot;
    public int Damage;
    public bool Alive = true;
    /// <summary>Stat changes that only last for this fight.</summary>
    public Stats Temp;
    public int Attacks;
    public int Hits;
}

/// <summary>
/// Resolves one fight. A random player attacks first; sides alternate, each side's characters attacking in slot
/// order (left to right, front to back). The target is a random enemy in the front row while one stands.
/// </summary>
public sealed class Fight
{
    private readonly Game g;
    private readonly Fighter?[][] slots = [new Fighter?[Board.Size], new Fighter?[Board.Size]];
    private readonly int[] lastAttacker = [-1, -1];
    private Fighter? attacker, target;
    private int damageThisAttack;

    public Fight(Game game)
    {
        g = game;
        foreach (var p in g.Players)
            foreach (var s in p.Occupied)
                slots[p.Index][s.Index] = new Fighter { Unit = s.Unit!, Owner = p, Slot = s.Index };
    }

    public IEnumerable<Entry> Entries(int side)
    {
        foreach (var f in slots[side])
            if (f is { Alive: true }) yield return new Entry(f.Unit, f.Slot, f);
    }

    public Stats Stats(Fighter f) => g.StatsOf(f.Owner, f.Unit, f.Slot, this, f);
    public int Health(Fighter f) => Stats(f).Hp - f.Damage;
    private bool AnyAlive(int side) => slots[side].Any(f => f is { Alive: true });
    private List<Fighter> Living(int side) => slots[side].Where(f => f is { Alive: true }).Select(f => f!).ToList();

    /// <summary>Returns the winning player's index, or -1 for a tie.</summary>
    public int Run()
    {
        var side = FirstAttacker();
        g.Say($"Fight: {Describe(0)}  vs  {Describe(1)}. {g.Players[side].Name} attacks first.");

        foreach (var s in new[] { side, 1 - side })
        {
            var p = g.Players[s];
            g.ApplyPaint(p);
            g.Fire(p, Trigger.FightStart);
            g.ResolveQueuedSpells(p);
        }
        ResolveDeaths();

        var passes = 0;
        var quiet = 0;
        for (var n = 0; n < g.Rules.MaxAttacksPerFight && AnyAlive(0) && AnyAlive(1); n++)
        {
            var next = NextAttacker(side);
            side = 1 - side;
            if (next is null)
            {
                if (++passes >= 2) break;
                continue;
            }
            passes = 0;
            damageThisAttack = 0;
            Attack(next);
            // Nobody can hurt anybody: stop instead of looping forever.
            quiet = damageThisAttack == 0 ? quiet + 1 : 0;
            if (quiet >= 4 * Board.Size) break;
        }

        var (a, b) = (AnyAlive(0), AnyAlive(1));
        return a == b ? -1 : a ? 0 : 1;
    }

    private int FirstAttacker()
    {
        int Score(Player p)
        {
            var score = g.Active(p, this).Count(h => h.C.FirstAttack) + (p.AttacksFirstNextFight ? 1 : 0);
            p.AttacksFirstNextFight = false;
            return score;
        }
        var (a, b) = (Score(g.Players[0]), Score(g.Players[1]));
        return a > b ? 0 : b > a ? 1 : g.Rng.Next(2);
    }

    private string Describe(int side) =>
        g.Log is null ? "" : g.Players[side].Name + " [" + string.Join(", ", Living(side).Select(f => $"{f.Unit.Card.Name} {Stats(f)}")) + "]";

    private Fighter? NextAttacker(int side)
    {
        for (var k = 1; k <= Board.Size; k++)
        {
            var slot = (lastAttacker[side] + k) % Board.Size;
            if (slots[side][slot] is { Alive: true } f && !g.Compiled(f.Unit).CantAttack)
            {
                lastAttacker[side] = slot;
                return f;
            }
        }
        return null;
    }

    private bool Has(Fighter f, string keyword)
    {
        foreach (var h in g.Active(f.Owner, this))
        {
            if (keyword == "ranged" && f.Slot == 0 && h.C.FirstCharacterRanged) return true;
            foreach (var (who, granted) in h.C.KeywordGrants)
                if (granted == keyword && who(new AuraCtx(g, f.Owner, h.H, h.Slot, f.Unit, f.Slot, this))) return true;
        }
        return false;
    }

    private bool IsRanged(Fighter f) => f.Unit.GrantedRanged || g.Compiled(f.Unit).Ranged || Has(f, "ranged");

    /// <summary>Swaps the character a fighter represents (transform effects).</summary>
    public void Replace(Unit from, Unit into)
    {
        foreach (var f in slots.SelectMany(s => s))
            if (f is not null && ReferenceEquals(f.Unit, from)) f.Unit = into;
    }

    private void Attack(Fighter a)
    {
        var enemySide = 1 - a.Owner.Index;
        var enemies = Living(enemySide);
        if (enemies.Count == 0) return;
        var front = enemies.Where(f => f.Slot < Board.FrontSlots).ToList();
        var back = enemies.Where(f => f.Slot >= Board.FrontSlots).ToList();
        var sneak = a.Unit.GrantedSneak || g.Compiled(a.Unit).Sneak || Has(a, "sneak");
        var options = !g.Rules.FrontRowProtectsBackRow ? enemies
            : sneak ? (back.Count > 0 ? back : enemies)
            : front.Count > 0 ? front : back;
        var t = options[g.Rng.Next(options.Count)];

        attacker = a;
        target = t;
        a.Attacks++;
        g.FireSelf(a.Owner, a.Unit, a.Slot, a, Trigger.Attack, t.Unit, t);
        g.Fire(a.Owner, Trigger.AllyAttack, a.Unit, a);
        g.FireSelf(t.Owner, t.Unit, t.Slot, t, Trigger.Attacked, a.Unit, a);
        g.Fire(t.Owner, Trigger.AllyAttacked, t.Unit, t);

        if (a.Alive && t.Alive)
        {
            var (hit, back2) = (Stats(a).Atk, Stats(t).Atk);
            g.Log?.Add($" {a.Unit.Card.Name} {Stats(a).Atk}/{Health(a)} attacks {t.Unit.Card.Name} {Stats(t).Atk}/{Health(t)}.");
            Damage(t, hit, a);
            if (!IsRanged(a)) Damage(a, back2, t);
        }
        ResolveDeaths();
        attacker = target = null;
    }

    /// <summary>Deals damage. Deaths are settled by the fight loop right after the current attack or effect.</summary>
    public void Damage(Fighter? f, int amount, Fighter? source)
    {
        if (f is not { Alive: true } || amount <= 0) return;
        if (f.Hits++ == 0 && g.Compiled(f.Unit).FirstHitImmune) return;
        f.Damage += amount;
        damageThisAttack += amount;
        source?.Owner.Inc("damage", amount);
        g.FireSelf(f.Owner, f.Unit, f.Slot, f, Trigger.TakeDamage, source?.Unit, source, amount);
        if (Health(f) > 0) g.FireSelf(f.Owner, f.Unit, f.Slot, f, Trigger.SurviveDamage, source?.Unit, source, amount);
    }

    public void SetHealth(Fighter f, int health) => f.Damage = Math.Max(f.Damage, Stats(f).Hp - health);

    private void ResolveDeaths()
    {
        for (var guard = 0; guard < 4 * Board.Size; guard++)
        {
            var dead = slots.SelectMany(s => s).Where(f => f is { Alive: true } && Health(f) <= 0).Select(f => f!)
                .OrderBy(f => ReferenceEquals(f, target) ? 0 : 1).ToList();
            if (dead.Count == 0) return;
            foreach (var f in dead)
                Kill(f, ReferenceEquals(f, target) ? attacker : ReferenceEquals(f, attacker) ? target : null);
        }
    }

    public void Kill(Fighter f, Fighter? killer)
    {
        if (!f.Alive) return;
        f.Alive = false;
        var side = f.Owner.Index;
        if (ReferenceEquals(slots[side][f.Slot], f)) slots[side][f.Slot] = null;
        f.Owner.Inc("ko");
        foreach (var faction in f.Unit.Card.Factions) f.Owner.Inc("ko:" + faction);
        g.Log?.Add($"  {f.Unit.Card.Name} ({f.Owner.Name}) is KO'd.");

        g.FireSelf(f.Owner, f.Unit, f.Slot, f, Trigger.Farewell, killer?.Unit, killer);
        g.Fire(f.Owner, Trigger.AllyKO, f.Unit, f);
        foreach (var p in g.Players) g.Fire(p, Trigger.AnyKO, f.Unit, f);

        // Hunt: the attacker KO'd the character it attacked (it still counts if the attacker died too).
        if (killer is not null && ReferenceEquals(f, target) && ReferenceEquals(killer, attacker)
            && g.FireSelf(killer.Owner, killer.Unit, killer.Slot, killer, Trigger.Hunt, f.Unit, f) > 0)
        {
            killer.Owner.Inc("hunts");
            g.Fire(killer.Owner, Trigger.AllyHunt, killer.Unit, killer);
        }
    }

    public Fighter? Summon(int side, Unit unit, int preferSlot)
    {
        var slot = preferSlot >= 0 && slots[side][preferSlot] is null ? preferSlot : Array.FindIndex(slots[side], f => f is null);
        if (slot < 0) return null;
        return slots[side][slot] = new Fighter { Unit = unit, Owner = g.Players[side], Slot = slot };
    }
}
