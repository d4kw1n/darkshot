using System.Drawing;
using System.Drawing.Drawing2D;
using DarkShot.Core;

namespace DarkShot.UI;

public sealed class PinForm : Form
{
    private readonly Bitmap _bitmap;
    private readonly AppSettings _settings;
    private float _zoom;
    private Point _dragStart;
    private bool _dragging;

    public PinForm(Bitmap bitmap, AppSettings settings)
    {
        _bitmap = bitmap;
        _settings = settings;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.SizeAll;

        var work = MonitorLayout.GetMonitorAt(Cursor.Position)?.WorkArea ?? Screen.PrimaryScreen!.WorkingArea;
        _zoom = Math.Min(1f, Math.Min((work.Width - 40f) / bitmap.Width, (work.Height - 40f) / bitmap.Height));
        _zoom = Math.Max(0.1f, _zoom);
        Size = new Size(Math.Max(32, (int)Math.Round(bitmap.Width * _zoom)), Math.Max(32, (int)Math.Round(bitmap.Height * _zoom)));

        var location = new Point(Cursor.Position.X + 18, Cursor.Position.Y + 18);
        location.X = Math.Clamp(location.X, work.Left + 8, work.Right - Width - 8);
        location.Y = Math.Clamp(location.Y, work.Top + 8, work.Bottom - Height - 8);
        Location = location;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NativeMethods.ApplyRoundedCorners(Handle);
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Background);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(_bitmap, new Rectangle(0, 0, Width, Height));
        using var border = new Pen(Color.FromArgb(150, 255, 255, 255), 1f);
        g.DrawRectangle(border, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragStart = e.Location;
            _dragging = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Location = new Point(Location.X + e.X - _dragStart.X, Location.Y + e.Y - _dragStart.Y);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float factor = (float)Math.Pow(1.12, e.Delta / 120.0);
        float oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * factor, 0.1f, 4f);
        if (Math.Abs(oldZoom - _zoom) < 0.001f) return;

        var work = MonitorLayout.GetMonitorAt(Location)?.WorkArea ?? Screen.PrimaryScreen!.WorkingArea;
        int newW = Math.Max(32, (int)Math.Round(_bitmap.Width * _zoom));
        int newH = Math.Max(32, (int)Math.Round(_bitmap.Height * _zoom));
        int newX = Location.X - (e.X - Width / 2) + newW / 2;
        int newY = Location.Y - (e.Y - Height / 2) + newH / 2;
        Location = new Point(
            Math.Clamp(newX, work.Left - newW + 40, work.Right - 40),
            Math.Clamp(newY, work.Top - newH + 40, work.Bottom - 40));
        Size = new Size(newW, newH);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left) return;
        _zoom = _zoom >= 0.99f ? 1f : 1f;
        Size = new Size(_bitmap.Width, _bitmap.Height);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        if (e.Button == MouseButtons.Right)
            ShowContextMenu(e.Location);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        if (keyData == (Keys.Control | Keys.C))
        {
            Copy();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ShowContextMenu(Point location)
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            RenderMode = ToolStripRenderMode.System
        };
        menu.Items.Add("Copy image", null, (_, _) => Copy());
        menu.Items.Add("Save image…", null, (_, _) => SaveImage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Actual size (100%)", null, (_, _) =>
        {
            _zoom = 1f;
            Size = new Size(_bitmap.Width, _bitmap.Height);
            Invalidate();
        });
        menu.Items.Add("Close", null, (_, _) => Close());
        menu.Show(this, location);
    }

    private void Copy()
    {
        ClipboardHelper.CopyImage(_bitmap);
    }

    private void SaveImage()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save pinned image",
            InitialDirectory = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|BMP image (*.bmp)|*.bmp",
            FileName = "pinned-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var format = ImageFormatInfo.FromExtension(dialog.FileName);
            var bytes = ExportEngine.Encode(_bitmap, format, _settings.Export.JpegQuality);
            File.WriteAllBytes(dialog.FileName, bytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Pinned save failed");
            MessageBox.Show(this, "Could not save image:\n\n" + ex.Message, "DarkShot",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _bitmap.Dispose();
        base.Dispose(disposing);
    }
}
