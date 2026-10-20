namespace AVADune2.Engine;

public enum GameState
{
    /// <summary>The title, with its tune.</summary>
    Title,
    /// <summary>The demonstration: the computer plays a skirmish of its own.</summary>
    Attract,
    /// <summary>Choosing a House (for the campaign or a skirmish).</summary>
    Houses,
    /// <summary>The advisor's briefing before a mission.</summary>
    Briefing,
    Playing,
    Paused,
    /// <summary>The score screen after a mission.</summary>
    Debrief,
    /// <summary>The region map: choosing the next territory to attack.</summary>
    Region,
    /// <summary>The campaign is over: a moment, then the initials or the table.</summary>
    GameOver,
    EnterInitials,
    HighScores,
}

public enum Phase
{
    Play,
    /// <summary>The mission won: a moment, then the score.</summary>
    Won,
    /// <summary>The mission lost.</summary>
    Lost,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Click, Refused, Place, Gun, Cannon, Rocket, Boom, BigBoom, Sonic, Deviate, Harvest, Credits, Rumble, Swallow,
    Alarm, Bloom, Crush, Frigate, Launch, Lift,
    SayBuilt, SayUnitReady, SayAttacked, SayReady, SayMoving, SayAttack, SayLowPower, SayStorage, SayCreature, SayMissile,
    SayVictory, SayDefeat,
    Music, MusicStop, TitleTune, Victory, GameOver,
}

/// <summary>
/// The whole game with no UI: the title, choosing a House, the briefings, the battles (the camera, selecting and
/// ordering units, building, placing), the score after each, the region map, saving, the end of the campaign and the
/// high scores. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed 60 Hz steps (more or
/// fewer of them at the faster and slower game speeds), so it plays the same at any frame rate. The mouse and the
/// keys come in through <see cref="SelectAt"/>, <see cref="OrderAt"/>, <see cref="Build"/> and the rest, in map tiles.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 20, AttractSeconds = 100, HighScoreSeconds = 10;
    public const double WonSeconds = 4, LostSeconds = 4, OverSeconds = 6;
    /// <summary>How fast the map scrolls (tiles a second), and how near the edge the mouse scrolls it (pixels).</summary>
    public const double ScrollSpeed = 16, EdgeScroll = 6;
    public static readonly double[] Speeds = [0.5, 0.75, 1, 1.5, 2];
    public static readonly string[] SpeedNames = ["SLOWEST", "SLOW", "NORMAL", "FAST", "FASTEST"];

    private double _accumulator, _idle, _watchAt, _shownTick;
    private bool _startPrev, _tunePending = true, _music;
    private readonly Dictionary<Sfx, double> _lastSound = [];
    private int _seed;

    public GameEngine(HighScoreTable? table = null, string? saveFolder = null, int seed = 0)
    {
        HighScores = table ?? HighScoreTable.Load();
        SaveFolder = saveFolder ?? HighScores.Folder;
        _seed = seed != 0 ? seed : (int)(DateTime.Now.Ticks % 1_000_000) + 1;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Play;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public string SaveFolder { get; }
    public World World { get; private set; } = new();
    public Scenario? Scenario { get; private set; }
    public Campaign? Campaign { get; private set; }
    public Debrief? Debrief { get; private set; }
    public bool Skirmish { get; private set; }
    /// <summary>How many computer Houses a skirmish is against (1 or 2).</summary>
    public int Opponents { get; set; } = 1;
    /// <summary>The House highlighted on the choice screen, and the territory on the region map.</summary>
    public int HouseIndex { get; set; }
    public int RegionIndex { get; set; }
    public int SpeedIndex { get; set; } = 2;
    public double Speed => Speeds[SpeedIndex];
    /// <summary>The player's side played by the computer (the demonstration, or a test).</summary>
    public bool Autopilot { get; set; }
    public int Score => Campaign?.Score ?? 0;
    public bool HasSave => SaveFile.Exists(SaveFolder);

    /// <summary>The map's top-left corner on screen, in tiles.</summary>
    public double CamX { get; private set; }
    public double CamY { get; private set; }
    /// <summary>The units picked out, or a building.</summary>
    public List<Unit> Selected { get; } = [];
    public Building? SelectedBuilding { get; private set; }
    /// <summary>An enemy (or anything not the player's) picked out to look at.</summary>
    public Thing? Inspected { get; private set; }
    public Dictionary<int, List<Unit>> Groups { get; } = [];
    /// <summary>Putting down the building the yard has ready.</summary>
    public bool Placing { get; private set; }
    /// <summary>A (A) pressed: the next click attacks there. The ruin missile waiting for its target.</summary>
    public bool ArmedAttack { get; private set; }
    public bool ArmedPalace { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>The credits shown, counting up and down to the real figure.</summary>
    public double ShownCredits { get; private set; }

    private void Play(Sfx s)
    {
        if (Silent) return;
        double gap = s >= Sfx.SayBuilt && s <= Sfx.SayDefeat ? 1.2 : s is Sfx.Gun ? 0.06 : s is Sfx.Credits ? 0.09 : 0.05;
        if (s < Sfx.Music && _lastSound.TryGetValue(s, out var at) && StateTime - at < gap && StateTime >= at) return;
        _lastSound[s] = StateTime;
        Sound?.Invoke(s);
    }

    public void Say(string text, double seconds = 4)
    {
        Message = text;
        MessageTime = seconds;
    }

    // ───────────────────────────── the loop ─────────────────────────────

    public void Update(double dt, Controls c, bool start)
    {
        double scale = State == GameState.Playing ? Speed : 1;
        _accumulator += Math.Min(dt, 0.25) * scale;
        bool first = true;
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            // The camera scrolls in real time, whatever the game speed.
            Tick(c, start, scroll: first ? dt : 0);
            first = false;
        }
        if (first && State == GameState.Playing) Scroll(c, dt);
    }

    public void Tick(Controls c = default, bool start = false, double scroll = Field.Step)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed) NewCampaign();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate();
                Watch();
                if (startPressed) { ReturnToTitle(); break; }
                if (State == GameState.Attract && (StateTime > AttractSeconds || World.Outcome >= 0)) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Houses:
                if (startPressed) ChooseHouse((Faction)HouseIndex);
                break;
            case GameState.Briefing:
                if (startPressed) BeginMission();
                break;
            case GameState.Playing:
                if (scroll > 0) Scroll(c, scroll);
                Simulate();
                break;
            case GameState.Debrief:
                if (startPressed && StateTime > 0.5) FinishDebrief();
                break;
            case GameState.Region:
                if (startPressed) ChooseTerritory();
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Math.Min(Campaign.Missions, Campaign?.Mission ?? 1));
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) ReturnToTitle();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void SetState(GameState s)
    {
        State = s;
        StateTime = 0;
        _lastSound.Clear();
    }

    private void Simulate()
    {
        var w = World;
        PhaseTime += Field.Step;
        if (Phase == Phase.Play)
        {
            Sim.Step(w);
            if (w.Outcome == 0 || w.Outcome == 1) EndMission(w.Outcome == 0);
        }
        else if (State == GameState.Playing)
        {
            // The battle goes on quietly behind the word.
            Sim.Step(w);
            if (PhaseTime > (Phase == Phase.Won ? WonSeconds : LostSeconds)) ShowDebrief();
        }
        Hear(w);
        foreach (var n in w.News) Say(n);
        w.News.Clear();
        Selected.RemoveAll(u => u.Dead || u.Owner != World.Player);
        if (SelectedBuilding is { Dead: true }) SelectedBuilding = null;
        if (Inspected is { Dead: true }) Inspected = null;
        if (Placing && YardReady() is null) Placing = false;
        // The credits counter ticks toward the real figure.
        double real = w.Houses.Count > 0 ? w.PlayerHouse.Credits : 0, step = Math.Max(4, Math.Abs(real - ShownCredits) * 0.08);
        double before = ShownCredits;
        ShownCredits = Math.Abs(real - ShownCredits) <= step ? real : ShownCredits + Math.Sign(real - ShownCredits) * step;
        if (ShownCredits > before + 0.5 && StateTime - _shownTick > 0.1) { _shownTick = StateTime; Play(Sfx.Credits); }
    }

    /// <summary>The world's sounds: those on screen (or everywhere) are played.</summary>
    private void Hear(World w)
    {
        foreach (var (s, x, y) in w.Heard)
        {
            if (State == GameState.Attract && s >= Sfx.SayBuilt && s <= Sfx.SayDefeat) continue;
            if (x < 0 || x >= CamX - 2 && x <= CamX + Field.ViewW + 2 && y >= CamY - 2 && y <= CamY + Field.ViewH + 2) Play(s);
        }
        w.Heard.Clear();
    }

    // ───────────────────────────── the camera ─────────────────────────────

    public void CenterOn(double x, double y)
    {
        CamX = Math.Clamp(x - Field.ViewW / 2, 0, Field.N - Field.ViewW);
        CamY = Math.Clamp(y - Field.ViewH / 2, 0, Field.N - Field.ViewH);
    }

    /// <summary>The arrows and the stick scroll the map; so does the mouse at the screen's edge. Up is -1.</summary>
    private void Scroll(Controls c, double dt)
    {
        int dx = c.Dx, dy = c.Dy;
        if (c.Point is var (px, py))
        {
            if (px <= EdgeScroll) dx = -1;
            else if (px >= Field.Width - EdgeScroll) dx = 1;
            if (py <= EdgeScroll) dy = -1;
            else if (py >= Field.Height - EdgeScroll) dy = 1;
        }
        if (dx == 0 && dy == 0) return;
        CamX = Math.Clamp(CamX + dx * ScrollSpeed * dt, 0, Field.N - Field.ViewW);
        CamY = Math.Clamp(CamY + dy * ScrollSpeed * dt, 0, Field.N - Field.ViewH);
    }

    /// <summary>In the demonstration the camera drifts to wherever the most is happening.</summary>
    private void Watch()
    {
        if (StateTime >= _watchAt)
        {
            _watchAt = StateTime + 5;
            _watch = Pilot.Watch(World);
        }
        double tx = Math.Clamp(_watch.X - Field.ViewW / 2, 0, Field.N - Field.ViewW), ty = Math.Clamp(_watch.Y - Field.ViewH / 2, 0, Field.N - Field.ViewH);
        CamX += (tx - CamX) * 0.03;
        CamY += (ty - CamY) * 0.03;
    }
    private (double X, double Y) _watch = (Field.N / 2.0, Field.N / 2.0);

    /// <summary>A point on the screen as a point on the map (in tiles).</summary>
    public (double X, double Y) ToMap(double sx, double sy) => (CamX + sx / Field.Tile, CamY + (sy - Field.TopBar) / Field.Tile);

    // ───────────────────────────── the flow of the game ─────────────────────────────

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    public void NewCampaign()
    {
        Skirmish = false;
        SetState(GameState.Houses);
        Music(false);
    }

    public void NewSkirmish()
    {
        Skirmish = true;
        SetState(GameState.Houses);
        Music(false);
    }

    /// <summary>A House chosen: the campaign begins at the region map; a skirmish begins at once.</summary>
    public void ChooseHouse(Faction f)
    {
        if (f == Faction.Sovereign) return;
        HouseIndex = (int)f;
        Play(Sfx.Click);
        if (Skirmish)
        {
            var (l, r) = Campaign.Rivals(f);
            Faction[] enemies = Opponents >= 2 ? [l, r] : [_seed % 2 == 0 ? l : r];
            Scenario = new Scenario(_seed++, 9, f, enemies, Goal.Destroy, 0, 9, 3000, 1, Creatures: 1, Skirmish: true);
            StartMission(Scenario);
            return;
        }
        Campaign = new Campaign(f, _seed++);
        ShowRegion();
    }

    /// <summary>Carries on a saved campaign at its region map.</summary>
    public bool Continue()
    {
        var c = SaveFile.Load(SaveFolder);
        if (c is null || c.Over) return false;
        Campaign = c;
        Skirmish = false;
        HouseIndex = (int)c.House;
        ShowRegion();
        return true;
    }

    private void ShowRegion()
    {
        SetState(GameState.Region);
        RegionIndex = 0;
        if (Campaign is { } c) SaveFile.Save(c, SaveFolder);
    }

    /// <summary>The territory highlighted (or clicked) on the region map is the next to attack.</summary>
    public void ChooseTerritory(int? id = null)
    {
        if (Campaign is not { } c) return;
        var choices = c.Choices();
        if (choices.Count == 0) return;
        int t = id is { } i && choices.Contains(i) ? i : choices[Math.Clamp(RegionIndex, 0, choices.Count - 1)];
        if (id is not null && !choices.Contains(id.Value)) return;
        c.Target = t;
        Scenario = c.ScenarioFor(t);
        Play(Sfx.Click);
        SetState(GameState.Briefing);
    }

    /// <summary>From the briefing into the battle.</summary>
    public void BeginMission()
    {
        if (Scenario is null) return;
        StartMission(Scenario);
    }

    public void StartMission(Scenario s)
    {
        Scenario = s;
        World = MapGen.Setup(s);
        Selected.Clear();
        SelectedBuilding = null;
        Inspected = null;
        Groups.Clear();
        Placing = ArmedAttack = ArmedPalace = false;
        Message = null;
        ShownCredits = World.PlayerHouse.Credits;
        var yard = World.BuildingsOf(World.Player).FirstOrDefault();
        if (yard is not null) CenterOn(yard.CX, yard.CY + 3);
        if (Autopilot) Pilot.TakeOver(World);
        SetState(GameState.Playing);
        Phase = Phase.Play;
        PhaseTime = 0;
        Music(true);
        Say(s.Goal == Goal.Harvest ? $"HARVEST {s.GoalCredits} CREDITS" : "DESTROY THE ENEMY", 5);
    }

    private void EndMission(bool won)
    {
        Phase = won ? Phase.Won : Phase.Lost;
        PhaseTime = 0;
        Placing = ArmedAttack = ArmedPalace = false;
        Music(false);
        Debrief = Debrief.Of(World, Scenario?.Mission ?? 1, won);
        if (State == GameState.Attract) return;
        Play(won ? Sfx.Victory : Sfx.GameOver);
        Play(won ? Sfx.SayVictory : Sfx.SayDefeat);
        Say(won ? "MISSION ACCOMPLISHED" : "MISSION FAILED", 6);
    }

    private void ShowDebrief()
    {
        SetState(GameState.Debrief);
        Message = null;
    }

    /// <summary>From the score screen: on to the region map (or the end of the campaign); a skirmish goes back to the title.</summary>
    public void FinishDebrief()
    {
        if (State != GameState.Debrief || Debrief is not { } d) return;
        Play(Sfx.Click);
        if (Skirmish || Campaign is not { } c) { ReturnToTitle(); return; }
        if (d.Won) c.Win(c.Target >= 0 ? c.Target : c.Choices().FirstOrDefault(), d.Points);
        else c.Lose(d.Points);
        if (c.Over)
        {
            SaveFile.Delete(SaveFolder);
            SetState(GameState.GameOver);
            Play(c.Won ? Sfx.Victory : Sfx.GameOver);
            return;
        }
        ShowRegion();
    }

    /// <summary>Gives up the campaign from the region map: it ends here, and the score goes to the table.</summary>
    public void Retire()
    {
        if (State != GameState.Region || Campaign is null) return;
        SaveFile.Delete(SaveFolder);
        Campaign.Defeats = Campaign.MaxDefeats;
        SetState(GameState.GameOver);
    }

    private void StartAttract()
    {
        var d = new Dice(_seed++);
        var f = (Faction)d.Next(3);
        var (l, r) = Campaign.Rivals(f);
        Scenario = new Scenario(_seed++, 9, f, [d.Next(2) == 0 ? l : r], Goal.Destroy, 0, 9, 2500, 3, PlayerBase: 3, Creatures: 2, Skirmish: true);
        World = MapGen.Setup(Scenario);
        World.RevealAll = true;
        Pilot.TakeOver(World);
        foreach (var h in World.Houses.Skip(1)) if (h.Brain is { } b) { b.FirstWave = 25; b.WaveEvery = 30; b.WaveSize = 3; }
        World.PlayerHouse.Brain!.FirstWave = 20;
        World.PlayerHouse.Brain!.WaveEvery = 30;
        World.PlayerHouse.Brain!.WaveSize = 4;
        Selected.Clear();
        SelectedBuilding = null;
        Phase = Phase.Play;
        PhaseTime = 0;
        _watchAt = 0;
        var yard = World.BuildingsOf(World.Player).FirstOrDefault();
        if (yard is not null) CenterOn(yard.CX, yard.CY);
        SetState(GameState.Attract);
        Music(true);
    }

    public void ReturnToTitle()
    {
        SetState(GameState.Title);
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        Campaign = null;
        World = new World();
    }

    private void ShowHighScores(int place)
    {
        SetState(GameState.HighScores);
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    public void Faster() => SpeedIndex = Math.Min(Speeds.Length - 1, SpeedIndex + 1);
    public void Slower() => SpeedIndex = Math.Max(0, SpeedIndex - 1);

    // ───────────────────────────── picking things out ─────────────────────────────

    private bool Mine(Thing t) => t.Owner == World.Player;

    /// <summary>What's at a point of the map that the player can see: a unit (in the air first), else a building.</summary>
    public Thing? ThingAt(double x, double y)
    {
        var w = World;
        if (!Sim.Visible(w, x, y)) return null;
        Thing? best = null;
        double bestD = 0.75;
        foreach (var u in w.Units)
        {
            if (u.Dead || u.CarriedBy is not null) continue;
            double d = Field.Dist(u.X, u.Y, x, y) - (u.InAir ? 0.2 : 0);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best ?? w.BuildingAt((int)Math.Floor(x), (int)Math.Floor(y));
    }

    /// <summary>A left click on the map: pick out what's there (with Shift, add a unit to those picked out).</summary>
    public void SelectAt(double x, double y, bool add = false)
    {
        if (State != GameState.Playing) return;
        if (Placing) { PlaceAt(x, y); return; }
        if (ArmedAttack || ArmedPalace) { OrderAt(x, y); return; }
        var t = ThingAt(x, y);
        if (!add) Clear();
        switch (t)
        {
            case Unit u when Mine(u):
                if (add && Selected.Contains(u)) Selected.Remove(u);
                else if (!Selected.Contains(u)) Selected.Add(u);
                SelectedBuilding = null;
                Play(Sfx.SayReady);
                break;
            case Building b when Mine(b) && !add:
                SelectedBuilding = b;
                Play(Sfx.Click);
                break;
            case { } other when !add:
                Inspected = other;
                break;
        }
    }

    /// <summary>A double click: every unit of the player's of the same kind on screen.</summary>
    public void SelectSame(double x, double y)
    {
        if (ThingAt(x, y) is not Unit u || !Mine(u)) return;
        Clear();
        foreach (var o in World.UnitsOf(World.Player))
            if (o.Type == u.Type && o.X >= CamX && o.X <= CamX + Field.ViewW && o.Y >= CamY && o.Y <= CamY + Field.ViewH) Selected.Add(o);
        Play(Sfx.SayReady);
    }

    /// <summary>A box dragged on the map: the player's units in it (fighters before harvesters, if there are both).</summary>
    public void SelectBox(double x0, double y0, double x1, double y1, bool add = false)
    {
        if (State != GameState.Playing || Placing) return;
        if (!add) Clear();
        double ax = Math.Min(x0, x1), bx = Math.Max(x0, x1), ay = Math.Min(y0, y1), by = Math.Max(y0, y1);
        var inBox = World.UnitsOf(World.Player).Where(u => u.CarriedBy is null && u.X >= ax && u.X <= bx && u.Y >= ay && u.Y <= by).ToList();
        if (inBox.Any(u => Rules.Fighter(u.Type))) inBox.RemoveAll(u => !Rules.Fighter(u.Type) && u.Type != Kind.Saboteur);
        foreach (var u in inBox)
            if (!Selected.Contains(u)) Selected.Add(u);
        if (inBox.Count > 0) { SelectedBuilding = null; Play(Sfx.SayReady); }
    }

    public void Clear()
    {
        Selected.Clear();
        SelectedBuilding = null;
        Inspected = null;
    }

    // ───────────────────────────── orders ─────────────────────────────

    private List<Unit> Commandable => Selected.Where(u => !u.Dead && u.Owner == World.Player && u.OnGround).ToList();

    /// <summary>
    /// A right click on the map (or a left click with attack armed): the units picked out attack an enemy there,
    /// harvest solarite, take a load home to a refinery, go to the repair pad, or move (attack-move if armed). With a
    /// factory picked out, it's where the factory's units will go. With the ruin missile armed, it's fired there.
    /// </summary>
    public void OrderAt(double x, double y)
    {
        if (State != GameState.Playing || Phase != Phase.Play) return;
        var w = World;
        int tx = Math.Clamp((int)Math.Floor(x), 0, Field.N - 1), ty = Math.Clamp((int)Math.Floor(y), 0, Field.N - 1);
        if (Placing) { Placing = false; return; }
        if (ArmedPalace)
        {
            ArmedPalace = false;
            if (Sim.FirePalace(w, World.Player, tx, ty)) Play(Sfx.Click);
            return;
        }
        bool attack = ArmedAttack;
        ArmedAttack = false;
        var units = Commandable;
        if (units.Count == 0)
        {
            if (SelectedBuilding is { } b && Mine(b) && Rules.Makes.ContainsKey(b.Type) && b.Type != Kind.Yard)
            {
                b.RallyX = tx;
                b.RallyY = ty;
                Play(Sfx.Click);
            }
            return;
        }
        var t = ThingAt(x, y);
        if (t is not null && w.Enemies(World.Player, t.Owner))
        {
            foreach (var u in units) Sim.Attack(w, u, t);
            Play(Sfx.SayAttack);
            return;
        }
        if (t is Building { Type: Kind.Refinery } r && Mine(r) && units.Any(u => u.Type == Kind.Harvester))
        {
            foreach (var u in units.Where(u => u.Type == Kind.Harvester)) { Sim.Command(w, u, Order.Return, tx, ty); u.Dock = r; }
            Play(Sfx.SayMoving);
            return;
        }
        if (t is Building { Type: Kind.RepairPad } pad && Mine(pad))
        {
            foreach (var u in units.Where(u => !Rules.IsInfantry(u.Type))) Sim.Command(w, u, Order.Repair, tx, ty);
            Play(Sfx.SayMoving);
            return;
        }
        if (w.Ore[tx, ty] > 0 && units.All(u => u.Type == Kind.Harvester))
        {
            Sim.MoveGroup(w, units, Order.Move, tx, ty);
            Play(Sfx.SayMoving);
            return;
        }
        Sim.MoveGroup(w, units, attack ? Order.AttackMove : Order.Move, tx, ty);
        Play(attack ? Sfx.SayAttack : Sfx.SayMoving);
    }

    /// <summary>A: the next click (left or right) attacks there; the units shoot whatever they meet on the way.</summary>
    public void ArmAttack()
    {
        if (Commandable.Any(u => Rules.Armed(u.Type))) ArmedAttack = true;
    }

    /// <summary>Esc while something is armed or being placed: put it away.</summary>
    public bool Disarm()
    {
        if (!Placing && !ArmedAttack && !ArmedPalace) return false;
        Placing = ArmedAttack = ArmedPalace = false;
        return true;
    }

    /// <summary>S: stop where they are.</summary>
    public void Stop()
    {
        foreach (var u in Commandable) Sim.Command(World, u, Order.Idle, u.Tx, u.Ty);
    }

    /// <summary>G: guard where they are, going further after anything that comes near.</summary>
    public void Guard()
    {
        foreach (var u in Commandable) Sim.Command(World, u, Order.Guard, u.Tx, u.Ty);
        if (Selected.Count > 0) Play(Sfx.SayMoving);
    }

    /// <summary>D: a mobile yard unfolds; a juggernaut blows itself up.</summary>
    public void Deploy()
    {
        foreach (var u in Commandable)
        {
            if (u.Type == Kind.Mcv) Sim.Command(World, u, Order.Deploy, u.Tx, u.Ty);
            if (u.Type == Kind.Juggernaut) Sim.SelfDestruct(World, u);
        }
    }

    /// <summary>Ctrl+number: the units picked out become a group; the number alone picks the group out again (and a
    /// second time, looks at it).</summary>
    public void Group(int n, bool assign)
    {
        if (assign)
        {
            Groups[n] = [.. Selected];
            Play(Sfx.Click);
            return;
        }
        if (!Groups.TryGetValue(n, out var g)) return;
        g.RemoveAll(u => u.Dead || u.Owner != World.Player);
        if (g.Count == 0) return;
        bool again = Selected.Count == g.Count && g.All(Selected.Contains);
        Clear();
        Selected.AddRange(g);
        if (again) CenterOn(g.Average(u => u.X), g.Average(u => u.Y));
        else Play(Sfx.SayReady);
    }

    /// <summary>Space: look at where the base was last attacked (or at the base).</summary>
    public void JumpToAlarm()
    {
        var w = World;
        if (w.AlarmX >= 0) CenterOn(w.AlarmX, w.AlarmY);
        else if (w.BuildingsOf(World.Player).FirstOrDefault() is { } b) CenterOn(b.CX, b.CY);
    }

    // ───────────────────────────── building ─────────────────────────────

    /// <summary>The player's yard holding a finished building.</summary>
    public Building? YardReady() => World.Buildings.FirstOrDefault(b => !b.Dead && b.Owner == World.Player && b.Type == Kind.Yard && b.Ready is not null);

    /// <summary>The building whose menu the sidebar shows: the one picked out if it makes things (or is the
    /// starport), else the yard.</summary>
    public Building? Factory
    {
        get
        {
            if (SelectedBuilding is { Dead: false } b && Mine(b) && (Rules.Makes.ContainsKey(b.Type) || b.Type == Kind.Starport)) return b;
            return World.Buildings.FirstOrDefault(o => !o.Dead && o.Owner == World.Player && o.Type == Kind.Yard);
        }
    }

    /// <summary>What the sidebar offers: the factory's options, or the starport's wares.</summary>
    public List<Kind> Menu()
    {
        if (Factory is not { } f) return [];
        if (f.Type == Kind.Starport) return Rules.Market.Where(k => Rules.CanMake(World.PlayerHouse.Faction, k)).ToList();
        return Sim.Options(World, f).ToList();
    }

    /// <summary>A left click on a menu item: a finished building is taken up to be put down; anything else is added to
    /// the queue (or bought, at the starport).</summary>
    public bool Build(Kind k)
    {
        if (State != GameState.Playing || Factory is not { } f) return false;
        if (f.Type == Kind.Starport)
        {
            bool bought = Sim.Buy(World, World.Player, k);
            Play(bought ? Sfx.Click : Sfx.Refused);
            if (!bought) Say("NOT ENOUGH CREDITS");
            return bought;
        }
        if (f.Ready == k)
        {
            Placing = true;
            Play(Sfx.Click);
            return true;
        }
        if (f.Type == Kind.Yard && f.Ready is not null)
        {
            Say("PLACE THE FINISHED BUILDING FIRST");
            Play(Sfx.Refused);
            return false;
        }
        bool ok = Sim.Enqueue(World, f, k);
        Play(ok ? Sfx.Click : Sfx.Refused);
        return ok;
    }

    /// <summary>A right click on a menu item: one fewer in the queue, or a finished building given back.</summary>
    public bool Unbuild(Kind k)
    {
        if (State != GameState.Playing || Factory is not { } f) return false;
        bool ok = Sim.Cancel(World, f, k);
        if (ok) { Play(Sfx.Click); if (f.Ready is null) Placing = false; }
        return ok;
    }

    /// <summary>Where the top-left of the building being placed goes, for the mouse at a point (its middle under the mouse).</summary>
    public (int X, int Y) PlacementAt(double x, double y)
    {
        var k = YardReady()?.Ready ?? Kind.Slab;
        var st = Rules.Of(k);
        return ((int)Math.Floor(x - st.W / 2.0 + 0.5), (int)Math.Floor(y - st.H / 2.0 + 0.5));
    }

    /// <summary>Puts the ready building down with its middle at a point of the map.</summary>
    public bool PlaceAt(double x, double y)
    {
        if (YardReady() is not { } yard) { Placing = false; return false; }
        var (px, py) = PlacementAt(x, y);
        if (Sim.CanPlace(World, World.Player, yard.Ready!.Value, px, py) is { } why)
        {
            Say(why, 2.5);
            Play(Sfx.Refused);
            return false;
        }
        Sim.PlaceReady(World, yard, px, py);
        Placing = false;
        return true;
    }

    /// <summary>R: the building picked out is repaired (or stops being).</summary>
    public void ToggleRepair()
    {
        if (SelectedBuilding is { Dead: false } b && Mine(b) && b.Hp < b.MaxHp)
        {
            b.Repairing = !b.Repairing;
            Play(Sfx.Click);
        }
    }

    /// <summary>The palace's weapon: the ruin missile waits for a target; the others go at once.</summary>
    public void LaunchPalace()
    {
        var h = World.PlayerHouse;
        if (!h.PalaceReady || !World.Has(World.Player, Kind.Palace)) { Play(Sfx.Refused); return; }
        if (h.Faction == Faction.Vrakos) { ArmedPalace = true; Say("CHOOSE THE MISSILE'S TARGET", 3); return; }
        var target = World.Buildings.Where(b => !b.Dead && World.Enemies(World.Player, b.Owner)).OrderBy(b => b.Type == Kind.Yard ? 0 : 1).FirstOrDefault();
        Sim.FirePalace(World, World.Player, (int)(target?.CX ?? Field.N / 2), (int)(target?.CY ?? Field.N / 2));
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a mission of the campaign (as the House given), past the menus.</summary>
    public void Go(int mission = 1, Faction house = Faction.Aldaine, int territory = -1)
    {
        Skirmish = false;
        Campaign = new Campaign(house, 7) { Mission = mission };
        int t = territory >= 0 ? territory : Campaign.Choices()[0];
        Campaign.Target = t;
        StartMission(Campaign.ScenarioFor(t));
        Message = null;
    }
}
