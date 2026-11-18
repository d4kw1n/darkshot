using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using DarkShot.Annotations;

namespace DarkShot.Core;

public sealed class ExportResult : IDisposable
{
    public Bitmap Image { get; }
    public int EffectiveDpi { get; }
    public ImageFormatKind Format { get; }

    public ExportResult(Bitmap image, int effectiveDpi, ImageFormatKind format)
    {
        Image = image;
        EffectiveDpi = effectiveDpi;
        Format = format;
    }

    public Size PixelSize => Image.Size;

    public double PrintWidthCm => EffectiveDpi <= 0 ? 0 : PixelSize.Width / (double)EffectiveDpi * 2.54;
    public double PrintHeightCm => EffectiveDpi <= 0 ? 0 : PixelSize.Height / (double)EffectiveDpi * 2.54;

    public void Dispose() => Image.Dispose();
}

public static class ExportEngine
{
    public static ExportResult Render(Bitmap baseImage, IReadOnlyList<Annotation> annotations, Rectangle crop,
        ExportSettings export, string? evidenceLabel, string timestampFormat, DateTime capturedAt)
    {
        var composed = AnnotationRenderer.Compose(baseImage, annotations, crop);
        Bitmap? final = composed;
        bool transferred = false;

        try
        {
            int targetWidth = export.TargetWidth > 0 ? export.TargetWidth : composed.Width;
            targetWidth = Math.Clamp(targetWidth, 64, 12000);

            if (targetWidth != composed.Width)
            {
                int targetHeight = Math.Max(1, (int)Math.Round(composed.Height * (double)targetWidth / composed.Width));
                targetHeight = Math.Min(targetHeight, 30000);
                var scaled = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(composed, new Rectangle(0, 0, targetWidth, targetHeight),
                        0, 0, composed.Width, composed.Height, GraphicsUnit.Pixel);
                }
                final = scaled;
            }

            int dpi = CalculateEffectiveDpi(final.Width, export);
            if (export.AddTimestamp)
                DrawTimestamp(final, evidenceLabel, timestampFormat, capturedAt);
            if (export.AddBorder)
                DrawBorder(final);

            try { final.SetResolution(dpi, dpi); } catch { /* some formats/drivers reject extreme DPI */ }

            var result = new ExportResult(final, dpi, export.Format);
            if (!ReferenceEquals(final, composed))
                composed.Dispose();

            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                if (!ReferenceEquals(final, composed))
                    final?.Dispose();
                composed.Dispose();
            }
        }
    }

    public static int CalculateEffectiveDpi(int pixelWidth, ExportSettings export)
    {
        if (export.PrintWidthCm > 0.1 && pixelWidth > 0)
        {
            double dpi = pixelWidth / (export.PrintWidthCm / 2.54);
            return (int)Math.Clamp(Math.Round(dpi), 48, 1200);
        }
        return Math.Clamp(export.Dpi, 48, 1200);
    }

    public static SizeF CalculatePrintSize(Size pixelSize, int dpi)
    {
        if (dpi <= 0) return SizeF.Empty;
        return new SizeF((float)(pixelSize.Width / (double)dpi * 2.54),
                         (float)(pixelSize.Height / (double)dpi * 2.54));
    }

    public static byte[] Encode(Bitmap image, ImageFormatKind format, int jpegQuality)
    {
        using var ms = new MemoryStream();
        switch (format)
        {
            case ImageFormatKind.Jpeg:
                SaveJpeg(image, ms, jpegQuality);
                break;
            case ImageFormatKind.Bmp:
                image.Save(ms, ImageFormat.Bmp);
                break;
            default:
                image.Save(ms, ImageFormat.Png);
                break;
        }
        return ms.ToArray();
    }

    public static void SaveJpeg(Image image, Stream stream, int quality)
    {
        quality = Math.Clamp(quality, 10, 100);
        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
        if (encoder == null)
        {
            image.Save(stream, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
        image.Save(stream, encoder, parameters);
    }

    private static void DrawTimestamp(Bitmap image, string? evidenceLabel, string timestampFormat, DateTime capturedAt)
    {
        string time;
        try { time = capturedAt.ToString(string.IsNullOrWhiteSpace(timestampFormat) ? "yyyy-MM-dd HH:mm:ss" : timestampFormat, CultureInfo.CurrentCulture); }
        catch { time = capturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

        string label = (evidenceLabel ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (label.Length > 90) label = label[..90].TrimEnd() + "…";
        string text = string.IsNullOrEmpty(label) ? time : $"{label}   ·   {time}";
        if (string.IsNullOrWhiteSpace(text)) return;

        float margin = Math.Max(6f, image.Width / 160f);
        float fontSize = Math.Clamp(image.Width / 72f, 9f, 30f);
        float padX = fontSize * 0.55f;
        float padY = fontSize * 0.35f;
        float accentW = Math.Max(2f, fontSize * 0.16f);

        SizeF textSize = TextMetrics.MeasureText(text, fontSize, bold: true);
        for (int guard = 0; guard < 12 && textSize.Width + padX * 2 + margin * 2 > image.Width; guard++)
        {
            fontSize = Math.Max(7f, fontSize * 0.9f);
            textSize = TextMetrics.MeasureText(text, fontSize, bold: true);
        }

        float boxW = textSize.Width + padX * 2 + accentW + 3f;
        float boxH = textSize.Height + padY * 2;
        float x = image.Width - boxW - margin;
        float y = image.Height - boxH - margin;

        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var bg = new RectangleF(x, y, boxW, boxH);
        using (var path = GeometryUtil.RoundedRect(bg, Math.Min(5f, boxH / 3f)))
        using (var fill = new SolidBrush(Color.FromArgb(172, 10, 12, 16)))
            g.FillPath(fill, path);

        using (var accent = new SolidBrush(Color.FromArgb(255, 255, 74, 62)))
            g.FillRectangle(accent, x + 1f, y + boxH * 0.22f, accentW, boxH * 0.56f);

        var font = TextMetrics.GetFont(fontSize, bold: true);
        using var format = TextMetrics.CreateDrawFormat();
        using var brush = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        g.DrawString(text, font, brush, new PointF(x + accentW + padX + 2f, y + padY), format);
    }

    private static void DrawBorder(Bitmap image)
    {
        using var g = Graphics.FromImage(image);
        using var pen = new Pen(Color.FromArgb(255, 205, 208, 215), 1f);
        g.DrawRectangle(pen, 0.5f, 0.5f, image.Width - 1.5f, image.Height - 1.5f);
    }
}
