using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AVADune2.Engine;
using AVADune2.Views;

namespace AVADune2.Tests;

/// <summary>The keys and clicks as the window gets them, through the canvas, in a headless window.</summary>
public sealed class CanvasTests
{
    private static (GameCanvas Canvas, GameEngine G) Open()
    {
        string folder = TestWorld.TempFolder();
        var g = new GameEngine(HighScoreTable.Load(folder), folder, seed: 9) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
        return (canvas, g);
    }

    [AvaloniaFact]
    public void Battle_keys_give_orders_groups_speed_pause_and_sound()
    {
        var (c, g) = Open();
        g.Go(3, Faction.Aldaine);
        foreach (var h in g.World.Houses.Skip(1)) h.Brain = null;
        var tank = g.World.UnitsOf(World.Player).First(u => u.Type == Kind.Tank);
        g.SelectAt(tank.X, tank.Y);
        c.PressKey(Key.A);
        Assert.True(g.ArmedAttack);
        c.PressKey(Key.Escape);
        Assert.False(g.ArmedAttack);
        Assert.Equal(GameState.Playing, g.State);
        c.PressKey(Key.G);
        Assert.Equal(Order.Guard, tank.Order);
        c.PressKey(Key.S);
        Assert.Equal(Order.Idle, tank.Order);
        c.PressKey(Key.D3, ctrl: true);
        g.Clear();
        c.PressKey(Key.D3);
        Assert.Equal([tank], g.Selected);
        c.PressKey(Key.OemPlus);
        Assert.Equal(3, g.SpeedIndex);
        c.PressKey(Key.OemMinus);
        c.PressKey(Key.OemMinus);
        Assert.Equal(1, g.SpeedIndex);
        c.PressKey(Key.M);
        Assert.True(c.Sound.IsMuted);
        c.PressKey(Key.P);
        Assert.Equal(GameState.Paused, g.State);
        c.PressKey(Key.P);
        Assert.Equal(GameState.Playing, g.State);
        c.PressKey(Key.Escape);
        Assert.Equal(GameState.Paused, g.State);
        c.PressKey(Key.Q);
        Assert.Equal(GameState.Title, g.State);
    }

    [AvaloniaFact]
    public void Clicks_pick_out_order_and_build_through_the_sidebar()
    {
        var (c, g) = Open();
        g.Go(3, Faction.Aldaine);
        foreach (var h in g.World.Houses.Skip(1)) h.Brain = null;
        var tank = g.World.UnitsOf(World.Player).First(u => u.Type == Kind.Tank);
        g.CenterOn(tank.X, tank.Y);
        var at = new Point((tank.X - g.CamX) * Field.Tile, Field.TopBar + (tank.Y - g.CamY) * Field.Tile);
        c.LeftDown(at, false);
        c.LeftUp(at);
        Assert.Equal([tank], g.Selected);
        c.RightDown(at + new Point(5 * Field.Tile, 0));
        Assert.Equal(Order.Move, tank.Order);
        // A drag picks out a box.
        g.Clear();
        c.LeftDown(new Point(1, Field.TopBar + 1), false);
        c.LeftUp(new Point(Field.SideX - 1, Field.Height - 1));
        Assert.Contains(tank, g.Selected);
        // The first item on the yard's menu (concrete) is built by clicking it.
        g.Clear();
        var cell = GameCanvas.CellRect(0);
        Assert.Equal(Kind.Slab, g.Menu()[0]);
        c.LeftDown(cell.Center, false);
        Assert.Equal([Kind.Slab], g.Factory!.Queue);
        c.RightDown(cell.Center);
        Assert.Empty(g.Factory.Queue);
    }

    [AvaloniaFact]
    public void Menus_answer_their_keys()
    {
        var (c, g) = Open();
        c.PressKey(Key.K);
        Assert.Equal(GameState.Houses, g.State);
        Assert.True(g.Skirmish);
        c.PressKey(Key.Up);
        Assert.Equal(2, g.Opponents);
        c.PressKey(Key.Escape);
        Assert.Equal(GameState.Title, g.State);
        g.NewCampaign();
        c.PressKey(Key.Right);
        Assert.Equal(1, g.HouseIndex);
        c.PressKey(Key.D3);
        Assert.Equal(GameState.Region, g.State);
        Assert.Equal(Faction.Sythe, g.Campaign!.House);
        c.PressKey(Key.Right);
        Assert.Equal(1, g.RegionIndex);
        c.PressKey(Key.Space);
        Assert.Equal(GameState.Briefing, g.State);
        c.PressKey(Key.Space);
        Assert.Equal(GameState.Playing, g.State);
    }
}
