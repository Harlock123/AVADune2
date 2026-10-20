using AVADune2.Engine;

namespace AVADune2.Tests;

/// <summary>
/// How the pilot gets on in the campaign's missions (set DUNE_PROBE to run it; DUNE_MISSIONS=4 for how many,
/// DUNE_FROM to start later, DUNE_HOUSE=0..2 for the House). DUNE_PROBE=lazy leaves the player's side to sit still.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_pilot_does()
    {
        if (Environment.GetEnvironmentVariable("DUNE_PROBE") is not { } mode) return;
        int last = int.TryParse(Environment.GetEnvironmentVariable("DUNE_MISSIONS"), out int n) ? n : 4;
        int first = int.TryParse(Environment.GetEnvironmentVariable("DUNE_FROM"), out int f0) ? f0 : 1;
        var house = (Faction)(int.TryParse(Environment.GetEnvironmentVariable("DUNE_HOUSE"), out int hi) ? hi : 0);
        for (int m = first; m <= last; m++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var c = new Campaign(house, 11) { Mission = m };
            var s = c.ScenarioFor(c.Choices()[0]);
            var w = MapGen.Setup(s);
            if (mode != "lazy") Pilot.TakeOver(w);
            string trace = "";
            int t = 0, most = 0;
            long slowest = 0;
            for (; t < 60 * 60 * 25 && w.Outcome < 0; t++)
            {
                long before = sw.ElapsedTicks;
                Sim.Step(w);
                slowest = Math.Max(slowest, sw.ElapsedTicks - before);
                most = Math.Max(most, w.Units.Count);
                w.Heard.Clear();
                w.News.Clear();
                if (Environment.GetEnvironmentVariable("DUNE_ARMY") is not null && t % 600 == 0)
                    output.WriteLine($"  {t / 60}s me: " + string.Join(" ", w.Units.Where(u => u.Owner == 0).GroupBy(u => (u.Type, u.Order)).Select(g => $"{g.Key.Type}/{g.Key.Order}x{g.Count()}"))
                        + "   them: " + string.Join(" ", w.Units.Where(u => u.Owner != 0).GroupBy(u => (u.Type, u.Order)).Select(g => $"{g.Key.Type}/{g.Key.Order}x{g.Count()}")) + $"  my bld {string.Join(",", w.Buildings.Where(b => b.Owner == 0).Select(b => b.Type))}");
                if (Environment.GetEnvironmentVariable("DUNE_TRACE") is not null && t % 600 == 0)
                    output.WriteLine($"  {t / 60}s " + string.Join(" ", w.Units.Where(u => u.Owner == 0).Select(u => $"{u.Type}@{u.Tx},{u.Ty}:{u.Order}{(u.Type == Kind.Harvester ? $"/c{u.Cargo:0}/p{u.Path?.Count ?? -1}{(u.PathPending ? "P" : "")}/g{u.Gx},{u.Gy}" : "")}"))
                        + " | " + string.Join(" ", w.Buildings.Where(b => b.Owner == 0).Select(b => $"{b.Type}@{b.X},{b.Y}{(b.Queue.Count > 0 ? "[" + string.Join(",", b.Queue) + $" {b.Progress:0.00}]" : "")}{(b.Ready is { } r ? "R:" + r : "")}"))
                        + " | maw " + string.Join(" ", w.Creatures.Select(c => $"{c.X:0},{c.Y:0}{(c.Prey is null ? "" : "->" + c.Prey.Type)}{(c.Dormant > 0 ? "z" : "")}")));
                if (t % (60 * 60) == 0)
                    trace += $" {t / 60}s:cr{(int)w.PlayerHouse.Credits}/h{(int)w.PlayerHouse.Harvested} me{w.UnitsOf(0).Count()}u{w.BuildingsOf(0).Count()}b them{w.Units.Count(u => u.Owner > 0)}u{w.Buildings.Count(b => b.Owner > 0)}b";
            }
            var p = w.PlayerHouse;
            output.WriteLine($"mission {m} vs {string.Join("+", s.Enemies)}: {(w.Outcome == 0 ? "WON" : w.Outcome == 1 ? "LOST" : "undecided")} after {t / 60}s; harvested {p.Harvested:0}, killed {p.UnitsKilled}u {p.BuildingsKilled}b, lost {p.UnitsLost}u {p.BuildingsLost}b; most units {most}; slowest step {slowest * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} ms; {sw.ElapsedMilliseconds} ms; paths {w.Paths.Searches}");
            output.WriteLine("   " + trace);
        }
    }
}
