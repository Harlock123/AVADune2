namespace AVADune2.Engine;

/// <summary>
/// A* over the tiles, eight ways, with no cutting of corners past something solid. Sand is slower going than rock,
/// dunes slower still; mountains are for feet only. Buildings and walls block; units block only when asked (a unit
/// stuck behind others looks for a way round them). If the goal can't be reached, the way leads to the reachable tile
/// nearest it, so a unit sent at a building ends beside it. The arrays are kept between searches (a search marks
/// what it touches with its own number) so nothing is allocated but the path.
/// </summary>
public sealed class PathFinder
{
    private const int N = Field.N, Size = N * N;
    private readonly int[] _cost = new int[Size], _from = new int[Size], _mark = new int[Size], _closed = new int[Size];
    private readonly int[] _heap = new int[Size * 8];
    private readonly int[] _heapKey = new int[Size * 8];
    private int _stamp, _count;

    /// <summary>The searches made, and the tiles they looked at (for the tests and the probe).</summary>
    public int Searches, Expanded;

    private static readonly int[] Dx = [1, -1, 0, 0, 1, 1, -1, -1];
    private static readonly int[] Dy = [0, 0, 1, -1, 1, -1, 1, -1];

    /// <summary>Whether a mover can stand on a tile, ignoring units.</summary>
    public static bool Passable(World w, int x, int y, Mover m)
    {
        if (!Field.In(x, y)) return false;
        if (m == Mover.Air) return true;
        if (w.Bld[x, y] is not null) return false;
        return w.Ground[x, y] != Ground.Mountain || m == Mover.Foot;
    }

    /// <summary>The cost of stepping onto a tile: rock is quickest, then sand, then dunes.</summary>
    public static int StepCost(World w, int x, int y, Mover m)
    {
        if (m == Mover.Air) return 10;
        if (w.Slab[x, y] != 0) return 10;
        return w.Ground[x, y] switch
        {
            Ground.Rock => 10,
            Ground.Mountain => 16,
            Ground.Dunes => m == Mover.Foot ? 13 : 17,
            _ => m == Mover.Wheels ? 11 : 12,
        };
    }

    /// <summary>How fast a mover goes over a tile, as a fraction of its best.</summary>
    public static double SpeedOn(World w, int x, int y, Mover m) => 10.0 / StepCost(w, x, y, m) * (m == Mover.Wheels && w.Ground[x, y] == Ground.Dunes ? 0.85 : 1);

    /// <summary>
    /// The way from (sx, sy) to (gx, gy) as packed tiles, not counting the start; empty if already there or there's
    /// nowhere better to go. <paramref name="avoid"/> treats tiles held by other standing units as blocked.
    /// </summary>
    public List<int> Find(World w, Unit? self, int sx, int sy, int gx, int gy, Mover m, bool avoid = false, int limit = Size)
    {
        Searches++;
        var path = new List<int>();
        gx = Math.Clamp(gx, 0, N - 1);
        gy = Math.Clamp(gy, 0, N - 1);
        if (sx == gx && sy == gy) return path;
        _stamp++;
        if (_stamp == int.MaxValue) { Array.Clear(_mark); Array.Clear(_closed); _stamp = 1; }
        _count = 0;
        int start = sy * N + sx, goal = gy * N + gx;
        _mark[start] = _stamp;
        _cost[start] = 0;
        _from[start] = -1;
        Push(start, H(sx, sy, gx, gy));
        int best = start, bestH = H(sx, sy, gx, gy), seen = 0;
        while (_count > 0)
        {
            int cur = Pop();
            if (_closed[cur] == _stamp) continue;
            _closed[cur] = _stamp;
            Expanded++;
            if (cur == goal) { best = cur; break; }
            if (++seen > limit) break;
            int cx = cur % N, cy = cur / N;
            int h = H(cx, cy, gx, gy);
            if (h < bestH || h == bestH && _cost[cur] < _cost[best]) { bestH = h; best = cur; }
            for (int d = 0; d < 8; d++)
            {
                int nx = cx + Dx[d], ny = cy + Dy[d];
                if (!Field.In(nx, ny)) continue;
                int ni = ny * N + nx;
                if (_closed[ni] == _stamp) continue;
                if (!Open(w, self, nx, ny, m, avoid, ni == goal)) continue;
                // No squeezing diagonally between two solid tiles.
                if (d >= 4 && (!Open(w, self, cx + Dx[d], cy, m, false, false) || !Open(w, self, cx, cy + Dy[d], m, false, false))) continue;
                int step = StepCost(w, nx, ny, m) * (d >= 4 ? 14 : 10) / 10;
                int c = _cost[cur] + step;
                if (_mark[ni] == _stamp && c >= _cost[ni]) continue;
                _mark[ni] = _stamp;
                _cost[ni] = c;
                _from[ni] = cur;
                Push(ni, c + H(nx, ny, gx, gy));
            }
        }
        for (int at = best; at != start && at >= 0; at = _from[at]) path.Add(at);
        path.Reverse();
        return path;
    }

    private static bool Open(World w, Unit? self, int x, int y, Mover m, bool avoid, bool isGoal)
    {
        if (!Passable(w, x, y, m)) return false;
        if (avoid && !isGoal && w.Occ[x, y] is { } u && u != self && !u.Moving) return false;
        return true;
    }

    /// <summary>The octile distance, in the same units as the steps (rock: 10 a tile).</summary>
    private static int H(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
    }

    private void Push(int node, int key)
    {
        if (_count >= _heap.Length) return;
        int i = _count++;
        while (i > 0)
        {
            int p = (i - 1) / 2;
            if (_heapKey[p] <= key) break;
            _heap[i] = _heap[p];
            _heapKey[i] = _heapKey[p];
            i = p;
        }
        _heap[i] = node;
        _heapKey[i] = key;
    }

    private int Pop()
    {
        int top = _heap[0];
        int last = _heap[--_count], lastKey = _heapKey[_count];
        int i = 0;
        while (true)
        {
            int c = 2 * i + 1;
            if (c >= _count) break;
            if (c + 1 < _count && _heapKey[c + 1] < _heapKey[c]) c++;
            if (_heapKey[c] >= lastKey) break;
            _heap[i] = _heap[c];
            _heapKey[i] = _heapKey[c];
            i = c;
        }
        _heap[i] = last;
        _heapKey[i] = lastKey;
        return top;
    }
}
