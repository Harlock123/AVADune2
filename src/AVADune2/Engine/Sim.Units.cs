namespace AVADune2.Engine;

public static partial class Sim
{
    private const int N = Field.N;

    // ───────────────────────────── orders ─────────────────────────────

    /// <summary>Gives a unit an order to carry out at a tile: move, attack-move, guard, harvest, return, repair,
    /// deploy, or stop (idle).</summary>
    public static void Command(World w, Unit u, Order o, int x, int y)
    {
        if (u.Dead || u.InAir || u.CarriedBy is not null) return;
        Undock(u);
        u.Target = null;
        u.Retry = 0;
        u.Blocked = 0;
        x = Math.Clamp(x, 0, N - 1);
        y = Math.Clamp(y, 0, N - 1);
        switch (o)
        {
            case Order.Move or Order.AttackMove:
                u.Order = o;
                u.HomeX = x;
                u.HomeY = y;
                RequestPath(w, u, x, y);
                break;
            case Order.Harvest:
                if (u.Type != Kind.Harvester) return;
                u.Order = Order.Harvest;
                if (w.Ore[x, y] > 0) RequestPath(w, u, x, y);
                else u.Path = null;
                break;
            case Order.Return:
                if (u.Type != Kind.Harvester) return;
                u.Order = Order.Return;
                u.Path = null;
                break;
            case Order.Deploy:
                if (u.Type != Kind.Mcv) return;
                u.Order = Order.Deploy;
                u.Path = null;
                break;
            case Order.Guard or Order.Repair:
                u.Order = o;
                u.HomeX = u.Tx;
                u.HomeY = u.Ty;
                u.Path = null;
                break;
            default:
                Halt(u);
                break;
        }
    }

    /// <summary>Sends a unit at a building or unit of the enemy's (a saboteur goes in to blow it up).</summary>
    public static void Attack(World w, Unit u, Thing target)
    {
        if (u.Dead || u.InAir || u.CarriedBy is not null || target.Dead) return;
        if (!Rules.Armed(u.Type) && u.Type != Kind.Saboteur) { Command(w, u, Order.Move, (int)target.CX, (int)target.CY); return; }
        if (u.Type == Kind.Saboteur && target is not Building) { Command(w, u, Order.Move, (int)target.CX, (int)target.CY); return; }
        Undock(u);
        u.Order = u.Type == Kind.Saboteur ? Order.Sabotage : Order.Attack;
        u.Target = target;
        u.Retry = 0;
        u.Blocked = 0;
        RequestPath(w, u, (int)target.CX, (int)target.CY);
    }

    /// <summary>Stop: stand and guard where it is.</summary>
    public static void Halt(Unit u)
    {
        u.Order = Order.Idle;
        u.Path = null;
        u.PathPending = false;
        u.Target = null;
        u.HomeX = u.Nx >= 0 ? u.Nx : u.Tx;
        u.HomeY = u.Nx >= 0 ? u.Ny : u.Ty;
    }

    private static void Undock(Unit u)
    {
        if (u.Order == Order.Docked && u.Dock is { } d && d.Docked == u) d.Docked = null;
    }

    /// <summary>
    /// Moves a group to a place: each unit gets a tile of its own round it, the nearest units the nearest tiles, so
    /// they don't all fight for the one spot.
    /// </summary>
    public static void MoveGroup(World w, IReadOnlyList<Unit> units, Order o, int x, int y)
    {
        var taken = new HashSet<int>();
        foreach (var u in units.Where(u => !u.Dead && u.OnGround).OrderBy(u => Field.Dist(u.X, u.Y, x + 0.5, y + 0.5)))
        {
            var m = u.Stats.Mover;
            (int X, int Y) spot = (x, y);
            bool found = false;
            for (int r = 0; r <= 6 && !found; r++)
            for (int dy = -r; dy <= r && !found; dy++)
            for (int dx = -r; dx <= r && !found; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                int tx = x + dx, ty = y + dy;
                if (!PathFinder.Passable(w, tx, ty, m) || taken.Contains(ty * N + tx)) continue;
                if (w.Occ[tx, ty] is { } o2 && o2 != u && !o2.Moving && !units.Contains(o2)) continue;
                spot = (tx, ty);
                found = true;
            }
            taken.Add(spot.Y * N + spot.X);
            if (u.Type == Kind.Harvester && o == Order.Move && w.Ore[x, y] > 0) Command(w, u, Order.Harvest, spot.X, spot.Y);
            else Command(w, u, o, spot.X, spot.Y);
        }
    }

    // ───────────────────────────── each unit ─────────────────────────────

    private static void UpdateUnit(World w, Unit u, double dt)
    {
        if (u.CarriedBy is { } c)
        {
            u.X = c.X;
            u.Y = c.Y + 0.15;
            return;
        }
        u.HitAgo += dt;
        u.Reload -= dt;
        u.Think -= dt;
        if (u.Turned > 0)
        {
            u.Turned -= dt;
            if (u.Turned <= 0) Unturn(w, u);
        }
        if (u.InAir) { Fly(w, u, dt); return; }
        switch (u.Order)
        {
            case Order.Idle or Order.Guard: Guard(w, u, dt); break;
            case Order.Move:
                if (Travel(w, u, dt)) Halt(u);
                else if (u.Stats.Turret) ShootOnTheMove(w, u, dt);
                break;
            case Order.AttackMove: AttackMove(w, u, dt); break;
            case Order.Attack or Order.Sabotage: Fight(w, u, dt); break;
            case Order.Harvest: Harvest(w, u, dt); break;
            case Order.Return: Return(w, u, dt); break;
            case Order.Docked: Unload(w, u, dt); break;
            case Order.Repair: GoRepair(w, u, dt); break;
            case Order.Deploy: Deploy(w, u, dt); break;
        }
        if (!u.Stats.Turret && Rules.IsUnit(u.Type) && u.Moving) u.Aim = u.Facing;
    }

    /// <summary>
    /// A step along the way: into the next tile (claiming it first), turning to face it. A unit in the way makes it
    /// wait, and if it stays in the way, find a way round. Tracked vehicles drive over enemy infantry. True when the
    /// way is done (or given up on).
    /// </summary>
    private static bool Travel(World w, Unit u, double dt)
    {
        var st = u.Stats;
        if (u.Nx < 0)
        {
            if (u.PathPending) { u.Moving = false; return false; }
            if (u.Path is null || u.PathAt >= u.Path.Count) { u.Path = null; u.Moving = false; return true; }
            int next = u.Path[u.PathAt], nx = next % N, ny = next / N;
            if (Math.Abs(nx - u.Tx) > 1 || Math.Abs(ny - u.Ty) > 1 || !PathFinder.Passable(w, nx, ny, st.Mover))
            {
                RequestPath(w, u, u.Gx, u.Gy, true);
                return false;
            }
            if (w.Occ[nx, ny] is { } o && o != u)
            {
                if (st.Mover == Mover.Tracks && Rules.IsInfantry(o.Type) && w.Enemies(u.Owner, o.Owner) && o.Nx < 0)
                {
                    w.Fx(Fx.Crush, o.X, o.Y, 0.8);
                    w.Hear(Sfx.Crush, o.X, o.Y);
                    Kill(w, o, u.Owner);
                }
                else
                {
                    u.Wait += dt;
                    u.Moving = false;
                    if (u.Wait > 0.35 && !o.Moving || u.Wait > 1.2)
                    {
                        u.Wait = 0;
                        bool last = u.PathAt >= u.Path.Count - 1;
                        if (last || ++u.Blocked > 6)
                        {
                            u.Path = null;
                            u.Blocked = 0;
                            return true;
                        }
                        RequestPath(w, u, u.Gx, u.Gy, true);
                    }
                    return false;
                }
            }
            w.Occ[nx, ny] = u;
            u.Nx = nx;
            u.Ny = ny;
            u.PathAt++;
            u.Wait = 0;
        }
        double tx = u.Nx + 0.5, ty = u.Ny + 0.5;
        double want = Math.Atan2(ty - u.Y, tx - u.X);
        u.Moving = true;
        if (st.Mover == Mover.Foot) u.Facing = want;
        else
        {
            double turn = (st.Mover == Mover.Wheels ? 6 : 3.6) * dt;
            u.Facing = Turn(u.Facing, want, turn);
            if (Math.Abs(Diff(u.Facing, want)) > 0.6) return false;
        }
        double speed = st.Speed * PathFinder.SpeedOn(w, u.Nx, u.Ny, st.Mover) * dt;
        double d = Field.Dist(u.X, u.Y, tx, ty);
        if (speed >= d)
        {
            u.X = tx;
            u.Y = ty;
            if (w.Occ[u.Tx, u.Ty] == u && (u.Tx != u.Nx || u.Ty != u.Ny)) w.Occ[u.Tx, u.Ty] = null;
            u.Tx = u.Nx;
            u.Ty = u.Ny;
            u.Nx = u.Ny = -1;
            if (w.Bloom[u.Tx, u.Ty]) Burst(w, u.Tx, u.Ty);
        }
        else
        {
            u.X += (tx - u.X) / d * speed;
            u.Y += (ty - u.Y) / d * speed;
        }
        return false;
    }

    /// <summary>Idle or on guard: shoot what comes near (a guard goes further after it), then back to its post.
    /// An idle harvester goes back to work.</summary>
    private static void Guard(World w, Unit u, double dt)
    {
        if (u.Type == Kind.Harvester)
        {
            Travel(w, u, dt);
            if (u.Think <= 0 && u.Path is null && !u.PathPending)
            {
                u.Think = 2;
                if (w.Has(u.Owner, Kind.Refinery)) u.Order = u.Cargo > Rules.HarvesterLoad * 0.9 ? Order.Return : Order.Harvest;
            }
            return;
        }
        if (!Rules.Armed(u.Type)) { Travel(w, u, dt); return; }
        if (u.Target is { } t0 && !Valid(w, u, t0)) u.Target = null;
        if (u.Think <= 0)
        {
            u.Think = 0.4 + (u.Id % 7) * 0.03;
            if (u.Target is null || !InRange(u, u.Target)) u.Target = FindTarget(w, u, u.Stats.Sight + (u.Order == Order.Guard ? 1.5 : 0)) ?? u.Target;
        }
        if (u.Target is { } t)
        {
            double chase = u.Order == Order.Guard ? 8 : 5;
            if (!InRange(u, t) && Field.Dist(u.X, u.Y, u.HomeX + 0.5, u.HomeY + 0.5) > chase)
            {
                u.Target = null;
                u.Retry = 0;
                RequestPath(w, u, u.HomeX, u.HomeY);
            }
            else Engage(w, u, t, dt);
            return;
        }
        if (ShootCreature(w, u)) { Travel(w, u, dt); return; }
        Travel(w, u, dt);
    }

    private static void ShootOnTheMove(World w, Unit u, double dt)
    {
        if (u.Target is { } t0 && (!Valid(w, u, t0) || !InRange(u, t0))) u.Target = null;
        if (u.Think <= 0)
        {
            u.Think = 0.4;
            u.Target ??= FindTarget(w, u, u.Stats.Range);
        }
        if (u.Target is { } t) AimAndFire(w, u, t, dt);
    }

    /// <summary>On the way somewhere, but fighting whatever it meets; then on again.</summary>
    private static void AttackMove(World w, Unit u, double dt)
    {
        if (u.Target is { } t0 && !Valid(w, u, t0))
        {
            u.Target = null;
            u.Path = null;
        }
        if (u.Think <= 0)
        {
            u.Think = 0.45 + (u.Id % 5) * 0.03;
            if (u.Target is null || !InRange(u, u.Target)) u.Target = FindTarget(w, u, u.Stats.Sight + 1) ?? u.Target;
        }
        if (u.Target is { } t) { Engage(w, u, t, dt); return; }
        if (u.Tx == u.HomeX && u.Ty == u.HomeY && u.Nx < 0) { Halt(u); return; }
        if (u.Path is null && !u.PathPending)
        {
            u.Retry -= dt;
            if (u.Retry > 0) return;
            if (Field.Dist(u.Tx, u.Ty, u.HomeX, u.HomeY) < 1.6 && u.Nx < 0) { Halt(u); return; }
            u.Retry = 1;
            RequestPath(w, u, u.HomeX, u.HomeY);
            return;
        }
        if (Travel(w, u, dt) && Field.Dist(u.Tx, u.Ty, u.HomeX, u.HomeY) < 2.5) Halt(u);
    }

    /// <summary>Attacking one thing until it's gone (a saboteur goes right up to it and blows it up).</summary>
    private static void Fight(World w, Unit u, double dt)
    {
        if (u.Target is not { } t || !Valid(w, u, t)) { Halt(u); return; }
        if (u.Order == Order.Sabotage && t is Building b)
        {
            if (b.Reach(u.X, u.Y) < 1.3)
            {
                w.Fx(Fx.BigBoom, b.CX, b.CY, 1.4);
                Damage(w, b, 2000, Weapon.Missile, u.Owner);
                Kill(w, u, -1);
                return;
            }
            Approach(w, u, t, dt);
            return;
        }
        Engage(w, u, t, dt);
    }

    /// <summary>Into range of a target and shooting it.</summary>
    private static void Engage(World w, Unit u, Thing t, double dt)
    {
        if (InRange(u, t))
        {
            u.Path = null;
            u.PathPending = false;
            if (u.Nx >= 0) Travel(w, u, dt);
            else u.Moving = false;
            AimAndFire(w, u, t, dt);
            return;
        }
        // Too close for rockets: back off a little.
        if (u.Stats.MinRange > 0 && Reach(u, t) < u.Stats.MinRange)
        {
            if (u.Path is null && !u.PathPending && u.Nx < 0)
            {
                double a = Math.Atan2(u.Y - t.CY, u.X - t.CX);
                RequestPath(w, u, (int)(u.X + Math.Cos(a) * 2.5), (int)(u.Y + Math.Sin(a) * 2.5));
            }
            Travel(w, u, dt);
            return;
        }
        Approach(w, u, t, dt);
        if (u.Stats.Turret && u.Target == t) u.Aim = Turn(u.Aim, Math.Atan2(t.CY - u.Y, t.CX - u.X), 4 * dt);
    }

    /// <summary>Heads for a target, finding the way again now and then as it moves.</summary>
    private static void Approach(World w, Unit u, Thing t, double dt)
    {
        u.Retry -= dt;
        bool moved = t is Unit tu && Math.Abs(tu.Tx - u.Gx) + Math.Abs(tu.Ty - u.Gy) > 2;
        if ((u.Path is null || moved) && !u.PathPending && u.Retry <= 0)
        {
            u.Retry = u.Path is null ? 0.6 : 1.2;
            RequestPath(w, u, (int)t.CX, (int)t.CY);
        }
        Travel(w, u, dt);
    }

    // ───────────────────────────── harvesters ─────────────────────────────

    /// <summary>Out to the solarite, gathering until full (or the field's gone), then home to the refinery.</summary>
    private static void Harvest(World w, Unit u, double dt)
    {
        if (u.Cargo >= Rules.HarvesterLoad - 0.5) { u.Order = Order.Return; u.Path = null; u.PathPending = false; return; }
        if (u.Nx < 0 && !u.PathPending && (u.Path is null || u.PathAt >= u.Path.Count) && w.Ore[u.Tx, u.Ty] > 0)
        {
            u.Path = null;
            u.Moving = false;
            u.Gather += Rules.HarvestRate * dt;
            int take = Math.Min((int)u.Gather, Math.Min(w.Ore[u.Tx, u.Ty], (int)Math.Ceiling(Rules.HarvesterLoad - u.Cargo)));
            if (take > 0)
            {
                int before = w.Ore[u.Tx, u.Ty];
                w.Ore[u.Tx, u.Ty] -= take;
                u.Gather -= take;
                u.Cargo = Math.Min(Rules.HarvesterLoad, u.Cargo + take);
                if (before / 100 != w.Ore[u.Tx, u.Ty] / 100 || w.Ore[u.Tx, u.Ty] == 0) w.GroundVersion++;
            }
            if ((w.Ticks + u.Id) % 150 == 0) w.Hear(Sfx.Harvest, u.X, u.Y);
            return;
        }
        if (u.Path is null && !u.PathPending)
        {
            u.Retry -= dt;
            if (u.Retry > 0) return;
            u.Retry = 0.8;
            if (FindOre(w, u) is var (ox, oy)) RequestPath(w, u, ox, oy);
            else if (u.Cargo > 0) u.Order = Order.Return;
            else { u.Order = Order.Idle; u.Think = 6; }
            return;
        }
        Travel(w, u, dt);
    }

    /// <summary>The nearest solarite no other unit is standing on, looking out from the harvester.</summary>
    public static (int X, int Y)? FindOre(World w, Unit u)
    {
        var claimed = new HashSet<int>();
        foreach (var h in w.Units)
            if (h != u && !h.Dead && h.Type == Kind.Harvester && h.Order == Order.Harvest && h.Gx >= 0) claimed.Add(h.Gy * N + h.Gx);
        for (int r = 0; r < N; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
            int x = u.Tx + dx, y = u.Ty + dy;
            if (!Field.In(x, y) || w.Ore[x, y] <= 0) continue;
            if (w.Occ[x, y] is { } o && o != u) continue;
            if (claimed.Contains(y * N + x)) continue;
            return (x, y);
        }
        return null;
    }

    /// <summary>The refinery a harvester should take its load to: the nearest, a free one if it's not much further.</summary>
    public static Building? RefineryFor(World w, Unit u) =>
        w.Buildings.Where(b => !b.Dead && b.Owner == u.Owner && b.Type == Kind.Refinery)
            .MinBy(b => Field.Dist(b.Door.X, b.Door.Y, u.Tx, u.Ty) + (b.Docked is not null && b.Docked != u ? 6 : 0));

    private static void Return(World w, Unit u, double dt)
    {
        if (u.Dock is not { Dead: false } dock || dock.Owner != u.Owner || u.Think <= 0 && u.Path is null)
        {
            u.Dock = RefineryFor(w, u);
            u.Think = 3;
            if (u.Dock is null) { u.Order = Order.Idle; u.Think = 5; return; }
            dock = u.Dock;
        }
        var (dx, dy) = dock.Door;
        // Into the intake: from the tile in front of it, or (if that's blocked) any tile beside the refinery.
        bool there = u.Tx == dx && u.Ty == dy || dock.Reach(u.X, u.Y) <= 1.0 && w.Occ[dx, dy] is not null && w.Occ[dx, dy] != u
            || dock.Reach(u.X, u.Y) <= 1.0 && w.Bld[Math.Clamp(dx, 0, N - 1), Math.Clamp(dy, 0, N - 1)] is not null;
        if (there && u.Nx < 0)
        {
            u.Path = null;
            u.Moving = false;
            if (dock.Docked is null || dock.Docked == u)
            {
                dock.Docked = u;
                u.Order = Order.Docked;
                u.Facing = -Math.PI / 2;
            }
            return;
        }
        if (u.Path is null && !u.PathPending)
        {
            u.Retry -= dt;
            if (u.Retry > 0) return;
            u.Retry = 0.7;
            RequestPath(w, u, dx, dy);
            return;
        }
        Travel(w, u, dt);
    }

    private static void Unload(World w, Unit u, double dt)
    {
        if (u.Dock is not { Dead: false } dock) { u.Order = Order.Return; return; }
        double amount = Math.Min(Rules.UnloadRate * dt, u.Cargo);
        u.Cargo -= amount;
        Store(w, u.Owner, amount);
        if (u.Cargo > 1e-6) return;
        u.Cargo = 0;
        dock.Docked = null;
        u.Order = Order.Harvest;
        u.Retry = 0;
    }

    // ───────────────────────────── repairs, deploying ─────────────────────────────

    private static void GoRepair(World w, Unit u, double dt)
    {
        var pad = w.Buildings.Where(b => !b.Dead && b.Owner == u.Owner && b.Type == Kind.RepairPad).MinBy(b => b.Reach(u.X, u.Y));
        if (pad is null || u.Hp >= u.MaxHp || u.Blocked > 12) { Halt(u); return; }
        if (pad.Reach(u.X, u.Y) <= 1.0 && u.Nx < 0)
        {
            u.Path = null;
            u.Moving = false;
            var h = w.Houses[u.Owner];
            double hp = Math.Min(u.MaxHp - u.Hp, RepairRate * 1.4 * dt), cost = hp * u.Stats.Cost / u.MaxHp * RepairPrice;
            if (h.Credits >= cost)
            {
                h.Credits -= cost;
                u.Hp += hp;
                if ((w.Ticks + u.Id) % 20 == 0) w.Fx(Fx.Spark, u.X + w.Dice.Range(-0.3, 0.3), u.Y + w.Dice.Range(-0.3, 0.3), 0.3);
            }
            if (u.Hp >= u.MaxHp - 1e-6) { u.Hp = u.MaxHp; Halt(u); }
            return;
        }
        if (u.Path is null && !u.PathPending)
        {
            u.Retry -= dt;
            if (u.Retry > 0) return;
            u.Retry = 1;
            u.Blocked++;
            RequestPath(w, u, (int)pad.CX, (int)pad.CY);
            return;
        }
        Travel(w, u, dt);
    }

    /// <summary>Whether a mobile yard can unfold where it stands: open rock, two tiles by two, its tile the top-left.</summary>
    public static bool CanDeploy(World w, Unit u)
    {
        for (int y = u.Ty; y < u.Ty + 2; y++)
        for (int x = u.Tx; x < u.Tx + 2; x++)
        {
            if (!Field.In(x, y) || w.Ground[x, y] != Ground.Rock || w.Bld[x, y] is not null) return false;
            if (w.Occ[x, y] is { } o && o != u) return false;
        }
        return true;
    }

    private static void Deploy(World w, Unit u, double dt)
    {
        if (u.Nx >= 0) { u.Path = null; Travel(w, u, dt); return; }
        if (!CanDeploy(w, u))
        {
            if (u.Owner == World.Player) w.News.Add("A MOBILE YARD MUST UNFOLD ON OPEN ROCK");
            w.Hear(Sfx.Refused);
            Halt(u);
            return;
        }
        u.Dead = true;
        w.Occ[u.Tx, u.Ty] = null;
        Put(w, u.Owner, Kind.Yard, u.Tx, u.Ty);
        w.Hear(Sfx.Place, u.X, u.Y);
        if (u.Owner == World.Player) { w.Hear(Sfx.SayBuilt); w.News.Add("CONSTRUCTION YARD READY"); }
    }

    private static void Unturn(World w, Unit u)
    {
        if (u.TrueOwner >= 0) u.Owner = u.TrueOwner;
        u.TrueOwner = -1;
        u.Turned = 0;
        w.Fx(Fx.Turned, u.X, u.Y, 0.8);
        Halt(u);
    }

    // ───────────────────────────── skylifters ─────────────────────────────

    private static void Fly(World w, Unit u, double dt)
    {
        if (u.Carrying is { } load)
        {
            if (load.Dead) { u.Carrying = null; return; }
            if (FlyTo(u, u.DropX + 0.5, u.DropY + 0.5, dt) && FreeTile(w, u.DropX, u.DropY, load.Stats.Mover, 6) is var (fx, fy))
            {
                load.CarriedBy = null;
                load.Tx = fx;
                load.Ty = fy;
                load.Nx = load.Ny = -1;
                load.X = fx + 0.5;
                load.Y = fy + 0.5;
                load.Path = null;
                load.Retry = 0;
                load.Think = 0;
                w.Occ[fx, fy] = load;
                u.Carrying = null;
                w.Hear(Sfx.Lift, u.X, u.Y);
            }
            return;
        }
        if (u.Target is Unit h)
        {
            if (h.Dead || h.CarriedBy is not null || h.Order is not (Order.Harvest or Order.Return)) { u.Target = null; return; }
            if (FlyTo(u, h.X, h.Y, dt) && h.Nx < 0)
            {
                if (w.Occ[h.Tx, h.Ty] == h) w.Occ[h.Tx, h.Ty] = null;
                h.CarriedBy = u;
                h.Path = null;
                h.PathPending = false;
                h.Moving = false;
                u.Carrying = h;
                u.Target = null;
                w.Hear(Sfx.Lift, u.X, u.Y);
            }
            return;
        }
        // Nothing to do: circle over home.
        double a = w.Clock * 0.6 + u.Id;
        FlyTo(u, u.HomeX + 0.5 + Math.Cos(a) * 1.5, u.HomeY + 0.5 + Math.Sin(a) * 1.5, dt * 0.4);
    }

    private static bool FlyTo(Unit u, double x, double y, double dt)
    {
        double d = Field.Dist(u.X, u.Y, x, y);
        if (d < 0.15) return true;
        u.Facing = Turn(u.Facing, Math.Atan2(y - u.Y, x - u.X), 5 * dt);
        u.Aim = u.Facing;
        double s = Math.Min(d, u.Stats.Speed * dt);
        u.X += (x - u.X) / d * s;
        u.Y += (y - u.Y) / d * s;
        u.Tx = Math.Clamp((int)u.X, 0, N - 1);
        u.Ty = Math.Clamp((int)u.Y, 0, N - 1);
        u.Moving = true;
        return d - s < 0.15;
    }

    /// <summary>Skylifters with nothing to do pick up harvesters with a long way to go: home with a load, or out to a
    /// distant field.</summary>
    private static void Ferry(World w)
    {
        foreach (var lifter in w.Units)
        {
            if (lifter.Dead || !lifter.InAir || lifter.Carrying is not null || lifter.Target is not null) continue;
            Unit? best = null;
            (int, int) drop = default;
            double bestD = double.MaxValue;
            foreach (var h in w.Units)
            {
                if (h.Dead || h.Owner != lifter.Owner || h.Type != Kind.Harvester || h.CarriedBy is not null) continue;
                if (w.Units.Any(o => o.InAir && o.Target == h)) continue;
                (int X, int Y)? to = null;
                if (h.Order == Order.Return && RefineryFor(w, h) is { } r && Field.Dist(h.Tx, h.Ty, r.Door.X, r.Door.Y) > 10) to = r.Door;
                else if (h.Order == Order.Harvest && h.Cargo < 50 && h.Path is not null && h.Gx >= 0 && Field.Dist(h.Tx, h.Ty, h.Gx, h.Gy) > 10 && w.Ore[h.Gx, h.Gy] > 0) to = (h.Gx, h.Gy);
                if (to is not { } t) continue;
                double d = Field.Dist(lifter.X, lifter.Y, h.X, h.Y);
                if (d < bestD) { bestD = d; best = h; drop = t; }
            }
            if (best is null) continue;
            lifter.Target = best;
            (lifter.DropX, lifter.DropY) = drop;
        }
    }

    // ───────────────────────────── angles ─────────────────────────────

    /// <summary>The difference between two angles, from -π to π.</summary>
    public static double Diff(double a, double b)
    {
        double d = (b - a) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        if (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }

    /// <summary>Turns from one angle toward another by at most a step.</summary>
    public static double Turn(double from, double to, double step)
    {
        double d = Diff(from, to);
        return Math.Abs(d) <= step ? to : from + Math.Sign(d) * step;
    }
}
