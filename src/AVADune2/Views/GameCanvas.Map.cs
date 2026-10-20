using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVADune2.Engine;

namespace AVADune2.Views;

public sealed partial class GameCanvas
{
    private const int T = Field.Tile;
    private static readonly Rect MapRect = new(0, Field.TopBar, Field.SideX, Field.Height - Field.TopBar);
    private static readonly RenderOptions Crisp = new() { BitmapInterpolationMode = BitmapInterpolationMode.None };
    private static readonly RenderOptions Smooth = new() { BitmapInterpolationMode = BitmapInterpolationMode.LowQuality };

    /// <summary>A point of the map on the screen.</summary>
    private Point S(double x, double y) => new((x - Engine.CamX) * T, Field.TopBar + (y - Engine.CamY) * T);

    private bool OnScreen(double x, double y, double margin = 2) =>
        x >= Engine.CamX - margin && x <= Engine.CamX + Field.ViewW + margin && y >= Engine.CamY - margin && y <= Engine.CamY + Field.ViewH + margin;

    private void DrawBattle(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        if (w.Houses.Count == 0) { DrawBackdrop(dc); return; }
        Painter.Update(w);
        Painter.UpdateShroud(w);
        Painter.UpdateRadar(w);
        using (dc.PushClip(MapRect))
        {
            var src = new Rect(g.CamX * T, g.CamY * T, Field.ViewW * T, Field.ViewH * T);
            using (dc.PushRenderOptions(Crisp)) dc.DrawImage(Painter.Map, src, MapRect);
            DrawWorld(dc, w);
            using (dc.PushRenderOptions(Smooth))
                dc.DrawImage(Painter.Shroud, new Rect(g.CamX * MapPainter.ShroudScale, g.CamY * MapPainter.ShroudScale, Field.ViewW * MapPainter.ShroudScale, Field.ViewH * MapPainter.ShroudScale), MapRect);
            DrawOverlays(dc, w);
            if (g.Phase != Phase.Play && g.State != GameState.Attract)
            {
                bool won = g.Phase == Phase.Won;
                dc.FillRectangle(Brush(0xB0000000), new Rect(0, 330, Field.SideX, 90));
                Text(dc, won ? "MISSION ACCOMPLISHED" : "MISSION FAILED", new Point(Field.SideX / 2, 375), 64, won ? Yellow : Red, center: true);
            }
        }
        DrawTopBar(dc, w);
        DrawSidebar(dc, w);
    }

    /// <summary>The buildings, then the units on the ground back to front, the sandmaws, shots and explosions, and the
    /// skylifters over everything.</summary>
    private void DrawWorld(DrawingContext dc, World w)
    {
        foreach (var b in w.Buildings)
            if (!b.Dead && OnScreen(b.CX, b.CY, 4)) DrawBuilding(dc, w, b);
        foreach (var c in w.Creatures)
            if (c.Dormant <= 0 && !c.Surfaced && OnScreen(c.X, c.Y) && Sim.Visible(w, c.X, c.Y)) DrawWake(dc, c);
        var ground = w.Units.Where(u => !u.Dead && u.OnGround && OnScreen(u.X, u.Y) && (u.Owner == World.Player || Sim.Visible(w, u.X, u.Y))).OrderBy(u => u.Y);
        foreach (var u in ground) DrawUnit(dc, w, u);
        foreach (var c in w.Creatures)
            if (c.Dormant <= 0 && c.Surfaced && OnScreen(c.X, c.Y)) DrawCreature(dc, c);
        foreach (var s in w.Shots)
            if (OnScreen(s.X, s.Y)) DrawShot(dc, s);
        foreach (var e in w.Effects)
            if (OnScreen(e.X, e.Y, 4)) DrawEffect(dc, e);
        foreach (var u in w.Units)
            if (!u.Dead && u.InAir && OnScreen(u.X, u.Y) && (u.Owner == World.Player || Sim.Visible(w, u.X, u.Y))) DrawUnit(dc, w, u);
    }

    // ───────────────────────────── buildings ─────────────────────────────

    private static int WallMask(World w, Building b)
    {
        bool Wall(int x, int y) => w.BuildingAt(x, y) is { Type: Kind.Wall } o && o.Owner == b.Owner;
        return (Wall(b.X + 1, b.Y) ? 1 : 0) | (Wall(b.X - 1, b.Y) ? 2 : 0) | (Wall(b.X, b.Y + 1) ? 4 : 0) | (Wall(b.X, b.Y - 1) ? 8 : 0);
    }

    private void DrawBuilding(DrawingContext dc, World w, Building b, double alpha = 1)
    {
        var h = w.Houses[b.Owner];
        var at = S(b.X, b.Y);
        var sprite = BuildingArt.Get(b.Type, h.Colour, b.Type == Kind.Wall ? WallMask(w, b) : 0);
        var dst = new Rect(at.X, at.Y - BuildingArt.Over, b.W * T, b.H * T + BuildingArt.Over);
        using (dc.PushOpacity(alpha))
        using (dc.PushRenderOptions(Crisp))
            dc.DrawImage(sprite, dst);
        double x = at.X, y = at.Y;
        uint hc = h.Colour;
        switch (b.Type)
        {
            case Kind.Turbine:
            {
                // The rotor turns with the wind (slower when damaged).
                double spin = _clock * 7 * (0.4 + 0.6 * b.Hp / b.MaxHp) + b.Id;
                var c = new Point(x + 32, y + 26);
                for (int i = 0; i < 3; i++)
                {
                    double a = spin + i * Math.PI * 2 / 3;
                    var tip = new Point(c.X + Math.Cos(a) * 19, c.Y + Math.Sin(a) * 19);
                    var side = new Point(c.X + Math.Cos(a + 0.35) * 12, c.Y + Math.Sin(a + 0.35) * 12);
                    Poly(dc, 0xFFD8D2C4, 0xFF6A6458, 1, c, side, tip);
                }
                dc.FillEllipse(c.X, c.Y, 5, hc);
                dc.FillEllipse(c.X - 1.5, c.Y - 1.5, 1.5, 0xA0FFFFFF);
                break;
            }
            case Kind.Outpost:
            {
                double a = h.Radar ? _clock * 2.2 : 0.6;
                var c = new Point(x + 26, y + 6);
                double sx = Math.Cos(a) * 14, sy = Math.Sin(a) * 6;
                dc.DrawLine(Pen(0xFF3A3632, 3), new Point(c.X, c.Y + 8), c);
                dc.DrawEllipse(Brush(0xFFD8D4CA), Pen(0xFF5A564E, 1.5), c, Math.Abs(sx) + 3, 6 + Math.Abs(sy) * 0.3);
                dc.DrawLine(Pen(0xFF5A564E, 2), c, new Point(c.X + sx * 0.6, c.Y - 4));
                if (h.Radar && (int)(_clock * 2) % 2 == 0) dc.FillEllipse(x + 55, y - 9 + BuildingArt.Over - 14, 2.5, 0xFFFF5040);
                break;
            }
            case Kind.Turret:
            {
                var c = new Point(x + 16, y + 14);
                var tip = new Point(c.X + Math.Cos(b.Aim) * 17, c.Y + Math.Sin(b.Aim) * 17);
                dc.DrawLine(Pen(0xFF2A2622, 6), c, tip);
                dc.DrawLine(Pen(0xFF9A968C, 3), c, tip);
                dc.FillEllipse(c.X, c.Y, 7.5, 0xFF7A766C);
                dc.FillEllipse(c.X - 2, c.Y - 2, 3, 0xFFB8B4AA);
                break;
            }
            case Kind.RocketTurret:
            {
                var c = new Point(x + 16, y + 12);
                using (dc.PushTransform(Matrix.CreateRotation(b.Aim) * Matrix.CreateTranslation(c.X, c.Y)))
                {
                    dc.FillRectangle(Brush(0xFF5A564E), new Rect(-8, -8, 18, 16), 2);
                    dc.FillRectangle(Brush(0xFF8A867C), new Rect(-7, -7, 16, 14), 2);
                    for (int i = 0; i < 2; i++)
                    for (int j = 0; j < 2; j++)
                        dc.FillEllipse(6, -4 + j * 8, 2.6, i == 0 ? 0xFF2A2622 : 0xFF2A2622);
                    dc.FillRectangle(Brush(hc), new Rect(-7, -1.5, 10, 3));
                }
                break;
            }
            case Kind.Barracks:
            {
                double wave = Math.Sin(_clock * 6 + b.Id) * 2;
                Poly(dc, hc, Shade(hc, 0.6), 1, new Point(x + 56, y - 8), new Point(x + 70, y - 6 + wave), new Point(x + 56, y - 1));
                break;
            }
            case Kind.Refinery:
                if (b.Docked is not null)
                    dc.FillRectangle(Brush(Alpha(0xFFFF9A30, 0.25 + 0.2 * Math.Sin(_clock * 8))), new Rect(x + 38, y + 50, 20, 12));
                break;
            case Kind.Starport:
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6;
                    if ((int)(_clock * 4 + i) % 12 == 0) dc.FillEllipse(x + 48 + Math.Cos(a) * 38.5, y + 50 + Math.Sin(a) * 38.5, 4, 0xC0FFF0A0);
                }
                if (h.Frigate > 0) Text(dc, $"{h.Frigate:0}", new Point(x + 48, y + 50), 24, Yellow, center: true);
                break;
            case Kind.Palace:
                if (h.PalaceReady && (int)(_clock * 3) % 2 == 0) dc.FillEllipse(x + 48, y + 36, 6, 0xC0FFF0A0);
                break;
            case Kind.Yard:
                if (b.Queue.Count > 0 && (int)(_clock * 3) % 2 == 0) dc.FillEllipse(x + 18, y - 3, 2.5, 0xFFFFC040);
                break;
        }
        // Damage: smoke, and fire when it's bad.
        double hurt = 1 - b.Hp / b.MaxHp;
        if (hurt > 0.5)
        {
            int puffs = hurt > 0.75 ? 3 : 2;
            for (int i = 0; i < puffs; i++)
            {
                double t = (_clock * 0.6 + i / (double)puffs + (b.Id % 7) * 0.13) % 1;
                double px = x + 10 + Hash(b.Id, i, 3) % (uint)Math.Max(1, b.W * T - 20), py = y + 10 + Hash(b.Id, i, 4) % (uint)Math.Max(1, b.H * T - 20);
                if (hurt > 0.75) dc.FillEllipse(px, py, 4 + Math.Sin(_clock * 20 + i) * 1.5, 0xE0FF8A20);
                dc.FillEllipse(px + t * 6, py - t * 28, 4 + t * 8, Alpha(0xFF3A3632, 0.6 * (1 - t)));
            }
        }
        if (b.HitAgo < 0.08) dc.FillRectangle(Brush(0x50FFFFFF), new Rect(x, y, b.W * T, b.H * T));
        if (b.Repairing && (int)(_clock * 2) % 2 == 0) DrawSpanner(dc, new Point(x + b.W * T / 2.0, y + b.H * T / 2.0));
    }

    private static void DrawSpanner(DrawingContext dc, Point c)
    {
        dc.FillEllipse(c.X, c.Y, 12, 0xC0000000);
        dc.DrawLine(Pen(0xFFFFE070, 3.5), new Point(c.X - 6, c.Y + 6), new Point(c.X + 4, c.Y - 4));
        dc.DrawEllipse(null, Pen(0xFFFFE070, 2.5), new Point(c.X + 5, c.Y - 5), 3.5, 3.5);
    }

    // ───────────────────────────── on top: selection, placing, dragging ─────────────────────────────

    private void DrawOverlays(DrawingContext dc, World w)
    {
        var g = Engine;
        foreach (var u in g.Selected)
        {
            if (u.Dead || !OnScreen(u.X, u.Y)) continue;
            var p = S(u.X, u.Y - (u.InAir ? 0.25 : 0));
            double r = Rules.IsInfantry(u.Type) ? 11 : u.Type is Kind.Juggernaut or Kind.SiegeTank or Kind.Mcv or Kind.Harvester ? 16 : 14;
            Brackets(dc, new Rect(p.X - r, p.Y - r, r * 2, r * 2), 0xFFFFFFFF);
            HealthBar(dc, new Rect(p.X - r, p.Y - r - 6, r * 2, 4), u.Hp / u.MaxHp);
            if (u.Type == Kind.Harvester) dc.FillRectangle(Brush(0xFFFF8A2A), new Rect(p.X - r, p.Y + r + 2, r * 2 * u.Cargo / Rules.HarvesterLoad, 3));
        }
        if (g.SelectedBuilding is { Dead: false } sb)
        {
            var p = S(sb.X, sb.Y);
            Brackets(dc, new Rect(p.X - 2, p.Y - 2, sb.W * T + 4, sb.H * T + 4), 0xFFFFFFFF);
            HealthBar(dc, new Rect(p.X, p.Y - 8, sb.W * T, 5), sb.Hp / sb.MaxHp);
            if (sb.RallyX >= 0)
            {
                var r = S(sb.RallyX + 0.5, sb.RallyY + 0.5);
                dc.DrawLine(Pen(0x90FFFFFF, 1.5), new Point(p.X + sb.W * T / 2.0, p.Y + sb.H * T), r);
                dc.DrawEllipse(null, Pen(Yellow, 2), r, 6, 6);
            }
        }
        if (g.Inspected is { Dead: false } ins)
        {
            var c = S(ins.CX, ins.CY);
            double r = ins is Building ib ? Math.Max(ib.W, ib.H) * T / 2.0 + 2 : 13;
            Brackets(dc, new Rect(c.X - r, c.Y - r, r * 2, r * 2), Red);
            HealthBar(dc, new Rect(c.X - r, c.Y - r - 6, r * 2, 4), ins.Hp / ins.MaxHp);
        }
        // The hovered damaged unit's health.
        if (InMap(_mouse) && g.ThingAt(g.ToMap(_mouse.X, _mouse.Y).X, g.ToMap(_mouse.X, _mouse.Y).Y) is Unit hu && !g.Selected.Contains(hu))
        {
            var p = S(hu.X, hu.Y);
            HealthBar(dc, new Rect(p.X - 12, p.Y - 18, 24, 4), hu.Hp / hu.MaxHp);
        }
        if (g.Placing && InMap(_mouse) && g.YardReady() is { Ready: { } k })
        {
            var (mx, my) = g.ToMap(_mouse.X, _mouse.Y);
            var (px, py) = g.PlacementAt(mx, my);
            var st = Rules.Of(k);
            bool ok = Sim.CanPlace(w, World.Player, k, px, py) is null;
            if (k != Kind.Slab)
            {
                var ghost = new Building { Type = k, Owner = World.Player, X = px, Y = py, W = st.W, H = st.H, Hp = st.Hp };
                DrawBuilding(dc, w, ghost, 0.55);
            }
            for (int j = 0; j < st.H; j++)
            for (int i = 0; i < st.W; i++)
            {
                int tx = px + i, ty = py + j;
                bool tile = Field.In(tx, ty) && w.Ground[tx, ty] == Ground.Rock && w.Bld[tx, ty] is null && w.Occ[tx, ty] is null;
                var at = S(tx, ty);
                dc.FillRectangle(Brush(tile && ok ? 0x5040E040u : tile ? 0x50E0C040u : 0x60E04040u), new Rect(at.X + 1, at.Y + 1, T - 2, T - 2));
            }
            var a0 = S(px, py);
            dc.DrawRectangle(null, Pen(ok ? 0xFF60FF60 : 0xFFFF5040, 2), new Rect(a0.X, a0.Y, st.W * T, st.H * T));
        }
        if (_dragFrom is { } from && Math.Abs(_mouse.X - from.X) + Math.Abs(_mouse.Y - from.Y) > 8)
        {
            var r = new Rect(from, new Point(Math.Clamp(_mouse.X, 0, Field.SideX), Math.Clamp(_mouse.Y, Field.TopBar, Field.Height))).Normalize();
            dc.FillRectangle(Brush(0x2060FF60), r);
            dc.DrawRectangle(null, Pen(0xFF80FF80, 1.5), r);
        }
        if ((g.ArmedAttack || g.ArmedPalace) && InMap(_mouse))
        {
            double r = g.ArmedPalace ? 40 : 12, pulse = 1 + 0.15 * Math.Sin(_clock * 10);
            dc.DrawEllipse(null, Pen(Red, 2), _mouse, r * pulse, r * pulse);
            dc.DrawLine(Pen(Red, 2), new Point(_mouse.X - r * 1.4, _mouse.Y), new Point(_mouse.X + r * 1.4, _mouse.Y));
            dc.DrawLine(Pen(Red, 2), new Point(_mouse.X, _mouse.Y - r * 1.4), new Point(_mouse.X, _mouse.Y + r * 1.4));
        }
    }

    private static void Brackets(DrawingContext dc, Rect r, uint colour)
    {
        double k = Math.Min(8, r.Width / 3);
        var pen = Pen(colour, 2);
        foreach (var (cx, cy, sx, sy) in new[] { (r.Left, r.Top, 1, 1), (r.Right, r.Top, -1, 1), (r.Left, r.Bottom, 1, -1), (r.Right, r.Bottom, -1, -1) })
        {
            dc.DrawLine(pen, new Point(cx, cy), new Point(cx + sx * k, cy));
            dc.DrawLine(pen, new Point(cx, cy), new Point(cx, cy + sy * k));
        }
    }

    private static void HealthBar(DrawingContext dc, Rect r, double f)
    {
        f = Math.Clamp(f, 0, 1);
        dc.FillRectangle(Brush(0xC0000000), r.Inflate(1));
        dc.FillRectangle(Brush(f > 0.6 ? 0xFF40D040 : f > 0.3 ? 0xFFE8C030 : 0xFFE03A2A), new Rect(r.X, r.Y, r.Width * f, r.Height));
    }

    // ───────────────────────────── the top bar ─────────────────────────────

    private void DrawTopBar(DrawingContext dc, World w)
    {
        var g = Engine;
        var h = w.PlayerHouse;
        dc.FillRectangle(Brush(0xFF1C150E), new Rect(0, 0, Field.Width, Field.TopBar));
        dc.DrawLine(Pen(h.Colour, 2), new Point(0, Field.TopBar - 1), new Point(Field.Width, Field.TopBar - 1));
        DrawEmblem(dc, h.Faction, new Point(18, 16), 12);
        string title = g.State == GameState.Attract ? "DEMONSTRATION" : g.Skirmish ? "SKIRMISH" : $"MISSION {g.Scenario?.Mission ?? 1}";
        Text(dc, title, new Point(36, 16), 24, Sandy);
        string goal = w.Goal == Goal.Harvest
            ? $"HARVEST {Math.Min(w.GoalCredits, (int)h.Harvested)} / {w.GoalCredits}"
            : $"ENEMY: {w.Buildings.Count(b => w.Enemies(World.Player, b.Owner) && b.Type != Kind.Wall)} BUILDINGS {w.Units.Count(u => w.Enemies(World.Player, u.Owner) && !u.InAir)} UNITS";
        Text(dc, goal, new Point(170, 16), 20, White);
        if (g.Message is { } msg)
        {
            bool flash = g.MessageTime > 3.4 && (int)(_clock * 8) % 2 == 0;
            Text(dc, msg, new Point(Field.SideX - 12, 16), msg.Length > 30 ? 19 : 22, flash ? White : Yellow, right: true);
        }
        // The credits.
        var cr = new Rect(Field.SideX + 8, 3, Field.Width - Field.SideX - 16, Field.TopBar - 6);
        dc.FillRectangle(Brush(0xFF0A0806), cr, 3);
        dc.DrawRectangle(null, Pen(0xFF8A6A3A, 1.5), cr, 3, 3);
        Text(dc, "CREDITS", new Point(cr.X + 8, cr.Center.Y), 20, Dim);
        bool full = h.Credits >= h.Capacity && h.Capacity > 0;
        Text(dc, $"{(int)g.ShownCredits}", new Point(cr.Right - 8, cr.Center.Y), 28, full && (int)(_clock * 3) % 2 == 0 ? Red : Orange, right: true);
    }
}
