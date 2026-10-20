using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AVADune2.Views;

/// <summary>
/// A little picture as an array of 0xAARRGGBB pixels, with the few things the art needs: rectangles, lines, discs,
/// rings, shading and grain, and a building's block seen from above with its south wall and its shadow.
/// </summary>
public sealed class Pix
{
    public readonly int W, H;
    public readonly uint[] P;

    public Pix(int w, int h, uint fill = 0)
    {
        W = w;
        H = h;
        P = new uint[w * h];
        if (fill != 0) Array.Fill(P, fill);
    }

    public uint Get(int x, int y) => x >= 0 && y >= 0 && x < W && y < H ? P[y * W + x] : 0;

    public void Set(int x, int y, uint c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        uint a = c >> 24;
        if (a == 0xFF) P[y * W + x] = c;
        else if (a > 0)
        {
            uint under = P[y * W + x];
            P[y * W + x] = under >> 24 == 0 ? c : Mix(under, c | 0xFF000000, a / 255.0) | (under & 0xFF000000);
        }
    }

    public void Fill(int x, int y, int w, int h, uint c)
    {
        for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++) Set(i, j, c);
    }

    public void HLine(int x0, int x1, int y, uint c)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Set(x, y, c);
    }

    public void VLine(int x, int y0, int y1, uint c)
    {
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) Set(x, y, c);
    }

    public void Frame(int x, int y, int w, int h, uint c)
    {
        HLine(x, x + w - 1, y, c);
        HLine(x, x + w - 1, y + h - 1, c);
        VLine(x, y, y + h - 1, c);
        VLine(x + w - 1, y, y + h - 1, c);
    }

    public void Line(int x0, int y0, int x1, int y1, uint c)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>A filled ellipse round (cx, cy), radii in pixels.</summary>
    public void Disc(double cx, double cy, double rx, double ry, uint c)
    {
        for (int y = (int)Math.Floor(cy - ry); y <= (int)Math.Ceiling(cy + ry); y++)
        for (int x = (int)Math.Floor(cx - rx); x <= (int)Math.Ceiling(cx + rx); x++)
        {
            double u = (x + 0.5 - cx) / rx, v = (y + 0.5 - cy) / ry;
            if (u * u + v * v <= 1) Set(x, y, c);
        }
    }

    public void Disc(double cx, double cy, double r, uint c) => Disc(cx, cy, r, r, c);

    /// <summary>A disc lit from the top-left: a dome, or a round tank seen from above.</summary>
    public void Dome(double cx, double cy, double r, uint c)
    {
        for (int y = (int)Math.Floor(cy - r); y <= (int)Math.Ceiling(cy + r); y++)
        for (int x = (int)Math.Floor(cx - r); x <= (int)Math.Ceiling(cx + r); x++)
        {
            double u = (x + 0.5 - cx) / r, v = (y + 0.5 - cy) / r, d = u * u + v * v;
            if (d > 1) continue;
            double light = 1.15 - 0.45 * (u + v + 0.6) / 2.6 - 0.25 * d;
            Set(x, y, Scale(c, light));
        }
        Disc(cx - r * 0.35, cy - r * 0.38, Math.Max(1, r * 0.22), 0x90FFFFFF);
    }

    /// <summary>A ring between two radii.</summary>
    public void Ring(double cx, double cy, double r0, double r1, uint c)
    {
        for (int y = (int)Math.Floor(cy - r1); y <= (int)Math.Ceiling(cy + r1); y++)
        for (int x = (int)Math.Floor(cx - r1); x <= (int)Math.Ceiling(cx + r1); x++)
        {
            double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
            if (d >= r0 && d <= r1) Set(x, y, c);
        }
    }

    /// <summary>Darkens (k &lt; 1) or lightens what's already there.</summary>
    public void Shade(int x, int y, int w, int h, double k)
    {
        for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++)
            if (i >= 0 && j >= 0 && i < W && j < H && P[j * W + i] >> 24 != 0) P[j * W + i] = Scale(P[j * W + i], k) | (P[j * W + i] & 0xFF000000);
    }

    /// <summary>Speckles a rectangle with lighter and darker pixels.</summary>
    public void Grain(int x, int y, int w, int h, double amount, int seed)
    {
        for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++)
        {
            if (i < 0 || j < 0 || i >= W || j >= H || P[j * W + i] >> 24 == 0) continue;
            double r = Rand(i, j, seed);
            uint a = P[j * W + i] & 0xFF000000;
            if (r < 0.18) P[j * W + i] = Scale(P[j * W + i], 1 - amount) | a;
            else if (r > 0.86) P[j * W + i] = Scale(P[j * W + i], 1 + amount * 0.8) | a;
        }
    }

    /// <summary>
    /// A flat-roofed block on the ground rectangle (x..x+w, y..y+d), <paramref name="h"/> pixels tall: its shadow to
    /// the south-east, its south wall, and its roof lifted by its height, lit from the north-west.
    /// </summary>
    public void Block(int x, int y, int w, int d, int h, uint roof, uint wall, bool windows = false)
    {
        // The shadow.
        for (int j = y - h + 3; j < y + d + 2; j++)
        for (int i = x + w; i < x + w + Math.Min(6, h / 2 + 2); i++) if (Get(i, j) >> 24 != 0) Shade(i, j, 1, 1, 0.6);
        int top = y - h;
        Fill(x, top + d, w, h, wall);
        VLine(x, top + d, y + d - 1, Scale(wall, 1.15));
        VLine(x + w - 1, top + d, y + d - 1, Scale(wall, 0.75));
        HLine(x, x + w - 1, y + d - 1, Scale(wall, 0.65));
        if (windows && h >= 5)
            for (int i = x + 2; i < x + w - 2; i += 3)
                Fill(i, top + d + 2, 2, Math.Max(1, h - 5), 0xFF2A2C34);
        Fill(x, top, w, d, roof);
        HLine(x, x + w - 1, top, Scale(roof, 1.25));
        VLine(x, top, top + d - 1, Scale(roof, 1.12));
        VLine(x + w - 1, top, top + d - 1, Scale(roof, 0.8));
        HLine(x, x + w - 1, top + d - 1, Scale(roof, 0.72));
    }

    public static uint Scale(uint c, double k)
    {
        uint Ch(int shift) => (uint)Math.Clamp(Math.Round(((c >> shift) & 0xFF) * k), 0, 255);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    public static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    public static double Rand(int x, int y, int salt)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    /// <summary>The picture as a bitmap (premultiplied, as Avalonia wants it).</summary>
    public unsafe WriteableBitmap ToBitmap()
    {
        var bmp = new WriteableBitmap(new PixelSize(W, H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        uint* dst = (uint*)fb.Address;
        int stride = fb.RowBytes / 4;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++) dst[y * stride + x] = Premul(P[y * W + x]);
        return bmp;
    }

    public static uint Premul(uint c)
    {
        uint a = c >> 24;
        if (a == 0xFF) return c;
        if (a == 0) return 0;
        uint Ch(int shift) => ((c >> shift) & 0xFF) * a / 255;
        return a << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}
