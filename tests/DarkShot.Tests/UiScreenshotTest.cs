using System.Drawing;
using System.Drawing.Drawing2D;
using DarkShot.Core;
using DarkShot.UI;
using DarkShot.UI.Editor;
using DarkShot.UI.Controls;
using System.Reflection;

namespace DarkShot.Tests;

internal static class UiScreenshotTest
{
    public static int Run(string artifacts)
    {
        Console.WriteLine("UI screenshot test starting");
        var settings = new AppSettings();
        settings.Normalize();
        settings.SaveFolder = Path.Combine(artifacts, "history-test");
        Directory.CreateDirectory(settings.SaveFolder);
        settings.FilenamePattern = "{date}_{time}_{type}";
        settings.Export.TargetWidth = 1200;
        settings.Export.PrintWidthCm = 16;
        settings.Export.AddTimestamp = true;
        settings.EvidenceLabel = "ACME / TARGET-01";
        settings.Annotation.Color = "#FF3B30";

        BuildHistorySamples(settings.SaveFolder);

        using (var form = new SettingsForm(settings))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, 0);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            for (int section = 0; section < 3; section++)
            {
                form.SelectSection(section);
                Application.DoEvents();
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                string path = Path.Combine(artifacts, $"settings-section-{section}.png");
                bmp.Save(path);
                Console.WriteLine("saved " + path);
            }
            var saveButton = Descendants(form).OfType<PillButton>()
                .FirstOrDefault(b => b.Text == "Save settings");
            if (saveButton != null)
            {
                Click(saveButton);
                Application.DoEvents();
                var result = form.Result;
                Console.WriteLine($"Settings submit: width={result.Export.TargetWidth} print={result.Export.PrintWidthCm:0.#}cm timestamp={result.Export.AddTimestamp} format={result.Export.Format} folder={result.SaveFolder}");
                Console.WriteLine(result.Export.TargetWidth == 1200 && result.Export.AddTimestamp && result.Export.PrintWidthCm > 15
                    ? "SETTINGS SAVE TEST OK"
                    : "SETTINGS SAVE TEST FAILED");
            }
            else
            {
                Console.WriteLine("SETTINGS SAVE TEST FAILED (button not found)");
            }
            if (!form.IsDisposed) form.Close();
        }

        using (var form = new HistoryForm(settings, _ => { }))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, 0);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            string path = Path.Combine(artifacts, "history-window.png");
            bmp.Save(path);
            Console.WriteLine("saved " + path);
            form.Close();
        }

        using (var exportDialog = new ExportOptionsDialog(settings.Export.Clone(), new Size(1200, 700)))
        {
            exportDialog.StartPosition = FormStartPosition.Manual;
            exportDialog.Location = new Point(-4000, 0);
            exportDialog.ShowInTaskbar = false;
            exportDialog.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(exportDialog.Width, exportDialog.Height);
            exportDialog.DrawToBitmap(bmp, new Rectangle(0, 0, exportDialog.Width, exportDialog.Height));
            string path = Path.Combine(artifacts, "export-dialog.png");
            bmp.Save(path);
            Console.WriteLine("saved " + path);
            exportDialog.Close();
        }

        using (var pinBitmap = new Bitmap(640, 400))
        {
            using (var g = Graphics.FromImage(pinBitmap))
            {
                g.Clear(Color.FromArgb(22, 26, 34));
                using var white = new SolidBrush(Color.White);
                using var accent = new SolidBrush(Color.FromArgb(62, 139, 255));
                g.FillRectangle(accent, 0, 0, 640, 54);
                g.DrawString("Pinned evidence", new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel), white, 28, 14);
                g.DrawString("Drag to move · wheel to zoom", new Font("Segoe UI", 15, GraphicsUnit.Pixel), white, 28, 110);
            }

            using var pin = new PinForm(pinBitmap, settings)
            {
                TopMost = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, 0)
            };
            pin.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(pin.Width, pin.Height);
            pin.DrawToBitmap(bmp, new Rectangle(0, 0, pin.Width, pin.Height));
            string path = Path.Combine(artifacts, "pin-window.png");
            bmp.Save(path);
            Console.WriteLine("saved " + path);
            pin.Close();
        }

        Console.WriteLine("UI SCREENSHOT TEST OK");
        return 0;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var grandChild in Descendants(child))
                yield return grandChild;
        }
    }

    private static void Click(Control control)
    {
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(control, new object[] { EventArgs.Empty });
    }

    private static void BuildHistorySamples(string folder)
    {
        var random = new Random(7);
        for (int i = 0; i < 6; i++)
        {
            using var bmp = new Bitmap(640, 400);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(18 + random.Next(0, 30), 24, 36));
            using var brush = new SolidBrush(Color.FromArgb(50 + i * 20, 120, 200));
            g.FillRectangle(brush, 30, 30, 580, 60);
            using var white = new SolidBrush(Color.White);
            g.DrawString($"PoC evidence sample {i + 1}", new Font("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel), white, 50, 45);
            g.DrawString("GET /api/v1/admin  →  200 OK", new Font("Consolas", 16, GraphicsUnit.Pixel), white, 50, 140);
            g.DrawString("CVSS 9.1 · authorization bypass", new Font("Consolas", 16, GraphicsUnit.Pixel), white, 50, 190);
            string path = Path.Combine(folder, $"sample-{i + 1}.png");
            bmp.Save(path);
            File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-i * 13));
        }
    }
}
