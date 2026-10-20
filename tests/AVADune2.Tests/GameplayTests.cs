using AVADune2.Engine;
using static AVADune2.Tests.TestWorld;

namespace AVADune2.Tests;

/// <summary>The base and the economy: putting buildings down, power, the yard and the factories, credits,
/// harvesting, storage, blooms and the starport.</summary>
public class GameplayTests
{
    // ───────────────────────────── putting buildings down ─────────────────────────────

    [Fact]
    public void A_building_goes_only_on_rock_clear_of_others_and_next_to_the_base()
    {
        var w = Blank(Ground.Sand);
        Rock(w, 5, 5, 24, 24);
        w.Ground[20, 6] = Ground.Mountain;
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        Assert.Null(Sim.CanPlace(w, World.Player, Kind.Turbine, 12, 10));      // right beside the yard
        Assert.Null(Sim.CanPlace(w, World.Player, Kind.Turbine, 13, 10));      // a lane's width away
        Assert.Equal("BUILD NEXT TO YOUR BASE", Sim.CanPlace(w, World.Player, Kind.Turbine, 15, 10));
        Assert.Equal("NOT ON ANOTHER BUILDING", Sim.CanPlace(w, World.Player, Kind.Turbine, 11, 11));
        Assert.Equal("BUILD ON ROCK", Sim.CanPlace(w, World.Player, Kind.Turbine, 3, 10));
        Assert.Equal("OFF THE MAP", Sim.CanPlace(w, World.Player, Kind.Turbine, -1, 10));
        Sim.Spawn(w, World.Player, Kind.Trike, 12, 8);
        Assert.Equal("UNITS IN THE WAY", Sim.CanPlace(w, World.Player, Kind.Turbine, 11, 7));
        // The enemy's base doesn't count as the player's.
        Sim.Put(w, Enemy, Kind.Yard, 20, 20);
        Assert.Equal("BUILD NEXT TO YOUR BASE", Sim.CanPlace(w, World.Player, Kind.Turbine, 18, 20));
        Assert.Null(Sim.CanPlace(w, Enemy, Kind.Turbine, 18, 20));
        Assert.True(Sim.Place(w, World.Player, Kind.Turbine, 12, 10));
        Assert.False(Sim.Place(w, World.Player, Kind.Turbine, 12, 10));
    }

    [Fact]
    public void Concrete_gives_a_building_its_full_strength_and_reaches_the_base_further()
    {
        var w = Blank();
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        // On bare rock a building stands at six tenths of its strength.
        Assert.True(Sim.Place(w, World.Player, Kind.Turbine, 13, 10));
        var bare = w.BuildingAt(13, 10)!;
        Assert.Equal(0, bare.Footing);
        Assert.Equal(bare.MaxHp * 0.6, bare.Hp, 3);
        // Concrete first: full strength.
        Assert.True(Sim.Place(w, World.Player, Kind.Slab, 10, 13));
        Assert.Equal(World.Player + 1, w.Slab[11, 14]);
        Assert.True(Sim.Place(w, World.Player, Kind.Turbine, 10, 13));
        Assert.Equal(1, w.BuildingAt(10, 13)!.Footing);
        Assert.Equal(w.BuildingAt(10, 13)!.MaxHp, w.BuildingAt(10, 13)!.Hp);
        // Half on concrete.
        Assert.True(Sim.Place(w, World.Player, Kind.Slab, 7, 10));
        Assert.True(Sim.Place(w, World.Player, Kind.Turbine, 6, 10));
        Assert.Equal(0.5, w.BuildingAt(6, 10)!.Footing);
        // A path of concrete takes the base further out; concrete can't go where there's already concrete.
        Assert.Equal("BUILD NEXT TO YOUR BASE", Sim.CanPlace(w, World.Player, Kind.Silo, 10, 18));
        Assert.True(Sim.Place(w, World.Player, Kind.Slab, 10, 16));
        Assert.Null(Sim.CanPlace(w, World.Player, Kind.Silo, 10, 18));
        Assert.Equal("CONCRETE IS ALREADY THERE", Sim.CanPlace(w, World.Player, Kind.Slab, 10, 16));
        // Nobody builds on the enemy's concrete.
        Sim.Put(w, Enemy, Kind.Yard, 30, 30, slab: true);
        w.Slab[33, 30] = Enemy + 1;
        Assert.Equal("NOT ON THE ENEMY'S CONCRETE", Sim.CanPlace(w, World.Player, Kind.Wall, 33, 30));
        Assert.Null(Sim.CanPlace(w, Enemy, Kind.Wall, 33, 30));
    }

    [Fact]
    public void A_mobile_yard_unfolds_only_on_open_rock()
    {
        var w = Blank(Ground.Sand);
        Rock(w, 20, 20, 30, 30);
        var mcv = Sim.Spawn(w, World.Player, Kind.Mcv, 10, 10)!;
        Sim.Command(w, mcv, Order.Deploy, 10, 10);
        Run(w, 0.5);
        Assert.False(mcv.Dead);
        Assert.Equal(Order.Idle, mcv.Order);
        Sim.Command(w, mcv, Order.Move, 24, 24);
        Assert.True(RunUntil(w, () => mcv.Order == Order.Idle, 30));
        Assert.True(Sim.CanDeploy(w, mcv));
        Sim.Command(w, mcv, Order.Deploy, 24, 24);
        Run(w, 0.5);
        Assert.True(mcv.Dead);
        var yard = Assert.Single(w.Buildings);
        Assert.Equal(Kind.Yard, yard.Type);
        Assert.Equal((24, 24), (yard.X, yard.Y));
        Assert.Equal(0, w.PlayerHouse.UnitsLost);
    }

    // ───────────────────────────── power ─────────────────────────────

    [Fact]
    public void Wind_turbines_make_power_buildings_use_it_and_a_damaged_turbine_makes_less()
    {
        var w = Blank();
        Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
        Sim.UpdatePower(w);
        Assert.Equal(1, w.PlayerHouse.PowerRatio);
        Sim.Put(w, World.Player, Kind.Refinery, 13, 10);
        Sim.Put(w, World.Player, Kind.Outpost, 10, 13);
        Sim.UpdatePower(w);
        Assert.Equal(60, w.PlayerHouse.PowerUsed);
        Assert.True(w.PlayerHouse.LowPower);
        Assert.False(w.PlayerHouse.Radar);
        var t = Sim.Put(w, World.Player, Kind.Turbine, 7, 10, slab: true);
        Sim.UpdatePower(w);
        Assert.Equal(Rules.TurbinePower, w.PlayerHouse.PowerMade);
        Assert.False(w.PlayerHouse.LowPower);
        Assert.True(w.PlayerHouse.Radar);
        t.Hp = t.MaxHp / 2;
        Sim.UpdatePower(w);
        Assert.Equal(Rules.TurbinePower / 2.0, w.PlayerHouse.PowerMade, 3);
        Assert.Equal(50 / 60.0, w.PlayerHouse.PowerRatio, 3);
        Assert.False(w.PlayerHouse.Radar);
    }

    [Fact]
    public void Low_power_slows_the_factories_and_stops_the_turrets()
    {
        double MakeTime(bool powered)
        {
            var w = Blank();
            var yard = Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
            if (powered) Sim.Put(w, World.Player, Kind.Turbine, 13, 10, slab: true);
            Sim.Put(w, World.Player, Kind.Outpost, 10, 13);
            Sim.Put(w, World.Player, Kind.Refinery, 13, 13);
            Sim.Put(w, World.Player, Kind.HeavyFactory, 7, 13);
            Assert.True(Sim.Enqueue(w, yard, Kind.Slab));
            int steps = 0;
            while (yard.Ready is null && steps < 60 * 60) { Sim.Step(w); steps++; }
            return steps / 60.0;
        }
        double full = MakeTime(true), low = MakeTime(false);
        Assert.InRange(full, Rules.Of(Kind.Slab).Time * 0.95, Rules.Of(Kind.Slab).Time * 1.1);
        Assert.True(low > full * 2.5, $"{low} vs {full}");

        var w2 = Blank();
        Sim.Put(w2, World.Player, Kind.Yard, 10, 10);
        Sim.Put(w2, World.Player, Kind.Turret, 13, 10);
        Sim.Put(w2, World.Player, Kind.Barracks, 10, 13);
        Sim.Put(w2, World.Player, Kind.Outpost, 13, 13);
        var target = Sim.Spawn(w2, Enemy, Kind.Trike, 16, 10)!;
        Run(w2, 5);
        Assert.Equal(target.MaxHp, target.Hp);        // no power: the turret is dead
        Sim.Put(w2, World.Player, Kind.Turbine, 7, 10);
        Run(w2, 5);
        Assert.True(target.Dead || target.Hp < target.MaxHp);
    }

    // ───────────────────────────── the yard and the factories ─────────────────────────────

    [Fact]
    public void The_yard_builds_one_thing_at_a_time_and_waits_for_it_to_be_put_down()
    {
        var w = Blank();
        var yard = Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
        Assert.True(Sim.Enqueue(w, yard, Kind.Turbine));
        Assert.True(Sim.Enqueue(w, yard, Kind.Turbine));
        Run(w, Rules.Of(Kind.Turbine).Time + 0.5);
        Assert.Equal(Kind.Turbine, yard.Ready);
        Assert.Single(yard.Queue);
        Assert.Equal(0, yard.Progress);
        Run(w, 5);
        Assert.Equal(0, yard.Progress);                // nothing more until it's down
        Assert.False(Sim.PlaceReady(w, yard, 40, 40)); // not there
        Assert.True(Sim.PlaceReady(w, yard, 13, 10));
        Assert.Null(yard.Ready);
        Run(w, 1);
        Assert.True(yard.Progress > 0);
        Assert.Equal(Kind.Turbine, w.BuildingAt(13, 10)!.Type);
    }

    [Fact]
    public void Credits_are_paid_as_the_work_goes_on_and_given_back_when_it_is_cancelled()
    {
        var w = Blank(credits: 1000);
        var yard = Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
        Sim.Enqueue(w, yard, Kind.Refinery);
        Assert.False(Sim.Enqueue(w, yard, Kind.Refinery)); // the refinery needs a turbine first
        Sim.Put(w, World.Player, Kind.Turbine, 13, 10);
        Assert.True(Sim.Enqueue(w, yard, Kind.Turbine));
        yard.Queue.Clear();
        Sim.Enqueue(w, yard, Kind.Refinery);
        Run(w, Rules.Of(Kind.Refinery).Time / 2);
        Assert.InRange(w.PlayerHouse.Credits, 1000 - 400 * 0.55, 1000 - 400 * 0.45);
        Assert.True(Sim.Cancel(w, yard, Kind.Refinery));
        Assert.Equal(1000, w.PlayerHouse.Credits, 3);
        Assert.Empty(yard.Queue);
        // With no credits the work stops, and the player is told.
        w.PlayerHouse.Credits = 0;
        Assert.True(Sim.Enqueue(w, yard, Kind.Turbine));
        Run(w, 3);
        Assert.Equal(0, yard.Progress);
        Assert.Contains("NOT ENOUGH CREDITS", w.News);
        // A finished building given back is refunded in full.
        w.PlayerHouse.Credits = 500;
        Run(w, Rules.Of(Kind.Turbine).Time + 1);
        Assert.Equal(Kind.Turbine, yard.Ready);
        w.PlayerHouse.Credits = 500;
        Assert.True(Sim.Cancel(w, yard, Kind.Turbine));
        Assert.Equal(500 + Rules.Of(Kind.Turbine).Cost, w.PlayerHouse.Credits, 3);
        w.PlayerHouse.Credits = 500;
        Assert.Null(yard.Ready);
    }

    [Fact]
    public void A_factory_queues_five_and_its_units_come_out_of_the_door_to_the_rally_point()
    {
        var w = Blank();
        Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
        Sim.Put(w, World.Player, Kind.Turbine, 13, 10);
        Sim.Put(w, World.Player, Kind.Turbine, 16, 10);
        Sim.Put(w, World.Player, Kind.Refinery, 10, 13);
        var lf = Sim.Put(w, World.Player, Kind.LightFactory, 14, 13);
        for (int i = 0; i < 5; i++) Assert.True(Sim.Enqueue(w, lf, Kind.Trike));
        Assert.False(Sim.Enqueue(w, lf, Kind.Trike));
        Assert.False(Sim.Enqueue(w, lf, Kind.Tank));     // not this factory's
        lf.RallyX = 20;
        lf.RallyY = 25;
        Run(w, Rules.Of(Kind.Trike).Time + 0.2);
        var trike = Assert.Single(w.UnitsOf(World.Player), u => u.Type == Kind.Trike);
        Assert.True(Field.Dist(trike.X, trike.Y, lf.Door.X + 0.5, lf.Door.Y + 0.5) < 2);
        Assert.Equal(Order.Move, trike.Order);
        Assert.True(RunUntil(w, () => trike.Order == Order.Idle, 20));
        Assert.True(Field.Dist(trike.X, trike.Y, 20.5, 25.5) < 1.5);
    }

    [Fact]
    public void What_can_be_built_depends_on_the_tech_the_buildings_standing_and_the_House()
    {
        var w = Blank();
        Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
        Assert.True(Sim.CanBuild(w, World.Player, Kind.Turbine));
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Refinery));
        Sim.Put(w, World.Player, Kind.Turbine, 13, 10);
        Assert.True(Sim.CanBuild(w, World.Player, Kind.Refinery));
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Tank));      // no heavy factory
        Sim.Put(w, World.Player, Kind.HeavyFactory, 10, 14);
        Sim.Put(w, World.Player, Kind.HighTech, 14, 14);
        Assert.True(Sim.CanBuild(w, World.Player, Kind.Tank));
        Assert.True(Sim.CanBuild(w, World.Player, Kind.Resonator));  // Aldaine's own
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Juggernaut));
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Beguiler));
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Yard));       // only by unfolding a mobile yard
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Nomads));
        w.PlayerHouse.Tech = 1;
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Tank));
        Assert.True(Sim.CanBuild(w, World.Player, Kind.Refinery));
        Assert.False(Sim.CanBuild(w, World.Player, Kind.Barracks));
        // Every buildable thing names a maker that makes it.
        foreach (var k in Enum.GetValues<Kind>().Where(k => Rules.Of(k).Tech < Rules.NoTech))
            Assert.Contains(k, Rules.Makes[Rules.Of(k).MadeBy]);
    }

    // ───────────────────────────── credits and harvesting ─────────────────────────────

    [Fact]
    public void A_refinery_comes_with_a_harvester_which_fills_up_on_the_solarite_and_brings_it_home()
    {
        var w = Blank(Ground.Sand, credits: 0);
        Rock(w, 5, 5, 20, 20);
        Ore(w, 23, 8, 28, 14, 300);
        Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
        Sim.Put(w, World.Player, Kind.Turbine, 13, 10);
        Assert.True(Sim.Place(w, World.Player, Kind.Refinery, 14, 13));
        var hv = Assert.Single(w.UnitsOf(World.Player));
        Assert.Equal(Kind.Harvester, hv.Type);
        Assert.Equal(Order.Harvest, hv.Order);
        int oreBefore = Enumerable.Range(0, Field.N * Field.N).Sum(i => w.Ore[i % Field.N, i / Field.N]);
        Assert.True(RunUntil(w, () => hv.Order == Order.Return, 90));
        Assert.Equal(Rules.HarvesterLoad, hv.Cargo);
        Assert.True(RunUntil(w, () => hv.Order == Order.Docked, 60));
        Assert.True(RunUntil(w, () => hv.Order == Order.Harvest, 20));
        Assert.Equal(Rules.HarvesterLoad, w.PlayerHouse.Credits, 0);
        Assert.Equal(Rules.HarvesterLoad, w.PlayerHouse.Harvested, 0);
        int oreAfter = Enumerable.Range(0, Field.N * Field.N).Sum(i => w.Ore[i % Field.N, i / Field.N]);
        Assert.Equal(oreBefore - Rules.HarvesterLoad, oreAfter);
    }

    [Fact]
    public void Credits_are_stored_in_refineries_and_silos_and_what_they_cannot_hold_is_lost()
    {
        var w = Blank(credits: 0);
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        Sim.Put(w, World.Player, Kind.Turbine, 13, 10);
        Sim.Put(w, World.Player, Kind.Refinery, 10, 13);
        Sim.UpdatePower(w);
        Assert.Equal(Rules.StorePerBuilding, w.PlayerHouse.Capacity);
        Assert.Equal(800, Sim.Store(w, World.Player, 800));
        Assert.Equal(200, Sim.Store(w, World.Player, 800));
        Assert.Equal(1000, w.PlayerHouse.Credits);
        Assert.Contains("STORAGE IS FULL - BUILD SILOS", w.News);
        Sim.Put(w, World.Player, Kind.Silo, 14, 13);
        Sim.UpdatePower(w);
        Assert.Equal(2 * Rules.StorePerBuilding, w.PlayerHouse.Capacity);
        Assert.Equal(500, Sim.Store(w, World.Player, 500));
        // Spending is never limited: starting credits may be more than the storage.
        w.PlayerHouse.Credits = 5000;
        Assert.Equal(0, Sim.Store(w, World.Player, 10));
    }

    [Fact]
    public void A_refinery_left_without_a_harvester_has_one_flown_in_for_a_price()
    {
        var w = Blank(credits: 1000);
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        Sim.Put(w, World.Player, Kind.Turbine, 13, 10);
        Sim.Put(w, World.Player, Kind.Refinery, 10, 13);
        Assert.Empty(w.UnitsOf(World.Player));
        Run(w, Sim.ReplaceSeconds - 2);
        Assert.Empty(w.UnitsOf(World.Player));
        Run(w, 4);
        Assert.Single(w.UnitsOf(World.Player), u => u.Type == Kind.Harvester);
        Assert.Equal(1000 - Sim.ReplacePrice, w.PlayerHouse.Credits, 3);
    }

    [Fact]
    public void A_bloom_bursts_under_a_unit_and_throws_solarite_over_the_sand()
    {
        var w = Blank(Ground.Sand);
        w.Bloom[20, 20] = true;
        var trike = Sim.Spawn(w, World.Player, Kind.Trike, 16, 20)!;
        Sim.Command(w, trike, Order.Move, 24, 20);
        Run(w, 6);
        Assert.False(w.Bloom[20, 20]);
        Assert.True(w.Ore[20, 20] > 0 && w.Ore[22, 21] > 0 && w.Ore[18, 19] > 0);
        Assert.Equal(0, w.Ore[26, 20]);
        Assert.True(trike.Hp < trike.MaxHp);
        // A shot bursts one too.
        w.Bloom[40, 40] = true;
        Sim.Spawn(w, World.Player, Kind.Tank, 36, 40);
        var dummy = Sim.Spawn(w, Enemy, Kind.Infantry, 40, 40)!;
        Run(w, 4);
        Assert.False(w.Bloom[40, 40]);
        Assert.True(dummy.Dead || dummy.Hp < dummy.MaxHp);
    }

    // ───────────────────────────── the starport, repairs ─────────────────────────────

    [Fact]
    public void The_starport_sells_at_changing_prices_and_the_frigate_brings_the_order()
    {
        var w = Blank(credits: 3000);
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        var port = Sim.Put(w, World.Player, Kind.Starport, 14, 10);
        Sim.Step(w);
        var prices = new HashSet<int>();
        for (int i = 0; i < 12; i++)
        {
            int p = Sim.Price(w, Kind.Tank);
            Assert.InRange(p, Rules.Of(Kind.Tank).Cost * 0.6 - 5, Rules.Of(Kind.Tank).Cost * 1.5 + 5);
            prices.Add(p);
            Run(w, Sim.PriceSeconds);
        }
        Assert.True(prices.Count > 2);
        double before = w.PlayerHouse.Credits;
        int price = Sim.Price(w, Kind.Quad);
        Assert.True(Sim.Buy(w, World.Player, Kind.Quad));
        Assert.True(Sim.Buy(w, World.Player, Kind.Harvester));
        Assert.Equal(before - price - Sim.Price(w, Kind.Harvester), w.PlayerHouse.Credits, 3);
        Assert.False(Sim.Buy(w, World.Player, Kind.Infantry));   // not sold here
        Run(w, Sim.FrigateSeconds - 1);
        Assert.Empty(w.UnitsOf(World.Player));
        Run(w, 2);
        Assert.Equal(2, w.UnitsOf(World.Player).Count());
        Assert.All(w.UnitsOf(World.Player), u => Assert.True(port.Reach(u.X, u.Y) < 3));
    }

    [Fact]
    public void Buildings_are_repaired_for_credits_and_the_repair_pad_mends_vehicles()
    {
        var w = Blank(credits: 2000);
        Sim.Put(w, World.Player, Kind.Yard, 10, 10, slab: true);
        var t = Sim.Put(w, World.Player, Kind.Turbine, 13, 10, slab: true);
        t.Hp = t.MaxHp / 2;
        t.Repairing = true;
        Run(w, 30);
        Assert.Equal(t.MaxHp, t.Hp);
        Assert.False(t.Repairing);
        Assert.True(w.PlayerHouse.Credits < 2000);
        var pad = Sim.Put(w, World.Player, Kind.RepairPad, 10, 14);
        var tank = Sim.Spawn(w, World.Player, Kind.Tank, 20, 20)!;
        tank.Hp = 60;
        Sim.Command(w, tank, Order.Repair, 20, 20);
        double credits = w.PlayerHouse.Credits;
        Assert.True(RunUntil(w, () => tank.Hp >= tank.MaxHp, 60));
        Assert.True(pad.Reach(tank.X, tank.Y) <= 1.0);
        Assert.Equal(Order.Idle, tank.Order);
        Assert.True(w.PlayerHouse.Credits < credits);
    }
}
