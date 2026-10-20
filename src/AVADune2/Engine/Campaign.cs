namespace AVADune2.Engine;

/// <summary>
/// One battle's setting: the map's seed, the mission's number (its tech level and how big the enemy is), the player's
/// House and the enemies', what it asks (harvest so much, or destroy them), the credits to start with, and whether
/// the Sovereign's legion is in it.
/// </summary>
public sealed record Scenario(
    int Seed, int Mission, Faction Player, Faction[] Enemies, Goal Goal, int GoalCredits, int Tech, int Credits,
    int EnemyBase, int PlayerBase = 0, bool Sovereign = false, int Creatures = 1, bool Skirmish = false);

/// <summary>A territory on the region map: its name, its place (0 to 1 across and down), its row (the mission it's
/// fought in) and column, and who holds it.</summary>
public sealed record Territory(int Id, string Name, double X, double Y, int Row, int Col);

/// <summary>
/// The campaign for Vharos: nine missions across the region map, from the player's home territory at the bottom to the
/// Sovereign's seat at the top. After each won mission the player chooses the next territory to attack from those
/// next to the last one taken. Losing a mission lets the player try again (any territory on offer); the third
/// defeat ends the campaign.
/// </summary>
public sealed class Campaign
{
    public const int Missions = 9, Rows = Missions + 1, Cols = 3, MaxDefeats = 3;

    public Faction House;
    public int Mission = 1;
    public int Score;
    public int Defeats;
    public int Seed;
    /// <summary>The territory last taken (where the next attack starts from).</summary>
    public int At;
    /// <summary>Each territory's holder.</summary>
    public Faction[] Owners = new Faction[Rows * Cols];
    /// <summary>The territory being fought over now.</summary>
    public int Target = -1;
    public bool Won => Mission > Missions;

    public static readonly Territory[] Map = BuildMap();

    private static Territory[] BuildMap()
    {
        string[] a = ["OCHRE", "EMBER", "SALT", "GLASS", "DUST", "ASH", "AMBER", "COPPER", "HOLLOW", "WHITE", "IRON", "BITTER", "RED", "SHALE", "CINDER"];
        string[] b = ["REACH", "BASIN", "FLATS", "DEEP", "RIDGE", "WASTE", "SPUR", "PAN", "SHELF", "STEPPE", "SINK", "GATE"];
        var list = new List<Territory>();
        var d = new Dice(4242);
        var used = new HashSet<string>();
        for (int r = 0; r < Rows; r++)
        for (int c = 0; c < Cols; c++)
        {
            string name;
            do name = a[d.Next(a.Length)] + " " + b[d.Next(b.Length)];
            while (!used.Add(name));
            if (r == 0 && c == 1) name = "THE HOMESTEAD";
            if (r == Rows - 1 && c == 1) name = "THE SOVEREIGN'S SEAT";
            double x = 0.2 + c * 0.3 + d.Range(-0.07, 0.07), y = 0.93 - r * 0.093 + d.Range(-0.025, 0.025);
            list.Add(new Territory(list.Count, name, x, y, r, c));
        }
        return [.. list];
    }

    public static int Id(int row, int col) => row * Cols + col;

    public Campaign(Faction house, int seed)
    {
        House = house;
        Seed = seed;
        At = Id(0, 1);
        var (left, right) = Rivals(house);
        foreach (var t in Map)
            Owners[t.Id] = t.Row == 0 ? (t.Col == 1 ? house : t.Col == 0 ? left : right)
                : t.Row == Rows - 1 ? Faction.Sovereign
                : t.Col == 0 ? left : t.Col == 2 ? right : t.Row % 2 == 0 ? left : right;
        Owners[At] = house;
    }

    /// <summary>The two Houses the player fights.</summary>
    public static (Faction Left, Faction Right) Rivals(Faction house)
    {
        var others = new[] { Faction.Aldaine, Faction.Vrakos, Faction.Sythe }.Where(f => f != house).ToArray();
        return (others[0], others[1]);
    }

    /// <summary>The territories the player may attack next: in the next row, beside the last one taken.</summary>
    public List<int> Choices()
    {
        if (Won) return [];
        var from = Map[At];
        var list = Map.Where(t => t.Row == Mission && Math.Abs(t.Col - from.Col) <= 1).Select(t => t.Id).ToList();
        if (Mission == Rows - 1) list = [Id(Mission, 1)];
        return list;
    }

    /// <summary>The enemy Houses in a territory's mission: its holder; both rivals in the eighth; and in the last, both
    /// rivals with the Sovereign's legion.</summary>
    public Faction[] EnemiesFor(int territory)
    {
        var (l, r) = Rivals(House);
        var owner = Owners[territory];
        if (Mission >= Missions - 1 || owner == Faction.Sovereign) return [l, r];
        return [owner == House ? l : owner];
    }

    public Scenario ScenarioFor(int territory)
    {
        int m = Mission;
        var goal = m <= 2 ? Goal.Harvest : Goal.Destroy;
        int[] baseSize = [0, 0, 1, 2, 3, 3, 4, 4, 4, 5];
        return new Scenario(Seed * 31 + territory * 7 + m, m, House, EnemiesFor(territory), goal, m == 1 ? 1000 : 2500, m,
            m == 1 ? 1200 : 1200 + m * 300, baseSize[m], Sovereign: m == Missions, Creatures: m >= 6 ? 2 : 1);
    }

    /// <summary>A mission won: the territory is the player's, and on to the next row.</summary>
    public void Win(int territory, int points)
    {
        Owners[territory] = House;
        At = territory;
        Score += points;
        Mission++;
        Target = -1;
    }

    /// <summary>A mission lost: a defeat counted (the points still count).</summary>
    public void Lose(int points)
    {
        Score += points;
        Defeats++;
        Target = -1;
    }

    public bool Over => Won || Defeats >= MaxDefeats;

    // ───────────────────────────── words ─────────────────────────────

    public static string PlanetName => "VHAROS";

    /// <summary>The advisor's briefing for a mission in a territory.</summary>
    public string Briefing(int territory)
    {
        var t = Map[territory];
        var s = ScenarioFor(territory);
        string foe = string.Join(" AND ", s.Enemies.Select(Rules.HouseName));
        string you = Rules.HouseName(House);
        string intro = House switch
        {
            Faction.Aldaine => "MY LORD, ",
            Faction.Vrakos => "LISTEN WELL, COMMANDER. ",
            _ => "A WORD, COMMANDER. ",
        };
        string body = Mission switch
        {
            1 => $"THE SOVEREIGN HAS GRANTED {you} A CLAIM ON {PlanetName}, AND ALL THAT MATTERS HERE IS SOLARITE, THE BURNING SAND THAT POWERS EVERY ENGINE BETWEEN THE STARS. IN {t.Name} YOU HAVE A CONSTRUCTION YARD AND LITTLE ELSE. BUILD WIND TURBINES FOR POWER AND A REFINERY, AND SEND ITS HARVESTER TO THE ORANGE FIELDS. HARVEST {s.GoalCredits} CREDITS OF SOLARITE. BUILD ONLY ON ROCK, AND ON CONCRETE IF YOU CAN. AND KEEP OFF THE OPEN SAND WHEN YOU CAN: THE SANDMAWS HEAR EVERYTHING THAT MOVES ON IT.",
            2 => $"{foe} HAS COME TO {t.Name} TOO. HARVEST {s.GoalCredits} CREDITS BEFORE THEY DRIVE US OFF. A BARRACKS AND A LIGHT FACTORY ARE YOURS TO BUILD NOW, AND A RADAR OUTPOST TO SEE THE FIELD. SILOS WILL HOLD WHAT THE REFINERY CANNOT.",
            3 => $"ENOUGH HARVESTING. {foe} HAS A BASE IN {t.Name}. DESTROY IT, EVERY BUILDING AND EVERY UNIT. THE HEAVY FACTORY GIVES YOU BATTLE TANKS, AND GUN TURRETS WILL GUARD YOUR WALLS.",
            4 => $"{foe} HOLDS {t.Name} IN STRENGTH. ROCKET TANKS, ROCKET TURRETS AND A REPAIR PAD ARE YOURS NOW. DESTROY THEM.",
            5 => $"THE HIGH-TECH FACTORY BUILDS SKYLIFTERS TO FLY YOUR HARVESTERS TO AND FRO, AND SIEGE TANKS WILL BREAK THEIR WALLS. {foe} WAITS IN {t.Name}. WIPE THEM OUT.",
            6 => $"A STARPORT LETS US BUY UNITS FROM OFF-WORLD. THE PRICES RISE AND FALL: BUY WHEN THEY ARE LOW. {foe} IS DUG IN AT {t.Name}. DESTROY THEM.",
            7 => $"OUR ENGINEERS HAVE FINISHED THE {Rules.Of(Rules.Special(House)).Name}, BUILT AT THE HEAVY FACTORY ONCE THE HIGH-TECH FACTORY STANDS. {Rules.Of(Rules.Special(House)).Blurb} USE IT ON {foe} IN {t.Name}.",
            8 => $"BUILD A PALACE AND ITS POWER IS YOURS: THE {Rules.PalacePower(House)}. BOTH {foe} STAND AGAINST US IN {t.Name}. DESTROY THEM BOTH.",
            _ => $"BETRAYAL! THE SOVEREIGN FEARS {you} AND HAS SENT THE PRAETORS TO STAND WITH {foe}. THIS IS THE LAST BATTLE FOR {PlanetName}. DESTROY THEM ALL, AND THE PLANET IS OURS.",
        };
        return intro + body;
    }

    /// <summary>A title for a campaign score.</summary>
    public static string Rank(int score) => score switch
    {
        < 1500 => "SAND RAT",
        < 4000 => "DESERT RUNNER",
        < 8000 => "FIELD OFFICER",
        < 13000 => "COMMANDER",
        < 20000 => "WAR CAPTAIN",
        < 28000 => "LORD MARSHAL",
        _ => "MASTER OF VHAROS",
    };
}

/// <summary>A mission's result, for the score screen.</summary>
public sealed record Debrief(bool Won, int Mission, double Seconds, int Harvested, int UnitsKilled, int UnitsLost,
    int BuildingsKilled, int BuildingsLost, int Points)
{
    /// <summary>The points for a mission: credits harvested, the enemy destroyed, a bonus for winning (more the
    /// harder the mission) and for winning quickly, less what was lost.</summary>
    public static Debrief Of(World w, int mission, bool won)
    {
        var h = w.PlayerHouse;
        int pts = (int)(h.Harvested / 5) + h.UnitsKilled * 25 + h.BuildingsKilled * 60 - h.UnitsLost * 8 - h.BuildingsLost * 20;
        if (won) pts += 500 * mission + Math.Max(0, (int)(1200 - w.Clock) * mission / 2);
        return new Debrief(won, mission, w.Clock, (int)h.Harvested, h.UnitsKilled, h.UnitsLost, h.BuildingsKilled, h.BuildingsLost, Math.Max(0, pts));
    }
}
