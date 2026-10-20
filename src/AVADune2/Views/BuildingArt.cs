using Avalonia.Media.Imaging;
using AVADune2.Engine;

namespace AVADune2.Views;

/// <summary>
/// The buildings' pictures, each drawn here pixel by pixel in its House's colours, seen from above with their south
/// walls showing, as on a desert base: the yard and its crane, the wind turbine's ducted rotor (the blades turn on top,
/// drawn live), the refinery's tanks and intake, the silos' domes, the barracks, the factories' ribbed hangars, the
/// high-tech factory's landing pad, the outpost (its dish turns on top), turrets' bases (their guns turn on top), the
/// walls that join their neighbours, the repair pad, the starport and the palace. Each picture is made once and kept.
/// </summary>
public static class BuildingArt
{
    public const int T = Field.Tile;
    /// <summary>How far a building's picture reaches above its footprint (its walls and towers stand up).</summary>
    public const int Over = 14;
    private static readonly Dictionary<(Kind, uint, int), WriteableBitmap> Cache = [];
    private static readonly Lock Guard = new();

    private const uint Apron = 0xFF8E8A82, ApronDark = 0xFF6A6660, Metal = 0xFFAAA69C, MetalDark = 0xFF6C685F, Stone = 0xFFC4AE88, StoneDark = 0xFF8A7658;
    private const uint Glass = 0xFF5E92B4, Hazard = 0xFFE2B42A, Ink = 0xFF26221E, Glow = 0xFFFF8A2A;

    /// <summary>A building's picture; walls also by which of their four neighbours are walls (a mask of 1, 2, 4, 8 for
    /// east, west, south, north).</summary>
    public static WriteableBitmap Get(Kind k, uint house, int mask = 0)
    {
        lock (Guard)
        {
            if (Cache.TryGetValue((k, house, mask), out var bmp)) return bmp;
            bmp = Draw(k, house, mask).ToBitmap();
            Cache[(k, house, mask)] = bmp;
            return bmp;
        }
    }

    public static Pix Draw(Kind k, uint house, int mask = 0)
    {
        var st = Rules.Of(k);
        var p = new Pix(st.W * T, st.H * T + Over);
        int o = Over, w = st.W * T, h = st.H * T;
        uint hc = house, hd = Pix.Scale(house, 0.6), hl = Pix.Scale(house, 1.3);
        if (k != Kind.Wall) Pad(p, 1, o + 1, w - 2, h - 2);
        switch (k)
        {
            case Kind.Yard:
                p.Block(4, o + 16, 40, 30, 10, 0xFFB8A784, 0xFF877660, windows: true);
                p.Fill(4, o + 10, 40, 4, hc);
                p.HLine(4, 43, o + 10, hl);
                p.Fill(10, o + 20, 8, 6, MetalDark);
                p.Fill(26, o + 20, 12, 3, MetalDark);
                p.Block(46, o + 30, 12, 14, 18, 0xFF9C9480, 0xFF6E6656);
                p.Fill(47, o + 13, 10, 3, Glass);
                // The crane: a lattice arm from its mast to a hook over the yard.
                Lattice(p, 52, o + 18, 16, o - 6, Hazard);
                p.VLine(18, o - 4, o + 12, Ink);
                p.Fill(16, o + 12, 5, 3, Ink);
                break;
            case Kind.Turbine:
                p.Block(5, o + 26, 54, 32, 6, 0xFF9A9284, 0xFF6A6254);
                p.Fill(8, o + 50, 48, 3, hc);
                p.Disc(32, o + 26, 25, 25, 0xFF4A4640);
                p.Ring(32, o + 26, 21, 25, Metal);
                p.Ring(32, o + 26, 24, 25, MetalDark);
                p.Ring(32, o + 26, 21, 22, 0xFFCCC8BE);
                p.Disc(32, o + 26, 20.5, 20.5, 0xFF2A2826);
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    p.Line(32, o + 26, 32 + (int)(Math.Cos(a) * 20), o + 26 + (int)(Math.Sin(a) * 20), 0xFF3A3834);
                }
                break;
            case Kind.Refinery:
                p.Block(38, o + 10, 54, 32, 12, 0xFFA69882, 0xFF76685A, windows: true);
                p.Fill(38, o - 2, 54, 4, hc);
                p.HLine(38, 91, o - 2, hl);
                // The tanks of solarite, glowing at the top.
                p.Dome(17, o + 16, 13, 0xFFB47C4C);
                p.Dome(17, o + 46, 12, 0xFFB47C4C);
                p.Disc(17, o + 16, 4, 4, Glow);
                p.Disc(17, o + 46, 4, 4, Glow);
                p.Fill(28, o + 15, 12, 3, MetalDark);
                p.Fill(28, o + 45, 12, 3, MetalDark);
                // The intake, where a harvester docks.
                p.Fill(36, o + 50, 24, 14, Ink);
                for (int i = 36; i < 60; i += 4) p.Fill(i, o + 61, 2, 3, Hazard);
                p.Fill(40, o + 52, 16, 6, 0xFF6A2A14);
                p.Fill(42, o + 53, 12, 3, Glow);
                p.Block(70, o + 30, 14, 10, 18, MetalDark, 0xFF4E4A44);
                p.Disc(77, o + 11, 3, 3, Glow);
                break;
            case Kind.Silo:
                foreach (var (cx, cy) in new[] { (17, 17), (47, 17), (17, 47), (47, 47) })
                {
                    p.Disc(cx + 3, o + cy + 3, 13, 13, 0x50000000);
                    p.Dome(cx, o + cy, 12.5, 0xFFB8B2A6);
                    p.Ring(cx, o + cy, 11, 12.5, Pix.Scale(hc, 0.9));
                    p.Disc(cx, o + cy, 3.5, 3.5, Glow);
                }
                break;
            case Kind.Barracks:
                p.Block(5, o + 16, 54, 30, 8, 0xFF8A8A6A, 0xFF6A684E, windows: true);
                p.Fill(5, o + 22, 54, 3, hc);
                p.Fill(20, o + 12, 8, 6, 0xFF6E6E54);
                p.Fill(36, o + 12, 8, 6, 0xFF6E6E54);
                for (int i = 6; i < 58; i += 6) p.Disc(i + 2, o + 56, 3.2, 2.4, 0xFFB09A6E);
                p.VLine(56, o - 8, o + 14, Ink);
                break;
            case Kind.LightFactory:
                Hangar(p, 4, o + 12, 56, 38, 12, hc);
                break;
            case Kind.HeavyFactory:
                Hangar(p, 4, o + 12, 88, 40, 14, hc);
                // The gantry crane over the roof.
                p.Fill(10, o - 6, 3, 44, Hazard);
                p.Fill(83, o - 6, 3, 44, Hazard);
                p.Fill(10, o + 8, 76, 4, Hazard);
                p.Fill(42, o + 6, 10, 8, MetalDark);
                break;
            case Kind.HighTech:
                p.Block(4, o + 14, 52, 34, 12, 0xFF8E9EAA, 0xFF5E6A74);
                for (int y = o + 4; y < o + 30; y += 6)
                for (int x = 8; x < 52; x += 8) p.Fill(x, y, 6, 4, Glass);
                p.Fill(4, o, 52, 3, hc);
                // The landing pad.
                p.Disc(76, o + 32, 17, 17, 0xFF5A5852);
                p.Ring(76, o + 32, 13, 15, Hazard);
                p.Fill(70, o + 26, 3, 13, 0xFFE8E4DA);
                p.Fill(80, o + 26, 3, 13, 0xFFE8E4DA);
                p.Fill(70, o + 31, 13, 3, 0xFFE8E4DA);
                break;
            case Kind.Outpost:
                p.Block(6, o + 22, 42, 28, 10, 0xFF9C9482, 0xFF6C6454, windows: true);
                p.Fill(6, o + 16, 42, 3, hc);
                p.Block(50, o + 30, 10, 10, 24, MetalDark, 0xFF4C4842);
                p.Line(55, o - 8, 55, o + 6, Ink);
                p.Disc(55, o - 9, 2, 2, 0xFFFF4030);
                break;
            case Kind.Turret:
                p.Disc(17, o + 18, 13, 13, 0x50000000);
                p.Disc(16, o + 16, 13, 13, MetalDark);
                p.Disc(16, o + 16, 11, 11, 0xFF8A867C);
                p.Ring(16, o + 16, 9, 10, hc);
                break;
            case Kind.RocketTurret:
                p.Block(3, o + 10, 26, 20, 6, 0xFF8A867C, MetalDark);
                p.Ring(16, o + 14, 7, 8, hc);
                break;
            case Kind.Wall:
                Wall(p, mask);
                break;
            case Kind.RepairPad:
                p.Fill(4, o + 4, 88, 56, 0xFF5E5C56);
                for (int x = 4; x < 92; x++)
                for (int y = o + 4; y < o + 60; y++)
                {
                    bool edge = x < 8 || x > 87 || y < o + 8 || y > o + 55;
                    if (edge) p.Set(x, y, (x + y) / 4 % 2 == 0 ? Hazard : Ink);
                }
                // A spanner painted on the deck.
                p.Disc(36, o + 32, 8, 8, 0xFFDAD6CC);
                p.Disc(36, o + 32, 3.5, 3.5, 0xFF5E5C56);
                p.Fill(42, o + 29, 22, 6, 0xFFDAD6CC);
                p.Disc(64, o + 32, 6, 6, 0xFFDAD6CC);
                p.Block(78, o + 8, 10, 10, 14, Metal, MetalDark);
                p.Fill(80, o - 6, 4, 10, hc);
                break;
            case Kind.Starport:
                p.Disc(48, o + 50, 40, 40, 0xFF4E4C48);
                p.Ring(48, o + 50, 34, 37, Hazard);
                p.Ring(48, o + 50, 20, 22, 0xFFDAD6CC);
                for (int i = 0; i < 12; i++)
                {
                    double a = i * Math.PI / 6;
                    p.Disc(48 + Math.Cos(a) * 38.5, o + 50 + Math.Sin(a) * 38.5, 1.6, 1.6, 0xFFFFE070);
                }
                p.Fill(42, o + 44, 12, 12, hc);
                p.Block(72, o + 8, 20, 18, 22, 0xFF9C9888, 0xFF6C685C);
                p.Fill(73, o - 12, 18, 4, Glass);
                p.Dome(10, o + 10, 7, 0xFFB8B2A6);
                p.Dome(10, o + 88, 6, 0xFFB8B2A6);
                break;
            case Kind.Palace:
                p.Fill(6, o + 72, 84, 20, 0xFFB09C7A);
                for (int i = 0; i < 4; i++) p.HLine(30 - i * 2, 66 + i * 2, o + 80 + i * 3, 0xFF8E7A5C);
                p.Block(10, o + 30, 76, 44, 16, Stone, StoneDark, windows: true);
                p.Fill(10, o + 14, 76, 4, hc);
                p.HLine(10, 85, o + 14, hl);
                foreach (var (tx, ty) in new[] { (14, 18), (82, 18), (14, 58), (82, 58) })
                {
                    p.Disc(tx + 2, o + ty + 4, 8, 8, 0x50000000);
                    p.Dome(tx, o + ty, 8, Stone);
                    p.Disc(tx, o + ty - 1, 2.5, 2.5, hc);
                }
                p.Disc(50, o + 40, 20, 20, 0x50000000);
                p.Dome(48, o + 36, 19, hc);
                p.Ring(48, o + 36, 17, 19, hd);
                p.Disc(48, o + 36, 3, 3, 0xFFFFE070);
                foreach (int bx in new[] { 24, 66 }) { p.Fill(bx, o + 62, 6, 12, hc); p.Fill(bx, o + 72, 6, 2, hd); }
                break;
        }
        return p;
    }

    /// <summary>The concrete apron under a building.</summary>
    private static void Pad(Pix p, int x, int y, int w, int h)
    {
        p.Fill(x, y, w, h, Apron);
        p.Frame(x, y, w, h, ApronDark);
        p.HLine(x + 1, x + w - 2, y + 1, 0xFFA8A49C);
        p.Grain(x, y, w, h, 0.08, 3);
    }

    /// <summary>A hangar with a ribbed, curved roof and a great door in its south wall.</summary>
    private static void Hangar(Pix p, int x, int y, int w, int d, int h, uint house)
    {
        p.Block(x, y, w, d, h, 0xFFA09A8C, 0xFF706A5E);
        int top = y - h;
        for (int j = 0; j < d; j++)
        {
            double curve = Math.Sin(Math.PI * (j + 0.5) / d);
            for (int i = 1; i < w - 1; i++)
            {
                uint c = Pix.Scale(0xFFA09A8C, 0.72 + 0.42 * curve);
                if (i % 6 == 0) c = Pix.Scale(c, 0.78);
                p.Set(x + i, top + j, c);
            }
        }
        p.Fill(x, top + d / 2 - 2, w, 4, house);
        int door = Math.Min(w - 12, 30);
        p.Fill(x + (w - door) / 2, y + d - h + 2, door, h - 2, Ink);
        for (int i = 0; i < door; i += 3) p.VLine(x + (w - door) / 2 + i, y + d - h + 2, y + d - 1, 0xFF3A3632);
        p.Frame(x + (w - door) / 2 - 1, y + d - h + 1, door + 2, h, Pix.Scale(house, 0.8));
    }

    /// <summary>A crane's lattice arm from (x0, y0) to (x1, y1).</summary>
    private static void Lattice(Pix p, int x0, int y0, int x1, int y1, uint c)
    {
        double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)), nx = -(y1 - y0) / len * 2, ny = (x1 - x0) / len * 2;
        p.Line(x0, y0, x1, y1, c);
        p.Line((int)(x0 + nx), (int)(y0 + ny), (int)(x1 + nx), (int)(y1 + ny), c);
        for (double t = 0; t < 1; t += 4 / len)
        {
            int ax = (int)(x0 + (x1 - x0) * t), ay = (int)(y0 + (y1 - y0) * t);
            p.Line(ax, ay, (int)(ax + nx + (x1 - x0) * 2 / len), (int)(ay + ny + (y1 - y0) * 2 / len), Pix.Scale(c, 0.7));
        }
    }

    /// <summary>A section of wall, reaching out to the walls beside it.</summary>
    private static void Wall(Pix p, int mask)
    {
        int o = Over;
        const uint top = 0xFFB4A68C, side = 0xFF7E7058;
        void Seg(int x, int y, int w, int d) => p.Block(x, o + y, w, d, 8, top, side);
        Seg(7, 9, 18, 16);
        if ((mask & 1) != 0) Seg(16, 9, 16, 16);
        if ((mask & 2) != 0) Seg(0, 9, 16, 16);
        if ((mask & 4) != 0) Seg(7, 17, 18, 15);
        if ((mask & 8) != 0) Seg(7, 0, 18, 16);
        p.Grain(0, 0, T, T + Over, 0.12, 5);
    }
}
