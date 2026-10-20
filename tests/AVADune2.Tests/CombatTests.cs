using System.Diagnostics;
using AVADune2.Engine;
using static AVADune2.Tests.TestWorld;

namespace AVADune2.Tests;

/// <summary>The units: their numbers, fights, the special weapons, turrets, the palaces, finding the way, the sandmaws,
/// the skylifters, and how fast a crowded battle runs.</summary>
public class CombatTests(ITestOutputHelper output)
{
    // ───────────────────────────── the numbers ─────────────────────────────

    [Fact]
    public void Every_unit_has_sensible_numbers()
    {
        foreach (var k in Enum.GetValues<Kind>().Where(Rules.IsUnit))
        {
            var st = Rules.Of(k);
            Assert.True(st.Hp > 0, $"{k} health");
            Assert.True(st.Speed > 0, $"{k} speed");
            Assert.True(st.Sight >= 2.5, $"{k} sight");
            Assert.NotEqual(Mover.None, st.Mover);
            if (st.Tech < Rules.NoTech) Assert.True(st.Cost > 0 && st.Time > 0, $"{k} cost");
            if (Rules.Armed(k))
            {
                Assert.True(st.Range > st.MinRange, $"{k} range");
                Assert.True(st.Reload > 0, $"{k} reload");
                Assert.True(st.Damage > 0 || st.Weapon == Weapon.Deviator, $"{k} damage");
            }
        }
        // The heavier the tank, the slower and the harder it hits.
        Assert.True(Rules.Of(Kind.SiegeTank).Damage > Rules.Of(Kind.Tank).Damage && Rules.Of(Kind.SiegeTank).Speed < Rules.Of(Kind.Tank).Speed);
        Assert.True(Rules.Of(Kind.Juggernaut).Hp > Rules.Of(Kind.SiegeTank).Hp);
        Assert.True(Rules.Of(Kind.RocketTank).Range > Rules.Of(Kind.Tank).Range);
        Assert.True(Rules.Of(Kind.Trike).Speed > Rules.Of(Kind.Quad).Speed && Rules.Of(Kind.Quad).Hp > Rules.Of(Kind.Trike).Hp);
        // Bullets for infantry, rockets for armour.
        Assert.True(Rules.Versus(Weapon.Gun, Armour.Infantry) > Rules.Versus(Weapon.Gun, Armour.Heavy));
        Assert.True(Rules.Versus(Weapon.Rocket, Armour.Heavy) > Rules.Versus(Weapon.Rocket, Armour.Infantry));
        Assert.True(Rules.HitsAir(Weapon.Rocket) && !Rules.HitsAir(Weapon.Cannon) && !Rules.HitsAir(Weapon.Gun));
        // The Houses' specials are each their own.
        Assert.Equal(Faction.Aldaine, Rules.Of(Kind.Resonator).Only);
        Assert.Equal(Faction.Vrakos, Rules.Of(Kind.Juggernaut).Only);
        Assert.Equal(Faction.Sythe, Rules.Of(Kind.Beguiler).Only);
        Assert.Equal(Weapon.Sonic, Rules.Of(Kind.Resonator).Weapon);
        Assert.Equal(Weapon.Deviator, Rules.Of(Kind.Beguiler).Weapon);
    }

    /// <summary>Two groups on open rock, ordered at each other: who's left.</summary>
    private static (int Mine, int Theirs) Fight(Kind mine, int n, Kind theirs, int m, int distance = 8)
    {
        var w = Blank();
        var a = Enumerable.Range(0, n).Select(i => Sim.Spawn(w, World.Player, mine, 20, 20 + i * 2)!).ToList();
        var b = Enumerable.Range(0, m).Select(i => Sim.Spawn(w, Enemy, theirs, 20 + distance, 20 + i * 2)!).ToList();
        foreach (var u in a) Sim.Command(w, u, Order.AttackMove, 20 + distance, 22);
        foreach (var u in b) Sim.Command(w, u, Order.AttackMove, 20, 22);
        RunUntil(w, () => a.All(u => u.Dead) || b.All(u => u.Dead), 120);
        return (a.Count(u => !u.Dead), b.Count(u => !u.Dead));
    }

    [Fact]
    public void Heavier_units_win_their_fights()
    {
        Assert.Equal(0, Fight(Kind.Tank, 1, Kind.Trike, 1).Theirs);
        Assert.Equal(0, Fight(Kind.Trike, 2, Kind.Tank, 1).Mine);
        Assert.Equal(0, Fight(Kind.SiegeTank, 2, Kind.Tank, 2).Theirs);
        Assert.Equal(0, Fight(Kind.Quad, 3, Kind.Infantry, 3).Theirs);
        Assert.Equal(0, Fight(Kind.Juggernaut, 1, Kind.Tank, 2).Theirs);
        // Even fights are close.
        var (x, y) = Fight(Kind.Tank, 4, Kind.Tank, 4);
        Assert.True(x <= 3 && y <= 3);
    }

    [Fact]
    public void Rockets_reach_further_than_cannon_but_not_up_close()
    {
        var w = Blank();
        var rocket = Sim.Spawn(w, World.Player, Kind.RocketTank, 20, 20)!;
        var tank = Sim.Spawn(w, Enemy, Kind.Tank, 26, 20)!;
        Sim.Command(w, tank, Order.Guard, 26, 20);
        Run(w, 4);
        Assert.True(tank.Hp < tank.MaxHp);             // in its range, out of the tank's
        Assert.Equal(rocket.MaxHp, rocket.Hp);
        Assert.False(Sim.InRange(rocket, Sim.Spawn(w, Enemy, Kind.Trike, 21, 20)!)); // too close to fire
    }

    [Fact]
    public void Tracked_vehicles_drive_over_enemy_infantry()
    {
        var w = Blank();
        var tank = Sim.Spawn(w, World.Player, Kind.Tank, 10, 10)!;
        var soldier = Sim.Spawn(w, Enemy, Kind.Infantry, 13, 10)!;
        soldier.Order = Order.Idle;
        Sim.Command(w, tank, Order.Move, 16, 10);
        Assert.True(RunUntil(w, () => soldier.Dead, 10));
        Assert.Equal(1, w.PlayerHouse.UnitsKilled);
        // Wheels go round them.
        var trike = Sim.Spawn(w, World.Player, Kind.Trike, 10, 20)!;
        var other = Sim.Spawn(w, Enemy, Kind.Infantry, 13, 20)!;
        other.Hp = 1e6;
        Sim.Command(w, trike, Order.Move, 16, 20);
        Run(w, 4);
        Assert.False(other.Dead);
    }

    [Fact]
    public void The_resonator_shakes_everything_along_its_wave_its_own_side_less()
    {
        var w = Blank();
        var res = Sim.Spawn(w, World.Player, Kind.Resonator, 10, 20)!;
        var friend = Sim.Spawn(w, World.Player, Kind.Tank, 12, 20)!;
        var a = Sim.Spawn(w, Enemy, Kind.Tank, 14, 20)!;
        var b = Sim.Spawn(w, Enemy, Kind.Tank, 15, 20)!;
        var aside = Sim.Spawn(w, Enemy, Kind.Tank, 14, 24)!;
        foreach (var u in new[] { friend, a, b, aside }) u.Hp = 1e5;
        Sim.Fire(w, World.Player, res.X, res.Y, res.Stats, a, res);
        Assert.Equal(1e5 - 50 * 0.9, a.Hp, 3);
        Assert.Equal(1e5 - 50 * 0.9, b.Hp, 3);
        Assert.Equal(1e5 - 50 * 0.4 * 0.9, friend.Hp, 3);
        Assert.Equal(1e5, aside.Hp);
        Assert.Contains(w.Effects, e => e.Kind == Fx.Sonic);
    }

    [Fact]
    public void The_beguilers_gas_turns_enemy_vehicles_for_a_while()
    {
        var w = Blank(player: Faction.Sythe);
        var beg = Sim.Spawn(w, World.Player, Kind.Beguiler, 10, 20)!;
        var tank = Sim.Spawn(w, Enemy, Kind.Tank, 15, 20)!;
        var soldier = Sim.Spawn(w, Enemy, Kind.Infantry, 15, 21)!;
        Sim.Fire(w, World.Player, beg.X, beg.Y, beg.Stats, tank, beg);
        Run(w, 2);
        Assert.Equal(World.Player, tank.Owner);
        Assert.Equal(Enemy, tank.TrueOwner);
        Assert.Equal(Enemy, soldier.Owner);           // infantry can't be turned
        Sim.Kill(w, beg, Enemy);                      // (or it would turn it again)
        Run(w, 26);
        Assert.Equal(Enemy, tank.Owner);
        Assert.Equal(-1, tank.TrueOwner);
    }

    [Fact]
    public void A_juggernaut_can_blow_itself_up()
    {
        var w = Blank(player: Faction.Vrakos);
        var jug = Sim.Spawn(w, World.Player, Kind.Juggernaut, 20, 20)!;
        var near = Sim.Spawn(w, Enemy, Kind.Tank, 21, 20)!;
        var far = Sim.Spawn(w, Enemy, Kind.Tank, 26, 20)!;
        var b = Sim.Put(w, Enemy, Kind.Turbine, 18, 21);
        Sim.SelfDestruct(w, jug);
        Assert.True(jug.Dead);
        Assert.True(near.Dead);
        Assert.Equal(far.MaxHp, far.Hp);
        Assert.True(b.Hp < b.MaxHp * 0.6);
    }

    [Fact]
    public void Turrets_shoot_enemies_in_range_and_rocket_turrets_reach_skylifters()
    {
        var w = Blank();
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        Sim.Put(w, World.Player, Kind.Turbine, 13, 10);
        Sim.Put(w, World.Player, Kind.Turret, 16, 10);
        Sim.Put(w, World.Player, Kind.RocketTurret, 16, 14);
        var lifter = Sim.Spawn(w, Enemy, Kind.Skylifter, 20, 14)!;
        lifter.HomeX = 20;
        lifter.HomeY = 14;
        var near = Sim.Spawn(w, Enemy, Kind.Trike, 19, 10)!;
        var far = Sim.Spawn(w, Enemy, Kind.Trike, 40, 40)!;
        Run(w, 10);
        Assert.True(near.Dead);
        Assert.True(lifter.Dead || lifter.Hp < lifter.MaxHp);
        Assert.Equal(far.MaxHp, far.Hp);
        Assert.True(Rules.Of(Kind.RocketTurret).Range > Rules.Of(Kind.Turret).Range);
    }

    [Fact]
    public void Destroying_counts_and_buildings_leave_rubble_and_sometimes_survivors()
    {
        var w = Blank();
        Sim.Put(w, World.Player, Kind.Yard, 40, 40);
        var b = Sim.Put(w, Enemy, Kind.Barracks, 20, 20);
        Sim.Put(w, Enemy, Kind.Yard, 30, 30);
        Sim.Damage(w, b, 1e6, Weapon.Missile, World.Player);
        Assert.True(b.Dead);
        Assert.Null(w.BuildingAt(20, 20));
        Assert.True(w.Scorch[20, 20] > 0);
        Assert.Equal(1, w.PlayerHouse.BuildingsKilled);
        Assert.Equal(1, w.Houses[Enemy].BuildingsLost);
        Assert.Contains(w.Effects, e => e.Kind == Fx.BigBoom);
    }

    // ───────────────────────────── the palaces ─────────────────────────────

    [Fact]
    public void Each_palace_has_its_own_weapon_when_it_has_charged()
    {
        // Vrakos: the ruin missile, off the mark but devastating.
        var w = Blank(player: Faction.Vrakos);
        Sim.Put(w, World.Player, Kind.Turbine, 4, 4);
        Sim.Put(w, World.Player, Kind.Turbine, 7, 4);
        Sim.Put(w, World.Player, Kind.Palace, 4, 7);
        var target = Sim.Put(w, Enemy, Kind.Yard, 40, 40);
        Assert.False(Sim.FirePalace(w, World.Player, 40, 40));
        Run(w, Rules.PalaceCharge(Faction.Vrakos) + 1);
        Assert.True(w.PlayerHouse.PalaceReady);
        Assert.True(Sim.FirePalace(w, World.Player, 40, 40));
        Assert.Single(w.Shots, s => s.Weapon == Weapon.Missile);
        Assert.True(RunUntil(w, () => w.Shots.Count == 0, 20));
        Assert.True(target.Dead || target.Hp < target.MaxHp);
        Assert.Equal(0, w.PlayerHouse.PalaceCharge, 1);

        // Aldaine: a raid of nomads by the enemy's base.
        var a = Blank(player: Faction.Aldaine);
        Sim.Put(a, World.Player, Kind.Palace, 4, 7).Owner = World.Player;
        a.PlayerHouse.PalaceCharge = 1;
        Sim.Put(a, Enemy, Kind.Yard, 40, 40);
        Assert.True(Sim.FirePalace(a, World.Player, 40, 40));
        var nomads = a.UnitsOf(World.Player).Where(u => u.Type == Kind.Nomads).ToList();
        Assert.Equal(4, nomads.Count);
        Assert.All(nomads, u => Assert.True(Field.Dist(u.X, u.Y, 41, 41) < 10));

        // Sythe: a saboteur, who walks into an enemy building and destroys it.
        var s = Blank(player: Faction.Sythe);
        Sim.Put(s, World.Player, Kind.Palace, 4, 7);
        s.PlayerHouse.PalaceCharge = 1;
        var victim = Sim.Put(s, Enemy, Kind.HeavyFactory, 12, 12);
        Assert.True(Sim.FirePalace(s, World.Player, 0, 0));
        var sab = Assert.Single(s.UnitsOf(World.Player));
        Assert.Equal(Kind.Saboteur, sab.Type);
        Sim.Attack(s, sab, victim);
        Assert.True(RunUntil(s, () => victim.Dead, 30));
        Assert.True(sab.Dead);
    }

    // ───────────────────────────── finding the way ─────────────────────────────

    [Fact]
    public void The_way_goes_round_mountains_which_only_infantry_can_cross()
    {
        var w = Blank();
        for (int y = 0; y < 40; y++) w.Ground[20, y] = Ground.Mountain;
        var path = w.Paths.Find(w, null, 10, 10, 30, 10, Mover.Tracks);
        Assert.Equal(30 + 10 * Field.N, path[^1]);
        Assert.DoesNotContain(path, i => w.Ground[i % Field.N, i / Field.N] == Ground.Mountain);
        Assert.Contains(path, i => i / Field.N >= 40);       // round the end of the ridge
        var foot = w.Paths.Find(w, null, 10, 10, 30, 10, Mover.Foot);
        Assert.Contains(foot, i => w.Ground[i % Field.N, i / Field.N] == Ground.Mountain);
        Assert.True(foot.Count < path.Count);
        // Every step is to a neighbour.
        int prev = 10 + 10 * Field.N;
        foreach (int i in path)
        {
            Assert.True(Math.Abs(i % Field.N - prev % Field.N) <= 1 && Math.Abs(i / Field.N - prev / Field.N) <= 1);
            prev = i;
        }
    }

    [Fact]
    public void A_way_to_somewhere_unreachable_ends_as_near_as_it_can()
    {
        var w = Blank();
        var b = Sim.Put(w, Enemy, Kind.HeavyFactory, 30, 30);
        var path = w.Paths.Find(w, null, 10, 31, 31, 31, Mover.Tracks);
        int end = path[^1];
        Assert.Null(w.BuildingAt(end % Field.N, end / Field.N));
        Assert.True(b.Reach(end % Field.N + 0.5, end / Field.N + 0.5) < 1);
        // Shut in completely: nowhere better to go.
        var shut = Blank();
        for (int x = 0; x < Field.N; x++) shut.Ground[x, 20] = Ground.Mountain;
        var p2 = shut.Paths.Find(shut, null, 10, 10, 10, 40, Mover.Wheels);
        Assert.Equal(19, p2[^1] / Field.N);
    }

    [Fact]
    public void Units_follow_the_way_and_a_group_spreads_out_round_where_it_was_sent()
    {
        var w = Blank();
        for (int y = 5; y < 45; y++) w.Ground[25, y] = Ground.Mountain;
        var units = Enumerable.Range(0, 9).Select(i => Sim.Spawn(w, World.Player, i % 3 == 0 ? Kind.Tank : Kind.Trike, 10 + i % 3, 20 + i / 3)!).ToList();
        Sim.MoveGroup(w, units, Order.Move, 40, 22);
        Assert.Equal(9, units.Select(u => (u.Gx, u.Gy)).Distinct().Count());
        Assert.True(RunUntil(w, () => units.All(u => u.Order == Order.Idle), 120));
        Assert.All(units, u => Assert.True(Field.Dist(u.Tx, u.Ty, 40, 22) <= 3.5, $"{u.Type} at {u.Tx},{u.Ty}"));
        Assert.Equal(9, units.Select(u => (u.Tx, u.Ty)).Distinct().Count());
        Assert.All(units, u => Assert.Equal(u, w.Occ[u.Tx, u.Ty]));
    }

    [Fact]
    public void Paths_are_worked_out_a_few_at_a_time()
    {
        var w = Blank();
        var units = Enumerable.Range(0, 40).Select(i => Sim.Spawn(w, World.Player, Kind.Trike, 5 + i % 10, 5 + i / 10)!).ToList();
        foreach (var u in units) Sim.Command(w, u, Order.Move, 55, 55);
        Assert.Equal(40, w.PathQueue.Count);
        Sim.Step(w);
        Assert.Equal(Sim.PathsPerStep, units.Count(u => !u.PathPending));
        TestWorld.Run(w, 0.2);
        Assert.All(units, u => Assert.False(u.PathPending));
    }

    [Fact]
    public void A_crowded_battle_of_three_hundred_units_runs_well_within_a_frame()
    {
        var w = Blank(Ground.Sand);
        Rock(w, 0, 0, 63, 12);
        Rock(w, 0, 51, 63, 63);
        Kind[] mix = [Kind.Tank, Kind.Infantry, Kind.Trike, Kind.RocketTank, Kind.Quad, Kind.Troopers];
        var all = new List<Unit>();
        for (int i = 0; i < 150; i++)
        {
            all.Add(Sim.Spawn(w, World.Player, mix[i % mix.Length], 2 + i % 60, 2 + i / 60 * 2)!);
            all.Add(Sim.Spawn(w, Enemy, mix[i % mix.Length], 2 + i % 60, 61 - i / 60 * 2)!);
        }
        Assert.Equal(300, all.Count(u => u is not null));
        foreach (var u in all) Sim.Command(w, u, Order.AttackMove, u.Tx, u.Owner == World.Player ? 58 : 5);
        TestWorld.Run(w, 1);
        var sw = Stopwatch.StartNew();
        int steps = 60 * 20;
        long worst = 0;
        for (int i = 0; i < steps; i++)
        {
            long before = sw.ElapsedTicks;
            Sim.Step(w);
            worst = Math.Max(worst, sw.ElapsedTicks - before);
        }
        double average = sw.Elapsed.TotalMilliseconds / steps;
        output.WriteLine($"300 units: average step {average:0.000} ms, worst {worst * 1000.0 / Stopwatch.Frequency:0.0} ms");
        Assert.True(average < 4, $"average step {average:0.00} ms (worst {worst * 1000.0 / Stopwatch.Frequency:0.0} ms)");
        Assert.True(w.Units.Count < 300, "they fought");
    }

    // ───────────────────────────── sandmaws ─────────────────────────────

    [Fact]
    public void A_sandmaw_swallows_what_moves_on_the_sand_but_never_comes_onto_rock()
    {
        var w = Blank(Ground.Sand);
        Rock(w, 30, 0, 40, 63);
        var maw = new Creature { X = 20.5, Y = 20.5 };
        w.Creatures.Add(maw);
        var onRock = Sim.Spawn(w, World.Player, Kind.Tank, 33, 20)!;
        var onSand = Sim.Spawn(w, World.Player, Kind.Trike, 24, 22)!;
        Assert.True(RunUntil(w, () => onSand.Dead, 30));
        Assert.Equal(1, maw.Eaten);
        Assert.Equal(1, w.PlayerHouse.UnitsLost);
        Assert.Contains(w.Effects, e => e.Kind == Fx.Swallow);
        Run(w, 30);
        Assert.False(onRock.Dead);
        Assert.True(maw.X < 30);
        // Harvesters draw it first.
        var w2 = Blank(Ground.Sand);
        var maw2 = new Creature { X = 20.5, Y = 20.5 };
        w2.Creatures.Add(maw2);
        var trike = Sim.Spawn(w2, World.Player, Kind.Trike, 25, 20)!;
        var hv = Sim.Spawn(w2, World.Player, Kind.Harvester, 20, 27)!;
        trike.Order = hv.Order = Order.Idle;
        Run(w2, 1.1);
        Assert.Equal(hv, maw2.Prey);
    }

    [Fact]
    public void A_sandmaw_dives_after_three_meals_or_when_hurt_and_comes_back_elsewhere()
    {
        var w = Blank(Ground.Sand);
        var maw = new Creature { X = 30.5, Y = 30.5 };
        w.Creatures.Add(maw);
        for (int i = 0; i < 3; i++)
        {
            var u = Sim.Spawn(w, World.Player, Kind.Infantry, 32, 30)!;
            Assert.True(RunUntil(w, () => u.Dead, 30));
            Run(w, 3);
        }
        Assert.True(maw.Dormant > 0);
        Assert.Null(maw.Prey);
        Assert.True(RunUntil(w, () => maw.Dormant <= 0, 200));
        Assert.Equal(0, maw.Eaten);
        Sim.HurtCreature(w, maw, Creature.MaxHp * 0.3);
        Assert.Equal(0, maw.Dormant);                 // a scratch
        Sim.HurtCreature(w, maw, Creature.MaxHp * 0.4);
        Assert.True(maw.Dormant > 0);
        Assert.True(RunUntil(w, () => maw.Dormant <= 0, 200));
        Assert.Equal(Creature.MaxHp, maw.Hp);
    }

    [Fact]
    public void Units_shoot_at_a_sandmaw_that_comes_up_near_them()
    {
        var w = Blank(Ground.Sand);
        var maw = new Creature { X = 22.5, Y = 20.5 };
        w.Creatures.Add(maw);
        Sim.Spawn(w, World.Player, Kind.Tank, 20, 20);
        var bait = Sim.Spawn(w, Enemy, Kind.Infantry, 23, 22)!;
        bait.Hp = 1;
        bait.Order = Order.Idle;
        Run(w, 0.6);
        Assert.True(maw.Hp < Creature.MaxHp || maw.Dormant > 0);
    }

    // ───────────────────────────── skylifters ─────────────────────────────

    [Fact]
    public void A_skylifter_flies_a_full_harvester_home_from_a_distant_field()
    {
        var w = Blank(Ground.Sand, credits: 0);
        Rock(w, 2, 2, 16, 16);
        Ore(w, 50, 50, 55, 55);
        Sim.Put(w, World.Player, Kind.Yard, 5, 5);
        Sim.Put(w, World.Player, Kind.Turbine, 8, 5);
        Sim.Put(w, World.Player, Kind.Turbine, 11, 5);
        var refinery = Sim.Put(w, World.Player, Kind.Refinery, 5, 9);
        var lifter = Sim.Spawn(w, World.Player, Kind.Skylifter, 10, 10)!;
        lifter.HomeX = lifter.HomeY = 10;
        var hv = Sim.Spawn(w, World.Player, Kind.Harvester, 52, 52)!;
        hv.Cargo = Rules.HarvesterLoad;
        hv.Order = Order.Return;
        Assert.True(RunUntil(w, () => hv.CarriedBy == lifter, 30));
        Assert.Null(w.Occ[52, 52]);
        Assert.True(RunUntil(w, () => hv.CarriedBy is null, 30));
        Assert.True(Field.Dist(hv.Tx, hv.Ty, refinery.Door.X, refinery.Door.Y) <= 2);
        Assert.True(RunUntil(w, () => w.PlayerHouse.Harvested >= Rules.HarvesterLoad - 1, 20));
        // Shot down with a harvester aboard, both are lost.
        hv.Order = Order.Return;
        lifter.Carrying = hv;
        hv.CarriedBy = lifter;
        Sim.Kill(w, lifter, Enemy);
        Assert.True(hv.Dead);
    }
}
