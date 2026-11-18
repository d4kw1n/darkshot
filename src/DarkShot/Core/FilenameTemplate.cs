using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DarkShot.Core;

public static class ImageFormatInfo
{
    public static string Extension(ImageFormatKind kind) => kind switch
    {
        ImageFormatKind.Jpeg => ".jpg",
        ImageFormatKind.Bmp => ".bmp",
        _ => ".png"
    };

    public static string DisplayName(ImageFormatKind kind) => kind switch
    {
        ImageFormatKind.Jpeg => "JPEG",
        ImageFormatKind.Bmp => "BMP",
        _ => "PNG"
    };

    public static ImageFormat ToGdi(ImageFormatKind kind) => kind switch
    {
        ImageFormatKind.Jpeg => ImageFormat.Jpeg,
        ImageFormatKind.Bmp => ImageFormat.Bmp,
        _ => ImageFormat.Png
    };

    public static ImageFormatKind FromExtension(string? pathOrExtension)
    {
        var ext = (pathOrExtension ?? "").ToLowerInvariant();
        if (ext.EndsWith(".jpeg") || ext.EndsWith(".jpg") || ext == ".jpeg" || ext == ".jpg") return ImageFormatKind.Jpeg;
        if (ext.EndsWith(".bmp") || ext == ".bmp") return ImageFormatKind.Bmp;
        return ImageFormatKind.Png;
    }
}

public static class FilenameTemplate
{
    private static readonly string[] KnownExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    public static string Build(string pattern, CaptureMetadata metadata, long sequence)
    {
        var captured = metadata.CapturedAt;
        string type = metadata.Kind switch
        {
            CaptureKind.Window => "window",
            CaptureKind.Fullscreen => "fullscreen",
            CaptureKind.AllMonitors => "all-monitors",
            CaptureKind.Clipboard => "clipboard",
            CaptureKind.OpenedFile => "image",
            _ => "region"
        };

        string app = SanitizeToken(metadata.SourceProcess, "screen");
        string title = SanitizeToken(metadata.SourceTitle, "capture");

        string name = pattern ?? "";
        foreach (var ext in KnownExtensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^ext.Length];
                break;
            }
        }

        name = Regex.Replace(name, @"\{date:([^{}]+)\}", m => FormatDate(captured, m.Groups[1].Value, "yyyy-MM-dd"), RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\{time:([^{}]+)\}", m => FormatDate(captured, m.Groups[1].Value, "HH-mm-ss"), RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\{datetime:([^{}]+)\}", m => FormatDate(captured, m.Groups[1].Value, "yyyy-MM-dd_HH-mm-ss"), RegexOptions.IgnoreCase);

        name = name
            .Replace("{date}", captured.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", captured.ToString("HH-mm-ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{datetime}", captured.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{yyyy}", captured.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{yy}", captured.ToString("yy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{MM}", captured.ToString("MM", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{dd}", captured.ToString("dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{HH}", captured.ToString("HH", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{mm}", captured.ToString("mm", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{ss}", captured.ToString("ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", type, StringComparison.OrdinalIgnoreCase)
            .Replace("{kind}", type, StringComparison.OrdinalIgnoreCase)
            .Replace("{app}", app, StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{w}", metadata.ImageWidth.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{width}", metadata.ImageWidth.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{h}", metadata.ImageHeight.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{height}", metadata.ImageHeight.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{n}", sequence.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        name = Regex.Replace(name, @"\{n:0+\}", m =>
        {
            int digits = m.Value.Length - 4;
            return sequence.ToString("D" + Math.Clamp(digits, 1, 12), CultureInfo.InvariantCulture);
        }, RegexOptions.IgnoreCase);

        // Drop unknown template tokens so a typo does not end up in the filename.
        name = Regex.Replace(name, @"\{[A-Za-z][A-Za-z0-9_]*\}", "");

        name = SanitizeFileName(name);
        if (string.IsNullOrWhiteSpace(name)) name = "screenshot";
        if (IsReservedDeviceName(name)) name = "_" + name;
        if (name.Length > 180) name = name[..180].TrimEnd('.', ' ', '_');
        return name;
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c == '\r' || c == '\n' || c == '\t' || char.IsControl(c) || invalid.Contains(c))
            {
                sb.Append('_');
            }
            else if (c == ' ')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        var s = Regex.Replace(sb.ToString(), "_{2,}", "_");
        s = Regex.Replace(s, @"[\s]+", "_");
        s = s.Trim(' ', '.', '_', '-');
        return s;
    }

    public static string EnsureUnique(string folder, string fileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;

        for (int i = 2; i < 10000; i++)
        {
            candidate = Path.Combine(folder, $"{baseName}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(folder, $"{baseName}_{Guid.NewGuid():N}{ext}");
    }

    public static string DefaultFileName(CaptureMetadata metadata, string pattern, ImageFormatKind format, long sequence)
        => Build(pattern, metadata, sequence) + ImageFormatInfo.Extension(format);

    private static string SanitizeToken(string? value, string fallback)
    {
        var s = SanitizeFileName(value ?? "");
        if (string.IsNullOrWhiteSpace(s)) s = fallback;
        return s.Length > 60 ? s[..60] : s;
    }

    private static string FormatDate(DateTime captured, string format, string fallback)
    {
        try { return captured.ToString(format, CultureInfo.InvariantCulture); }
        catch { return captured.ToString(fallback, CultureInfo.InvariantCulture); }
    }

    private static bool IsReservedDeviceName(string name)
    {
        string stem = name.Split('.')[0].Trim().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL") return true;
        if (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))
            return true;
        return false;
    }
}
