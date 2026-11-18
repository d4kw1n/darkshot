using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace DarkShot.Annotations;

public static class AnnotationRenderer
{
    public static void DrawAll(Graphics g, Image baseImage, IEnumerable<Annotation> annotations)
    {
        DrawBase(g, baseImage);
        foreach (var annotation in annotations)
            DrawOne(g, baseImage, annotation);
    }

    public static void DrawBase(Graphics g, Image baseImage)
    {
        g.DrawImage(baseImage, new Rectangle(0, 0, baseImage.Width, baseImage.Height),
            0, 0, baseImage.Width, baseImage.Height, GraphicsUnit.Pixel);
    }

    public static void DrawOne(Graphics g, Image baseImage, Annotation a)
    {
        switch (a.Kind)
        {
            case AnnotationKind.Rectangle:
                DrawRectangle(g, a);
                break;
            case AnnotationKind.Ellipse:
                DrawEllipse(g, a);
                break;
            case AnnotationKind.Line:
                DrawLine(g, a, arrow: false);
                break;
            case AnnotationKind.Arrow:
                DrawLine(g, a, arrow: true);
                break;
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                DrawFreehand(g, a);
                break;
            case AnnotationKind.Text:
                DrawText(g, a);
                break;
            case AnnotationKind.Step:
                DrawStep(g, a);
                break;
            case AnnotationKind.Pixelate:
                DrawPixelate(g, baseImage, a);
                break;
            case AnnotationKind.Blur:
                DrawBlur(g, baseImage, a);
                break;
            case AnnotationKind.Redact:
                DrawRedact(g, a);
                break;
        }
    }

    public static Bitmap Compose(Bitmap baseImage, IReadOnlyList<Annotation> annotations, Rectangle crop)
    {
        var imageRect = new Rectangle(0, 0, baseImage.Width, baseImage.Height);
        var safeCrop = Rectangle.Intersect(crop, imageRect);
        if (safeCrop.Width <= 0 || safeCrop.Height <= 0) safeCrop = imageRect;

        var output = new Bitmap(safeCrop.Width, safeCrop.Height, PixelFormat.Format24bppRgb);
        output.SetResolution(96f, 96f);
        using var g = Graphics.FromImage(output);
        g.PageUnit = GraphicsUnit.Pixel;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.TranslateTransform(-safeCrop.X, -safeCrop.Y);
        DrawAll(g, baseImage, annotations);
        return output;
    }

    private static void DrawRectangle(Graphics g, Annotation a)
    {
        float w = Math.Max(1f, a.StrokeWidth);
        var r = Deflate(a.GetBounds(), w / 2f);
        if (r.Width <= 0 || r.Height <= 0) return;

        if (a.Filled)
        {
            using var fill = new SolidBrush(WithAlpha(a.Color, 60));
            g.FillRectangle(fill, r);
        }

        using var pen = CreatePen(a.Color, w);
        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }

    private static void DrawEllipse(Graphics g, Annotation a)
    {
        float w = Math.Max(1f, a.StrokeWidth);
        var r = Deflate(a.GetBounds(), w / 2f);
        if (r.Width <= 0 || r.Height <= 0) return;

        if (a.Filled)
        {
            using var fill = new SolidBrush(WithAlpha(a.Color, 60));
            g.FillEllipse(fill, r);
        }

        using var pen = CreatePen(a.Color, w);
        g.DrawEllipse(pen, r);
    }

    private static void DrawLine(Graphics g, Annotation a, bool arrow)
    {
        float w = Math.Max(1f, a.StrokeWidth);
        float length = GeometryUtil.Distance(a.Start, a.End);
        if (length < 0.5f)
        {
            using var brush = new SolidBrush(a.Color);
            g.FillEllipse(brush, a.Start.X - w / 2f, a.Start.Y - w / 2f, w, w);
            return;
        }

        using var pen = CreatePen(a.Color, w);
        using var brush2 = new SolidBrush(a.Color);

        if (!arrow)
        {
            g.DrawLine(pen, a.Start, a.End);
            return;
        }

        float head = Math.Min(Math.Max(12f, w * 4.5f), length * 0.85f);
        float headWidth = head * 0.62f;
        float angle = MathF.Atan2(a.End.Y - a.Start.Y, a.End.X - a.Start.X);
        var basePoint = new PointF(a.End.X - head * MathF.Cos(angle), a.End.Y - head * MathF.Sin(angle));
        var perp = new PointF(-MathF.Sin(angle), MathF.Cos(angle));

        g.DrawLine(pen, a.Start, basePoint);

        var p1 = new PointF(basePoint.X + perp.X * headWidth / 2f, basePoint.Y + perp.Y * headWidth / 2f);
        var p2 = new PointF(basePoint.X - perp.X * headWidth / 2f, basePoint.Y - perp.Y * headWidth / 2f);
        g.FillPolygon(brush2, new[] { a.End, p1, p2 });
    }

    private static void DrawFreehand(Graphics g, Annotation a)
    {
        float w = Math.Max(1f, a.StrokeWidth);
        if (a.Points.Count == 0) return;

        if (a.Points.Count == 1)
        {
            using var brush = new SolidBrush(a.Color);
            g.FillEllipse(brush, a.Points[0].X - w / 2f, a.Points[0].Y - w / 2f, w, w);
            return;
        }

        using var path = new GraphicsPath();
        path.AddLines(a.Points.ToArray());
        using var pen = CreatePen(a.Color, w);
        g.DrawPath(pen, path);
    }

    private static void DrawText(Graphics g, Annotation a)
    {
        if (string.IsNullOrWhiteSpace(a.Text)) return;
        var font = TextMetrics.GetFont(a.FontSize, a.FontBold);
        var bounds = a.GetBounds();
        var textPos = new PointF(bounds.X + TextMetrics.Padding, bounds.Y + TextMetrics.Padding);

        using var format = TextMetrics.CreateDrawFormat();
        using var textBrush = new SolidBrush(a.Color);

        if (a.TextBackground)
        {
            using var bg = new SolidBrush(Color.FromArgb(168, 10, 12, 16));
            using var bgPath = GeometryUtil.RoundedRect(bounds, Math.Min(6f, bounds.Height / 4f));
            g.FillPath(bg, bgPath);

            using var border = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
            g.DrawPath(border, bgPath);
        }

        if (!a.TextBackground)
        {
            using var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            g.DrawString(a.Text, font, shadow, new PointF(textPos.X + 1.4f, textPos.Y + 1.4f), format);
        }

        g.DrawString(a.Text, font, textBrush, textPos, format);
    }

    private static void DrawStep(Graphics g, Annotation a)
    {
        var bounds = a.GetBounds();
        float d = Math.Min(bounds.Width, bounds.Height);
        if (d <= 0) return;
        var center = GeometryUtil.Center(bounds);
        var circle = new RectangleF(center.X - d / 2f, center.Y - d / 2f, d, d);

        using (var shadow = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillEllipse(shadow, circle.X, circle.Y + 2f, circle.Width, circle.Height);

        using (var fill = new SolidBrush(a.Color))
            g.FillEllipse(fill, circle);

        using (var outline = new Pen(Color.FromArgb(230, 255, 255, 255), Math.Max(1f, d * 0.035f)))
            g.DrawEllipse(outline, circle);

        string number = a.StepNumber.ToString();
        float factor = number.Length switch { 1 => 0.48f, 2 => 0.40f, _ => 0.33f };
        float fontSize = Math.Max(6f, d * factor);
        var font = TextMetrics.GetFont(fontSize, bold: true);
        using var format = TextMetrics.CreateCenteredFormat();
        using var brush = new SolidBrush(GeometryUtil.ContrastingTextColor(a.Color));

        // Slight vertical correction for Segoe UI's baseline.
        var textRect = new RectangleF(circle.X, circle.Y - d * 0.015f, circle.Width, circle.Height);
        g.DrawString(number, font, brush, textRect, format);
    }

    private static void DrawPixelate(Graphics g, Image baseImage, Annotation a)
    {
        var src = Rectangle.Round(RectangleF.Intersect(a.GetBounds(), new RectangleF(0, 0, baseImage.Width, baseImage.Height)));
        if (src.Width < 2 || src.Height < 2) return;

        int block = Math.Clamp(a.Strength, 2, 120);
        int smallW = Math.Max(1, src.Width / block);
        int smallH = Math.Max(1, src.Height / block);

        using var small = new Bitmap(smallW, smallH, PixelFormat.Format24bppRgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sg.CompositingQuality = CompositingQuality.HighQuality;
            sg.DrawImage(baseImage, new Rectangle(0, 0, smallW, smallH), src, GraphicsUnit.Pixel);
        }

        var oldInterp = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(small, src);
        g.InterpolationMode = oldInterp;
        g.PixelOffsetMode = oldOffset;
    }

    private static void DrawBlur(Graphics g, Image baseImage, Annotation a)
    {
        var src = Rectangle.Round(RectangleF.Intersect(a.GetBounds(), new RectangleF(0, 0, baseImage.Width, baseImage.Height)));
        if (src.Width < 2 || src.Height < 2) return;

        int radius = Math.Clamp(a.Strength, 2, 150);
        int divisor = Math.Max(2, radius * 2);
        int smallW = Math.Max(1, src.Width / divisor);
        int smallH = Math.Max(1, src.Height / divisor);

        using var small = new Bitmap(smallW, smallH, PixelFormat.Format24bppRgb);
        using (var sg = Graphics.FromImage(small))
        {
            sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
            sg.CompositingQuality = CompositingQuality.HighQuality;
            sg.DrawImage(baseImage, new Rectangle(0, 0, smallW, smallH), src, GraphicsUnit.Pixel);
        }

        var oldInterp = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(small, src);
        g.InterpolationMode = oldInterp;
        g.PixelOffsetMode = oldOffset;
    }

    private static void DrawRedact(Graphics g, Annotation a)
    {
        var r = a.GetBounds();
        if (r.Width < 1 || r.Height < 1) return;
        using var brush = new SolidBrush(Color.FromArgb(255, a.Color));
        g.FillRectangle(brush, r);
    }

    private static Pen CreatePen(Color color, float width)
    {
        return new Pen(color, Math.Max(1f, width))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
            DashStyle = DashStyle.Solid
        };
    }

    private static Color WithAlpha(Color c, int alpha)
        => Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    private static RectangleF Deflate(RectangleF r, float amount)
    {
        float w = Math.Max(0f, r.Width - amount * 2f);
        float h = Math.Max(0f, r.Height - amount * 2f);
        return new RectangleF(r.X + amount, r.Y + amount, w, h);
    }
}
