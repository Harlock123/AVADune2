using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVADune2.Engine;

namespace AVADune2.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVADune2/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF4EEE0, Yellow = 0xFFFFD84A, Red = 0xFFFF4A3A, Dim = 0xFFA8946C, Sandy = 0xFFE8C48A, Orange = 0xFFFF9A30, Green = 0xFF6AE070;

    private static FormattedText Format(string text, double size, uint colour) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = Format(text, size, colour);
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(Format(text, size, 0xC0000000), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static double TextWidth(string text, double size) => Format(text, size, White).Width;

    /// <summary>Words wrapped to a width; returns the lines.</summary>
    private static List<string> Wrap(string text, double size, double width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (TextWidth(next, size) > width && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);
    private static uint Blend(uint a, uint b, double t) => Pix.Mix(a, b, t);
    private static uint Shade(uint c, double k) => Pix.Scale(c, k);

    /// <summary>A panel: dark, with a bevelled edge in a colour.</summary>
    private static void Panel(DrawingContext dc, Rect r, uint edge = 0xFF8A6A3A, uint fill = 0xE81A140E)
    {
        dc.FillRectangle(Brush(fill), r, 6);
        dc.DrawRectangle(null, Pen(edge, 2), r.Deflate(1), 6, 6);
        dc.DrawRectangle(null, Pen(Alpha(0xFFFFFFFF, 0.08), 1), r.Deflate(4), 4, 4);
    }

    /// <summary>A button: highlighted when the mouse is over it.</summary>
    private void Button(DrawingContext dc, Rect r, string label, bool on = true, double size = 20, bool lit = false)
    {
        bool over = r.Contains(_mouse);
        dc.FillRectangle(Brush(!on ? 0xFF221C16 : lit ? 0xFF5A4420 : over ? 0xFF4A3A22 : 0xFF2E2418), r, 4);
        dc.DrawRectangle(null, Pen(!on ? 0xFF3E3428 : over || lit ? Yellow : 0xFF8A6A3A, 1.5), r, 4, 4);
        Text(dc, label, r.Center, size, on ? White : 0xFF6A5E4E, center: true);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.Houses: DrawHouses(dc); break;
            case GameState.Briefing: DrawBriefing(dc); break;
            case GameState.Region: DrawRegion(dc); break;
            case GameState.Debrief: DrawDebrief(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            case GameState.HighScores: DrawBackdrop(dc); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBackdrop(dc); DrawInitials(dc); break;
            default:
                DrawBattle(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 84, Yellow, center: true);
            Text(dc, "P OR ESC TO CARRY ON   -   Q TO LEAVE THE BATTLE", new Point(512, 366), 26, White, center: true);
            Text(dc, "THE CAMPAIGN IS SAVED AT THE REGION MAP", new Point(512, 398), 22, Dim, center: true);
        }
        if (g.State == GameState.Attract)
        {
            dc.FillRectangle(Brush(0xB0000000), new Rect(0, 40, Field.SideX, 34));
            Text(dc, "DEMONSTRATION - PRESS ENTER", new Point(Field.SideX / 2, 57), 26, Yellow, center: true);
        }
        if (_padActive) DrawPadCursor(dc);
    }

    private void DrawPadCursor(DrawingContext dc)
    {
        var p = _padCursor;
        dc.DrawLine(Pen(0xFF000000, 4), new Point(p.X - 10, p.Y), new Point(p.X + 10, p.Y));
        dc.DrawLine(Pen(0xFF000000, 4), new Point(p.X, p.Y - 10), new Point(p.X, p.Y + 10));
        dc.DrawLine(Pen(Yellow, 2), new Point(p.X - 9, p.Y), new Point(p.X + 9, p.Y));
        dc.DrawLine(Pen(Yellow, 2), new Point(p.X, p.Y - 9), new Point(p.X, p.Y + 9));
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillEllipse(this DrawingContext dc, double x, double y, double rx, double ry, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), rx, ry);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
