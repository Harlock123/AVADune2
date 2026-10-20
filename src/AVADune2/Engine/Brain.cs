namespace AVADune2.Engine;

/// <summary>
/// A House's mind, for the computer's Houses and for the player's side in the demonstration (the pilot). Twice a
/// second it looks over its base: puts down what the yard has finished (where the same building stood before if it was
/// lost, otherwise near the middle of the base, turrets out toward the enemy), keeps the power up, rebuilds what was
/// destroyed, grows the base as the tech allows, keeps two harvesters to a refinery and a skylifter or two, trains an
/// army, repairs what's damaged, turns its army on anything that comes near its base, sends it out in waves that grow
/// as the battle goes on, and fires its palace's weapon when it's ready.
/// </summary>
public sealed class Brain(int house, bool pilot = false)
{
    public readonly int House = house;
    public readonly bool Pilot = pilot;
    public int BaseX, BaseY, Mission = 5;
    /// <summary>Factories work faster by this much; and this many credits a second come in for free.</summary>
    public double BuildBoost = 1, Income;
    /// <summary>The first attack, the time between attacks, and how many go in each (growing to the most).</summary>
    public double FirstWave = 200, WaveEvery = 110;
    public int WaveSize = 3, WaveMax = 12, ArmyCap = 16;
    /// <summary>A mind for a harvesting mission (the pilot): an economy first, and only a small guard.</summary>
    public bool Harvesting;
    public bool Attacks = true;
    /// <summary>The base as it was laid out: lost buildings go back where they were.</summary>
    public List<(Kind Kind, int X, int Y)> Plan = [];
    public int WavesSent;
    public Thing? WaveTarget;
    private double _next, _nextWave = -1, _blockedUntil, _nextUnit;
    /// <summary>The computer makes a unit at most this often (seconds).</summary>
    public double UnitGap = 8;
    private Kind? _blocked;

    /// <summary>The computer's mind, harder on the later missions.</summary>
    public void Tune(Scenario s)
    {
        int m = s.Mission;
        Mission = m;
        BuildBoost = 1 + (m - 1) * 0.03;
        Income = m * 0.3;
        FirstWave = m <= 2 ? 300 : Math.Max(270, 420 - 20 * m);
        UnitGap = Math.Max(9, 30 - 2.5 * m);
        WaveEvery = Math.Max(70, 180 - 12 * m);
        WaveSize = 2 + m / 3;
        WaveMax = 4 + m;
        ArmyCap = 4 + m * 2;
        if (s.Skirmish)
        {
            FirstWave = 240;
            UnitGap = 6;
            Income = 2;
            WaveMax = 14;
            ArmyCap = 22;
        }
    }

    /// <summary>The pilot: patient, then all in once it has an army.</summary>
    public static Brain ForPilot(World w, bool harvesting)
    {
        var yard = w.BuildingsOf(World.Player).FirstOrDefault(b => b.Type == Kind.Yard);
        var b = new Brain(World.Player, pilot: true)
        {
            BaseX = yard is null ? Field.N / 2 : (int)yard.CX, BaseY = yard is null ? Field.N / 2 : (int)yard.CY,
            Harvesting = harvesting, FirstWave = 130, WaveEvery = 35, WaveSize = 7, WaveMax = 12, ArmyCap = harvesting ? 6 : 30,
        };
        b.Remember(w);
        return b;
    }

    public void Remember(World w) => Plan = w.BuildingsOf(House).Select(b => (b.Type, b.X, b.Y)).ToList();

    public void Update(World w)
    {
        if (w.Clock < _next) return;
        _next = w.Clock + 0.5;
        if (w.Outcome >= 0 || !Sim.Alive(w, House)) return;
        var h = w.Houses[House];
        if (Income > 0 && h.Credits < Math.Max(2500, h.Capacity)) h.Credits += Income * 0.5;
        if (_nextWave < 0) _nextWave = w.Clock + FirstWave;
        var yard = w.BuildingsOf(House).FirstOrDefault(b => b.Type == Kind.Yard);
        if (yard is not null)
        {
            PlaceReady(w, yard);
            Construct(w, h, yard);
        }
        Deploy(w);
        Produce(w, h, yard is not null);
        Repair(w, h);
        Fight(w);
        Palace(w, h);
    }

    // ───────────────────────────── building ─────────────────────────────

    private bool Can(World w, Kind k) => Sim.CanBuild(w, House, k) && !(k == _blocked && w.Clock < _blockedUntil);

    private (Kind Kind, int Count)[] Wants(House h)
    {
        if (Harvesting)
            return [(Kind.Refinery, 1), (Kind.Turbine, 2), (Kind.Refinery, 2), (Kind.Silo, 1), (Kind.Barracks, 1), (Kind.Outpost, 1), (Kind.LightFactory, 1), (Kind.Silo, 3)];
        if (Pilot)
            return h.Tech >= 3
                ? [(Kind.Refinery, 1), (Kind.LightFactory, 1), (Kind.Outpost, 1), (Kind.HeavyFactory, 1), (Kind.Turret, 2), (Kind.Refinery, 2), (Kind.Silo, 1), (Kind.Turret, 3),
                    (Kind.RepairPad, 1), (Kind.HighTech, 1), (Kind.RocketTurret, 2), (Kind.HeavyFactory, 2), (Kind.Refinery, 3), (Kind.Palace, 1), (Kind.Silo, 3)]
                : [(Kind.Refinery, 1), (Kind.Barracks, 1), (Kind.LightFactory, 1), (Kind.Outpost, 1), (Kind.Refinery, 2), (Kind.Silo, 2)];
        return [(Kind.Refinery, 1), (Kind.Barracks, 1), (Kind.LightFactory, 1), (Kind.Outpost, 1), (Kind.Silo, 1), (Kind.HeavyFactory, 1), (Kind.Refinery, 2),
            (Kind.Turret, 2), (Kind.RepairPad, 1), (Kind.RocketTurret, 2), (Kind.HighTech, 1), (Kind.Turret, 3), (Kind.Starport, Mission >= 7 ? 1 : 0),
            (Kind.Palace, 1), (Kind.RocketTurret, 4)];
    }

    /// <summary>What the yard should build next, if anything.</summary>
    public Kind? NextBuilding(World w, House h, Building yard)
    {
        int Have(Kind k) => w.Count(House, k) + (yard.Ready == k ? 1 : 0) + yard.Queue.Count(q => q == k);
        double spare = h.PowerMade - h.PowerUsed;
        if (Can(w, Kind.Turbine) && spare < 20 && Have(Kind.Turbine) < 12) return Kind.Turbine;
        foreach (var group in Plan.GroupBy(p => p.Kind))
            if (Can(w, group.Key) && Have(group.Key) < group.Count() && group.Key != Kind.Yard) return group.Key;
        foreach (var (k, n) in Wants(h))
        {
            if (!Can(w, k) || Have(k) >= n) continue;
            if (k == Kind.Silo && h.Credits < h.Capacity * 0.6) continue;
            if (Rules.Of(k).Power < 0 && spare + Rules.Of(k).Power < 0 && Can(w, Kind.Turbine)) return Kind.Turbine;
            return k;
        }
        return null;
    }

    private void Construct(World w, House h, Building yard)
    {
        if (yard.Ready is not null || yard.Queue.Count > 0) return;
        if (NextBuilding(w, h, yard) is { } k && h.Credits >= Math.Min(Rules.Of(k).Cost * 0.3, 200)) Sim.Enqueue(w, yard, k);
    }

    /// <summary>Where the enemy is: the nearest enemy base (or the middle of the map).</summary>
    private (int X, int Y) Enemy(World w)
    {
        var b = w.Buildings.Where(o => !o.Dead && w.Enemies(House, o.Owner)).MinBy(o => Field.Dist(o.CX, o.CY, BaseX, BaseY));
        return b is null ? (Field.N / 2, Field.N / 2) : ((int)b.CX, (int)b.CY);
    }

    private void PlaceReady(World w, Building yard)
    {
        if (yard.Ready is not { } k) return;
        (int X, int Y)? spot = null;
        foreach (var p in Plan)
            if (p.Kind == k && Sim.CanPlace(w, House, k, p.X, p.Y) is null) { spot = (p.X, p.Y); break; }
        if (spot is null)
        {
            int ax = BaseX, ay = BaseY;
            if (k is Kind.Turret or Kind.RocketTurret)
            {
                var (ex, ey) = Enemy(w);
                double a = Math.Atan2(ey - BaseY, ex - BaseX) + w.Dice.Range(-0.7, 0.7);
                ax = BaseX + (int)Math.Round(Math.Cos(a) * 6);
                ay = BaseY + (int)Math.Round(Math.Sin(a) * 6);
            }
            spot = Sim.FindSpot(w, House, k, ax, ay, 16);
        }
        if (spot is var (x, y) && Sim.PlaceReady(w, yard, x, y))
        {
            if (!Plan.Contains((k, x, y)) && k != Kind.Slab) Plan.Add((k, x, y));
            return;
        }
        Sim.Cancel(w, yard, k);
        _blocked = k;
        _blockedUntil = w.Clock + 45;
    }

    private void Deploy(World w)
    {
        foreach (var u in w.UnitsOf(House))
        {
            if (u.Type != Kind.Mcv || u.Order is Order.Deploy or Order.Move) continue;
            if (Sim.CanDeploy(w, u)) Sim.Command(w, u, Order.Deploy, u.Tx, u.Ty);
            else Sim.Command(w, u, Order.Move, BaseX + w.Dice.Next(7) - 3, BaseY + w.Dice.Next(7) - 3);
        }
    }

    // ───────────────────────────── units ─────────────────────────────

    private List<Unit> Army(World w) =>
        w.Units.Where(u => !u.Dead && u.Owner == House && u.TrueOwner < 0 && u.OnGround && Rules.Fighter(u.Type) && u.Type != Kind.Nomads).ToList();

    private Kind Pick(World w, params (Kind Kind, int Weight)[] options)
    {
        var ok = options.Where(o => o.Weight > 0 && Can(w, o.Kind)).ToList();
        if (ok.Count == 0) return options[0].Kind;
        int total = ok.Sum(o => o.Weight), roll = w.Dice.Next(total);
        foreach (var o in ok)
        {
            if (roll < o.Weight) return o.Kind;
            roll -= o.Weight;
        }
        return ok[^1].Kind;
    }

    private void Produce(World w, House h, bool haveYard)
    {
        int army = Army(w).Count, refineries = w.Count(House, Kind.Refinery), harvesters = w.Count(House, Kind.Harvester);
        int wantHarvesters = Math.Min(refineries * 2, Harvesting ? 6 : 4);
        bool heavy = w.Has(House, Kind.HeavyFactory);
        int reserve = Pilot ? 150 : 100;
        // The pilot puts its base first: no new units while the yard is busy, unless it's rich or under attack.
        var yard = w.BuildingsOf(House).FirstOrDefault(b => b.Type == Kind.Yard);
        bool saving = yard is not null && (yard.Queue.Count > 0 || yard.Ready is not null) && h.Credits < 700 && Threats(w).Count == 0;
        var (rx, ry) = Rally(w);
        void Train(Building b, Kind k)
        {
            if (!Sim.Enqueue(w, b, k) || Pilot) return;
            army++;
            _nextUnit = w.Clock + UnitGap;
        }
        foreach (var b in w.BuildingsOf(House).ToList())
        {
            if (!Rules.Makes.ContainsKey(b.Type) || b.Type == Kind.Yard || b.Queue.Count > 0) continue;
            b.RallyX = rx;
            b.RallyY = ry;
            bool more = army < ArmyCap && h.Credits > reserve && (Pilot ? !saving : w.Clock >= _nextUnit);
            switch (b.Type)
            {
                case Kind.HeavyFactory:
                    if (!haveYard && Can(w, Kind.Mcv) && w.Count(House, Kind.Mcv) == 0) Sim.Enqueue(w, b, Kind.Mcv);
                    else if (harvesters < wantHarvesters && Can(w, Kind.Harvester)) { Sim.Enqueue(w, b, Kind.Harvester); harvesters++; }
                    else if (more)
                    {
                        var special = Rules.Special(h.Faction);
                        Train(b, Pick(w, (Kind.Tank, 5), (Kind.RocketTank, 3), (Kind.SiegeTank, 2), (special, h.Faction == Faction.Sythe ? 1 : 2)));
                    }
                    break;
                case Kind.LightFactory:
                    if (more && (!heavy || !Pilot && w.Dice.Next() < 0.25)) Train(b, Pick(w, (Kind.Trike, 2), (Kind.Quad, 3)));
                    break;
                case Kind.Barracks:
                    if (more && w.Dice.Next() < (heavy ? Pilot ? 0 : 0.2 : 0.6)) Train(b, Pick(w, (Kind.Infantry, 2), (Kind.Troopers, 2)));
                    break;
                case Kind.HighTech:
                    if (w.Count(House, Kind.Skylifter) < (harvesters > 2 ? 2 : 1) && Can(w, Kind.Skylifter)) Sim.Enqueue(w, b, Kind.Skylifter);
                    break;
            }
        }
        if (!Pilot && h.Credits > 2500 && army < ArmyCap && w.Dice.Next() < 0.1) Sim.Buy(w, House, Kind.Tank);
    }

    /// <summary>Where new units gather: a little out from the base, toward the enemy.</summary>
    private (int X, int Y) Rally(World w)
    {
        var (ex, ey) = Enemy(w);
        double a = Math.Atan2(ey - BaseY, ex - BaseX);
        int x = Math.Clamp(BaseX + (int)Math.Round(Math.Cos(a) * 5), 1, Field.N - 2), y = Math.Clamp(BaseY + (int)Math.Round(Math.Sin(a) * 5), 1, Field.N - 2);
        return Sim.FreeTile(w, x, y, Mover.Tracks, 4) ?? (x, y);
    }

    private void Repair(World w, House h)
    {
        foreach (var b in w.BuildingsOf(House))
            if (b.Hp < b.MaxHp * 0.6 && !b.Repairing && h.Credits > 300) b.Repairing = true;
        if (!w.Has(House, Kind.RepairPad)) return;
        foreach (var u in Army(w))
            if (u.Hp < u.MaxHp * 0.35 && u.Order is Order.Idle or Order.Guard && h.Credits > 200 && !Rules.IsInfantry(u.Type)) Sim.Command(w, u, Order.Repair, u.Tx, u.Ty);
    }

    // ───────────────────────────── fighting ─────────────────────────────

    /// <summary>The enemy's units near the base.</summary>
    public List<Unit> Threats(World w)
    {
        var mine = w.BuildingsOf(House).ToList();
        return w.Units.Where(u => !u.Dead && u.OnGround && w.Enemies(House, u.Owner) && mine.Any(b => b.Reach(u.X, u.Y) < 7)).ToList();
    }

    private void Fight(World w)
    {
        var army = Army(w);
        var threats = Threats(w);
        if (threats.Count > 0)
        {
            foreach (var u in army)
            {
                if (u.Order is not (Order.Idle or Order.Guard or Order.Move) || Field.Dist(u.X, u.Y, BaseX, BaseY) > 22) continue;
                var t = threats.MinBy(t => Field.Dist(t.X, t.Y, u.X, u.Y))!;
                Sim.Attack(w, u, t);
            }
            return;
        }
        if (!Attacks || Harvesting && Pilot) return;
        var ready = army.Where(u => u.Order is Order.Idle or Order.Guard).ToList();
        bool time = w.Clock >= _nextWave && ready.Count >= WaveSize;
        // A great army waiting goes early, but never before the first wave is due.
        bool massive = w.Clock >= FirstWave && ready.Count >= Math.Max(WaveMax, ArmyCap - 2);
        // The pilot waits until it's clearly the stronger.
        if (Pilot && time && Strength(w, ready) < 1.3 * EnemyStrength(w) && ready.Count < 22) time = false;
        if (!time && !massive) return;
        var target = ChooseTarget(w);
        if (target is null) return;
        WaveTarget = target;
        WavesSent++;
        Sim.MoveGroup(w, ready, Order.AttackMove, (int)target.CX, (int)target.CY);
        _nextWave = w.Clock + WaveEvery;
        WaveSize = Math.Min(WaveSize + 1, WaveMax);
    }

    /// <summary>An army's worth: what its units cost, as damaged as they are.</summary>
    public static double Strength(World w, IEnumerable<Unit> units) => units.Sum(u => u.Stats.Cost * u.Hp / u.MaxHp);

    /// <summary>The enemy's fighters and turrets, by the same measure (a turret counts as a tank and a half).</summary>
    public double EnemyStrength(World w) =>
        Strength(w, w.Units.Where(u => !u.Dead && u.OnGround && w.Enemies(House, u.Owner) && Rules.Fighter(u.Type))) +
        w.Buildings.Where(b => !b.Dead && w.Enemies(House, b.Owner) && b.Stats.Turret).Sum(b => 450 * b.Hp / b.MaxHp);

    /// <summary>What a wave goes for: the enemy building nearest home (now and then a harvester), or, with no
    /// buildings left, whatever enemy unit is nearest.</summary>
    public Thing? ChooseTarget(World w)
    {
        if (!Pilot && w.Dice.Next() < 0.25)
        {
            var harvester = w.Units.Where(u => !u.Dead && u.OnGround && u.Type == Kind.Harvester && w.Enemies(House, u.Owner)).MinBy(u => Field.Dist(u.X, u.Y, BaseX, BaseY));
            if (harvester is not null) return harvester;
        }
        var b = w.Buildings.Where(o => !o.Dead && w.Enemies(House, o.Owner) && o.Type != Kind.Wall).MinBy(o => Field.Dist(o.CX, o.CY, BaseX, BaseY));
        if (b is not null) return b;
        return (Thing?)w.Buildings.Where(o => !o.Dead && w.Enemies(House, o.Owner)).MinBy(o => Field.Dist(o.CX, o.CY, BaseX, BaseY))
            ?? w.Units.Where(u => !u.Dead && !u.InAir && w.Enemies(House, u.Owner)).MinBy(u => Field.Dist(u.X, u.Y, BaseX, BaseY));
    }

    private void Palace(World w, House h)
    {
        if (!h.PalaceReady) return;
        var target = w.Buildings.Where(o => !o.Dead && w.Enemies(House, o.Owner)).OrderByDescending(o => o.Type == Kind.Yard).ThenBy(o => Field.Dist(o.CX, o.CY, BaseX, BaseY)).FirstOrDefault();
        if (target is not null) Sim.FirePalace(w, House, (int)target.CX, (int)target.CY);
    }
}
