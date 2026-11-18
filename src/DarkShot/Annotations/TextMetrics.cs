using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DarkShot.Annotations;

public static class TextMetrics
{
    public const string FontFamily = "Segoe UI";
    public const float Padding = 7f;

    private static readonly object Gate = new();
    private static readonly Dictionary<(int Size, bool Bold), Font> FontCache = new();
    private static readonly StringFormat MeasureFormat;

    static TextMetrics()
    {
        MeasureFormat = (StringFormat)StringFormat.GenericTypographic.Clone();
        MeasureFormat.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        MeasureFormat.Trimming = StringTrimming.None;
    }

    public static Font GetFont(float pixelSize, bool bold)
    {
        int size = Math.Max(4, (int)Math.Round(pixelSize));
        var key = (size, bold);
        lock (Gate)
        {
            if (FontCache.TryGetValue(key, out var font)) return font;
            if (FontCache.Count > 96)
            {
                foreach (var f in FontCache.Values) f.Dispose();
                FontCache.Clear();
            }
            var style = bold ? FontStyle.Bold : FontStyle.Regular;
            font = new Font(FontFamily, size, style, GraphicsUnit.Pixel);
            FontCache[key] = font;
            return font;
        }
    }

    public static SizeF MeasureText(string text, float pixelSize, bool bold)
    {
        if (string.IsNullOrEmpty(text)) text = " ";
        var font = GetFont(pixelSize, bold);
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var size = g.MeasureString(text, font, new PointF(0, 0), MeasureFormat);
        return new SizeF(Math.Max(1f, size.Width), Math.Max(1f, size.Height));
    }

    public static SizeF MeasureTextWithPadding(string text, float pixelSize, bool bold, float padding = Padding)
    {
        var m = MeasureText(text, pixelSize, bold);
        return new SizeF(m.Width + padding * 2f, m.Height + padding * 2f);
    }

    public static StringFormat CreateDrawFormat()
    {
        var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
        fmt.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        fmt.Alignment = StringAlignment.Near;
        fmt.LineAlignment = StringAlignment.Near;
        fmt.Trimming = StringTrimming.None;
        return fmt;
    }

    public static StringFormat CreateCenteredFormat()
    {
        var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
        fmt.Alignment = StringAlignment.Center;
        fmt.LineAlignment = StringAlignment.Center;
        fmt.FormatFlags |= StringFormatFlags.NoClip;
        return fmt;
    }
}
