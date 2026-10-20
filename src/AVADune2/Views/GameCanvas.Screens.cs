using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVADune2.Engine;

namespace AVADune2.Views;

public sealed partial class GameCanvas
{
    private static WriteableBitmap? _backdrop, _regionArt;
    private static int _regionKey = -1;

    // ───────────────────────────── the desert backdrop ─────────────────────────────

    /// <summary>The desert at dusk, drawn pixel by pixel once: a sky from violet to amber with stars, a great pale
    /// sun low on the horizon, a second small moon, mesas in the haze, and dunes rolling to the front.</summary>
    private static WriteableBitmap Backdrop()
    {
        if (_backdrop is not null) return _backdrop;
        int w = (int)ScreenWidth, h = (int)ScreenHeight;
        var p = new Pix(w, h);
        double horizon = 470;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            uint c;
            if (y < horizon)
            {
                double t = y / horizon;
                c = t < 0.55 ? Pix.Mix(0xFF1A1030, 0xFF6A3050, t / 0.55) : Pix.Mix(0xFF6A3050, 0xFFE8904A, (t - 0.55) / 0.45);
                double sun = Math.Sqrt((x - 700.0) * (x - 700.0) + (y - 420.0) * (y - 420.0));
                if (sun < 180) c = Pix.Mix(c, 0xFFFFD890, Math.Pow(1 - sun / 180, 2) * 0.8);
                if (sun < 62) c = Pix.Mix(0xFFFFF0C8, 0xFFFFE0A0, sun / 62);
                double moon = Math.Sqrt((x - 150.0) * (x - 150.0) + (y - 345.0) * (y - 345.0));
                if (moon < 20) c = Pix.Mix(0xFFF0E4D8, 0xFFA894A8, Math.Clamp((x - 150.0 + y - 345.0) / 36 + 0.4, 0, 1));
                if (y < 300 && Pix.Rand(x, y, 7) > 0.9985) c = Pix.Mix(c, 0xFFFFFFFF, 0.8 * (1 - y / 300.0));
            }
            else c = 0xFFC88A4A;
            p.P[y * w + x] = c;
        }
        // Mesas in the haze: flat tops and steep sides.
        for (int x = 0; x < w; x++)
        {
            double n = MapGen.Noise(x / 260.0, 3, 17), lift = Math.Clamp((n - 0.48) * 7, 0, 1);
            lift = lift * lift * (3 - 2 * lift);
            double top = horizon - 8 - lift * 64 - MapGen.Noise(x / 30.0, 5, 18) * 6;
            for (int y = (int)top; y < horizon + 6; y++)
            {
                double k = (y - top) / Math.Max(1, horizon + 6 - top);
                uint c = Pix.Mix(0xFF9A5A58, 0xFFC07A5C, k);
                if (y < top + 2) c = 0xFFD89070;
                p.P[y * w + x] = Pix.Mix(c, 0xFFE8A070, 0.25);
            }
        }
        // Dunes: smooth ridges in layers, each lit on its windward face, the far ones pale in the haze.
        for (int layer = 0; layer < 5; layer++)
        {
            double baseY = horizon + 30 + layer * 58, amp = 18 + layer * 12, f = 0.006 + layer * 0.0015, phase = layer * 1.7;
            int lay = layer;
            double Crest(double x) => baseY - amp * (0.6 * Math.Sin(x * f + phase) + 0.3 * Math.Sin(x * f * 2.3 + phase * 2.1) + 0.35 * MapGen.Noise(x / 260.0, lay * 3.1, 40 + lay));
            uint light = Pix.Mix(0xFFE0A868, 0xFFF4CC8C, layer / 4.0), shade = Pix.Mix(0xFFA87048, 0xFFB47A44, layer / 4.0), deep = Pix.Mix(0xFF8A5A3E, 0xFF9A6438, layer / 4.0);
            double haze = 0.45 * (1 - layer / 4.0);
            for (int x = 0; x < w; x++)
            {
                double crest = Crest(x), slope = Crest(x + 4) - Crest(x - 4);
                double lit = 0.5 + Math.Clamp(slope * 0.08, -0.45, 0.45);
                for (int y = Math.Max(0, (int)crest); y < h; y++)
                {
                    double depth = Math.Min(1, (y - crest) / 90.0);
                    uint c = Pix.Mix(Pix.Mix(shade, light, lit * (1 - depth * 0.6) + 0.3 * depth * 0.6), deep, depth * 0.55);
                    if (y - crest < 1.5 && lit > 0.5) c = Pix.Mix(c, 0xFFFFE4B0, 0.6);
                    if (Pix.Rand(x, y, layer) > 0.985) c = Pix.Scale(c, 1.06);
                    p.P[y * w + x] = Pix.Mix(c, 0xFFE8A878, haze);
                }
            }
        }
        return _backdrop = p.ToBitmap();
    }

    private static void DrawBackdrop(DrawingContext dc) => dc.DrawImage(Backdrop(), new Rect(0, 0, ScreenWidth, ScreenHeight));

    // ───────────────────────────── the title ─────────────────────────────

    private static readonly Rect CampaignButton = new(362, 452, 300, 40), SkirmishButton = new(362, 500, 300, 40), ContinueButton = new(362, 548, 300, 40);

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        // A sandmaw breaching far out on the dunes.
        double t = (_clock * 0.12 + 0.25) % 1;
        DrawTitleMaw(dc, new Point(880, 640), t);
        double cx = ScreenWidth / 2;
        string title = "THE SOLARITE WARS";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 25 + 25 + i * 50, ty = 150 + Math.Sin(_clock * 2 + i * 0.4) * 4;
            Text(dc, title[i].ToString(), new Point(tx + 3, ty + 4), 112, 0x90401808, center: true, shadow: false);
            Text(dc, title[i].ToString(), new Point(tx, ty), 112, i < 3 ? 0xFFFFE8B0 : 0xFFFF9A3A, center: true);
        }
        Text(dc, "BATTLE FOR VHAROS", new Point(cx, 236), 40, 0xFFFFE8C0, center: true);
        Text(dc, "THREE HOUSES. ONE PLANET. THE BURNING SAND THAT POWERS THE STARS.", new Point(cx, 280), 24, White, center: true);
        Text(dc, "HARVEST SOLARITE, BUILD A BASE, RAISE AN ARMY, AND DRIVE YOUR RIVALS FROM THE DESERT.", new Point(cx, 306), 22, Sandy, center: true);
        Panel(dc, new Rect(342, 432, 340, 172), 0xFF8A6A3A, 0xD0140E0A);
        Button(dc, CampaignButton, "NEW CAMPAIGN  (ENTER)", size: 24);
        Button(dc, SkirmishButton, "SKIRMISH  (K)", size: 24);
        Button(dc, ContinueButton, "CONTINUE CAMPAIGN  (C)", g.HasSave, size: 24);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 690, ScreenWidth, 78));
        Text(dc, "MOUSE: CLICK/DRAG TO PICK OUT, RIGHT-CLICK TO ORDER   A ATTACK  S STOP  G GUARD  D DEPLOY", new Point(cx, 708), 19, 0xFFE0D0B0, center: true);
        Text(dc, "CTRL+1-9 GROUPS   ARROWS SCROLL   +/- SPEED   P PAUSE   M SOUND   CTRL+S SCREENSHOT", new Point(cx, 730), 19, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: TOCCATA AND FUGUE IN D MINOR, BWV 565 (J. S. BACH)", new Point(cx, 753), 17, Dim, center: true);
    }

    /// <summary>The title's sandmaw: a great ringed body arching up out of the dunes, its round mouth open to the
    /// sky, and sinking back.</summary>
    private static void DrawTitleMaw(DrawingContext dc, Point at, double t)
    {
        double rise = Math.Max(0, Math.Sin(t * Math.PI));
        if (rise < 0.03) return;
        // The body follows a curve out of the sand, up and over; drawn back to front as shaded rings.
        Point Along(double k) => new(at.X + 40 - 70 * k - 30 * k * k, at.Y + 30 - 230 * rise * Math.Sin(k * 1.4));
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, at.Y + 22)))
        {
            for (int i = 0; i <= 24; i++)
            {
                double k = i / 24.0;
                var c = Along(k);
                double r = 36 - k * 6;
                dc.FillEllipse(c.X + 6, c.Y + 4, r, 0x40000000);
                dc.FillEllipse(c.X, c.Y, r, i % 2 == 0 ? 0xFF6E523A : 0xFF7E6044);
                dc.FillEllipse(c.X - r * 0.3, c.Y - r * 0.25, r * 0.55, 0x30FFE8C8);
                dc.DrawEllipse(null, Pen(0xFF3E2C1E, 2), c, r, r);
            }
            var head = Along(1);
            dc.FillEllipse(head.X, head.Y, 44, 0xFF5E4430);
            dc.FillEllipse(head.X, head.Y, 37, 0xFF3A1414);
            dc.FillEllipse(head.X + 3, head.Y + 3, 22, 0xFF8A2A2A);
            dc.FillEllipse(head.X + 5, head.Y + 5, 10, 0xFF140606);
            for (int ring = 0; ring < 2; ring++)
            {
                double rr = 38 - ring * 13;
                int teeth = 16 - ring * 5;
                for (int i = 0; i < teeth; i++)
                {
                    double a = i * Math.PI * 2 / teeth + ring * 0.2;
                    var o = new Point(head.X + Math.Cos(a) * rr, head.Y + Math.Sin(a) * rr);
                    var tip = new Point(head.X + Math.Cos(a) * rr * 0.7, head.Y + Math.Sin(a) * rr * 0.7);
                    var side = new Point(-Math.Sin(a) * 4, Math.Cos(a) * 4);
                    Poly(dc, 0xFFF4ECD8, 0xFF8A7A60, 0.8, new Point(o.X + side.X, o.Y + side.Y), tip, new Point(o.X - side.X, o.Y - side.Y));
                }
            }
        }
        // Sand pouring off it, and a spray where it broke the surface.
        for (int i = 0; i < 26; i++)
        {
            double a = Math.PI + i * Math.PI / 25, d = 46 + Hash(i, 1, 2) % 30, fall = (t * 3 + i * 0.13) % 1;
            dc.FillEllipse(at.X + 40 + Math.Cos(a) * d, at.Y + 26 + Math.Sin(a) * d * 0.5 * rise, 3.5, 0xC0E8B878);
            var c = Along(0.3 + (i % 8) / 12.0);
            dc.FillEllipse(c.X + (i % 3 - 1) * 14, c.Y + fall * 60, 2.5, Alpha(0xFFE8C080, 0.7 * (1 - fall)));
        }
    }

    private void TitleClick(Point at)
    {
        var g = Engine;
        if (CampaignButton.Contains(at)) g.NewCampaign();
        else if (SkirmishButton.Contains(at)) g.NewSkirmish();
        else if (ContinueButton.Contains(at)) g.Continue();
    }

    // ───────────────────────────── choosing a House ─────────────────────────────

    private static Rect HouseRect(int i) => new(28 + i * 330, 112, 308, 560);
    private static readonly Rect OpponentsRect = new(392, 690, 240, 34);

    private void DrawHouses(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        Text(dc, g.Skirmish ? "SKIRMISH - CHOOSE YOUR HOUSE" : "CHOOSE YOUR HOUSE", new Point(512, 56), 54, Yellow, center: true);
        for (int i = 0; i < 3; i++)
        {
            var f = (Faction)i;
            var r = HouseRect(i);
            bool on = g.HouseIndex == i || r.Contains(_mouse);
            uint c = Rules.Colour(f);
            Panel(dc, r, on ? c : Shade(c, 0.5), on ? 0xF0201810 : 0xD0140E0A);
            DrawEmblem(dc, f, new Point(r.Center.X, r.Y + 70), 48);
            Text(dc, Rules.HouseName(f), new Point(r.Center.X, r.Y + 142), 38, on ? White : Sandy, center: true);
            Text(dc, "\"" + Rules.Motto(f) + "\"", new Point(r.Center.X, r.Y + 172), 18, c == 0xFF3C74E8 ? 0xFF8AB0FF : Shade(c, 1.4), center: true);
            DrawPortrait(dc, f, new Rect(r.X + 84, r.Y + 194, 140, 150), talking: false);
            Text(dc, Rules.Advisor(f), new Point(r.Center.X, r.Y + 358), 20, Dim, center: true);
            string[] lines = f switch
            {
                Faction.Aldaine => ["AN OLD AND HONOURABLE HOUSE.", "SPECIAL: THE RESONATOR, WHOSE", "SOUND WAVE SHAKES ALL IN ITS PATH.", "PALACE: A NOMAD RAID."],
                Faction.Vrakos => ["A BRUTAL HOUSE OF HEAVY ARMS.", "SPECIAL: THE JUGGERNAUT, TWIN-", "GUNNED, THAT CAN BLOW ITSELF UP.", "PALACE: THE RUIN MISSILE."],
                _ => ["A SLY HOUSE OF MERCHANTS.", "SPECIAL: THE BEGUILER, WHOSE GAS", "TURNS ENEMY VEHICLES FOR A WHILE.", "PALACE: A SABOTEUR."],
            };
            for (int k = 0; k < lines.Length; k++) Text(dc, lines[k], new Point(r.Center.X, r.Y + 400 + k * 26), 20, White, center: true);
            Text(dc, $"PRESS {i + 1}", new Point(r.Center.X, r.Bottom - 26), 22, on ? Yellow : Dim, center: true);
        }
        if (g.Skirmish) Button(dc, OpponentsRect, $"OPPONENTS: {g.Opponents}  (UP/DOWN)", size: 22);
        else Text(dc, "LEFT/RIGHT AND ENTER, OR CLICK A HOUSE   -   ESC: BACK", new Point(512, 706), 22, Dim, center: true);
    }

    private void HousesClick(Point at)
    {
        var g = Engine;
        if (g.Skirmish && OpponentsRect.Contains(at)) { g.Opponents = 3 - g.Opponents; return; }
        for (int i = 0; i < 3; i++)
            if (HouseRect(i).Contains(at)) g.ChooseHouse((Faction)i);
    }

    // ───────────────────────────── the briefing ─────────────────────────────

    private void DrawBriefing(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        if (g.Campaign is not { } c || c.Target < 0) return;
        var f = c.House;
        var t = Campaign.Map[c.Target];
        var frame = new Rect(40, 90, 330, 400);
        Panel(dc, frame, Rules.Colour(f), 0xF0100C08);
        DrawPortrait(dc, f, frame.Deflate(14), talking: g.StateTime < 9);
        Text(dc, Rules.Advisor(f), new Point(frame.Center.X, frame.Bottom + 24), 26, Sandy, center: true);
        DrawEmblem(dc, f, new Point(frame.Center.X, frame.Bottom + 90), 34);
        Text(dc, $"MISSION {c.Mission} OF {Campaign.Missions}", new Point(400, 60), 30, Dim);
        Text(dc, t.Name, new Point(400, 100), 52, Yellow);
        var s = g.Scenario!;
        Text(dc, "ENEMY: " + string.Join(", ", s.Enemies.Select(Rules.HouseName)) + (s.Sovereign ? ", THE SOVEREIGN'S PRAETORS" : ""), new Point(400, 144), 22, Red);
        Text(dc, s.Goal == Goal.Harvest ? $"ORDERS: HARVEST {s.GoalCredits} CREDITS OF SOLARITE" : "ORDERS: DESTROY EVERY ENEMY BUILDING AND UNIT", new Point(400, 172), 22, Green);
        var text = c.Briefing(c.Target);
        // The words appear as the advisor speaks them.
        int shown = (int)Math.Min(text.Length, g.StateTime * 70);
        var lines = Wrap(text, 25, 580);
        int left = shown;
        for (int i = 0; i < lines.Count && left > 0; i++)
        {
            string line = lines[i].Length <= left ? lines[i] : lines[i][..left];
            left -= lines[i].Length + 1;
            Text(dc, line, new Point(400, 218 + i * 30), 25, White);
        }
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "CLICK OR PRESS ENTER TO BEGIN", new Point(690, 712), 30, Yellow, center: true);
    }

    // ───────────────────────────── the region map ─────────────────────────────

    private static readonly Rect RegionRect = new(40, 96, 944, 560);

    private static Point RegionPoint(Territory t) => new(RegionRect.X + t.X * RegionRect.Width, RegionRect.Y + t.Y * RegionRect.Height);

    /// <summary>The region map: the desert from above, each territory in its holder's colour, with borders between.</summary>
    private static WriteableBitmap RegionArt(Campaign c)
    {
        int key = c.Owners.Aggregate(17, (h, f) => h * 5 + (int)f) ^ (int)c.House * 7919;
        if (_regionArt is not null && key == _regionKey) return _regionArt;
        int w = (int)RegionRect.Width, h = (int)RegionRect.Height;
        var p = new Pix(w, h);
        var map = Campaign.Map;
        var owner = new int[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // The nearest territory, with the borders roughened by noise.
            double wx = x + (MapGen.Noise(x / 40.0, y / 40.0, 5) - 0.5) * 60, wy = y + (MapGen.Noise(x / 40.0, y / 40.0, 9) - 0.5) * 60;
            int best = 0;
            double bestD = double.MaxValue;
            foreach (var t in map)
            {
                double d = (wx - t.X * w) * (wx - t.X * w) + (wy - t.Y * h) * (wy - t.Y * h);
                if (d < bestD) { bestD = d; best = t.Id; }
            }
            owner[y * w + x] = best;
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int id = owner[y * w + x];
            // The ground in relief, lit from the upper left, washed with the holder's colour.
            double n = MapGen.Noise(x / 45.0, y / 45.0, 3), slope = MapGen.Noise((x - 2) / 45.0, (y - 2) / 45.0, 3) - MapGen.Noise((x + 2) / 45.0, (y + 2) / 45.0, 3);
            uint ground = Pix.Scale(Pix.Mix(0xFFD2A46C, 0xFF9A8268, Math.Clamp((n - 0.55) * 4, 0, 1)), 0.9 + slope * 6 + MapGen.Noise(x / 6.0, y / 6.0, 4) * 0.08);
            var f = c.Owners[id];
            uint col = Pix.Mix(ground, Rules.Colour(f), f == c.House ? 0.55 : 0.42);
            int Near(int dx, int dy) => x + dx >= 0 && x + dx < w && y + dy >= 0 && y + dy < h ? owner[(y + dy) * w + x + dx] : id;
            if (Near(1, 0) != id || Near(0, 1) != id || Near(-1, 0) != id || Near(0, -1) != id) col = 0xFF2A1E14;
            else if (Near(2, 0) != id || Near(0, 2) != id || Near(-2, 0) != id || Near(0, -2) != id) col = Pix.Mix(col, 0xFFFFF0D0, 0.3);
            p.P[y * w + x] = col;
        }
        p.Frame(0, 0, w, h, 0xFF2A1E14);
        _regionKey = key;
        return _regionArt = p.ToBitmap();
    }

    private void DrawRegion(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        if (g.Campaign is not { } c) return;
        Text(dc, $"THE REGIONS OF {Campaign.PlanetName}", new Point(40, 34), 40, Yellow);
        Text(dc, $"MISSION {c.Mission} OF {Campaign.Missions}   SCORE {c.Score}   DEFEATS {c.Defeats} OF {Campaign.MaxDefeats}", new Point(984, 34), 24, White, right: true);
        Text(dc, "CHOOSE THE NEXT TERRITORY TO ATTACK", new Point(40, 72), 26, Sandy);
        dc.DrawImage(RegionArt(c), RegionRect);
        var choices = c.Choices();
        foreach (var t in Campaign.Map)
        {
            var at = RegionPoint(t);
            if (t.Id == c.At) DrawEmblem(dc, c.House, at, 14);
            else if (!choices.Contains(t.Id)) Text(dc, t.Name, new Point(at.X, at.Y), 15, 0xC0FFF0D8, center: true);
        }
        Territory? hover = null;
        for (int i = 0; i < choices.Count; i++)
        {
            var t = Campaign.Map[choices[i]];
            var at = RegionPoint(t);
            bool on = i == g.RegionIndex || Math.Abs(at.X - _mouse.X) < 60 && Math.Abs(at.Y - _mouse.Y) < 26;
            if (on) hover = t;
            double pulse = 1 + 0.2 * Math.Sin(_clock * 6);
            dc.DrawEllipse(Brush(Alpha(0xFF000000, 0.4)), Pen(on ? Yellow : 0xFFFFA040, on ? 3 : 2), at, 34 * pulse, 18 * pulse);
            Text(dc, t.Name, at, on ? 22 : 18, on ? Yellow : White, center: true);
            // An arrow from where the last attack started.
            var from = RegionPoint(Campaign.Map[c.At]);
            double a = Math.Atan2(at.Y - from.Y, at.X - from.X), len = Math.Sqrt((at.X - from.X) * (at.X - from.X) + (at.Y - from.Y) * (at.Y - from.Y));
            if (len > 50)
            {
                var tip = new Point(at.X - Math.Cos(a) * 26, at.Y - Math.Sin(a) * 22);
                var tail = new Point(from.X + Math.Cos(a) * 18, from.Y + Math.Sin(a) * 18);
                dc.DrawLine(Pen(on ? Yellow : 0xC0FFA040, 3), tail, tip);
                Poly(dc, on ? Yellow : 0xFFFFA040, null, 0, tip, new Point(tip.X - Math.Cos(a - 0.4) * 12, tip.Y - Math.Sin(a - 0.4) * 12), new Point(tip.X - Math.Cos(a + 0.4) * 12, tip.Y - Math.Sin(a + 0.4) * 12));
            }
        }
        // The legend.
        double lx = 40;
        foreach (var f in new[] { c.House }.Concat(new[] { Campaign.Rivals(c.House).Left, Campaign.Rivals(c.House).Right, Faction.Sovereign }))
        {
            dc.FillRectangle(Brush(Rules.Colour(f)), new Rect(lx, 668, 18, 18), 3);
            Text(dc, Rules.HouseName(f), new Point(lx + 24, 677), 20, White);
            lx += 34 + TextWidth(Rules.HouseName(f), 20);
        }
        if (hover is { } h)
        {
            var enemies = c.EnemiesFor(h.Id);
            Text(dc, $"{h.Name}: HELD BY {Rules.HouseName(c.Owners[h.Id])} - {(c.Mission <= 2 ? "HARVEST" : "DESTROY")} - AGAINST {string.Join(" AND ", enemies.Select(Rules.Name))}", new Point(512, 712), 20, Yellow, center: true);
        }
        Text(dc, "CLICK OR LEFT/RIGHT AND ENTER   -   Q: RETIRE   -   ESC: TITLE (THE CAMPAIGN IS SAVED)", new Point(512, 744), 19, Dim, center: true);
    }

    private void RegionClick(Point at)
    {
        var g = Engine;
        if (g.Campaign is not { } c) return;
        foreach (int id in c.Choices())
        {
            var p = RegionPoint(Campaign.Map[id]);
            if (Math.Abs(p.X - at.X) < 60 && Math.Abs(p.Y - at.Y) < 26) { g.ChooseTerritory(id); return; }
        }
    }

    // ───────────────────────────── the score ─────────────────────────────

    private void DrawDebrief(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        if (g.Debrief is not { } d) return;
        var r = new Rect(162, 60, 700, 640);
        var f = g.World.Houses.Count > 0 ? g.World.PlayerHouse.Faction : Faction.Aldaine;
        Panel(dc, r, Rules.Colour(f), 0xF0140E0A);
        Text(dc, d.Won ? "MISSION ACCOMPLISHED" : "MISSION FAILED", new Point(512, 110), 56, d.Won ? Yellow : Red, center: true);
        Text(dc, g.Skirmish ? "SKIRMISH" : $"MISSION {d.Mission}  -  {TimeSpan.FromSeconds(d.Seconds):mm\\:ss}", new Point(512, 154), 26, Dim, center: true);
        // The figures count up as they're shown.
        double k = Math.Min(1, g.StateTime / 2.5);
        var rows = new (string Label, int Value, int Max, uint Colour)[]
        {
            ("SOLARITE HARVESTED", d.Harvested, Math.Max(2500, d.Harvested), Orange),
            ("ENEMY UNITS DESTROYED", d.UnitsKilled, Math.Max(40, d.UnitsKilled), Green),
            ("ENEMY BUILDINGS DESTROYED", d.BuildingsKilled, Math.Max(20, d.BuildingsKilled), Green),
            ("UNITS LOST", d.UnitsLost, Math.Max(40, d.UnitsLost), Red),
            ("BUILDINGS LOST", d.BuildingsLost, Math.Max(20, d.BuildingsLost), Red),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            var (label, value, max, colour) = rows[i];
            double y = 214 + i * 58;
            Text(dc, label, new Point(200, y), 26, White);
            var bar = new Rect(200, y + 16, 520, 14);
            dc.FillRectangle(Brush(0xFF0A0806), bar, 3);
            dc.FillRectangle(Brush(colour), new Rect(bar.X, bar.Y, bar.Width * value / max * k, bar.Height), 3);
            Text(dc, $"{(int)(value * k)}", new Point(826, y + 10), 30, colour, right: true);
        }
        Text(dc, "POINTS", new Point(200, 520), 34, Sandy);
        Text(dc, $"{(int)(d.Points * k)}", new Point(826, 520), 40, Yellow, right: true);
        if (g.Campaign is { } c)
        {
            int total = c.Score + d.Points;
            Text(dc, "CAMPAIGN SCORE", new Point(200, 568), 30, Sandy);
            Text(dc, $"{total}", new Point(826, 568), 34, White, right: true);
            Text(dc, $"RANK: {Campaign.Rank(total)}", new Point(512, 616), 30, Yellow, center: true);
        }
        if (g.StateTime > 1 && (int)(_clock * 2) % 2 == 0) Text(dc, "CLICK OR PRESS ENTER", new Point(512, 668), 26, White, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        bool won = g.Campaign?.Won ?? false;
        var r = new Rect(162, 200, 700, 300);
        Panel(dc, r, won ? Yellow : Red, 0xF0140E0A);
        Text(dc, won ? $"{Campaign.PlanetName} IS YOURS" : "THE CAMPAIGN IS LOST", new Point(512, 262), 64, (int)(_clock * 3) % 2 == 0 ? Yellow : White, center: true);
        Text(dc, won ? "THE SOVEREIGN'S LEGION IS BROKEN, AND THE HOUSES BOW TO YOU." : "THE COUNCIL OF YOUR HOUSE WILL STAND NO MORE DEFEATS.", new Point(512, 330), 24, White, center: true);
        Text(dc, $"FINAL SCORE {g.Score}", new Point(512, 392), 44, Orange, center: true);
        Text(dc, $"RANK: {Campaign.Rank(g.Score)}", new Point(512, 446), 30, Sandy, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = ScreenWidth / 2;
        Panel(dc, new Rect(cx - 330, 60, 660, 650), 0xFF8A6A3A, 0xE0140E0A);
        Text(dc, "THE GREAT COMMANDERS", new Point(cx, 112), 54, Yellow, center: true);
        Text(dc, "RANK NAME   SCORE  MISSION", new Point(cx, 180), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,7}   {e.Stage,4}", new Point(cx, 226 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER", new Point(cx, 680), 28, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = ScreenWidth / 2;
        Panel(dc, new Rect(cx - 330, 140, 660, 460), 0xFF8A6A3A, 0xE0140E0A);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }

    // ───────────────────────────── emblems and advisors ─────────────────────────────

    /// <summary>A House's emblem: Aldaine's sword through the sun, Vrakos's three spikes on a shield, Sythe's eye in
    /// the crescent moon, the Sovereign's star.</summary>
    private static void DrawEmblem(DrawingContext dc, Faction f, Point c, double r)
    {
        uint col = Rules.Colour(f), dark = Shade(col, 0.45), gold = 0xFFE8C060;
        dc.FillEllipse(c.X, c.Y, r, dark);
        dc.DrawEllipse(null, Pen(gold, Math.Max(1.5, r / 12)), c, r, r);
        double s = r / 10;
        switch (f)
        {
            case Faction.Aldaine:
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6;
                    dc.DrawLine(Pen(gold, s * 0.8), new Point(c.X + Math.Cos(a) * 4.5 * s, c.Y + Math.Sin(a) * 4.5 * s), new Point(c.X + Math.Cos(a) * 7.5 * s, c.Y + Math.Sin(a) * 7.5 * s));
                }
                dc.FillEllipse(c.X, c.Y, 4 * s, col);
                Poly(dc, 0xFFE8E4DA, null, 0, new Point(c.X - s, c.Y - 8 * s), new Point(c.X + s, c.Y - 8 * s), new Point(c.X + s, c.Y + 6 * s), new Point(c.X, c.Y + 8.5 * s), new Point(c.X - s, c.Y + 6 * s));
                dc.FillRectangle(Brush(gold), new Rect(c.X - 3.5 * s, c.Y - 5 * s, 7 * s, 1.6 * s));
                break;
            case Faction.Vrakos:
                Poly(dc, col, gold, s * 0.6, new Point(c.X - 6 * s, c.Y - 3 * s), new Point(c.X + 6 * s, c.Y - 3 * s), new Point(c.X + 5 * s, c.Y + 3 * s), new Point(c.X, c.Y + 7.5 * s), new Point(c.X - 5 * s, c.Y + 3 * s));
                for (int i = -1; i <= 1; i++)
                    Poly(dc, 0xFFE8E4DA, null, 0, new Point(c.X + i * 3.5 * s - 1.4 * s, c.Y - 2 * s), new Point(c.X + i * 3.5 * s, c.Y - (i == 0 ? 8.5 : 6.5) * s), new Point(c.X + i * 3.5 * s + 1.4 * s, c.Y - 2 * s));
                dc.FillEllipse(c.X, c.Y + 1.5 * s, 1.6 * s, 0xFF1A0A08);
                break;
            case Faction.Sythe:
                dc.FillEllipse(c.X - 1.2 * s, c.Y, 6.5 * s, 0xFFE8E4DA);
                dc.FillEllipse(c.X + 1.5 * s, c.Y - 0.8 * s, 6 * s, dark);
                dc.DrawEllipse(Brush(0xFFE8E4DA), null, new Point(c.X + 1.2 * s, c.Y), 3 * s, 1.7 * s);
                dc.FillEllipse(c.X + 1.2 * s, c.Y, 1.3 * s, col);
                break;
            default:
                Poly(dc, gold, null, 0, new Point(c.X, c.Y - 8 * s), new Point(c.X + 2 * s, c.Y - 2 * s), new Point(c.X + 8 * s, c.Y), new Point(c.X + 2 * s, c.Y + 2 * s),
                    new Point(c.X, c.Y + 8 * s), new Point(c.X - 2 * s, c.Y + 2 * s), new Point(c.X - 8 * s, c.Y), new Point(c.X - 2 * s, c.Y - 2 * s));
                break;
        }
    }

    /// <summary>A House's advisor (see <see cref="Portraits"/>), in a frame: they blink, and their mouths move while
    /// they speak; Gresh's eyepiece glows.</summary>
    private void DrawPortrait(DrawingContext dc, Faction f, Rect r, bool talking)
    {
        double s = Math.Min(r.Width / Portraits.W, r.Height / Portraits.H);
        var dst = new Rect(r.Center.X - Portraits.W * s / 2, r.Center.Y - Portraits.H * s / 2, Portraits.W * s, Portraits.H * s);
        dc.FillRectangle(Brush(Shade(Rules.Colour(f), 0.12)), r);
        dc.DrawImage(Portraits.Get(f), dst);
        Point P(double x, double y) => new(dst.X + x * s, dst.Y + y * s);
        uint skin = Portraits.SkinOf(f);
        bool blink = (_clock + (int)f * 1.3) % 4.2 < 0.13;
        if (blink)
            foreach (int side in new[] { -1, 1 })
            {
                if (f == Faction.Vrakos && side > 0) continue;
                var e = P(Portraits.Cx + side * Portraits.EyeDx, Portraits.EyeY);
                dc.DrawEllipse(Brush(Shade(skin, 0.8)), null, e, 8.5 * s, 5 * s);
                dc.DrawLine(Pen(0xFF3A2418, 1.2 * s), new Point(e.X - 8 * s, e.Y + 1 * s), new Point(e.X + 8 * s, e.Y + 1 * s));
            }
        if (talking)
        {
            double open = Math.Abs(Math.Sin(_clock * 11)) * (0.6 + 0.4 * Math.Sin(_clock * 3.1));
            if (open > 0.1)
            {
                var m = P(Portraits.Cx, Portraits.MouthY + 1.5);
                dc.DrawEllipse(Brush(0xFF3A1410), null, m, 7 * s, (1 + open * 4) * s);
                dc.FillRectangle(Brush(0xFFE8E0D0), new Rect(m.X - 5 * s, m.Y - (1 + open * 4) * s + 0.5 * s, 10 * s, 1.4 * s));
            }
        }
        if (f == Faction.Vrakos)
        {
            var e = P(Portraits.Cx + Portraits.EyeDx + 1, Portraits.EyeY);
            double glow = 0.6 + 0.4 * Math.Sin(_clock * 3);
            dc.FillEllipse(e.X, e.Y, 7 * s, Alpha(0xFFFF2010, 0.3 * glow));
            dc.FillEllipse(e.X, e.Y, 3.2 * s, Alpha(0xFFFF4020, 0.6 + 0.4 * glow));
            dc.FillEllipse(e.X - 1 * s, e.Y - 1 * s, 1 * s, 0xFFFFD0C0);
        }
        dc.DrawRectangle(null, Pen(0xFF000000, 2), r);
    }
}
