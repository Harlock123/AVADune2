using Avalonia.Input;
using AVADune2.Engine;
using static AVADune2.Tests.TestWorld;

namespace AVADune2.Tests;

/// <summary>The game around the battles, and the player's hand in them: picking out and ordering units, the menus,
/// the shroud, the computer's mind, winning and losing, the campaign and its region map, saving, the keys, the title,
/// the demonstration and the high scores.</summary>
public class EngineTests
{
    /// <summary>Straight into mission three as Aldaine, with the shroud lifted and the enemy asleep.</summary>
    private static GameEngine Battle(int mission = 3)
    {
        var g = NewGame();
        g.Go(mission, Faction.Aldaine);
        g.World.RevealAll = true;
        foreach (var h in g.World.Houses.Skip(1)) h.Brain = null;
        return g;
    }

    private static Unit Mine(GameEngine g, Kind k) => g.World.UnitsOf(World.Player).First(u => u.Type == k);

    // ───────────────────────────── picking out and ordering ─────────────────────────────

    [Fact]
    public void A_click_picks_out_a_unit_and_a_right_click_sends_it()
    {
        var g = Battle();
        var tank = Mine(g, Kind.Tank);
        g.SelectAt(tank.X + 0.2, tank.Y - 0.2);
        Assert.Equal([tank], g.Selected);
        int tx = (int)(tank.X + 6), ty = (int)(tank.Y + 1);
        g.OrderAt(tank.X + 6, tank.Y + 1);
        Assert.Equal(Order.Move, tank.Order);
        g.Run(60 * 10);
        Assert.Equal(Order.Idle, tank.Order);
        Assert.True(Field.Dist(tank.Tx, tank.Ty, tx, ty) <= 1.5);
        // A click on the ground lets go; a click on an enemy only shows it.
        g.SelectAt(1.5, 1.5);
        Assert.Empty(g.Selected);
        var enemy = g.World.Units.First(u => u.Owner != World.Player);
        g.SelectAt(enemy.X, enemy.Y);
        Assert.Empty(g.Selected);
        Assert.Equal(enemy, g.Inspected);
    }

    [Fact]
    public void A_box_picks_out_fighters_shift_adds_and_a_double_click_takes_all_of_a_kind()
    {
        var g = Battle();
        var w = g.World;
        var mine = w.UnitsOf(World.Player).ToList();
        double x0 = mine.Min(u => u.X) - 1, x1 = mine.Max(u => u.X) + 1, y0 = mine.Min(u => u.Y) - 1, y1 = mine.Max(u => u.Y) + 1;
        Sim.Spawn(w, World.Player, Kind.Harvester, (int)x0 + 1, (int)y0 + 1);
        g.SelectBox(x0, y0, x1 + 1, y1 + 1);
        Assert.Equal(mine.Count, g.Selected.Count);
        Assert.DoesNotContain(g.Selected, u => u.Type == Kind.Harvester);
        var trike = Mine(g, Kind.Trike);
        g.SelectAt(trike.X, trike.Y);
        Assert.Single(g.Selected);
        var inf = Mine(g, Kind.Infantry);
        g.SelectAt(inf.X, inf.Y, add: true);
        Assert.Equal(2, g.Selected.Count);
        g.SelectAt(inf.X, inf.Y, add: true);
        Assert.Single(g.Selected);
        var tank = Mine(g, Kind.Tank);
        g.CenterOn(tank.X, tank.Y);
        g.SelectSame(tank.X, tank.Y);
        Assert.Equal(w.UnitsOf(World.Player).Count(u => u.Type == Kind.Tank), g.Selected.Count);
        Assert.All(g.Selected, u => Assert.Equal(Kind.Tank, u.Type));
    }

    [Fact]
    public void Right_clicks_attack_harvest_send_home_and_repair_by_what_is_under_them()
    {
        var g = Battle(5);
        var w = g.World;
        var tank = Mine(g, Kind.Tank);
        var enemy = Sim.Spawn(w, TestWorld.Enemy, Kind.Trike, tank.Tx + 3, tank.Ty)!;
        g.SelectAt(tank.X, tank.Y);
        g.OrderAt(enemy.X, enemy.Y);
        Assert.Equal(Order.Attack, tank.Order);
        Assert.Equal(enemy, tank.Target);
        // A harvester sent onto solarite harvests it; onto a refinery, goes home.
        var refinery = Sim.Put(w, World.Player, Kind.Refinery, (int)tank.X - 6, (int)tank.Y - 8);
        var hv = Sim.Spawn(w, World.Player, Kind.Harvester, refinery.Door.X, refinery.Door.Y + 1)!;
        int ox = Enumerable.Range(0, Field.N * Field.N).First(i => w.Ore[i % Field.N, i / Field.N] > 0) % Field.N;
        int oy = Enumerable.Range(0, Field.N * Field.N).First(i => w.Ore[i % Field.N, i / Field.N] > 0) / Field.N;
        g.SelectAt(hv.X, hv.Y);
        g.OrderAt(ox + 0.5, oy + 0.5);
        Assert.Equal(Order.Harvest, hv.Order);
        Assert.Equal((ox, oy), (hv.Gx, hv.Gy));
        g.OrderAt(refinery.CX, refinery.CY);
        Assert.Equal(Order.Return, hv.Order);
        Assert.Equal(refinery, hv.Dock);
        // Onto the repair pad: repair.
        var pad = Sim.Put(w, World.Player, Kind.RepairPad, (int)tank.X + 4, (int)tank.Y - 8);
        g.SelectAt(tank.X, tank.Y);
        g.OrderAt(pad.CX, pad.CY);
        Assert.Equal(Order.Repair, tank.Order);
        // A factory picked out: its rally point.
        var hf = w.BuildingsOf(World.Player).First(b => b.Type == Kind.Yard);
        var lf = Sim.Put(w, World.Player, Kind.LightFactory, hf.X + 4, hf.Y + 4);
        g.SelectAt(lf.CX, lf.CY);
        Assert.Equal(lf, g.SelectedBuilding);
        g.OrderAt(30.5, 31.5);
        Assert.Equal((30, 31), (lf.RallyX, lf.RallyY));
    }

    [Fact]
    public void Attack_stop_guard_and_deploy_and_groups_by_number()
    {
        var g = Battle();
        var w = g.World;
        var tank = Mine(g, Kind.Tank);
        g.SelectAt(tank.X, tank.Y);
        g.ArmAttack();
        Assert.True(g.ArmedAttack);
        g.SelectAt(tank.X + 8, tank.Y);       // a left click with attack armed is the order
        Assert.Equal(Order.AttackMove, tank.Order);
        Assert.False(g.ArmedAttack);
        g.Stop();
        Assert.Equal(Order.Idle, tank.Order);
        g.Guard();
        Assert.Equal(Order.Guard, tank.Order);
        // Groups.
        var tanks = w.UnitsOf(World.Player).Where(u => u.Type == Kind.Tank).ToList();
        g.SelectBox(tanks.Min(u => u.X) - 0.4, tanks.Min(u => u.Y) - 0.4, tanks.Max(u => u.X) + 0.4, tanks.Max(u => u.Y) + 0.4);
        g.Group(1, assign: true);
        g.Clear();
        g.Group(1, assign: false);
        Assert.Equal(tanks.Count, g.Selected.Count);
        g.CenterOn(0, 0);
        g.Group(1, assign: false);        // again: look at them
        Assert.True(Field.Dist(g.CamX + Field.ViewW / 2, g.CamY + Field.ViewH / 2, tanks.Average(u => u.X), tanks.Average(u => u.Y)) < 3);
        // Deploy a mobile yard.
        var yard = w.BuildingsOf(World.Player).First();
        for (int x = yard.X + 5; x <= yard.X + 6; x++)
        for (int y = yard.Y + 5; y <= yard.Y + 6; y++) { w.Ground[x, y] = Ground.Rock; w.Ore[x, y] = 0; }
        var mcv = Sim.Spawn(w, World.Player, Kind.Mcv, yard.X + 5, yard.Y + 5)!;
        g.SelectAt(mcv.X, mcv.Y);
        g.Deploy();
        g.Run(10);
        Assert.True(mcv.Dead);
        Assert.Equal(2, w.Count(World.Player, Kind.Yard));
    }

    [Fact]
    public void The_menu_queues_buildings_and_a_finished_one_is_put_down_by_clicking()
    {
        var g = Battle();
        var w = g.World;
        Assert.Equal(Kind.Yard, g.Factory!.Type);
        Assert.Contains(Kind.Turbine, g.Menu());
        Assert.DoesNotContain(Kind.Palace, g.Menu());
        Assert.True(g.Build(Kind.Turbine));
        Assert.Equal([Kind.Turbine], g.Factory.Queue);
        g.Run((int)(60 * (Rules.Of(Kind.Turbine).Time + 1)));
        Assert.Equal(Kind.Turbine, g.Factory.Ready);
        Assert.False(g.Build(Kind.Barracks));      // put the finished one down first
        Assert.True(g.Build(Kind.Turbine));
        Assert.True(g.Placing);
        var yard = g.Factory;
        Assert.False(g.PlaceAt(1.5, 1.5));          // nowhere near
        Assert.True(g.Placing);
        var spot = Sim.FindSpot(w, World.Player, Kind.Turbine, yard.X + 1, yard.Y + 1)!.Value;
        Assert.True(g.PlaceAt(spot.X + 1, spot.Y + 1));
        Assert.False(g.Placing);
        Assert.Equal(Kind.Turbine, w.BuildingAt(spot.X, spot.Y)!.Type);
        // Right-clicking an item takes it off the queue.
        Assert.True(g.Build(Kind.Turbine));
        Assert.True(g.Unbuild(Kind.Turbine));
        Assert.Empty(yard.Queue);
    }

    // ───────────────────────────── the shroud ─────────────────────────────

    [Fact]
    public void The_shroud_lifts_round_the_players_units_and_hides_what_they_have_not_seen()
    {
        var g = NewGame();
        g.Go(3, Faction.Aldaine);
        var w = g.World;
        var yard = w.BuildingsOf(World.Player).First();
        g.Run(10);
        Assert.True(w.Seen[(int)yard.CX, (int)yard.CY]);
        var enemyYard = w.Buildings.First(b => b.Owner != World.Player);
        Assert.False(w.Seen[(int)enemyYard.CX, (int)enemyYard.CY]);
        Assert.Null(g.ThingAt(enemyYard.CX, enemyYard.CY));       // can't be picked out under the shroud
        var trike = Mine(g, Kind.Trike);
        trike.X = enemyYard.CX + 3.5;
        trike.Y = enemyYard.CY + 3.5;
        g.Run(10);
        Assert.True(w.Seen[(int)enemyYard.CX, (int)enemyYard.CY]);
        Assert.NotNull(g.ThingAt(enemyYard.CX, enemyYard.CY));
        // What's been seen stays seen.
        trike.X = yard.CX;
        trike.Y = yard.CY;
        g.Run(10);
        Assert.True(w.Seen[(int)enemyYard.CX, (int)enemyYard.CY]);
    }

    // ───────────────────────────── the computer's mind ─────────────────────────────

    [Fact]
    public void The_computer_keeps_its_power_up_rebuilds_what_it_lost_and_repairs()
    {
        var w = Blank(credits: 4000);
        MapGen.BuildBase(w, Enemy, 32, 32, 3, (5, 5));
        var brain = new Brain(Enemy) { BaseX = 32, BaseY = 32, Mission = 5 };
        brain.Remember(w);
        w.Houses[Enemy].Brain = brain;
        Sim.Put(w, World.Player, Kind.Yard, 4, 4);
        int turbines = w.Count(Enemy, Kind.Turbine);
        var lost = w.BuildingsOf(Enemy).First(b => b.Type == Kind.Turbine);
        var (lx, ly) = (lost.X, lost.Y);
        Sim.Kill(w, lost, World.Player);
        var hurt = w.BuildingsOf(Enemy).First(b => b.Type == Kind.Barracks);
        hurt.Hp = hurt.MaxHp * 0.4;
        Assert.True(RunUntil(w, () => w.Count(Enemy, Kind.Turbine) >= turbines, 60));
        Assert.Equal(Kind.Turbine, w.BuildingAt(lx, ly)?.Type);   // where it stood before
        Assert.True(hurt.Repairing || hurt.Hp > hurt.MaxHp * 0.4);
        Assert.True(w.Houses[Enemy].PowerMade >= w.Houses[Enemy].PowerUsed);
    }

    [Fact]
    public void The_computer_trains_an_army_and_harvesters_and_attacks_in_waves()
    {
        var w = Blank(Ground.Sand, credits: 6000);
        Rock(w, 20, 20, 44, 44);
        Rock(w, 0, 0, 10, 10);
        Ore(w, 46, 30, 52, 36);
        MapGen.BuildBase(w, Enemy, 32, 32, 4, (5, 5));
        Sim.Put(w, World.Player, Kind.Yard, 4, 4);
        var brain = new Brain(Enemy) { BaseX = 32, BaseY = 32, FirstWave = 60, WaveEvery = 60, WaveSize = 4, UnitGap = 3, ArmyCap = 20 };
        brain.Remember(w);
        w.Houses[Enemy].Brain = brain;
        Assert.True(RunUntil(w, () => brain.WavesSent > 0, 240));
        Assert.True(w.Count(Enemy, Kind.Harvester) >= 2);
        Assert.Contains(w.UnitsOf(Enemy), u => u.Order == Order.AttackMove);
        Assert.Equal(Kind.Yard, brain.WaveTarget!.Kind);
    }

    [Fact]
    public void The_computer_turns_its_army_on_anything_that_comes_near_its_base()
    {
        var w = Blank(credits: 0);
        Sim.Put(w, Enemy, Kind.Yard, 30, 30);
        var guards = Enumerable.Range(0, 3).Select(i => Sim.Spawn(w, Enemy, Kind.Tank, 26 + i, 26)!).ToList();
        var brain = new Brain(Enemy) { BaseX = 31, BaseY = 31, Attacks = false };
        w.Houses[Enemy].Brain = brain;
        Sim.Put(w, World.Player, Kind.Yard, 4, 4);
        var raider = Sim.Spawn(w, World.Player, Kind.Trike, 34, 34)!;
        raider.Hp = 5000;
        Run(w, 1);
        Assert.Single(brain.Threats(w));
        Assert.All(guards, u => Assert.Equal(raider, u.Target));
    }

    [Fact]
    public void The_pilot_wins_the_first_three_missions()
    {
        for (int m = 1; m <= 3; m++)
        {
            var c = new Campaign(Faction.Aldaine, 11) { Mission = m };
            var w = MapGen.Setup(c.ScenarioFor(c.Choices()[0]));
            Pilot.TakeOver(w);
            Assert.True(RunUntil(w, () => w.Outcome >= 0, 60 * 20), $"mission {m} undecided");
            Assert.Equal(0, w.Outcome);
        }
    }

    // ───────────────────────────── winning and losing ─────────────────────────────

    [Fact]
    public void A_harvesting_mission_is_won_by_harvesting_and_any_mission_lost_with_nothing_left()
    {
        var w = Blank();
        w.Goal = Goal.Harvest;
        w.GoalCredits = 1000;
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        Sim.Put(w, Enemy, Kind.Yard, 40, 40);
        Sim.CheckOutcome(w);
        Assert.Equal(-1, w.Outcome);
        w.PlayerHouse.Harvested = 1000;
        Sim.CheckOutcome(w);
        Assert.Equal(0, w.Outcome);
        var lost = Blank();
        var yard = Sim.Put(lost, World.Player, Kind.Yard, 10, 10);
        Sim.Put(lost, Enemy, Kind.Yard, 40, 40);
        Sim.Kill(lost, yard, Enemy);
        Sim.CheckOutcome(lost);
        Assert.Equal(1, lost.Outcome);
    }

    [Fact]
    public void A_destroy_mission_is_won_when_every_enemy_building_and_unit_is_gone()
    {
        var w = Blank();
        Sim.Put(w, World.Player, Kind.Yard, 10, 10);
        var by = Sim.Put(w, Enemy, Kind.Yard, 40, 40);
        var u = Sim.Spawn(w, Enemy, Kind.Trike, 50, 50)!;
        Sim.Spawn(w, Enemy, Kind.Skylifter, 50, 52);        // up in the air doesn't count
        Sim.Kill(w, by, World.Player);
        Sim.CheckOutcome(w);
        Assert.Equal(-1, w.Outcome);
        Sim.Kill(w, u, World.Player);
        Sim.CheckOutcome(w);
        Assert.Equal(0, w.Outcome);
        var d = Debrief.Of(w, 3, true);
        Assert.Equal(1, d.BuildingsKilled);
        Assert.Equal(1, d.UnitsKilled);
        Assert.True(d.Points >= 1500);
        Assert.True(Debrief.Of(w, 3, false).Points < d.Points);
    }

    [Fact]
    public void Winning_a_mission_shows_the_score_then_the_region_map_and_the_campaign_is_saved()
    {
        var g = NewGame();
        g.NewCampaign();
        Assert.Equal(GameState.Houses, g.State);
        g.ChooseHouse(Faction.Vrakos);
        Assert.Equal(GameState.Region, g.State);
        Assert.True(g.HasSave);
        var choices = g.Campaign!.Choices();
        Assert.Equal(3, choices.Count);
        g.ChooseTerritory(choices[2]);
        Assert.Equal(GameState.Briefing, g.State);
        Assert.Contains("HARVEST 1000", g.Campaign.Briefing(choices[2]));
        g.Tick(start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Faction.Vrakos, g.World.PlayerHouse.Faction);
        g.World.PlayerHouse.Harvested = 1000;
        g.Run(40);
        Assert.Equal(Phase.Won, g.Phase);
        g.Run((int)(60 * GameEngine.WonSeconds) + 5);
        Assert.Equal(GameState.Debrief, g.State);
        g.Run(60);
        g.FinishDebrief();
        Assert.Equal(GameState.Region, g.State);
        Assert.Equal(2, g.Campaign.Mission);
        Assert.Equal(Faction.Vrakos, g.Campaign.Owners[choices[2]]);
        Assert.True(g.Campaign.Score > 0);
        var saved = SaveFile.Load(g.SaveFolder)!;
        Assert.Equal(2, saved.Mission);
        Assert.Equal(g.Campaign.Score, saved.Score);
    }

    [Fact]
    public void Three_defeats_end_the_campaign_and_a_good_score_goes_on_the_table()
    {
        var g = NewGame();
        g.NewCampaign();
        g.ChooseHouse(Faction.Sythe);
        for (int i = 0; i < Campaign.MaxDefeats; i++)
        {
            Assert.Equal(GameState.Region, g.State);
            g.ChooseTerritory();
            g.BeginMission();
            g.Campaign!.Score += 20000;
            foreach (var b in g.World.BuildingsOf(World.Player).ToList()) Sim.Kill(g.World, b, 1);
            foreach (var u in g.World.UnitsOf(World.Player).ToList()) Sim.Kill(g.World, u, 1);
            g.Run(40);
            Assert.Equal(Phase.Lost, g.Phase);
            g.Run((int)(60 * GameEngine.LostSeconds) + 5);
            g.FinishDebrief();
        }
        Assert.Equal(GameState.GameOver, g.State);
        Assert.False(g.HasSave);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "LIR") g.Initials!.Type(ch);
        g.Tick();
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("LIR", g.HighScores.Entries[g.NewEntryIndex].Initials);
        Assert.Equal(g.Score, g.HighScores.Entries[g.NewEntryIndex].Score);
    }

    [Fact]
    public void A_skirmish_is_against_one_or_two_computer_houses_and_goes_back_to_the_title()
    {
        var g = NewGame();
        g.NewSkirmish();
        g.Opponents = 2;
        g.ChooseHouse(Faction.Aldaine);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(3, g.World.Houses.Count);
        Assert.All(g.World.Houses.Skip(1), h => Assert.NotNull(h.Brain));
        Assert.Equal(9, g.World.PlayerHouse.Tech);
        g.World.Outcome = 0;
        g.Run((int)(60 * GameEngine.WonSeconds) + 60);
        Assert.Equal(GameState.Debrief, g.State);
        g.FinishDebrief();
        Assert.Equal(GameState.Title, g.State);
    }

    // ───────────────────────────── the campaign ─────────────────────────────

    [Fact]
    public void The_region_map_offers_the_territories_next_to_the_last_one_taken()
    {
        var c = new Campaign(Faction.Aldaine, 1);
        Assert.Equal(Faction.Aldaine, c.Owners[c.At]);
        Assert.Equal(0, Campaign.Map[c.At].Row);
        Assert.Equal(3, c.Choices().Count);
        c.Win(Campaign.Id(1, 0), 100);
        var next = c.Choices();
        Assert.Equal([Campaign.Id(2, 0), Campaign.Id(2, 1)], next);
        Assert.All(next, id => Assert.Equal(2, Campaign.Map[id].Row));
        // The enemies and what's asked change as the campaign goes on.
        Assert.Equal(Goal.Harvest, c.ScenarioFor(next[0]).Goal);
        c.Win(next[0], 100);
        Assert.Equal(Goal.Destroy, c.ScenarioFor(c.Choices()[0]).Goal);
        Assert.Equal(3, c.ScenarioFor(c.Choices()[0]).Tech);
        while (c.Mission < Campaign.Missions) c.Win(c.Choices()[0], 100);
        var last = c.Choices();
        Assert.Equal([Campaign.Id(Campaign.Missions, 1)], last);
        var s = c.ScenarioFor(last[0]);
        Assert.True(s.Sovereign);
        Assert.Equal(2, s.Enemies.Length);
        var w = MapGen.Setup(s);
        Assert.Contains(w.Houses, h => h.Faction == Faction.Sovereign && h.Team == 1);
        Assert.Contains(w.Units, u => u.Type == Kind.Praetors);
        c.Win(last[0], 100);
        Assert.True(c.Won && c.Over);
        Assert.Empty(c.Choices());
        Assert.Equal(900, c.Score);
    }

    [Fact]
    public void Every_mission_map_has_a_way_from_the_player_to_every_enemy_and_solarite_near_home()
    {
        for (int m = 1; m <= Campaign.Missions; m++)
        {
            var c = new Campaign((Faction)(m % 3), 100 + m) { Mission = m };
            var s = c.ScenarioFor(c.Choices()[0]);
            var w = MapGen.Setup(s);
            var yard = w.BuildingsOf(World.Player).Single();
            var reach = MapGen.Reachable(w, (int)yard.CX, (int)yard.CY);
            foreach (var b in w.Buildings.Where(b => b.Owner != World.Player && b.Type == Kind.Yard))
                Assert.True(reach[b.X, b.Y + 2] || reach[b.X - 1, b.Y], $"mission {m}: no way to {b.Owner}");
            int ore = 0;
            for (int x = 0; x < Field.N; x++)
            for (int y = 0; y < Field.N; y++)
                if (w.Ore[x, y] > 0 && Field.Dist(x, y, yard.CX, yard.CY) < 18) ore += w.Ore[x, y];
            Assert.True(ore > 4000, $"mission {m}: only {ore} near home");
            Assert.Equal(m, w.PlayerHouse.Tech);
            Assert.Equal(s.Enemies.Length + (s.Sovereign ? 1 : 0) + 1, w.Houses.Count);
        }
    }

    [Fact]
    public void The_campaign_saves_and_loads_and_a_bad_file_is_ignored()
    {
        var c = new Campaign(Faction.Sythe, 77);
        c.Win(c.Choices()[1], 1234);
        c.Lose(50);
        string text = SaveFile.Write(c);
        var back = SaveFile.Read(text)!;
        Assert.Equal(c.House, back.House);
        Assert.Equal(c.Mission, back.Mission);
        Assert.Equal(c.Score, back.Score);
        Assert.Equal(c.Defeats, back.Defeats);
        Assert.Equal(c.At, back.At);
        Assert.Equal(c.Owners, back.Owners);
        Assert.Equal(c.Choices(), back.Choices());
        Assert.Null(SaveFile.Read("nonsense"));
        Assert.Null(SaveFile.Read(text.Replace("campaign 2", "campaign 9")));
        Assert.Null(SaveFile.Read(text.Replace("owners", "owners 1")));
        // On disk, and continued from the title.
        string folder = TempFolder();
        SaveFile.Save(c, folder);
        var g = new GameEngine(HighScoreTable.Load(folder), folder) { Silent = true };
        Assert.True(g.HasSave);
        Assert.True(g.Continue());
        Assert.Equal(GameState.Region, g.State);
        Assert.Equal(c.Score, g.Score);
        SaveFile.Delete(folder);
        Assert.False(g.HasSave);
    }

    // ───────────────────────────── the keys, the camera, the speed ─────────────────────────────

    [Fact]
    public void The_keys()
    {
        using var input = new InputHandler(gamepad: false);
        input.OnKeyDown(Key.Up, PhysicalKey.ArrowUp);
        var c = input.Player;
        Assert.Equal(-1, c.Dy);                  // up is -1, as on the screen
        Assert.Equal(0, c.Dx);
        input.OnKeyDown(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(1, input.Player.Dy);        // the newer of two opposite keys wins
        input.OnKeyUp(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up, PhysicalKey.ArrowUp);
        input.OnKeyDown(Key.NumPad4);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.NumPad4);
        // A Shift let go arrives as CapsLock: the physical key lets go of the Shift all the same.
        input.OnKeyDown(Key.LeftShift, PhysicalKey.ShiftLeft);
        Assert.True(input.Shift);
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftLeft);
        Assert.False(input.Shift);
        input.OnKeyDown(Key.LeftCtrl, PhysicalKey.ControlLeft);
        Assert.True(input.Ctrl);
        input.Clear();
        Assert.False(input.Ctrl);
        // The mouse.
        input.PointAt((300, 200));
        Assert.Equal((300.0, 200.0), input.Player.Point);
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
    }

    [Fact]
    public void The_arrows_and_the_screen_edge_scroll_the_map_the_way_they_point()
    {
        var g = Battle();
        using var input = new InputHandler(gamepad: false);
        g.CenterOn(32, 32);
        double x = g.CamX, y = g.CamY;
        input.OnKeyDown(Key.Up, PhysicalKey.ArrowUp);
        for (int i = 0; i < 30; i++) g.Tick(input.Player);
        Assert.True(g.CamY < y - 5);            // up the screen is up the map
        Assert.Equal(x, g.CamX);
        input.OnKeyUp(Key.Up, PhysicalKey.ArrowUp);
        input.OnKeyDown(Key.Right, PhysicalKey.ArrowRight);
        for (int i = 0; i < 30; i++) g.Tick(input.Player);
        Assert.True(g.CamX > x + 5);
        input.OnKeyUp(Key.Right, PhysicalKey.ArrowRight);
        double y2 = g.CamY;
        input.PointAt((400, Field.Height - 1));  // the bottom edge
        for (int i = 0; i < 30; i++) g.Tick(input.Player);
        Assert.True(g.CamY > y2 + 5);
        // Never past the map's edges.
        for (int i = 0; i < 600; i++) g.Tick(input.Player);
        Assert.Equal(Field.N - Field.ViewH, g.CamY);
        // A point on the screen to the map and back.
        var (mx, my) = g.ToMap(64, Field.TopBar + 96);
        Assert.Equal(g.CamX + 2, mx, 6);
        Assert.Equal(g.CamY + 3, my, 6);
    }

    [Fact]
    public void Faster_and_slower_game_speeds_run_more_or_fewer_steps_a_second()
    {
        double Clock(int speed)
        {
            var g = Battle();
            g.SpeedIndex = speed;
            double before = g.World.Clock;
            for (int i = 0; i < 60; i++) g.Update(1 / 60.0, default, false);
            return g.World.Clock - before;
        }
        double normal = Clock(2), fastest = Clock(4), slowest = Clock(0);
        Assert.Equal(1, normal, 1);
        Assert.Equal(2, fastest, 1);
        Assert.Equal(0.5, slowest, 1);
        var e = Battle();
        e.Faster();
        e.Faster();
        e.Faster();
        Assert.Equal(GameEngine.Speeds.Length - 1, e.SpeedIndex);
        e.Slower();
        Assert.Equal(3, e.SpeedIndex);
    }

    [Fact]
    public void Pausing_stops_the_battle()
    {
        var g = Battle();
        g.Pause();
        Assert.Equal(GameState.Paused, g.State);
        double clock = g.World.Clock;
        g.Run(60);
        Assert.Equal(clock, g.World.Clock);
        g.Resume();
        g.Run(60);
        Assert.True(g.World.Clock > clock);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = NewGame();
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        Assert.NotNull(g.World.PlayerHouse.Brain);
        Assert.True(g.World.RevealAll);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(start: true);
        Assert.Equal(GameState.Houses, g.State);
    }

    [Fact]
    public void The_player_hears_about_what_matters_and_only_sounds_from_the_screen()
    {
        var g = NewGame();
        g.Silent = false;
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Go(3, Faction.Aldaine);
        var w = g.World;
        foreach (var h in w.Houses.Skip(1)) h.Brain = null;
        var yard = w.BuildingsOf(World.Player).First();
        g.CenterOn(yard.CX, yard.CY);
        heard.Clear();
        Sim.Damage(w, yard, 10, Weapon.Gun, 1);
        g.Tick();
        Assert.Contains(Sfx.SayAttacked, heard);
        Assert.Equal("OUR BASE IS UNDER FIRE", g.Message);
        g.JumpToAlarm();
        Assert.True(Field.Dist(g.CamX + Field.ViewW / 2, g.CamY + Field.ViewH / 2, yard.CX, yard.CY) < 2);
        heard.Clear();
        w.Hear(Sfx.Cannon, (yard.CX + 30) % Field.N, (yard.CY + 30) % Field.N);
        w.Hear(Sfx.Boom, yard.CX, yard.CY);
        g.Tick();
        Assert.DoesNotContain(Sfx.Cannon, heard);
        Assert.Contains(Sfx.Boom, heard);
    }
}
