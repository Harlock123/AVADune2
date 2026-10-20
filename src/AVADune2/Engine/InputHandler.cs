using Avalonia.Input;

namespace AVADune2.Engine;

/// <summary>
/// The keys held, the mouse and the gamepad, boiled down to what the game needs each step: which way to scroll the map
/// (the arrows, the numeric pad or the gamepad's right stick and D-pad; up is -1, as on the screen and in the engine)
/// and where the mouse points. The keys that do one thing once (orders, groups, menus) are handled by the canvas as
/// they're pressed. The gamepad's left stick moves a cursor of its own; A is the left button, B the right.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager? _pad;
    private (double X, double Y)? _point;

    public InputHandler(bool gamepad = true) => _pad = gamepad ? new ControllerManager(0) : null;

    public bool MouseLeft { get; set; }
    public bool MouseRight { get; set; }

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public bool Shift => Any(Key.LeftShift, Key.RightShift);
    public bool Ctrl => Any(Key.LeftCtrl, Key.RightCtrl);

    /// <summary>Let go of everything (the window lost the focus).</summary>
    public void Clear()
    {
        _held.Clear();
        _pressedAs.Clear();
        MouseLeft = MouseRight = false;
    }

    /// <summary>The mouse moved to a point on the screen (or off it).</summary>
    public void PointAt((double X, double Y)? at) => _point = at;
    public (double X, double Y)? Point => _point;

    public void Update() => _pad?.Update();

    /// <summary>The newer of two opposite keys wins, so rolling from one to the other turns at once.</summary>
    private int Axis(Key[] minus, Key[] plus)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (minus.Contains(_held[i])) return -1;
            if (plus.Contains(_held[i])) return 1;
        }
        return 0;
    }

    /// <summary>The controls this step. Up is -1, as on the screen (and in the engine).</summary>
    public Controls Player
    {
        get
        {
            int dx = Axis([Key.Left, Key.NumPad4], [Key.Right, Key.NumPad6]);
            int dy = Axis([Key.Up, Key.NumPad8], [Key.Down, Key.NumPad2]);
            if (_pad is { IsConnected: true } pad)
            {
                if (dx == 0) dx = pad.DPadLeft || pad.RightStickX < -0.4f ? -1 : pad.DPadRight || pad.RightStickX > 0.4f ? 1 : 0;
                if (dy == 0) dy = pad.DPadUp || pad.RightStickY < -0.4f ? -1 : pad.DPadDown || pad.RightStickY > 0.4f ? 1 : 0;
            }
            return new Controls(dx, dy, _point);
        }
    }

    /// <summary>Enter, or the gamepad's Start.</summary>
    public bool Start => Any(Key.Enter) || (_pad?.ButtonStart ?? false);

    /// <summary>The gamepad's left stick, for its cursor (-1 to 1 each way; up is -1).</summary>
    public (double X, double Y) PadStick => _pad is { IsConnected: true } p ? (Dead(p.LeftStickX), Dead(p.LeftStickY)) : (0, 0);
    private static double Dead(float v) => Math.Abs(v) < 0.18 ? 0 : v;
    public bool PadConnected => _pad?.IsConnected ?? false;
    public bool PadA => _pad?.ButtonA ?? false;
    public bool PadB => _pad?.ButtonB ?? false;
    public bool PadX => _pad?.ButtonX ?? false;
    public bool PadY => _pad?.ButtonY ?? false;
    public bool PadPause => _pad?.ButtonStart ?? false;
    public bool PadBack => _pad?.ButtonBack ?? false;
    public bool PadUp => _pad is { } p && (p.DPadUp || p.LeftStickY < -0.5f);
    public bool PadDown => _pad is { } p && (p.DPadDown || p.LeftStickY > 0.5f);
    public bool PadLeft => _pad is { } p && (p.DPadLeft || p.LeftStickX < -0.5f);
    public bool PadRight => _pad is { } p && (p.DPadRight || p.LeftStickX > 0.5f);

    public void Dispose() => _pad?.Dispose();
}
