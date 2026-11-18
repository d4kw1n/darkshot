using System.Drawing;
using System.Drawing.Imaging;
using DarkShot.Annotations;
using DarkShot.UI.Controls;
using System.Reflection;
using DarkShot.Core;

namespace DarkShot.Tests;

internal static class CoreTests
{
    private static int _passed;
    private static int _failed;
    private static string _temp = "";

    public static int Run(string artifacts)
    {
        _temp = Path.Combine(Path.GetTempPath(), "DarkShotTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        Directory.CreateDirectory(artifacts);

        RunGroup("Hotkeys", TestHotkeys);
        RunGroup("Hotkey recorder", TestHotkeyInput);
        RunGroup("Command line", TestCommandLine);
        RunGroup("Filename templates", TestFilenames);
        RunGroup("Settings", TestSettings);
        RunGroup("Annotations", TestAnnotations);
        RunGroup("Undo/redo", TestUndo);
        RunGroup("Export engine", () => TestExport(artifacts));
        RunGroup("Screen capture", TestScreenCapture);
        RunGroup("Save + DPI metadata", () => TestSave(artifacts));
        RunGroup("Clipboard data object", TestClipboardDataObject);

        try { Directory.Delete(_temp, recursive: true); } catch { /* ignore */ }

        Console.WriteLine($"UNIT TESTS: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void RunGroup(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Fail($"{name} threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(bool condition, string name)
    {
        if (condition) { _passed++; Console.WriteLine("  PASS  " + name); }
        else Fail(name);
    }

    private static void Fail(string name)
    {
        _failed++;
        Console.WriteLine("  FAIL  " + name);
    }

    private static void Equal<T>(T actual, T expected, string name)
    {
        bool ok = EqualityComparer<T>.Default.Equals(actual, expected);
        Check(ok, $"{name} (expected {expected}, got {actual})");
    }

    private static void Near(float actual, float expected, float tolerance, string name)
    {
        Check(Math.Abs(actual - expected) <= tolerance, $"{name} (expected {expected} ± {tolerance}, got {actual})");
    }

    private static void TestHotkeys()
    {
        HotkeySpec.TryParse("PrintScreen", out var print, out _);
        Equal(print.Key, Keys.PrintScreen, "PrintScreen parses");
        Equal(print.Modifiers, HotkeyModifiers.None, "PrintScreen has no modifiers");
        Equal(print.ToDisplayString(), "PrintScreen", "PrintScreen display");

        HotkeySpec.TryParse("ctrl+shift+a", out var combo, out _);
        Equal(combo.Modifiers, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+A modifiers");
        Equal(combo.ToDisplayString(), "Ctrl+Shift+A", "Ctrl+Shift+A display");

        HotkeySpec.TryParse("ctrl+alt+printscreen", out var cap, out _);
        Equal(cap.ToDisplayString(), "Ctrl+Alt+PrintScreen", "Ctrl+Alt+PrintScreen normalization");

        HotkeySpec.TryParse("F8", out var f8, out _);
        Equal(f8.Key, Keys.F8, "F8 parses");

        Check(!HotkeySpec.TryParse("A", out _, out _), "bare letter is rejected");
        Check(!HotkeySpec.TryParse("Ctrl+Shift", out _, out _), "modifier-only is rejected");
        Check(!HotkeySpec.TryParse("Ctrl+NotARealKey", out _, out _), "unknown key is rejected");
        Check(HotkeySpec.ParseOrEmpty("").IsEmpty, "empty hotkey parses to Empty");
    }

    private static void TestHotkeyInput()
    {
        var input = new HotkeyInput();
        var method = typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(input, new object[] { new KeyEventArgs(Keys.Control | Keys.Shift | Keys.X) });
        Equal(input.HotkeyText, "Ctrl+Shift+X", "recorder captures Ctrl+Shift+X");

        method?.Invoke(input, new object[] { new KeyEventArgs(Keys.X) });
        Equal(input.HotkeyText, "Ctrl+Shift+X", "recorder rejects a bare typing key");

        method?.Invoke(input, new object[] { new KeyEventArgs(Keys.Back) });
        Equal(input.HotkeyText, "", "Delete clears a recorded hotkey");
    }

    private static void TestCommandLine()
    {
        var options = CommandLineOptions.Parse(new[]
        {
            "--rect", "-1920,0,1920,1080", "--output", @"C:\PoC\login.png",
            "--format", "jpg", "--width", "1200", "--dpi", "190", "--copy", "--no-editor", "--silent"
        });
        Equal(options.Errors.Count, 0, "negative rect command has no errors");
        Equal(options.Rect, new Rectangle(-1920, 0, 1920, 1080), "negative rect parsed");
        Equal(options.Output, @"C:\PoC\login.png", "output parsed");
        Equal(options.Format, (ImageFormatKind?)ImageFormatKind.Jpeg, "format parsed");
        Equal(options.Width, (int?)1200, "width parsed");
        Equal(options.Dpi, (int?)190, "dpi parsed");
        Check(options.Copy && options.NoEditor && options.Silent, "copy/no-editor/silent flags");

        var full = CommandLineOptions.Parse(new[] { "--full", "--all-monitors" });
        Check(full.Fullscreen && full.AllMonitors, "fullscreen all monitors flags");

        var bad = CommandLineOptions.Parse(new[] { "--rect", "not-a-rect" });
        Check(bad.Errors.Count > 0 && bad.Rect == null, "invalid rect reports an error");

        var export = options.ApplyTo(new ExportSettings { TargetWidth = 0, Dpi = 96, Format = ImageFormatKind.Png });
        Equal(export.TargetWidth, 1200, "export override width");
        Equal(export.Dpi, 190, "export override dpi");
        Equal(export.Format, ImageFormatKind.Jpeg, "export override format");
    }

    private static void TestFilenames()
    {
        var metadata = new CaptureMetadata
        {
            Kind = CaptureKind.Window,
            CapturedAt = new DateTime(2026, 11, 17, 10, 20, 30),
            SourceProcess = "chrome",
            SourceTitle = "Admin: Panel/Test",
            ImageWidth = 800,
            ImageHeight = 600
        };

        string name = FilenameTemplate.Build("{date:yyyyMMdd-HHmmss}_{type}_{app}_{w}x{h}_{n:000}", metadata, 7);
        Equal(name, "20261117-102030_window_chrome_800x600_007", "formatted date, tokens and sequence");

        string title = FilenameTemplate.Build("{title}", metadata, 1);
        Equal(title, "Admin_Panel_Test", "title sanitization");

        string clean = FilenameTemplate.Build("a:b/c*?d", metadata, 1);
        Check(!clean.Any(c => Path.GetInvalidFileNameChars().Contains(c)), "invalid filename characters removed");

        string empty = FilenameTemplate.Build("", metadata, 1);
        Equal(empty, "screenshot", "empty pattern fallback");

        string reserved = FilenameTemplate.Build("CON", metadata, 1);
        Equal(reserved, "_CON", "reserved device name escaped");

        string token = FilenameTemplate.Build("{n:00}_{unknown}", metadata, 5);
        Equal(token, "05", "sequence padding and unknown token removal");

        string folder = Path.Combine(_temp, "names");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "shot.png"), "x");
        string unique = FilenameTemplate.EnsureUnique(folder, "shot.png");
        Equal(Path.GetFileName(unique), "shot_2.png", "unique filename suffix");
    }

    private static void TestSettings()
    {
        var settings = new AppSettings
        {
            Export = new ExportSettings
            {
                TargetWidth = -5,
                Dpi = 9999,
                PrintWidthCm = -2,
                JpegQuality = 1
            },
            Annotation = new AnnotationSettings
            {
                StrokeWidth = 0,
                HighlighterWidth = 1,
                FontSize = 1,
                PixelBlock = 0,
                BlurRadius = 1,
                StepSize = 1,
                Color = "nope"
            },
            CaptureDelaySeconds = -2,
            LastRegion = new IntRect(0, 0, 0, 10)
        };
        settings.Normalize();

        Equal(settings.Export.TargetWidth, 0, "negative target width normalized");
        Equal(settings.Export.Dpi, 1200, "high dpi clamped");
        Equal(settings.Export.PrintWidthCm, 0, "negative print width normalized");
        Equal(settings.Export.JpegQuality, 10, "jpeg quality clamped");
        Near(settings.Annotation.StrokeWidth, 1f, 0.001f, "stroke width clamped");
        Near(settings.Annotation.FontSize, 8f, 0.001f, "font size clamped");
        Equal(settings.Annotation.PixelBlock, 2, "pixel block clamped");
        Equal(settings.Annotation.BlurRadius, 2, "blur radius clamped");
        Equal(settings.Annotation.StepSize, 10, "step size clamped");
        Equal(settings.Annotation.Color, "#FF3B30", "invalid colour reset");
        Equal(settings.CaptureDelaySeconds, 0, "negative delay clamped");
        Equal(settings.LastRegion, (IntRect?)null, "invalid last region cleared");

        var copy = settings.Clone();
        copy.Hotkeys.Region = "Ctrl+Alt+R";
        copy.Export.TargetWidth = 1600;
        settings.CopyFrom(copy);
        Equal(settings.Hotkeys.Region, "Ctrl+Alt+R", "CopyFrom copies hotkeys");
        Equal(settings.Export.TargetWidth, 1600, "CopyFrom replaces nested export settings");
        Check(AppSettings.IsHexColor("#AABBCC") && !AppSettings.IsHexColor("red"), "hex colour validation");
    }

    private static void TestAnnotations()
    {
        var rect = Annotation.CreateShape(AnnotationKind.Rectangle, new RectangleF(10, 10, 100, 60), Color.Red, 3, false);
        Check(rect.HitTest(new PointF(60, 40), 3), "rectangle hit test inside");
        Check(!rect.HitTest(new PointF(300, 300), 3), "rectangle miss outside");
        rect.MoveBy(5, -3);
        Near(rect.Bounds.X, 15, 0.01f, "rectangle move x");
        Near(rect.Bounds.Y, 7, 0.01f, "rectangle move y");
        Equal(rect.GetHandles().Length, 8, "rectangle has eight handles");
        rect.MoveHandle(4, new PointF(180, 100));
        Near(rect.Bounds.Width, 165, 0.5f, "rectangle resize width");
        Check(rect.Clone() is { } clone && clone.Bounds == rect.Bounds, "annotation clone");

        var line = Annotation.CreateLine(new PointF(10, 10), new PointF(110, 110), Color.Blue, 4, true);
        Check(line.HitTest(new PointF(60, 60), 3), "line hit on segment");
        Check(!line.HitTest(new PointF(60, 20), 3), "line miss away from segment");
        Equal(line.GetHandles().Length, 2, "line has endpoint handles");

        var step = Annotation.CreateStep(new PointF(100, 100), 3, Color.Gold, 30);
        Check(step.HitTest(new PointF(100, 100), 1), "step hit centre");
        Equal(AnnotationDefaults.NextStepNumber(new[] { step, Annotation.CreateStep(new PointF(0, 0), 1, Color.Red, 20) }, 1),
            4, "next step number");

        var text = Annotation.CreateText(new PointF(20, 20), "hello", Color.White, 18, true, true);
        Check(text.Bounds.Width > 10 && text.Bounds.Height > 10, "text bounds measured");
        var textClone = text.Clone();
        textClone.Text = "changed";
        Equal(text.Text, "hello", "text clone is independent");

        var redact = Annotation.CreateRedaction(AnnotationKind.Redact, new RectangleF(5, 5, 50, 20), Color.Black, 1);
        Check(redact.HitTest(new PointF(20, 10), 2), "redact hit test");
    }

    private static void TestUndo()
    {
        var history = new UndoHistory(10);
        var first = new EditorSnapshot
        {
            Annotations = new List<Annotation>(),
            CropRect = new Rectangle(0, 0, 100, 100),
            ImageBounds = new Rectangle(0, 0, 100, 100)
        };
        var second = new EditorSnapshot
        {
            Annotations = new List<Annotation> { Annotation.CreateStep(new PointF(10, 10), 1, Color.Red, 20) },
            CropRect = new Rectangle(0, 0, 100, 100),
            ImageBounds = new Rectangle(0, 0, 100, 100)
        };

        Check(!history.CanUndo, "history starts empty");
        history.Push(first);
        Check(history.CanUndo, "history can undo after push");
        var undone = history.Undo(second);
        Check(undone != null && undone.Annotations.Count == 0, "undo returns previous snapshot");
        Check(history.CanRedo, "undo enables redo");
        var redone = history.Redo(undone!);
        Check(redone != null && redone.Annotations.Count == 1, "redo restores snapshot");
        history.Push(second);
        Check(!history.CanRedo, "new push clears redo");
    }

    private static void TestExport(string artifacts)
    {
        using var baseImage = CreateTestImage(800, 450, Color.FromArgb(32, 48, 64));
        using (var g = Graphics.FromImage(baseImage))
        {
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, 40, 40, 200, 80);
        }

        var export = new ExportSettings
        {
            TargetWidth = 400,
            Dpi = 150,
            Format = ImageFormatKind.Png
        };
        using (var result = ExportEngine.Render(baseImage, Array.Empty<Annotation>(), new Rectangle(0, 0, 800, 450),
                   export, "", "yyyy-MM-dd", DateTime.Now))
        {
            Equal(result.PixelSize, new Size(400, 225), "export scales to target width");
            Equal(result.EffectiveDpi, 150, "export writes configured DPI");
            Check(result.Image.GetPixel(10, 10).R < 80, "scaled pixel retains dark base colour");
        }

        var word = new ExportSettings
        {
            TargetWidth = 1200,
            Dpi = 96,
            PrintWidthCm = 16,
            Format = ImageFormatKind.Png,
            AddTimestamp = true
        };
        using (var result = ExportEngine.Render(baseImage, Array.Empty<Annotation>(), new Rectangle(0, 0, 800, 450),
                   word, "TARGET-01", "yyyy-MM-dd", DateTime.Now))
        {
            Equal(result.PixelSize.Width, 1200, "Word preset target width");
            Equal(result.EffectiveDpi, 191, "print width derives DPI");
            Near((float)result.PrintWidthCm, 16f, 0.1f, "print width round trip");
            int whitePixels = 0;
            for (int y = result.Image.Height - 70; y < result.Image.Height; y += 2)
                for (int x = result.Image.Width - 420; x < result.Image.Width; x += 2)
                    if (result.Image.GetPixel(x, y).R > 200) whitePixels++;
            Check(whitePixels > 10, "timestamp stamp rendered");
        }

        using (var cropped = AnnotationRenderer.Compose(baseImage, Array.Empty<Annotation>(), new Rectangle(100, 50, 200, 100)))
        {
            Equal(cropped.Size, new Size(200, 100), "compose applies crop size");
        }

        var border = new ExportSettings { TargetWidth = 0, Dpi = 96, Format = ImageFormatKind.Png, AddBorder = true };
        using (var result = ExportEngine.Render(baseImage, Array.Empty<Annotation>(), new Rectangle(0, 0, 800, 450),
                   border, "", "yyyy-MM-dd", DateTime.Now))
        {
            var corner = result.Image.GetPixel(0, 1);
            Check(corner.R > 150 && corner.G > 150, "border rendered at image edge");
        }

        // Redaction
        var redact = Annotation.CreateRedaction(AnnotationKind.Redact, new RectangleF(50, 50, 80, 60), Color.Black, 1);
        using (var result = ExportEngine.Render(baseImage, new[] { redact }, new Rectangle(0, 0, 800, 450),
                   new ExportSettings { TargetWidth = 0, Dpi = 96, Format = ImageFormatKind.Png }, "", "yyyy-MM-dd", DateTime.Now))
        {
            var p = result.Image.GetPixel(90, 80);
            Check(p.R < 5 && p.G < 5 && p.B < 5, "solid redaction is opaque black");
        }

        // Pixelate: neighbouring pixels inside a block should become identical.
        using (var noise = CreateTestImage(200, 200, Color.Black))
        {
            using (var ng = Graphics.FromImage(noise))
            {
                for (int by = 0; by < 20; by++)
                    for (int bx = 0; bx < 20; bx++)
                        using (var cellBrush = new SolidBrush(Color.FromArgb(255, (bx * 37) % 256, (by * 53) % 256, ((bx + by) * 29) % 256)))
                            ng.FillRectangle(cellBrush, bx * 10, by * 10, 10, 10);
            }
            var pixel = Annotation.CreateRedaction(AnnotationKind.Pixelate, new RectangleF(0, 0, 120, 120), Color.Black, 10);
            using var result = ExportEngine.Render(noise, new[] { pixel }, new Rectangle(0, 0, 200, 200),
                new ExportSettings { TargetWidth = 0, Dpi = 96, Format = ImageFormatKind.Png }, "", "yyyy-MM-dd", DateTime.Now);
            var pixelA = result.Image.GetPixel(55, 55);
            var pixelB = result.Image.GetPixel(56, 55);
            Check(pixelA.ToArgb() == pixelB.ToArgb(), "pixelate produces uniform blocks");
        }

        // Blur: a hard black/white edge becomes a mid-tone.
        using (var edge = CreateTestImage(200, 200, Color.Black))
        {
            using (var g = Graphics.FromImage(edge))
                g.FillRectangle(Brushes.White, 100, 0, 100, 200);
            var blur = Annotation.CreateRedaction(AnnotationKind.Blur, new RectangleF(85, 40, 30, 120), Color.Black, 10);
            using var result = ExportEngine.Render(edge, new[] { blur }, new Rectangle(0, 0, 200, 200),
                new ExportSettings { TargetWidth = 0, Dpi = 96, Format = ImageFormatKind.Png }, "", "yyyy-MM-dd", DateTime.Now);
            var p = result.Image.GetPixel(100, 100);
            Check(p.R > 20 && p.R < 235, "blur softens a hard edge");
        }

        var jpeg = ExportEngine.Encode(baseImage, ImageFormatKind.Jpeg, 85);
        Check(jpeg.Length > 100, "JPEG encoder returns bytes");
        using (var ms = new MemoryStream(jpeg))
        using (var decoded = Image.FromStream(ms))
            Equal(decoded.Size, baseImage.Size, "JPEG round trip size");

        // Keep a rendered example for manual inspection.
        using (var result = ExportEngine.Render(baseImage, new[] { redact }, new Rectangle(0, 0, 800, 450),
                   word, "TARGET-01 / unit test", "yyyy-MM-dd HH:mm:ss", DateTime.Now))
        {
            var bytes = ExportEngine.Encode(result.Image, ImageFormatKind.Png, 92);
            File.WriteAllBytes(Path.Combine(artifacts, "unit-export.png"), bytes);
        }
    }

    private static Bitmap CreateTestImage(int width, int height, Color color)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    private static void TestScreenCapture()
    {
        var virtualBounds = MonitorLayout.VirtualBounds;
        Check(virtualBounds.Width > 0 && virtualBounds.Height > 0, "virtual desktop has size");
        Check(MonitorLayout.GetMonitors().Count >= 1, "at least one monitor detected");
        var monitor = MonitorLayout.GetMonitors().First();
        Check(monitor.Dpi >= 96, "monitor DPI detected");

        var rect = new Rectangle(virtualBounds.Left + 4, virtualBounds.Top + 4, 120, 80);
        using var frame = ScreenCapture.CaptureRect(rect, CaptureKind.Region);
        Equal(frame.Image.Size, new Size(120, 80), "direct capture size");
        Equal(frame.Metadata.ScreenBounds, rect, "direct capture metadata");

        using var frozen = ScreenCapture.FreezeDesktop();
        Equal(frozen.Image.Size, virtualBounds.Size, "frozen desktop matches virtual bounds");
        int localX = Math.Clamp(10, 0, frozen.Image.Width - 60);
        int localY = Math.Clamp(10, 0, frozen.Image.Height - 40);
        var selection = new Rectangle(frozen.Bounds.X + localX, frozen.Bounds.Y + localY, 50, 30);
        using var crop = ScreenCapture.CaptureRegionFromFrozen(frozen, selection);
        Equal(crop.Image.Size, new Size(50, 30), "frozen crop size");
        Equal(crop.Metadata.ScreenBounds, selection, "frozen crop metadata");
    }

    private static void TestClipboardDataObject()
    {
        using var bmp = CreateTestImage(80, 50, Color.FromArgb(10, 120, 220));
        var data = ClipboardHelper.CreateImageDataObject(bmp);
        Check(data.GetDataPresent(DataFormats.Bitmap), "clipboard data object contains CF_BITMAP");
        Check(data.GetDataPresent("PNG"), "clipboard data object contains PNG flavour");
        if (data.TryGetData<Stream>("PNG", out var pngStream) && pngStream != null)
        {
            var stream = pngStream;
            if (stream.CanSeek) stream.Position = 0;
            Span<byte> header = stackalloc byte[8];
            int read = stream.Read(header);
            Check(read == 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                "PNG clipboard flavour starts with the PNG signature");
        }
        else
        {
            Fail("PNG clipboard flavour is a readable stream");
        }
    }

    private static void TestSave(string artifacts)
    {
        using var bmp = CreateTestImage(800, 450, Color.FromArgb(25, 40, 60));
        var metadata = new CaptureMetadata
        {
            Kind = CaptureKind.Region,
            CapturedAt = new DateTime(2026, 11, 17, 12, 0, 0),
            SourceProcess = "unit",
            SourceTitle = "save test",
            ImageWidth = 800,
            ImageHeight = 450,
            ScreenBounds = new Rectangle(0, 0, 800, 450)
        };
        var settings = new AppSettings();
        settings.Normalize();
        var export = new ExportSettings
        {
            TargetWidth = 1200,
            PrintWidthCm = 16,
            Format = ImageFormatKind.Png,
            AddTimestamp = true,
            AddBorder = true
        };
        string path = Path.Combine(artifacts, "unit-save.png");
        if (File.Exists(path)) File.Delete(path);

        var outcome = CaptureOutput.Save(bmp, Array.Empty<Annotation>(), new Rectangle(0, 0, 800, 450),
            metadata, settings, export, path);
        Check(File.Exists(path), "save writes the file");
        Equal(outcome.PixelSize, new Size(1200, 675), "save applies target width");
        Equal(outcome.Dpi, 191, "save writes derived DPI");
        using (var loaded = new Bitmap(path))
        {
            Equal(loaded.Size, new Size(1200, 675), "saved PNG dimensions");
            Near(loaded.HorizontalResolution, 191f, 0.6f, "saved PNG DPI metadata");
        }

        using var loadedUnlocked = ImageLoader.LoadUnlocked(path);
        Equal(loadedUnlocked.Size, new Size(1200, 675), "ImageLoader opens a saved image");
        Equal(ImageFormatInfo.FromExtension("x.jpg"), ImageFormatKind.Jpeg, "format from jpg extension");
        Equal(ImageFormatInfo.FromExtension("x.bmp"), ImageFormatKind.Bmp, "format from bmp extension");
    }
}
