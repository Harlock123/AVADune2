using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AVADune2.Engine;

namespace AVADune2.Views;

/// <summary>
/// The battlefield's ground as one picture, 32 pixels a tile, drawn pixel by pixel and kept up to date tile by tile:
/// when the ground changes, every tile's key is worked out and only the tiles whose keys changed are drawn again. The
/// edges between sand, rock and mountain, and the drifts of solarite, are drawn from values blended between the tiles'
/// middles and roughened with noise, so they wander naturally rather than following the grid. Also the radar map, and
/// the shroud as a small picture stretched (and smoothed) over the view so its edges are soft.
/// </summary>
public sealed class MapPainter
{
    public const int T = Field.Tile, N = Field.N;
    public WriteableBitmap Map { get; } = Bitmap(N * T, N * T);
    public WriteableBitmap Radar { get; } = Bitmap(N, N);
    /// <summary>The shroud, four pixels a tile.</summary>
    public const int ShroudScale = 4;
    public WriteableBitmap Shroud { get; } = Bitmap(N * ShroudScale, N * ShroudScale);

    private readonly ulong[] _keys = new ulong[N * N];
    private readonly float[,] _rock = new float[N, N], _mount = new float[N, N], _dune = new float[N, N], _ore = new float[N, N];
    private World? _world;
    private int _version = -1, _seenCount = -1, _radarFrame;

    private static WriteableBitmap Bitmap(int w, int h) => new(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    public MapPainter() => Array.Fill(_keys, ulong.MaxValue);

    // ───────────────────────────── the palette ─────────────────────────────

    private const uint SandLight = 0xFFE9BC78, Sand = 0xFFD6A15C, SandDark = 0xFFB98446;
    private const uint DuneLight = 0xFFF2CC8C, DuneDark = 0xFFA47038;
    private const uint RockLight = 0xFFA09280, Rock = 0xFF847664, RockDark = 0xFF5E5446, RockRim = 0xFF4C4338;
    private const uint MountLight = 0xFF9A8C78, Mount = 0xFF6A5E50, MountDark = 0xFF3C342C;
    private const uint OreLight = 0xFFE8742A, Ore = 0xFFCF5320, OreDark = 0xFFA2361A, Spark = 0xFFFFD27A;
    private const uint Slab = 0xFF9C9A94, SlabLight = 0xFFBAB8B2, SlabSeam = 0xFF6C6A66;

    // ───────────────────────────── noise ─────────────────────────────

    private const int NS = 256;
    private static readonly float[] Fine = Texture(1), Medium = Texture(16), Large = Texture(64), Ridge = Ridges();

    /// <summary>Value noise that wraps every 256 pixels, with a cell of <paramref name="cell"/> pixels.</summary>
    private static float[] Texture(int cell)
    {
        var t = new float[NS * NS];
        int cells = NS / cell;
        for (int y = 0; y < NS; y++)
        for (int x = 0; x < NS; x++)
        {
            if (cell == 1) { t[y * NS + x] = (float)Pix.Rand(x, y, 91); continue; }
            double fx = (double)x / cell, fy = (double)y / cell;
            int ix = (int)fx, iy = (int)fy;
            double u = fx - ix, v = fy - iy;
            u = u * u * (3 - 2 * u);
            v = v * v * (3 - 2 * v);
            double a = Pix.Rand(ix % cells, iy % cells, cell), b = Pix.Rand((ix + 1) % cells, iy % cells, cell);
            double c = Pix.Rand(ix % cells, (iy + 1) % cells, cell), d = Pix.Rand((ix + 1) % cells, (iy + 1) % cells, cell);
            t[y * NS + x] = (float)(a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v);
        }
        return t;
    }

    /// <summary>The dunes' ridges: long gentle slopes to windward, short steep ones in the lee.</summary>
    private static float[] Ridges()
    {
        var t = new float[NS * NS];
        var large = Texture(64);
        for (int y = 0; y < NS; y++)
        for (int x = 0; x < NS; x++)
        {
            // Whole numbers of ridges across the texture, so it wraps without a seam.
            double r = (x * 5 + y * 3) / (double)NS + large[y * NS + x] * 1.6;
            t[y * NS + x] = (float)(r - Math.Floor(r));
        }
        return t;
    }

    private static float At(float[] tex, int x, int y) => tex[(y & (NS - 1)) * NS + (x & (NS - 1))];

    // ───────────────────────────── the ground ─────────────────────────────

    private static int OreBucket(int ore) => Math.Min(7, (ore + 89) / 90);

    /// <summary>Everything about how a tile looks, in one number: its ground and its neighbours', their solarite, its
    /// concrete and its neighbours', a bloom, and how scorched it is.</summary>
    private static ulong Key(World w, int x, int y)
    {
        ulong k = 0;
        for (int j = -1; j <= 1; j++)
        for (int i = -1; i <= 1; i++)
        {
            int u = Math.Clamp(x + i, 0, N - 1), v = Math.Clamp(y + j, 0, N - 1);
            k = k * 4 + (ulong)w.Ground[u, v];
            k = k * 8 + (ulong)OreBucket(w.Ore[u, v]);
        }
        k = k * 2 + (w.Slab[x, y] != 0 ? 1UL : 0);
        for (int d = 0; d < 4; d++)
        {
            int u = x + (d == 0 ? 1 : d == 1 ? -1 : 0), v = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
            k = k * 2 + (Field.In(u, v) && w.Slab[u, v] != 0 ? 1UL : 0);
        }
        k = k * 2 + (w.Bloom[x, y] ? 1UL : 0);
        k = k * 4 + (ulong)Math.Min(3, (int)(w.Scorch[x, y] * 4));
        return k;
    }

    /// <summary>Redraws the tiles that have changed; true if any did.</summary>
    public unsafe bool Update(World w)
    {
        if (!ReferenceEquals(w, _world))
        {
            _world = w;
            Array.Fill(_keys, ulong.MaxValue);
            _version = -1;
            _seenCount = -1;
        }
        if (w.GroundVersion == _version) return false;
        _version = w.GroundVersion;
        for (int x = 0; x < N; x++)
        for (int y = 0; y < N; y++)
        {
            var g = w.Ground[x, y];
            _rock[x, y] = g is Ground.Rock or Ground.Mountain ? 1 : 0;
            _mount[x, y] = g == Ground.Mountain ? 1 : 0;
            _dune[x, y] = g == Ground.Dunes ? 1 : 0;
            _ore[x, y] = Math.Min(1f, w.Ore[x, y] / 520f);
        }
        bool any = false;
        using var fb = Map.Lock();
        uint* dst = (uint*)fb.Address;
        int stride = fb.RowBytes / 4;
        for (int x = 0; x < N; x++)
        for (int y = 0; y < N; y++)
        {
            ulong key = Key(w, x, y);
            if (_keys[y * N + x] == key) continue;
            _keys[y * N + x] = key;
            any = true;
            PaintTile(w, x, y, dst, stride);
        }
        return any;
    }

    /// <summary>A field's value at a point, blended between the middles of the four nearest tiles.</summary>
    private static float Sample(float[,] f, double fx, double fy)
    {
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double u = fx - x0, v = fy - y0;
        float F(int x, int y) => f[Math.Clamp(x, 0, N - 1), Math.Clamp(y, 0, N - 1)];
        return (float)(F(x0, y0) * (1 - u) * (1 - v) + F(x0 + 1, y0) * u * (1 - v) + F(x0, y0 + 1) * (1 - u) * v + F(x0 + 1, y0 + 1) * u * v);
    }

    private unsafe void PaintTile(World w, int tx, int ty, uint* dst, int stride)
    {
        bool slab = w.Slab[tx, ty] != 0;
        float scorch = w.Scorch[tx, ty];
        for (int j = 0; j < T; j++)
        for (int i = 0; i < T; i++)
        {
            int px = tx * T + i, py = ty * T + j;
            double fx = (px + 0.5) / T - 0.5, fy = (py + 0.5) / T - 0.5;
            float fine = At(Fine, px, py), med = At(Medium, px, py), large = At(Large, px, py);
            uint c;
            if (slab) c = SlabPixel(w, tx, ty, i, j, fine);
            else
            {
                double rock = Sample(_rock, fx, fy) + (med - 0.5) * 0.42 + (fine - 0.5) * 0.06;
                if (rock > 0.5)
                {
                    double mount = Sample(_mount, fx, fy) + (At(Medium, px + 97, py + 31) - 0.5) * 0.45;
                    if (mount > 0.5)
                    {
                        // Craggy: lit from the top-left by the slope of the noise.
                        double slope = At(Medium, px - 2, py - 2) - At(Medium, px + 2, py + 2) + (At(Large, px - 3, py - 3) - At(Large, px + 3, py + 3)) * 2;
                        c = slope > 0.04 ? Pix.Mix(Mount, MountLight, Math.Min(1, slope * 6)) : Pix.Mix(Mount, MountDark, Math.Min(1, -slope * 7));
                        if (mount < 0.56) c = Pix.Mix(c, RockDark, 0.5);
                        c = Pix.Scale(c, 0.92 + fine * 0.16);
                    }
                    else
                    {
                        c = Pix.Mix(RockDark, RockLight, med * 0.7 + large * 0.3);
                        if (fine > 0.93) c = Pix.Scale(c, 0.8);
                        // Cracks.
                        double crack = Math.Abs(At(Medium, px * 2, py * 2) - 0.5);
                        if (crack < 0.018) c = Pix.Scale(c, 0.72);
                        c = Pix.Scale(c, 0.94 + fine * 0.1);
                        // The rim of the plateau, lit on the top-left, in shadow on the bottom-right.
                        if (rock < 0.57)
                        {
                            double toward = Sample(_rock, fx - 0.3, fy - 0.3) - Sample(_rock, fx + 0.3, fy + 0.3);
                            c = toward < 0 ? Pix.Mix(c, RockLight, 0.5) : Pix.Mix(c, RockRim, 0.6);
                        }
                    }
                }
                else
                {
                    double ripple = 0.5 + 0.5 * Math.Sin((px * 0.62 + py * 0.36) / 2.6 + large * 9);
                    c = Pix.Mix(SandDark, SandLight, 0.35 + ripple * 0.25 + (med - 0.5) * 0.5 + (fine - 0.5) * 0.12);
                    double dune = Sample(_dune, fx, fy) + (large - 0.5) * 0.5;
                    if (dune > 0.42)
                    {
                        float r = At(Ridge, px, py);
                        uint dc = r < 0.72 ? Pix.Mix(Sand, DuneLight, r / 0.72) : Pix.Mix(DuneDark, Sand, (r - 0.72) / 0.28 * 0.5);
                        c = Pix.Mix(c, dc, Math.Min(1, (dune - 0.42) * 4));
                    }
                    // In the shadow of a plateau's edge.
                    if (rock > 0.4)
                    {
                        double toward = Sample(_rock, fx - 0.35, fy - 0.35);
                        if (toward > 0.45) c = Pix.Scale(c, 0.82);
                    }
                    // Solarite: drifts of glowing orange grains, thicker where there's more.
                    double ore = Sample(_ore, fx, fy);
                    if (ore > 0.02)
                    {
                        double grains = med * 0.7 + At(Medium, px * 3 + 50, py * 3 + 70) * 0.2 + fine * 0.1;
                        double cover = ore * 1.15 + 0.12;
                        if (grains < cover)
                        {
                            double deep = Math.Min(1, (cover - grains) * 2.2);
                            c = Pix.Mix(c, deep > 0.6 ? Pix.Mix(Ore, OreDark, (deep - 0.6) * 1.8) : Pix.Mix(OreLight, Ore, deep / 0.6), 0.55 + deep * 0.45);
                            if (fine > 0.992 - ore * 0.02) c = Spark;
                        }
                    }
                }
            }
            if (scorch > 0)
            {
                double blot = scorch * 1.2 - Math.Abs(fx - tx - 0.5 + (med - 0.5) * 0.5) - Math.Abs(fy - ty - 0.5 + (large - 0.5) * 0.5);
                if (blot > 0) c = Pix.Scale(c, 1 - Math.Min(0.55, blot * 0.9));
            }
            dst[py * stride + px] = c;
        }
        if (w.Bloom[tx, ty]) PaintBloom(tx, ty, dst, stride);
    }

    /// <summary>Concrete: four plates a tile, with seams, bevelled where the slab ends.</summary>
    private static uint SlabPixel(World w, int tx, int ty, int i, int j, float fine)
    {
        uint c = Pix.Scale(Slab, 0.95 + fine * 0.08);
        if (i % 16 == 0 || j % 16 == 0) c = SlabSeam;
        else if (i % 16 == 1 || j % 16 == 1) c = SlabLight;
        bool left = tx > 0 && w.Slab[tx - 1, ty] != 0, up = ty > 0 && w.Slab[tx, ty - 1] != 0;
        bool right = tx < N - 1 && w.Slab[tx + 1, ty] != 0, down = ty < N - 1 && w.Slab[tx, ty + 1] != 0;
        if (!left && i < 2 || !up && j < 2) c = SlabLight;
        if (!right && i > T - 3 || !down && j > T - 3) c = Pix.Scale(Slab, 0.62);
        return c;
    }

    /// <summary>A bloom: a swollen mound of sand, crusted dark red, about to burst.</summary>
    private static unsafe void PaintBloom(int tx, int ty, uint* dst, int stride)
    {
        double cx = tx * T + 16, cy = ty * T + 17;
        for (int y = ty * T; y < ty * T + T; y++)
        for (int x = tx * T; x < tx * T + T; x++)
        {
            double u = (x + 0.5 - cx) / 11, v = (y + 0.5 - cy) / 9, d = u * u + v * v;
            if (d > 1) continue;
            double light = 1.1 - (u + v) * 0.35 - d * 0.3;
            uint c = Pix.Mix(0xFF8A2E2A, 0xFFD8743A, Math.Clamp(1 - d, 0, 1) * 0.6);
            if (At(Fine, x, y) > 0.9) c = 0xFFFFB050;
            if (Math.Abs(Math.Sin(Math.Atan2(v, u) * 5)) < 0.12 && d > 0.2) c = 0xFF4A1A1A;
            dst[y * stride + x] = Pix.Scale(c, light);
        }
    }

    // ───────────────────────────── the radar and the shroud ─────────────────────────────

    private static uint GroundColour(World w, int x, int y)
    {
        if (w.Slab[x, y] != 0) return 0xFF8E8C86;
        if (w.Ore[x, y] > 0) return w.Ore[x, y] > 350 ? 0xFFB8401A : 0xFFD8642A;
        return w.Ground[x, y] switch
        {
            Ground.Rock => 0xFF7A6E5E,
            Ground.Mountain => 0xFF4E453A,
            Ground.Dunes => 0xFFC08A48,
            _ => 0xFFD29C58,
        };
    }

    /// <summary>The radar map: the ground, the buildings and units in their Houses' colours, black where unseen.</summary>
    public unsafe void UpdateRadar(World w, bool force = false)
    {
        if (!force && _radarFrame++ % 12 != 0) return;
        using var fb = Radar.Lock();
        uint* dst = (uint*)fb.Address;
        int stride = fb.RowBytes / 4;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            uint c = GroundColour(w, x, y);
            if (w.Bld[x, y] is { } b) c = Pix.Scale(w.Houses[b.Owner].Colour, 0.85);
            if (w.Occ[x, y] is { } u && Sim.Visible(w, x, y)) c = Pix.Scale(w.Houses[u.Owner].Colour, 1.3);
            if (!Sim.Visible(w, x, y)) c = 0xFF000000;
            dst[y * stride + x] = c;
        }
    }

    /// <summary>The shroud: black where the player hasn't been, see-through where they have, with a ragged edge a
    /// little under a tile wide that wanders with the noise.</summary>
    public unsafe void UpdateShroud(World w)
    {
        int count = 0;
        for (int x = 0; x < N; x++)
        for (int y = 0; y < N; y++)
            if (w.RevealAll || w.Seen[x, y]) { _seen[x, y] = 1; count++; }
            else _seen[x, y] = 0;
        if (count == _seenCount) return;
        _seenCount = count;
        using var fb = Shroud.Lock();
        uint* dst = (uint*)fb.Address;
        int stride = fb.RowBytes / 4, n = N * ShroudScale;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            double fx = (x + 0.5) / ShroudScale - 0.5, fy = (y + 0.5) / ShroudScale - 0.5;
            double v = Sample(_seen, fx, fy) + (At(Medium, x * 8, y * 8) - 0.5) * 0.35;
            double a = Math.Clamp((0.62 - v) / 0.24, 0, 1);
            a = a * a * (3 - 2 * a);
            dst[y * stride + x] = (uint)(a * 255) << 24;
        }
    }

    private readonly float[,] _seen = new float[N, N];
}
