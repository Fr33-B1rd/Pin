using System.Drawing.Drawing2D;
using static PinApp.Native;

namespace PinApp;

/// <summary>Floating pin box. Drag from it to take out a pin; Ctrl+drag moves the box; right-click for the menu.</summary>
internal sealed class Dispenser : LayeredWindow
{
    private const int VK_LBUTTON = 0x01;

    private readonly PinManager _mgr;
    private readonly Func<ContextMenuStrip> _menu;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 15 };
    private Point _grab;
    private bool _moving;
    private float _scale = 1;
    private int _box;

    public Dispenser(PinManager mgr, Func<ContextMenuStrip> menu) : base(clickThrough: false, topmost: true)
    {
        _mgr = mgr;
        _menu = menu;
        MouseDown += OnDown;
        _poll.Tick += (_, _) => PollMove();
    }

    public void ShowDefault()
    {
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        _scale = ScaleAt(new POINT(wa.Right - 1, wa.Bottom - 1));
        _box = (int)(60 * _scale);
        Render(new Point(wa.Right - _box - (int)(24 * _scale), wa.Bottom - _box - (int)(24 * _scale)));
    }

    public void Toggle()
    {
        if (Visible) Visible = false;
        else if (ScreenPos == Point.Empty) ShowDefault();
        else Render(ScreenPos);
    }

    private void Render(Point at)
    {
        using var bmp = NewCanvas(_box, _box);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float m = 3 * _scale;
            var rect = new RectangleF(m, m, _box - 2 * m, _box - 2 * m);
            using (var path = PinArt.RoundRect(rect, 14 * _scale))
            {
                using var bg = new LinearGradientBrush(rect, Color.FromArgb(225, 52, 52, 60), Color.FromArgb(225, 24, 24, 28), 90f);
                g.FillPath(bg, path);
                using var edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1.2f * _scale);
                g.DrawPath(edge, path);
            }
            // cork-ish cushion
            float c = _box * 0.62f;
            using (var cushion = new SolidBrush(Color.FromArgb(255, 176, 128, 86)))
                g.FillEllipse(cushion, (_box - c) / 2, (_box - c) / 2 + 3 * _scale, c, c * 0.8f);
            float p = _box * 0.62f;
            PinArt.Draw(g, new RectangleF((_box - p) / 2 + 3 * _scale, (_box - p) / 2 - 3 * _scale, p, p), PinArt.Red, lift: 0.15f);
        }
        Present(bmp, at);
    }

    private void OnDown(object? sender, MouseEventArgs e)
    {
        if (DragSession.Active) return;
        if (e.Button == MouseButtons.Right)
        {
            Menus.Popup(_menu());
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if ((ModifierKeys & Keys.Control) != 0)
        {
            var c = Win.Cursor();
            _grab = new Point(c.X - ScreenPos.X, c.Y - ScreenPos.Y);
            _moving = true;
            _poll.Start();
            return;
        }
        new DragSession(_mgr).Start();
    }

    private void PollMove()
    {
        if (!_moving || (GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
        {
            _moving = false;
            _poll.Stop();
            return;
        }
        var c = Win.Cursor();
        MoveTo(new Point(c.X - _grab.X, c.Y - _grab.Y));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _poll.Dispose();
        base.Dispose(disposing);
    }
}
