using static PinApp.Native;

namespace PinApp;

/// <summary>
/// The pin head stuck on a pinned window. Click = show what it's pinned to; drag = pull the pin out
/// (drop on another window to re-pin, drop on empty space to remove); right-click = options.
/// </summary>
internal sealed class PinDecal : LayeredWindow
{
    private const int VK_LBUTTON = 0x01;

    private readonly PinManager _mgr;
    private readonly Pin _pin;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 15 };
    private Color _renderedColor;
    private float _scale = -1;
    private bool _topmost;
    private Point _tip;
    private POINT _down;
    private bool _pressed;

    public bool Dragging { get; private set; }

    public PinDecal(PinManager mgr, Pin pin) : base(clickThrough: false, topmost: false)
    {
        _mgr = mgr;
        _pin = pin;
        _poll.Tick += (_, _) => Poll();
        MouseDown += OnDown;
    }

    /// <summary>Place the needle tip at <paramref name="needle"/>, directly above the paper in z-order.</summary>
    public void ShowAt(Point needle, bool topmost)
    {
        float scale = ScaleAt(new POINT(needle.X, needle.Y));
        if (scale != _scale || !Visible || _renderedColor != _pin.HeadColor)
        {
            _scale = scale;
            int box = (int)(30 * _scale);
            _tip = new Point((int)(PinArt.Tip.X * box), (int)(PinArt.Tip.Y * box));
            Render(new Point(needle.X - _tip.X, needle.Y - _tip.Y));
        }
        else MoveTo(new Point(needle.X - _tip.X, needle.Y - _tip.Y));

        if (topmost != _topmost)
        {
            _topmost = topmost;
            SetWindowPos(Handle, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        // Keep the head just above its paper (not above unrelated windows that cover the paper).
        if (GetWindow(_pin.Paper, GW_HWNDPREV) != Handle)
        {
            var above = GetWindow(_pin.Paper, GW_HWNDPREV);
            SetWindowPos(Handle, above == IntPtr.Zero ? HWND_TOP : above, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
    }

    private void Render(Point at, bool empty = false)
    {
        int box = (int)(30 * _scale);
        using var bmp = NewCanvas(box, box);
        if (!empty)
        {
            _renderedColor = _pin.HeadColor;
            using (var g = Graphics.FromImage(bmp))
                PinArt.Draw(g, new RectangleF(0, 0, box, box), _renderedColor, lift: 0f);
        }
        Present(bmp, at);
    }

    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (DragSession.Active) return;
        if (e.Button == MouseButtons.Right) { ShowMenu(); return; }
        if (e.Button != MouseButtons.Left) return;
        _down = Win.Cursor();
        _pressed = true;
        _poll.Start();
    }

    private void Poll()
    {
        var p = Win.Cursor();
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
        {
            _poll.Stop();
            if (_pressed) Flash();
            _pressed = false;
            return;
        }
        float dist = MathF.Sqrt((p.X - _down.X) * (p.X - _down.X) + (p.Y - _down.Y) * (p.Y - _down.Y));
        if (dist < 6 * _scale) return;

        // Pull the pin out.
        _poll.Stop();
        _pressed = false;
        Dragging = true;
        Render(ScreenPos, empty: true);   // stay visible (keeps mouse capture) but draw nothing; the ghost takes over
        new DragSession(_mgr, _pin.Paper, changed =>
        {
            if (IsDisposed) return;       // pin was removed / replaced
            Dragging = false;
            Render(ScreenPos);
            _mgr.SyncDecal(_pin);
        }).Start();
    }

    /// <summary>Briefly outline the paper and its wall.</summary>
    private void Flash()
    {
        float s = _scale;
        var paperHl = new HighlightOverlay();
        paperHl.ShowFor(Win.Bounds(_pin.Paper), PinArt.PaperBlue, UiText.Paper + Win.Label(_pin.Paper), s);
        HighlightOverlay? wallHl = null;
        if (!_pin.OnDesktop && IsWindow(_pin.Wall))
        {
            wallHl = new HighlightOverlay();
            wallHl.ShowFor(Win.Bounds(_pin.Wall), PinArt.WallAmber, UiText.Wall + Win.Label(_pin.Wall), s);
        }
        else
        {
            paperHl.ShowFor(Win.Bounds(_pin.Paper), PinArt.PaperBlue, UiText.Paper + Win.Label(_pin.Paper) + UiText.OnDesktop, s);
        }
        var t = new System.Windows.Forms.Timer { Interval = 1100 };
        t.Tick += (_, _) =>
        {
            t.Dispose();
            paperHl.Close(); paperHl.Dispose();
            wallHl?.Close(); wallHl?.Dispose();
        };
        t.Start();
    }

    private void ShowMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel($"{Win.Label(_pin.Paper)}  →  {Win.Label(_pin.Wall)}") { ForeColor = Color.Gray });
        menu.Items.Add(new ToolStripSeparator());
        var follow = new ToolStripMenuItem(UiText.Follow) { Checked = _pin.FollowMove, Enabled = !_pin.OnDesktop };
        follow.Click += (_, _) => { _pin.FollowMove = !_pin.FollowMove; _mgr.Touch(); };
        var minimize = new ToolStripMenuItem(UiText.Minimize) { Checked = _pin.SyncMinimize, Enabled = !_pin.OnDesktop };
        minimize.Click += (_, _) => { _pin.SyncMinimize = !_pin.SyncMinimize; _mgr.Touch(); };
        var pull = new ToolStripMenuItem(UiText.Remove);
        pull.Click += (_, _) => _mgr.Remove(_pin.Paper);
        menu.Items.AddRange(new ToolStripItem[] { follow, minimize, new ToolStripSeparator(), pull });
        if (_pin.OnDesktop) menu.Items.Insert(2, _mgr.WidgetMenu(_pin));
        menu.Items.Insert(2, Menus.PinColor(_pin, _mgr));
        Menus.Popup(menu);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _poll.Dispose();
        base.Dispose(disposing);
    }
}

