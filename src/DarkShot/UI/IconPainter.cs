using System.Drawing;
using System.Drawing.Drawing2D;
using DarkShot.Annotations;

namespace DarkShot.UI;

public enum IconKind
{
    Logo,
    Cursor,
    Move,
    Pen,
    Highlighter,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
    Step,
    Pixelate,
    Blur,
    Redact,
    Crop,
    Undo,
    Redo,
    Save,
    Copy,
    Pin,
    Settings,
    Close,
    Minimize,
    Maximize,
    Folder,
    Image,
    Clipboard,
    Trash,
    Check,
    Info,
    Warning,
    ZoomIn,
    ZoomOut,
    Fit,
    OneToOne,
    More,
    Reset,
    ChevronDown,
    Export,
    History,
    Shield
}

public static class IconPainter
{
    public static void Draw(Graphics g, IconKind kind, RectangleF box, Color color, float stroke = 1.8f, Color? background = null)
    {
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;

            float s = Math.Min(box.Width, box.Height) / 24f;
            if (s <= 0) return;
            g.TranslateTransform(box.X + (box.Width - 24f * s) / 2f, box.Y + (box.Height - 24f * s) / 2f);
            g.ScaleTransform(s, s);

            float sw = Math.Max(0.75f, stroke / s);
            using var pen = new Pen(color, sw)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var brush = new SolidBrush(color);
            DrawCore(g, kind, pen, brush, sw, color, background ?? Theme.Surface);
        }
        finally
        {
            g.Restore(state);
        }
    }

    private static void DrawCore(Graphics g, IconKind kind, Pen pen, Brush brush, float sw, Color color, Color bg)
    {
        switch (kind)
        {
            case IconKind.Logo:
                DrawBracket(g, pen, 4.5f, 4.5f, 1, 1);
                DrawBracket(g, pen, 19.5f, 4.5f, -1, 1);
                DrawBracket(g, pen, 4.5f, 19.5f, 1, -1);
                DrawBracket(g, pen, 19.5f, 19.5f, -1, -1);
                using (var red = new SolidBrush(Theme.Danger))
                    g.FillEllipse(red, 9.4f, 9.4f, 5.2f, 5.2f);
                using (var white = new SolidBrush(Color.White))
                    g.FillEllipse(white, 11.2f, 11.2f, 1.6f, 1.6f);
                break;

            case IconKind.Cursor:
            {
                var pts = new[]
                {
                    new PointF(5.5f, 3f), new PointF(19f, 12.2f), new PointF(12.6f, 13.2f),
                    new PointF(16.4f, 20.2f), new PointF(13.6f, 21.4f), new PointF(10.2f, 14.5f),
                    new PointF(5.5f, 18.2f)
                };
                g.FillPolygon(brush, pts);
                break;
            }

            case IconKind.Move:
                g.DrawLine(pen, 12, 3.5f, 12, 20.5f);
                g.DrawLine(pen, 3.5f, 12, 20.5f, 12);
                FillTriangle(g, brush, new PointF(12, 2.4f), new PointF(9.4f, 6.2f), new PointF(14.6f, 6.2f));
                FillTriangle(g, brush, new PointF(12, 21.6f), new PointF(9.4f, 17.8f), new PointF(14.6f, 17.8f));
                FillTriangle(g, brush, new PointF(2.4f, 12), new PointF(6.2f, 9.4f), new PointF(6.2f, 14.6f));
                FillTriangle(g, brush, new PointF(21.6f, 12), new PointF(17.8f, 9.4f), new PointF(17.8f, 14.6f));
                break;

            case IconKind.Pen:
            {
                using var thick = new Pen(color, sw * 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(thick, 9.2f, 14.8f, 17.8f, 6.2f);
                var tip = new[] { new PointF(4.2f, 20.2f), new PointF(8.8f, 18.1f), new PointF(6.1f, 15.4f) };
                g.FillPolygon(brush, tip);
                using var cap = new SolidBrush(Theme.WithAlpha(color, 120));
                g.FillEllipse(cap, 17.1f, 4.6f, 3.6f, 3.6f);
                break;
            }

            case IconKind.Highlighter:
            {
                using var thick = new Pen(Theme.WithAlpha(color, 150), sw * 3.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(thick, 9.5f, 14.5f, 18f, 6f);
                var tip = new[] { new PointF(4f, 20.5f), new PointF(8.5f, 18.5f), new PointF(6f, 15.7f) };
                g.FillPolygon(brush, tip);
                using var basePen = new Pen(Theme.WithAlpha(color, 160), sw);
                g.DrawLine(basePen, 4, 22, 14, 22);
                break;
            }

            case IconKind.Line:
                g.DrawLine(pen, 5, 19, 19, 5);
                g.FillEllipse(brush, 3.3f, 17.3f, 3.4f, 3.4f);
                g.FillEllipse(brush, 17.3f, 3.3f, 3.4f, 3.4f);
                break;

            case IconKind.Arrow:
                g.DrawLine(pen, 5, 19, 17.6f, 6.4f);
                FillTriangle(g, brush, new PointF(20f, 4f), new PointF(11.8f, 5.9f), new PointF(18.1f, 12.2f));
                break;

            case IconKind.Rectangle:
                using (var path = GeometryUtil.RoundedRect(new RectangleF(4, 6, 16, 12), 1.7f))
                    g.DrawPath(pen, path);
                break;

            case IconKind.Ellipse:
                g.DrawEllipse(pen, 4, 6, 16, 12);
                break;

            case IconKind.Text:
                g.DrawLine(pen, 5, 6, 19, 6);
                g.DrawLine(pen, 12, 6, 12, 19.5f);
                g.DrawLine(pen, 8.8f, 19.5f, 15.2f, 19.5f);
                break;

            case IconKind.Step:
                g.DrawEllipse(pen, 4, 4, 16, 16);
                g.DrawLine(pen, 10.5f, 8.6f, 13, 7.3f);
                g.DrawLine(pen, 13, 7.3f, 13, 16.6f);
                g.DrawLine(pen, 9.5f, 16.6f, 16.5f, 16.6f);
                break;

            case IconKind.Pixelate:
            {
                using var grid = new Pen(Theme.WithAlpha(color, 100), Math.Max(0.7f, sw * 0.7f));
                g.DrawRectangle(grid, 4, 4, 16, 16);
                for (int i = 1; i <= 2; i++)
                {
                    g.DrawLine(grid, 4 + i * 16f / 3f, 4, 4 + i * 16f / 3f, 20);
                    g.DrawLine(grid, 4, 4 + i * 16f / 3f, 20, 4 + i * 16f / 3f);
                }
                g.FillRectangle(brush, 4.6f, 4.6f, 3.7f, 3.7f);
                g.FillRectangle(brush, 12.4f, 8.3f, 3.7f, 3.7f);
                g.FillRectangle(brush, 4.6f, 12.1f, 3.7f, 3.7f);
                g.FillRectangle(brush, 12.4f, 15.9f, 3.7f, 3.7f);
                break;
            }

            case IconKind.Blur:
                using (var b1 = new SolidBrush(Theme.WithAlpha(color, 90)))
                using (var b2 = new SolidBrush(Theme.WithAlpha(color, 130)))
                using (var b3 = new SolidBrush(Theme.WithAlpha(color, 180)))
                {
                    g.FillEllipse(b1, 4, 7, 9, 9);
                    g.FillEllipse(b2, 10.5f, 9, 9, 9);
                    g.FillEllipse(b3, 7, 13, 6, 6);
                }
                break;

            case IconKind.Redact:
            {
                var rect = new RectangleF(4, 7, 16, 10);
                using (var path = GeometryUtil.RoundedRect(rect, 1.5f))
                using (var soft = new SolidBrush(Theme.WithAlpha(color, 55)))
                    g.FillPath(soft, path);
                var clip = g.Clip;
                using (var path = GeometryUtil.RoundedRect(rect, 1.5f))
                {
                    g.SetClip(path, CombineMode.Intersect);
                    using var hatch = new Pen(Theme.WithAlpha(color, 150), Math.Max(0.9f, sw));
                    for (float x = 0; x < 28; x += 3.4f)
                        g.DrawLine(hatch, x - 4, 22, x + 10, 2);
                    g.Clip = clip;
                }
                using (var path = GeometryUtil.RoundedRect(rect, 1.5f))
                    g.DrawPath(pen, path);
                break;
            }

            case IconKind.Crop:
                DrawBracket(g, pen, 3.5f, 3.5f, 1, 1, 6.5f);
                DrawBracket(g, pen, 20.5f, 3.5f, -1, 1, 6.5f);
                DrawBracket(g, pen, 3.5f, 20.5f, 1, -1, 6.5f);
                DrawBracket(g, pen, 20.5f, 20.5f, -1, -1, 6.5f);
                break;

            case IconKind.Undo:
            case IconKind.Redo:
            {
                bool redo = kind == IconKind.Redo;
                var state = g.Save();
                if (redo)
                {
                    g.TranslateTransform(24, 0);
                    g.ScaleTransform(-1, 1);
                }
                using var path = new GraphicsPath();
                path.AddBezier(18.5f, 18f, 7f, 18f, 5f, 11.5f, 7.4f, 6.6f);
                g.DrawPath(pen, path);
                FillTriangle(g, brush, new PointF(6.2f, 5.4f), new PointF(10.2f, 7.9f), new PointF(5.2f, 10.1f));
                g.Restore(state);
                break;
            }

            case IconKind.Reset:
            {
                using var path = new GraphicsPath();
                path.AddArc(5, 5, 14, 14, 30, 280);
                g.DrawPath(pen, path);
                FillTriangle(g, brush, new PointF(18.8f, 5.4f), new PointF(14.8f, 5.9f), new PointF(18.4f, 9.7f));
                break;
            }

            case IconKind.Save:
                using (var path = GeometryUtil.RoundedRect(new RectangleF(4, 4, 16, 16), 2f))
                    g.DrawPath(pen, path);
                g.DrawLine(pen, 8, 4, 8, 9.2f);
                g.DrawLine(pen, 16, 4, 16, 9.2f);
                g.DrawLine(pen, 8, 9.2f, 16, 9.2f);
                using (var path = GeometryUtil.RoundedRect(new RectangleF(8, 13.5f, 8, 6.5f), 1f))
                    g.DrawPath(pen, path);
                break;

            case IconKind.Copy:
            {
                using var bgBrush = new SolidBrush(bg);
                using (var path = GeometryUtil.RoundedRect(new RectangleF(8, 4, 12, 12), 1.8f))
                    g.DrawPath(pen, path);
                g.FillRectangle(bgBrush, 3.6f, 9.2f, 12.4f, 11.4f);
                using (var path = GeometryUtil.RoundedRect(new RectangleF(4, 9, 12, 11), 1.8f))
                    g.DrawPath(pen, path);
                break;
            }

            case IconKind.Pin:
            {
                var head = new[]
                {
                    new PointF(8, 4), new PointF(16, 4), new PointF(15, 9), new PointF(17.5f, 12.5f),
                    new PointF(6.5f, 12.5f), new PointF(9, 9)
                };
                using (var soft = new SolidBrush(Theme.WithAlpha(color, 45)))
                    g.FillPolygon(soft, head);
                g.DrawPolygon(pen, head);
                g.DrawLine(pen, 12, 12.5f, 12, 20);
                g.DrawLine(pen, 10.2f, 20, 13.8f, 20);
                break;
            }

            case IconKind.Settings:
            {
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    float x1 = 12 + (float)Math.Cos(a) * 6.2f;
                    float y1 = 12 + (float)Math.Sin(a) * 6.2f;
                    float x2 = 12 + (float)Math.Cos(a) * 9f;
                    float y2 = 12 + (float)Math.Sin(a) * 9f;
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
                g.DrawEllipse(pen, 7.8f, 7.8f, 8.4f, 8.4f);
                g.DrawEllipse(pen, 4.4f, 4.4f, 15.2f, 15.2f);
                break;
            }

            case IconKind.Close:
                g.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f);
                g.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f);
                break;

            case IconKind.Minimize:
                g.DrawLine(pen, 5, 12, 19, 12);
                break;

            case IconKind.Maximize:
                g.DrawRectangle(pen, 5.5f, 5.5f, 13, 13);
                break;

            case IconKind.Folder:
            {
                using var path = new GraphicsPath();
                path.AddLines(new[]
                {
                    new PointF(3, 19), new PointF(3, 6), new PointF(9.6f, 6), new PointF(11.6f, 8.4f),
                    new PointF(21, 8.4f), new PointF(21, 19)
                });
                path.CloseFigure();
                g.DrawPath(pen, path);
                g.DrawLine(pen, 3, 11, 21, 11);
                break;
            }

            case IconKind.Image:
            {
                using (var path = GeometryUtil.RoundedRect(new RectangleF(3, 5, 18, 14), 2f))
                    g.DrawPath(pen, path);
                g.FillEllipse(brush, 7, 8.5f, 3f, 3f);
                g.DrawLines(pen, new[]
                {
                    new PointF(5.2f, 17.2f), new PointF(10.2f, 12.4f), new PointF(13.2f, 15.3f),
                    new PointF(16.4f, 11.5f), new PointF(20, 16.4f)
                });
                break;
            }

            case IconKind.Clipboard:
                using (var path = GeometryUtil.RoundedRect(new RectangleF(6, 5, 12, 16), 2f))
                    g.DrawPath(pen, path);
                using (var path = GeometryUtil.RoundedRect(new RectangleF(9, 3, 6, 4.2f), 1.2f))
                    g.DrawPath(pen, path);
                g.DrawLine(pen, 9, 11, 15, 11);
                g.DrawLine(pen, 9, 14.5f, 15, 14.5f);
                g.DrawLine(pen, 9, 18, 13, 18);
                break;

            case IconKind.Trash:
                g.DrawLine(pen, 4, 7, 20, 7);
                g.DrawLine(pen, 9, 4.2f, 15, 4.2f);
                g.DrawLine(pen, 9, 4.2f, 9, 7);
                g.DrawLine(pen, 15, 4.2f, 15, 7);
                g.DrawLines(pen, new[]
                {
                    new PointF(6, 7), new PointF(7.4f, 21), new PointF(16.6f, 21), new PointF(18, 7)
                });
                g.DrawLine(pen, 10, 10.5f, 10.5f, 18);
                g.DrawLine(pen, 14, 10.5f, 13.5f, 18);
                break;

            case IconKind.Check:
                g.DrawLines(pen, new[] { new PointF(4.5f, 12.2f), new PointF(9.6f, 17.4f), new PointF(19.5f, 6.6f) });
                break;

            case IconKind.Info:
                g.DrawEllipse(pen, 4, 4, 16, 16);
                g.FillEllipse(brush, 11f, 7.2f, 2f, 2f);
                g.DrawLine(pen, 12, 11, 12, 17);
                break;

            case IconKind.Warning:
                g.DrawLines(pen, new[]
                {
                    new PointF(12, 3), new PointF(22, 21), new PointF(2, 21), new PointF(12, 3)
                });
                g.DrawLine(pen, 12, 9, 12, 15.2f);
                g.FillEllipse(brush, 11f, 17.2f, 2f, 2f);
                break;

            case IconKind.ZoomIn:
            case IconKind.ZoomOut:
                g.DrawEllipse(pen, 3.5f, 3.5f, 12.5f, 12.5f);
                g.DrawLine(pen, 14.8f, 14.8f, 20.5f, 20.5f);
                g.DrawLine(pen, 7, 9.75f, 12.5f, 9.75f);
                if (kind == IconKind.ZoomIn) g.DrawLine(pen, 9.75f, 7, 9.75f, 12.5f);
                break;

            case IconKind.Fit:
                DrawBracket(g, pen, 3.5f, 3.5f, 1, 1, 5f);
                DrawBracket(g, pen, 20.5f, 3.5f, -1, 1, 5f);
                DrawBracket(g, pen, 3.5f, 20.5f, 1, -1, 5f);
                DrawBracket(g, pen, 20.5f, 20.5f, -1, -1, 5f);
                g.DrawRectangle(pen, 8, 9.5f, 8, 5);
                break;

            case IconKind.OneToOne:
            {
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var font = Theme.PixelFont(11, FontStyle.Bold);
                g.DrawString("1:1", font, brush, new RectangleF(1, 1, 22, 22), fmt);
                break;
            }

            case IconKind.More:
                g.FillEllipse(brush, 4.7f, 10.7f, 2.6f, 2.6f);
                g.FillEllipse(brush, 10.7f, 10.7f, 2.6f, 2.6f);
                g.FillEllipse(brush, 16.7f, 10.7f, 2.6f, 2.6f);
                break;

            case IconKind.ChevronDown:
                g.DrawLines(pen, new[] { new PointF(7, 9.5f), new PointF(12, 14.5f), new PointF(17, 9.5f) });
                break;

            case IconKind.Export:
                g.DrawLines(pen, new[]
                {
                    new PointF(4, 12), new PointF(4, 20), new PointF(20, 20), new PointF(20, 12)
                });
                g.DrawLine(pen, 12, 16, 12, 3.5f);
                FillTriangle(g, brush, new PointF(12, 2.2f), new PointF(8.5f, 6.3f), new PointF(15.5f, 6.3f));
                break;

            case IconKind.History:
                g.DrawEllipse(pen, 3.8f, 3.8f, 16.4f, 16.4f);
                g.DrawLine(pen, 12, 12, 12, 6.3f);
                g.DrawLine(pen, 12, 12, 16.4f, 14.3f);
                break;

            case IconKind.Shield:
                g.DrawLines(pen, new[]
                {
                    new PointF(12, 2.8f), new PointF(20.5f, 6.2f), new PointF(19.5f, 13.8f),
                    new PointF(12, 21.4f), new PointF(4.5f, 13.8f), new PointF(3.5f, 6.2f),
                    new PointF(12, 2.8f)
                });
                g.DrawLines(pen, new[] { new PointF(8.2f, 11.7f), new PointF(11.2f, 14.7f), new PointF(16.2f, 9.2f) });
                break;
        }
    }

    private static void DrawBracket(Graphics g, Pen pen, float x, float y, int sx, int sy, float len = 5.5f)
    {
        g.DrawLine(pen, x, y, x + sx * len, y);
        g.DrawLine(pen, x, y, x, y + sy * len);
    }

    private static void FillTriangle(Graphics g, Brush brush, PointF a, PointF b, PointF c)
        => g.FillPolygon(brush, new[] { a, b, c });
}
