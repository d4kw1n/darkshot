using System.Drawing;

namespace DarkShot.Annotations;

public enum AnnotationKind
{
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Pen,
    Highlighter,
    Text,
    Step,
    Pixelate,
    Blur,
    Redact
}

public sealed class Annotation
{
    public AnnotationKind Kind { get; set; }
    public RectangleF Bounds { get; set; }
    public PointF Start { get; set; }
    public PointF End { get; set; }
    public List<PointF> Points { get; set; } = new();
    public Color Color { get; set; } = Color.FromArgb(255, 255, 59, 48);
    public float StrokeWidth { get; set; } = 3f;
    public bool Filled { get; set; }
    public string Text { get; set; } = string.Empty;
    public float FontSize { get; set; } = 18f;
    public bool FontBold { get; set; } = true;
    public bool TextBackground { get; set; } = true;
    public int StepNumber { get; set; }
    public int Strength { get; set; } = 12;

    public bool IsShape => Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse;
    public bool IsLine => Kind is AnnotationKind.Line or AnnotationKind.Arrow;
    public bool IsFreehand => Kind is AnnotationKind.Pen or AnnotationKind.Highlighter;
    public bool IsRedaction => Kind is AnnotationKind.Pixelate or AnnotationKind.Blur or AnnotationKind.Redact;
    public bool SupportsFill => IsShape;
    public bool SupportsTextStyle => Kind == AnnotationKind.Text;
    public bool SupportsStrength => Kind is AnnotationKind.Pixelate or AnnotationKind.Blur;
    public bool IsPointBased => IsLine || IsFreehand;

    public static Annotation CreateShape(AnnotationKind kind, RectangleF rect, Color color, float strokeWidth, bool filled)
        => new()
        {
            Kind = kind,
            Bounds = GeometryUtil.Normalize(rect),
            Color = color,
            StrokeWidth = Math.Max(1f, strokeWidth),
            Filled = filled
        };

    public static Annotation CreateLine(PointF start, PointF end, Color color, float strokeWidth, bool arrow)
        => new()
        {
            Kind = arrow ? AnnotationKind.Arrow : AnnotationKind.Line,
            Start = start,
            End = end,
            Bounds = GeometryUtil.FromPoints(start, end),
            Color = color,
            StrokeWidth = Math.Max(1f, strokeWidth)
        };

    public static Annotation CreateFreehand(AnnotationKind kind, IEnumerable<PointF> points, Color color, float strokeWidth)
    {
        var list = points.ToList();
        if (list.Count == 0) list.Add(PointF.Empty);
        return new Annotation
        {
            Kind = kind,
            Points = list,
            Bounds = PointsBounds(list, 0),
            Color = color,
            StrokeWidth = Math.Max(1f, strokeWidth)
        };
    }

    public static Annotation CreateText(PointF anchor, string text, Color color, float fontSize, bool bold, bool background)
    {
        var a = new Annotation
        {
            Kind = AnnotationKind.Text,
            Text = text ?? string.Empty,
            Color = color,
            FontSize = Math.Max(4f, fontSize),
            FontBold = bold,
            TextBackground = background
        };
        a.UpdateTextLayout(anchor);
        return a;
    }

    public static Annotation CreateStep(PointF center, int number, Color color, float size)
    {
        size = Math.Max(8f, size);
        return new Annotation
        {
            Kind = AnnotationKind.Step,
            StepNumber = Math.Max(1, number),
            Color = color,
            Bounds = new RectangleF(center.X - size / 2f, center.Y - size / 2f, size, size),
            StrokeWidth = 1.5f
        };
    }

    public static Annotation CreateRedaction(AnnotationKind kind, RectangleF rect, Color color, int strength)
        => new()
        {
            Kind = kind,
            Bounds = GeometryUtil.Normalize(rect),
            Color = color,
            Strength = Math.Max(2, strength),
            StrokeWidth = 1f
        };

    public void UpdateTextLayout(PointF anchor)
    {
        var size = TextMetrics.MeasureTextWithPadding(Text, FontSize, FontBold);
        Bounds = new RectangleF(anchor.X, anchor.Y, size.Width, size.Height);
    }

    public void UpdateTextLayout()
        => UpdateTextLayout(new PointF(Bounds.X, Bounds.Y));

    public RectangleF GetBounds()
    {
        switch (Kind)
        {
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                return Inflate(GeometryUtil.FromPoints(Start, End), Math.Max(2f, StrokeWidth / 2f));
            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                return Inflate(PointsBounds(Points, StrokeWidth / 2f), 2f);
            default:
                return GeometryUtil.Normalize(Bounds);
        }
    }

    public RectangleF GetSelectionBounds() => Inflate(GetBounds(), 1f);

    public bool HitTest(PointF p, float tolerance)
    {
        var bounds = GetBounds();
        if (!Inflate(bounds, tolerance).Contains(p)) return false;

        switch (Kind)
        {
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                return GeometryUtil.DistanceToSegment(p, Start, End) <= tolerance + StrokeWidth / 2f;

            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
                if (Points.Count == 1) return GeometryUtil.Distance(p, Points[0]) <= tolerance + StrokeWidth / 2f;
                for (int i = 1; i < Points.Count; i++)
                    if (GeometryUtil.DistanceToSegment(p, Points[i - 1], Points[i]) <= tolerance + StrokeWidth / 2f)
                        return true;
                return false;

            case AnnotationKind.Step:
            {
                var c = GeometryUtil.Center(bounds);
                float radius = Math.Min(bounds.Width, bounds.Height) / 2f;
                return GeometryUtil.Distance(p, c) <= radius + tolerance;
            }

            default:
                return true;
        }
    }

    public PointF[] GetHandles()
    {
        switch (Kind)
        {
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                return new[] { Start, End };
            case AnnotationKind.Text:
                return Array.Empty<PointF>();
            default:
                return GeometryUtil.HandlePoints(GetBounds());
        }
    }

    public void MoveHandle(int handle, PointF p)
    {
        if (IsLine)
        {
            if (handle == 0) Start = p;
            else if (handle == 1) End = p;
            return;
        }

        var old = GetBounds();
        var resized = GeometryUtil.ResizeFromHandle(old, handle, p);

        if (Kind == AnnotationKind.Step)
        {
            var center = GeometryUtil.Center(resized);
            float size = Math.Max(8f, Math.Max(resized.Width, resized.Height));
            Bounds = new RectangleF(center.X - size / 2f, center.Y - size / 2f, size, size);
            return;
        }

        ScaleTo(resized, old);
    }

    public void ScaleTo(RectangleF newBounds, RectangleF? oldBounds = null)
    {
        var old = oldBounds ?? GetBounds();
        if (IsLine)
        {
            Start = GeometryUtil.ScalePoint(Start, old, newBounds);
            End = GeometryUtil.ScalePoint(End, old, newBounds);
            Bounds = GeometryUtil.FromPoints(Start, End);
            return;
        }

        if (IsFreehand)
        {
            for (int i = 0; i < Points.Count; i++)
                Points[i] = GeometryUtil.ScalePoint(Points[i], old, newBounds);
            Bounds = PointsBounds(Points, 0);
            return;
        }

        Bounds = GeometryUtil.Normalize(newBounds);
    }

    public void MoveBy(float dx, float dy)
    {
        if (dx == 0 && dy == 0) return;
        Bounds = new RectangleF(Bounds.X + dx, Bounds.Y + dy, Bounds.Width, Bounds.Height);
        Start = new PointF(Start.X + dx, Start.Y + dy);
        End = new PointF(End.X + dx, End.Y + dy);
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new PointF(Points[i].X + dx, Points[i].Y + dy);
    }

    public Annotation Clone()
    {
        var copy = (Annotation)MemberwiseClone();
        copy.Points = new List<PointF>(Points);
        return copy;
    }

    public void CopyFrom(Annotation other)
    {
        Kind = other.Kind;
        Bounds = other.Bounds;
        Start = other.Start;
        End = other.End;
        Points = new List<PointF>(other.Points);
        Color = other.Color;
        StrokeWidth = other.StrokeWidth;
        Filled = other.Filled;
        Text = other.Text;
        FontSize = other.FontSize;
        FontBold = other.FontBold;
        TextBackground = other.TextBackground;
        StepNumber = other.StepNumber;
        Strength = other.Strength;
    }

    public string DescribeForTooltip()
    {
        return Kind switch
        {
            AnnotationKind.Step => $"Step {StepNumber}",
            AnnotationKind.Text => string.IsNullOrWhiteSpace(Text) ? "Text" : (Text.Length > 40 ? Text[..40] + "…" : Text),
            AnnotationKind.Pixelate => "Pixelate",
            AnnotationKind.Blur => "Blur",
            AnnotationKind.Redact => "Redact",
            AnnotationKind.Arrow => "Arrow",
            _ => Kind.ToString()
        };
    }

    private static RectangleF PointsBounds(List<PointF> points, float pad)
    {
        if (points.Count == 0) return RectangleF.Empty;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }
        var r = RectangleF.FromLTRB(minX, minY, maxX, maxY);
        return pad > 0 ? Inflate(r, pad) : r;
    }

    private static RectangleF Inflate(RectangleF r, float amount)
        => new(r.X - amount, r.Y - amount, r.Width + amount * 2f, r.Height + amount * 2f);
}

public static class AnnotationDefaults
{
    public static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var s = hex.Trim();
            if (s.StartsWith('#')) s = s[1..];
            if (s.Length == 6)
            {
                int rgb = Convert.ToInt32(s, 16);
                return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
            if (s.Length == 8)
            {
                uint argb = Convert.ToUInt32(s, 16);
                return Color.FromArgb((int)((argb >> 24) & 0xFF), (int)((argb >> 16) & 0xFF),
                    (int)((argb >> 8) & 0xFF), (int)(argb & 0xFF));
            }
        }
        catch { /* ignore */ }
        return fallback;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static int NextStepNumber(IEnumerable<Annotation> annotations, int start)
    {
        int max = Math.Max(0, start - 1);
        foreach (var a in annotations)
            if (a.Kind == AnnotationKind.Step)
                max = Math.Max(max, a.StepNumber);
        return max + 1;
    }
}
