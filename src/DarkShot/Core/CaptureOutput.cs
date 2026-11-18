using System.Drawing;
using DarkShot.Annotations;

namespace DarkShot.Core;

public sealed record SaveOutcome(string FilePath, Size PixelSize, int Dpi, double PrintWidthCm, long Bytes);

public sealed record CopyOutcome(Size PixelSize, int Dpi, double PrintWidthCm, bool Success);

public static class CaptureOutput
{
    public static string BuildAutoPath(AppSettings settings, CaptureMetadata metadata, ImageFormatKind format, long sequence)
    {
        string folder = string.IsNullOrWhiteSpace(settings.SaveFolder)
            ? AppSettings.DefaultSaveFolder()
            : settings.SaveFolder;
        Directory.CreateDirectory(folder);
        string name = FilenameTemplate.Build(settings.FilenamePattern, metadata, sequence)
                      + ImageFormatInfo.Extension(format);
        return FilenameTemplate.EnsureUnique(folder, name);
    }

    public static ExportResult Render(Bitmap baseImage, IReadOnlyList<Annotation> annotations, Rectangle crop,
        AppSettings settings, ExportSettings? exportOverride = null, DateTime? capturedAt = null)
    {
        var export = exportOverride?.Clone() ?? settings.Export.Clone();
        return ExportEngine.Render(
            baseImage,
            annotations,
            crop,
            export,
            settings.EvidenceLabel,
            settings.TimestampFormat,
            capturedAt ?? DateTime.Now);
    }

    public static SaveOutcome Save(Bitmap baseImage, IReadOnlyList<Annotation> annotations, Rectangle crop,
        CaptureMetadata metadata, AppSettings settings, ExportSettings? exportOverride = null, string? explicitPath = null)
    {
        var export = (exportOverride ?? settings.Export).Clone();
        if (!string.IsNullOrWhiteSpace(explicitPath))
            export.Format = ImageFormatInfo.FromExtension(explicitPath);

        long sequence = settings.LastSequence + 1;
        string path = !string.IsNullOrWhiteSpace(explicitPath)
            ? Path.GetFullPath(explicitPath)
            : BuildAutoPath(settings, metadata, export.Format, sequence);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        using var rendered = ExportEngine.Render(
            baseImage,
            annotations,
            crop,
            export,
            settings.EvidenceLabel,
            settings.TimestampFormat,
            metadata.CapturedAt);

        byte[] bytes = ExportEngine.Encode(rendered.Image, export.Format, export.JpegQuality);

        string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        }

        if (string.IsNullOrWhiteSpace(explicitPath))
        {
            settings.LastSequence = sequence;
            SettingsStore.Save(settings);
        }

        Log.Info($"Saved {rendered.PixelSize.Width}x{rendered.PixelSize.Height} @ {rendered.EffectiveDpi} DPI to {path} ({bytes.Length} bytes)");
        return new SaveOutcome(path, rendered.PixelSize, rendered.EffectiveDpi, rendered.PrintWidthCm, bytes.Length);
    }

    public static CopyOutcome Copy(Bitmap baseImage, IReadOnlyList<Annotation> annotations, Rectangle crop,
        AppSettings settings, ExportSettings? exportOverride = null, DateTime? capturedAt = null)
    {
        using var rendered = Render(baseImage, annotations, crop, settings, exportOverride, capturedAt);
        bool ok = ClipboardHelper.CopyImage(rendered.Image);
        if (ok)
            Log.Info($"Copied {rendered.PixelSize.Width}x{rendered.PixelSize.Height} @ {rendered.EffectiveDpi} DPI to clipboard");
        return new CopyOutcome(rendered.PixelSize, rendered.EffectiveDpi, rendered.PrintWidthCm, ok);
    }

    public static string SuggestedFileName(AppSettings settings, CaptureMetadata metadata, ExportSettings export)
        => FilenameTemplate.DefaultFileName(metadata, settings.FilenamePattern, export.Format, settings.LastSequence + 1);
}
