using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVADune2.Engine;

namespace AVADune2.Views;

/// <summary>
/// The screen: a fixed 1024×768 picture scaled to fit the window. In battle, the map fills the left with the top bar
/// over it, and the sidebar on the right holds the credits, the radar, what's picked out and the building menu. Runs
/// the engine from a 60 Hz timer and turns the mouse, the keys and the gamepad into its orders.
/// </summary>
public sealed partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly DispatcherTimer? _timer;
    private DateTime _last = DateTime.Now;
    private bool _padPausePrev, _padBackPrev, _padUpPrev, _padDownPrev, _padLeftPrev, _padRightPrev, _padAPrev, _padBPrev, _padXPrev, _padYPrev;
    private double _clock;
    /// <summary>The mouse on the screen (-1: off it), where a drag began, and the last click (for double clicks).</summary>
    private Point _mouse = new(-1, -1);
    private Point? _dragFrom;
    private bool _radarDrag;
    private DateTime _lastClick;
    private Point _lastClickAt;
    /// <summary>The gamepad's own cursor, shown once the stick has moved it.</summary>
    private Point _padCursor = new(400, 400);
    private bool _padActive;
    private int _menuScroll;

    public GameEngine Engine { get; }
    public SoundManager Sound { get; }
    public InputHandler Input { get; }
    public MapPainter Painter { get; } = new();

    public GameCanvas(bool silent = false) : this(new GameEngine(), new SoundManager(silent), startTimer: true) { }

    /// <summary>For tests: an engine of your own, and no timer (call <see cref="Tick"/>).</summary>
    public GameCanvas(GameEngine engine, SoundManager sound, bool startTimer, bool gamepad = true)
    {
        Engine = engine;
        Sound = sound;
        Input = new InputHandler(gamepad && startTimer);
        Engine.Sound += Sound.Play;
        Focusable = true;
        ClipToBounds = true;
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer?.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        Sound.Dispose();
        Input.Dispose();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Input.Clear();
    }

    /// <summary>One frame: read the controls, run the game, redraw.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        _clock += dt;
        Input.Update();
        HandlePad(dt);
        Engine.Update(dt, Input.Player, Input.Start);
        InvalidateVisual();
    }

    /// <summary>For tests: moves the drawing clock (turning rotors, blinking words) without running the game.</summary>
    public void Advance(double dt)
    {
        _clock += dt;
        InvalidateVisual();
    }

    // ───────────────────────────── the gamepad ─────────────────────────────

    private void HandlePad(double dt)
    {
        bool Edge(bool now, ref bool prev) { bool e = now && !prev; prev = now; return e; }
        var g = Engine;
        if (Edge(Input.PadPause, ref _padPausePrev))
        {
            if (g.State == GameState.Playing) g.Pause();
            else if (g.State == GameState.Paused) g.Resume();
        }
        if (Edge(Input.PadBack, ref _padBackPrev)) PressKey(Key.Escape);
        if (g.State == GameState.EnterInitials && g.Initials is { } entry)
        {
            if (Edge(Input.PadUp, ref _padUpPrev)) entry.Cycle(1);
            if (Edge(Input.PadDown, ref _padDownPrev)) entry.Cycle(-1);
            if (Edge(Input.PadLeft, ref _padLeftPrev)) entry.Back();
            if (Edge(Input.PadRight, ref _padRightPrev)) entry.Move(1);
            if (Edge(Input.PadA, ref _padAPrev)) entry.Advance();
            return;
        }
        // The left stick moves the gamepad's cursor; A clicks with it, B right-clicks, X stops, Y arms an attack.
        var (sx, sy) = Input.PadStick;
        if (sx != 0 || sy != 0)
        {
            _padActive = true;
            double speed = 760 * dt;
            _padCursor = new Point(Math.Clamp(_padCursor.X + sx * Math.Abs(sx) * speed, 0, ScreenWidth - 1), Math.Clamp(_padCursor.Y + sy * Math.Abs(sy) * speed, 0, ScreenHeight - 1));
            Input.PointAt((_padCursor.X, _padCursor.Y));
            _mouse = _padCursor;
        }
        if (Edge(Input.PadA, ref _padAPrev) && _padActive) { LeftDown(_padCursor, false); LeftUp(_padCursor); }
        if (Edge(Input.PadB, ref _padBPrev) && _padActive) RightDown(_padCursor);
        if (Edge(Input.PadX, ref _padXPrev)) g.Stop();
        if (Edge(Input.PadY, ref _padYPrev)) g.ArmAttack();
    }

    // ───────────────────────────── keys ─────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var g = Engine;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.S && ctrl)
        {
            SaveScreenshot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"AVADune2_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
            return;
        }
        // While entering initials every key belongs to the entry.
        if (g.State == GameState.EnterInitials && g.Initials is { } entry)
        {
            InitialsKey(entry, e.Key);
            return;
        }
        PressKey(e.Key, e.PhysicalKey, ctrl);
    }

    /// <summary>A key pressed (for tests too): the keys that do one thing once, then the held keys.</summary>
    public void PressKey(Key key, PhysicalKey physical = PhysicalKey.None, bool ctrl = false)
    {
        var g = Engine;
        ctrl |= Input.Ctrl;
        if (key == Key.M) { Sound.IsMuted = !Sound.IsMuted; return; }
        switch (g.State)
        {
            case GameState.Title:
                if (key == Key.K) { g.NewSkirmish(); return; }
                if (key == Key.C) { g.Continue(); return; }
                if (key == Key.Escape) { (TopLevel.GetTopLevel(this) as Window)?.Close(); return; }
                break;
            case GameState.Attract or GameState.HighScores:
                if (key == Key.Escape || key == Key.Space) { g.ReturnToTitle(); return; }
                break;
            case GameState.Houses:
                if (key is Key.Left) g.HouseIndex = (g.HouseIndex + 2) % 3;
                else if (key is Key.Right) g.HouseIndex = (g.HouseIndex + 1) % 3;
                else if (key is Key.Up or Key.Down) g.Opponents = 3 - g.Opponents;
                else if (key is Key.D1 or Key.D2 or Key.D3) g.ChooseHouse((Faction)(key - Key.D1));
                else if (key == Key.Escape) g.ReturnToTitle();
                break;
            case GameState.Briefing:
                if (key is Key.Space) g.BeginMission();
                else if (key == Key.Escape) g.ReturnToTitle();
                break;
            case GameState.Region:
                int n = g.Campaign?.Choices().Count ?? 1;
                if (key is Key.Left) g.RegionIndex = (g.RegionIndex + n - 1) % n;
                else if (key is Key.Right) g.RegionIndex = (g.RegionIndex + 1) % n;
                else if (key is Key.Space) g.ChooseTerritory();
                else if (key == Key.Q) g.Retire();
                else if (key == Key.Escape) g.ReturnToTitle();
                break;
            case GameState.Debrief:
                if (key is Key.Space or Key.Escape) g.FinishDebrief();
                break;
            case GameState.Paused:
                if (key is Key.P or Key.Pause or Key.Escape) g.Resume();
                else if (key == Key.Q) g.ReturnToTitle();
                return;
            case GameState.Playing:
                if (BattleKey(key, ctrl)) return;
                break;
        }
        Input.OnKeyDown(key, physical);
    }

    /// <summary>The keys of battle. True if the key was used up.</summary>
    private bool BattleKey(Key key, bool ctrl)
    {
        var g = Engine;
        switch (key)
        {
            case Key.Escape:
                if (!g.Disarm()) g.Pause();
                return true;
            case Key.P or Key.Pause: g.Pause(); return true;
            case Key.A: g.ArmAttack(); return true;
            case Key.S: g.Stop(); return true;
            case Key.G: g.Guard(); return true;
            case Key.D: g.Deploy(); return true;
            case Key.R: g.ToggleRepair(); return true;
            case Key.L: g.LaunchPalace(); return true;
            case Key.Space: g.JumpToAlarm(); return true;
            case Key.H:
                if (g.World.BuildingsOf(World.Player).FirstOrDefault() is { } home) g.CenterOn(home.CX, home.CY);
                return true;
            case Key.OemPlus or Key.Add: g.Faster(); return true;
            case Key.OemMinus or Key.Subtract: g.Slower(); return true;
            case >= Key.D1 and <= Key.D9:
                g.Group(key - Key.D0, ctrl);
                return true;
        }
        return false;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.OnKeyUp(e.Key, e.PhysicalKey);
        e.Handled = true;
    }

    private static void InitialsKey(InitialsEntry entry, Key key)
    {
        switch (key)
        {
            case Key.Up: entry.Cycle(1); break;
            case Key.Down: entry.Cycle(-1); break;
            case Key.Left or Key.Back: entry.Back(); break;
            case Key.Right: entry.Move(1); break;
            case Key.Space: entry.Advance(); break;
            case Key.Enter: entry.Finish(); break;
            case Key.OemPeriod or Key.Decimal: entry.Type('.'); break;
            case >= Key.A and <= Key.Z: entry.Type((char)('A' + (key - Key.A))); break;
            case >= Key.D0 and <= Key.D9: entry.Type((char)('0' + (key - Key.D0))); break;
            case >= Key.NumPad0 and <= Key.NumPad9: entry.Type((char)('0' + (key - Key.NumPad0))); break;
        }
    }

    // ───────────────────────────── the mouse ─────────────────────────────

    /// <summary>A point in the window to the fixed screen.</summary>
    private Point ToScreen(Point p)
    {
        var (s, ox, oy) = Fit();
        return s <= 0 ? new Point(-1, -1) : new Point((p.X - ox) / s, (p.Y - oy) / s);
    }

    private static bool InMap(Point p) => p.X >= 0 && p.X < Field.SideX && p.Y >= Field.TopBar && p.Y < ScreenHeight;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _mouse = ToScreen(e.GetPosition(this));
        _padActive = false;
        Input.PointAt((_mouse.X, _mouse.Y));
        if (_radarDrag && RadarRect.Contains(_mouse)) RadarLook(_mouse);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var at = ToScreen(e.GetPosition(this));
        _mouse = at;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed) RightDown(at);
        else if (props.IsLeftButtonPressed) LeftDown(at, e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Input.Shift);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        LeftUp(ToScreen(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Input.PointAt(null);
        _mouse = new Point(-1, -1);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Engine.State != GameState.Playing) return;
        _menuScroll = Math.Max(0, _menuScroll - Math.Sign(e.Delta.Y));
    }

    private bool _shiftDrag;

    /// <summary>The left button (or the gamepad's A) down at a point of the screen.</summary>
    public void LeftDown(Point at, bool shift)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: TitleClick(at); return;
            case GameState.Houses: HousesClick(at); return;
            case GameState.Briefing: g.BeginMission(); return;
            case GameState.Region: RegionClick(at); return;
            case GameState.Debrief: g.FinishDebrief(); return;
            case GameState.Attract or GameState.HighScores: g.ReturnToTitle(); return;
            case GameState.Paused: g.Resume(); return;
            case GameState.Playing: break;
            default: return;
        }
        if (SidebarClick(at, right: false)) return;
        if (!InMap(at)) return;
        var (x, y) = g.ToMap(at.X, at.Y);
        if (g.Placing || g.ArmedAttack || g.ArmedPalace) { g.SelectAt(x, y); return; }
        _dragFrom = at;
        _shiftDrag = shift;
    }

    /// <summary>The left button up: a click picks out what's there (twice quickly: all of its kind on screen); a drag
    /// picks out the units in the box.</summary>
    public void LeftUp(Point at)
    {
        _radarDrag = false;
        if (_dragFrom is not { } from) return;
        _dragFrom = null;
        var g = Engine;
        if (g.State != GameState.Playing) return;
        var (x0, y0) = g.ToMap(from.X, from.Y);
        if (Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) > 8)
        {
            var (x1, y1) = g.ToMap(Math.Clamp(at.X, 0, Field.SideX), Math.Clamp(at.Y, Field.TopBar, ScreenHeight));
            g.SelectBox(x0, y0, x1, y1, _shiftDrag);
            return;
        }
        var now = DateTime.Now;
        bool twice = (now - _lastClick).TotalMilliseconds < 350 && Math.Abs(at.X - _lastClickAt.X) + Math.Abs(at.Y - _lastClickAt.Y) < 10;
        _lastClick = now;
        _lastClickAt = at;
        if (twice) g.SelectSame(x0, y0);
        else g.SelectAt(x0, y0, _shiftDrag);
    }

    /// <summary>The right button (or the gamepad's B): orders on the map or the radar, or a cancel in the sidebar.</summary>
    public void RightDown(Point at)
    {
        var g = Engine;
        if (g.State != GameState.Playing) return;
        if (RadarRect.Contains(at) && g.World.PlayerHouse.Radar)
        {
            var (rx, ry) = RadarToMap(at);
            g.OrderAt(rx, ry);
            return;
        }
        if (SidebarClick(at, right: true)) return;
        if (!InMap(at)) return;
        var (x, y) = g.ToMap(at.X, at.Y);
        g.OrderAt(x, y);
    }

    private void RadarLook(Point at)
    {
        var (x, y) = RadarToMap(at);
        Engine.CenterOn(x, y);
    }

    private static (double X, double Y) RadarToMap(Point at) =>
        ((at.X - RadarRect.X) / RadarRect.Width * Field.N, (at.Y - RadarRect.Y) / RadarRect.Height * Field.N);

    /// <summary>Saves what's on screen as a PNG.</summary>
    public void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>How the fixed screen fits the window: scale and letterbox offsets.</summary>
    private (double Scale, double X, double Y) Fit()
    {
        double s = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (s, (Bounds.Width - ScreenWidth * s) / 2, (Bounds.Height - ScreenHeight * s) / 2);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var (s, ox, oy) = Fit();
        if (s <= 0) return;
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
            DrawScreen(dc);
    }
}
