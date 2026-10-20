using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVADune2.Engine;
using AVADune2.Views;

namespace AVADune2.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set DUNE_SHOTS to a folder to save them as
/// PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        string folder = Path.Combine(Path.GetTempPath(), "avadune2-ui-" + Guid.NewGuid().ToString("N"));
        var g = new GameEngine(HighScoreTable.Load(folder), folder, seed: 5) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("DUNE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVADune2-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Plays (the pilot playing for the player) until a moment comes, or gives up.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 900)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick();
            if (t % 6 == 0) c.Advance(0.1);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(1.3);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 21480, 9);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + 1)));
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Choosing_a_house_and_the_briefing()
    {
        var (w, c, g) = Open();
        g.NewCampaign();
        g.HouseIndex = 1;
        c.Advance(0.5);
        Shot(w, c, "houses");
        g.ChooseHouse(Faction.Vrakos);
        Assert.Equal(GameState.Region, g.State);
        g.ChooseTerritory();
        Assert.Equal(GameState.Briefing, g.State);
        g.Idle(60 * 12);
        c.Advance(2);
        Shot(w, c, "briefing");
    }

    [AvaloniaFact]
    public void The_region_map()
    {
        var (w, c, g) = Open();
        g.NewCampaign();
        g.ChooseHouse(Faction.Sythe);
        var camp = g.Campaign!;
        // Four missions won.
        for (int m = 0; m < 4; m++) camp.Win(camp.Choices()[m % camp.Choices().Count], 3000);
        g.Go(camp.Mission, Faction.Sythe);
        g.Campaign!.Owners = camp.Owners;
        g.Campaign.At = camp.At;
        g.Campaign.Score = camp.Score;
        g.World.Outcome = 0;
        for (int i = 0; i < 60 * 6 && g.State != GameState.Debrief; i++) g.Tick();
        g.FinishDebrief();
        Assert.Equal(GameState.Region, g.State);
        g.RegionIndex = 1;
        c.Advance(0.3);
        Shot(w, c, "region");
    }

    [AvaloniaFact]
    public void An_early_base_harvesting()
    {
        var (w, c, g) = Open();
        g.Autopilot = true;
        g.Go(3, Faction.Aldaine);
        Assert.True(PlayUntil(g, c, g => g.World.BuildingsOf(World.Player).Count() >= 8 &&
            g.World.UnitsOf(World.Player).Any(u => u.Type == Kind.Harvester && u.Order == Order.Harvest && !u.Moving && u.Cargo > 200)));
        var h = g.World.UnitsOf(World.Player).First(u => u.Type == Kind.Harvester && u.Order == Order.Harvest && !u.Moving && u.Cargo > 200);
        var yard = g.World.BuildingsOf(World.Player).First(b => b.Type == Kind.Yard);
        g.CenterOn(yard.CX * 0.6 + h.X * 0.4, yard.CY * 0.6 + h.Y * 0.4);
        g.SelectAt(h.X, h.Y);
        c.Advance(0.2);
        Shot(w, c, "harvesting");
    }

    [AvaloniaFact]
    public void A_battle_at_a_base()
    {
        var (w, c, g) = Open();
        g.Autopilot = true;
        g.Go(4, Faction.Vrakos);
        // Wait for a big fight at the enemy's base.
        Assert.True(PlayUntil(g, c, g =>
        {
            var wd = g.World;
            var booms = wd.Effects.Where(e => e.Kind is Fx.Boom or Fx.BigBoom).ToList();
            if (booms.Count < 8) return false;
            double x = booms.Average(e => e.X), y = booms.Average(e => e.Y);
            return wd.Buildings.Count(b => b.Owner != World.Player && b.Reach(x, y) < 5) >= 4 && wd.Units.Count(u => u.Owner == World.Player && Field.Dist(u.X, u.Y, x, y) < 6) >= 6;
        }, 1400));
        var booms = g.World.Effects.Where(e => e.Kind is Fx.Boom or Fx.BigBoom).ToList();
        double bx = booms.Average(e => e.X), by = booms.Average(e => e.Y);
        g.CenterOn(bx, by);
        // The whole of the enemy's base in view, and a few of the attackers picked out.
        g.World.RevealAll = true;
        foreach (var u in g.World.UnitsOf(World.Player).Where(u => Rules.Fighter(u.Type)).OrderBy(u => Field.Dist(u.X, u.Y, bx, by)).Take(4)) g.Selected.Add(u);
        c.Advance(0.1);
        Shot(w, c, "battle");
    }

    [AvaloniaFact]
    public void A_sandmaw_swallows_a_unit()
    {
        var (w, c, g) = Open();
        g.Go(2, Faction.Sythe);
        var wd = g.World;
        wd.RevealAll = true;
        // A trike out on the open sand, and a sandmaw come up near it.
        var spot = Enumerable.Range(0, Field.N * Field.N).Select(i => (X: i % Field.N, Y: i / Field.N))
            .Where(p => Enumerable.Range(-4, 9).All(d => wd.IsSandy(p.X + d, p.Y) && wd.IsSandy(p.X, p.Y + d)) && wd.Ore[p.X, p.Y] == 0)
            .OrderBy(p => Field.Dist(p.X, p.Y, 32, 32)).First();
        var trike = Sim.Spawn(wd, World.Player, Kind.Tank, spot.X, spot.Y)!;
        Sim.Spawn(wd, World.Player, Kind.Trike, spot.X + 2, spot.Y + 3);
        Sim.Command(wd, trike, Order.Move, spot.X + 4, spot.Y);
        var maw = wd.Creatures[0];
        maw.Dormant = 0;
        maw.X = spot.X - 3.5;
        maw.Y = spot.Y + 0.5;
        g.CenterOn(spot.X, spot.Y);
        bool seen = false;
        for (int i = 0; i < 60 * 30 && !seen; i++)
        {
            g.Tick();
            if (i % 6 == 0) c.Advance(0.1);
            seen = maw.Mouth > 1.2 && maw.Mouth < 1.5;
        }
        Assert.True(seen);
        c.Advance(0.05);
        Shot(w, c, "sandmaw");
    }

    [AvaloniaFact]
    public void The_score_after_a_mission()
    {
        var (w, c, g) = Open();
        g.Autopilot = true;
        g.Go(3, Faction.Aldaine);
        Assert.True(PlayUntil(g, c, g => g.State != GameState.Playing || g.Phase != Phase.Play, 1500));
        for (int i = 0; i < 60 * 10 && g.State != GameState.Debrief; i++) g.Tick();
        Assert.Equal(GameState.Debrief, g.State);
        g.Idle(60 * 4);
        c.Advance(1);
        Shot(w, c, "score");
    }
}
