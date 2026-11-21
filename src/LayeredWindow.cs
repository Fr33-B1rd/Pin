using System.Drawing.Imaging;
using static PinApp.Native;

namespace PinApp;

/// <summary>
/// Borderless per-pixel-alpha window that never takes focus. Content and position are pushed with
/// <see cref="Present"/> (UpdateLayeredWindow), in physical screen pixels. Fully transparent pixels let
/// clicks through; <see cref="ClickThrough"/> makes the whole window ignore the mouse.
/// </summary>
internal class LayeredWindow : Form
{
    private readonly bool _clickThrough;
    private readonly bool _topmost;

    public LayeredWindow(bool clickThrough, bool topmost)
    {
        _clickThrough = clickThrough;
        _topmost = topmost;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Pin";
    }

    public bool ClickThrough => _clickThrough;
    public Point ScreenPos { get; private set; }
    public Size PixelSize { get; private set; }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            if (_clickThrough) cp.ExStyle |= (int)WS_EX_TRANSPARENT;
            if (_topmost) cp.ExStyle |= (int)WS_EX_TOPMOST;
            return cp;
        }
    }

    // Never become active, even when clicked.
    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
        const int WM_DPICHANGED = 0x02E0;
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = MA_NOACTIVATE; return; }
        if (m.Msg == WM_DPICHANGED) { m.Result = IntPtr.Zero; return; }   // size/position are driven by UpdateLayeredWindow
        base.WndProc(ref m);
    }

    /// <summary>Show <paramref name="bmp"/> (32bpp ARGB) with its top-left at <paramref name="screenPos"/>.</summary>
    public void Present(Bitmap bmp, Point screenPos)
    {
        var hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        try { Present(hBmp, bmp.Size, screenPos); }
        finally { DeleteObject(hBmp); }
    }

    protected void Present(IntPtr hBmp, Size dimensions, Point screenPos)
    {
        if (!IsHandleCreated) CreateHandle();
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var old = SelectObject(memDc, hBmp);
        try
        {
            var size = new SIZE(dimensions.Width, dimensions.Height);
            var src = new POINT(0, 0);
            var dst = new POINT(screenPos.X, screenPos.Y);
            var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            ScreenPos = screenPos;
            PixelSize = dimensions;
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
        if (!Visible) Visible = true;
    }

    /// <summary>Move without re-rendering.</summary>
    public void MoveTo(Point screenPos)
    {
        if (screenPos == ScreenPos || !IsHandleCreated) return;
        SetWindowPos(Handle, IntPtr.Zero, screenPos.X, screenPos.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        ScreenPos = screenPos;
    }

    public static Bitmap NewCanvas(int w, int h) => new(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
}
