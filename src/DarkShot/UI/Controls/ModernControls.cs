using System.Drawing;
using System.Drawing.Drawing2D;

namespace DarkShot.UI.Controls;

public sealed class IconButton : Control
{
    private bool _hover;
    private bool _pressed;

    public IconKind Icon { get; set; } = IconKind.Cursor;
    public bool Checked { get; set; }
    public bool Accent { get; set; }
    public bool DangerHover { get; set; }
    public Color BaseColor { get; set; } = Theme.Surface;
    public float IconSize { get; set; } = 20f;
    public float CornerRadius { get; set; } = 8f;

    public IconButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(36, 36);
        Cursor = Cursors.Hand;
        TabStop = false;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(BaseColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        Color bg = Color.Empty;
        Color icon = Enabled ? Theme.TextMuted : Theme.WithAlpha(Theme.TextMuted, 70);

        if (!Enabled)
        {
            bg = Color.Empty;
        }
        else if (DangerHover && _hover)
        {
            bg = Theme.CloseHover;
            icon = Color.White;
        }
        else if (Accent)
        {
            bg = _pressed ? Theme.AccentHover : _hover ? Theme.AccentHover : Theme.Accent;
            icon = Color.White;
        }
        else if (Checked)
        {
            bg = _pressed ? Theme.AccentHover : Theme.Accent;
            icon = Color.White;
        }
        else if (_pressed)
        {
            bg = Theme.SurfacePressed;
            icon = Theme.Text;
        }
        else if (_hover)
        {
            bg = Theme.SurfaceHover;
            icon = Theme.Text;
        }

        if (bg.A > 0)
        {
            using var brush = new SolidBrush(bg);
            using var path = Rounded(ClientRectangle, CornerRadius);
            g.FillPath(brush, path);
        }

        float size = Math.Min(IconSize, Math.Min(ClientSize.Width, ClientSize.Height) - 8);
        var iconRect = new RectangleF(
            (ClientSize.Width - size) / 2f,
            (ClientSize.Height - size) / 2f,
            size, size);
        IconPainter.Draw(g, Icon, iconRect, icon, 1.9f, bg.A > 0 ? bg : BaseColor);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    public void SetIcon(IconKind kind)
    {
        if (Icon == kind) return;
        Icon = kind;
        Invalidate();
    }

    private static GraphicsPath Rounded(Rectangle r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public sealed class SwatchButton : Control
{
    private bool _hover;

    public Color SwatchColor { get; set; } = Color.Red;
    public bool Checked { get; set; }
    public bool IsCustom { get; set; }
    public Color BaseColor { get; set; } = Theme.SurfaceAlt;

    public SwatchButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(26, 26);
        Cursor = Cursors.Hand;
        TabStop = false;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(BaseColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var center = new RectangleF(3, 3, ClientSize.Width - 6, ClientSize.Height - 6);
        if (Checked)
        {
            using var ring = new Pen(Theme.Accent, 2f);
            g.DrawEllipse(ring, center);
        }
        else if (_hover)
        {
            using var ring = new Pen(Theme.WithAlpha(Color.White, 110), 1f);
            g.DrawEllipse(ring, center);
        }

        var inner = new RectangleF(center.X + 3, center.Y + 3, center.Width - 6, center.Height - 6);
        if (IsCustom)
        {
            using var lg = new LinearGradientBrush(inner, Color.Red, Color.Blue, 20f);
            lg.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(255, 255, 59, 48), Color.FromArgb(255, 255, 204, 0), Color.FromArgb(255, 52, 199, 89), Color.FromArgb(255, 10, 132, 255), Color.FromArgb(255, 191, 90, 242) },
                Positions = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }
            };
            g.FillEllipse(lg, inner);
            using var white = new SolidBrush(Color.White);
            g.FillEllipse(white, inner.X + inner.Width * 0.27f, inner.Y + inner.Height * 0.27f, inner.Width * 0.46f, inner.Height * 0.46f);
            using var plus = new Pen(Theme.Text, 1.4f);
            float cx = inner.X + inner.Width / 2f, cy = inner.Y + inner.Height / 2f;
            g.DrawLine(plus, cx - 2.5f, cy, cx + 2.5f, cy);
            g.DrawLine(plus, cx, cy - 2.5f, cx, cy + 2.5f);
        }
        else
        {
            using var fill = new SolidBrush(SwatchColor);
            g.FillEllipse(fill, inner);
            using var border = new Pen(Color.FromArgb(130, 255, 255, 255), 1f);
            g.DrawEllipse(border, inner);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

public sealed class PillButton : Control
{
    private bool _hover;
    private bool _pressed;

    public bool Checked { get; set; }
    public Color BaseColor { get; set; } = Theme.SurfaceAlt;
    public Color CheckedColor { get; set; } = Theme.Accent;
    public bool ShowBorder { get; set; }

    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(40, 26);
        Cursor = Cursors.Hand;
        TabStop = false;
        Font = Theme.PixelFont(11.5f, FontStyle.Bold);
        AccessibleRole = AccessibleRole.PushButton;
    }

    public static int MeasureWidth(string text)
    {
        var size = TextRenderer.MeasureText(text, Theme.PixelFont(11.5f, FontStyle.Bold), new Size(int.MaxValue, 100), TextFormatFlags.NoPadding);
        return Math.Max(30, size.Width + 18);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(BaseColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        Color bg;
        Color fg;
        if (Checked)
        {
            bg = _pressed ? Theme.AccentHover : CheckedColor;
            fg = Color.White;
        }
        else if (_pressed)
        {
            bg = Theme.SurfacePressed;
            fg = Theme.Text;
        }
        else if (_hover)
        {
            bg = Theme.SurfaceHover;
            fg = Theme.Text;
        }
        else
        {
            bg = Color.Empty;
            fg = Theme.TextMuted;
        }

        var rect = new RectangleF(0.5f, 1.5f, ClientSize.Width - 1f, ClientSize.Height - 3f);
        if (bg.A > 0)
        {
            using var brush = new SolidBrush(bg);
            using var path = Rounded(rect, 6f);
            g.FillPath(brush, path);
        }
        if (ShowBorder && !Checked)
        {
            using var pen = new Pen(Theme.Border, 1f);
            using var path = Rounded(rect, 6f);
            g.DrawPath(pen, path);
        }

        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var textBrush = new SolidBrush(fg);
        g.DrawString(Text, Font, textBrush, new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), fmt);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    public void SetCheckedSilently(bool value)
    {
        if (Checked == value) return;
        Checked = value;
        Invalidate();
    }
}

public sealed class ValueSpinner : Control
{
    private int _value = 1;
    private bool _hoverMinus;
    private bool _hoverPlus;
    private bool _pressedMinus;
    private bool _pressedPlus;

    public int Minimum { get; set; } = 1;
    public int Maximum { get; set; } = 100;
    public int Step { get; set; } = 1;
    public string Suffix { get; set; } = "";
    public Color BaseColor { get; set; } = Theme.SurfaceAlt;
    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, Maximum);
            if (_value == v) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ValueSpinner()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(94, 28);
        Cursor = Cursors.Hand;
        TabStop = false;
        Font = Theme.PixelFont(11.5f, FontStyle.Bold);
    }

    public void SetValueSilently(int value)
    {
        _value = Math.Clamp(value, Minimum, Maximum);
        Invalidate();
    }

    private Rectangle MinusRect => new(2, 2, 24, Height - 4);
    private Rectangle PlusRect => new(Width - 26, 2, 24, Height - 4);

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(BaseColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new RectangleF(0.5f, 1.5f, Width - 1f, Height - 3f);
        using (var bg = new SolidBrush(Theme.Surface))
        using (var path = Rounded(rect, 6f))
            g.FillPath(bg, path);
        using (var border = new Pen(Theme.Border, 1f))
        using (var path = Rounded(rect, 6f))
            g.DrawPath(border, path);

        if (_pressedMinus || _pressedPlus)
        {
            var r = _pressedMinus ? MinusRect : PlusRect;
            using var pressed = new SolidBrush(Theme.SurfacePressed);
            using var path = Rounded(new RectangleF(r.X, r.Y, r.Width, r.Height), 5f);
            g.FillPath(pressed, path);
        }
        else if (_hoverMinus || _hoverPlus)
        {
            var r = _hoverMinus ? MinusRect : PlusRect;
            using var hover = new SolidBrush(Theme.SurfaceHover);
            using var path = Rounded(new RectangleF(r.X, r.Y, r.Width, r.Height), 5f);
            g.FillPath(hover, path);
        }

        using var pen = new Pen(Theme.TextMuted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float cy = Height / 2f;
        g.DrawLine(pen, 9, cy, 17, cy);
        g.DrawLine(pen, Width - 17, cy, Width - 9, cy);
        g.DrawLine(pen, Width - 13, cy - 4, Width - 13, cy + 4);

        string text = _value + Suffix;
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(Enabled ? Theme.Text : Theme.TextDim);
        g.DrawString(text, Font, brush, new RectangleF(26, 0, Width - 52, Height), fmt);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool hm = MinusRect.Contains(e.Location);
        bool hp = PlusRect.Contains(e.Location);
        if (hm != _hoverMinus || hp != _hoverPlus)
        {
            _hoverMinus = hm;
            _hoverPlus = hp;
            Cursor = hm || hp ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverMinus = _hoverPlus = false;
        _pressedMinus = _pressedPlus = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (MinusRect.Contains(e.Location))
        {
            _pressedMinus = true;
            Invalidate();
        }
        else if (PlusRect.Contains(e.Location))
        {
            _pressedPlus = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool minus = _pressedMinus && MinusRect.Contains(e.Location);
        bool plus = _pressedPlus && PlusRect.Contains(e.Location);
        _pressedMinus = _pressedPlus = false;
        Invalidate();
        if (minus) Value -= Step;
        else if (plus) Value += Step;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (e.Delta > 0) Value += Step;
        else if (e.Delta < 0) Value -= Step;
        base.OnMouseWheel(e);
    }
}

public sealed class TextEditorPanel : Panel
{
    public Color AccentColor { get; set; } = Theme.Accent;

    public TextEditorPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
        Padding = new Padding(9, 6, 8, 6);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var white = new SolidBrush(Color.White))
            g.FillRectangle(white, ClientRectangle);
        using (var accent = new SolidBrush(AccentColor))
            g.FillRectangle(accent, 0, 0, 3, Height);
        using (var border = new Pen(Theme.BorderStrong, 1f))
            g.DrawRectangle(border, 0.5f, 0.5f, Width - 1f, Height - 1f);
    }
}

public sealed class HotkeyInput : Control
{
    private string _hotkeyText = "";
    private bool _focused;

    public event EventHandler? HotkeyChanged;
    public event EventHandler<string>? InvalidHotkey;

    public string HotkeyText
    {
        get => _hotkeyText;
        set
        {
            _hotkeyText = value ?? "";
            Invalidate();
        }
    }

    public HotkeyInput()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Size = new Size(240, 30);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.PixelFont(11.5f, FontStyle.Bold);
        BackColor = Theme.Surface;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var brush = new SolidBrush(BackColor);
        pevent.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 1.5f, Width - 1f, Height - 3f);
        using (var bg = new SolidBrush(Theme.Surface))
        using (var path = Rounded(rect, 6f))
            g.FillPath(bg, path);
        using (var border = new Pen(_focused ? Theme.Accent : Theme.Border, _focused ? 1.5f : 1f))
        using (var path = Rounded(rect, 6f))
            g.DrawPath(border, path);

        string text = string.IsNullOrWhiteSpace(_hotkeyText) ? "Not set — click and press keys" : Format(_hotkeyText);
        using var brush2 = new SolidBrush(string.IsNullOrWhiteSpace(_hotkeyText) ? Theme.TextDim : Theme.Text);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        g.DrawString(text, string.IsNullOrWhiteSpace(_hotkeyText) ? Theme.PixelFont(10.5f) : Font, brush2,
            new RectangleF(8, 0, Width - 16, Height), fmt);
    }

    private static string Format(string spec) => Core.HotkeySpec.ParseOrEmpty(spec).IsEmpty ? spec : Core.HotkeySpec.ParseOrEmpty(spec).ToDisplayString();

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { _focused = true; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _focused = false; Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.Tab or Keys.Escape or Keys.Enter) return false;
        return base.IsInputKey(keyData) || key is >= Keys.A and <= Keys.Z;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Tab or Keys.Escape or Keys.Enter) return;

        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            HotkeyText = "";
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu)
            return;

        var mods = new List<string>();
        if ((e.Modifiers & Keys.Control) != 0) mods.Add("Ctrl");
        if ((e.Modifiers & Keys.Alt) != 0) mods.Add("Alt");
        if ((e.Modifiers & Keys.Shift) != 0) mods.Add("Shift");
        mods.Add(Core.HotkeySpec.KeyDisplayName(e.KeyCode));
        string candidate = string.Join("+", mods);

        if (Core.HotkeySpec.TryParse(candidate, out var spec, out string error))
        {
            HotkeyText = spec.ToDisplayString();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            InvalidHotkey?.Invoke(this, error);
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
