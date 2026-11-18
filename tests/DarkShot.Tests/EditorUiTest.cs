using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using DarkShot.Core;
using DarkShot.UI;
using DarkShot.UI.Editor;

namespace DarkShot.Tests;

internal static class EditorUiTest
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;

    private static IntPtr LP(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    public static int Run(string artifacts)
    {
        Console.WriteLine("Editor test starting");
        var captures = Path.Combine(artifacts, "test-captures");
        Directory.CreateDirectory(captures);

        var image = BuildMockImage();
        var settings = new AppSettings();
        settings.Normalize();
        settings.SaveFolder = captures;
        settings.FilenamePattern = "editor-test_{yyyyMMdd-HHmmss}";
        settings.Export = new ExportSettings
        {
            TargetWidth = 1200,
            PrintWidthCm = 16,
            Format = ImageFormatKind.Png,
            AddTimestamp = true,
            AddBorder = true,
            JpegQuality = 92
        };
        settings.EvidenceLabel = "ACME / TARGET-01";
        settings.Annotation.Color = "#FF3B30";
        settings.Annotation.HighlighterColor = "#FFE600";

        var metadata = new CaptureMetadata
        {
            Kind = CaptureKind.Region,
            CapturedAt = DateTime.Now,
            SourceProcess = "test",
            SourceTitle = "Mock admin console",
            ScreenBounds = new Rectangle(0, 0, 1200, 700),
            ImageWidth = 1200,
            ImageHeight = 700
        };
        var frame = new CaptureFrame(image, metadata);
        var form = new EditorForm(frame, settings) { TopMost = true };
        var canvas = form.Canvas;

        var driver = new System.Windows.Forms.Timer { Interval = 420 };
        int step = 0;
        bool saved = false;
        string savedPath = "";

        Point P(float x, float y)
        {
            var p = canvas.ImageToScreen(new PointF(x, y));
            return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }

        void Drag(float x1, float y1, float x2, float y2)
        {
            var a = P(x1, y1);
            var b = P(x2, y2);
            PostMessage(canvas.Handle, WM_MOUSEMOVE, IntPtr.Zero, LP(a.X, a.Y));
            PostMessage(canvas.Handle, WM_LBUTTONDOWN, (IntPtr)1, LP(a.X, a.Y));
            for (int i = 1; i <= 10; i++)
                PostMessage(canvas.Handle, WM_MOUSEMOVE, (IntPtr)1,
                    LP(a.X + (b.X - a.X) * i / 10, a.Y + (b.Y - a.Y) * i / 10));
            PostMessage(canvas.Handle, WM_LBUTTONUP, IntPtr.Zero, LP(b.X, b.Y));
        }

        void Click(float x, float y)
        {
            var p = P(x, y);
            PostMessage(canvas.Handle, WM_MOUSEMOVE, IntPtr.Zero, LP(p.X, p.Y));
            PostMessage(canvas.Handle, WM_LBUTTONDOWN, (IntPtr)1, LP(p.X, p.Y));
            PostMessage(canvas.Handle, WM_LBUTTONUP, IntPtr.Zero, LP(p.X, p.Y));
        }

        driver.Tick += (_, _) =>
        {
            try
            {
                switch (step++)
                {
                    case 0:
                        canvas.SetTool(EditorTool.Arrow);
                        canvas.SetActiveColor(Color.FromArgb(255, 59, 48));
                        canvas.SetActiveStrokeWidth(5);
                        Drag(1040, 540, 860, 420);
                        break;
                    case 1:
                        canvas.SetTool(EditorTool.Rectangle);
                        canvas.SetActiveColor(Color.FromArgb(10, 132, 255));
                        canvas.SetActiveStrokeWidth(3);
                        Drag(70, 130, 560, 300);
                        break;
                    case 2:
                        canvas.SetTool(EditorTool.Step);
                        canvas.SetActiveColor(Color.FromArgb(255, 59, 48));
                        Click(90, 120);
                        Click(600, 360);
                        Click(800, 180);
                        break;
                    case 3:
                        canvas.SetTool(EditorTool.Pixelate);
                        canvas.SetPixelStrength(14);
                        Drag(620, 390, 980, 432);
                        break;
                    case 4:
                        canvas.SetTool(EditorTool.Blur);
                        canvas.SetBlurRadius(20);
                        Drag(150, 288, 500, 330);
                        break;
                    case 5:
                        canvas.SetTool(EditorTool.Redact);
                        Drag(620, 470, 980, 505);
                        break;
                    case 6:
                        canvas.SetTool(EditorTool.Text);
                        canvas.SetActiveColor(Color.FromArgb(255, 210, 0));
                        canvas.SetFontSize(22);
                        Click(90, 560);
                        break;
                    case 7:
                    {
                        var box = canvas.Controls.Cast<Control>()
                            .SelectMany(c => c.Controls.Cast<Control>())
                            .OfType<TextBox>()
                            .FirstOrDefault();
                        if (box != null)
                        {
                            box.Text = "Proof: admin@target.com / token leaked";
                            canvas.CommitPendingEdits();
                            Console.WriteLine("Text annotation committed");
                        }
                        else
                        {
                            Console.WriteLine("Text editor textbox not found");
                        }
                        break;
                    }
                    case 8:
                    {
                        form.Activate();
                        Application.DoEvents();
                        using var bmp = new Bitmap(form.Width, form.Height);
                        using var g = Graphics.FromImage(bmp);
                        g.CopyFromScreen(form.Left, form.Top, 0, 0, form.Size);
                        bmp.Save(Path.Combine(artifacts, "editor-screen.png"));
                        Console.WriteLine($"Editor screenshot saved ({form.Width}x{form.Height}), annotations={canvas.AnnotationsView.Count}, crop={canvas.CropRect}");
                        break;
                    }
                    case 9:
                    {
                        int before = canvas.AnnotationsView.Count;
                        canvas.Undo();
                        int afterUndo = canvas.AnnotationsView.Count;
                        canvas.Redo();
                        int afterRedo = canvas.AnnotationsView.Count;
                        Console.WriteLine($"Undo/redo: {before} -> {afterUndo} -> {afterRedo}");
                        if (!(afterUndo == before - 1 && afterRedo == before))
                            Console.WriteLine("UNDO/REDO TEST FAILED");

                        canvas.SetTool(EditorTool.Crop);
                        Drag(40, 40, 1160, 660);
                        break;
                    }
                    case 10:
                    {
                        Console.WriteLine($"Crop applied: cropped={canvas.IsCropped} crop={canvas.CropRect}");
                        if (!canvas.IsCropped) Console.WriteLine("CROP TEST FAILED");
                        canvas.Undo();
                        Console.WriteLine($"Crop undo: cropped={canvas.IsCropped} crop={canvas.CropRect}");
                        if (canvas.IsCropped) Console.WriteLine("CROP UNDO TEST FAILED");
                        saved = form.QuickSave();
                        savedPath = form.LastSavedPath ?? "";
                        Console.WriteLine($"QuickSave={saved} path={savedPath}");
                        break;
                    }
                    case 11:
                        driver.Stop();
                        driver.Dispose();
                        form.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Editor driver error: " + ex);
                try { driver.Stop(); } catch { /* ignore */ }
                form.Close();
            }
        };

        form.FormClosed += (_, _) =>
        {
            try { driver.Stop(); driver.Dispose(); } catch { /* ignore */ }
            Application.ExitThread();
        };

        form.Show();
        form.Activate();
        driver.Start();
        Application.Run();

        if (saved && File.Exists(savedPath))
        {
            using var outBmp = new Bitmap(savedPath);
            Console.WriteLine($"EDITOR TEST OK output={savedPath} size={outBmp.Width}x{outBmp.Height} dpi={outBmp.HorizontalResolution:F1}");
            return 0;
        }

        Console.WriteLine("EDITOR TEST FAILED (no output)");
        return 1;
    }

    private static Bitmap BuildMockImage()
    {
        var bmp = new Bitmap(1200, 700, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(16, 20, 28));

        using (var header = new SolidBrush(Color.FromArgb(26, 32, 44)))
            g.FillRectangle(header, 0, 0, 1200, 64);
        using (var accent = new SolidBrush(Color.FromArgb(62, 139, 255)))
            g.FillRectangle(accent, 0, 62, 1200, 2);
        using (var white = new SolidBrush(Color.White))
        using (var muted = new SolidBrush(Color.FromArgb(150, 160, 178)))
        using (var red = new SolidBrush(Color.FromArgb(255, 70, 60)))
        {
            g.DrawString("ACME ADMIN CONSOLE", new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel), white, 28, 20);
            g.DrawString("https://admin.acme-target.test/users", new Font("Consolas", 13, GraphicsUnit.Pixel), muted, 340, 26);
            g.DrawString("● PROD", new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel), red, 1080, 24);
        }

        using (var panel = new SolidBrush(Color.FromArgb(30, 37, 50)))
            g.FillRectangle(panel, 40, 100, 560, 480);
        using (var white = new SolidBrush(Color.White))
        using (var muted = new SolidBrush(Color.FromArgb(150, 160, 178)))
        using (var field = new SolidBrush(Color.FromArgb(22, 27, 38)))
        {
            g.DrawString("User profile", new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel), white, 70, 120);
            g.DrawString("Username", new Font("Segoe UI", 12, GraphicsUnit.Pixel), muted, 70, 190);
            g.FillRectangle(field, 70, 210, 470, 34);
            g.DrawString("analyst@example.com", new Font("Segoe UI", 14, GraphicsUnit.Pixel), white, 82, 218);
            g.DrawString("Email", new Font("Segoe UI", 12, GraphicsUnit.Pixel), muted, 70, 260);
            g.FillRectangle(field, 70, 280, 470, 34);
            g.DrawString("analyst@example.com", new Font("Segoe UI", 14, GraphicsUnit.Pixel), white, 82, 288);
            g.DrawString("Role", new Font("Segoe UI", 12, GraphicsUnit.Pixel), muted, 70, 330);
            g.FillRectangle(field, 70, 350, 470, 34);
            g.DrawString("super_admin (elevated)", new Font("Segoe UI", 14, GraphicsUnit.Pixel), white, 82, 358);
            g.DrawString("API token", new Font("Segoe UI", 12, GraphicsUnit.Pixel), muted, 70, 400);
            g.FillRectangle(field, 70, 420, 470, 34);
            g.DrawString("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...", new Font("Consolas", 12, GraphicsUnit.Pixel), white, 82, 428);
            g.DrawString("Session cookie", new Font("Segoe UI", 12, GraphicsUnit.Pixel), muted, 70, 470);
            g.FillRectangle(field, 70, 490, 470, 34);
            g.DrawString("session=8f14e45fceea167a5a36dedd4bea2543", new Font("Consolas", 12, GraphicsUnit.Pixel), white, 82, 498);
        }

        using (var panel = new SolidBrush(Color.FromArgb(30, 37, 50)))
            g.FillRectangle(panel, 620, 100, 540, 480);
        using (var white = new SolidBrush(Color.White))
        using (var muted = new SolidBrush(Color.FromArgb(150, 160, 178)))
        using (var green = new SolidBrush(Color.FromArgb(52, 199, 89)))
        {
            g.DrawString("Access log", new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel), white, 650, 120);
            string[] rows =
            {
                "GET /api/v1/users ........ 200  12ms",
                "POST /api/v1/login ....... 200  31ms",
                "GET /api/v1/admin ........ 200  18ms",
                "GET /api/v1/tokens ....... 200  9ms",
                "GET /api/v1/audit ........ 200  22ms",
                "POST /api/v1/export ...... 201  47ms"
            };
            for (int i = 0; i < rows.Length; i++)
                g.DrawString(rows[i], new Font("Consolas", 13, GraphicsUnit.Pixel), i == 2 ? green : muted, 650, 175 + i * 38);
            g.DrawString("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9", new Font("Consolas", 12, GraphicsUnit.Pixel), white, 650, 415);
            g.DrawString("Set-Cookie: session=8f14e45fceea167a5a36dedd4bea2543", new Font("Consolas", 12, GraphicsUnit.Pixel), white, 650, 480);
        }

        using (var footer = new SolidBrush(Color.FromArgb(120, 130, 148)))
            g.DrawString("Proof of concept — internal use only", new Font("Segoe UI", 11, GraphicsUnit.Pixel), footer, 40, 665);
        return bmp;
    }
}
