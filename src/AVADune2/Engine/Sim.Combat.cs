namespace AVADune2.Engine;

public static partial class Sim
{
    // ───────────────────────────── targets ─────────────────────────────

    /// <summary>Whether a unit may (still) shoot at something: the enemy's, alive, and on the ground unless the
    /// weapon reaches the sky.</summary>
    public static bool Valid(World w, Unit u, Thing t)
    {
        if (t.Dead || !w.Enemies(u.Owner, t.Owner)) return false;
        if (t is Unit tu)
        {
            if (tu.CarriedBy is not null) return false;
            if (tu.InAir && !Rules.HitsAir(u.Stats.Weapon)) return false;
            if (u.Stats.Weapon == Weapon.Deviator && (Rules.IsInfantry(tu.Type) || tu.InAir)) return false;
        }
        else if (u.Stats.Weapon == Weapon.Deviator) return false;
        return true;
    }

    /// <summary>How far a unit is from a thing: to its centre, or to the nearest edge of a building.</summary>
    public static double Reach(Unit u, Thing t) => t is Building b ? b.Reach(u.X, u.Y) + 0.5 : Field.Dist(u.X, u.Y, t.CX, t.CY);

    public static bool InRange(Unit u, Thing t)
    {
        double d = Reach(u, t);
        return d <= u.Stats.Range + 0.15 && d >= u.Stats.MinRange;
    }

    /// <summary>The best thing for a unit to shoot within a radius: armed units first, then other units, then buildings
    /// (walls last).</summary>
    public static Thing? FindTarget(World w, Unit u, double radius) => FindTarget(w, u.Owner, u.X, u.Y, radius, u.Stats.Weapon, u.Stats.MinRange);

    public static Thing? FindTarget(World w, int owner, double x, double y, double radius, Weapon weapon, double minRange = 0)
    {
        Thing? best = null;
        double bestScore = double.MaxValue;
        int r = (int)Math.Ceiling(radius) + 1, cx = (int)x, cy = (int)y;
        for (int ty = cy - r; ty <= cy + r; ty++)
        for (int tx = cx - r; tx <= cx + r; tx++)
        {
            if (!Field.In(tx, ty)) continue;
            if (w.Occ[tx, ty] is { Dead: false } o && o.Tx == tx && o.Ty == ty && w.Enemies(owner, o.Owner))
            {
                if (weapon == Weapon.Deviator && Rules.IsInfantry(o.Type)) continue;
                double d = Field.Dist(x, y, o.X, o.Y);
                if (d <= radius && d >= minRange)
                {
                    double score = d - (Rules.Armed(o.Type) ? 2 : 0);
                    if (score < bestScore) { bestScore = score; best = o; }
                }
            }
            if (weapon != Weapon.Deviator && w.Bld[tx, ty] is { Dead: false } b && w.Enemies(owner, b.Owner) && b.X == tx && b.Y == ty)
            {
                double d = b.Reach(x, y) + 0.5;
                if (d <= radius && d >= minRange)
                {
                    double score = d + (b.Type == Kind.Wall ? 8 : b.Stats.Turret ? 0.5 : 3);
                    if (score < bestScore) { bestScore = score; best = b; }
                }
            }
        }
        if (Rules.HitsAir(weapon))
            foreach (var a in w.Units)
            {
                if (a.Dead || !a.InAir || !w.Enemies(owner, a.Owner)) continue;
                double d = Field.Dist(x, y, a.X, a.Y);
                if (d <= radius && d + 1 < bestScore) { bestScore = d + 1; best = a; }
            }
        return best;
    }

    // ───────────────────────────── shooting ─────────────────────────────

    private static void AimAndFire(World w, Unit u, Thing t, double dt)
    {
        var st = u.Stats;
        double want = Math.Atan2(t.CY - u.Y, t.CX - u.X);
        if (st.Mover == Mover.Foot) u.Facing = u.Aim = want;
        else if (st.Turret) u.Aim = Turn(u.Aim, want, 4.5 * dt);
        else if (u.Nx < 0) u.Aim = u.Facing = Turn(u.Facing, want, 3.5 * dt);
        if (Math.Abs(Diff(u.Aim, want)) > 0.2 || u.Reload > 0) return;
        Fire(w, u.Owner, u.X, u.Y, st, t, u);
        if (st.Shots > 1 && u.Salvo == 0) { u.Salvo = 1; u.Reload = 0.28; }
        else { u.Salvo = 0; u.Reload = st.Reload * (0.92 + w.Dice.Next() * 0.16); }
    }

    /// <summary>A shot from (x, y) at a target, by weapon.</summary>
    public static void Fire(World w, int owner, double x, double y, Stats st, Thing t, Unit? shooter = null)
    {
        double tx = t.CX, ty = t.CY;
        if (t is Building b)
        {
            tx = Math.Clamp(x, b.X + 0.3, b.X + b.W - 0.3);
            ty = Math.Clamp(y, b.Y + 0.3, b.Y + b.H - 0.3);
        }
        double a = Math.Atan2(ty - y, tx - x);
        double mx = x + Math.Cos(a) * 0.45, my = y + Math.Sin(a) * 0.45;
        bool air = t is Unit { InAir: true };
        switch (st.Weapon)
        {
            case Weapon.Gun:
                w.Fx(Fx.Tracer, mx, my, 0.09, tx + w.Dice.Range(-0.2, 0.2), ty + w.Dice.Range(-0.2, 0.2));
                w.Fx(Fx.Flash, mx, my, 0.07);
                w.Hear(Sfx.Gun, x, y);
                Damage(w, t, st.Damage * w.Dice.Range(0.85, 1.15), Weapon.Gun, owner);
                break;
            case Weapon.Cannon:
                w.Fx(Fx.Flash, mx, my, 0.1);
                w.Hear(Sfx.Cannon, x, y);
                w.Shots.Add(new Shot { X = mx, Y = my, Sx = mx, Sy = my, Tx = tx, Ty = ty, Speed = 10, Splash = 0.45, Damage = st.Damage, Weapon = Weapon.Cannon, Owner = owner });
                break;
            case Weapon.Rocket:
            {
                double d = Field.Dist(x, y, tx, ty), miss = air ? 0 : 0.5 * d / Math.Max(1, st.Range);
                w.Hear(Sfx.Rocket, x, y);
                w.Shots.Add(new Shot { X = mx, Y = my, Sx = mx, Sy = my, Tx = tx + w.Dice.Range(-miss, miss), Ty = ty + w.Dice.Range(-miss, miss), Speed = 7, Splash = 0.6, Damage = st.Damage, Weapon = Weapon.Rocket, Owner = owner, Air = air });
                break;
            }
            case Weapon.Deviator:
                w.Hear(Sfx.Deviate, x, y);
                w.Shots.Add(new Shot { X = mx, Y = my, Sx = mx, Sy = my, Tx = tx, Ty = ty, Speed = 5, Splash = 1.3, Damage = 0, Weapon = Weapon.Deviator, Owner = owner });
                break;
            case Weapon.Sonic:
                Sonic(w, owner, x, y, a, st, shooter);
                break;
        }
    }

    /// <summary>The resonator's wave: everything along its line is shaken (its own side's too, if less).</summary>
    private static void Sonic(World w, int owner, double x, double y, double a, Stats st, Unit? shooter)
    {
        double len = st.Range + 1;
        var hit = new HashSet<Thing>();
        for (double s = 0.5; s <= len; s += 0.3)
        {
            double px = x + Math.Cos(a) * s, py = y + Math.Sin(a) * s;
            int tx = (int)px, ty = (int)py;
            if (!Field.In(tx, ty)) break;
            for (int j = ty - 1; j <= ty + 1; j++)
            for (int i = tx - 1; i <= tx + 1; i++)
                if (w.UnitAt(i, j) is { Dead: false } o && o != shooter && Field.Dist(o.X, o.Y, px, py) < 0.6) hit.Add(o);
            if (w.Bld[tx, ty] is { Dead: false } b) hit.Add(b);
            foreach (var c in w.Creatures)
                if (c.Dormant <= 0 && c.Surfaced && Field.Dist(c.X, c.Y, px, py) < 0.8) HurtCreature(w, c, st.Damage * 0.1);
        }
        foreach (var t in hit) Damage(w, t, w.Enemies(owner, t.Owner) ? st.Damage : st.Damage * 0.4, Weapon.Sonic, owner);
        w.Fx(Fx.Sonic, x + Math.Cos(a) * 0.5, y + Math.Sin(a) * 0.5, 0.55, x + Math.Cos(a) * len, y + Math.Sin(a) * len);
        w.Hear(Sfx.Sonic, x, y);
    }

    private static void UpdateShots(World w, double dt)
    {
        for (int i = w.Shots.Count - 1; i >= 0; i--)
        {
            var s = w.Shots[i];
            s.Life += dt;
            double d = Field.Dist(s.X, s.Y, s.Tx, s.Ty), step = s.Speed * dt;
            if (step >= d || s.Life > 12)
            {
                s.X = s.Tx;
                s.Y = s.Ty;
                w.Shots.RemoveAt(i);
                Explode(w, s);
                continue;
            }
            s.X += (s.Tx - s.X) / d * step;
            s.Y += (s.Ty - s.Y) / d * step;
            if (s.Weapon is Weapon.Rocket or Weapon.Missile && w.Ticks % 3 == 0) w.Fx(Fx.Smoke, s.X, s.Y, s.Weapon == Weapon.Missile ? 1.4 : 0.6);
        }
    }

    private static void Explode(World w, Shot s)
    {
        if (s.Weapon == Weapon.Deviator)
        {
            w.Fx(Fx.Gas, s.X, s.Y, 1.2);
            foreach (var u in Near(w, s.X, s.Y, s.Splash))
            {
                if (!w.Enemies(s.Owner, u.Owner) || Rules.IsInfantry(u.Type) || u.InAir || u.CarriedBy is not null) continue;
                if (u.TrueOwner < 0) u.TrueOwner = u.Owner;
                u.Owner = s.Owner;
                u.Turned = 25;
                if (u.Order == Order.Docked && u.Dock is { } d && d.Docked == u) d.Docked = null;
                Halt(u);
                if (u.Type == Kind.Harvester) u.Order = Order.Idle;
                w.Fx(Fx.Turned, u.X, u.Y, 0.9);
            }
            return;
        }
        bool big = s.Weapon == Weapon.Missile;
        w.Fx(big ? Fx.BigBoom : Fx.Boom, s.X, s.Y, big ? 1.8 : 0.55, colour: (uint)(s.Splash * 100));
        w.Hear(big ? Sfx.BigBoom : Sfx.Boom, s.X, s.Y);
        if (s.Air)
        {
            foreach (var a in w.Units)
                if (!a.Dead && a.InAir && w.Enemies(s.Owner, a.Owner) && Field.Dist(a.X, a.Y, s.X, s.Y) < 0.9) { Damage(w, a, s.Damage, s.Weapon, s.Owner); break; }
            return;
        }
        foreach (var u in Near(w, s.X, s.Y, s.Splash + 0.35))
        {
            if (u.InAir || !(big || w.Enemies(s.Owner, u.Owner))) continue;
            double d = Field.Dist(u.X, u.Y, s.X, s.Y), fall = d < 0.5 ? 1 : Math.Max(0.25, 1 - (d - 0.5) / (s.Splash + 0.35));
            Damage(w, u, s.Damage * fall, s.Weapon, s.Owner);
        }
        var hitB = new HashSet<Building>();
        int r = (int)Math.Ceiling(s.Splash);
        for (int y = (int)s.Y - r; y <= (int)s.Y + r; y++)
        for (int x = (int)s.X - r; x <= (int)s.X + r; x++)
        {
            if (!Field.In(x, y)) continue;
            if (w.Bld[x, y] is { Dead: false } b && (big || w.Enemies(s.Owner, b.Owner)) && b.Reach(s.X, s.Y) <= (big ? s.Splash : 0.25)) hitB.Add(b);
            if (Field.Dist(x + 0.5, y + 0.5, s.X, s.Y) <= Math.Max(0.6, s.Splash))
            {
                if (w.Bloom[x, y]) Burst(w, x, y);
                Scorch(w, x, y, big ? 0.6f : 0.12f);
            }
        }
        foreach (var b in hitB) Damage(w, b, s.Damage, s.Weapon, s.Owner);
        foreach (var c in w.Creatures)
            if (c.Dormant <= 0 && c.Surfaced && Field.Dist(c.X, c.Y, s.X, s.Y) < s.Splash + 0.8) HurtCreature(w, c, s.Damage);
    }

    private static void Scorch(World w, int x, int y, float amount)
    {
        float before = w.Scorch[x, y];
        w.Scorch[x, y] = Math.Min(1, before + amount);
        if ((int)(before * 4) != (int)(w.Scorch[x, y] * 4)) w.GroundVersion++;
    }

    /// <summary>The units on the ground within a radius of a point (a copy, safe to change the world under).</summary>
    private static List<Unit> Near(World w, double x, double y, double radius)
    {
        var list = new List<Unit>();
        int r = (int)Math.Ceiling(radius) + 1;
        for (int ty = (int)y - r; ty <= (int)y + r; ty++)
        for (int tx = (int)x - r; tx <= (int)x + r; tx++)
            if (Field.In(tx, ty) && w.Occ[tx, ty] is { Dead: false } u && u.Tx == tx && u.Ty == ty && Field.Dist(u.X, u.Y, x, y) <= radius) list.Add(u);
        return list;
    }

    // ───────────────────────────── damage ─────────────────────────────

    /// <summary>Harm done to a thing, by weapon against its armour; at no health it's destroyed. The player hears
    /// when the base or a harvester comes under attack.</summary>
    public static void Damage(World w, Thing t, double amount, Weapon weapon, int attacker)
    {
        if (t.Dead || amount <= 0) return;
        double hurt = amount * Rules.Versus(weapon, t.Stats.Armour);
        if (hurt <= 0) return;
        t.Hp -= hurt;
        t.HitAgo = 0;
        if (attacker >= 0) t.HitBy = attacker;
        if (t.Owner == World.Player && attacker >= 0 && w.Enemies(attacker, World.Player) && (t is Building || t.Kind == Kind.Harvester))
        {
            var h = w.PlayerHouse;
            if (w.Clock - h.AttackAlarm > 20)
            {
                h.AttackAlarm = w.Clock;
                w.AlarmX = t.CX;
                w.AlarmY = t.CY;
                w.Hear(Sfx.SayAttacked);
                w.News.Add(t is Building ? "OUR BASE IS UNDER FIRE" : "A HARVESTER IS UNDER FIRE");
            }
        }
        if (t.Hp <= 0) Kill(w, t, attacker);
    }

    /// <summary>A thing destroyed: off the map, with an explosion, and counted. A building may leave a few survivors.</summary>
    public static void Kill(World w, Thing t, int attacker, bool swallowed = false)
    {
        if (t.Dead) return;
        t.Dead = true;
        t.Hp = 0;
        int owner = t is Unit { TrueOwner: >= 0 } tu ? tu.TrueOwner : t.Owner;
        bool counts = attacker >= 0 && attacker < w.Houses.Count && w.Enemies(attacker, owner);
        if (t is Unit u)
        {
            if (w.Occ[u.Tx, u.Ty] == u) w.Occ[u.Tx, u.Ty] = null;
            if (u.Nx >= 0 && w.Occ[u.Nx, u.Ny] == u) w.Occ[u.Nx, u.Ny] = null;
            if (u.Dock is { } d && d.Docked == u) d.Docked = null;
            if (u.Carrying is { } load) Kill(w, load, attacker);
            w.Houses[owner].UnitsLost++;
            if (counts) w.Houses[attacker].UnitsKilled++;
            if (swallowed) return;
            bool small = Rules.IsInfantry(u.Type);
            w.Fx(small ? Fx.Spark : Fx.Boom, u.X, u.Y, small ? 0.5 : 0.9, colour: small ? 0u : 90u);
            w.Hear(small ? Sfx.Crush : Sfx.Boom, u.X, u.Y);
            if (!small && u.OnGround && !u.InAir) Scorch(w, u.Tx, u.Ty, 0.5f);
            if (u.InAir) w.Fx(Fx.BigBoom, u.X, u.Y, 1.2);
            return;
        }
        var b = (Building)t;
        for (int y = b.Y; y < b.Y + b.H; y++)
        for (int x = b.X; x < b.X + b.W; x++)
        {
            if (w.Bld[x, y] == b) w.Bld[x, y] = null;
            Scorch(w, x, y, 0.7f);
            w.Fx(Fx.BigBoom, x + 0.5 + w.Dice.Range(-0.2, 0.2), y + 0.5 + w.Dice.Range(-0.2, 0.2), 1.1 + w.Dice.Next() * 0.6);
        }
        if (b.Docked is { } hv && !hv.Dead) { hv.Order = Order.Return; hv.Dock = null; }
        if (b.Type != Kind.Wall)
        {
            w.Houses[owner].BuildingsLost++;
            if (counts) w.Houses[attacker].BuildingsKilled++;
        }
        w.Hear(Sfx.BigBoom, b.CX, b.CY);
        w.GroundVersion++;
        // Survivors stagger out of the ruins.
        if (b.Type is not (Kind.Wall or Kind.Turret or Kind.RocketTurret) && w.Dice.Next() < 0.5 && Alive(w, owner))
            if (Spawn(w, owner, Kind.Infantry, b.Door.X, b.Door.Y - 1) is { } s) s.Hp = s.MaxHp * 0.5;
        UpdatePower(w);
    }

    /// <summary>A juggernaut blows itself up, taking everything close with it.</summary>
    public static void SelfDestruct(World w, Unit u)
    {
        if (u.Dead || u.Type != Kind.Juggernaut) return;
        double x = u.X, y = u.Y;
        int owner = u.Owner;
        Kill(w, u, -1);
        w.Fx(Fx.BigBoom, x, y, 2);
        w.Hear(Sfx.BigBoom, x, y);
        foreach (var o in Near(w, x, y, 2.6))
            Damage(w, o, 520 * (1 - Field.Dist(o.X, o.Y, x, y) / 3.2), Weapon.Missile, owner);
        var hit = new HashSet<Building>();
        for (int j = (int)y - 3; j <= (int)y + 3; j++)
        for (int i = (int)x - 3; i <= (int)x + 3; i++)
            if (w.BuildingAt(i, j) is { Dead: false } b && b.Reach(x, y) < 2.5) hit.Add(b);
        foreach (var b in hit) Damage(w, b, 350, Weapon.Missile, owner);
    }

    // ───────────────────────────── turrets ─────────────────────────────

    private static void UpdateTurrets(World w, double dt)
    {
        foreach (var b in w.Buildings)
        {
            if (b.Dead || !b.Stats.Turret) continue;
            var h = w.Houses[b.Owner];
            // Without full power a turret is slow; without half, it's dead.
            if (h.PowerRatio < 0.5) continue;
            b.Reload -= dt * h.PowerRatio;
            var st = b.Stats;
            if (b.Target is { } t0 && (t0.Dead || DistTo(b, t0) > st.Range + 0.2 || t0 is Unit { CarriedBy: not null })) b.Target = null;
            if (b.Target is null && (w.Ticks + b.Id) % 15 == 0) b.Target = FindTarget(w, b.Owner, b.CX, b.CY, st.Range, st.Weapon, st.MinRange);
            if (b.Target is not { } t) continue;
            double want = Math.Atan2(t.CY - b.CY, t.CX - b.CX);
            b.Aim = Turn(b.Aim, want, 3.5 * dt);
            if (Math.Abs(Diff(b.Aim, want)) > 0.15 || b.Reload > 0) continue;
            Fire(w, b.Owner, b.CX, b.CY, st, t);
            b.Reload = st.Reload;
        }
    }

    private static double DistTo(Building b, Thing t) => t is Building o ? o.Reach(b.CX, b.CY) + 0.5 : Field.Dist(b.CX, b.CY, t.CX, t.CY);

    // ───────────────────────────── sandmaws ─────────────────────────────

    public const double MawHunt = 1.6, MawWander = 0.8, MawSense = 11;

    /// <summary>A sandmaw out of the deep somewhere on the open sand, far from any base.</summary>
    public static Creature? Emerge(World w, Creature? c = null)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            int x = w.Dice.Next(Field.N), y = w.Dice.Next(Field.N);
            if (!w.IsSandy(x, y) || w.Buildings.Any(b => !b.Dead && b.Reach(x, y) < 9)) continue;
            int sand = 0;
            for (int j = y - 2; j <= y + 2; j++)
            for (int i = x - 2; i <= x + 2; i++)
                if (w.IsSandy(i, j)) sand++;
            if (sand < 20) continue;
            c ??= new Creature();
            c.X = x + 0.5;
            c.Y = y + 0.5;
            c.Dormant = 0;
            c.Eaten = 0;
            c.Hp = Creature.MaxHp;
            c.Prey = null;
            c.Wx = c.Wy = -1;
            if (!w.Creatures.Contains(c)) w.Creatures.Add(c);
            return c;
        }
        return null;
    }

    private static void UpdateCreatures(World w, double dt)
    {
        foreach (var c in w.Creatures)
        {
            if (c.Dormant > 0)
            {
                c.Dormant -= dt;
                if (c.Dormant <= 0) Emerge(w, c);
                continue;
            }
            c.Mouth = Math.Max(0, c.Mouth - dt);
            c.Think -= dt;
            if (c.Prey is { } p0 && (p0.Dead || !p0.OnGround || !w.IsSandy(p0.Tx, p0.Ty) || Field.Dist(p0.X, p0.Y, c.X, c.Y) > 18)) c.Prey = null;
            if (c.Think <= 0)
            {
                c.Think = 1;
                if (c.Mouth <= 0) c.Prey = Prey(w, c) ?? c.Prey;
            }
            double tx, ty, speed;
            if (c.Prey is { } p) { tx = p.X; ty = p.Y; speed = MawHunt; }
            else
            {
                if (c.Wx < 0 || Field.Dist(c.X, c.Y, c.Wx, c.Wy) < 0.6)
                {
                    for (int k = 0; k < 20; k++)
                    {
                        int x = (int)c.X + w.Dice.Next(21) - 10, y = (int)c.Y + w.Dice.Next(21) - 10;
                        if (w.IsSandy(x, y)) { c.Wx = x + 0.5; c.Wy = y + 0.5; break; }
                    }
                    if (c.Wx < 0) { c.Wx = c.X; c.Wy = c.Y; }
                }
                tx = c.Wx;
                ty = c.Wy;
                speed = MawWander;
            }
            if (c.Mouth > 0.4) speed = 0;
            double d = Field.Dist(c.X, c.Y, tx, ty);
            if (d > 0.05 && speed > 0)
            {
                c.Dir = Turn(c.Dir, Math.Atan2(ty - c.Y, tx - c.X), 2.5 * dt);
                double s = Math.Min(d, speed * dt), nx = c.X + Math.Cos(c.Dir) * s, ny = c.Y + Math.Sin(c.Dir) * s;
                if (w.IsSandy((int)nx, (int)ny)) { c.X = nx; c.Y = ny; }
                else if (w.IsSandy((int)nx, (int)c.Y)) c.X = nx;
                else if (w.IsSandy((int)c.X, (int)ny)) c.Y = ny;
                else { c.Prey = null; c.Wx = -1; c.Dir += Math.PI / 2; }
            }
            c.Surfaced = c.Mouth > 0 || c.Prey is { } q && Field.Dist(q.X, q.Y, c.X, c.Y) < 4.5;
            if (c.Surfaced && Visible(w, c.X, c.Y))
            {
                if (w.Clock - c.Seen > 50)
                {
                    w.Hear(Sfx.SayCreature);
                    w.News.Add("SANDMAW SIGHTED!");
                }
                if (w.Clock - c.Seen > 4) w.Hear(Sfx.Rumble, c.X, c.Y);
                if (w.Clock - c.Seen > 4) c.Seen = w.Clock;
            }
            if (c.Prey is { } prey && Field.Dist(prey.X, prey.Y, c.X, c.Y) < 0.55 && w.IsSandy(prey.Tx, prey.Ty))
            {
                w.Fx(Fx.Swallow, prey.X, prey.Y, 2.2);
                w.Hear(Sfx.Swallow, prey.X, prey.Y);
                if (prey.Owner == World.Player) w.News.Add($"A SANDMAW HAS SWALLOWED OUR {prey.Stats.Name}");
                Kill(w, prey, -1, swallowed: true);
                c.X = prey.X;
                c.Y = prey.Y;
                c.Mouth = 2.2;
                c.Prey = null;
                c.Think = 2.5;
                if (++c.Eaten >= 3) c.Dormant = 80 + w.Dice.Next() * 60;
            }
        }
    }

    /// <summary>What a sandmaw goes after: the nearest thing moving on the open sand, harvesters above all (their
    /// thumping draws it). Nomads know how to walk the sand unheard.</summary>
    private static Unit? Prey(World w, Creature c)
    {
        Unit? best = null;
        double bestScore = MawSense;
        foreach (var u in w.Units)
        {
            if (u.Dead || !u.OnGround || u.Type is Kind.Nomads || !w.IsSandy(u.Tx, u.Ty)) continue;
            double d = Field.Dist(u.X, u.Y, c.X, c.Y);
            if (d > MawSense) continue;
            double score = d * (u.Type == Kind.Harvester ? 0.6 : 1) * (u.Moving || u.Type == Kind.Harvester ? 0.8 : 1.4);
            if (score < bestScore) { bestScore = score; best = u; }
        }
        return best;
    }

    /// <summary>A sandmaw hurt: badly enough, it dives deep and is gone for a while.</summary>
    public static void HurtCreature(World w, Creature c, double amount)
    {
        c.Hp -= amount;
        if (c.Hp > Creature.MaxHp * 0.35) return;
        c.Dormant = 90 + w.Dice.Next() * 60;
        c.Prey = null;
        c.Surfaced = false;
        c.Mouth = 0;
        w.Fx(Fx.Swallow, c.X, c.Y, 1.6);
    }

    /// <summary>A unit with nothing else to shoot takes aim at a sandmaw that has come up near it.</summary>
    private static bool ShootCreature(World w, Unit u)
    {
        var st = u.Stats;
        if (u.Reload > 0 || st.Weapon is Weapon.Deviator or Weapon.None) return false;
        foreach (var c in w.Creatures)
        {
            if (c.Dormant > 0 || !c.Surfaced || Field.Dist(u.X, u.Y, c.X, c.Y) > st.Range) continue;
            u.Aim = Math.Atan2(c.Y - u.Y, c.X - u.X);
            if (st.Mover == Mover.Foot || !st.Turret) u.Facing = u.Aim;
            w.Fx(Fx.Tracer, u.X, u.Y, 0.09, c.X, c.Y);
            w.Fx(Fx.Boom, c.X + w.Dice.Range(-0.3, 0.3), c.Y + w.Dice.Range(-0.3, 0.3), 0.4, colour: 30);
            w.Hear(st.Weapon == Weapon.Gun ? Sfx.Gun : Sfx.Cannon, u.X, u.Y);
            HurtCreature(w, c, st.Damage * st.Shots);
            u.Reload = st.Reload;
            return true;
        }
        return false;
    }
}
