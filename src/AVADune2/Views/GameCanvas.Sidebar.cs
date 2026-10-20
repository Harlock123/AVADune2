using Avalonia;
using Avalonia.Media;
using AVADune2.Engine;

namespace AVADune2.Views;

public sealed partial class GameCanvas
{
    public static readonly Rect RadarRect = new(816, 40, 192, 192);
    private static readonly Rect PowerRect = new(812, 240, 200, 16);
    private static readonly Rect InfoRect = new(812, 262, 200, 116);
    private const double GridX = 813, GridY = 389, Cell = 46, Gap = 3;
    private const int Cols = 4, Rows = 5;
    private static readonly Rect StripRect = new(812, 636, 200, 60);
    private static readonly Rect SlowerRect = new(812, 702, 28, 26), FasterRect = new(984, 702, 28, 26);

    /// <summary>A command button in the info panel: (label, key hint, rect).</summary>
    private static Rect CommandRect(int i) => new(816 + i * 49, 344, 46, 22);

    public static Rect CellRect(int i) => new(GridX + i % Cols * (Cell + Gap), GridY + i / Cols * (Cell + Gap), Cell, Cell);

    private List<(string Label, Action Do, bool On)> Commands()
    {
        var g = Engine;
        var list = new List<(string, Action, bool)>();
        var units = g.Selected.Where(u => !u.Dead).ToList();
        if (units.Count > 0)
        {
            list.Add(("ATTACK", g.ArmAttack, units.Any(u => Rules.Armed(u.Type))));
            list.Add(("STOP", g.Stop, true));
            list.Add(("GUARD", g.Guard, true));
            if (units.Any(u => u.Type == Kind.Mcv)) list.Add(("DEPLOY", g.Deploy, true));
            else if (units.Any(u => u.Type == Kind.Juggernaut)) list.Add(("BLAST", g.Deploy, true));
            else if (units.Any(u => u.Type == Kind.Harvester)) list.Add(("HOME", () => { foreach (var u in units.Where(u => u.Type == Kind.Harvester)) Sim.Command(g.World, u, Order.Return, u.Tx, u.Ty); }, true));
        }
        else if (g.SelectedBuilding is { Dead: false } b && b.Owner == World.Player)
        {
            list.Add(("REPAIR", g.ToggleRepair, b.Hp < b.MaxHp));
            if (b.Type == Kind.Palace) list.Add(("LAUNCH", g.LaunchPalace, g.World.PlayerHouse.PalaceReady));
        }
        return list;
    }

    /// <summary>A click in the sidebar (or on the radar): true if it was used.</summary>
    private bool SidebarClick(Point at, bool right)
    {
        var g = Engine;
        if (at.X < Field.SideX && at.Y >= Field.TopBar) return false;
        if (RadarRect.Contains(at))
        {
            if (!right && g.World.PlayerHouse.Radar) { _radarDrag = true; RadarLook(at); }
            return true;
        }
        var cmds = Commands();
        for (int i = 0; i < cmds.Count; i++)
            if (CommandRect(i).Contains(at) && cmds[i].On) { cmds[i].Do(); return true; }
        var menu = g.Menu();
        int first = Math.Clamp(_menuScroll, 0, Math.Max(0, (menu.Count - Cols * Rows + Cols - 1) / Cols)) * Cols;
        for (int i = 0; i < Cols * Rows && first + i < menu.Count; i++)
        {
            if (!CellRect(i).Contains(at)) continue;
            if (right) g.Unbuild(menu[first + i]);
            else g.Build(menu[first + i]);
            return true;
        }
        if (SlowerRect.Contains(at)) { g.Slower(); return true; }
        if (FasterRect.Contains(at)) { g.Faster(); return true; }
        return true;
    }

    private void DrawSidebar(DrawingContext dc, World w)
    {
        var g = Engine;
        var h = w.PlayerHouse;
        var side = new Rect(Field.SideX, Field.TopBar, Field.Width - Field.SideX, Field.Height - Field.TopBar);
        dc.FillRectangle(Brush(0xFF2A2118), side);
        for (int y = (int)side.Y; y < side.Bottom; y += 4) dc.DrawLine(Pen(0x14000000, 1), new Point(side.X, y), new Point(side.Right, y));
        dc.DrawLine(Pen(Shade(h.Colour, 0.8), 3), new Point(side.X + 1, side.Y), new Point(side.X + 1, side.Bottom));
        // The radar.
        var frame = RadarRect.Inflate(4);
        dc.FillRectangle(Brush(0xFF0A0806), frame, 4);
        dc.DrawRectangle(null, Pen(0xFF8A6A3A, 2), frame, 4, 4);
        if (h.Radar)
        {
            using (dc.PushRenderOptions(Crisp)) dc.DrawImage(Painter.Radar, RadarRect);
            double k = RadarRect.Width / Field.N;
            dc.DrawRectangle(null, Pen(0xFFFFFFFF, 1.2), new Rect(RadarRect.X + g.CamX * k, RadarRect.Y + g.CamY * k, Field.ViewW * k, Field.ViewH * k));
        }
        else
        {
            for (int i = 0; i < 220; i++)
            {
                uint r = Hash(i, (int)(_clock * 12), 5);
                dc.FillRectangle(Brush(0xFF000000 | (r & 0x3F) * 0x010101 + 0x202020), new Rect(RadarRect.X + r % 188, RadarRect.Y + (r >> 8) % 188, 4, 4));
            }
            bool outpost = w.Has(World.Player, Kind.Outpost);
            Text(dc, outpost ? "LOW POWER" : "NO RADAR", RadarRect.Center + new Point(0, -12), 30, outpost ? Red : Dim, center: true);
            Text(dc, outpost ? "BUILD WIND TURBINES" : "BUILD A RADAR OUTPOST", RadarRect.Center + new Point(0, 16), 18, Dim, center: true);
        }
        // Power.
        dc.FillRectangle(Brush(0xFF0A0806), PowerRect, 3);
        double used = h.PowerUsed, made = h.PowerMade, f = made <= 0 ? (used > 0 ? 1 : 0) : Math.Min(1, used / made);
        dc.FillRectangle(Brush(h.LowPower ? 0xFFD83A2A : 0xFF4AB04A), new Rect(PowerRect.X, PowerRect.Y, PowerRect.Width * f, PowerRect.Height), 3);
        Text(dc, $"POWER {(int)used} / {(int)made}", PowerRect.Center, 18, White, center: true);
        DrawInfo(dc, w);
        DrawMenu(dc, w);
        // Game speed.
        Button(dc, SlowerRect, "-", size: 24);
        Button(dc, FasterRect, "+", size: 24);
        Text(dc, $"SPEED {GameEngine.SpeedNames[g.SpeedIndex]}", new Point(912, 715), 20, Sandy, center: true);
        Text(dc, "P PAUSE   M SOUND   SPACE ALERT", new Point(912, 744), 16, Dim, center: true);
    }

    private void DrawInfo(DrawingContext dc, World w)
    {
        var g = Engine;
        Panel(dc, InfoRect, 0xFF6A5232, 0xFF1A140E);
        Thing? t = g.Selected.FirstOrDefault(u => !u.Dead) ?? (Thing?)g.SelectedBuilding ?? g.Inspected;
        if (t is null)
        {
            var h = w.PlayerHouse;
            DrawEmblem(dc, h.Faction, new Point(InfoRect.X + 34, InfoRect.Y + 40), 26);
            Text(dc, Rules.HouseName(h.Faction), new Point(InfoRect.X + 68, InfoRect.Y + 26), 22, Sandy);
            Text(dc, $"STORAGE {(int)h.Credits} / {h.Capacity}", new Point(InfoRect.X + 68, InfoRect.Y + 48), 18, Dim);
            Text(dc, "CLICK OR DRAG TO PICK OUT UNITS", new Point(InfoRect.Center.X, InfoRect.Y + 76), 16, Dim, center: true);
            Text(dc, "RIGHT-CLICK TO ORDER THEM", new Point(InfoRect.Center.X, InfoRect.Y + 92), 16, Dim, center: true);
            return;
        }
        var house = w.Houses[t.Owner];
        var icon = new Rect(InfoRect.X + 8, InfoRect.Y + 8, 46, 46);
        dc.FillRectangle(Brush(0xFF0A0806), icon, 3);
        DrawIcon(dc, t.Kind, icon, house.Colour);
        int count = g.Selected.Count(u => !u.Dead);
        string name = t.Stats.Name + (count > 1 ? $" +{count - 1}" : "");
        Text(dc, name, new Point(InfoRect.X + 60, InfoRect.Y + 16), name.Length > 16 ? 17 : 20, t.Owner == World.Player ? White : Red);
        Text(dc, t.Owner == World.Player ? Rules.HouseName(house.Faction) : "ENEMY - " + Rules.Name(house.Faction), new Point(InfoRect.X + 60, InfoRect.Y + 34), 15, Dim);
        HealthBar(dc, new Rect(InfoRect.X + 60, InfoRect.Y + 46, 132, 6), t.Hp / t.MaxHp);
        Text(dc, Status(w, t), new Point(InfoRect.X + 10, InfoRect.Y + 66), 17, Sandy);
        if (t.Owner != World.Player) return;
        var cmds = Commands();
        for (int i = 0; i < cmds.Count; i++)
            Button(dc, CommandRect(i), cmds[i].Label, cmds[i].On, 15, lit: cmds[i].Label == "ATTACK" && g.ArmedAttack);
    }

    private static string Status(World w, Thing t)
    {
        if (t is Building b)
        {
            if (b.Type == Kind.Palace && b.Owner == World.Player)
            {
                var h = w.Houses[b.Owner];
                return h.PalaceReady ? $"{Rules.PalacePower(h.Faction)} READY (L)" : $"{Rules.PalacePower(h.Faction)} {(int)(h.PalaceCharge * 100)}%";
            }
            if (b.Repairing) return "REPAIRING";
            if (b.Ready is { } r) return $"{Rules.Of(r).Name} READY";
            if (b.Queue.Count > 0) return $"MAKING {Rules.Of(b.Queue[0]).Name} {(int)(b.Progress * 100)}%";
            if (b.Footing < 1 && b.Type != Kind.Wall) return $"FOOTING {(int)(b.Footing * 100)}% CONCRETE";
            return $"HEALTH {(int)b.Hp} / {(int)b.MaxHp}";
        }
        var u = (Unit)t;
        return u.Order switch
        {
            Order.Harvest when u.Type == Kind.Harvester => $"HARVESTING  LOAD {(int)(u.Cargo / Rules.HarvesterLoad * 100)}%",
            Order.Return => $"GOING HOME  LOAD {(int)(u.Cargo / Rules.HarvesterLoad * 100)}%",
            Order.Docked => $"UNLOADING  {(int)u.Cargo}",
            Order.Attack or Order.AttackMove => "ATTACKING",
            Order.Move => "MOVING",
            Order.Guard => "ON GUARD",
            Order.Repair => "TO THE REPAIR PAD",
            Order.Sabotage => "ON A MISSION",
            _ => u.CarriedBy is not null ? "IN THE AIR" : u.Turned > 0 ? $"BEGUILED {(int)u.Turned}s" : "READY",
        };
    }

    /// <summary>The menu: what the picked-out factory (or the yard, or the starport) offers, with prices, progress
    /// and how many are queued; and under it, what's being made or what's under the mouse.</summary>
    private void DrawMenu(DrawingContext dc, World w)
    {
        var g = Engine;
        var f = g.Factory;
        var menu = g.Menu();
        var h = w.PlayerHouse;
        int pages = Math.Max(0, (menu.Count - Cols * Rows + Cols - 1) / Cols);
        _menuScroll = Math.Clamp(_menuScroll, 0, pages);
        int first = _menuScroll * Cols;
        Text(dc, f is null ? "NO CONSTRUCTION YARD" : f.Stats.Name, new Point(912, 378), 18, Sandy, center: true);
        Kind? hover = null;
        for (int i = 0; i < Cols * Rows; i++)
        {
            var r = CellRect(i);
            dc.FillRectangle(Brush(0xFF14100C), r, 3);
            if (first + i >= menu.Count) continue;
            var k = menu[first + i];
            bool over = r.Contains(_mouse);
            if (over) hover = k;
            DrawIcon(dc, k, r.Deflate(3), h.Colour);
            int price = f?.Type == Kind.Starport ? Sim.Price(w, k) : Rules.Of(k).Cost;
            bool afford = h.Credits >= price * (f?.Type == Kind.Starport ? 1 : 0.1);
            Text(dc, $"{price}", new Point(r.Right - 3, r.Bottom - 8), 15, afford ? Yellow : Red, right: true);
            if (f is not null && f.Queue.Count > 0 && f.Queue[0] == k)
            {
                dc.FillRectangle(Brush(0xA0000000), new Rect(r.X, r.Y, r.Width, r.Height * (1 - f.Progress)));
                Text(dc, $"{(int)(f.Progress * 100)}%", new Point(r.Center.X, r.Y + 14), 18, White, center: true);
            }
            int queued = f?.Type == Kind.Starport ? h.Order.Count(o => o == k) : f?.Queue.Count(q => q == k) ?? 0;
            if (queued > (f?.Queue.FirstOrDefault() == k && f.Type != Kind.Starport ? 1 : 0) || f?.Type == Kind.Starport && queued > 0)
            {
                dc.FillEllipse(r.X + 8, r.Y + 8, 7, 0xFF8A2A1A);
                Text(dc, $"{queued}", new Point(r.X + 8, r.Y + 8), 15, White, center: true, shadow: false);
            }
            if (f?.Ready == k)
            {
                bool blink = (int)(_clock * 3) % 2 == 0;
                dc.FillRectangle(Brush(blink ? 0x9030A030 : 0x60208020), r);
                Text(dc, "READY", new Point(r.Center.X, r.Center.Y - 4), 17, White, center: true);
            }
            dc.DrawRectangle(null, Pen(over ? Yellow : f?.Ready == k ? Green : 0xFF5A4630, over ? 2 : 1), r, 3, 3);
        }
        if (pages > 0) Text(dc, $"WHEEL: MORE ({_menuScroll + 1}/{pages + 1})", new Point(912, 632), 14, Dim, center: true);
        // The strip under the menu.
        Panel(dc, StripRect, 0xFF6A5232, 0xFF1A140E);
        if (hover is { } hk)
        {
            var st = Rules.Of(hk);
            Text(dc, st.Name, new Point(StripRect.X + 8, StripRect.Y + 12), 18, White);
            var lines = Wrap(st.Blurb, 15, StripRect.Width - 16);
            for (int i = 0; i < Math.Min(3, lines.Count); i++) Text(dc, lines[i], new Point(StripRect.X + 8, StripRect.Y + 28 + i * 12), 15, Dim);
        }
        else if (f?.Type == Kind.Starport)
        {
            Text(dc, h.Frigate > 0 ? $"FRIGATE LANDS IN {(int)h.Frigate}s" : "BUY AT TODAY'S PRICES", new Point(StripRect.X + 8, StripRect.Y + 16), 18, White);
            Text(dc, $"NEW PRICES IN {(int)Math.Max(0, w.PricesAt - w.Clock)}s", new Point(StripRect.X + 8, StripRect.Y + 38), 16, Dim);
        }
        else if (f is { } fac && (fac.Ready is { } ready))
        {
            Text(dc, $"{Rules.Of(ready).Name} READY", new Point(StripRect.X + 8, StripRect.Y + 16), 18, Green);
            Text(dc, g.Placing ? "CLICK ON ROCK BY YOUR BASE" : "CLICK IT, THEN PLACE IT", new Point(StripRect.X + 8, StripRect.Y + 38), 16, Dim);
        }
        else if (f is { Queue.Count: > 0 } busy)
        {
            Text(dc, $"{Rules.Of(busy.Queue[0]).Name}", new Point(StripRect.X + 8, StripRect.Y + 14), 18, White);
            var bar = new Rect(StripRect.X + 8, StripRect.Y + 28, StripRect.Width - 16, 10);
            dc.FillRectangle(Brush(0xFF0A0806), bar, 2);
            dc.FillRectangle(Brush(h.LowPower ? 0xFFD8A02A : 0xFF4AB04A), new Rect(bar.X, bar.Y, bar.Width * busy.Progress, bar.Height), 2);
            Text(dc, busy.Queue.Count > 1 ? $"{busy.Queue.Count - 1} MORE QUEUED" : h.LowPower ? "SLOWED: LOW POWER" : "RIGHT-CLICK TO CANCEL", new Point(StripRect.X + 8, StripRect.Y + 48), 15, Dim);
        }
        else Text(dc, f is null ? "" : "LEFT-CLICK TO BUILD", new Point(StripRect.X + 8, StripRect.Y + 22), 16, Dim);
    }

    /// <summary>A thing's picture for a menu or the info panel: a building's own picture, or the unit drawn large.</summary>
    private void DrawIcon(DrawingContext dc, Kind k, Rect r, uint house)
    {
        if (Rules.IsBuilding(k))
        {
            var st = Rules.Of(k);
            if (k == Kind.Slab)
            {
                for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    var t = new Rect(r.X + 4 + i * (r.Width - 8) / 2, r.Y + 4 + j * (r.Height - 8) / 2, (r.Width - 8) / 2 - 1, (r.Height - 8) / 2 - 1);
                    dc.FillRectangle(Brush(0xFF9C9A94), t);
                    dc.DrawLine(Pen(0xFFBAB8B2, 1), t.TopLeft, t.TopRight);
                }
                return;
            }
            var bmp = BuildingArt.Get(k, house);
            double sw = st.W * T, sh = st.H * T + BuildingArt.Over, s = Math.Min(r.Width / sw, r.Height / sh);
            var dst = new Rect(r.Center.X - sw * s / 2, r.Center.Y - sh * s / 2, sw * s, sh * s);
            dc.DrawImage(bmp, dst);
            return;
        }
        double scale = k is Kind.Infantry or Kind.Troopers or Kind.Nomads or Kind.Praetors or Kind.Saboteur ? 2.6 : k == Kind.Skylifter ? 1.2 : k is Kind.Juggernaut or Kind.Mcv ? 1.3 : 1.5;
        using (dc.PushTransform(Matrix.CreateRotation(-0.5) * Matrix.CreateTranslation(r.Center.X, r.Center.Y)))
            Body(dc, k, house, scale, 0, _clock, false, 1, 1, k == Kind.Harvester ? 0.6 : 0);
    }
}
