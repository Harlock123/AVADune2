namespace AVADune2.Engine;

/// <summary>The great Houses at war on Vharos, and the Sovereign who rules them all.</summary>
public enum Faction { Aldaine, Vrakos, Sythe, Sovereign }

/// <summary>Everything that can be built, bought or called down: the buildings first, then the units.</summary>
public enum Kind
{
    Yard, Slab, Turbine, Refinery, Silo, Barracks, LightFactory, HeavyFactory, HighTech, Outpost, Turret, RocketTurret,
    Wall, RepairPad, Starport, Palace,
    Infantry, Troopers, Trike, Quad, Tank, RocketTank, SiegeTank, Harvester, Skylifter, Mcv, Resonator, Juggernaut,
    Beguiler, Nomads, Saboteur, Praetors,
}

/// <summary>What a weapon throws: bullets hit at once; shells, rockets and the ruin missile fly; the resonator's sound
/// wave hits everything in its line; the beguiler's gas turns vehicles to its side for a while.</summary>
public enum Weapon { None, Gun, Cannon, Rocket, Sonic, Deviator, Missile }

public enum Armour { Infantry, Light, Heavy, Air, Building }

/// <summary>How a unit gets about: on foot (over the mountains too), on wheels or tracks (sand, dunes and rock), or
/// in the air.</summary>
public enum Mover { None, Foot, Wheels, Tracks, Air }

/// <summary>A thing's numbers: what it costs and how long it takes to make (at full power), its health, its size and
/// power (buildings), its speed and its weapon (units), when it comes (the tech level) and what it needs first.</summary>
public sealed record Stats(
    string Name, int Cost, double Time, int Hp,
    int W = 1, int H = 1, int Power = 0, int Storage = 0,
    double Speed = 0, double Sight = 2.5, double Range = 0, double MinRange = 0, int Damage = 0, double Reload = 1,
    Weapon Weapon = Weapon.None, int Shots = 1, Armour Armour = Armour.Building, Mover Mover = Mover.None,
    int Tech = 1, Kind MadeBy = Kind.Yard, Kind[]? Requires = null, Faction? Only = null, bool Turret = false,
    string Blurb = "");

/// <summary>The numbers for everything, and the Houses' names, colours and advisors.</summary>
public static class Rules
{
    public const int NoTech = 99;
    /// <summary>A harvester's full load, and how fast it gathers and unloads (in credits a second).</summary>
    public const int HarvesterLoad = 700;
    public const double HarvestRate = 40, UnloadRate = 200;
    /// <summary>The refinery and every silo hold this many credits.</summary>
    public const int StorePerBuilding = 1000;
    /// <summary>A wind turbine's power at full health.</summary>
    public const int TurbinePower = 100;

    private static readonly Dictionary<Kind, Stats> Table = new()
    {
        // ───── buildings ─────
        [Kind.Yard] = new("CONSTRUCTION YARD", 900, 30, 1300, 2, 2, Sight: 4, Tech: NoTech, Storage: 0,
            Blurb: "BUILDS EVERY OTHER BUILDING. PROTECT IT AT ALL COSTS."),
        [Kind.Slab] = new("CONCRETE", 20, 1.5, 1, 2, 2, Tech: 1,
            Blurb: "A FOOTING OF CONCRETE. BUILDINGS ON IT STAND AT FULL STRENGTH."),
        [Kind.Turbine] = new("WIND TURBINE", 300, 8, 600, 2, 2, Power: Rules.TurbinePower, Sight: 3, Tech: 1,
            Blurb: "CATCHES THE DESERT WIND FOR POWER."),
        [Kind.Refinery] = new("REFINERY", 400, 12, 900, 3, 2, Power: -30, Storage: StorePerBuilding, Sight: 3, Tech: 1, Requires: [Kind.Turbine],
            Blurb: "TURNS SOLARITE INTO CREDITS. COMES WITH A HARVESTER."),
        [Kind.Silo] = new("SILO", 150, 5, 400, 2, 2, Power: -5, Storage: StorePerBuilding, Tech: 1, Requires: [Kind.Refinery],
            Blurb: "STORES A THOUSAND MORE CREDITS."),
        [Kind.Barracks] = new("BARRACKS", 300, 8, 600, 2, 2, Power: -10, Sight: 3, Tech: 2, Requires: [Kind.Turbine],
            Blurb: "TRAINS INFANTRY AND TROOPERS."),
        [Kind.LightFactory] = new("LIGHT FACTORY", 400, 10, 700, 2, 2, Power: -20, Sight: 3, Tech: 2, Requires: [Kind.Refinery],
            Blurb: "BUILDS TRIKES AND QUADS."),
        [Kind.Outpost] = new("RADAR OUTPOST", 400, 10, 700, 2, 2, Power: -30, Sight: 6, Tech: 2, Requires: [Kind.Turbine],
            Blurb: "THE RADAR MAP. NEEDS FULL POWER."),
        [Kind.Wall] = new("WALL", 50, 2, 500, Tech: 2, Requires: [Kind.Barracks],
            Blurb: "A BLOCK OF STONE IN THE ENEMY'S WAY."),
        [Kind.HeavyFactory] = new("HEAVY FACTORY", 600, 14, 900, 3, 2, Power: -35, Sight: 3, Tech: 3, Requires: [Kind.LightFactory, Kind.Outpost],
            Blurb: "BUILDS TANKS, HARVESTERS AND MOBILE YARDS."),
        [Kind.Turret] = new("GUN TURRET", 125, 5, 450, Power: -10, Sight: 5, Range: 5.5, Damage: 26, Reload: 1.2, Weapon: Weapon.Cannon,
            Turret: true, Tech: 3, Requires: [Kind.Outpost], Blurb: "A CANNON ON A PIVOT. NEEDS POWER."),
        [Kind.RocketTurret] = new("ROCKET TURRET", 250, 7, 500, Power: -15, Sight: 7, Range: 8, MinRange: 1.5, Damage: 36, Reload: 2.2,
            Weapon: Weapon.Rocket, Turret: true, Tech: 4, Requires: [Kind.Outpost], Blurb: "LONG-RANGE ROCKETS. HITS SKYLIFTERS TOO."),
        [Kind.RepairPad] = new("REPAIR PAD", 700, 12, 700, 3, 2, Power: -20, Tech: 4, Requires: [Kind.HeavyFactory],
            Blurb: "MENDS DAMAGED VEHICLES, FOR A PRICE."),
        [Kind.HighTech] = new("HIGH-TECH FACTORY", 500, 14, 800, 3, 2, Power: -35, Sight: 3, Tech: 5, Requires: [Kind.HeavyFactory],
            Blurb: "BUILDS SKYLIFTERS, WHICH FERRY HARVESTERS."),
        [Kind.Starport] = new("STARPORT", 500, 14, 1000, 3, 3, Power: -50, Sight: 3, Tech: 6, Requires: [Kind.HeavyFactory],
            Blurb: "BUYS UNITS FROM OFF-WORLD AT THE DAY'S PRICES."),
        [Kind.Palace] = new("PALACE", 1000, 20, 1600, 3, 3, Power: -80, Sight: 4, Tech: 8, Requires: [Kind.Starport],
            Blurb: "THE HOUSE'S SEAT, AND ITS SECRET WEAPON."),

        // ───── units ─────
        [Kind.Infantry] = new("INFANTRY", 100, 5, 90, Speed: 0.9, Sight: 3, Range: 2.5, Damage: 9, Reload: 0.6, Weapon: Weapon.Gun,
            Armour: Armour.Infantry, Mover: Mover.Foot, Tech: 2, MadeBy: Kind.Barracks, Blurb: "A SQUAD OF RIFLES. CHEAP, AND CROSSES MOUNTAINS."),
        [Kind.Troopers] = new("TROOPERS", 200, 7, 110, Speed: 0.8, Sight: 4, Range: 4.5, MinRange: 1, Damage: 22, Reload: 1.6, Weapon: Weapon.Rocket,
            Armour: Armour.Infantry, Mover: Mover.Foot, Tech: 3, MadeBy: Kind.Barracks, Requires: [Kind.Outpost], Blurb: "ROCKETS ON FOOT. GOOD AGAINST ARMOUR."),
        [Kind.Trike] = new("TRIKE", 150, 6, 100, Speed: 3.2, Sight: 4, Range: 3, Damage: 8, Reload: 0.35, Weapon: Weapon.Gun,
            Armour: Armour.Light, Mover: Mover.Wheels, Tech: 2, MadeBy: Kind.LightFactory, Blurb: "FAST AND LIGHT. A SCOUT AND A RAIDER."),
        [Kind.Quad] = new("QUAD", 200, 7, 140, Speed: 2.7, Sight: 4, Range: 3, Damage: 10, Reload: 0.35, Weapon: Weapon.Gun,
            Armour: Armour.Light, Mover: Mover.Wheels, Tech: 3, MadeBy: Kind.LightFactory, Blurb: "TWIN GUNS ON FOUR WHEELS."),
        [Kind.Tank] = new("BATTLE TANK", 300, 9, 300, Speed: 1.7, Sight: 4, Range: 4, Damage: 32, Reload: 1.6, Weapon: Weapon.Cannon,
            Armour: Armour.Heavy, Mover: Mover.Tracks, Turret: true, Tech: 3, MadeBy: Kind.HeavyFactory, Blurb: "THE BACKBONE OF ANY ARMY."),
        [Kind.RocketTank] = new("ROCKET TANK", 450, 11, 220, Speed: 1.6, Sight: 6, Range: 7, MinRange: 1.5, Damage: 34, Reload: 2.8, Shots: 2,
            Weapon: Weapon.Rocket, Armour: Armour.Heavy, Mover: Mover.Tracks, Tech: 4, MadeBy: Kind.HeavyFactory, Requires: [Kind.Outpost],
            Blurb: "LONG-RANGE ROCKETS. WEAK UP CLOSE."),
        [Kind.SiegeTank] = new("SIEGE TANK", 600, 13, 420, Speed: 1.2, Sight: 5, Range: 5.5, Damage: 55, Reload: 2.4, Weapon: Weapon.Cannon,
            Armour: Armour.Heavy, Mover: Mover.Tracks, Turret: true, Tech: 5, MadeBy: Kind.HeavyFactory, Requires: [Kind.Outpost],
            Blurb: "A HEAVY GUN FOR BREAKING BASES."),
        [Kind.Harvester] = new("HARVESTER", 300, 10, 450, Speed: 1.5, Sight: 3, Armour: Armour.Heavy, Mover: Mover.Tracks, Tech: 3,
            MadeBy: Kind.HeavyFactory, Blurb: "GATHERS SOLARITE FOR THE REFINERY."),
        [Kind.Skylifter] = new("SKYLIFTER", 800, 12, 200, Speed: 4, Sight: 3, Armour: Armour.Air, Mover: Mover.Air, Tech: 5,
            MadeBy: Kind.HighTech, Blurb: "FLIES HARVESTERS TO THE FIELDS AND HOME AGAIN."),
        [Kind.Mcv] = new("MOBILE YARD", 900, 14, 450, Speed: 1.1, Sight: 3, Armour: Armour.Heavy, Mover: Mover.Tracks, Tech: 4,
            MadeBy: Kind.HeavyFactory, Blurb: "UNFOLDS INTO A CONSTRUCTION YARD ON ROCK."),
        [Kind.Resonator] = new("RESONATOR", 600, 13, 300, Speed: 1.4, Sight: 5, Range: 5, Damage: 50, Reload: 2.6, Weapon: Weapon.Sonic,
            Armour: Armour.Heavy, Mover: Mover.Tracks, Tech: 7, MadeBy: Kind.HeavyFactory, Requires: [Kind.HighTech], Only: Faction.Aldaine,
            Blurb: "A WAVE OF SOUND THAT SHAKES EVERYTHING IN ITS PATH."),
        [Kind.Juggernaut] = new("JUGGERNAUT", 800, 16, 650, Speed: 0.8, Sight: 4, Range: 5, Damage: 40, Reload: 2.0, Shots: 2, Weapon: Weapon.Cannon,
            Armour: Armour.Heavy, Mover: Mover.Tracks, Turret: true, Tech: 7, MadeBy: Kind.HeavyFactory, Requires: [Kind.HighTech], Only: Faction.Vrakos,
            Blurb: "TWIN GUNS AND THICK ARMOUR. CAN BLOW ITSELF UP (D)."),
        [Kind.Beguiler] = new("BEGUILER", 750, 13, 220, Speed: 1.6, Sight: 6, Range: 7, MinRange: 1.5, Damage: 0, Reload: 4, Weapon: Weapon.Deviator,
            Armour: Armour.Heavy, Mover: Mover.Tracks, Tech: 7, MadeBy: Kind.HeavyFactory, Requires: [Kind.HighTech], Only: Faction.Sythe,
            Blurb: "ITS GAS TURNS ENEMY VEHICLES TO YOUR SIDE FOR A WHILE."),
        [Kind.Nomads] = new("NOMADS", 0, 0, 150, Speed: 1.3, Sight: 4, Range: 3.5, Damage: 16, Reload: 0.6, Weapon: Weapon.Gun,
            Armour: Armour.Infantry, Mover: Mover.Foot, Tech: NoTech, Blurb: "DESERT WARRIORS, SWORN TO HOUSE ALDAINE."),
        [Kind.Saboteur] = new("SABOTEUR", 0, 0, 60, Speed: 1.8, Sight: 4, Armour: Armour.Infantry, Mover: Mover.Foot, Tech: NoTech,
            Blurb: "SEND IT INTO AN ENEMY BUILDING TO DESTROY IT."),
        [Kind.Praetors] = new("PRAETORS", 0, 0, 170, Speed: 1.0, Sight: 4, Range: 4, Damage: 18, Reload: 0.5, Weapon: Weapon.Gun,
            Armour: Armour.Infantry, Mover: Mover.Foot, Tech: NoTech, Blurb: "THE SOVEREIGN'S OWN GUARD."),
    };

    public static Stats Of(Kind k) => Table[k];

    public static bool IsBuilding(Kind k) => k <= Kind.Palace;
    public static bool IsUnit(Kind k) => k >= Kind.Infantry;
    public static bool IsInfantry(Kind k) => Of(k).Armour == Armour.Infantry;
    public static bool IsAir(Kind k) => k == Kind.Skylifter;
    public static bool Armed(Kind k) => Of(k).Weapon != Weapon.None;
    /// <summary>A unit that fights (not a harvester, a yard, a skylifter or a saboteur).</summary>
    public static bool Fighter(Kind k) => IsUnit(k) && Armed(k);

    /// <summary>What a factory makes.</summary>
    public static readonly Dictionary<Kind, Kind[]> Makes = new()
    {
        [Kind.Yard] = [Kind.Slab, Kind.Turbine, Kind.Refinery, Kind.Silo, Kind.Barracks, Kind.LightFactory, Kind.Outpost, Kind.Wall,
            Kind.HeavyFactory, Kind.Turret, Kind.RocketTurret, Kind.RepairPad, Kind.HighTech, Kind.Starport, Kind.Palace],
        [Kind.Barracks] = [Kind.Infantry, Kind.Troopers],
        [Kind.LightFactory] = [Kind.Trike, Kind.Quad],
        [Kind.HeavyFactory] = [Kind.Tank, Kind.RocketTank, Kind.SiegeTank, Kind.Harvester, Kind.Mcv, Kind.Resonator, Kind.Juggernaut, Kind.Beguiler],
        [Kind.HighTech] = [Kind.Skylifter],
    };

    /// <summary>What the starport sells.</summary>
    public static readonly Kind[] Market = [Kind.Trike, Kind.Quad, Kind.Tank, Kind.RocketTank, Kind.SiegeTank, Kind.Harvester, Kind.Mcv, Kind.Skylifter];

    /// <summary>How hard each weapon hits each armour.</summary>
    public static double Versus(Weapon w, Armour a) => (w, a) switch
    {
        (Weapon.Gun, Armour.Infantry) => 1.0,
        (Weapon.Gun, Armour.Light) => 0.7,
        (Weapon.Gun, Armour.Heavy) => 0.3,
        (Weapon.Gun, Armour.Building) => 0.25,
        (Weapon.Cannon, Armour.Infantry) => 0.6,
        (Weapon.Cannon, Armour.Building) => 0.8,
        (Weapon.Cannon, Armour.Light or Armour.Heavy) => 1.0,
        (Weapon.Rocket, Armour.Infantry) => 0.5,
        (Weapon.Rocket, Armour.Heavy) => 1.1,
        (Weapon.Rocket, Armour.Building) => 0.9,
        (Weapon.Rocket, _) => 1.0,
        (Weapon.Sonic, Armour.Heavy) => 0.9,
        (Weapon.Sonic, Armour.Building) => 0.6,
        (Weapon.Sonic, Armour.Air) => 0,
        (Weapon.Sonic, _) => 1.0,
        (Weapon.Missile, Armour.Air) => 0,
        (Weapon.Missile, _) => 1.0,
        _ => 0,
    };

    /// <summary>Whether a weapon can reach a thing in the air.</summary>
    public static bool HitsAir(Weapon w) => w == Weapon.Rocket;

    // ───────────────────────────── the Houses ─────────────────────────────

    public static string Name(Faction f) => f switch
    {
        Faction.Aldaine => "ALDAINE", Faction.Vrakos => "VRAKOS", Faction.Sythe => "SYTHE", _ => "THE SOVEREIGN",
    };

    public static string HouseName(Faction f) => f == Faction.Sovereign ? "THE SOVEREIGN" : "HOUSE " + Name(f);

    public static uint Colour(Faction f) => f switch
    {
        Faction.Aldaine => 0xFF3C74E8, Faction.Vrakos => 0xFFD8392C, Faction.Sythe => 0xFF34B04C, _ => 0xFFA35AD8,
    };

    public static string Advisor(Faction f) => f switch
    {
        Faction.Aldaine => "ADVISOR CORVEN", Faction.Vrakos => "ADVISOR GRESH", Faction.Sythe => "ADVISOR LIRA", _ => "THE HERALD",
    };

    public static string Motto(Faction f) => f switch
    {
        Faction.Aldaine => "HONOUR IS THE SHARPEST BLADE",
        Faction.Vrakos => "WHAT WE CANNOT HOLD, WE BURN",
        Faction.Sythe => "THE PATIENT HAND TAKES ALL",
        _ => "ALL SANDS ARE THE SOVEREIGN'S",
    };

    /// <summary>A House's own unit, built at the heavy factory.</summary>
    public static Kind Special(Faction f) => f switch
    {
        Faction.Aldaine => Kind.Resonator, Faction.Vrakos => Kind.Juggernaut, Faction.Sythe => Kind.Beguiler, _ => Kind.Praetors,
    };

    /// <summary>What the palace does.</summary>
    public static string PalacePower(Faction f) => f switch
    {
        Faction.Aldaine => "NOMAD RAID", Faction.Vrakos => "RUIN MISSILE", Faction.Sythe => "SABOTEUR", _ => "",
    };

    /// <summary>Seconds for the palace's power to come round, at full power.</summary>
    public static double PalaceCharge(Faction f) => f == Faction.Vrakos ? 300 : 240;

    public static bool CanMake(Faction f, Kind k) => Of(k).Only is not { } only || only == f;
}
