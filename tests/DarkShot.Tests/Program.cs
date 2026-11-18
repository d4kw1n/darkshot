using System.Drawing;
using System.Runtime.InteropServices;
using DarkShot.Core;
using DarkShot.UI;

namespace DarkShot.Tests;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [STAThread]
    private static int Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();

        var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts"));
        Directory.CreateDirectory(artifacts);

        if (args.Any(a => a.Equals("overlay", StringComparison.OrdinalIgnoreCase)))
            return RunOverlayTest(artifacts, auto: false);
        if (args.Any(a => a.Equals("overlay-auto", StringComparison.OrdinalIgnoreCase)))
            return RunOverlayTest(artifacts, auto: true);
        if (args.Any(a => a.Equals("editor", StringComparison.OrdinalIgnoreCase)))
            return EditorUiTest.Run(artifacts);
        if (args.Any(a => a.Equals("ui", StringComparison.OrdinalIgnoreCase)))
            return UiScreenshotTest.Run(artifacts);
        if (args.Any(a => a.Equals("tests", StringComparison.OrdinalIgnoreCase)))
            return CoreTests.Run(artifacts);
        if (args.Any(a => a.Equals("clipboard", StringComparison.OrdinalIgnoreCase)))
            return ClipboardTests.Run();

        return RunProbe(artifacts);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    private static IntPtr LP(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    private static int RunOverlayTest(string artifacts, bool auto)
    {
        Console.WriteLine("Overlay test starting");
        var settings = new AppSettings();
        settings.Normalize();
        settings.ShowMagnifier = true;
        settings.DetectWindows = true;

        var foreground = WindowEnumerator.GetForegroundWindowInfo();
        var frozen = ScreenCapture.FreezeDesktop(includeCursor: false);
        var form = new CaptureOverlayForm(frozen, settings, foreground);
        string output = Path.Combine(artifacts, "overlay-result.png");

        form.Completed += (_, result) =>
        {
            try
            {
                Console.WriteLine($"Overlay completed action={result.Action} bounds={result.ScreenBounds} source={result.SourceWindow?.Title}");
                if (result.Frame != null)
                {
                    result.Frame.Image.Save(output);
                    Console.WriteLine($"Saved {output} {result.Frame.Image.Width}x{result.Frame.Image.Height}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Overlay result error: " + ex);
            }
            finally
            {
                result.Dispose();
            }
        };

        var timer = new System.Windows.Forms.Timer { Interval = 10000 };
        timer.Tick += (_, _) =>
        {
            Console.WriteLine("Overlay test timeout");
            timer.Stop();
            form.Close();
        };
        timer.Start();

        form.FormClosed += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            Application.ExitThread();
        };

        if (auto)
        {
            var driver = new System.Windows.Forms.Timer { Interval = 900 };
            int step = 0;
            driver.Tick += (_, _) =>
            {
                try
                {
                    switch (step++)
                    {
                        case 0:
                            PostMessage(form.Handle, WM_MOUSEMOVE, (IntPtr)1, LP(600, 350));
                            PostMessage(form.Handle, WM_LBUTTONDOWN, (IntPtr)1, LP(600, 350));
                            for (int i = 1; i <= 12; i++)
                                PostMessage(form.Handle, WM_MOUSEMOVE, (IntPtr)1, LP(600 + 1000 * i / 12, 350 + 500 * i / 12));
                            PostMessage(form.Handle, WM_LBUTTONUP, IntPtr.Zero, LP(1600, 850));
                            break;
                        case 1:
                            using (var bmp = new Bitmap(3840, 1080))
                            {
                                using var g = Graphics.FromImage(bmp);
                                g.CopyFromScreen(-1920, 0, 0, 0, bmp.Size);
                                bmp.Save(Path.Combine(artifacts, "overlay-selection.png"));
                            }
                            Console.WriteLine("Saved overlay-selection.png");
                            PostMessage(form.Handle, WM_KEYDOWN, (IntPtr)Keys.Enter, IntPtr.Zero);
                            PostMessage(form.Handle, WM_KEYUP, (IntPtr)Keys.Enter, IntPtr.Zero);
                            break;
                        case 2:
                            driver.Stop();
                            driver.Dispose();
                            if (!form.IsDisposed) form.Close();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Overlay driver error: " + ex);
                    driver.Stop();
                    form.Close();
                }
            };
            driver.Start();
        }

        form.Show();
        Console.WriteLine($"Overlay shown bounds={form.Bounds} client={form.ClientSize}");
        Application.Run();
        return 0;
    }

    private static int RunProbe(string artifacts)
    {
        Console.WriteLine("== Monitor layout (PerMonitorV2 process) ==");
        foreach (var m in MonitorLayout.GetMonitors())
            Console.WriteLine($"{m.DeviceName} bounds={m.Bounds} work={m.WorkArea} dpi={m.Dpi} primary={m.IsPrimary}");
        Console.WriteLine($"VirtualBounds = {MonitorLayout.VirtualBounds}");
        Console.WriteLine($"Cursor = {Cursor.Position}");

        Console.WriteLine("== Capture test ==");
        using (var frozen = ScreenCapture.FreezeDesktop(includeCursor: false))
        {
            Console.WriteLine($"Frozen bounds={frozen.Bounds} image={frozen.Image.Width}x{frozen.Image.Height} format={frozen.Image.PixelFormat}");
            var sample1 = frozen.Image.GetPixel(frozen.Image.Width - 10, 10);
            frozen.Image.Save(Path.Combine(artifacts, "capture-virtual.png"));
            Console.WriteLine($"Saved capture-virtual.png; sample pixel={sample1}");

            var selLocal = new Rectangle(frozen.Image.Width - 400, 40, 320, 200);
            var selScreen = new Rectangle(frozen.Bounds.X + selLocal.X, frozen.Bounds.Y + selLocal.Y, selLocal.Width, selLocal.Height);
            using var frame = ScreenCapture.CaptureRegionFromFrozen(frozen, selScreen);
            Console.WriteLine($"Crop frame={frame.Image.Width}x{frame.Image.Height} meta={frame.Metadata.ScreenBounds}");
            frame.Image.Save(Path.Combine(artifacts, "capture-crop.png"));
        }

        Console.WriteLine("== Export test ==");
        using (var bmp = new Bitmap(800, 450))
        {
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.FromArgb(30, 34, 44));
            var export = new ExportSettings { TargetWidth = 1200, Dpi = 96, PrintWidthCm = 16, Format = ImageFormatKind.Png, AddTimestamp = true, AddBorder = true };
            using var rendered = ExportEngine.Render(bmp, Array.Empty<DarkShot.Annotations.Annotation>(), new Rectangle(0, 0, bmp.Width, bmp.Height), export, "TARGET-01", "yyyy-MM-dd HH:mm:ss", DateTime.Now);
            Console.WriteLine($"Rendered={rendered.PixelSize} dpi={rendered.EffectiveDpi} print={rendered.PrintWidthCm:F2}cm");
            var bytes = ExportEngine.Encode(rendered.Image, ImageFormatKind.Png, 92);
            File.WriteAllBytes(Path.Combine(artifacts, "export-test.png"), bytes);
            Console.WriteLine($"Saved export-test.png ({bytes.Length} bytes)");
        }

        Console.WriteLine("PROBE OK");
        return 0;
    }
}
