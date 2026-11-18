using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkShot.Core;

public enum AfterCaptureAction
{
    OpenEditor = 0,
    CopyToClipboard = 1,
    SaveToFile = 2
}

public enum ImageFormatKind
{
    Png = 0,
    Jpeg = 1,
    Bmp = 2
}

public enum FullscreenScope
{
    CurrentMonitor = 0,
    AllMonitors = 1
}

public enum CaptureKind
{
    Region,
    Window,
    Fullscreen,
    AllMonitors,
    Clipboard,
    OpenedFile
}

public sealed class HotkeySettings
{
    public string Region { get; set; } = "PrintScreen";
    public string Fullscreen { get; set; } = "Ctrl+PrintScreen";
    public string ActiveWindow { get; set; } = "Ctrl+Alt+PrintScreen";
    public string RepeatLast { get; set; } = "Ctrl+Shift+PrintScreen";
}

public sealed class ExportSettings
{
    /// <summary>0 = keep original width, otherwise resize to this pixel width.</summary>
    public int TargetWidth { get; set; } = 0;

    /// <summary>DPI metadata written into the output file. Used by Word to size the image.</summary>
    public int Dpi { get; set; } = 96;

    /// <summary>Optional physical print width in centimetres. When &gt; 0, DPI is derived from it.</summary>
    public double PrintWidthCm { get; set; } = 0;

    public ImageFormatKind Format { get; set; } = ImageFormatKind.Png;
    public int JpegQuality { get; set; } = 92;
    public bool AddBorder { get; set; } = false;
    public bool AddTimestamp { get; set; } = false;

    public ExportSettings Clone() => (ExportSettings)MemberwiseClone();
}

public sealed class AnnotationSettings
{
    public string Color { get; set; } = "#FF3B30";
    public string HighlighterColor { get; set; } = "#FFE600";
    public string StepColor { get; set; } = "#FF3B30";
    public float StrokeWidth { get; set; } = 3f;
    public float HighlighterWidth { get; set; } = 18f;
    public float FontSize { get; set; } = 18f;
    public bool FontBold { get; set; } = true;
    public bool TextBackground { get; set; } = true;
    public bool FilledShapes { get; set; } = false;
    public int PixelBlock { get; set; } = 12;
    public int BlurRadius { get; set; } = 18;
    public int StepSize { get; set; } = 30;
    public int NextStepStart { get; set; } = 1;
}

public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
    public Rectangle ToRectangle() => new(X, Y, Width, Height);
    public static IntRect From(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public HotkeySettings Hotkeys { get; set; } = new();
    public ExportSettings Export { get; set; } = new();
    public AnnotationSettings Annotation { get; set; } = new();

    public string SaveFolder { get; set; } = DefaultSaveFolder();
    public string FilenamePattern { get; set; } = "{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}_{type}";

    public AfterCaptureAction AfterCapture { get; set; } = AfterCaptureAction.OpenEditor;
    public FullscreenScope FullscreenScope { get; set; } = FullscreenScope.CurrentMonitor;

    public bool IncludeCursor { get; set; } = false;
    public bool ShowMagnifier { get; set; } = true;
    public bool DetectWindows { get; set; } = true;
    public int CaptureDelaySeconds { get; set; } = 0;
    public bool FreezeScreen { get; set; } = true;

    public bool CopyAfterSave { get; set; } = false;
    public bool OpenFolderAfterSave { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;
    public string EvidenceLabel { get; set; } = "";
    public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";

    public int EditorMaxUndo { get; set; } = 100;
    public int HistoryLimit { get; set; } = 200;

    public long LastSequence { get; set; } = 0;
    public IntRect? LastRegion { get; set; }

    public static string DefaultSaveFolder()
    {
        try
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!string.IsNullOrWhiteSpace(pictures))
                return Path.Combine(pictures, "DarkShot");
        }
        catch { /* ignore */ }

        return Path.Combine(AppPaths.DataDir, "Captures");
    }

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;

        Hotkeys ??= new HotkeySettings();
        Export ??= new ExportSettings();
        Annotation ??= new AnnotationSettings();

        if (string.IsNullOrWhiteSpace(SaveFolder))
            SaveFolder = DefaultSaveFolder();
        if (string.IsNullOrWhiteSpace(FilenamePattern))
            FilenamePattern = "{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}_{type}";
        if (string.IsNullOrWhiteSpace(TimestampFormat))
            TimestampFormat = "yyyy-MM-dd HH:mm:ss";

        CaptureDelaySeconds = Math.Clamp(CaptureDelaySeconds, 0, 60);
        EditorMaxUndo = Math.Clamp(EditorMaxUndo, 10, 500);
        HistoryLimit = Math.Clamp(HistoryLimit, 20, 1000);
        LastSequence = Math.Max(0, LastSequence);

        Export.TargetWidth = Export.TargetWidth <= 0 ? 0 : Math.Clamp(Export.TargetWidth, 64, 12000);
        Export.Dpi = Math.Clamp(Export.Dpi, 48, 1200);
        Export.PrintWidthCm = Export.PrintWidthCm <= 0 ? 0 : Math.Clamp(Export.PrintWidthCm, 1, 200);
        Export.JpegQuality = Math.Clamp(Export.JpegQuality, 10, 100);

        Annotation.StrokeWidth = Math.Clamp(Annotation.StrokeWidth, 1f, 100f);
        Annotation.HighlighterWidth = Math.Clamp(Annotation.HighlighterWidth, 4f, 200f);
        Annotation.FontSize = Math.Clamp(Annotation.FontSize, 8f, 300f);
        Annotation.PixelBlock = Math.Clamp(Annotation.PixelBlock, 2, 120);
        Annotation.BlurRadius = Math.Clamp(Annotation.BlurRadius, 2, 150);
        Annotation.StepSize = Math.Clamp(Annotation.StepSize, 10, 200);
        Annotation.NextStepStart = Math.Clamp(Annotation.NextStepStart, 1, 9999);

        if (!IsHexColor(Annotation.Color)) Annotation.Color = "#FF3B30";
        if (!IsHexColor(Annotation.HighlighterColor)) Annotation.HighlighterColor = "#FFE600";
        if (!IsHexColor(Annotation.StepColor)) Annotation.StepColor = "#FF3B30";

        if (LastRegion is { } lr && !lr.IsValid)
            LastRegion = null;
    }

    public static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6 && s.Length != 8) return false;
        return s.All(Uri.IsHexDigit);
    }

    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this, SettingsStore.JsonOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions) ?? new AppSettings();
    }

    /// <summary>Copies all public writable properties from another settings instance.</summary>
    public void CopyFrom(AppSettings other)
    {
        if (other == null) return;
        var clone = other.Clone();
        foreach (var property in typeof(AppSettings).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (property.CanWrite && property.GetIndexParameters().Length == 0)
                property.SetValue(this, property.GetValue(clone));
        }
    }
}

public static class AppPaths
{
    private static string? _dataDir;

    public static bool IsPortable
    {
        get
        {
            try
            {
                return File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));
            }
            catch { return false; }
        }
    }

    public static string DataDir
    {
        get
        {
            if (_dataDir != null) return _dataDir;

            string preferred = IsPortable
                ? Path.Combine(AppContext.BaseDirectory, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DarkShot");

            try
            {
                Directory.CreateDirectory(preferred);
                _dataDir = preferred;
            }
            catch
            {
                var fallback = Path.Combine(Path.GetTempPath(), "DarkShot");
                try { Directory.CreateDirectory(fallback); } catch { /* ignore */ }
                _dataDir = fallback;
            }

            return _dataDir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "logs", "darkshot.log");
    public static string CacheDir => Path.Combine(DataDir, "cache");
    public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;
}

public static class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };

    private static readonly object Gate = new();

    public static AppSettings Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(AppPaths.SettingsFile))
                {
                    var fresh = new AppSettings();
                    fresh.Normalize();
                    return fresh;
                }

                var json = File.ReadAllText(AppPaths.SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load settings; using defaults");
                TryBackupCorruptSettings();
                var fallback = new AppSettings();
                fallback.Normalize();
                return fallback;
            }
        }
    }

    public static bool Save(AppSettings settings)
    {
        lock (Gate)
        {
            try
            {
                settings.Normalize();
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                var tmp = AppPaths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save settings");
                return false;
            }
        }
    }

    private static void TryBackupCorruptSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return;
            var backup = AppPaths.SettingsFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(AppPaths.SettingsFile, backup, overwrite: false);
        }
        catch { /* ignore */ }
    }
}
