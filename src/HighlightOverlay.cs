using System.Drawing.Drawing2D;
using static PinApp.Native;

namespace PinApp;

/// <summary>Click-through outline + caption drawn around a target window while dragging a pin.</summary>
internal sealed class HighlightOverlay : LayeredWindow
{
    private readonly record struct Appearance(Size Size, Color Accent, string Caption, float Scale);
    private readonly List<(Appearance Key, HighlightBitmap Bitmap)> _cache = new(2);
    private const long CacheBytes = 48L * 1024 * 1024;
    private Appearance? _displayed;

    public HighlightOverlay() : base(clickThrough: true, topmost: true) { }

    public void ShowFor(RECT r, Color accent, string caption, float scale)
    {
        var key = new Appearance(new Size(r.Width, r.Height), accent, caption, scale);
        int pad = (int)Math.Ceiling(2 * scale);
        var position = new Point(r.Left - pad, r.Top - pad);
        if (_displayed == key)
        {
            MoveTo(position);
            if (!Visible) Visible = true;
            return;
        }

        int index = _cache.FindIndex(entry => entry.Key == key);
        if (index >= 0)
        {
            var entry = _cache[index];
            _cache.RemoveAt(index);
            _cache.Add(entry);
            Present(entry.Bitmap.Handle, entry.Bitmap.Size, position);
        }
        else
        {
            var size = new Size(r.Width + 2 * pad, r.Height + 2 * pad);
            long bytes = (long)size.Width * size.Height * 4;
            while (_cache.Count > 0 && (_cache.Count == 2 || _cache.Sum(entry => entry.Bitmap.Bytes) + bytes > CacheBytes))
            {
                _cache[0].Bitmap.Dispose();
                _cache.RemoveAt(0);
            }
            var bitmap = new HighlightBitmap(size);
            bool cached = false;
            try
            {
                using (var canvas = bitmap.OpenCanvas())
                using (var g = Graphics.FromImage(canvas))
                    Draw(g, key, pad);
                Present(bitmap.Handle, size, position);
                if (bytes <= CacheBytes)
                {
                    _cache.Add((key, bitmap));
                    cached = true;
                }
            }
            finally
            {
                if (!cached) bitmap.Dispose();
            }
        }
        _displayed = key;
    }

    private static void Draw(Graphics g, Appearance key, int pad)
    {
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float t = 3f * key.Scale;
        var rect = new RectangleF(pad + t / 2, pad + t / 2, key.Size.Width - t, key.Size.Height - t);
        using (var fill = new SolidBrush(Color.FromArgb(26, key.Accent)))
            g.FillRectangle(fill, rect);
        using (var path = PinArt.RoundRect(rect, 8 * key.Scale))
        using (var pen = new Pen(key.Accent, t))
            g.DrawPath(pen, path);
        PinArt.DrawChip(g, key.Caption, new PointF(pad + 12 * key.Scale, pad + 12 * key.Scale), key.Scale, key.Accent);
    }

    public void HideOverlay()
    {
        if (Visible) Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var entry in _cache) entry.Bitmap.Dispose();
            _cache.Clear();
        }
        base.Dispose(disposing);
    }
}
