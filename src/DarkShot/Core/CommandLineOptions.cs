using System.Drawing;
using System.Globalization;

namespace DarkShot.Core;

public sealed class CommandLineOptions
{
    public bool Region { get; set; }
    public bool Fullscreen { get; set; }
    public bool AllMonitors { get; set; }
    public bool ActiveWindow { get; set; }
    public bool RepeatLast { get; set; }
    public bool OpenSettings { get; set; }
    public bool OpenHistory { get; set; }
    public bool ClipboardImage { get; set; }
    public bool Quit { get; set; }
    public bool Help { get; set; }
    public bool Version { get; set; }
    public bool Startup { get; set; }
    public bool NoEditor { get; set; }
    public bool Copy { get; set; }
    public bool Silent { get; set; }
    public bool AddTimestamp { get; set; }
    public bool AddBorder { get; set; }

    public string? OpenFile { get; set; }
    public Rectangle? Rect { get; set; }
    public string? Output { get; set; }
    public int? DelaySeconds { get; set; }
    public ImageFormatKind? Format { get; set; }
    public int? Width { get; set; }
    public int? Dpi { get; set; }
    public double? PrintWidthCm { get; set; }

    public List<string> Errors { get; } = new();

    public bool HasCaptureAction => Region || Fullscreen || AllMonitors || ActiveWindow || RepeatLast || Rect.HasValue;
    public bool HasUiAction => OpenSettings || OpenHistory || !string.IsNullOrWhiteSpace(OpenFile) || ClipboardImage;

    public ExportSettings ApplyTo(ExportSettings baseSettings)
    {
        var export = baseSettings.Clone();
        if (Format.HasValue) export.Format = Format.Value;
        if (Width.HasValue) export.TargetWidth = Width.Value;
        if (Dpi.HasValue) export.Dpi = Dpi.Value;
        if (PrintWidthCm.HasValue) export.PrintWidthCm = PrintWidthCm.Value;
        if (AddTimestamp) export.AddTimestamp = true;
        if (AddBorder) export.AddBorder = true;
        return export;
    }

    public static CommandLineOptions Parse(string[] args)
    {
        var o = new CommandLineOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            string key = raw;
            string? inlineValue = null;
            int eq = raw.IndexOf('=');
            if (eq > 0)
            {
                key = raw[..eq];
                inlineValue = raw[(eq + 1)..];
            }

            string NextValue()
            {
                if (inlineValue != null) return inlineValue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) return args[++i];
                o.Errors.Add($"Missing value for {key}");
                return "";
            }

            switch (key.ToLowerInvariant())
            {
                case "--region":
                case "-r":
                    o.Region = true;
                    break;
                case "--full":
                case "--fullscreen":
                case "-f":
                    o.Fullscreen = true;
                    break;
                case "--all-monitors":
                case "--all":
                    o.AllMonitors = true;
                    o.Fullscreen = true;
                    break;
                case "--window":
                case "-w":
                    o.ActiveWindow = true;
                    break;
                case "--repeat":
                    o.RepeatLast = true;
                    break;
                case "--settings":
                    o.OpenSettings = true;
                    break;
                case "--history":
                    o.OpenHistory = true;
                    break;
                case "--clipboard":
                    o.ClipboardImage = true;
                    break;
                case "--open":
                    o.OpenFile = NextValue();
                    break;
                case "--rect":
                {
                    // Negative coordinates are valid on multi-monitor desktops, so take
                    // the next token even when it starts with '-'.
                    string v = inlineValue ?? (i + 1 < args.Length ? args[++i] : "");
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        if (TryParseRect(v, out var rect)) o.Rect = rect;
                        else o.Errors.Add($"Invalid --rect value: {v} (expected x,y,w,h)");
                    }
                    else
                    {
                        o.Errors.Add("Missing value for --rect");
                    }
                    break;
                }
                case "--output":
                case "-o":
                    o.Output = NextValue();
                    break;
                case "--delay":
                {
                    string v = NextValue();
                    if (int.TryParse(v, out int d)) o.DelaySeconds = Math.Clamp(d, 0, 60);
                    else o.Errors.Add($"Invalid --delay value: {v}");
                    break;
                }
                case "--format":
                {
                    string v = NextValue().ToLowerInvariant();
                    o.Format = v switch
                    {
                        "png" => ImageFormatKind.Png,
                        "jpg" or "jpeg" => ImageFormatKind.Jpeg,
                        "bmp" => ImageFormatKind.Bmp,
                        _ => null
                    };
                    if (o.Format == null) o.Errors.Add($"Unknown --format: {v} (use png, jpg, bmp)");
                    break;
                }
                case "--width":
                {
                    string v = NextValue();
                    if (int.TryParse(v, out int w) && w >= 64) o.Width = w;
                    else o.Errors.Add($"Invalid --width value: {v}");
                    break;
                }
                case "--dpi":
                {
                    string v = NextValue();
                    if (int.TryParse(v, out int d) && d >= 48) o.Dpi = d;
                    else o.Errors.Add($"Invalid --dpi value: {v}");
                    break;
                }
                case "--print-width":
                {
                    string v = NextValue();
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double cm) ||
                        double.TryParse(v, NumberStyles.Float, CultureInfo.CurrentCulture, out cm))
                        o.PrintWidthCm = cm;
                    else o.Errors.Add($"Invalid --print-width value: {v}");
                    break;
                }
                case "--timestamp":
                    o.AddTimestamp = true;
                    break;
                case "--border":
                    o.AddBorder = true;
                    break;
                case "--copy":
                    o.Copy = true;
                    break;
                case "--no-editor":
                    o.NoEditor = true;
                    break;
                case "--silent":
                    o.Silent = true;
                    break;
                case "--startup":
                    o.Startup = true;
                    break;
                case "--quit":
                    o.Quit = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    o.Help = true;
                    break;
                case "--version":
                case "-v":
                    o.Version = true;
                    break;
                default:
                    if (key.StartsWith('-'))
                        o.Errors.Add($"Unknown option: {key}");
                    break;
            }
        }

        return o;
    }

    public static bool TryParseRect(string text, out Rectangle rect)
    {
        rect = Rectangle.Empty;
        var parts = text.Split(new[] { ',', ';', 'x', 'X', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) return false;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return false;
        if (w <= 0 || h <= 0) return false;
        rect = new Rectangle(x, y, w, h);
        return true;
    }

    public static string HelpText => """
        DarkShot — screenshot tool for pentest / red-team / bug-bounty evidence

        Usage:
          DarkShot.exe [options]

        Capture:
          -r, --region              select a screen region (interactive)
          -f, --fullscreen          capture the current monitor
              --all-monitors        capture the entire virtual desktop
          -w, --window              capture the active window
              --repeat              capture the last region again
              --rect x,y,w,h        capture a physical screen rectangle without UI
              --delay N             wait N seconds before capturing (0-60)
              --clipboard           open the image currently on the clipboard

        Output (used with capture / --no-editor):
          -o, --output PATH         save to file (or folder for an auto name)
              --format png|jpg|bmp  output format
              --width PX            resize to this pixel width
              --dpi N               DPI metadata (Word/print sizing)
              --print-width CM      derive DPI from a physical width in cm
              --timestamp           add timestamp / evidence label watermark
              --border              add a 1 px border
              --copy                also copy the result to the clipboard
              --no-editor           do not open the editor
              --silent              suppress notifications

        App:
              --settings            open settings
              --history             open capture history
              --open FILE           open an image in the editor
              --startup             silent tray start (used by Windows startup)
              --quit                exit the running instance
          -h, --help                show this help
          -v, --version             show version

        Examples:
          DarkShot.exe --rect 0,0,1280,720 --output C:\PoC\login.png --width 1200 --dpi 190
          DarkShot.exe --fullscreen --copy --no-editor
          DarkShot.exe --open C:\PoC\finding.png
          DarkShot.exe --quit
        """;
}
