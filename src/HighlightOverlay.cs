using System.Drawing.Drawing2D;
using static PinApp.Native;

namespace PinApp;

/// <summary>Click-through outline + caption drawn around a target window while dragging a pin.</summary>
internal sealed class HighlightOverlay : LayeredWindow
{
    private string _key = "";

    public HighlightOverlay() : base(clickThrough: true, topmost: true) { }

    public void ShowFor(RECT r, Color accent, string caption, float scale)
    {
        var key = $"{r.Left},{r.Top},{r.Right},{r.Bottom}|{accent.ToArgb()}|{caption}|{scale}";
        if (key == _key && Visible) return;
        _key = key;

        int pad = (int)Math.Ceiling(2 * scale);
        using var bmp = NewCanvas(r.Width + pad * 2, r.Height + pad * 2);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float t = 3f * scale;
            var rect = new RectangleF(pad + t / 2, pad + t / 2, r.Width - t, r.Height - t);
            using (var fill = new SolidBrush(Color.FromArgb(26, accent)))
                g.FillRectangle(fill, rect);
            using (var path = PinArt.RoundRect(rect, 8 * scale))
            using (var pen = new Pen(accent, t))
                g.DrawPath(pen, path);
            PinArt.DrawChip(g, caption, new PointF(pad + 12 * scale, pad + 12 * scale), scale, accent);
        }
        Present(bmp, new Point(r.Left - pad, r.Top - pad));
    }

    public void HideOverlay()
    {
        _key = "";
        if (Visible) Visible = false;
    }
}
