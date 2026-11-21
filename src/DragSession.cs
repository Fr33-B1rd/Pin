using static PinApp.Native;

namespace PinApp;

/// <summary>
/// A pin being carried by the mouse. On release, the top window under the needle becomes the "paper"
/// and the next window beneath that point becomes the "wall" (or the desktop if there is none).
/// Polls the mouse instead of relying on capture, so it works no matter which of our windows started it.
/// </summary>
internal sealed class DragSession : IDisposable
{
    private const int VK_LBUTTON = 0x01, VK_ESCAPE = 0x1B, VK_RBUTTON = 0x02;

    public static bool Active { get; private set; }

    /// <summary>Screen area where dropping means "put the pin back" (the pin box).</summary>
    public static Func<Rectangle>? DropBackZone;

    private readonly PinManager _mgr;
    private readonly Color? _customColor;
    private readonly IntPtr _moving;                 // paper whose pin is being pulled out (or Zero for a new pin)
    private readonly Action<bool>? _ended;           // arg: true if a pin was placed/removed, false if cancelled
    private readonly LayeredWindow _ghost = new(clickThrough: true, topmost: true);
    private readonly HighlightOverlay _paperHl = new(), _wallHl = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };

    private IntPtr _paper, _wall;
    private bool _valid;
    private string _ghostKey = "";
    private Point _tipOffset;

    public DragSession(PinManager mgr, IntPtr moving = default, Action<bool>? ended = null)
    {
        _mgr = mgr;
        _moving = moving;
        _customColor = mgr.Get(moving)?.CustomColor;
        _ended = ended;
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        Active = true;
        Update();
        _timer.Start();
    }

    private void Tick()
    {
        if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
        {
            Finish(drop: false);
            return;
        }
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
        {
            Finish(drop: true);
            return;
        }
        Update();
    }

    private void Update()
    {
        var pt = Win.Cursor();
        float scale = ScaleAt(pt);
        bool overBox = DropBackZone?.Invoke().Contains(pt.X, pt.Y) == true;
        var hits = overBox ? new List<IntPtr>() : Win.WindowsAt(pt);
        _paper = hits.Count > 0 ? hits[0] : IntPtr.Zero;
        _wall = hits.Count > 1 ? hits[1] : IntPtr.Zero;

        string caption;
        Color color;
        if (overBox)
        {
            _valid = false;
            caption = _moving != IntPtr.Zero ? UiText.ReturnPin : UiText.ReturnBox;
            color = PinArt.Gray;
        }
        else if (_paper == IntPtr.Zero)
        {
            _valid = false;
            caption = _moving != IntPtr.Zero ? UiText.Release : UiText.Drag;
            color = PinArt.Gray;
        }
        else if (_mgr.WouldCycle(_paper, _wall, _moving))
        {
            _valid = false;
            caption = UiText.CycleHint;
            color = PinArt.Invalid;
        }
        else
        {
            _valid = true;
            caption = UiText.PinTo + Win.Label(_wall);
            color = _customColor ?? PinArt.Red;
        }

        // highlights: wall first, paper on top of it
        if (_paper != IntPtr.Zero && _wall != IntPtr.Zero)
            _wallHl.ShowFor(Win.Bounds(_wall), PinArt.WallAmber, UiText.Wall + Win.Label(_wall), scale);
        else _wallHl.HideOverlay();
        if (_paper != IntPtr.Zero)
            _paperHl.ShowFor(Win.Bounds(_paper), _valid ? PinArt.PaperBlue : PinArt.Invalid, UiText.Paper + Win.Label(_paper), scale);
        else _paperHl.HideOverlay();

        RenderGhost(caption, color, scale);
        _ghost.MoveTo(new Point(pt.X - _tipOffset.X, pt.Y - _tipOffset.Y));
        SetWindowPos(_ghost.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void RenderGhost(string caption, Color color, float scale)
    {
        var key = $"{caption}|{color.ToArgb()}|{scale}";
        if (key == _ghostKey) return;
        _ghostKey = key;

        int box = (int)(44 * scale);
        _tipOffset = new Point((int)(PinArt.Tip.X * box), (int)(PinArt.Tip.Y * box));
        SizeF chip;
        using (var probe = new Bitmap(1, 1))
        using (var g0 = Graphics.FromImage(probe))
            chip = PinArt.DrawChip(g0, caption, PointF.Empty, scale, color, measureOnly: true);

        float chipX = _tipOffset.X + 14 * scale, chipY = _tipOffset.Y + 10 * scale;
        int w = (int)Math.Ceiling(Math.Max(box, chipX + chip.Width)) + 2;
        int h = (int)Math.Ceiling(chipY + chip.Height) + 2;
        using var bmp = LayeredWindow.NewCanvas(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            PinArt.Draw(g, new RectangleF(0, 0, box, box), color, lift: 1f);
            PinArt.DrawChip(g, caption, new PointF(chipX, chipY), scale, color);
        }
        var pt = Win.Cursor();
        _ghost.Present(bmp, new Point(pt.X - _tipOffset.X, pt.Y - _tipOffset.Y));
    }

    private void Finish(bool drop)
    {
        _timer.Stop();
        Active = false;
        var pt = Win.Cursor();
        bool changed = false;

        if (drop)
        {
            if (_valid)
            {
                if (_moving != IntPtr.Zero && _moving != _paper) _mgr.Remove(_moving);
                var err = _mgr.Add(_paper, _wall, pt, _customColor);
                if (err != null) _mgr.Warn(err);
                changed = err == null;
            }
            else if (_moving != IntPtr.Zero && _paper == IntPtr.Zero)
            {
                _mgr.Remove(_moving);       // dropped into empty space: pin pulled out
                changed = true;
            }
        }
        Dispose();
        _ended?.Invoke(changed);
    }

    public void Dispose()
    {
        _timer.Dispose();
        _ghost.Close(); _ghost.Dispose();
        _paperHl.Close(); _paperHl.Dispose();
        _wallHl.Close(); _wallHl.Dispose();
    }
}
