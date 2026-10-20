using AVADune2.Engine;

namespace AVADune2.Tests;

/// <summary>Small battlefields for the tests: all one ground, the player (Aldaine) and an enemy (Vrakos), no minds.</summary>
internal static class TestWorld
{
    public const int Enemy = 1;

    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "avadune2-tests-" + Guid.NewGuid().ToString("N"));

    public static GameEngine NewGame() { string f = TempFolder(); return new GameEngine(HighScoreTable.Load(f), f, seed: 3) { Silent = true }; }

    public static World Blank(Ground ground = Ground.Rock, int credits = 5000, Faction player = Faction.Aldaine, Faction enemy = Faction.Vrakos)
    {
        var w = new World { Goal = Goal.Destroy };
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++) w.Ground[x, y] = ground;
        Sim.AddHouse(w, player, 0, false, credits);
        Sim.AddHouse(w, enemy, 1, true, credits);
        return w;
    }

    /// <summary>Sand everywhere but a block of rock.</summary>
    public static void Rock(World w, int x0, int y0, int x1, int y1)
    {
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++) w.Ground[x, y] = Ground.Rock;
    }

    /// <summary>A field of solarite.</summary>
    public static void Ore(World w, int x0, int y0, int x1, int y1, int amount = 400)
    {
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++) w.Ore[x, y] = amount;
    }

    public static void Run(World w, double seconds)
    {
        for (int i = 0; i < (int)(seconds * 60); i++) Sim.Step(w);
    }

    /// <summary>Runs until something happens (true), or the time runs out (false).</summary>
    public static bool RunUntil(World w, Func<bool> done, double seconds)
    {
        for (int i = 0; i < (int)(seconds * 60); i++)
        {
            if (done()) return true;
            Sim.Step(w);
        }
        return done();
    }
}
