using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PinApp;

/// <summary>Vector drawing of a tilted push-pin. Needle tip is the hotspot.</summary>
internal static class PinArt
{
    public static readonly Color Red = Color.FromArgb(229, 72, 77);
    public static readonly Color Gray = Color.FromArgb(140, 140, 146);
    public static readonly Color PaperBlue = Color.FromArgb(56, 132, 255);
    public static readonly Color WallAmber = Color.FromArgb(245, 158, 11);
    public static readonly Color Invalid = Color.FromArgb(220, 38, 38);

    /// <summary>Hotspot (needle tip) as a fraction of the pin box.</summary>
    public static readonly PointF Tip = new(0.14f, 0.86f);

    /// <param name="lift">0 = pressed into the surface (short shadow), 1 = held in the air (long, soft shadow).</param>
    public static void Draw(Graphics g, RectangleF box, Color color, float lift)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = box.Width;
        PointF P(float x, float y) => new(box.X + x * s, box.Y + y * s);

        var tip = P(Tip.X, Tip.Y);
        var baseC = P(0.52f, 0.48f);   // flared base of the head
        var capC = P(0.70f, 0.30f);    // round cap

        // shadow: the head's silhouette projected down-right; further away when lifted
        float sh = s * (0.04f + 0.14f * lift);
        using (var shadow = new SolidBrush(Color.FromArgb((int)(70 - 30 * lift), 0, 0, 0)))
        {
            float r = s * 0.20f;
            g.FillEllipse(shadow, baseC.X - r + sh, baseC.Y - r + sh * 1.3f, r * 2, r * 2);
            g.FillEllipse(shadow, capC.X - r * 0.8f + sh, capC.Y - r * 0.8f + sh * 1.3f, r * 1.6f, r * 1.6f);
        }

        // needle (only the visible part when pressed in)
        var needleStart = lift < 0.05f ? Lerp(tip, baseC, 0.55f) : tip;
        using (var steel = new LinearGradientBrush(tip, baseC, Color.FromArgb(150, 150, 160), Color.FromArgb(235, 235, 240)))
        using (var needle = new Pen(steel, s * 0.05f))
        {
            needle.StartCap = LineCap.Round;
            g.DrawLine(needle, needleStart, baseC);
        }

        var dark = Shade(color, 0.65f);
        var light = Shade(color, 1.25f);

        // body connecting base and cap
        using (var body = new Pen(color, s * 0.17f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(body, baseC, capC);

        // base disc
        float rb = s * 0.19f;
        using (var b = new LinearGradientBrush(new PointF(baseC.X - rb, baseC.Y - rb), new PointF(baseC.X + rb, baseC.Y + rb), light, dark))
            g.FillEllipse(b, baseC.X - rb, baseC.Y - rb, rb * 2, rb * 2);

        // cap
        float rc = s * 0.15f;
        using (var b = new LinearGradientBrush(new PointF(capC.X - rc, capC.Y - rc), new PointF(capC.X + rc, capC.Y + rc), light, dark))
            g.FillEllipse(b, capC.X - rc, capC.Y - rc, rc * 2, rc * 2);

        // gloss
        using (var gloss = new SolidBrush(Color.FromArgb(170, 255, 255, 255)))
            g.FillEllipse(gloss, capC.X - rc * 0.55f, capC.Y - rc * 0.7f, rc * 0.6f, rc * 0.45f);
    }

    /// <summary>Rounded "chip" with text, used for captions on the ghost pin and overlays.</summary>
    public static SizeF DrawChip(Graphics g, string text, PointF at, float scale, Color accent, bool measureOnly = false)
    {
        using var font = new Font("Microsoft YaHei UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var ts = g.MeasureString(text, font);
        float padX = 8 * scale, padY = 4 * scale;
        var rect = new RectangleF(at.X, at.Y, ts.Width + padX * 2, ts.Height + padY * 2);
        if (measureOnly) return rect.Size;
        using (var path = RoundRect(rect, 6 * scale))
        using (var bg = new SolidBrush(Color.FromArgb(235, 28, 28, 32)))
        using (var edge = new Pen(accent, 1.5f * scale))
        {
            g.FillPath(bg, path);
            g.DrawPath(edge, path);
        }
        using (var fg = new SolidBrush(Color.White))
            g.DrawString(text, font, fg, rect.X + padX, rect.Y + padY);
        return rect.Size;
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Icon MakeIcon(int size)
    {
        using var bmp = LayeredWindow.NewCanvas(size, size);
        using (var g = Graphics.FromImage(bmp)) Draw(g, new RectangleF(0, 0, size, size), Red, 0.3f);
        var h = bmp.GetHicon();
        using var tmp = Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone();
        Native.DestroyIcon(h);
        return icon;
    }

    private static PointF Lerp(PointF a, PointF b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static Color Shade(Color c, float f) => Color.FromArgb(c.A,
        Math.Clamp((int)(c.R * f), 0, 255), Math.Clamp((int)(c.G * f), 0, 255), Math.Clamp((int)(c.B * f), 0, 255));
}
