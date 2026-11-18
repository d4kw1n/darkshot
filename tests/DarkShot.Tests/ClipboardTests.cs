using System.Drawing;
using System.Windows.Forms;
using DarkShot.Core;

namespace DarkShot.Tests;

internal static class ClipboardTests
{
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        DataFormats.Text, DataFormats.UnicodeText, DataFormats.Rtf, DataFormats.Html,
        "Csv", DataFormats.Bitmap, DataFormats.Dib, DataFormats.FileDrop,
        "PNG", "image/png", "DeviceIndependentBitmap"
    };

    public static int Run()
    {
        Console.WriteLine("Clipboard test starting (backup/restore)");
        IDataObject? original = null;
        var streams = new List<MemoryStream>();
        var bitmaps = new List<Bitmap>();
        var backup = new DataObject();
        bool canRestore = true;

        try
        {
            original = Clipboard.GetDataObject();
            if (original != null)
            {
                var formats = original.GetFormats(false);
                foreach (var format in formats)
                {
                    if (!SupportedFormats.Contains(format))
                    {
                        Console.WriteLine($"Skipping clipboard test: unsupported format '{format}'");
                        canRestore = false;
                        break;
                    }

                    object? data;
                    try { data = original.GetData(format, false); }
                    catch { canRestore = false; break; }
                    if (data == null) continue;

                    if (data is Stream stream)
                    {
                        var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        ms.Position = 0;
                        streams.Add(ms);
                        backup.SetData(format, false, ms);
                    }
                    else if (data is Bitmap bmp)
                    {
                        var clone = new Bitmap(bmp);
                        bitmaps.Add(clone);
                        backup.SetData(format, false, clone);
                    }
                    else
                    {
                        backup.SetData(format, false, data);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Clipboard test skipped: " + ex.Message);
            canRestore = false;
        }

        if (!canRestore)
        {
            foreach (var stream in streams) stream.Dispose();
            foreach (var bitmap in bitmaps) bitmap.Dispose();
            return 0;
        }

        int failures = 0;
        try
        {
            using var testImage = new Bitmap(120, 80);
            using (var g = Graphics.FromImage(testImage))
            {
                g.Clear(Color.FromArgb(51, 102, 204));
                g.FillRectangle(Brushes.White, 10, 10, 40, 20);
            }

            bool copied = ClipboardHelper.CopyImage(testImage);
            Console.WriteLine("  " + (copied ? "PASS" : "FAIL") + "  clipboard copy did not throw");
            if (!copied) failures++;

            using var roundTrip = ClipboardHelper.TryGetImage();
            if (roundTrip == null)
            {
                Console.WriteLine("  FAIL  clipboard image could be read back");
                failures++;
            }
            else
            {
                bool sizeOk = roundTrip.Width == 120 && roundTrip.Height == 80;
                Console.WriteLine("  " + (sizeOk ? "PASS" : "FAIL") + $"  clipboard round-trip size ({roundTrip.Width}x{roundTrip.Height})");
                if (!sizeOk) failures++;
                var p = roundTrip.GetPixel(20, 20);
                bool colorOk = p.R > 240 && p.G > 240 && p.B > 240;
                Console.WriteLine("  " + (colorOk ? "PASS" : "FAIL") + $"  clipboard round-trip pixels ({p})");
                if (!colorOk) failures++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  FAIL  clipboard test threw: " + ex.Message);
            failures++;
        }
        finally
        {
            try
            {
                if (original == null)
                    Clipboard.Clear();
                else
                    Clipboard.SetDataObject(backup, true, 10, 60);
                Console.WriteLine("  INFO  previous clipboard content restored");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  WARN  could not restore previous clipboard: " + ex.Message);
            }
            foreach (var stream in streams) stream.Dispose();
            foreach (var bitmap in bitmaps) bitmap.Dispose();
        }

        Console.WriteLine(failures == 0 ? "CLIPBOARD TEST OK" : "CLIPBOARD TEST FAILED");
        return failures == 0 ? 0 : 1;
    }
}
