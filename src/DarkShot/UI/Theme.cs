using System.Drawing;
using System.Drawing.Text;

namespace DarkShot.UI;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(22, 23, 27);
    public static readonly Color Surface = Color.FromArgb(30, 32, 38);
    public static readonly Color SurfaceAlt = Color.FromArgb(37, 40, 49);
    public static readonly Color SurfaceHover = Color.FromArgb(45, 49, 60);
    public static readonly Color SurfacePressed = Color.FromArgb(52, 57, 70);
    public static readonly Color Border = Color.FromArgb(52, 57, 69);
    public static readonly Color BorderStrong = Color.FromArgb(72, 79, 95);
    public static readonly Color Text = Color.FromArgb(237, 240, 245);
    public static readonly Color TextMuted = Color.FromArgb(154, 162, 177);
    public static readonly Color TextDim = Color.FromArgb(112, 120, 136);
    public static readonly Color Accent = Color.FromArgb(62, 139, 255);
    public static readonly Color AccentHover = Color.FromArgb(91, 157, 255);
    public static readonly Color AccentSoft = Color.FromArgb(42, 73, 128);
    public static readonly Color Danger = Color.FromArgb(255, 77, 66);
    public static readonly Color Success = Color.FromArgb(52, 199, 89);
    public static readonly Color Warning = Color.FromArgb(255, 176, 32);
    public static readonly Color Canvas = Color.FromArgb(13, 14, 17);
    public static readonly Color CanvasGrid = Color.FromArgb(24, 26, 31);
    public static readonly Color Selection = Color.FromArgb(76, 154, 255);
    public static readonly Color OverlayToolbar = Color.FromArgb(27, 29, 35);
    public static readonly Color CloseHover = Color.FromArgb(232, 17, 35);

    public const string FontFamily = "Segoe UI";

    private static readonly object Gate = new();
    private static readonly Dictionary<(int Px, FontStyle Style), Font> PixelFonts = new();
    private static readonly Dictionary<(int Pt, FontStyle Style), Font> PointFonts = new();

    public static Font PixelFont(float pixels, FontStyle style = FontStyle.Regular)
    {
        int key = Math.Max(6, (int)Math.Round(pixels));
        lock (Gate)
        {
            if (PixelFonts.TryGetValue((key, style), out var f)) return f;
            if (PixelFonts.Count > 64)
            {
                foreach (var old in PixelFonts.Values) old.Dispose();
                PixelFonts.Clear();
            }
            f = new Font(FontFamily, key, style, GraphicsUnit.Pixel);
            PixelFonts[(key, style)] = f;
            return f;
        }
    }

    public static Font UiFont(float points, FontStyle style = FontStyle.Regular)
    {
        int key = Math.Max(6, (int)Math.Round(points * 4));
        lock (Gate)
        {
            if (PointFonts.TryGetValue((key, style), out var f)) return f;
            if (PointFonts.Count > 48)
            {
                foreach (var old in PointFonts.Values) old.Dispose();
                PointFonts.Clear();
            }
            f = new Font(FontFamily, key / 4f, style, GraphicsUnit.Point);
            PointFonts[(key, style)] = f;
            return f;
        }
    }

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c);

    public static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }
}
