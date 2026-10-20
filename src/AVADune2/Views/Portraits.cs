using Avalonia.Media.Imaging;
using AVADune2.Engine;

namespace AVADune2.Views;

/// <summary>
/// The Houses' advisors, painted pixel by pixel with light from the upper left: Corven of Aldaine, white-haired and
/// white-bearded with a gold circlet, in blue robes trimmed with gold; Gresh of Vrakos, bald and scarred, a metal
/// eyepiece in place of his right eye, in spiked black-and-red armour; Lira of Sythe, pale and sharp-eyed under a
/// green hood, a line of tattooed dots down her cheek. The blinking and the talking are drawn on top, live.
/// </summary>
public static class Portraits
{
    public const int W = 180, H = 210;
    /// <summary>Where the face's features are, for the live parts drawn over the picture.</summary>
    public const double Cx = 90, Cy = 92, EyeDx = 15, EyeY = Cy - 2, MouthY = Cy + 31;
    private static readonly Dictionary<Faction, WriteableBitmap> Cache = [];
    private static readonly Lock Guard = new();

    public static WriteableBitmap Get(Faction f)
    {
        lock (Guard)
        {
            if (!Cache.TryGetValue(f, out var bmp)) Cache[f] = bmp = Paint(f).ToBitmap();
            return bmp;
        }
    }

    public static uint SkinOf(Faction f) => f switch { Faction.Aldaine => 0xFFE4BA94, Faction.Vrakos => 0xFFB98A6C, _ => 0xFFECD4C2 };
    public static uint IrisOf(Faction f) => f switch { Faction.Aldaine => 0xFF3A6CB4, Faction.Vrakos => 0xFF4A3420, _ => 0xFF2AA050 };

    /// <summary>Lambert light from the upper left on a surface with the given normal (x, y; z worked out).</summary>
    private static double Light(double nx, double ny)
    {
        double nz = Math.Sqrt(Math.Max(0, 1 - nx * nx - ny * ny));
        double l = (-0.5 * nx - 0.55 * ny + 0.67 * nz) / 1.0;
        return Math.Clamp(l, 0, 1);
    }

    private static uint Lit(uint c, double light) => Pix.Mix(Pix.Scale(c, 0.45), Pix.Scale(c, 1.12), light);

    private static Pix Paint(Faction f)
    {
        var p = new Pix(W, H);
        uint house = Rules.Colour(f), skin = SkinOf(f);
        // The background: the House's colour, darkening to the edges.
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            double d = Math.Sqrt((x - Cx) * (x - Cx) + (y - 80.0) * (y - 80.0)) / 140;
            p.P[y * W + x] = Pix.Mix(Pix.Scale(house, 0.42), Pix.Scale(house, 0.12), d) ;
        }
        Torso(p, f, house);
        // The neck.
        for (int y = 118; y < 168; y++)
        for (int x = 72; x < 108; x++)
        {
            double u = (x - 90) / 18.0;
            p.Set(x, y, Lit(skin, Light(u * 0.9, 0.1) * 0.8 - (y < 135 ? 0.25 : 0)));
        }
        if (f == Faction.Sythe) Hood(p, house, back: true);
        Head(p, f, skin);
        Face(p, f, skin);
        switch (f)
        {
            case Faction.Aldaine: Corven(p); break;
            case Faction.Vrakos: Gresh(p, skin); break;
            default: Lira(p, house); break;
        }
        return p;
    }

    /// <summary>Inside the head: an egg, wider at the brow, the jaw narrowing to the chin.</summary>
    private static bool InHead(double x, double y, out double nx, out double ny)
    {
        double dy = y - Cy;
        double rx = dy > 0 ? 38 * (1 - 0.38 * Math.Pow(dy / 52, 2)) : 38;
        double ry = dy > 0 ? 52 : 48;
        nx = (x - Cx) / rx;
        ny = dy / ry;
        return nx * nx + ny * ny <= 1;
    }

    private static void Head(Pix p, Faction f, uint skin)
    {
        // The ears first, behind.
        foreach (int side in new[] { -1, 1 })
            for (int y = (int)Cy - 10; y < Cy + 16; y++)
            for (int x = (int)Cx + side * 32; x != (int)Cx + side * 46; x += side)
            {
                double u = (x - (Cx + side * 38)) / 7.0, v = (y - (Cy + 3)) / 12.0;
                if (u * u + v * v <= 1) p.Set(x, y, Lit(skin, Light(u * 0.6, v * 0.6) * 0.8));
            }
        for (int y = 30; y < 150; y++)
        for (int x = 40; x < 140; x++)
        {
            if (!InHead(x, y, out double nx, out double ny)) continue;
            double l = Light(nx * 0.95, ny * 0.75);
            // The cheekbones and the hollows under them.
            double cheek = Math.Exp(-Math.Pow((Math.Abs(x - Cx) - 22) / 7, 2) - Math.Pow((y - Cy - 14) / 6, 2));
            l -= cheek * 0.18;
            uint c = Lit(skin, l);
            // A little warmth in the cheeks and nose.
            double warm = Math.Exp(-Math.Pow((Math.Abs(x - Cx) - 17) / 9, 2) - Math.Pow((y - Cy - 8) / 8, 2));
            c = Pix.Mix(c, 0xFFD87A6A, warm * 0.18);
            if (Pix.Rand(x, y, 3) > 0.92) c = Pix.Scale(c, 0.97);
            p.Set(x, y, c);
        }
    }

    private static void Face(Pix p, Faction f, uint skin)
    {
        uint iris = IrisOf(f);
        foreach (int side in new[] { -1, 1 })
        {
            double ex = Cx + side * EyeDx, ey = EyeY;
            // The socket's shadow, the white, the iris and its highlight, the lid.
            p.Disc(ex, ey + 1, 11, 7, 0x40301810);
            p.Disc(ex, ey, 7.5, 3.8, 0xFFF2ECE2);
            p.Disc(ex + side * 0.5, ey, 3.4, 3.4, iris);
            p.Disc(ex + side * 0.5, ey, 1.6, 1.6, 0xFF101010);
            p.Set((int)ex - 1, (int)ey - 2, 0xFFFFFFFF);
            for (int x = (int)ex - 8; x <= ex + 8; x++)
            {
                double u = (x - ex) / 8;
                p.Set(x, (int)Math.Round(ey - 4 + u * u * 2.5), 0xFF3A2418);
            }
        }
        // The nose: a lit ridge, a shadowed side, the nostrils.
        for (int y = (int)Cy - 4; y < Cy + 17; y++)
        {
            p.Set((int)Cx - 1, y, Pix.Mix(Lit(skin, 0.95), 0xFFFFFFFF, 0.1));
            p.Set((int)Cx + 3, y, Lit(skin, 0.35));
            p.Set((int)Cx + 4, y, Lit(skin, 0.45));
        }
        p.Disc(Cx, Cy + 17, 5.5, 3.2, Lit(skin, 0.8));
        p.Disc(Cx - 3.5, Cy + 19, 1.6, 1.1, 0xFF4A2A20);
        p.Disc(Cx + 3.5, Cy + 19, 1.6, 1.1, 0xFF4A2A20);
        // The mouth.
        p.Disc(Cx, MouthY + 2.5, 9, 2.6, Pix.Mix(Lit(skin, 0.6), 0xFFB05A50, 0.45));
        for (int x = (int)Cx - 10; x <= Cx + 10; x++) p.Set(x, (int)MouthY + (Math.Abs(x - Cx) > 7 ? 1 : 0), 0xFF5A2A22);
        p.Disc(Cx, MouthY - 2, 7, 1.6, Pix.Mix(Lit(skin, 0.55), 0xFFA0504A, 0.3));
    }

    private static void Torso(Pix p, Faction f, uint house)
    {
        uint robe = f switch { Faction.Aldaine => 0xFF2A4C9C, Faction.Vrakos => 0xFF2A1C1A, _ => 0xFF1E5E30 };
        for (int y = 148; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            double half = 34 + (y - 148) * 1.5;
            if (Math.Abs(x - Cx) > half) continue;
            double u = (x - Cx) / half;
            uint c = Lit(robe, 0.6 - u * 0.35 - (y - 148) / 200.0);
            // Folds.
            if (f != Faction.Vrakos && Math.Abs(Math.Sin((x - Cx) / 6.0 + y / 30.0)) < 0.08) c = Pix.Scale(c, 0.8);
            p.Set(x, y, c);
        }
        switch (f)
        {
            case Faction.Aldaine:
                // A gold-trimmed V at the neck.
                for (int t = 0; t < 52; t++)
                {
                    p.Disc(Cx - 26 + t * 0.5, 150 + t, 2.2, 2.2, 0xFFE8C060);
                    p.Disc(Cx + 26 - t * 0.5, 150 + t, 2.2, 2.2, 0xFFE8C060);
                }
                p.Dome(Cx, 202, 6, 0xFFE8C060);
                break;
            case Faction.Vrakos:
                // Red-trimmed plate, a gorget, and spiked pauldrons.
                for (int y = 160; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (Math.Abs(x - Cx) < 34 + (y - 148) * 1.5 && (y - 160) % 14 == 0) p.Set(x, y, 0xFF8A2018);
                p.Fill(66, 150, 48, 12, 0xFF5A5654);
                p.HLine(66, 113, 150, 0xFF9A9694);
                foreach (int side in new[] { -1, 1 })
                {
                    p.Dome(Cx + side * 70, 182, 26, 0xFF3A2A28);
                    for (int i = 0; i < 4; i++)
                    {
                        int bx = (int)(Cx + side * (52 + i * 11)), by = 166 + i * 3;
                        for (int k = 0; k < 16; k++) p.HLine(bx - (16 - k) / 3, bx + (16 - k) / 3, by - k, Pix.Mix(0xFFB8B4B0, 0xFF6A6664, k / 16.0));
                    }
                }
                break;
            default:
                p.Dome(Cx, 168, 6, 0xFFC8D0D8);
                break;
        }
    }

    private static void Corven(Pix p)
    {
        const uint white = 0xFFEAE6DE, grey = 0xFFB8B2A8;
        // Hair at the sides and back, and a little on top; a gold circlet.
        for (int y = 40; y < 118; y++)
        for (int x = 44; x < 136; x++)
        {
            InHead(x, y, out double nx, out double ny);
            double r = Math.Sqrt(nx * nx + ny * ny);
            bool side = Math.Abs(nx) > 0.72 && ny < 0.45 && r < 1.12;
            bool top = ny < -0.72 && r < 1.06 && r > 0.85;
            if (!side && !top) continue;
            uint c = Pix.Mix(grey, white, Light(nx, ny) * 0.8 + Pix.Rand(x, y, 9) * 0.2);
            if ((x + y * 3) % 7 == 0) c = grey;
            p.Set(x, y, c);
        }
        for (int x = 54; x < 127; x++)
        {
            InHead(x, Cy - 24, out double nx, out _);
            int y = (int)(Cy - 24 + nx * nx * 6);
            p.Set(x, y, 0xFFE8C060);
            p.Set(x, y + 1, 0xFFB8903A);
        }
        p.Dome(Cx, Cy - 25, 3.5, 0xFF5AA0E8);
        // Bushy white brows.
        foreach (int side in new[] { -1, 1 })
            for (int i = 0; i < 18; i++) p.Disc(Cx + side * (7 + i * 0.75), EyeY - 8 - Math.Sin(i / 18.0 * Math.PI) * 2.5, 2, 1.5, i % 3 == 0 ? grey : white);
        // The moustache and the long beard.
        for (int y = (int)Cy + 20; y < 196; y++)
        for (int x = 46; x < 134; x++)
        {
            double dy = y - (Cy + 20);
            double half = dy < 12 ? 20 + dy * 1.4 : Math.Max(0, 37 - (dy - 12) * 0.5);
            if (Math.Abs(x - Cx) > half) continue;
            if (y > Cy + 26 && y < Cy + 34 && Math.Abs(x - Cx) < 9) continue;
            uint c = Pix.Mix(grey, white, 0.55 + 0.35 * Math.Sin((x - Cx) * 0.9 + y * 0.15) * 0.5 - (x - Cx) / 120.0);
            p.Set(x, y, c);
        }
        // The mouth shows through the beard.
        p.Fill((int)Cx - 7, (int)MouthY - 1, 15, 3, 0xFF6A3A30);
    }

    private static void Gresh(Pix p, uint skin)
    {
        // A heavy brow and a shadowed jaw.
        foreach (int side in new[] { -1, 1 })
            for (int i = 0; i < 20; i++) p.Disc(Cx + side * (6 + i * 0.8), EyeY - 8 + (side < 0 ? i * 0.12 : -i * 0.05), 2.2, 1.6, 0xFF3A2418);
        for (int y = (int)Cy + 22; y < 145; y++)
        for (int x = 50; x < 130; x++)
            if (InHead(x, y, out _, out _) && Pix.Rand(x, y, 7) > 0.55) p.Set(x, y, 0x40201008);
        // The scar across the left eye.
        for (int t = 0; t < 34; t++)
        {
            int x = (int)(Cx - 26 + t * 0.6), y = (int)(Cy - 20 + t);
            p.Set(x, y, 0xFFE0A090);
            p.Set(x + 1, y, 0xFF8A4A3A);
        }
        // The eyepiece: a plate bolted over the right eye, its lens red.
        double ex = Cx + EyeDx, ey = EyeY;
        p.Dome(ex + 1, ey, 11, 0xFF7A7672);
        p.Ring(ex + 1, ey, 9.5, 11, 0xFF3A3632);
        for (int i = 0; i < 4; i++) p.Disc(ex + 1 + Math.Cos(i * Math.PI / 2 + 0.7) * 8, ey + Math.Sin(i * Math.PI / 2 + 0.7) * 8, 1.2, 1.2, 0xFFC8C4BE);
        p.Disc(ex + 1, ey, 4.5, 4.5, 0xFF5A1010);
        p.Fill((int)ex + 10, (int)ey - 2, 16, 3, 0xFF5A5652);
    }

    private static void Lira(Pix p, uint house)
    {
        // Dark hair framing the brow.
        for (int y = 40; y < 100; y++)
        for (int x = 48; x < 132; x++)
        {
            if (!InHead(x, y, out double nx, out double ny)) continue;
            if (ny < -0.42 || Math.Abs(nx) > 0.8 && ny < 0.2)
                p.Set(x, y, Pix.Mix(0xFF1A1418, 0xFF3A2A30, Light(nx, ny) * 0.8 + ((x + y) % 5 == 0 ? 0.2 : 0)));
        }
        // Fine dark brows, lined eyes.
        foreach (int side in new[] { -1, 1 })
        {
            for (int i = 0; i < 16; i++) p.Set((int)(Cx + side * (8 + i * 0.8)), (int)(EyeY - 8 - Math.Sin(i / 16.0 * Math.PI) * 3), 0xFF2A1A18);
            for (int i = 0; i < 14; i++) p.Set((int)(Cx + side * (9 + i * 0.6)), (int)(EyeY - 4 + i * 0.05), 0xFF101010);
        }
        // The tattoo: a line of dots down the left cheek.
        for (int i = 0; i < 6; i++) p.Disc(Cx - 22 + i * 0.6, Cy + 4 + i * 5, 1.2, 1.2, Pix.Scale(house, 0.9));
        Hood(p, house, back: false);
    }

    /// <summary>A hood: behind the head (its inside, in shadow), or its rim over the brow and down the cheeks.</summary>
    private static void Hood(Pix p, uint house, bool back)
    {
        uint cloth = 0xFF2A7A40;
        for (int y = 14; y < 175; y++)
        for (int x = 18; x < 162; x++)
        {
            double u = (x - Cx) / 62, v = (y - Cy + 6) / 76;
            double r = u * u + v * v;
            if (r > 1) continue;
            bool head = InHead(x, y, out double nx, out double ny);
            if (back)
            {
                if (y > 150 && Math.Abs(x - Cx) < 34 + (y - 148) * 1.5) continue;
                p.Set(x, y, Lit(Pix.Scale(cloth, 0.55), Light(u * 0.7, v * 0.7) * 0.6));
                continue;
            }
            // The rim: the hood's edge round the face.
            double inner = (x - Cx) / 44 * ((x - Cx) / 44) + (y - Cy + 2) / 58 * ((y - Cy + 2) / 58);
            if (inner < 0.84 || head && inner < 0.98) continue;
            uint c = Lit(cloth, Light(u * 0.9, v * 0.8));
            if (Math.Abs(Math.Sin(Math.Atan2(v, u) * 7 + r * 3)) < 0.1) c = Pix.Scale(c, 0.8);
            p.Set(x, y, c);
        }
    }
}
