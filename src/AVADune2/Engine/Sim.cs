namespace AVADune2.Engine;

/// <summary>
/// The rules of the battlefield, one 60th of a second at a time: power and storage, the factories and the yard,
/// putting buildings down, the units (in Sim.Units), the shooting and the sandmaws (in Sim.Combat), the starport and
/// the palace, the blooms of solarite, what the player has seen, and whether the mission is won or lost.
/// </summary>
public static partial class Sim
{
    /// <summary>The most paths worked out in a step, and the most tiles they may look at between them.</summary>
    public const int PathsPerStep = 8, PathTilesPerStep = 14000;
    /// <summary>How many things may wait in a factory's queue.</summary>
    public const int QueueMax = 5;
    /// <summary>Seconds for the starport's frigate to land with an order.</summary>
    public const double FrigateSeconds = 12;
    /// <summary>How often the starport's prices change.</summary>
    public const double PriceSeconds = 40;

    // ───────────────────────────── setting up ─────────────────────────────

    public static House AddHouse(World w, Faction f, int team, bool ai, int credits, int tech = 9)
    {
        var h = new House { Index = w.Houses.Count, Faction = f, Team = team, Ai = ai, Credits = credits, Tech = tech };
        w.Houses.Add(h);
        return h;
    }

    /// <summary>A unit on the nearest free tile to (x, y) it can stand on; null if there's none near.</summary>
    public static Unit? Spawn(World w, int owner, Kind k, int x, int y)
    {
        var st = Rules.Of(k);
        var spot = st.Mover == Mover.Air ? (Math.Clamp(x, 0, Field.N - 1), Math.Clamp(y, 0, Field.N - 1)) : FreeTile(w, x, y, st.Mover, 8);
        if (spot is not var (tx, ty)) return null;
        var u = new Unit
        {
            Id = w.NextId++, Owner = owner, Type = k, X = tx + 0.5, Y = ty + 0.5, Tx = tx, Ty = ty, Hp = st.Hp,
            Facing = Math.PI / 2, Aim = Math.PI / 2, HomeX = tx, HomeY = ty,
        };
        if (!u.InAir) w.Occ[tx, ty] = u;
        w.Units.Add(u);
        return u;
    }

    /// <summary>The free tile nearest (x, y) that a mover can stand on, looking out ring by ring.</summary>
    public static (int X, int Y)? FreeTile(World w, int x, int y, Mover m, int radius)
    {
        for (int r = 0; r <= radius; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            int tx = x + dx, ty = y + dy;
            if (PathFinder.Passable(w, tx, ty, m) && w.Occ[tx, ty] is null) return (tx, ty);
        }
        return null;
    }

    /// <summary>A building put straight down (for setting up a map): no rules, no cost; concrete under it if asked.</summary>
    public static Building Put(World w, int owner, Kind k, int x, int y, bool slab = false)
    {
        var st = Rules.Of(k);
        if (slab)
            for (int j = y; j < y + st.H; j++)
            for (int i = x; i < x + st.W; i++)
                if (Field.In(i, j) && w.Ground[i, j] == Ground.Rock) w.Slab[i, j] = owner + 1;
        int on = 0;
        for (int j = y; j < y + st.H; j++)
        for (int i = x; i < x + st.W; i++)
        {
            if (w.Slab[i, j] != 0) on++;
            if (w.Occ[i, j] is { } u) { u.Dead = true; w.Occ[i, j] = null; }
        }
        var b = new Building { Id = w.NextId++, Owner = owner, Type = k, X = x, Y = y, W = st.W, H = st.H, Footing = on / (double)(st.W * st.H) };
        b.Hp = st.Hp * (0.6 + 0.4 * b.Footing);
        for (int j = y; j < y + st.H; j++)
        for (int i = x; i < x + st.W; i++) w.Bld[i, j] = b;
        w.Buildings.Add(b);
        w.GroundVersion++;
        UpdatePower(w);
        return b;
    }

    // ───────────────────────────── placing buildings ─────────────────────────────

    /// <summary>
    /// Why a building can't go at (x, y) (its top-left tile), or null if it can: on rock (or the House's own
    /// concrete), clear of buildings and units, and next to the base.
    /// </summary>
    public static string? CanPlace(World w, int owner, Kind k, int x, int y)
    {
        var st = Rules.Of(k);
        bool anyBare = false, near = false;
        for (int j = y; j < y + st.H; j++)
        for (int i = x; i < x + st.W; i++)
        {
            if (!Field.In(i, j)) return "OFF THE MAP";
            if (w.Ground[i, j] != Ground.Rock) return "BUILD ON ROCK";
            if (w.Bld[i, j] is not null) return "NOT ON ANOTHER BUILDING";
            if (w.Occ[i, j] is not null) return "UNITS IN THE WAY";
            if (w.Slab[i, j] == 0) anyBare = true;
            if (w.Slab[i, j] != 0 && w.Slab[i, j] != owner + 1) return "NOT ON THE ENEMY'S CONCRETE";
        }
        if (k == Kind.Slab && !anyBare) return "CONCRETE IS ALREADY THERE";
        // Next to the base: within two tiles of one of the House's buildings or its concrete (a lane may run between).
        for (int j = y - 2; j <= y + st.H + 1 && !near; j++)
        for (int i = x - 2; i <= x + st.W + 1 && !near; i++)
        {
            if (!Field.In(i, j)) continue;
            if (w.Bld[i, j] is { Dead: false } b && b.Owner == owner || w.Slab[i, j] == owner + 1 && (k != Kind.Slab || i < x || j < y || i >= x + st.W || j >= y + st.H))
                near = true;
        }
        return near ? null : "BUILD NEXT TO YOUR BASE";
    }

    /// <summary>Puts down the building the yard has ready (or, for a test or a mind, any building) if it may go there.</summary>
    public static bool Place(World w, int owner, Kind k, int x, int y)
    {
        if (CanPlace(w, owner, k, x, y) is not null) return false;
        var h = w.Houses[owner];
        if (k == Kind.Slab)
        {
            var st = Rules.Of(k);
            for (int j = y; j < y + st.H; j++)
            for (int i = x; i < x + st.W; i++) w.Slab[i, j] = owner + 1;
            w.GroundVersion++;
        }
        else
        {
            var b = Put(w, owner, k, x, y);
            if (k == Kind.Refinery)
            {
                // A refinery comes with a harvester.
                var (dx, dy) = b.Door;
                if (Spawn(w, owner, Kind.Harvester, dx, dy) is { } hv) { hv.Order = Order.Harvest; hv.Think = 1; }
            }
            if (k == Kind.Palace) h.PalaceCharge = 0;
        }
        w.Hear(Sfx.Place, x + 1, y + 1);
        return true;
    }

    /// <summary>Puts down the building waiting at the yard, if it may go there.</summary>
    public static bool PlaceReady(World w, Building yard, int x, int y)
    {
        if (yard.Ready is not { } k || !Place(w, yard.Owner, k, x, y)) return false;
        yard.Ready = null;
        return true;
    }

    /// <summary>
    /// A good place for a building near (cx, cy): rock, next to the base, and (for the bigger buildings) with a lane of
    /// open ground round it so units can get about. Searches outward; null if there's nowhere.
    /// </summary>
    public static (int X, int Y)? FindSpot(World w, int owner, Kind k, int cx, int cy, int radius = 14)
    {
        var st = Rules.Of(k);
        bool lanes = k is not (Kind.Wall or Kind.Turret or Kind.RocketTurret or Kind.Slab);
        for (int r = 0; r <= radius; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            int x = cx + dx - st.W / 2, y = cy + dy - st.H / 2;
            if (CanPlace(w, owner, k, x, y) is not null) continue;
            if (lanes && !Clear(w, x, y, st.W, st.H)) continue;
            if (BlocksDoor(w, x, y, st.W, st.H)) continue;
            return (x, y);
        }
        return null;
    }

    /// <summary>Whether a footprint would stand in front of a building's door (a refinery's intake, a factory's door).</summary>
    private static bool BlocksDoor(World w, int x, int y, int wd, int ht)
    {
        foreach (var b in w.Buildings)
        {
            if (b.Dead || b.Type is Kind.Wall or Kind.Turret or Kind.RocketTurret or Kind.Slab) continue;
            var (dx, dy) = b.Door;
            if (dx >= x - 1 && dx <= x + wd && dy >= y && dy <= y + ht) return true;
        }
        return false;
    }

    /// <summary>No other building (but walls and turrets) in the ring of tiles round a footprint, and a free door.</summary>
    private static bool Clear(World w, int x, int y, int wd, int ht)
    {
        for (int j = y - 1; j <= y + ht; j++)
        for (int i = x - 1; i <= x + wd; i++)
        {
            if (i >= x && j >= y && i < x + wd && j < y + ht) continue;
            if (!Field.In(i, j)) { if (j == y + ht) return false; continue; }
            if (w.Bld[i, j] is { } b && b.Type is not (Kind.Wall or Kind.Turret or Kind.RocketTurret)) return false;
            if (j == y + ht && w.Ground[i, j] == Ground.Mountain) return false;
        }
        return true;
    }

    // ───────────────────────────── making things ─────────────────────────────

    /// <summary>Whether a house may make a kind of thing now: its tech, its House, and the buildings it needs.</summary>
    public static bool CanBuild(World w, int owner, Kind k)
    {
        var h = w.Houses[owner];
        var st = Rules.Of(k);
        if (st.Tech > h.Tech || !Rules.CanMake(h.Faction, k)) return false;
        if (!w.Has(owner, st.MadeBy)) return false;
        return st.Requires is null || st.Requires.All(r => w.Has(owner, r));
    }

    /// <summary>What a factory (or the yard) can make now.</summary>
    public static IEnumerable<Kind> Options(World w, Building b) =>
        Rules.Makes.TryGetValue(b.Type, out var list) ? list.Where(k => CanBuild(w, b.Owner, k)) : [];

    /// <summary>Adds to a factory's queue; false if it can't make that or the queue is full.</summary>
    public static bool Enqueue(World w, Building b, Kind k)
    {
        if (b.Dead || !Rules.Makes.TryGetValue(b.Type, out var list) || !list.Contains(k) || !CanBuild(w, b.Owner, k)) return false;
        if (b.Queue.Count >= QueueMax) return false;
        b.Queue.Add(k);
        return true;
    }

    /// <summary>Takes the last of a kind off a factory's queue, refunding what's been paid; a building waiting to be
    /// put down is given back in full.</summary>
    public static bool Cancel(World w, Building b, Kind k)
    {
        var h = w.Houses[b.Owner];
        if (b.Ready == k && !b.Queue.Contains(k))
        {
            b.Ready = null;
            h.Credits += Rules.Of(k).Cost;
            return true;
        }
        int i = b.Queue.LastIndexOf(k);
        if (i < 0) return false;
        if (i == 0)
        {
            h.Credits += b.Progress * Rules.Of(k).Cost;
            b.Progress = 0;
        }
        b.Queue.RemoveAt(i);
        return true;
    }

    /// <summary>How fast a house's factories work: slowed when power is short (never below a fifth), and the
    /// computer's a little quicker on the harder missions.</summary>
    public static double BuildSpeed(House h) => Math.Max(0.2, h.PowerRatio) * (h.Brain?.BuildBoost ?? 1);

    private static void Production(World w, double dt)
    {
        foreach (var b in w.Buildings)
        {
            if (b.Dead || b.Queue.Count == 0 || b.Ready is not null) continue;
            var h = w.Houses[b.Owner];
            var k = b.Queue[0];
            var st = Rules.Of(k);
            if (!CanBuild(w, b.Owner, k)) { Cancel(w, b, k); continue; }
            double rate = dt / st.Time * BuildSpeed(h);
            rate = Math.Min(rate, 1 - b.Progress);
            double pay = rate * st.Cost;
            if (h.Credits < pay)
            {
                if (b.Owner == World.Player && w.Clock - h.StorageAlarm > 20) { h.StorageAlarm = w.Clock; w.News.Add("NOT ENOUGH CREDITS"); }
                continue;
            }
            h.Credits -= pay;
            b.Progress += rate;
            if (b.Progress < 1 - 1e-9) continue;
            b.Progress = 0;
            b.Queue.RemoveAt(0);
            if (Rules.IsBuilding(k))
            {
                b.Ready = k;
                if (b.Owner == World.Player) { w.Hear(Sfx.SayBuilt); w.News.Add($"{st.Name} READY - PLACE IT"); }
            }
            else Deliver(w, b, k);
        }
    }

    /// <summary>A unit made: out of the factory's door and off to the rally point (or out of the way).</summary>
    private static void Deliver(World w, Building b, Kind k)
    {
        var (dx, dy) = b.Door;
        var u = Spawn(w, b.Owner, k, dx, dy);
        if (u is null) return;
        if (b.Owner == World.Player) { w.Hear(Sfx.SayUnitReady); w.News.Add($"{Rules.Of(k).Name} READY"); }
        if (k == Kind.Harvester) { u.Order = Order.Harvest; return; }
        if (k == Kind.Skylifter) { u.HomeX = b.X + 1; u.HomeY = b.Y + 1; u.X = b.CX; u.Y = b.CY; return; }
        if (b.RallyX >= 0) Command(w, u, Order.Move, b.RallyX, b.RallyY);
        else if (FreeTile(w, dx + (int)(w.Dice.Next() * 3) - 1, dy + 2, u.Stats.Mover, 4) is var (fx, fy)) Command(w, u, Order.Move, fx, fy);
    }

    // ───────────────────────────── the step ─────────────────────────────

    public static void Step(World w)
    {
        double dt = Field.Step;
        w.Clock += dt;
        w.Ticks++;
        UpdatePower(w);
        foreach (var h in w.Houses) h.Brain?.Update(w);
        Production(w, dt);
        FindPaths(w);
        for (int i = 0; i < w.Units.Count; i++)
            if (!w.Units[i].Dead) UpdateUnit(w, w.Units[i], dt);
        UpdateShots(w, dt);
        UpdateTurrets(w, dt);
        UpdateCreatures(w, dt);
        Repairs(w, dt);
        Starport(w, dt);
        Palaces(w, dt);
        if (w.Ticks % 30 == 0) { Ferry(w); Blooms(w); Replace(w); }
        if (w.Ticks % 6 == 1) Reveal(w);
        Tidy(w, dt);
        if (w.Ticks % 30 == 15) CheckOutcome(w);
    }

    private static void FindPaths(World w)
    {
        int done = 0, start = w.Paths.Expanded;
        while (w.PathQueue.Count > 0 && done < PathsPerStep && w.Paths.Expanded - start < PathTilesPerStep)
        {
            var u = w.PathQueue.Dequeue();
            if (u.Dead || !u.PathPending) continue;
            done++;
            var m = u.Stats.Mover;
            // A unit on its way into a tile carries on into it, and the new way starts there.
            int sx = u.Nx >= 0 ? u.Nx : u.Tx, sy = u.Nx >= 0 ? u.Ny : u.Ty;
            u.Path = w.Paths.Find(w, u, sx, sy, u.Gx, u.Gy, m, u.AvoidUnits);
            u.PathAt = 0;
            u.PathPending = false;
        }
    }

    /// <summary>Asks for a way to (gx, gy); it's worked out within a step or two.</summary>
    public static void RequestPath(World w, Unit u, int gx, int gy, bool avoid = false)
    {
        u.Gx = Math.Clamp(gx, 0, Field.N - 1);
        u.Gy = Math.Clamp(gy, 0, Field.N - 1);
        u.AvoidUnits = avoid;
        u.Path = null;
        if (!u.PathPending) { u.PathPending = true; w.PathQueue.Enqueue(u); }
    }

    // ───────────────────────────── power and storage ─────────────────────────────

    public static void UpdatePower(World w)
    {
        foreach (var h in w.Houses) { h.PowerMade = h.PowerUsed = 0; h.Capacity = 0; h.Radar = false; }
        foreach (var b in w.Buildings)
        {
            if (b.Dead) continue;
            var h = w.Houses[b.Owner];
            var st = b.Stats;
            if (st.Power > 0) h.PowerMade += st.Power * Math.Clamp(b.Hp / b.MaxHp, 0.25, 1);
            else h.PowerUsed -= st.Power;
            h.Capacity += st.Storage;
            if (b.Type == Kind.Outpost) h.Radar = true;
        }
        for (int i = 0; i < w.Houses.Count; i++)
        {
            var h = w.Houses[i];
            if (h.LowPower) h.Radar = false;
            if (i == World.Player && h.LowPower && w.Clock - h.PowerAlarm > 30 && w.Buildings.Any(b => !b.Dead && b.Owner == i))
            {
                h.PowerAlarm = w.Clock;
                w.Hear(Sfx.Alarm);
                w.Hear(Sfx.SayLowPower);
                w.News.Add("POWER IS LOW - BUILD WIND TURBINES");
            }
        }
    }

    /// <summary>Credits in: up to what the refineries and silos can hold; the rest is lost.</summary>
    public static double Store(World w, int owner, double amount)
    {
        var h = w.Houses[owner];
        double room = Math.Max(0, h.Capacity - h.Credits), taken = Math.Min(room, amount);
        h.Credits += taken;
        h.Harvested += taken;
        if (taken < amount && owner == World.Player && w.Clock - h.StorageAlarm > 25)
        {
            h.StorageAlarm = w.Clock;
            w.Hear(Sfx.SayStorage);
            w.News.Add("STORAGE IS FULL - BUILD SILOS");
        }
        return taken;
    }

    // ───────────────────────────── repairs ─────────────────────────────

    public const double RepairRate = 30, RepairPrice = 0.5;

    private static void Repairs(World w, double dt)
    {
        foreach (var b in w.Buildings)
        {
            if (b.Dead || !b.Repairing) continue;
            var h = w.Houses[b.Owner];
            double hp = Math.Min(b.MaxHp - b.Hp, RepairRate * dt * Math.Max(0.3, h.PowerRatio));
            double cost = hp * b.Stats.Cost / b.MaxHp * RepairPrice;
            if (hp <= 0) { b.Repairing = false; continue; }
            if (h.Credits < cost) continue;
            h.Credits -= cost;
            b.Hp += hp;
            if (b.Hp >= b.MaxHp) { b.Hp = b.MaxHp; b.Repairing = false; }
        }
    }

    // ───────────────────────────── the starport and the palace ─────────────────────────────

    /// <summary>What a unit costs at the starport today.</summary>
    public static int Price(World w, Kind k)
    {
        int i = Array.IndexOf(Rules.Market, k);
        double f = i >= 0 && i < w.Prices.Length ? w.Prices[i] : 1;
        return (int)Math.Round(Rules.Of(k).Cost * f / 5) * 5;
    }

    /// <summary>Buys a unit at the starport: paid now, delivered when the frigate lands.</summary>
    public static bool Buy(World w, int owner, Kind k)
    {
        var h = w.Houses[owner];
        if (!w.Has(owner, Kind.Starport) || Array.IndexOf(Rules.Market, k) < 0 || h.Order.Count >= 8) return false;
        if (!Rules.CanMake(h.Faction, k)) return false;
        int price = Price(w, k);
        if (h.Credits < price) return false;
        h.Credits -= price;
        h.Order.Add(k);
        if (h.Frigate < 0) h.Frigate = FrigateSeconds;
        return true;
    }

    private static void Starport(World w, double dt)
    {
        if (w.Prices.Length != Rules.Market.Length || w.Clock >= w.PricesAt)
        {
            if (w.Prices.Length != Rules.Market.Length) w.Prices = Rules.Market.Select(_ => 1.0).ToArray();
            for (int i = 0; i < w.Prices.Length; i++) w.Prices[i] = Math.Clamp(w.Prices[i] + w.Dice.Range(-0.25, 0.25), 0.6, 1.5);
            w.PricesAt = w.Clock + PriceSeconds;
        }
        foreach (var h in w.Houses)
        {
            if (h.Frigate < 0) continue;
            h.Frigate -= dt;
            if (h.Frigate > 0) continue;
            h.Frigate = -1;
            var port = w.BuildingsOf(h.Index).FirstOrDefault(b => b.Type == Kind.Starport);
            if (port is null) { h.Credits += h.Order.Sum(k => Rules.Of(k).Cost); h.Order.Clear(); continue; }
            foreach (var k in h.Order)
            {
                var u = Spawn(w, h.Index, k, port.Door.X, port.Door.Y);
                if (u is null) continue;
                if (k == Kind.Harvester) u.Order = Order.Harvest;
                if (k == Kind.Skylifter) { u.HomeX = port.X + 1; u.HomeY = port.Y + 1; }
            }
            h.Order.Clear();
            w.Fx(Fx.Deliver, port.CX, port.CY, 1.6);
            w.Hear(Sfx.Frigate, port.CX, port.CY);
            if (h.Index == World.Player) { w.Hear(Sfx.SayUnitReady); w.News.Add("THE FRIGATE HAS LANDED"); }
        }
    }

    private static void Palaces(World w, double dt)
    {
        foreach (var h in w.Houses)
        {
            var palace = w.Buildings.FirstOrDefault(b => !b.Dead && b.Owner == h.Index && b.Type == Kind.Palace);
            if (palace is null || h.Faction == Faction.Sovereign) continue;
            bool was = h.PalaceReady;
            h.PalaceCharge = Math.Min(1, h.PalaceCharge + dt / Rules.PalaceCharge(h.Faction) * h.PowerRatio);
            if (!was && h.PalaceReady && h.Index == World.Player)
            {
                w.Hear(Sfx.SayBuilt);
                w.News.Add($"THE {Rules.PalacePower(h.Faction)} IS READY");
            }
        }
    }

    /// <summary>
    /// The palace's weapon: Vrakos's ruin missile at (x, y) (wide of the mark as often as not), a raid of nomads on the
    /// enemy nearest (x, y) for Aldaine, or a saboteur at the palace door for Sythe.
    /// </summary>
    public static bool FirePalace(World w, int owner, int x, int y)
    {
        var h = w.Houses[owner];
        var palace = w.Buildings.FirstOrDefault(b => !b.Dead && b.Owner == owner && b.Type == Kind.Palace);
        if (palace is null || !h.PalaceReady) return false;
        h.PalaceCharge = 0;
        switch (h.Faction)
        {
            case Faction.Vrakos:
            {
                double tx = x + 0.5 + w.Dice.Range(-2.5, 2.5), ty = y + 0.5 + w.Dice.Range(-2.5, 2.5);
                w.Shots.Add(new Shot { X = palace.CX, Y = palace.CY, Sx = palace.CX, Sy = palace.CY, Tx = tx, Ty = ty, Speed = 4.5, Damage = 520, Splash = 2.6, Weapon = Weapon.Missile, Owner = owner });
                w.Hear(Sfx.Launch, palace.CX, palace.CY);
                w.Hear(Sfx.SayMissile);
                w.News.Add(owner == World.Player ? "RUIN MISSILE LAUNCHED" : "MISSILE APPROACHING!");
                break;
            }
            case Faction.Aldaine:
            {
                var target = w.Buildings.Where(b => !b.Dead && w.Enemies(owner, b.Owner)).MinBy(b => Field.Dist(b.CX, b.CY, x, y));
                int cx = target is null ? x : (int)target.CX, cy = target is null ? y : (int)target.CY;
                for (int i = 0; i < 4; i++)
                {
                    double a = w.Dice.Range(0, Math.PI * 2);
                    var u = Spawn(w, owner, Kind.Nomads, cx + (int)(Math.Cos(a) * 7), cy + (int)(Math.Sin(a) * 7));
                    if (u is not null) Command(w, u, Order.AttackMove, cx, cy);
                }
                w.News.Add(owner == World.Player ? "THE NOMADS RIDE FOR US" : "NOMADS ARE RAIDING!");
                break;
            }
            case Faction.Sythe:
            {
                var u = Spawn(w, owner, Kind.Saboteur, palace.Door.X, palace.Door.Y);
                if (u is not null && owner != World.Player)
                {
                    var target = w.Buildings.Where(b => !b.Dead && w.Enemies(owner, b.Owner) && b.Type != Kind.Wall).MinBy(b => Field.Dist(b.CX, b.CY, palace.CX, palace.CY));
                    if (target is not null) Attack(w, u, target);
                }
                if (owner == World.Player) w.News.Add("A SABOTEUR AWAITS YOUR ORDERS");
                break;
            }
        }
        return true;
    }

    /// <summary>Seconds a refinery left without a harvester waits before one is flown in, and its price.</summary>
    public const double ReplaceSeconds = 20;
    public const int ReplacePrice = 300;

    /// <summary>A House with a refinery but no harvester (and no heavy factory to build one) has one flown in to the
    /// refinery, for a price, after a short wait.</summary>
    private static void Replace(World w)
    {
        foreach (var h in w.Houses)
        {
            var refinery = w.Buildings.FirstOrDefault(b => !b.Dead && b.Owner == h.Index && b.Type == Kind.Refinery);
            bool none = refinery is not null && !w.Units.Any(u => !u.Dead && u.Owner == h.Index && u.Type == Kind.Harvester) && !h.Order.Contains(Kind.Harvester)
                && !w.Buildings.Any(b => !b.Dead && b.Owner == h.Index && b.Type == Kind.HeavyFactory && b.Queue.Contains(Kind.Harvester));
            if (!none) { h.Replace = -1; continue; }
            if (h.Replace < 0) { h.Replace = w.Clock + ReplaceSeconds; continue; }
            if (w.Clock < h.Replace || h.Credits < ReplacePrice) continue;
            h.Replace = -1;
            var (dx, dy) = refinery!.Door;
            if (Spawn(w, h.Index, Kind.Harvester, dx, dy) is not { } hv) continue;
            h.Credits -= ReplacePrice;
            hv.Order = Order.Harvest;
            w.Fx(Fx.Deliver, hv.X, hv.Y, 1.2);
            w.Hear(Sfx.Lift, hv.X, hv.Y);
            if (h.Index == World.Player) { w.Hear(Sfx.SayUnitReady); w.News.Add("A NEW HARVESTER HAS BEEN FLOWN IN"); }
        }
    }

    // ───────────────────────────── solarite blooms ─────────────────────────────

    /// <summary>Now and then a new bloom swells on the open sand.</summary>
    private static void Blooms(World w)
    {
        if (w.Ticks % 1800 != 0) return;
        int have = 0;
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
            if (w.Bloom[x, y]) have++;
        if (have >= 4) return;
        for (int tries = 0; tries < 40; tries++)
        {
            int x = w.Dice.Next(Field.N), y = w.Dice.Next(Field.N);
            if (!w.IsSandy(x, y) || w.Ore[x, y] > 0 || w.Occ[x, y] is not null) continue;
            if (w.Buildings.Any(b => !b.Dead && b.Reach(x, y) < 6)) continue;
            w.Bloom[x, y] = true;
            w.GroundVersion++;
            return;
        }
    }

    /// <summary>A bloom bursts: solarite thrown over the sand round it, and anything on it hurt.</summary>
    public static void Burst(World w, int x, int y)
    {
        if (!Field.In(x, y) || !w.Bloom[x, y]) return;
        w.Bloom[x, y] = false;
        for (int j = y - 4; j <= y + 4; j++)
        for (int i = x - 4; i <= x + 4; i++)
        {
            if (!w.IsSandy(i, j)) continue;
            double d = Field.Dist(i, j, x, y);
            if (d > 3.6) continue;
            w.Ore[i, j] = Math.Min(900, w.Ore[i, j] + (int)((1 - d / 4.2) * w.Dice.Range(250, 420)));
        }
        foreach (var u in w.Units)
            if (!u.Dead && u.OnGround && Field.Dist(u.X, u.Y, x + 0.5, y + 0.5) < 1.6) Damage(w, u, 70, Weapon.Missile, -1);
        w.Fx(Fx.Bloom, x + 0.5, y + 0.5, 1.4);
        w.Hear(Sfx.Bloom, x + 0.5, y + 0.5);
        w.GroundVersion++;
    }

    // ───────────────────────────── what the player sees ─────────────────────────────

    private static void Reveal(World w)
    {
        if (w.RevealAll) return;
        foreach (var u in w.Units)
            if (!u.Dead && u.Owner == World.Player) See(w, u.X, u.Y, u.Stats.Sight + 0.5);
        foreach (var b in w.Buildings)
            if (!b.Dead && b.Owner == World.Player) See(w, b.CX, b.CY, b.Stats.Sight + Math.Max(b.W, b.H) / 2.0);
    }

    public static void See(World w, double cx, double cy, double r)
    {
        for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
        for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
            if (Field.In(x, y) && (x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy) <= r * r) w.Seen[x, y] = true;
    }

    public static bool Visible(World w, double x, double y) => w.RevealAll || Field.In((int)x, (int)y) && w.Seen[(int)x, (int)y];

    // ───────────────────────────── tidying, winning and losing ─────────────────────────────

    private static void Tidy(World w, double dt)
    {
        w.Units.RemoveAll(u => u.Dead);
        w.Buildings.RemoveAll(b => b.Dead);
        for (int i = w.Effects.Count - 1; i >= 0; i--)
        {
            var e = w.Effects[i];
            e.T += dt;
            if (e.T >= e.Life) w.Effects.RemoveAt(i);
            else w.Effects[i] = e;
        }
        foreach (var b in w.Buildings) b.HitAgo += dt;
    }

    /// <summary>Whether a house still has anything that counts: a building, or a unit on the ground.</summary>
    public static bool Alive(World w, int owner) =>
        w.Buildings.Any(b => !b.Dead && b.Owner == owner && b.Type != Kind.Wall) ||
        w.Units.Any(u => !u.Dead && (u.TrueOwner >= 0 ? u.TrueOwner : u.Owner) == owner && !u.InAir);

    public static void CheckOutcome(World w)
    {
        if (w.Outcome >= 0) return;
        if (!Alive(w, World.Player)) { w.Outcome = 1; return; }
        if (w.Goal == Goal.Harvest && w.PlayerHouse.Harvested >= w.GoalCredits) { w.Outcome = 0; return; }
        if (w.Goal == Goal.Destroy && !w.Houses.Any(h => w.Enemies(World.Player, h.Index) && Alive(w, h.Index))) w.Outcome = 0;
    }
}
