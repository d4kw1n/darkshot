using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DarkShot.Annotations;
using DarkShot.Core;

namespace DarkShot.UI;

public enum OverlayAction
{
    Cancel,
    Edit,
    Copy,
    Save,
    Pin
}

public sealed class CaptureOverlayResult : IDisposable
{
    public OverlayAction Action { get; init; }
    public CaptureFrame? Frame { get; init; }
    public Rectangle ScreenBounds { get; init; }
    public WindowInfo? SourceWindow { get; init; }
    public void Dispose() => Frame?.Dispose();
}

public sealed class CaptureOverlayForm : Form
{
    private enum DragMode { None, Creating, Moving, Resizing }

    private readonly FrozenDesktop _desktop;
    private readonly AppSettings _settings;
    private readonly WindowInfo? _fallbackSource;
    private readonly Rectangle _virtualBounds;
    private Bitmap? _dimmed;
    private bool _completed;
    private bool _handlingDpi;

    private Rectangle _selection;
    private Rectangle _initialSelection;
    private bool _hasSelection;
    private DragMode _dragMode;
    private int _dragHandle = -1;
    private Point _dragStart;
    private WindowInfo? _hoverWindow;
    private WindowInfo? _sourceWindow;
    private DateTime _lastHoverCheck = DateTime.MinValue;
    private Point _lastHoverPoint = new(int.MinValue, int.MinValue);
    private bool _magnifier;
    private Point _lastMouse;
    private bool _mouseInside;

    private readonly Rectangle[] _buttonRects = new Rectangle[5];
    private readonly OverlayAction[] _buttonActions =
    {
        OverlayAction.Edit, OverlayAction.Copy, OverlayAction.Save, OverlayAction.Pin, OverlayAction.Cancel
    };
    private readonly string[] _buttonLabels = { "Edit", "Copy", "Save", "Pin", "Cancel" };
    private readonly string[] _buttonHints =
    {
        "Edit  ·  Enter", "Copy  ·  Ctrl+C", "Save  ·  Ctrl+S", "Pin to screen  ·  Ctrl+P", "Cancel  ·  Esc"
    };
    private readonly IconKind[] _buttonIcons =
    {
        IconKind.Pen, IconKind.Copy, IconKind.Save, IconKind.Pin, IconKind.Close
    };
    private int _hoverButton = -1;
    private Rectangle _toolbarRect;

    public event EventHandler<CaptureOverlayResult>? Completed;

    public CaptureOverlayForm(FrozenDesktop desktop, AppSettings settings, WindowInfo? fallbackSource = null)
    {
        _desktop = desktop;
        _settings = settings;
        _fallbackSource = fallbackSource;
        _virtualBounds = desktop.Bounds;
        _magnifier = settings.ShowMagnifier;
        _lastMouse = Cursor.Position;

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        Bounds = _virtualBounds;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        Text = "DarkShot Capture";
        DoubleBuffered = true;

        BuildDimmed();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                _virtualBounds.X, _virtualBounds.Y, _virtualBounds.Width, _virtualBounds.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
            Focus();
        }
        catch (Exception ex)
        {
            Log.Warn("Overlay activation failed: " + ex.Message);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_completed)
        {
            e.Cancel = true;
            Complete(OverlayAction.Cancel);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_DPICHANGED)
        {
            if (!_handlingDpi)
            {
                _handlingDpi = true;
                try
                {
                    NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
                        _virtualBounds.X, _virtualBounds.Y, _virtualBounds.Width, _virtualBounds.Height,
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                }
                finally
                {
                    _handlingDpi = false;
                }
            }
            return;
        }

        base.WndProc(ref m);
    }

    private void BuildDimmed()
    {
        try
        {
            int w = _desktop.Image.Width;
            int h = _desktop.Image.Height;
            _dimmed = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(_dimmed);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_desktop.Image, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            using var shade = new SolidBrush(Color.FromArgb(126, 8, 9, 12));
            g.FillRectangle(shade, 0, 0, w, h);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not prepare dimmed overlay bitmap");
            _dimmed?.Dispose();
            _dimmed = null;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // All painting is done in OnPaint to avoid flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (_dimmed != null)
            g.DrawImageUnscaled(_dimmed, 0, 0);
        else
            g.DrawImageUnscaled(_desktop.Image, 0, 0);

        bool hasSelection = _hasSelection && _selection.Width > 0 && _selection.Height > 0;

        if (hasSelection)
        {
            DrawUndimmedRegion(g, _selection);
            DrawSelection(g, _selection, _dragMode != DragMode.Creating);
        }
        else if (_hoverWindow != null && _dragMode == DragMode.None)
        {
            var local = ScreenToLocal(_hoverWindow.Bounds);
            var clipped = Rectangle.Intersect(local, ClientRectangle);
            if (clipped.Width > 0 && clipped.Height > 0)
            {
                DrawUndimmedRegion(g, clipped);
                using var border = new Pen(Theme.Selection, 2f);
                g.DrawRectangle(border, clipped.X + 1, clipped.Y + 1, clipped.Width - 2, clipped.Height - 2);
                DrawWindowLabel(g, local, _hoverWindow);
            }
        }

        if (hasSelection)
        {
            DrawSizeLabel(g, _selection);
            if (_dragMode == DragMode.None || _dragMode == DragMode.Resizing || _dragMode == DragMode.Moving)
                DrawToolbar(g);
        }
        else
        {
            DrawHint(g);
        }

        if (_magnifier && _mouseInside && !IsPointInToolbar(_lastMouse) &&
            (!_hasSelection || _dragMode is DragMode.Creating or DragMode.Resizing))
            DrawMagnifier(g);

        if (_hoverButton >= 0 && _buttonRects.Any(r => r.Contains(_lastMouse)))
            DrawTooltip(g);
    }

    private void DrawUndimmedRegion(Graphics g, Rectangle rect)
    {
        var src = Rectangle.Intersect(rect, new Rectangle(0, 0, _desktop.Image.Width, _desktop.Image.Height));
        if (src.Width <= 0 || src.Height <= 0) return;
        g.DrawImage(_desktop.Image, src, src, GraphicsUnit.Pixel);
    }

    private void DrawSelection(Graphics g, Rectangle sel, bool showHandles)
    {
        using (var white = new Pen(Color.FromArgb(235, 255, 255, 255), 1f))
            g.DrawRectangle(white, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);
        using (var accent = new Pen(Theme.Selection, 2f))
            g.DrawRectangle(accent, sel.X + 1, sel.Y + 1, Math.Max(0, sel.Width - 2), Math.Max(0, sel.Height - 2));

        if (!showHandles) return;
        var handles = GeometryUtil.HandlePoints(sel);
        const int size = 9;
        foreach (var p in handles)
        {
            var r = new RectangleF(p.X - size / 2f, p.Y - size / 2f, size, size);
            using var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            g.FillRectangle(shadow, r.X + 1, r.Y + 1, r.Width, r.Height);
            using var fill = new SolidBrush(Color.White);
            g.FillRectangle(fill, r);
            using var pen = new Pen(Theme.Selection, 1.5f);
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
        }
    }

    private void DrawSizeLabel(Graphics g, Rectangle sel)
    {
        string text = $"{sel.Width} × {sel.Height}";
        var font = Theme.PixelFont(13, FontStyle.Bold);
        var size = g.MeasureString(text, font);
        float w = size.Width + 20;
        float h = 28;
        float x = sel.X;
        float y = sel.Y - h - 8;
        if (y < 6) y = sel.Bottom + 8;
        x = Math.Clamp(x, 6, ClientSize.Width - w - 6);
        y = Math.Clamp(y, 6, ClientSize.Height - h - 6);
        var rect = new RectangleF(x, y, w, h);
        FillRound(g, rect, 6f, Color.FromArgb(238, 22, 24, 29));
        using var border = new Pen(Color.FromArgb(160, Theme.Selection), 1f);
        using var path = GeometryUtil.RoundedRect(rect, 6f);
        g.DrawPath(border, path);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(Theme.Text);
        g.DrawString(text, font, brush, rect, fmt);
    }

    private void DrawWindowLabel(Graphics g, Rectangle windowRect, WindowInfo info)
    {
        string text = info.DisplayLabel;
        if (text.Length > 90) text = text[..90] + "…";
        var font = Theme.PixelFont(12, FontStyle.Regular);
        var size = g.MeasureString(text, font);
        float w = Math.Min(size.Width + 22, ClientSize.Width - 20);
        float h = 26;
        float x = Math.Clamp(windowRect.X + 4, 6, ClientSize.Width - w - 6);
        float y = windowRect.Y - h - 6;
        if (y < 6) y = windowRect.Y + 6;
        y = Math.Clamp(y, 6, ClientSize.Height - h - 6);
        var rect = new RectangleF(x, y, w, h);
        FillRound(g, rect, 6f, Color.FromArgb(238, 22, 24, 29));
        using var accent = new SolidBrush(Theme.Selection);
        g.FillRectangle(accent, rect.X + 1, rect.Y + 6, 3, rect.Height - 12);
        using var fmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var brush = new SolidBrush(Theme.Text);
        g.DrawString(text, font, brush, new RectangleF(rect.X + 8, rect.Y, rect.Width - 14, rect.Height), fmt);
    }

    private void DrawHint(Graphics g)
    {
        string text = "Drag to select  ·  Click a window to capture  ·  Esc / right-click to cancel";
        if (!_magnifier) text += "  ·  M: magnifier";
        var font = Theme.PixelFont(12);
        var size = g.MeasureString(text, font);
        float w = Math.Min(size.Width + 30, ClientSize.Width - 24);
        float h = 32;
        var monitor = MonitorLayout.GetMonitorAt(Cursor.Position);
        Rectangle area = monitor?.Bounds ?? _virtualBounds;
        var local = ScreenToLocal(area);
        float x = local.X + (local.Width - w) / 2f;
        float y = local.Y + 22;
        x = Math.Clamp(x, local.X + 8, local.X + local.Width - w - 8);
        var rect = new RectangleF(x, y, w, h);
        FillRound(g, rect, 8f, Color.FromArgb(225, 18, 20, 25));
        using var border = new Pen(Color.FromArgb(90, 255, 255, 255), 1f);
        using var path = GeometryUtil.RoundedRect(rect, 8f);
        g.DrawPath(border, path);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        using var brush = new SolidBrush(Theme.Text);
        g.DrawString(text, font, brush, new RectangleF(rect.X + 10, rect.Y, rect.Width - 20, rect.Height), fmt);
    }

    private void DrawToolbar(Graphics g)
    {
        const int btn = 40;
        const int gap = 4;
        const int pad = 6;
        int count = _buttonRects.Length;
        int barW = pad * 2 + count * btn + (count - 1) * gap;
        int barH = btn + pad * 2;

        int x = _selection.Right - barW;
        int y = _selection.Bottom + 12;
        if (y + barH > ClientSize.Height - 8)
            y = _selection.Top - barH - 12;
        if (y < 8)
            y = Math.Clamp(_selection.Bottom - barH - 12, 8, ClientSize.Height - barH - 8);
        x = Math.Clamp(x, 8, ClientSize.Width - barW - 8);
        y = Math.Clamp(y, 8, ClientSize.Height - barH - 8);

        _toolbarRect = new Rectangle(x, y, barW, barH);

        using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
        using (var shadowPath = GeometryUtil.RoundedRect(new RectangleF(x + 2, y + 4, barW, barH), 10f))
            g.FillPath(shadow, shadowPath);

        using (var bg = new SolidBrush(Color.FromArgb(244, 26, 28, 34)))
        using (var path = GeometryUtil.RoundedRect(_toolbarRect, 10f))
            g.FillPath(bg, path);
        using (var border = new Pen(Color.FromArgb(255, 54, 60, 73), 1f))
        using (var path = GeometryUtil.RoundedRect(_toolbarRect, 10f))
            g.DrawPath(border, path);

        for (int i = 0; i < count; i++)
        {
            var rect = new Rectangle(x + pad + i * (btn + gap), y + pad, btn, btn);
            _buttonRects[i] = rect;
            bool hover = _hoverButton == i;
            bool primary = i == 0;

            var bgColor = primary
                ? (hover ? Theme.AccentHover : Theme.Accent)
                : (hover ? Theme.SurfaceHover : Color.Transparent);
            if (bgColor.A > 0)
            {
                using var brush = new SolidBrush(bgColor);
                using var path = GeometryUtil.RoundedRect(rect, 8f);
                g.FillPath(brush, path);
            }

            var iconColor = primary ? Color.White : (hover ? Theme.Text : Theme.TextMuted);
            var iconRect = new RectangleF(rect.X + (rect.Width - 20) / 2f, rect.Y + (rect.Height - 20) / 2f, 20, 20);
            IconPainter.Draw(g, _buttonIcons[i], iconRect, iconColor, 1.9f, primary ? Theme.Accent : Theme.OverlayToolbar);
        }
    }

    private void DrawTooltip(Graphics g)
    {
        int i = _hoverButton;
        if (i < 0 || i >= _buttonHints.Length) return;
        string text = _buttonHints[i];
        var font = Theme.PixelFont(11.5f);
        var size = g.MeasureString(text, font);
        float w = size.Width + 18;
        float h = 25;
        var btnRect = _buttonRects[i];
        float x = btnRect.X + btnRect.Width / 2f - w / 2f;
        float y = _toolbarRect.Y - h - 7;
        x = Math.Clamp(x, 6, ClientSize.Width - w - 6);
        if (y < 4) y = _toolbarRect.Bottom + 7;
        var rect = new RectangleF(x, y, w, h);
        FillRound(g, rect, 6f, Color.FromArgb(246, 16, 17, 21));
        using var border = new Pen(Color.FromArgb(90, 255, 255, 255), 1f);
        using var path = GeometryUtil.RoundedRect(rect, 6f);
        g.DrawPath(border, path);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(Theme.Text);
        g.DrawString(text, font, brush, rect, fmt);
    }

    private void DrawMagnifier(Graphics g)
    {
        if (!_mouseInside) return;
        var p = _lastMouse;
        if (p.X < 0 || p.Y < 0 || p.X >= _desktop.Image.Width || p.Y >= _desktop.Image.Height) return;

        const int zoom = 8;
        const int srcW = 19;
        const int srcH = 13;
        int magW = srcW * zoom;
        int magH = srcH * zoom;
        int infoH = 38;
        int totalH = magH + infoH;

        float x = p.X + 28;
        float y = p.Y + 28;
        if (x + magW + 8 > ClientSize.Width) x = p.X - magW - 28;
        if (y + totalH + 8 > ClientSize.Height) y = p.Y - totalH - 28;
        x = Math.Clamp(x, 8, ClientSize.Width - magW - 8);
        y = Math.Clamp(y, 8, ClientSize.Height - totalH - 8);

        int sx = Math.Clamp(p.X - srcW / 2, 0, Math.Max(0, _desktop.Image.Width - srcW));
        int sy = Math.Clamp(p.Y - srcH / 2, 0, Math.Max(0, _desktop.Image.Height - srcH));
        var source = new Rectangle(sx, sy, Math.Min(srcW, _desktop.Image.Width - sx), Math.Min(srcH, _desktop.Image.Height - sy));

        var outer = new RectangleF(x, y, magW, totalH);
        using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
        using (var sp = GeometryUtil.RoundedRect(new RectangleF(x + 2, y + 4, magW, totalH), 9f))
            g.FillPath(shadow, sp);

        var oldInterp = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_desktop.Image, new Rectangle((int)x, (int)y, magW, magH), source, GraphicsUnit.Pixel);
        g.InterpolationMode = oldInterp;
        g.PixelOffsetMode = oldOffset;

        // Crosshair
        var cursorInMag = new PointF(x + (p.X - sx) * zoom, y + (p.Y - sy) * zoom);
        using (var cross = new Pen(Color.FromArgb(230, 255, 255, 255), 1f))
        {
            g.DrawLine(cross, cursorInMag.X, y, cursorInMag.X, y + magH);
            g.DrawLine(cross, x, cursorInMag.Y, x + magW, cursorInMag.Y);
        }
        using (var center = new Pen(Theme.Selection, 2f))
            g.DrawRectangle(center, cursorInMag.X - zoom / 2f, cursorInMag.Y - zoom / 2f, zoom, zoom);

        // Info panel
        var infoRect = new RectangleF(x, y + magH, magW, infoH);
        using (var bg = new SolidBrush(Color.FromArgb(246, 20, 22, 27)))
            g.FillRectangle(bg, infoRect);
        using (var border = new Pen(Color.FromArgb(180, Theme.Selection), 1f))
            g.DrawRectangle(border, x, y, magW - 1, totalH - 1);

        int screenX = p.X + _virtualBounds.X;
        int screenY = p.Y + _virtualBounds.Y;
        Color pixel;
        try { pixel = _desktop.Image.GetPixel(p.X, p.Y); }
        catch { pixel = Color.Black; }
        string hex = $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}";

        var small = Theme.PixelFont(10.5f);
        var bold = Theme.PixelFont(11.5f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Theme.Text);
        using var muted = new SolidBrush(Theme.TextMuted);
        g.DrawString($"{screenX}, {screenY}", bold, textBrush, x + 8, y + magH + 5);
        g.DrawString($"{pixel.R}, {pixel.G}, {pixel.B}", small, muted, x + 8, y + magH + 20);
        using var swatch = new SolidBrush(pixel);
        g.FillRectangle(swatch, x + magW - 26, y + magH + 16, 14, 14);
        using var swatchBorder = new Pen(Color.FromArgb(130, 255, 255, 255), 1f);
        g.DrawRectangle(swatchBorder, x + magW - 26, y + magH + 16, 14, 14);
        g.DrawString(hex, small, textBrush, x + magW - 96, y + magH + 19);
    }

    private void FillRound(Graphics g, RectangleF rect, float radius, Color color)
    {
        using var brush = new SolidBrush(color);
        using var path = GeometryUtil.RoundedRect(rect, radius);
        g.FillPath(brush, path);
    }

    private Rectangle ScreenToLocal(Rectangle screenRect)
    {
        int x = screenRect.X - _virtualBounds.X;
        int y = screenRect.Y - _virtualBounds.Y;
        // Overlay coordinates follow the client area; with mixed DPI the client
        // origin is the top-left of the virtual desktop bitmap.
        return new Rectangle(x, y, screenRect.Width, screenRect.Height);
    }

    private Point ScreenToLocal(Point screenPoint)
        => new(screenPoint.X - _virtualBounds.X, screenPoint.Y - _virtualBounds.Y);

    private Point LocalToScreen(Point localPoint)
        => new(localPoint.X + _virtualBounds.X, localPoint.Y + _virtualBounds.Y);

    private bool IsPointInToolbar(Point p)
        => _toolbarRect.Contains(p) && (_hasSelection || _hoverButton >= 0);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _lastMouse = e.Location;
        _mouseInside = true;

        if (e.Button == MouseButtons.Right)
        {
            Complete(OverlayAction.Cancel);
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (_hasSelection && _toolbarRect.Contains(e.Location))
        {
            for (int i = 0; i < _buttonRects.Length; i++)
            {
                if (_buttonRects[i].Contains(e.Location))
                {
                    Complete(_buttonActions[i]);
                    return;
                }
            }
            return;
        }

        if (_hasSelection)
        {
            int handle = HitHandle(e.Location);
            if (handle >= 0)
            {
                _dragMode = DragMode.Resizing;
                _dragHandle = handle;
                _dragStart = e.Location;
                _initialSelection = _selection;
                Capture = true;
                UpdateCursor(e.Location);
                return;
            }

            if (_selection.Contains(e.Location))
            {
                _dragMode = DragMode.Moving;
                _dragStart = e.Location;
                _initialSelection = _selection;
                Capture = true;
                UpdateCursor(e.Location);
                return;
            }
        }

        _dragMode = DragMode.Creating;
        _dragStart = e.Location;
        _hasSelection = false;
        _selection = Rectangle.Empty;
        _hoverButton = -1;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _lastMouse = e.Location;
        _mouseInside = true;

        if (_dragMode == DragMode.Creating)
        {
            var rect = Normalize(_dragStart, e.Location);
            rect = Rectangle.Intersect(rect, ClientRectangle);
            _selection = rect;
            _hasSelection = rect.Width >= 2 && rect.Height >= 2;
            Invalidate();
            return;
        }

        if (_dragMode == DragMode.Moving)
        {
            int dx = e.Location.X - _dragStart.X;
            int dy = e.Location.Y - _dragStart.Y;
            var moved = new Rectangle(_initialSelection.X + dx, _initialSelection.Y + dy,
                _initialSelection.Width, _initialSelection.Height);
            moved.X = Math.Clamp(moved.X, 0, Math.Max(0, ClientSize.Width - moved.Width));
            moved.Y = Math.Clamp(moved.Y, 0, Math.Max(0, ClientSize.Height - moved.Height));
            _selection = moved;
            _hasSelection = true;
            Invalidate();
            return;
        }

        if (_dragMode == DragMode.Resizing)
        {
            var resized = GeometryUtil.ResizeFromHandle(_initialSelection, _dragHandle, e.Location, 2f);
            resized = RectangleF.Intersect(resized, ClientRectangle);
            _selection = Rectangle.Round(resized);
            _hasSelection = _selection.Width >= 2 && _selection.Height >= 2;
            Invalidate();
            return;
        }

        UpdateHoverWindow(e.Location, force: false);
        UpdateToolbarHover(e.Location);
        UpdateCursor(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;

        if (_dragMode == DragMode.Creating)
        {
            if (_hasSelection && _selection.Width >= 3 && _selection.Height >= 3)
            {
                _sourceWindow = DetectSourceForSelection(_selection);
            }
            else if (_hoverWindow != null)
            {
                SelectWindow(_hoverWindow);
            }
            else
            {
                _hasSelection = false;
                _selection = Rectangle.Empty;
            }
        }
        else if (_hasSelection)
        {
            _sourceWindow = DetectSourceForSelection(_selection);
        }

        _dragMode = DragMode.None;
        _dragHandle = -1;
        Capture = false;
        UpdateToolbarHover(e.Location);
        UpdateHoverWindow(e.Location, force: true);
        UpdateCursor(e.Location);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && _hasSelection && _selection.Contains(e.Location) && !_toolbarRect.Contains(e.Location))
            Complete(OverlayAction.Edit);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // Deliberately ignored; the overlay is pixel-accurate and does not zoom.
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInside = false;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Complete(OverlayAction.Cancel);
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.A)
        {
            if (e.Shift)
            {
                var m = MonitorLayout.GetMonitorAt(Cursor.Position);
                if (m != null) _selection = ScreenToLocal(m.Bounds);
            }
            else
            {
                _selection = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            }
            _selection = Rectangle.Intersect(_selection, ClientRectangle);
            _hasSelection = _selection.Width > 2 && _selection.Height > 2;
            _sourceWindow = DetectSourceForSelection(_selection);
            UpdateToolbarHover(_lastMouse);
            Invalidate();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.M)
        {
            _magnifier = !_magnifier;
            Invalidate();
            e.Handled = true;
            return;
        }

        if (!_hasSelection) return;

        if (e.KeyCode == Keys.Enter)
        {
            Complete(OverlayAction.Edit);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.C)
        {
            Complete(OverlayAction.Copy);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            Complete(OverlayAction.Save);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.P)
        {
            Complete(OverlayAction.Pin);
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            int step = e.Shift ? 10 : 1;
            var moved = _selection;
            switch (e.KeyCode)
            {
                case Keys.Left: moved.X -= step; break;
                case Keys.Right: moved.X += step; break;
                case Keys.Up: moved.Y -= step; break;
                case Keys.Down: moved.Y += step; break;
            }
            moved.X = Math.Clamp(moved.X, 0, Math.Max(0, ClientSize.Width - moved.Width));
            moved.Y = Math.Clamp(moved.Y, 0, Math.Max(0, ClientSize.Height - moved.Height));
            _selection = moved;
            Invalidate();
            e.Handled = true;
        }
    }

    private void UpdateHoverWindow(Point local, bool force)
    {
        if (!_settings.DetectWindows || _hasSelection || _dragMode != DragMode.None)
        {
            if (_hoverWindow != null && !_hasSelection)
            {
                _hoverWindow = null;
                Invalidate();
            }
            return;
        }

        var now = DateTime.UtcNow;
        if (!force && (now - _lastHoverCheck).TotalMilliseconds < 45 && Math.Abs(local.X - _lastHoverPoint.X) < 2 && Math.Abs(local.Y - _lastHoverPoint.Y) < 2)
            return;

        _lastHoverCheck = now;
        _lastHoverPoint = local;
        var screen = LocalToScreen(local);
        WindowInfo? info = null;
        try { info = WindowEnumerator.GetTopWindowAt(screen, Environment.ProcessId); }
        catch (Exception ex) { Log.Warn("Window detection failed: " + ex.Message); }

        if (!EqualsWindow(info, _hoverWindow))
        {
            _hoverWindow = info;
            Invalidate();
        }
    }

    private static bool EqualsWindow(WindowInfo? a, WindowInfo? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        return a.Handle == b.Handle && a.Bounds == b.Bounds && a.Title == b.Title;
    }

    private void UpdateToolbarHover(Point local)
    {
        int old = _hoverButton;
        _hoverButton = -1;
        if (_hasSelection && _toolbarRect.Contains(local))
        {
            for (int i = 0; i < _buttonRects.Length; i++)
            {
                if (_buttonRects[i].Contains(local)) { _hoverButton = i; break; }
            }
        }
        if (old != _hoverButton) Invalidate();
    }

    private void UpdateCursor(Point local)
    {
        if (_hoverButton >= 0)
        {
            Cursor = Cursors.Hand;
            return;
        }

        if (_hasSelection && _dragMode == DragMode.None)
        {
            int handle = HitHandle(local);
            Cursor = handle switch
            {
                0 or 4 => Cursors.SizeNWSE,
                2 or 6 => Cursors.SizeNESW,
                1 or 5 => Cursors.SizeNS,
                3 or 7 => Cursors.SizeWE,
                _ => _selection.Contains(local) ? Cursors.SizeAll : Cursors.Cross
            };
            return;
        }

        if (_dragMode == DragMode.Moving) Cursor = Cursors.SizeAll;
        else if (_dragMode == DragMode.Resizing)
        {
            Cursor = _dragHandle switch
            {
                0 or 4 => Cursors.SizeNWSE,
                2 or 6 => Cursors.SizeNESW,
                1 or 5 => Cursors.SizeNS,
                _ => Cursors.SizeWE
            };
        }
        else Cursor = Cursors.Cross;
    }

    private int HitHandle(Point p)
    {
        if (!_hasSelection) return -1;
        var handles = GeometryUtil.HandlePoints(_selection);
        const float tolerance = 7f;
        for (int i = 0; i < handles.Length; i++)
        {
            if (Math.Abs(p.X - handles[i].X) <= tolerance && Math.Abs(p.Y - handles[i].Y) <= tolerance)
                return i;
        }
        return -1;
    }

    private WindowInfo? DetectSourceForSelection(Rectangle selection)
    {
        try
        {
            var center = LocalToScreen(new Point(selection.X + selection.Width / 2, selection.Y + selection.Height / 2));
            return WindowEnumerator.GetTopWindowAt(center, Environment.ProcessId) ?? _fallbackSource ?? _sourceWindow;
        }
        catch
        {
            return _fallbackSource;
        }
    }

    private void SelectWindow(WindowInfo window)
    {
        _selection = Rectangle.Intersect(ScreenToLocal(window.Bounds), ClientRectangle);
        _hasSelection = _selection.Width > 2 && _selection.Height > 2;
        _sourceWindow = window;
        _hoverWindow = null;
        UpdateToolbarHover(_lastMouse);
        Invalidate();
    }

    private static Rectangle Normalize(Point a, Point b)
    {
        int x = Math.Min(a.X, b.X);
        int y = Math.Min(a.Y, b.Y);
        return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private void Complete(OverlayAction action)
    {
        if (_completed) return;
        _completed = true;

        Rectangle screenRect = Rectangle.Empty;
        CaptureFrame? frame = null;
        try
        {
            if (action != OverlayAction.Cancel && _hasSelection && _selection.Width > 0 && _selection.Height > 0)
            {
                screenRect = new Rectangle(_virtualBounds.X + _selection.X, _virtualBounds.Y + _selection.Y,
                    _selection.Width, _selection.Height);
                var source = _sourceWindow ?? _fallbackSource;
                frame = ScreenCapture.CaptureRegionFromFrozen(_desktop, screenRect,
                    source?.ProcessName, source?.Title, source?.Handle ?? IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not crop capture overlay selection");
            action = OverlayAction.Cancel;
        }

        var result = new CaptureOverlayResult
        {
            Action = action,
            Frame = frame,
            ScreenBounds = screenRect,
            SourceWindow = _sourceWindow
        };

        Hide();
        try
        {
            var handler = Completed;
            if (handler != null)
                handler(this, result);
            else
                result.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Overlay completion handler failed");
            result.Dispose();
        }
        finally
        {
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed?.Dispose();
            _dimmed = null;
            _desktop.Dispose();
        }
        base.Dispose(disposing);
    }
}
