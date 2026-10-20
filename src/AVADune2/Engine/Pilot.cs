namespace AVADune2.Engine;

/// <summary>
/// The player's side played by the computer, for the demonstration and the tests: a <see cref="Brain"/> put in charge
/// of the player's House, and an eye for where the camera should be (the fighting, then the army, then the
/// harvesters, then home).
/// </summary>
public static class Pilot
{
    /// <summary>Hands the player's House to the computer.</summary>
    public static Brain TakeOver(World w)
    {
        var b = Brain.ForPilot(w, w.Goal == Goal.Harvest);
        w.PlayerHouse.Brain = b;
        return b;
    }

    /// <summary>Where the most is happening that's worth watching.</summary>
    public static (double X, double Y) Watch(World w)
    {
        // Explosions near the player's units or buildings.
        double sx = 0, sy = 0;
        int n = 0;
        foreach (var e in w.Effects)
        {
            if (e.Kind is not (Fx.Boom or Fx.BigBoom or Fx.Sonic or Fx.Swallow)) continue;
            sx += e.X;
            sy += e.Y;
            n++;
        }
        if (n >= 2) return (sx / n, sy / n);
        var c = w.Creatures.FirstOrDefault(c => c.Dormant <= 0 && c.Surfaced);
        if (c is not null) return (c.X, c.Y);
        var army = w.UnitsOf(World.Player).Where(u => Rules.Fighter(u.Type) && u.Order == Order.AttackMove).ToList();
        if (army.Count > 0) return (army.Average(u => u.X), army.Average(u => u.Y));
        var h = w.UnitsOf(World.Player).FirstOrDefault(u => u.Type == Kind.Harvester && u.Order == Order.Harvest);
        if (h is not null) return (h.X, h.Y);
        var yard = w.BuildingsOf(World.Player).FirstOrDefault();
        return yard is null ? (Field.N / 2.0, Field.N / 2.0) : (yard.CX, yard.CY);
    }
}
