namespace AVADune2.Engine;

/// <summary>
/// Makes a battlefield from a scenario: open sand with dunes, outcrops of rock and mountain, a plateau of rock for each
/// base, fields of solarite near every base and out in the deep desert, a few blooms; then the bases themselves (the
/// enemy's built up to the mission's size on concrete), the starting units, the Houses' minds and the sandmaws.
/// </summary>
public static class MapGen
{
    private const int N = Field.N;

    /// <summary>Where bases may stand: the corners first, then the middles of the edges.</summary>
    private static readonly (int X, int Y)[] Spots = [(11, 11), (52, 52), (52, 11), (11, 52), (32, 10), (32, 53), (10, 32), (53, 32)];

    /// <summary>What a base has, by size: each size adds to the one before.</summary>
    private static readonly Kind[][] BaseLists =
    [
        [],
        [Kind.Yard, Kind.Turbine, Kind.Barracks],
        [Kind.Refinery, Kind.Turbine, Kind.LightFactory],
        [Kind.Outpost, Kind.Silo, Kind.Turret, Kind.Turret],
        [Kind.HeavyFactory, Kind.Turbine, Kind.RepairPad, Kind.RocketTurret, Kind.RocketTurret, Kind.Turbine],
        [Kind.HighTech, Kind.Turbine, Kind.Starport, Kind.Turbine, Kind.Palace, Kind.Turbine, Kind.RocketTurret, Kind.Turret],
    ];

    public static IEnumerable<Kind> BaseList(int size) => BaseLists.Take(Math.Clamp(size, 0, BaseLists.Length - 1) + 1).SelectMany(l => l);

    /// <summary>Sets up a whole battle.</summary>
    public static World Setup(Scenario s)
    {
        var w = new World { Dice = new Dice(s.Seed), Goal = s.Goal, GoalCredits = s.GoalCredits };
        var d = new Dice(s.Seed * 7 + 3);
        // Where everyone starts: the player in a corner, the first enemy across from it, the rest in the other corners.
        int first = d.Next(4);
        var spots = new List<(int X, int Y)> { Spots[first] };
        var rest = Spots.Where(p => p != Spots[first]).OrderByDescending(p => Field.Dist(p.X, p.Y, Spots[first].X, Spots[first].Y)).ToList();
        int needed = s.Enemies.Length + (s.Sovereign ? 1 : 0);
        foreach (var p in rest)
        {
            if (spots.Count > needed) break;
            if (spots.Skip(1).All(q => Field.Dist(q.X, q.Y, p.X, p.Y) > 30)) spots.Add(p);
        }
        foreach (var p in rest)
            if (spots.Count <= needed && !spots.Contains(p)) spots.Add(p);

        Terrain(w, s.Seed, spots, s.EnemyBase, s.PlayerBase);

        Sim.AddHouse(w, s.Player, 0, false, s.Credits, s.Tech);
        foreach (var f in s.Enemies) Sim.AddHouse(w, f, 1, true, 600 + s.Mission * 200, Math.Max(2, s.Tech));
        if (s.Sovereign) Sim.AddHouse(w, Faction.Sovereign, 1, true, 3000, 9);

        // The player: a construction yard on concrete, and a few units.
        var (px, py) = spots[0];
        if (s.PlayerBase > 0) BuildBase(w, World.Player, px, py, s.PlayerBase, spots[1]);
        else Sim.Put(w, World.Player, Kind.Yard, px - 1, py - 1, slab: true);
        Kind[] escort = s.Mission switch
        {
            1 => [Kind.Infantry, Kind.Infantry, Kind.Trike],
            2 => [Kind.Infantry, Kind.Infantry, Kind.Trike, Kind.Trike],
            <= 4 => [Kind.Infantry, Kind.Trike, Kind.Tank, Kind.Tank],
            _ => [Kind.Infantry, Kind.Quad, Kind.Tank, Kind.Tank, Kind.RocketTank],
        };
        if (s.Skirmish) escort = [Kind.Infantry, Kind.Trike, Kind.Tank];
        foreach (var k in escort) Sim.Spawn(w, World.Player, k, px + 1, py + 3);

        // The enemies' bases.
        for (int i = 1; i < w.Houses.Count; i++)
        {
            var h = w.Houses[i];
            var (ex, ey) = spots[i];
            int size = h.Faction == Faction.Sovereign ? 3 : i == 2 && !s.Sovereign ? Math.Max(1, s.EnemyBase - 1) : s.EnemyBase;
            if (s.Skirmish) size = s.EnemyBase;
            var brain = new Brain(i) { BaseX = ex, BaseY = ey, Mission = s.Mission };
            brain.Tune(s);
            // With more than one enemy, they don't all come at once: each later House waits longer, the Sovereign longest.
            brain.FirstWave += (i - 1) * 100 + (h.Faction == Faction.Sovereign ? 120 : 0);
            h.Brain = brain;
            if (size > 0) BuildBase(w, i, ex, ey, size, spots[0]);
            foreach (var k in Garrison(s, size, h.Faction)) Sim.Spawn(w, i, k, ex + d.Next(5) - 2, ey + 4 + d.Next(2));
            brain.Remember(w);
        }
        // The sandmaws start deep under the sand, and come up a while into the battle.
        for (int i = 0; i < s.Creatures; i++) w.Creatures.Add(new Creature { Dormant = (s.Mission == 1 ? 200 : 100) + i * 90 });
        Sim.UpdatePower(w);
        // A first look round the player's base.
        Sim.See(w, px, py, 9);
        return w;
    }

    /// <summary>The units an enemy base starts with.</summary>
    private static IEnumerable<Kind> Garrison(Scenario s, int size, Faction f)
    {
        if (f == Faction.Sovereign) return [Kind.Praetors, Kind.Praetors, Kind.Praetors, Kind.Praetors, Kind.Tank, Kind.Tank, Kind.SiegeTank];
        return size switch
        {
            0 => [Kind.Infantry, Kind.Infantry],
            1 => [Kind.Infantry, Kind.Infantry, Kind.Infantry],
            2 => [Kind.Infantry, Kind.Trike, Kind.Trike, Kind.Infantry],
            3 => [Kind.Infantry, Kind.Trike, Kind.Tank, Kind.Tank],
            4 => [Kind.Troopers, Kind.Quad, Kind.Tank, Kind.Tank, Kind.RocketTank],
            _ => [Kind.Troopers, Kind.Quad, Kind.Tank, Kind.Tank, Kind.RocketTank, Kind.SiegeTank, Rules.Special(f)],
        };
    }

    /// <summary>A base of the given size round (cx, cy): the yard in the middle and the rest packed round it with lanes
    /// between, each on concrete; turrets out toward the enemy.</summary>
    public static void BuildBase(World w, int owner, int cx, int cy, int size, (int X, int Y) enemy)
    {
        Sim.Put(w, owner, Kind.Yard, cx - 1, cy - 1, slab: true);
        double a = Math.Atan2(enemy.Y - cy, enemy.X - cx);
        int guard = 0;
        foreach (var k in BaseList(size).Skip(1))
        {
            int ax = cx, ay = cy;
            if (k is Kind.Turret or Kind.RocketTurret)
            {
                double side = (guard++ % 2 == 0 ? 1 : -1) * (0.45 + guard / 2 * 0.25);
                ax = cx + (int)Math.Round(Math.Cos(a + side) * 6);
                ay = cy + (int)Math.Round(Math.Sin(a + side) * 6);
            }
            if (Sim.FindSpot(w, owner, k, ax, ay) is not var (x, y)) continue;
            var b = Sim.Put(w, owner, k, x, y, slab: true);
            if (k == Kind.Refinery && Sim.Spawn(w, owner, Kind.Harvester, b.Door.X, b.Door.Y) is { } hv) hv.Order = Order.Harvest;
            if (k == Kind.HighTech) Sim.Spawn(w, owner, Kind.Skylifter, b.X + 1, b.Y + 1);
        }
    }

    // ───────────────────────────── the ground ─────────────────────────────

    private static double Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    private static double Smooth(double x, double y, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        double a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), e = Hash(ix + 1, iy + 1, seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + e) * fx * fy;
    }

    /// <summary>Fractal noise from 0 to 1.</summary>
    public static double Noise(double x, double y, int seed)
    {
        double sum = 0, amp = 1, total = 0, f = 1;
        for (int o = 0; o < 4; o++)
        {
            sum += Smooth(x * f, y * f, seed + o * 101) * amp;
            total += amp;
            amp *= 0.5;
            f *= 2;
        }
        return sum / total;
    }

    private static void Terrain(World w, int seed, List<(int X, int Y)> bases, int enemySize, int playerSize)
    {
        var d = new Dice(seed);
        for (int x = 0; x < N; x++)
        for (int y = 0; y < N; y++)
        {
            double r = Noise(x / 9.0, y / 9.0, seed), dune = Noise(x / 6.0, y / 6.0, seed + 77);
            w.Ground[x, y] = r > 0.74 ? Ground.Mountain : r > 0.62 ? Ground.Rock : dune > 0.6 ? Ground.Dunes : Ground.Sand;
        }
        // The bases' plateaus, with no mountains on or near them.
        for (int i = 0; i < bases.Count; i++)
        {
            var (bx, by) = bases[i];
            double radius = 7.5 + (i == 0 ? playerSize : enemySize) * 0.5;
            for (int x = bx - 14; x <= bx + 14; x++)
            for (int y = by - 14; y <= by + 14; y++)
            {
                if (!Field.In(x, y)) continue;
                double dist = Field.Dist(x, y, bx, by), edge = radius + (Noise(x / 3.0, y / 3.0, seed + 5) - 0.5) * 3;
                if (dist < edge) w.Ground[x, y] = Ground.Rock;
                else if (dist < edge + 3 && w.Ground[x, y] == Ground.Mountain) w.Ground[x, y] = Ground.Rock;
            }
        }
        // Solarite: fields on the sand near each base, and out in the deep desert.
        foreach (var (bx, by) in bases)
        {
            double toward = Math.Atan2(N / 2.0 - by, N / 2.0 - bx);
            for (int k = 0; k < 2; k++)
            {
                double a = toward + (k == 0 ? -0.7 : 0.7) + d.Range(-0.3, 0.3), dist = d.Range(12, 15);
                Sow(w, bx + Math.Cos(a) * dist, by + Math.Sin(a) * dist, d.Range(3.2, 4.5), seed + k);
            }
        }
        for (int k = 0; k < 5; k++)
        {
            double x = d.Range(8, N - 8), y = d.Range(8, N - 8);
            if (bases.Any(b => Field.Dist(b.X, b.Y, x, y) < 12)) continue;
            Sow(w, x, y, d.Range(2.5, 5), seed + 10 + k);
        }
        for (int k = 0, made = 0; k < 60 && made < 3; k++)
        {
            int x = d.Next(N), y = d.Next(N);
            if (!w.IsSandy(x, y) || w.Ore[x, y] > 0 || bases.Any(b => Field.Dist(b.X, b.Y, x, y) < 10)) continue;
            w.Bloom[x, y] = true;
            made++;
        }
        Connect(w, bases);
    }

    private static void Sow(World w, double cx, double cy, double radius, int seed)
    {
        for (int x = (int)(cx - radius - 2); x <= (int)(cx + radius + 2); x++)
        for (int y = (int)(cy - radius - 2); y <= (int)(cy + radius + 2); y++)
        {
            if (!Field.In(x, y) || !w.IsSandy(x, y)) continue;
            double dist = Field.Dist(x + 0.5, y + 0.5, cx, cy), edge = radius * (0.75 + 0.5 * Noise(x / 2.5, y / 2.5, seed + 31));
            if (dist >= edge) continue;
            double t = 1 - dist / edge;
            w.Ore[x, y] = Math.Max(w.Ore[x, y], (int)(140 + 520 * t * (0.6 + 0.4 * Noise(x / 2.0, y / 2.0, seed + 9))));
        }
    }

    /// <summary>Makes sure a tank can drive from the player's base to every other: a pass cut through any mountains
    /// in the way.</summary>
    private static void Connect(World w, List<(int X, int Y)> bases)
    {
        var (sx, sy) = bases[0];
        for (int i = 1; i < bases.Count; i++)
        {
            var reach = Reachable(w, sx, sy);
            var (tx, ty) = bases[i];
            if (reach[tx, ty]) continue;
            double len = Field.Dist(sx, sy, tx, ty);
            for (double t = 0; t <= 1; t += 0.5 / len)
            {
                int x = (int)Math.Round(sx + (tx - sx) * t), y = (int)Math.Round(sy + (ty - sy) * t);
                for (int j = y - 1; j <= y + 1; j++)
                for (int k = x - 1; k <= x + 1; k++)
                    if (Field.In(k, j) && w.Ground[k, j] == Ground.Mountain) w.Ground[k, j] = Ground.Rock;
            }
        }
    }

    public static bool[,] Reachable(World w, int sx, int sy, Mover m = Mover.Tracks)
    {
        var seen = new bool[N, N];
        var q = new Queue<(int, int)>();
        q.Enqueue((sx, sy));
        seen[sx, sy] = true;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                if (!Field.In(nx, ny) || seen[nx, ny] || w.Ground[nx, ny] == Ground.Mountain && m != Mover.Foot) continue;
                seen[nx, ny] = true;
                q.Enqueue((nx, ny));
            }
        }
        return seen;
    }
}
