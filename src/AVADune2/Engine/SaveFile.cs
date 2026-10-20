using System.Globalization;
using System.Text;

namespace AVADune2.Engine;

/// <summary>
/// The campaign saved between missions, so it can be continued: a small text file of the <see cref="Campaign"/> (the
/// House, the mission reached, the score, the defeats, the territory last taken and who holds each territory), kept
/// next to the high-score table.
/// </summary>
public static class SaveFile
{
    private const string Magic = "AVADUNE2 1";

    public static string PathIn(string folder) => Path.Combine(folder, "campaign.sav");

    public static bool Exists(string folder) => File.Exists(PathIn(folder));

    public static void Delete(string folder)
    {
        try { File.Delete(PathIn(folder)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static string Write(Campaign c)
    {
        var b = new StringBuilder();
        b.AppendLine(Magic);
        b.AppendLine(Line("campaign", (int)c.House, c.Mission, c.Score, c.Defeats, c.Seed, c.At));
        b.AppendLine(Line("owners", c.Owners.Select(f => (int)f).ToArray()));
        return b.ToString();
    }

    private static string Line(string key, params int[] values) => key + " " + string.Join(" ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    public static Campaign? Read(string text)
    {
        try
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 3 || lines[0] != Magic) return null;
            Campaign? c = null;
            int[]? owners = null;
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(' ');
                int[] v = parts.Skip(1).Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
                switch (parts[0])
                {
                    case "campaign":
                        if (v[0] is < 0 or > 2 || v[1] is < 1 or > Campaign.Missions + 1) return null;
                        c = new Campaign((Faction)v[0], v[4]) { Mission = v[1], Score = v[2], Defeats = v[3], At = v[5] };
                        break;
                    case "owners":
                        owners = v;
                        break;
                }
            }
            if (c is null || owners is null || owners.Length != c.Owners.Length || owners.Any(o => o is < 0 or > 3)) return null;
            if (c.At < 0 || c.At >= Campaign.Map.Length) return null;
            c.Owners = owners.Select(o => (Faction)o).ToArray();
            return c;
        }
        catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public static void Save(Campaign c, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string path = PathIn(folder), temp = path + ".tmp";
            File.WriteAllText(temp, Write(c));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A read-only home folder just means the campaign can't be continued.
        }
    }

    public static Campaign? Load(string folder)
    {
        try
        {
            return File.Exists(PathIn(folder)) ? Read(File.ReadAllText(PathIn(folder))) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
