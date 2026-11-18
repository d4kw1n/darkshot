using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DarkShot.Annotations;
using DarkShot.Core;
using DarkShot.UI.Controls;

namespace DarkShot.UI.Editor;

public sealed class EditorForm : Form
{
    private const int FramePad = 6;
    private const int TitleHeight = 42;
    private const int RailWidth = 56;
    private const int OptionsHeight = 46;
    private const int StatusHeight = 30;

    private readonly AppSettings _settings;
    private readonly CaptureFrame _frame;
    private readonly CaptureMetadata _metadata;
    private readonly EditorCanvas _canvas;
    private readonly ToolTip _toolTip = new()
    {
        InitialDelay = 350,
        ReshowDelay = 120,
        AutoPopDelay = 9000,
        ShowAlways = true
    };

    private readonly Panel _titleBar = new();
    private readonly Panel _rail = new();
    private readonly Panel _options = new();
    private readonly Panel _status = new();
    private readonly Dictionary<EditorTool, IconButton> _toolButtons = new();

    private IconButton? _undoBtn;
    private IconButton? _redoBtn;
    private IconButton? _copyBtn;
    private IconButton? _saveBtn;
    private IconButton? _exportBtn;
    private IconButton? _pinBtn;
    private IconButton? _settingsBtn;
    private IconButton? _minBtn;
    private IconButton? _closeBtn;

    private readonly List<SwatchButton> _swatches = new();
    private SwatchButton? _customSwatch;
    private readonly List<PillButton> _widthPills = new();
    private PillButton? _fillPill;
    private PillButton? _boldPill;
    private PillButton? _bgPill;
    private PillButton? _duplicatePill;
    private PillButton? _deletePill;
    private PillButton? _resetCropPill;
    private ValueSpinner? _fontSpinner;
    private ValueSpinner? _strengthSpinner;
    private ValueSpinner? _stepSpinner;
    private Label? _fontLabel;
    private Label? _strengthLabel;
    private Label? _stepLabel;
    private Label? _nextStepLabel;
    private Label? _hintLabel;

    private Label? _messageLabel;
    private Label? _dimsLabel;
    private Label? _zoomLabel;
    private Label? _exportLabel;
    private System.Windows.Forms.Timer? _messageTimer;

    private bool _dirty;
    private bool _updatingOptions;
    private bool _strengthIsBlur;
    private bool _allowClose;
    private bool _customMaximized;
    private Rectangle _normalBounds;
    private string? _lastSavedPath;
    private string _defaultMessage = "Drag with a tool to annotate  ·  Ctrl+Z undo  ·  Ctrl+C copy  ·  Ctrl+S save";

    public event EventHandler? SettingsRequested;
    public event EventHandler? EditorClosed;

    public bool IsDirty => _dirty;
    public string? LastSavedPath => _lastSavedPath;
    public EditorCanvas Canvas => _canvas;

    public EditorForm(CaptureFrame frame, AppSettings settings)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _metadata = frame.Metadata;
        _settings = settings;
        _canvas = new EditorCanvas(frame.Image, settings);

        Text = "DarkShot Editor";
        Name = "DarkShotEditor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        MinimumSize = new Size(920, 620);
        ShowInTaskbar = true;
        ShowIcon = false;
        Icon = AppIcon.Get();

        BuildInterface();
        SizeAndCenter();
        WireEvents();
        ApplyLayout();
        UpdateToolButtons();
        UpdateTopButtons();
        UpdateOptionsBar();
        UpdateStatusTexts();
        SetStatusDefault();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NativeMethods.ApplyRoundedCorners(Handle);
        _canvas.FitToWindow();
        _canvas.Focus();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyLayout();
    }

    private void BuildInterface()
    {
        _titleBar.BackColor = Theme.Surface;
        _rail.BackColor = Theme.Surface;
        _options.BackColor = Theme.SurfaceAlt;
        _status.BackColor = Theme.Surface;

        Controls.Add(_canvas);
        Controls.Add(_titleBar);
        Controls.Add(_rail);
        Controls.Add(_options);
        Controls.Add(_status);

        BuildTitleBar();
        BuildToolRail();
        BuildOptionsBar();
        BuildStatusBar();

        _titleBar.Paint += TitleBar_Paint;
        _titleBar.MouseDown += TitleBar_MouseDown;
        _titleBar.DoubleClick += (_, _) => ToggleMaximize();
        _options.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, 0, _options.Height - 1, _options.Width, _options.Height - 1);
        };
        _rail.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, _rail.Width - 1, 0, _rail.Width - 1, _rail.Height);
        };
        _status.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, 0, 0, _status.Width, 0);
        };
    }

    private void BuildTitleBar()
    {
        _undoBtn = CreateTopButton(IconKind.Undo, "Undo  (Ctrl+Z)");
        _redoBtn = CreateTopButton(IconKind.Redo, "Redo  (Ctrl+Y)");
        _copyBtn = CreateTopButton(IconKind.Copy, "Copy image to clipboard  (Ctrl+C)");
        _saveBtn = CreateTopButton(IconKind.Save, "Quick save  (Ctrl+S)");
        _saveBtn.Accent = true;
        _exportBtn = CreateTopButton(IconKind.Export, "Save as / export options");
        _pinBtn = CreateTopButton(IconKind.Pin, "Pin to screen  (Ctrl+P)");
        _settingsBtn = CreateTopButton(IconKind.Settings, "DarkShot settings");
        _minBtn = CreateTopButton(IconKind.Minimize, "Minimize");
        _closeBtn = CreateTopButton(IconKind.Close, "Close editor");
        _closeBtn.DangerHover = true;

        _undoBtn.Click += (_, _) => _canvas.Undo();
        _redoBtn.Click += (_, _) => _canvas.Redo();
        _copyBtn.Click += (_, _) => CopyToClipboard();
        _saveBtn.Click += (_, _) => QuickSave();
        _exportBtn.Click += (_, _) => OpenExportOptions();
        _pinBtn.Click += (_, _) => PinToScreen();
        _settingsBtn.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _minBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _closeBtn.Click += (_, _) => Close();

        foreach (var button in new[] { _undoBtn, _redoBtn, _copyBtn, _saveBtn, _exportBtn, _pinBtn, _settingsBtn, _minBtn, _closeBtn })
        {
            button.BaseColor = Theme.Surface;
            button.Size = new Size(34, 34);
            _titleBar.Controls.Add(button);
        }
    }

    private IconButton CreateTopButton(IconKind icon, string tooltip)
    {
        var button = new IconButton { Icon = icon, BaseColor = Theme.Surface, IconSize = 19, Size = new Size(34, 34) };
        _toolTip.SetToolTip(button, tooltip);
        return button;
    }

    private void BuildToolRail()
    {
        var tools = new[]
        {
            EditorTool.Select, EditorTool.Pen, EditorTool.Highlighter, EditorTool.Line, EditorTool.Arrow,
            EditorTool.Rectangle, EditorTool.Ellipse, EditorTool.Text, EditorTool.Step,
            EditorTool.Pixelate, EditorTool.Blur, EditorTool.Redact, EditorTool.Crop
        };

        int y = 8;
        foreach (var tool in tools)
        {
            var button = new IconButton
            {
                Icon = EditorToolInfo.Icon(tool),
                BaseColor = Theme.Surface,
                IconSize = 19,
                Size = new Size(36, 36),
                Location = new Point(10, y)
            };
            _toolTip.SetToolTip(button, EditorToolInfo.Tooltip(tool));
            var captured = tool;
            button.Click += (_, _) => _canvas.SetTool(captured);
            _rail.Controls.Add(button);
            _toolButtons[tool] = button;
            y += 39;
        }
    }

    private void BuildOptionsBar()
    {
        var presets = new[]
        {
            Color.FromArgb(255, 59, 48), Color.FromArgb(255, 149, 0), Color.FromArgb(255, 230, 0),
            Color.FromArgb(52, 199, 89), Color.FromArgb(10, 132, 255), Color.FromArgb(191, 90, 242),
            Color.White, Color.FromArgb(20, 20, 22)
        };
        foreach (var color in presets)
        {
            var swatch = new SwatchButton
            {
                SwatchColor = color,
                BaseColor = Theme.SurfaceAlt,
                Size = new Size(26, 26),
                Visible = false
            };
            swatch.Click += (_, _) => _canvas.SetActiveColor(swatch.SwatchColor);
            _toolTip.SetToolTip(swatch, "Stroke / text colour");
            _options.Controls.Add(swatch);
            _swatches.Add(swatch);
        }

        _customSwatch = new SwatchButton
        {
            IsCustom = true,
            BaseColor = Theme.SurfaceAlt,
            Size = new Size(26, 26),
            Visible = false
        };
        _customSwatch.Click += (_, _) =>
        {
            using var dialog = new ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = _canvas.ActiveColor,
                CustomColors = Array.Empty<int>()
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _canvas.SetActiveColor(dialog.Color);
        };
        _toolTip.SetToolTip(_customSwatch, "Custom colour…");
        _options.Controls.Add(_customSwatch);

        for (int i = 0; i < 5; i++)
        {
            var pill = new PillButton
            {
                Text = "3",
                BaseColor = Theme.SurfaceAlt,
                Size = new Size(38, 26),
                Visible = false
            };
            pill.Click += (_, _) =>
            {
                if (pill.Tag is float value) _canvas.SetActiveStrokeWidth(value);
            };
            _options.Controls.Add(pill);
            _widthPills.Add(pill);
        }

        _fillPill = MakeOptionPill("Fill", "Toggle fill for rectangles/ellipses");
        _fillPill.Click += (_, _) => _canvas.SetShapeFill(!_canvas.ActiveFill);

        _boldPill = MakeOptionPill("B", "Bold text");
        _boldPill.Width = 34;
        _boldPill.Click += (_, _) => _canvas.SetFontBold(!_canvas.ActiveFontBold);

        _bgPill = MakeOptionPill("BG", "Text background");
        _bgPill.Width = 40;
        _bgPill.Click += (_, _) => _canvas.SetTextBackground(!_canvas.ActiveTextBackground);

        _duplicatePill = MakeOptionPill("Duplicate", "Duplicate selected annotation");
        _duplicatePill.Width = PillButton.MeasureWidth("Duplicate");
        _duplicatePill.Click += (_, _) => _canvas.DuplicateSelected();

        _deletePill = MakeOptionPill("Delete", "Delete selected annotation  (Del)");
        _deletePill.Width = PillButton.MeasureWidth("Delete");
        _deletePill.Click += (_, _) => _canvas.DeleteSelected();

        _resetCropPill = MakeOptionPill("Reset crop", "Restore the full screenshot");
        _resetCropPill.Width = PillButton.MeasureWidth("Reset crop");
        _resetCropPill.Click += (_, _) => _canvas.ResetCrop();

        _fontLabel = MakeOptionLabel("Size");
        _fontSpinner = new ValueSpinner
        {
            Minimum = 8,
            Maximum = 160,
            Step = 2,
            Suffix = " px",
            BaseColor = Theme.SurfaceAlt,
            Size = new Size(94, 28),
            Visible = false
        };
        _fontSpinner.ValueChanged += (_, _) =>
        {
            if (!_updatingOptions) _canvas.SetFontSize(_fontSpinner.Value);
        };
        _options.Controls.Add(_fontSpinner);

        _strengthLabel = MakeOptionLabel("Block");
        _strengthSpinner = new ValueSpinner
        {
            Minimum = 2,
            Maximum = 150,
            Step = 2,
            Suffix = " px",
            BaseColor = Theme.SurfaceAlt,
            Size = new Size(94, 28),
            Visible = false
        };
        _strengthSpinner.ValueChanged += (_, _) =>
        {
            if (_updatingOptions) return;
            if (_strengthIsBlur) _canvas.SetBlurRadius(_strengthSpinner.Value);
            else _canvas.SetPixelStrength(_strengthSpinner.Value);
        };
        _options.Controls.Add(_strengthSpinner);

        _stepLabel = MakeOptionLabel("Size");
        _stepSpinner = new ValueSpinner
        {
            Minimum = 12,
            Maximum = 120,
            Step = 2,
            Suffix = " px",
            BaseColor = Theme.SurfaceAlt,
            Size = new Size(94, 28),
            Visible = false
        };
        _stepSpinner.ValueChanged += (_, _) =>
        {
            if (!_updatingOptions) _canvas.SetStepSize(_stepSpinner.Value);
        };
        _options.Controls.Add(_stepSpinner);

        _nextStepLabel = MakeOptionLabel("Next: 1");
        _hintLabel = MakeOptionLabel("");
        _hintLabel.AutoSize = false;
        _hintLabel.ForeColor = Theme.TextDim;
        _hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        _hintLabel.AutoEllipsis = true;
    }

    private PillButton MakeOptionPill(string text, string tooltip)
    {
        var pill = new PillButton
        {
            Text = text,
            BaseColor = Theme.SurfaceAlt,
            Size = new Size(PillButton.MeasureWidth(text), 26),
            Visible = false
        };
        _toolTip.SetToolTip(pill, tooltip);
        _options.Controls.Add(pill);
        return pill;
    }

    private Label MakeOptionLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.SurfaceAlt,
            Font = Theme.UiFont(8.7f),
            Visible = false
        };
        _options.Controls.Add(label);
        return label;
    }

    private void BuildStatusBar()
    {
        _messageLabel = new Label
        {
            AutoSize = false,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Font = Theme.UiFont(8.7f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        _dimsLabel = new Label
        {
            AutoSize = false,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Surface,
            Font = Theme.UiFont(8.7f),
            TextAlign = ContentAlignment.MiddleRight,
            UseMnemonic = false
        };
        _zoomLabel = new Label
        {
            AutoSize = false,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Font = Theme.UiFont(8.7f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Cursor = Cursors.Hand,
            UseMnemonic = false
        };
        _exportLabel = new Label
        {
            AutoSize = false,
            ForeColor = Theme.Accent,
            BackColor = Theme.Surface,
            Font = Theme.UiFont(8.7f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            UseMnemonic = false
        };

        _zoomLabel.Click += (_, _) => _canvas.ActualSize();
        _exportLabel.Click += (_, _) => OpenExportOptions();
        _toolTip.SetToolTip(_zoomLabel, "Click for 100%  ·  Ctrl+0 fit");
        _toolTip.SetToolTip(_exportLabel, "Click to change output width, DPI, format and print size");

        _status.Controls.Add(_messageLabel);
        _status.Controls.Add(_dimsLabel);
        _status.Controls.Add(_zoomLabel);
        _status.Controls.Add(_exportLabel);

        _messageTimer = new System.Windows.Forms.Timer { Interval = 4200 };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            _messageLabel.Text = _defaultMessage;
            _messageLabel.ForeColor = Theme.TextMuted;
        };
    }

    private void WireEvents()
    {
        _canvas.DocumentChanged += (_, _) =>
        {
            _dirty = true;
            _titleBar.Invalidate();
            UpdateStatusTexts();
            UpdateTopButtons();
        };
        _canvas.SelectionChanged += (_, _) =>
        {
            UpdateOptionsBar();
            UpdateTopButtons();
        };
        _canvas.HistoryChanged += (_, _) =>
        {
            UpdateTopButtons();
            _titleBar.Invalidate();
        };
        _canvas.OptionsChanged += (_, _) => UpdateOptionsBar();
        _canvas.ToolChanged += (_, _) =>
        {
            UpdateToolButtons();
            UpdateOptionsBar();
            UpdateStatusTexts();
            ShowStatus(ToolStatusText(_canvas.Tool));
        };
        _canvas.ZoomChanged += (_, _) => UpdateStatusTexts();
        _canvas.StatusMessage += (_, message) => ShowStatus(message);
    }

    private static string ToolStatusText(EditorTool tool) => tool switch
    {
        EditorTool.Select => "Select: click an annotation to move/delete it. Del removes.",
        EditorTool.Pen => "Pen: drag to draw freehand.",
        EditorTool.Highlighter => "Highlighter: drag over text. Colours are translucent.",
        EditorTool.Line => "Line: drag from start to end.",
        EditorTool.Arrow => "Arrow: drag toward the element you are proving.",
        EditorTool.Rectangle => "Rectangle: drag to box a finding. Toggle Fill for emphasis.",
        EditorTool.Ellipse => "Ellipse: drag to circle a finding.",
        EditorTool.Text => "Text: click, type, then Ctrl+Enter or click outside to add.",
        EditorTool.Step => "Step numbers: click to place 1, 2, 3… in order.",
        EditorTool.Pixelate => "Pixelate: drag over tokens, emails, PII — irreversible in the export.",
        EditorTool.Blur => "Blur: drag over sensitive values. Increase radius for stronger blur.",
        EditorTool.Redact => "Redact: solid opaque block. Use for secrets/PII.",
        EditorTool.Crop => "Crop: drag a rectangle. The crop is non-destructive — Ctrl+Z restores it.",
        _ => ""
    };

    private void ApplyLayout()
    {
        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;

        _titleBar.SetBounds(FramePad, FramePad, Math.Max(0, w - FramePad * 2), TitleHeight);
        _rail.SetBounds(FramePad, FramePad + TitleHeight, RailWidth, Math.Max(0, h - FramePad * 2 - TitleHeight - StatusHeight));
        _options.SetBounds(FramePad + RailWidth, FramePad + TitleHeight, Math.Max(0, w - FramePad * 2 - RailWidth), OptionsHeight);
        _canvas.SetBounds(FramePad + RailWidth, FramePad + TitleHeight + OptionsHeight,
            Math.Max(0, w - FramePad * 2 - RailWidth),
            Math.Max(0, h - FramePad * 2 - TitleHeight - OptionsHeight - StatusHeight));
        _status.SetBounds(FramePad, h - FramePad - StatusHeight, Math.Max(0, w - FramePad * 2), StatusHeight);

        LayoutTitleButtons();
        LayoutStatusLabels();
        _titleBar.Invalidate();
    }

    private void LayoutTitleButtons()
    {
        if (_undoBtn == null || _closeBtn == null) return;
        int y = (TitleHeight - 34) / 2;
        int x = _titleBar.Width - 8 - 34;

        void Place(IconButton button, int gap)
        {
            button.SetBounds(x, y, 34, 34);
            x -= 34 + gap;
        }

        Place(_closeBtn, 4);
        Place(_minBtn!, 8);
        Place(_settingsBtn!, 4);
        Place(_pinBtn!, 8);
        Place(_exportBtn!, 4);
        Place(_saveBtn!, 4);
        Place(_copyBtn!, 12);
        Place(_redoBtn!, 4);
        Place(_undoBtn!, 4);
    }

    private void LayoutStatusLabels()
    {
        if (_messageLabel == null || _exportLabel == null) return;
        int y = 1;
        int height = StatusHeight - 2;
        int exportW = Math.Min(420, Math.Max(240, _status.Width / 3));
        int zoomW = 80;
        int dimsW = 120;
        int right = _status.Width - 10;
        _exportLabel.SetBounds(right - exportW, y, exportW, height);
        _zoomLabel!.SetBounds(_exportLabel.Left - zoomW - 8, y, zoomW, height);
        _dimsLabel!.SetBounds(_zoomLabel.Left - dimsW - 4, y, dimsW, height);
        _messageLabel.SetBounds(10, y, Math.Max(50, _dimsLabel.Left - 20), height);
    }

    private void TitleBar_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        IconPainter.Draw(g, IconKind.Logo, new RectangleF(14, 9, 24, 24), Theme.Text, 1.8f, Theme.Surface);

        var titleFont = Theme.UiFont(11.5f, FontStyle.Bold);
        var subtitleFont = Theme.UiFont(8.6f);
        using var titleBrush = new SolidBrush(Theme.Text);
        using var subtitleBrush = new SolidBrush(Theme.TextMuted);

        const string title = "DarkShot";
        g.DrawString(title, titleFont, titleBrush, 48, 4);
        var titleSize = g.MeasureString(title, titleFont);
        if (_dirty)
        {
            using var dot = new SolidBrush(Theme.Warning);
            g.FillEllipse(dot, 48 + titleSize.Width + 3, 11, 6, 6);
        }

        int available = Math.Max(80, (_undoBtn?.Left ?? _titleBar.Width - 380) - 64);
        string subtitle = BuildSubtitle();
        TextRenderer.DrawText(g, subtitle, subtitleFont,
            new Rectangle(48, 23, available, 17), Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);

        using var border = new Pen(Theme.Border, 1f);
        g.DrawLine(border, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
    }

    private string BuildSubtitle()
    {
        string kind = _metadata.Kind switch
        {
            CaptureKind.Window => "Window",
            CaptureKind.Fullscreen => "Full screen",
            CaptureKind.AllMonitors => "All monitors",
            CaptureKind.Clipboard => "Clipboard",
            CaptureKind.OpenedFile => "Image",
            _ => "Region"
        };
        string? source = _metadata.SourceTitle;
        if (string.IsNullOrWhiteSpace(source)) source = _metadata.SourceProcess ?? "";
        source = source.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (source.Length > 120) source = source[..120] + "…";
        string dims = $"{_canvas.CropRect.Width} × {_canvas.CropRect.Height}";
        return string.IsNullOrWhiteSpace(source)
            ? $"{kind}  ·  {dims}"
            : $"{kind}  ·  {dims}  ·  {source}";
    }

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, 0x00A1 /* WM_NCLBUTTONDOWN */, new IntPtr(2 /* HTCAPTION */), IntPtr.Zero);
    }

    private void ToggleMaximize()
    {
        if (!_customMaximized)
        {
            _normalBounds = Bounds;
            var screen = Screen.FromControl(this);
            Bounds = screen.WorkingArea;
            _customMaximized = true;
        }
        else
        {
            Bounds = _normalBounds;
            _customMaximized = false;
        }
        ApplyLayout();
    }

    private void SizeAndCenter()
    {
        var monitor = MonitorLayout.GetMonitorForWindow(_metadata.SourceWindow);
        var work = monitor?.WorkArea ?? Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);

        int desiredW = Math.Clamp(_canvas.CropRect.Width + RailWidth + FramePad * 2 + 220, 1000, Math.Max(1000, work.Width - 24));
        int desiredH = Math.Clamp(_canvas.CropRect.Height + TitleHeight + OptionsHeight + StatusHeight + FramePad * 2 + 160,
            680, Math.Max(680, work.Height - 24));
        int x = work.X + (work.Width - desiredW) / 2;
        int y = work.Y + (work.Height - desiredH) / 2;
        Bounds = new Rectangle(x, y, desiredW, desiredH);
        _normalBounds = Bounds;
    }

    private void UpdateToolButtons()
    {
        foreach (var (tool, button) in _toolButtons)
            button.Checked = tool == _canvas.Tool;
    }

    private void UpdateTopButtons()
    {
        if (_undoBtn != null) _undoBtn.Enabled = _canvas.CanUndo;
        if (_redoBtn != null) _redoBtn.Enabled = _canvas.CanRedo;
    }

    private void UpdateStatusTexts()
    {
        if (_dimsLabel != null)
            _dimsLabel.Text = $"{_canvas.CropRect.Width} × {_canvas.CropRect.Height}";
        if (_zoomLabel != null)
            _zoomLabel.Text = $"{_canvas.ZoomPercent}%";
        if (_exportLabel != null)
            _exportLabel.Text = BuildExportSummary();
    }

    private string BuildExportSummary()
    {
        var export = _settings.Export;
        int finalWidth = export.TargetWidth > 0 ? export.TargetWidth : _canvas.CropRect.Width;
        int dpi = ExportEngine.CalculateEffectiveDpi(finalWidth, export);
        string format = ImageFormatInfo.DisplayName(export.Format);
        string text = $"{format}  ·  {finalWidth} px  ·  {dpi} DPI";
        if (export.PrintWidthCm > 0)
            text += $"  ·  {export.PrintWidthCm:0.0} cm";
        if (export.AddTimestamp)
            text += "  ·  stamp";
        return text;
    }

    private void UpdateOptionsBar()
    {
        if (_updatingOptions) return;
        _updatingOptions = true;
        try
        {
            foreach (Control control in _options.Controls)
                control.Visible = false;

            var selected = _canvas.SelectedAnnotation;
            bool selectMode = _canvas.Tool == EditorTool.Select;
            if (selectMode && selected == null)
            {
                ShowHint("Click an annotation to move/resize/delete it, or pick a tool from the left rail.");
                return;
            }

            if (_canvas.Tool == EditorTool.Crop)
            {
                int cropX = 12;
                if (_canvas.IsCropped)
                {
                    _resetCropPill!.Width = PillButton.MeasureWidth("Reset crop");
                    ShowControl(_resetCropPill, ref cropX);
                }
                ShowHint("Drag a rectangle to crop. The crop is non-destructive — Ctrl+Z restores it.", cropX);
                return;
            }

            var kind = selected?.Kind;
            var tool = _canvas.Tool;
            bool showColors = true;
            bool showWidths = tool is EditorTool.Pen or EditorTool.Highlighter or EditorTool.Line or EditorTool.Arrow
                or EditorTool.Rectangle or EditorTool.Ellipse;
            bool showFill = tool is EditorTool.Rectangle or EditorTool.Ellipse;
            bool showFont = tool == EditorTool.Text;
            bool showStep = tool == EditorTool.Step;
            bool showStrength = tool is EditorTool.Pixelate or EditorTool.Blur;

            if (selectMode && kind.HasValue)
            {
                showColors = true;
                showWidths = kind is AnnotationKind.Pen or AnnotationKind.Highlighter or AnnotationKind.Line
                    or AnnotationKind.Arrow or AnnotationKind.Rectangle or AnnotationKind.Ellipse;
                showFill = kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse;
                showFont = kind == AnnotationKind.Text;
                showStep = kind == AnnotationKind.Step;
                showStrength = kind is AnnotationKind.Pixelate or AnnotationKind.Blur;
                if (kind is AnnotationKind.Pixelate or AnnotationKind.Blur or AnnotationKind.Redact)
                    showColors = kind == AnnotationKind.Redact;
            }

            int x = 12;
            const int centerY = (OptionsHeight - 28) / 2;

            if (showColors)
            {
                Color active = _canvas.ActiveColor;
                bool anyPreset = false;
                foreach (var swatch in _swatches)
                {
                    swatch.Visible = true;
                    swatch.Checked = SameRgb(swatch.SwatchColor, active);
                    if (swatch.Checked) anyPreset = true;
                    swatch.Location = new Point(x, centerY);
                    x += 26 + 3;
                }
                _customSwatch!.Visible = true;
                _customSwatch.Checked = !anyPreset;
                _customSwatch.Location = new Point(x, centerY);
                x += 26 + 14;
            }

            if (showWidths)
            {
                bool highlighter = (selectMode ? kind == AnnotationKind.Highlighter : tool == EditorTool.Highlighter);
                float[] values = highlighter ? new[] { 12f, 20f, 28f, 40f } : new[] { 1f, 2f, 3f, 5f, 8f };
                float active = _canvas.ActiveStrokeWidth;
                for (int i = 0; i < _widthPills.Count; i++)
                {
                    var pill = _widthPills[i];
                    float value = values[Math.Min(i, values.Length - 1)];
                    pill.Tag = value;
                    pill.Text = value.ToString("0");
                    pill.Width = PillButton.MeasureWidth(pill.Text);
                    pill.Checked = Math.Abs(active - value) < 0.01f;
                    pill.Visible = i < values.Length;
                    if (!pill.Visible) continue;
                    pill.Location = new Point(x, (OptionsHeight - 26) / 2);
                    x += pill.Width + 3;
                }
                x += 11;
            }

            if (showFill)
            {
                _fillPill!.Checked = _canvas.ActiveFill;
                _fillPill.Width = PillButton.MeasureWidth("Fill");
                ShowControl(_fillPill, ref x);
            }

            if (showFont)
            {
                ShowLabel(_fontLabel!, ref x);
                _fontSpinner!.Minimum = 8;
                _fontSpinner.Maximum = 160;
                _fontSpinner.Suffix = " px";
                _fontSpinner.SetValueSilently((int)Math.Round(_canvas.ActiveFontSize));
                ShowControl(_fontSpinner, ref x);
                _boldPill!.Checked = _canvas.ActiveFontBold;
                _boldPill.Width = 34;
                ShowControl(_boldPill, ref x);
                _bgPill!.Checked = _canvas.ActiveTextBackground;
                _bgPill.Width = 40;
                ShowControl(_bgPill, ref x);
            }

            if (showStep)
            {
                ShowLabel(_stepLabel!, ref x);
                _stepSpinner!.Minimum = 12;
                _stepSpinner.Maximum = 120;
                _stepSpinner.Suffix = " px";
                _stepSpinner.SetValueSilently(_canvas.ActiveStepSize);
                ShowControl(_stepSpinner, ref x);
                _nextStepLabel!.Text = $"Next: {_canvas.NextStepNumber}";
                ShowLabel(_nextStepLabel, ref x);
            }

            if (showStrength)
            {
                bool blur = (selectMode ? kind == AnnotationKind.Blur : tool == EditorTool.Blur);
                _strengthIsBlur = blur;
                _strengthLabel!.Text = blur ? "Radius" : "Block";
                ShowLabel(_strengthLabel, ref x);
                _strengthSpinner!.Minimum = 2;
                _strengthSpinner.Maximum = blur ? 150 : 120;
                _strengthSpinner.Suffix = " px";
                _strengthSpinner.SetValueSilently(_canvas.ActiveStrength);
                ShowControl(_strengthSpinner, ref x);
            }

            if (selectMode && selected != null)
            {
                _duplicatePill!.Width = PillButton.MeasureWidth("Duplicate");
                ShowControl(_duplicatePill, ref x);
                _deletePill!.Width = PillButton.MeasureWidth("Delete");
                ShowControl(_deletePill, ref x);
            }

            if (tool == EditorTool.Step && selectMode == false)
            {
                _nextStepLabel!.Text = $"Next: {_canvas.NextStepNumber}";
                ShowLabel(_nextStepLabel, ref x);
            }

            if (x < _options.Width - 80)
                ShowHint("", x);
        }
        finally
        {
            _updatingOptions = false;
        }
    }

    private void ShowControl(Control control, ref int x)
    {
        control.Visible = true;
        control.Location = new Point(x, (OptionsHeight - control.Height) / 2);
        x += control.Width + 4;
    }

    private void ShowLabel(Label label, ref int x)
    {
        label.Visible = true;
        label.Location = new Point(x, (OptionsHeight - label.Height) / 2);
        x += label.Width + 5;
    }

    private void ShowHint(string text, int x = 12)
    {
        if (_hintLabel == null) return;
        _hintLabel.Text = text;
        _hintLabel.Visible = true;
        _hintLabel.SetBounds(x, (OptionsHeight - 22) / 2, Math.Max(60, _options.Width - x - 12), 22);
    }

    private static bool SameRgb(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

    private void SetStatusDefault()
    {
        if (_messageLabel == null) return;
        _messageLabel.Text = _defaultMessage;
        _messageLabel.ForeColor = Theme.TextMuted;
    }

    private void ShowStatus(string message, Color? color = null)
    {
        if (_messageLabel == null || _messageTimer == null) return;
        _messageLabel.Text = message;
        _messageLabel.ForeColor = color ?? Theme.TextMuted;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    public void RefreshFromSettings()
    {
        UpdateStatusTexts();
        _titleBar.Invalidate();
    }

    private void OpenExportOptions()
    {
        _canvas.CommitPendingEdits();
        var size = new Size(_canvas.CropRect.Width, _canvas.CropRect.Height);
        using var dialog = new ExportOptionsDialog(_settings.Export.Clone(), size);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        CopyExport(dialog.Result, _settings.Export);
        SettingsStore.Save(_settings);
        UpdateStatusTexts();
        ShowStatus("Export preset updated");
    }

    private static void CopyExport(ExportSettings from, ExportSettings to)
    {
        to.TargetWidth = from.TargetWidth;
        to.Dpi = from.Dpi;
        to.PrintWidthCm = from.PrintWidthCm;
        to.Format = from.Format;
        to.JpegQuality = from.JpegQuality;
        to.AddBorder = from.AddBorder;
        to.AddTimestamp = from.AddTimestamp;
    }

    public bool QuickSave()
    {
        try
        {
            _canvas.CommitPendingEdits();
            _canvas.SaveDefaultsTo(_settings.Annotation);
            var outcome = CaptureOutput.Save(
                _frame.Image,
                _canvas.AnnotationsView,
                _canvas.CropRect,
                _metadata,
                _settings,
                _settings.Export);

            _dirty = false;
            _lastSavedPath = outcome.FilePath;
            _titleBar.Invalidate();
            UpdateStatusTexts();
            ShowStatus($"Saved {Path.GetFileName(outcome.FilePath)}  ·  {outcome.PixelSize.Width}×{outcome.PixelSize.Height}  ·  {outcome.Dpi} DPI");
            SettingsStore.Save(_settings);

            if (_settings.CopyAfterSave)
                CopyToClipboard(silent: true);
            if (_settings.OpenFolderAfterSave)
                ShellHelper.OpenFolder(Path.GetDirectoryName(outcome.FilePath));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Quick save failed");
            MessageBox.Show(this, "Could not save the screenshot:\n\n" + ex.Message,
                "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void SaveAs()
    {
        _canvas.CommitPendingEdits();
        var export = _settings.Export;
        var dialog = new SaveFileDialog
        {
            Title = "Save annotated screenshot",
            InitialDirectory = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            FileName = CaptureOutput.SuggestedFileName(_settings, _metadata, export),
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|BMP image (*.bmp)|*.bmp",
            FilterIndex = export.Format switch { ImageFormatKind.Jpeg => 2, ImageFormatKind.Bmp => 3, _ => 1 },
            DefaultExt = ImageFormatInfo.Extension(export.Format).TrimStart('.'),
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _canvas.SaveDefaultsTo(_settings.Annotation);
            var outcome = CaptureOutput.Save(
                _frame.Image,
                _canvas.AnnotationsView,
                _canvas.CropRect,
                _metadata,
                _settings,
                export,
                dialog.FileName);
            _dirty = false;
            _lastSavedPath = outcome.FilePath;
            _titleBar.Invalidate();
            ShowStatus($"Saved {Path.GetFileName(outcome.FilePath)}  ·  {outcome.PixelSize.Width}×{outcome.PixelSize.Height}  ·  {outcome.Dpi} DPI");
            SettingsStore.Save(_settings);
            if (_settings.CopyAfterSave) CopyToClipboard(silent: true);
            if (_settings.OpenFolderAfterSave) ShellHelper.OpenFolder(Path.GetDirectoryName(outcome.FilePath));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Save As failed");
            MessageBox.Show(this, "Could not save the screenshot:\n\n" + ex.Message,
                "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyToClipboard(bool silent = false)
    {
        try
        {
            _canvas.CommitPendingEdits();
            var outcome = CaptureOutput.Copy(
                _frame.Image,
                _canvas.AnnotationsView,
                _canvas.CropRect,
                _settings,
                _settings.Export,
                _metadata.CapturedAt);

            if (outcome.Success)
                ShowStatus($"Copied {outcome.PixelSize.Width}×{outcome.PixelSize.Height}  ·  {outcome.Dpi} DPI  ·  paste into Word", Theme.Success);
            else if (!silent)
                ShowStatus("Clipboard is busy — try Ctrl+C again", Theme.Danger);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Copy failed");
            if (!silent)
                ShowStatus("Copy failed: " + ex.Message, Theme.Danger);
        }
    }

    private void PinToScreen()
    {
        try
        {
            _canvas.CommitPendingEdits();
            var bitmap = AnnotationRenderer.Compose(_frame.Image, _canvas.AnnotationsView, _canvas.CropRect);
            var pin = new PinForm(bitmap, _settings);
            pin.Show();
            ShowStatus("Pinned to screen — drag to move, wheel to zoom, right-click for actions");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Pin failed");
            ShowStatus("Pin failed: " + ex.Message, Theme.Danger);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool ctrl = (keyData & Keys.Control) != 0;
        bool shift = (keyData & Keys.Shift) != 0;
        bool alt = (keyData & Keys.Alt) != 0;
        var key = keyData & Keys.KeyCode;

        if (_canvas.IsTextEditing)
        {
            if (ctrl && key is Keys.S or Keys.C or Keys.P)
                _canvas.CommitPendingEdits();
            else
                return base.ProcessCmdKey(ref msg, keyData);
        }

        if (ctrl)
        {
            switch (key)
            {
                case Keys.Z:
                    if (shift) _canvas.Redo(); else _canvas.Undo();
                    return true;
                case Keys.Y:
                    _canvas.Redo();
                    return true;
                case Keys.S:
                    if (shift) SaveAs(); else QuickSave();
                    return true;
                case Keys.C:
                    CopyToClipboard();
                    return true;
                case Keys.P:
                    PinToScreen();
                    return true;
                case Keys.D:
                    _canvas.DuplicateSelected();
                    return true;
                case Keys.W:
                    Close();
                    return true;
                case Keys.D0:
                case Keys.NumPad0:
                    _canvas.FitToWindow();
                    return true;
                case Keys.D1:
                case Keys.NumPad1:
                    _canvas.ActualSize();
                    return true;
                case Keys.Oemplus:
                case Keys.Add:
                    _canvas.ZoomIn();
                    return true;
                case Keys.OemMinus:
                case Keys.Subtract:
                    _canvas.ZoomOut();
                    return true;
            }
        }

        if (!ctrl && !alt)
        {
            var tool = key switch
            {
                Keys.V => EditorTool.Select,
                Keys.P => EditorTool.Pen,
                Keys.H => EditorTool.Highlighter,
                Keys.L => EditorTool.Line,
                Keys.A => EditorTool.Arrow,
                Keys.R => EditorTool.Rectangle,
                Keys.O => EditorTool.Ellipse,
                Keys.T => EditorTool.Text,
                Keys.N => EditorTool.Step,
                Keys.M => EditorTool.Pixelate,
                Keys.B => EditorTool.Blur,
                Keys.D => EditorTool.Redact,
                Keys.C => EditorTool.Crop,
                _ => (EditorTool?)null
            };
            if (tool.HasValue)
            {
                _canvas.SetTool(tool.Value);
                return true;
            }
        }

        if (key == Keys.Escape)
        {
            _canvas.CancelTransient();
            return true;
        }

        if (key == Keys.Delete)
        {
            if (_canvas.HasSelection)
            {
                _canvas.DeleteSelected();
                return true;
            }
        }

        if (_canvas.HasSelection && key is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            float step = shift ? 10f : 1f;
            switch (key)
            {
                case Keys.Left: _canvas.NudgeSelected(-step, 0); break;
                case Keys.Right: _canvas.NudgeSelected(step, 0); break;
                case Keys.Up: _canvas.NudgeSelected(0, -step); break;
                case Keys.Down: _canvas.NudgeSelected(0, step); break;
            }
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == NativeMethods.HTCLIENT)
            {
                int screenX = (short)((long)m.LParam & 0xFFFF);
                int screenY = (short)(((long)m.LParam >> 16) & 0xFFFF);
                const int edge = 7;
                bool left = screenX >= Left && screenX < Left + edge;
                bool right = screenX < Right && screenX >= Right - edge;
                bool top = screenY >= Top && screenY < Top + edge;
                bool bottom = screenY < Bottom && screenY >= Bottom - edge;

                if (top && left) m.Result = (IntPtr)NativeMethods.HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)NativeMethods.HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)NativeMethods.HTLEFT;
                else if (right) m.Result = (IntPtr)NativeMethods.HTRIGHT;
                else if (top) m.Result = (IntPtr)NativeMethods.HTTOP;
                else if (bottom) m.Result = (IntPtr)NativeMethods.HTBOTTOM;
            }
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            _canvas.CommitPendingEdits();
            if (_dirty)
            {
                var result = MessageBox.Show(this,
                    "Save your annotated screenshot before closing?",
                    "DarkShot", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (result == DialogResult.Yes && !QuickSave())
                {
                    e.Cancel = true;
                    return;
                }
            }
            _allowClose = true;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try
        {
            _canvas.SaveDefaultsTo(_settings.Annotation);
            SettingsStore.Save(_settings);
        }
        catch { /* ignore */ }

        _messageTimer?.Stop();
        _messageTimer?.Dispose();
        _toolTip.Dispose();
        try { _frame.Dispose(); } catch { /* ignore */ }
        EditorClosed?.Invoke(this, EventArgs.Empty);
        base.OnFormClosed(e);
    }
}
