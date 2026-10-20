namespace AVADune2.Engine;

/// <summary>
/// The battlefield's measures: a square of 64 by 64 tiles, seen from above, and the screen it's shown on: the map on
/// the left under the top bar, the sidebar (radar, orders and building) on the right.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int N = 64;
    /// <summary>A tile's size on screen.</summary>
    public const int Tile = 32;
    public const double TopBar = 32, SideX = 800;
    public const double ViewW = SideX / Tile, ViewH = (Height - TopBar) / Tile;

    public static bool In(int x, int y) => (uint)x < N && (uint)y < N;
    public static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
}

/// <summary>A small random number generator whose state can be kept.</summary>
public struct Dice
{
    public ulong State;
    public Dice(int seed) => State = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        State ^= State << 13;
        State ^= State >> 7;
        State ^= State << 17;
        return (State >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Range(double a, double b) => a + Next() * (b - a);
}

/// <summary>The ground: open sand, dunes (slow going), rock (the only ground to build on) and mountains (on foot only).</summary>
public enum Ground : byte { Sand, Dunes, Rock, Mountain }

/// <summary>
/// The controls held this step: scroll the map (up is -1, as on the screen and in the engine) and where the mouse
/// points, in screen pixels.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, (double X, double Y)? Point = null);

/// <summary>What a unit is about.</summary>
public enum Order { Idle, Move, AttackMove, Attack, Guard, Harvest, Return, Docked, Repair, Deploy, Sabotage }

/// <summary>A building or a unit: something with an owner and health that can be shot.</summary>
public abstract class Thing
{
    public int Id, Owner;
    public double Hp;
    public bool Dead;
    /// <summary>Seconds since it was last hit (for the flash and the alarms), and by whom.</summary>
    public double HitAgo = 99;
    public int HitBy = -1;
    public abstract Kind Kind { get; }
    public abstract double CX { get; }
    public abstract double CY { get; }
    public Stats Stats => Rules.Of(Kind);
    public double MaxHp => Stats.Hp;
}

public sealed class Unit : Thing
{
    public Kind Type;
    public override Kind Kind => Type;
    /// <summary>Where it is (in tiles; a tile's centre is at .5), the tile it stands on, and the tile it's moving into.</summary>
    public double X, Y;
    public int Tx, Ty, Nx = -1, Ny = -1;
    public override double CX => X;
    public override double CY => Y;
    /// <summary>Which way the hull and the turret face (radians, 0 = east, down the screen positive).</summary>
    public double Facing, Aim;
    public Order Order;
    /// <summary>Where it's going, and the way there (tiles packed as y * N + x) and how far along it is.</summary>
    public int Gx = -1, Gy = -1;
    public List<int>? Path;
    public int PathAt;
    public bool PathPending, AvoidUnits;
    public double Wait, Reload, Think, Retry;
    public int Blocked;
    public Thing? Target;
    /// <summary>Where a guard stands and returns to.</summary>
    public int HomeX = -1, HomeY = -1;
    /// <summary>A harvester's load, the field tile it's working, and the refinery it's going to (or docked at).</summary>
    public double Cargo, Gather;
    public Building? Dock;
    /// <summary>A skylifter's passenger; the skylifter carrying this unit.</summary>
    public Unit? Carrying, CarriedBy;
    /// <summary>Where a skylifter is taking its passenger.</summary>
    public int DropX = -1, DropY = -1;
    /// <summary>Turned by a beguiler: whose it really is, and for how long it's not.</summary>
    public int TrueOwner = -1;
    public double Turned;
    /// <summary>A pause between the two shots of a twin weapon.</summary>
    public int Salvo;
    public bool Moving;
    public bool InAir => Type == Kind.Skylifter;
    public bool OnGround => !InAir && CarriedBy is null;
}

public sealed class Building : Thing
{
    public Kind Type;
    public override Kind Kind => Type;
    public int X, Y, W, H;
    public override double CX => X + W / 2.0;
    public override double CY => Y + H / 2.0;
    /// <summary>What it's making: the queue (the first is in hand), and how much of the first is paid for (0 to 1).</summary>
    public List<Kind> Queue = [];
    public double Progress;
    /// <summary>A building finished at the yard, waiting to be put down.</summary>
    public Kind? Ready;
    public bool Repairing;
    /// <summary>Where its units go when they're made (-1: stay by the door).</summary>
    public int RallyX = -1, RallyY = -1;
    /// <summary>A turret's aim, reload and target.</summary>
    public double Aim = Math.PI / 2, Reload;
    public Thing? Target;
    /// <summary>The harvester unloading in a refinery.</summary>
    public Unit? Docked;
    /// <summary>The palace's power coming round (0 to 1).</summary>
    public double Charge;
    /// <summary>How much of its footing was concrete when it was put down (0 to 1).</summary>
    public double Footing = 1;
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + W && y < Y + H;
    /// <summary>How far a point is from the nearest edge of the building (0 inside).</summary>
    public double Reach(double px, double py)
    {
        double dx = Math.Max(Math.Max(X - px, 0), px - (X + W)), dy = Math.Max(Math.Max(Y - py, 0), py - (Y + H));
        return Math.Sqrt(dx * dx + dy * dy);
    }
    /// <summary>The tile in front of a refinery where a harvester docks, and the door a factory's units come out of.</summary>
    public (int X, int Y) Door => (X + W / 2, Y + H);
}

/// <summary>Something in flight: a shell, a rocket, the beguiler's gas or the ruin missile.</summary>
public sealed class Shot
{
    public double X, Y, Tx, Ty, Speed, Splash;
    public int Damage, Owner;
    public Weapon Weapon;
    public double Sx, Sy, Life;
    public bool Air;
}

/// <summary>A sandmaw: a giant creature that swims through the sand and swallows whatever moves on it.</summary>
public sealed class Creature
{
    public double X, Y, Dir, Hp = MaxHp;
    public const double MaxHp = 1500;
    public Unit? Prey;
    public int Eaten;
    /// <summary>Seconds deep under the sand before it comes back (0: about).</summary>
    public double Dormant, Think, Wx = -1, Wy = -1, Mouth, Seen;
    /// <summary>Close enough to its prey to show its back above the sand.</summary>
    public bool Surfaced;
}

public enum Fx { Boom, BigBoom, Flash, Tracer, Sonic, Gas, Swallow, Bloom, Smoke, Spark, Crush, Deliver, Turned }

/// <summary>Something to show for a moment: an explosion, a shot, a sound wave, a swallowing.</summary>
public struct Effect
{
    public Fx Kind;
    public double X, Y, X2, Y2, T, Life;
    public uint Colour;
}

/// <summary>One of the sides in a battle: its credits and storage, power, radar, and what it has done.</summary>
public sealed class House
{
    public int Index;
    public Faction Faction;
    /// <summary>Houses on the same team never fight.</summary>
    public int Team;
    public bool Ai;
    public double Credits;
    public int Capacity;
    public double PowerMade, PowerUsed;
    public bool Radar;
    public Brain? Brain;
    public int Tech = 9;
    /// <summary>The mission's tallies.</summary>
    public double Harvested;
    public int UnitsKilled, UnitsLost, BuildingsKilled, BuildingsLost;
    /// <summary>When the alarms last sounded.</summary>
    public double AttackAlarm = -99, PowerAlarm = -99, StorageAlarm = -99;
    /// <summary>Units bought at the starport, and when the frigate lands with them.</summary>
    public List<Kind> Order = [];
    public double Frigate = -1;
    /// <summary>When a replacement harvester is due (-1: none needed).</summary>
    public double Replace = -1;
    /// <summary>The palace's weapon is ready.</summary>
    public bool PalaceReady => PalaceCharge >= 1;
    public double PalaceCharge;
    public uint Colour => Rules.Colour(Faction);
    /// <summary>Full power or better: 1; short of power: what fraction there is.</summary>
    public double PowerRatio => PowerUsed <= 0 ? 1 : Math.Min(1, PowerMade / PowerUsed);
    public bool LowPower => PowerRatio < 1;
}

/// <summary>What a mission asks: harvest so many credits, or wipe out the enemy.</summary>
public enum Goal { Harvest, Destroy }

/// <summary>
/// Everything on the battlefield, kept together: the ground, the solarite and the blooms, the concrete, the houses,
/// their buildings and units, shots in flight, the sandmaws, and what the player has seen.
/// </summary>
public sealed class World
{
    public const int Player = 0;

    public Ground[,] Ground = new Ground[Field.N, Field.N];
    /// <summary>Solarite on a tile, in credits' worth (0: none).</summary>
    public int[,] Ore = new int[Field.N, Field.N];
    public bool[,] Bloom = new bool[Field.N, Field.N];
    /// <summary>Concrete on a tile: its owner + 1 (0: none).</summary>
    public int[,] Slab = new int[Field.N, Field.N];
    /// <summary>Scorch marks left by explosions and wrecks (how dark, 0 to 1).</summary>
    public float[,] Scorch = new float[Field.N, Field.N];
    /// <summary>The player's map: the tiles seen (the rest is under the shroud).</summary>
    public bool[,] Seen = new bool[Field.N, Field.N];
    /// <summary>The building on each tile, and the unit on (or moving into) each tile.</summary>
    public Building?[,] Bld = new Building?[Field.N, Field.N];
    public Unit?[,] Occ = new Unit?[Field.N, Field.N];

    public List<House> Houses = [];
    public List<Unit> Units = [];
    public List<Building> Buildings = [];
    public List<Shot> Shots = [];
    public List<Creature> Creatures = [];
    public List<Effect> Effects = [];
    public Queue<Unit> PathQueue = new();
    public PathFinder Paths = new();

    public double Clock;
    public long Ticks;
    public Dice Dice = new(1);
    public int NextId = 1;
    /// <summary>Bumped whenever the ground, the solarite or the concrete changes, so the map is redrawn.</summary>
    public int GroundVersion;

    public Goal Goal = Goal.Destroy;
    public int GoalCredits;
    /// <summary>-1 while it's going on; 0 when the player has won, 1 when the player has lost.</summary>
    public int Outcome = -1;
    /// <summary>The starport's prices today (a multiple of each unit's cost) and when they change.</summary>
    public double[] Prices = [];
    public double PricesAt;
    /// <summary>When the player is shown everything (the demonstration, or a test).</summary>
    public bool RevealAll;
    /// <summary>Where the last attack on the player was (for the jump-to key).</summary>
    public double AlarmX = -1, AlarmY = -1;

    /// <summary>The sounds made this step, and where (x below 0: everywhere).</summary>
    public List<(Sfx Sound, double X, double Y)> Heard = [];
    /// <summary>What the player is told this step.</summary>
    public List<string> News = [];

    public House PlayerHouse => Houses[Player];

    public bool Enemies(int a, int b) => a != b && Houses[a].Team != Houses[b].Team;

    public void Hear(Sfx s, double x = -1, double y = -1) => Heard.Add((s, x, y));

    public void Fx(Fx kind, double x, double y, double life, double x2 = 0, double y2 = 0, uint colour = 0) =>
        Effects.Add(new Effect { Kind = kind, X = x, Y = y, X2 = x2, Y2 = y2, Life = life, Colour = colour });

    public bool IsSandy(int x, int y) => Field.In(x, y) && Ground[x, y] is Engine.Ground.Sand or Engine.Ground.Dunes && Slab[x, y] == 0;

    public Unit? UnitAt(int x, int y) => Field.In(x, y) ? Occ[x, y] : null;
    public Building? BuildingAt(int x, int y) => Field.In(x, y) ? Bld[x, y] : null;

    public IEnumerable<Building> BuildingsOf(int owner) => Buildings.Where(b => !b.Dead && b.Owner == owner);
    public IEnumerable<Unit> UnitsOf(int owner) => Units.Where(u => !u.Dead && u.Owner == owner);
    public int Count(int owner, Kind k) => Rules.IsBuilding(k) ? Buildings.Count(b => !b.Dead && b.Owner == owner && b.Type == k) : Units.Count(u => !u.Dead && u.Owner == owner && u.Type == k);
    public bool Has(int owner, Kind k) => Buildings.Any(b => !b.Dead && b.Owner == owner && b.Type == k);
}
