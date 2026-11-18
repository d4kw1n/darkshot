using System.Drawing;
using System.Drawing.Drawing2D;

namespace DarkShot.Annotations;

public static class GeometryUtil
{
    public static RectangleF Normalize(RectangleF r)
    {
        float x = Math.Min(r.Left, r.Right);
        float y = Math.Min(r.Top, r.Bottom);
        return new RectangleF(x, y, Math.Abs(r.Width), Math.Abs(r.Height));
    }

    public static RectangleF FromPoints(PointF a, PointF b)
        => Normalize(RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y));

    public static PointF Center(RectangleF r) => new(r.X + r.Width / 2f, r.Y + r.Height / 2f);

    public static float Distance(PointF a, PointF b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        if (len2 <= 0.0001f) return Distance(p, a);
        float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        t = Math.Clamp(t, 0f, 1f);
        return Distance(p, new PointF(a.X + t * dx, a.Y + t * dy));
    }

    public static PointF[] HandlePoints(RectangleF r)
    {
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + r.Height / 2f;
        return new[]
        {
            new PointF(r.Left, r.Top),
            new PointF(cx, r.Top),
            new PointF(r.Right, r.Top),
            new PointF(r.Right, cy),
            new PointF(r.Right, r.Bottom),
            new PointF(cx, r.Bottom),
            new PointF(r.Left, r.Bottom),
            new PointF(r.Left, cy)
        };
    }

    public static RectangleF ResizeFromHandle(RectangleF r, int handle, PointF p, float minSize = 4f)
    {
        float l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
        switch (handle)
        {
            case 0: l = p.X; t = p.Y; break;
            case 1: t = p.Y; break;
            case 2: rt = p.X; t = p.Y; break;
            case 3: rt = p.X; break;
            case 4: rt = p.X; b = p.Y; break;
            case 5: b = p.Y; break;
            case 6: l = p.X; b = p.Y; break;
            case 7: l = p.X; break;
        }

        if (rt - l < minSize)
        {
            if (handle is 0 or 6 or 7) l = rt - minSize;
            else rt = l + minSize;
        }
        if (b - t < minSize)
        {
            if (handle is 0 or 1 or 2) t = b - minSize;
            else b = t + minSize;
        }

        return RectangleF.FromLTRB(l, t, rt, b);
    }

    public static PointF ScalePoint(PointF p, RectangleF oldBounds, RectangleF newBounds)
    {
        float sx = oldBounds.Width <= 0.0001f ? 1f : newBounds.Width / oldBounds.Width;
        float sy = oldBounds.Height <= 0.0001f ? 1f : newBounds.Height / oldBounds.Height;
        return new PointF(newBounds.X + (p.X - oldBounds.X) * sx,
                          newBounds.Y + (p.Y - oldBounds.Y) * sy);
    }

    public static RectangleF IntersectOrEmpty(RectangleF a, RectangleF b)
    {
        var r = RectangleF.Intersect(a, b);
        return r.Width <= 0 || r.Height <= 0 ? RectangleF.Empty : r;
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) return path;
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        if (radius <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        float d = radius * 2f;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static int RelativeLuminance(Color c)
        => (int)(0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B);

    public static Color ContrastingTextColor(Color background)
        => RelativeLuminance(background) > 150 ? Color.FromArgb(255, 20, 20, 22) : Color.White;
}
