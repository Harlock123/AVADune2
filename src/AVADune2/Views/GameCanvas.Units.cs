using Avalonia;
using Avalonia.Media;
using AVADune2.Engine;
using Effect = AVADune2.Engine.Effect;

namespace AVADune2.Views;

public sealed partial class GameCanvas
{
    private const uint Track = 0xFF2C2824, TrackLight = 0xFF4A443C, Skin = 0xFFE0B48A, Steel = 0xFF9A968C, Dark = 0xFF2A2622;

    private void DrawUnit(DrawingContext dc, World w, Unit u)
    {
        var p = S(u.X, u.Y);
        uint c = w.Houses[u.Owner].Colour;
        if (u.InAir)
        {
            // A shadow on the ground below and behind.
            using (dc.PushTransform(Matrix.CreateRotation(u.Facing) * Matrix.CreateTranslation(p.X + 10, p.Y + 14)))
                Plane(dc, 0x50000000, 0x50000000, shadow: true);
            if (u.Carrying is { } load)
                DrawBody(dc, load, w.Houses[load.Owner].Colour, new Point(p.X, p.Y + 2), 0.8);
            using (dc.PushTransform(Matrix.CreateRotation(u.Facing) * Matrix.CreateTranslation(p.X, p.Y - 8)))
                Plane(dc, c, Shade(c, 0.7), shadow: false);
            return;
        }
        // The harvester's spray of sand as it works.
        if (u.Type == Kind.Harvester && u.Order == Order.Harvest && !u.Moving && w.Ore[u.Tx, u.Ty] > 0)
            for (int i = 0; i < 6; i++)
            {
                double t = (_clock * 2.2 + i / 6.0) % 1, a = u.Facing + (Hash(u.Id, i, 9) % 100 - 50) / 70.0;
                dc.FillEllipse(p.X + Math.Cos(a) * (12 + t * 14), p.Y + Math.Sin(a) * (12 + t * 14) - t * 6, 1.5 + t * 2.5, Alpha(0xFFE8A050, 0.8 * (1 - t)));
            }
        DrawBody(dc, u, c, p, Rules.IsInfantry(u.Type) ? 1.1 : 1.18);
        if (u.Turned > 0)
            dc.DrawEllipse(null, Pen(Alpha(0xFF60F070, 0.5 + 0.4 * Math.Sin(_clock * 8)), 2), p, 15, 15);
        if (u.HitAgo < 0.07) dc.FillEllipse(p.X, p.Y, 10, 0x60FFFFFF);
    }

    /// <summary>A unit at a point: its hull along its heading, its turret along its aim.</summary>
    private void DrawBody(DrawingContext dc, Unit u, uint c, Point p, double scale)
    {
        using (dc.PushTransform(Matrix.CreateRotation(u.Facing) * Matrix.CreateTranslation(p.X, p.Y)))
            Body(dc, u.Type, c, scale, u.Aim - u.Facing, _clock, u.Moving || u.Type == Kind.Harvester && u.Cargo > 0 && !u.Moving, u.Id, u.Hp / u.MaxHp, u.Cargo / Rules.HarvesterLoad);
    }

    private static readonly Dictionary<(double, int), Pen> TreadPens = [];

    /// <summary>The marks on a track, as one dashed line (a mark every four pixels), shifted as the track turns.</summary>
    private static Pen Tread(double width, int phase)
    {
        if (TreadPens.TryGetValue((width, phase), out var pen)) return pen;
        var dash = new DashStyle([1 / width, 3 / width], phase / width);
        pen = new Pen(Brush(TrackLight), width, dash, PenLineCap.Flat);
        TreadPens[(width, phase)] = pen;
        return pen;
    }

    /// <summary>
    /// A unit drawn facing along +x at the origin: its hull (part 1), its turret turned by <paramref name="turret"/>
    /// (part 2), or both (0, for the menu's icons). <paramref name="t"/> is the moment for the moving parts: the
    /// tracks, a soldier's stride, the harvester's drum.
    /// </summary>
    private void Body(DrawingContext dc, Kind k, uint c, double scale, double turret, double t, bool moving, int id, double health, double cargo, int part = 0)
    {
        using var _ = dc.PushTransform(Matrix.CreateScale(scale, scale));
        bool hull = part != 2, top = part != 1;
        double tread = moving ? t * 24 % 4 : 0;
        uint cd = Shade(c, 0.62), cl = Shade(c, 1.25);
        void Tracks(double len, double width, double gap)
        {
            if (!hull) return;
            foreach (double side in new[] { -1.0, 1.0 })
            {
                var r = new Rect(-len / 2, side < 0 ? -gap - width : gap, len, width);
                dc.FillRectangle(Brush(Track), r, 2);
                dc.DrawLine(Tread(width - 2, (int)tread), new Point(r.Left, r.Center.Y), new Point(r.Right, r.Center.Y));
            }
        }
        void Hull(double len, double width, uint col)
        {
            if (!hull) return;
            dc.FillRectangle(Brush(0x40000000), new Rect(-len / 2 + 2, -width / 2 + 2, len, width), 3);
            dc.FillRectangle(Brush(Shade(col, 0.8)), new Rect(-len / 2, -width / 2, len, width), 3);
            dc.FillRectangle(Brush(col), new Rect(-len / 2 + 1.5, -width / 2 + 1.5, len - 3, width - 3), 2);
            dc.DrawLine(Pen(Shade(col, 1.3), 1.5), new Point(-len / 2 + 2, -width / 2 + 2), new Point(len / 2 - 2, -width / 2 + 2));
        }
        void Turret(double r, double barrel, double bw, bool twin = false)
        {
            if (!top) return;
            using var rot = dc.PushTransform(Matrix.CreateRotation(turret));
            if (twin)
            {
                dc.FillRectangle(Brush(Dark), new Rect(0, -3.5, barrel, 2.6));
                dc.FillRectangle(Brush(Dark), new Rect(0, 1, barrel, 2.6));
            }
            else
            {
                dc.FillRectangle(Brush(Dark), new Rect(0, -bw / 2, barrel, bw));
                dc.FillRectangle(Brush(Steel), new Rect(1, -bw / 2 + 0.7, barrel - 2, bw * 0.35));
            }
            dc.FillEllipse(0.5, 0.5, r, 0x50000000);
            dc.FillEllipse(0, 0, r, cd);
            dc.FillEllipse(-0.6, -0.6, r - 1.6, c);
            dc.FillEllipse(-r * 0.35, -r * 0.35, r * 0.3, 0x70FFFFFF);
        }
        void Soldier(double x, double y, uint body, uint head, bool tube, double phase)
        {
            double bob = moving ? Math.Sin(t * 14 + phase) * 1.2 : 0;
            dc.FillEllipse(x + 1, y + 1.5, 3.4, 0x40000000);
            dc.DrawLine(Pen(Dark, tube ? 2.6 : 1.6), new Point(x, y + 1), new Point(x + (tube ? 7 : 6.5), y + 1));
            dc.FillEllipse(x + bob * 0.3, y, 3.2, body);
            dc.FillEllipse(x + 0.4 + bob * 0.3, y - 0.2, 2, head);
        }
        switch (k)
        {
            case Kind.Infantry or Kind.Troopers or Kind.Nomads or Kind.Praetors:
            {
                int n = Math.Clamp((int)Math.Ceiling(health * 3), 1, 3);
                uint body = k == Kind.Nomads ? 0xFFC8A878 : k == Kind.Praetors ? 0xFF6A3A90 : c;
                uint head = k == Kind.Nomads ? 0xFF8A6A48 : k == Kind.Praetors ? 0xFFE0C050 : k == Kind.Troopers ? Shade(c, 0.55) : Skin;
                (double X, double Y)[] at = [(3, 0), (-4, -5), (-4, 5)];
                for (int i = 0; i < n; i++) Soldier(at[i].X, at[i].Y, body, head, k == Kind.Troopers, i * 2.1 + id);
                if (k == Kind.Nomads) for (int i = 0; i < n; i++) dc.FillRectangle(Brush(c), new Rect(at[i].X - 1, at[i].Y - 3, 2, 6));
                break;
            }
            case Kind.Saboteur:
                Soldier(0, 0, 0xFF2A2A30, 0xFF1A1A1E, false, id);
                dc.FillRectangle(Brush(0xFF8A2A20), new Rect(-5, -2.5, 3, 5));
                break;
            case Kind.Trike:
                dc.FillRectangle(Brush(Track), new Rect(-8, -6.5, 5, 3), 1.5);
                dc.FillRectangle(Brush(Track), new Rect(-8, 3.5, 5, 3), 1.5);
                dc.FillRectangle(Brush(Track), new Rect(5, -1.5, 5, 3), 1.5);
                Hull(14, 6, c);
                dc.FillEllipse(-1, 0, 2.6, Skin);
                dc.DrawLine(Pen(Dark, 1.8), new Point(2, 0), new Point(11, 0));
                break;
            case Kind.Quad:
                foreach (var (x, y) in new[] { (-7.0, -7.0), (-7.0, 4.0), (4.0, -7.0), (4.0, 4.0) }) dc.FillRectangle(Brush(Track), new Rect(x, y, 5, 3.2), 1.5);
                Hull(16, 10, c);
                dc.DrawLine(Pen(Dark, 1.8), new Point(3, -2), new Point(12, -2));
                dc.DrawLine(Pen(Dark, 1.8), new Point(3, 2), new Point(12, 2));
                dc.FillEllipse(-2, 0, 2.6, Skin);
                break;
            case Kind.Tank:
                Tracks(24, 5, 5);
                Hull(20, 11, c);
                Turret(5.5, 14, 3);
                break;
            case Kind.RocketTank:
                Tracks(24, 5, 5);
                Hull(20, 11, c);
                if (top)
                    using (dc.PushTransform(Matrix.CreateRotation(turret)))
                    {
                        dc.FillRectangle(Brush(cd), new Rect(-6, -6, 13, 12), 2);
                        dc.FillRectangle(Brush(Shade(c, 0.9)), new Rect(-5, -5, 11, 10), 2);
                        for (int i = 0; i < 2; i++)
                        {
                            dc.FillRectangle(Brush(0xFFD8D4CA), new Rect(1, -4 + i * 5, 9, 3), 1.5);
                            dc.FillEllipse(10, -2.5 + i * 5, 1.6, 0xFFD8402A);
                        }
                    }
                break;
            case Kind.SiegeTank:
                Tracks(28, 6, 6);
                Hull(24, 13, c);
                Turret(7, 19, 3, twin: true);
                break;
            case Kind.Juggernaut:
                Tracks(32, 7, 7);
                Hull(28, 15, c);
                if (hull) dc.FillRectangle(Brush(cd), new Rect(-12, -9, 6, 18), 2);
                Turret(8.5, 20, 3, twin: true);
                break;
            case Kind.Resonator:
                Tracks(24, 5, 5);
                Hull(20, 11, c);
                if (top)
                    using (dc.PushTransform(Matrix.CreateRotation(turret)))
                    {
                        Poly(dc, 0xFFC8D0D8, 0xFF5A6068, 1, new Point(-3, -2), new Point(9, -7), new Point(11, 0), new Point(9, 7), new Point(-3, 2));
                        for (int i = 0; i < 3; i++) dc.DrawLine(Pen(0xFF6A8AA8, 1), new Point(4 + i * 2, -4 + i * 0.7), new Point(4 + i * 2, 4 - i * 0.7));
                        dc.FillEllipse(-2, 0, 3.5, cl);
                    }
                break;
            case Kind.Beguiler:
                Tracks(24, 5, 5);
                Hull(20, 11, c);
                if (top)
                    using (dc.PushTransform(Matrix.CreateRotation(turret)))
                    {
                        dc.FillRectangle(Brush(cd), new Rect(-6, -4, 11, 8), 2);
                        dc.FillRectangle(Brush(0xFFD8D4CA), new Rect(-2, -1.6, 12, 3.2), 1.5);
                        dc.FillEllipse(10, 0, 2.4, 0xFF60E070);
                    }
                break;
            case Kind.Harvester:
            {
                Tracks(26, 6, 6);
                dc.FillRectangle(Brush(0x40000000), new Rect(-11, -8, 24, 18), 3);
                dc.FillRectangle(Brush(0xFF9A7A34), new Rect(-13, -9, 24, 18), 3);
                dc.FillRectangle(Brush(0xFFC8A040), new Rect(-12, -8, 22, 16), 2);
                dc.FillRectangle(Brush(c), new Rect(-12, -8, 4, 16));
                // The hopper, filling with orange.
                dc.FillRectangle(Brush(0xFF3A3228), new Rect(-7, -5.5, 11, 11), 1.5);
                double fill = Math.Clamp(cargo, 0, 1);
                dc.FillRectangle(Brush(0xFFE0682A), new Rect(-6.5, 5 - 10 * fill, 10, 10 * fill));
                // The spinning drum at the front.
                dc.FillRectangle(Brush(0xFF5A5246), new Rect(10, -9, 5, 18), 2);
                double spin = moving ? t * 30 % 4 : 0;
                for (double y = -8 + spin; y < 9; y += 4) dc.DrawLine(Pen(0xFF9A9284, 1.2), new Point(10.5, y), new Point(14.5, y));
                break;
            }
            case Kind.Mcv:
                Tracks(30, 6, 6);
                Hull(28, 14, Shade(c, 0.9));
                dc.FillRectangle(Brush(0xFF8A9AA8), new Rect(9, -6, 5, 12), 1.5);
                dc.FillRectangle(Brush(0xFFE2B42A), new Rect(-12, -2, 20, 4));
                dc.FillRectangle(Brush(0xFF7A6430), new Rect(-12, -5, 5, 10), 1.5);
                break;
            case Kind.Skylifter:
                Plane(dc, c, cd, shadow: false);
                break;
        }
    }

    /// <summary>A skylifter: a long body, broad wings with an engine pod on each, and a twin tail.</summary>
    private void Plane(DrawingContext dc, uint c, uint cd, bool shadow)
    {
        uint body = shadow ? c : 0xFFB8B4AA;
        Poly(dc, shadow ? c : cd, null, 0, new Point(-3, -16), new Point(5, -16), new Point(6, 16), new Point(-3, 16));
        Poly(dc, shadow ? c : c, null, 0, new Point(-2, -15), new Point(4, -15), new Point(5, 15), new Point(-2, 15));
        dc.FillRectangle(Brush(body), new Rect(-15, -3.5, 30, 7), 3.5);
        Poly(dc, body, null, 0, new Point(-15, -3), new Point(-19, -8), new Point(-16, -8), new Point(-11, -3));
        Poly(dc, body, null, 0, new Point(-15, 3), new Point(-19, 8), new Point(-16, 8), new Point(-11, 3));
        if (shadow) return;
        dc.FillEllipse(1, -12, 3, 0xFF5A564E);
        dc.FillEllipse(1, 12, 3, 0xFF5A564E);
        double blur = _clock * 40;
        dc.DrawLine(Pen(0x90D8D8D8, 1.5), new Point(1 + Math.Cos(blur) * 6, -12 + Math.Sin(blur) * 2), new Point(1 - Math.Cos(blur) * 6, -12 - Math.Sin(blur) * 2));
        dc.DrawLine(Pen(0x90D8D8D8, 1.5), new Point(1 + Math.Cos(blur) * 6, 12 + Math.Sin(blur) * 2), new Point(1 - Math.Cos(blur) * 6, 12 - Math.Sin(blur) * 2));
        dc.FillEllipse(11, 0, 3, 0xFF4A7A9A);
    }

    // ───────────────────────────── shots and explosions ─────────────────────────────

    private void DrawShot(DrawingContext dc, Shot s)
    {
        var p = S(s.X, s.Y);
        double a = Math.Atan2(s.Ty - s.Sy, s.Tx - s.Sx);
        switch (s.Weapon)
        {
            case Weapon.Cannon:
                dc.FillEllipse(p.X, p.Y, 4, 0x60FFB040);
                dc.FillEllipse(p.X, p.Y, 2, 0xFFFFF0C0);
                break;
            case Weapon.Rocket or Weapon.Missile:
            {
                double len = s.Weapon == Weapon.Missile ? 14 : 7, wdt = s.Weapon == Weapon.Missile ? 4 : 2.2;
                var back = new Point(p.X - Math.Cos(a) * len, p.Y - Math.Sin(a) * len);
                dc.FillEllipse(back.X, back.Y, wdt * 1.4 + Math.Sin(_clock * 40) * 0.8, 0xE0FFA030);
                dc.DrawLine(Pen(0xFFD8D4CA, wdt), back, p);
                dc.FillEllipse(p.X, p.Y, wdt * 0.6, 0xFFD83A2A);
                break;
            }
            case Weapon.Deviator:
                dc.FillEllipse(p.X, p.Y, 5, 0x6060F070);
                dc.FillEllipse(p.X, p.Y, 2.5, 0xFFB0FFB0);
                break;
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        var p = S(e.X, e.Y);
        double k = e.T / e.Life, a = 1 - k;
        switch (e.Kind)
        {
            case Fx.Boom:
            case Fx.BigBoom:
            {
                double size = e.Kind == Fx.BigBoom ? 1.9 : e.Colour > 0 ? Math.Clamp(e.Colour / 60.0, 0.5, 1.6) : 1;
                double r = (6 + 16 * Math.Sqrt(k)) * size;
                dc.FillEllipse(p.X + 4 * k * size, p.Y - 10 * k * size, r * 1.2, Alpha(0xFF3A3430, 0.55 * a));
                if (k < 0.6)
                {
                    double f = 1 - k / 0.6;
                    dc.FillEllipse(p.X, p.Y, r, Alpha(0xFFE8501E, f));
                    dc.FillEllipse(p.X - r * 0.15, p.Y - r * 0.15, r * 0.65, Alpha(0xFFFFB030, f));
                    dc.FillEllipse(p.X - r * 0.2, p.Y - r * 0.2, r * 0.3, Alpha(0xFFFFF4C0, f));
                }
                if (e.Kind == Fx.BigBoom)
                    for (int i = 0; i < 8; i++)
                    {
                        double ang = Hash((int)(e.X * 10), i, 7) % 628 / 100.0, d = k * (20 + Hash((int)(e.Y * 10), i, 8) % 26);
                        dc.FillEllipse(p.X + Math.Cos(ang) * d, p.Y + Math.Sin(ang) * d - k * 10, 2, Alpha(0xFF4A3A2A, a));
                    }
                break;
            }
            case Fx.Flash:
                dc.FillEllipse(p.X, p.Y, 5 * a + 2, Alpha(0xFFFFF0A0, a));
                break;
            case Fx.Tracer:
                dc.DrawLine(Pen(Alpha(0xFFFFE070, a), 1.5), p, S(e.X2, e.Y2));
                dc.FillEllipse(S(e.X2, e.Y2).X, S(e.X2, e.Y2).Y, 2.5, Alpha(0xFFFFF0C0, a));
                break;
            case Fx.Sonic:
            {
                var q = S(e.X2, e.Y2);
                double ang = Math.Atan2(q.Y - p.Y, q.X - p.X), len = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                using (dc.PushTransform(Matrix.CreateRotation(ang) * Matrix.CreateTranslation(p.X, p.Y)))
                {
                    double front = len * Math.Min(1, k * 2.2);
                    for (int i = 0; i < 7; i++)
                    {
                        double x = front - i * 9;
                        if (x < 0) break;
                        double h = 6 + x / len * 10;
                        var g = new StreamGeometry();
                        using (var ctx = g.Open())
                        {
                            ctx.BeginFigure(new Point(x - 3, -h), false);
                            ctx.QuadraticBezierTo(new Point(x + 5, 0), new Point(x - 3, h));
                        }
                        dc.DrawGeometry(null, Pen(Alpha(0xFFB0F0FF, a * (1 - i / 7.0)), 2.5), g);
                    }
                }
                break;
            }
            case Fx.Gas:
                for (int i = 0; i < 6; i++)
                {
                    double ang = i * Math.PI / 3 + k, d = 6 + 18 * k;
                    dc.FillEllipse(p.X + Math.Cos(ang) * d, p.Y + Math.Sin(ang) * d, 8 + 8 * k, Alpha(0xFF60D060, 0.45 * a));
                }
                break;
            case Fx.Swallow:
            {
                double r = 12 + 22 * k;
                dc.DrawEllipse(null, Pen(Alpha(0xFFE8C080, a), 6 * a + 1), p, r, r * 0.8);
                for (int i = 0; i < 14; i++)
                {
                    double ang = i * Math.PI / 7 + Hash(i, (int)e.X, 3) % 100 / 100.0, d = r * (0.9 + Hash(i, (int)e.Y, 4) % 50 / 100.0);
                    dc.FillEllipse(p.X + Math.Cos(ang) * d, p.Y + Math.Sin(ang) * d * 0.8 - Math.Sin(k * Math.PI) * 16, 2.5, Alpha(0xFFD8A060, a));
                }
                break;
            }
            case Fx.Bloom:
                for (int i = 0; i < 20; i++)
                {
                    double ang = i * Math.PI / 10 + Hash(i, 3, 9) % 100 / 160.0, d = k * (24 + Hash(i, 4, 9) % 40);
                    dc.FillEllipse(p.X + Math.Cos(ang) * d, p.Y + Math.Sin(ang) * d - Math.Sin(k * Math.PI) * 20, 3 * a + 1, Alpha(i % 3 == 0 ? 0xFFFFD27A : 0xFFE0602A, a));
                }
                dc.FillEllipse(p.X, p.Y, 20 * (1 - k) + 4, Alpha(0xFFFF9A40, a * 0.6));
                break;
            case Fx.Smoke:
                dc.FillEllipse(p.X, p.Y - k * 6, 2.5 + k * 6, Alpha(0xFFB8B0A6, 0.5 * a));
                break;
            case Fx.Spark:
                for (int i = 0; i < 5; i++)
                {
                    double ang = i * 1.3 + e.X, d = 3 + k * 12;
                    dc.FillEllipse(p.X + Math.Cos(ang) * d, p.Y + Math.Sin(ang) * d, 1.4, Alpha(0xFFFFE8A0, a));
                }
                break;
            case Fx.Crush:
                dc.FillEllipse(p.X, p.Y, 6 + 6 * k, Alpha(0xFFB89868, 0.6 * a));
                break;
            case Fx.Deliver:
                dc.FillRectangle(Brush(Alpha(0xFFFFF4C0, 0.35 * a)), new Rect(p.X - 18 * a, p.Y - 400, 36 * a, 400));
                dc.DrawEllipse(null, Pen(Alpha(0xFFFFF4C0, a), 3), p, 10 + 30 * k, (10 + 30 * k) * 0.6);
                break;
            case Fx.Turned:
                dc.DrawEllipse(null, Pen(Alpha(0xFF60F070, a), 3), p, 8 + 16 * k, 8 + 16 * k);
                break;
        }
    }

    // ───────────────────────────── sandmaws ─────────────────────────────

    /// <summary>A sandmaw deep under the sand: just the wake it leaves, a line of humps and ripples.</summary>
    private void DrawWake(DrawingContext dc, Creature c)
    {
        var p = S(c.X, c.Y);
        for (int i = 0; i < 6; i++)
        {
            double d = i * 9, x = p.X - Math.Cos(c.Dir) * d, y = p.Y - Math.Sin(c.Dir) * d;
            double r = 9 - i * 1.1 + Math.Sin(_clock * 5 + i) * 1.2;
            dc.FillEllipse(x + 2, y + 2, r, r * 0.7, Alpha(0xFF6A4A26, 0.35 * (1 - i / 6.0)));
            dc.FillEllipse(x - 1, y - 1, r * 0.8, r * 0.55, Alpha(0xFFF4D49A, 0.55 * (1 - i / 6.0)));
        }
    }

    /// <summary>A sandmaw up out of the sand: its ringed back curving behind it, and, when it strikes, its great round
    /// mouth open with rings of teeth.</summary>
    private void DrawCreature(DrawingContext dc, Creature c)
    {
        var at = S(c.X, c.Y);
        using var big = dc.PushTransform(Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateScale(1.45, 1.45) * Matrix.CreateTranslation(at.X, at.Y));
        var p = at;
        double rise = c.Mouth > 0 ? Math.Min(1, Math.Sin(Math.Min(1, (2.2 - c.Mouth) / 2.2) * Math.PI) * 1.6) : 0.35;
        // The back: rings shrinking behind the head.
        for (int i = 6; i >= 1; i--)
        {
            double d = i * 11, x = p.X - Math.Cos(c.Dir) * d, y = p.Y - Math.Sin(c.Dir) * d + Math.Sin(_clock * 4 + i) * 1.5;
            double r = 13 - i * 1.3;
            dc.FillEllipse(x + 3, y + 3, r + 2, (r + 2) * 0.7, 0x40000000);
            dc.FillEllipse(x, y, r, 0xFF7A5E44);
            dc.FillEllipse(x - r * 0.25, y - r * 0.25, r * 0.65, 0xFF9C7C5A);
            dc.DrawEllipse(null, Pen(0xFF4A3828, 1.2), new Point(x, y), r, r);
            dc.FillEllipse(x, y, r + 4, (r + 4) * 0.5, Alpha(0xFFE8C080, 0.25));
        }
        double head = 16 + rise * 16;
        // Sand pouring off it.
        dc.FillEllipse(p.X + 4, p.Y + 5, head + 6, (head + 6) * 0.7, 0x50000000);
        dc.FillEllipse(p.X, p.Y + 3, head + 8, (head + 8) * 0.6, Alpha(0xFFE8C080, 0.5));
        dc.FillEllipse(p.X, p.Y - rise * 10, head, 0xFF6E523A);
        dc.FillEllipse(p.X - head * 0.2, p.Y - rise * 10 - head * 0.2, head * 0.75, 0xFF8E6E50);
        if (c.Mouth > 0 || rise > 0.4)
        {
            double open = Math.Max(0.35, rise);
            var m = new Point(p.X, p.Y - rise * 10);
            dc.FillEllipse(m.X, m.Y, head * 0.82 * open, 0xFF3A1414);
            dc.FillEllipse(m.X, m.Y, head * 0.55 * open, 0xFF8A2A2A);
            dc.FillEllipse(m.X, m.Y, head * 0.25 * open, 0xFF1A0808);
            for (int ring = 0; ring < 2; ring++)
            {
                double rr = head * (0.8 - ring * 0.25) * open;
                int teeth = 14 - ring * 4;
                for (int i = 0; i < teeth; i++)
                {
                    double a = i * Math.PI * 2 / teeth + _clock * (ring == 0 ? 0.5 : -0.7);
                    var o = new Point(m.X + Math.Cos(a) * rr, m.Y + Math.Sin(a) * rr);
                    var tip = new Point(m.X + Math.Cos(a) * rr * 0.72, m.Y + Math.Sin(a) * rr * 0.72);
                    var side = new Point(-Math.Sin(a) * 2.6, Math.Cos(a) * 2.6);
                    Poly(dc, 0xFFF0E8D0, 0xFF8A7A60, 0.6, new Point(o.X + side.X, o.Y + side.Y), tip, new Point(o.X - side.X, o.Y - side.Y));
                }
            }
        }
        else
        {
            // Closed: three lobes meeting at the front.
            for (int i = 0; i < 3; i++)
            {
                double a = c.Dir + i * Math.PI * 2 / 3;
                dc.DrawLine(Pen(0xFF3A2818, 2), p, new Point(p.X + Math.Cos(a) * head * 0.8, p.Y + Math.Sin(a) * head * 0.8));
            }
        }
    }
}
