using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using DarkShot.Core;
using DarkShot.UI.Controls;

namespace DarkShot.UI;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _original;
    private AppSettings _result;

    private readonly Panel _titleBar = new();
    private readonly Panel _nav = new();
    private readonly Panel _content = new();
    private readonly Panel _footer = new();
    private readonly Panel _capturePanel = new();
    private readonly Panel _outputPanel = new();
    private readonly Panel _generalPanel = new();
    private readonly ToolTip _toolTip = new();

    private readonly PillButton _navCapture = new();
    private readonly PillButton _navOutput = new();
    private readonly PillButton _navGeneral = new();

    private readonly HotkeyInput _hotkeyRegion = new();
    private readonly HotkeyInput _hotkeyFullscreen = new();
    private readonly HotkeyInput _hotkeyWindow = new();
    private readonly HotkeyInput _hotkeyRepeat = new();

    private readonly PillButton _includeCursor = new();
    private readonly PillButton _magnifier = new();
    private readonly PillButton _detectWindows = new();
    private readonly PillButton _scopeCurrent = new();
    private readonly PillButton _scopeAll = new();
    private readonly PillButton _afterEditor = new();
    private readonly PillButton _afterCopy = new();
    private readonly PillButton _afterSave = new();
    private readonly ValueSpinner _delaySpinner = new();

    private readonly TextBox _saveFolder = new();
    private readonly TextBox _filenamePattern = new();
    private readonly PillButton _png = new();
    private readonly PillButton _jpeg = new();
    private readonly PillButton _bmp = new();
    private readonly PillButton _presetOriginal = new();
    private readonly PillButton _presetWord1200 = new();
    private readonly PillButton _presetWord1600 = new();
    private readonly PillButton _presetWeb = new();
    private readonly ValueSpinner _widthSpinner = new();
    private readonly ValueSpinner _dpiSpinner = new();
    private readonly ValueSpinner _printSpinner = new();
    private readonly ValueSpinner _qualitySpinner = new();
    private readonly PillButton _addTimestamp = new();
    private readonly PillButton _addBorder = new();
    private readonly PillButton _copyAfterSave = new();
    private readonly PillButton _openFolderAfterSave = new();
    private readonly TextBox _timestampFormat = new();
    private readonly TextBox _evidenceLabel = new();
    private readonly Label _outputSummary = new();

    private readonly PillButton _startWithWindows = new();
    private readonly PillButton _showNotifications = new();
    private readonly ValueSpinner _undoLimit = new();
    private readonly ValueSpinner _historyLimit = new();
    private readonly Label _generalInfo = new();

    private bool _loading;
    private int _activeSection;

    public AppSettings Result => _result;

    public SettingsForm(AppSettings settings)
    {
        _original = settings;
        _result = settings.Clone();

        Text = "DarkShot Settings";
        Name = "DarkShotSettings";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1040, 780);
        BackColor = Theme.Background;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;

        BuildChrome();
        BuildCaptureSection();
        BuildOutputSection();
        BuildGeneralSection();
        LoadValues(_result);
        ShowSection(0);
        UpdateOutputSummary();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NativeMethods.ApplyRoundedCorners(Handle);
        try
        {
            TopMost = true;
            NativeMethods.SetForegroundWindow(Handle);
            Activate();
            var timer = new System.Windows.Forms.Timer { Interval = 700 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!IsDisposed) TopMost = false;
            };
            timer.Start();
        }
        catch { /* ignore */ }
    }

    private void BuildChrome()
    {
        _titleBar.SetBounds(8, 8, ClientSize.Width - 16, 44);
        _titleBar.BackColor = Theme.Surface;
        _titleBar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            IconPainter.Draw(g, IconKind.Settings, new RectangleF(16, 10, 24, 24), Theme.Text, 1.7f, Theme.Surface);
            using var title = new SolidBrush(Theme.Text);
            using var sub = new SolidBrush(Theme.TextMuted);
            g.DrawString("Settings", Theme.UiFont(12.5f, FontStyle.Bold), title, 50, 6);
            g.DrawString("Local-only, low-footprint screenshot evidence", Theme.UiFont(8.6f), sub, 50, 26);
            using var border = new Pen(Theme.Border, 1f);
            g.DrawLine(border, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
        };
        _titleBar.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        };
        Controls.Add(_titleBar);

        var close = new IconButton
        {
            Icon = IconKind.Close,
            DangerHover = true,
            BaseColor = Theme.Surface,
            Size = new Size(32, 32),
            Location = new Point(_titleBar.Width - 40, 6)
        };
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _titleBar.Controls.Add(close);

        _nav.SetBounds(8, 60, 180, ClientSize.Height - 60 - 68);
        _nav.BackColor = Theme.Surface;
        _nav.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, _nav.Width - 1, 0, _nav.Width - 1, _nav.Height);
        };
        Controls.Add(_nav);

        ConfigureNavButton(_navCapture, "Capture", 0);
        ConfigureNavButton(_navOutput, "Output & Word", 1);
        ConfigureNavButton(_navGeneral, "General", 2);

        _content.SetBounds(200, 60, ClientSize.Width - 208, ClientSize.Height - 60 - 68);
        _content.BackColor = Theme.Background;
        Controls.Add(_content);

        foreach (var panel in new[] { _capturePanel, _outputPanel, _generalPanel })
        {
            panel.BackColor = Theme.Background;
            panel.AutoScroll = true;
            panel.Dock = DockStyle.Fill;
            panel.Visible = false;
            _content.Controls.Add(panel);
        }

        _footer.SetBounds(8, ClientSize.Height - 60, ClientSize.Width - 16, 52);
        _footer.BackColor = Theme.Surface;
        _footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawLine(pen, 0, 0, _footer.Width, 0);
            using var muted = new SolidBrush(Theme.TextDim);
            e.Graphics.DrawString("Config: " + AppPaths.SettingsFile, Theme.UiFont(8.2f), muted, 14, 18);
        };
        Controls.Add(_footer);

        var cancel = new PillButton
        {
            Text = "Cancel",
            BaseColor = Theme.Surface,
            ShowBorder = true,
            Height = 34,
            Width = PillButton.MeasureWidth("Cancel") + 16,
            Location = new Point(_footer.Width - 14 - (PillButton.MeasureWidth("Cancel") + 16), 9)
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var save = new PillButton
        {
            Text = "Save settings",
            BaseColor = Theme.Surface,
            Checked = true,
            CheckedColor = Theme.Accent,
            Height = 34,
            Width = PillButton.MeasureWidth("Save settings") + 22,
            Location = new Point(cancel.Left - (PillButton.MeasureWidth("Save settings") + 22) - 10, 9)
        };
        save.Click += (_, _) => SaveAndClose();

        _footer.Controls.Add(save);
        _footer.Controls.Add(cancel);
    }

    private void ConfigureNavButton(PillButton button, string text, int section)
    {
        button.Text = text;
        button.BaseColor = Theme.Surface;
        button.ShowBorder = false;
        button.Height = 38;
        button.Width = 150;
        button.Location = new Point(15, 14 + section * 48);
        button.Click += (_, _) => ShowSection(section);
        _nav.Controls.Add(button);
    }

    public void SelectSection(int index) => ShowSection(index);

    private void ShowSection(int index)
    {
        _activeSection = index;
        _capturePanel.Visible = index == 0;
        _outputPanel.Visible = index == 1;
        _generalPanel.Visible = index == 2;
        _navCapture.Checked = index == 0;
        _navOutput.Checked = index == 1;
        _navGeneral.Checked = index == 2;
    }

    private void BuildCaptureSection()
    {
        AddTitle(_capturePanel, "Capture", "Global hotkeys and what happens after a screenshot.");
        AddHeading(_capturePanel, "Hotkeys", 18, 84);
        AddFieldLabel(_capturePanel, "Region select", 18, 114);
        SetupHotkey(_capturePanel, _hotkeyRegion, 190, 108);
        AddFieldLabel(_capturePanel, "Full screen", 18, 154);
        SetupHotkey(_capturePanel, _hotkeyFullscreen, 190, 148);
        AddFieldLabel(_capturePanel, "Active window", 18, 194);
        SetupHotkey(_capturePanel, _hotkeyWindow, 190, 188);
        AddFieldLabel(_capturePanel, "Repeat last region", 18, 234);
        SetupHotkey(_capturePanel, _hotkeyRepeat, 190, 228);
        AddMuted(_capturePanel, "Click a field, then press the new key combination. Delete clears it.", 18, 266, 620);

        AddHeading(_capturePanel, "Capture options", 18, 304);
        SetupToggle(_capturePanel, _includeCursor, "Include mouse cursor", 18, 334);
        SetupToggle(_capturePanel, _magnifier, "Magnifier loupe", 220, 334);
        SetupToggle(_capturePanel, _detectWindows, "Detect window under cursor", 390, 334);
        AddFieldLabel(_capturePanel, "Delay before capture", 18, 384);
        SetupSpinner(_capturePanel, _delaySpinner, 190, 378, 0, 60, 1, " s");
        AddMuted(_capturePanel, "0 = instant. A delay is useful for opening menus before capturing.", 345, 384, 330);

        AddFieldLabel(_capturePanel, "Full screen mode", 18, 428);
        SetupChoice(_capturePanel, _scopeCurrent, "Current monitor", 190, 422);
        SetupChoice(_capturePanel, _scopeAll, "All monitors", 328, 422);
        _scopeCurrent.Click += (_, _) => { _scopeCurrent.Checked = true; _scopeAll.Checked = false; };
        _scopeAll.Click += (_, _) => { _scopeCurrent.Checked = false; _scopeAll.Checked = true; };

        AddFieldLabel(_capturePanel, "After capture", 18, 472);
        SetupChoice(_capturePanel, _afterEditor, "Open editor", 190, 466);
        SetupChoice(_capturePanel, _afterCopy, "Copy to clipboard", 308, 466);
        SetupChoice(_capturePanel, _afterSave, "Save file", 462, 466);
        _afterEditor.Click += (_, _) => SelectAfterCapture(AfterCaptureAction.OpenEditor);
        _afterCopy.Click += (_, _) => SelectAfterCapture(AfterCaptureAction.CopyToClipboard);
        _afterSave.Click += (_, _) => SelectAfterCapture(AfterCaptureAction.SaveToFile);

        AddMuted(_capturePanel,
            "The editor gives you arrows, boxes, text, auto-numbered steps, pixelate, blur and solid redaction.\n" +
            "Tip: if PrintScreen is reserved by Windows, use Ctrl+PrintScreen or another hotkey.",
            18, 522, 620);
    }

    private void SelectAfterCapture(AfterCaptureAction action)
    {
        _afterEditor.Checked = action == AfterCaptureAction.OpenEditor;
        _afterCopy.Checked = action == AfterCaptureAction.CopyToClipboard;
        _afterSave.Checked = action == AfterCaptureAction.SaveToFile;
    }

    private void BuildOutputSection()
    {
        AddTitle(_outputPanel, "Output & Word reports", "Choose one resolution convention and reuse it across every PoC.");
        AddFieldLabel(_outputPanel, "Save folder", 18, 92);
        SetupTextBox(_outputPanel, _saveFolder, 170, 86, 470);
        var browse = new PillButton
        {
            Text = "Browse…",
            BaseColor = Theme.Background,
            ShowBorder = true,
            Height = 30,
            Width = PillButton.MeasureWidth("Browse…") + 14,
            Location = new Point(650, 85)
        };
        browse.Click += (_, _) => BrowseFolder();
        _outputPanel.Controls.Add(browse);

        AddFieldLabel(_outputPanel, "Filename pattern", 18, 132);
        SetupTextBox(_outputPanel, _filenamePattern, 170, 126, 470);
        AddMuted(_outputPanel,
            "Tokens: {date} {time} {datetime} {type} {app} {title} {w} {h} {n}   —   custom date: {date:yyyyMMdd-HHmmss}",
            170, 158, 650);

        AddFieldLabel(_outputPanel, "Format", 18, 202);
        SetupChoice(_outputPanel, _png, "PNG", 170, 196);
        SetupChoice(_outputPanel, _jpeg, "JPEG", 240, 196);
        SetupChoice(_outputPanel, _bmp, "BMP", 320, 196);
        _png.Click += (_, _) => SelectFormat(ImageFormatKind.Png);
        _jpeg.Click += (_, _) => SelectFormat(ImageFormatKind.Jpeg);
        _bmp.Click += (_, _) => SelectFormat(ImageFormatKind.Bmp);

        AddFieldLabel(_outputPanel, "Report presets", 18, 242);
        SetupChoice(_outputPanel, _presetOriginal, "Original", 170, 236);
        SetupChoice(_outputPanel, _presetWord1200, "Word 1200 px", 262, 236);
        SetupChoice(_outputPanel, _presetWord1600, "Word 1600 px", 380, 236);
        SetupChoice(_outputPanel, _presetWeb, "Web 1920 px", 498, 236);
        _presetOriginal.Click += (_, _) => SetPreset(0, 96, 0);
        _presetWord1200.Click += (_, _) => SetPreset(1200, 191, 16);
        _presetWord1600.Click += (_, _) => SetPreset(1600, 254, 16);
        _presetWeb.Click += (_, _) => SetPreset(1920, 96, 0);

        AddHeading(_outputPanel, "Report sizing", 18, 286);
        AddFieldLabel(_outputPanel, "Target width (px)", 18, 318);
        SetupSpinner(_outputPanel, _widthSpinner, 170, 312, 0, 12000, 50, " px");
        AddFieldLabel(_outputPanel, "DPI metadata", 430, 318, 120);
        SetupSpinner(_outputPanel, _dpiSpinner, 540, 312, 48, 1200, 6, " dpi");
        AddFieldLabel(_outputPanel, "Print width (cm)", 18, 356);
        SetupSpinner(_outputPanel, _printSpinner, 170, 350, 0, 100, 1, " cm");
        AddFieldLabel(_outputPanel, "JPEG quality", 430, 356, 120);
        SetupSpinner(_outputPanel, _qualitySpinner, 540, 350, 10, 100, 1, "");
        AddMuted(_outputPanel,
            "0 px = original width. Print width overrides DPI: 1200 px at 16 cm → 191 DPI, so Word/PDF inserts the image at a predictable size.",
            18, 390, 650);

        AddHeading(_outputPanel, "Evidence extras", 18, 436);
        SetupToggle(_outputPanel, _addTimestamp, "Timestamp / label stamp", 18, 466);
        SetupToggle(_outputPanel, _addBorder, "1 px border", 280, 466);
        SetupToggle(_outputPanel, _copyAfterSave, "Copy after save", 430, 466);
        SetupToggle(_outputPanel, _openFolderAfterSave, "Open folder after save", 570, 466);
        AddFieldLabel(_outputPanel, "Timestamp format", 18, 514);
        SetupTextBox(_outputPanel, _timestampFormat, 170, 508, 260);
        AddFieldLabel(_outputPanel, "Evidence label", 18, 552);
        SetupTextBox(_outputPanel, _evidenceLabel, 170, 546, 470);
        AddMuted(_outputPanel, "Example label: ACME / TARGET-01 — included in the bottom-right stamp.", 170, 580, 620);

        _outputSummary.AutoSize = false;
        _outputSummary.SetBounds(18, 616, 650, 34);
        _outputSummary.BackColor = Theme.Surface;
        _outputSummary.ForeColor = Theme.Text;
        _outputSummary.Font = Theme.UiFont(9f, FontStyle.Bold);
        _outputSummary.TextAlign = ContentAlignment.MiddleLeft;
        _outputSummary.Padding = new Padding(12, 0, 12, 0);
        _outputSummary.AutoEllipsis = true;
        _outputSummary.UseMnemonic = false;
        _outputPanel.Controls.Add(_outputSummary);

        foreach (var spinner in new[] { _widthSpinner, _dpiSpinner, _printSpinner, _qualitySpinner })
            spinner.ValueChanged += (_, _) => UpdateOutputSummary();
    }

    private void SelectFormat(ImageFormatKind format)
    {
        _png.Checked = format == ImageFormatKind.Png;
        _jpeg.Checked = format == ImageFormatKind.Jpeg;
        _bmp.Checked = format == ImageFormatKind.Bmp;
        UpdateOutputSummary();
    }

    private void SetPreset(int width, int dpi, int printCm)
    {
        _widthSpinner.SetValueSilently(width);
        _dpiSpinner.SetValueSilently(dpi);
        _printSpinner.SetValueSilently(printCm);
        UpdateOutputSummary();
    }

    private void UpdateOutputSummary()
    {
        if (_loading) return;
        int width = _widthSpinner.Value;
        int dpi = _dpiSpinner.Value;
        int print = _printSpinner.Value;
        var format = _jpeg.Checked ? ImageFormatKind.Jpeg : _bmp.Checked ? ImageFormatKind.Bmp : ImageFormatKind.Png;
        int effective = print > 0 && width > 0
            ? ExportEngine.CalculateEffectiveDpi(width, new ExportSettings { TargetWidth = width, Dpi = dpi, PrintWidthCm = print })
            : dpi;
        string size = width > 0 ? $"{width} px wide" : "original width";
        string printText = print > 0 ? $"print {print} cm · {effective} DPI" : $"{dpi} DPI metadata";
        if (print > 0 && width > 0)
            _dpiSpinner.SetValueSilently(effective);
        string extras = "";
        if (_addTimestamp.Checked) extras += " · stamp";
        if (_addBorder.Checked) extras += " · border";
        _outputSummary.Text = $"{ImageFormatInfo.DisplayName(format)}  ·  {size}  ·  {printText}{extras}";
        if (width > 0 && width < 64)
            _outputSummary.Text += "   (width will be raised to 64 px)";
        PresetChecks();
    }

    private void BuildGeneralSection()
    {
        AddTitle(_generalPanel, "General", "Startup, notifications and maintenance.");
        SetupToggle(_generalPanel, _startWithWindows, "Start DarkShot with Windows", 18, 92);
        SetupToggle(_generalPanel, _showNotifications, "Show tray notifications", 18, 132);
        AddFieldLabel(_generalPanel, "Undo limit (editor)", 18, 184);
        SetupSpinner(_generalPanel, _undoLimit, 200, 178, 10, 500, 10, "");
        AddFieldLabel(_generalPanel, "History limit (files)", 18, 222);
        SetupSpinner(_generalPanel, _historyLimit, 200, 216, 20, 1000, 20, "");
        AddMuted(_generalPanel, "Lower undo/history limits reduce memory and disk usage.", 360, 198, 430);

        var openCaptures = new PillButton
        {
            Text = "Open captures folder",
            BaseColor = Theme.Background,
            ShowBorder = true,
            Height = 32,
            Width = PillButton.MeasureWidth("Open captures folder") + 16,
            Location = new Point(18, 268)
        };
        openCaptures.Click += (_, _) => ShellHelper.OpenFolder(_saveFolder.Text);
        _generalPanel.Controls.Add(openCaptures);

        var openConfig = new PillButton
        {
            Text = "Open config folder",
            BaseColor = Theme.Background,
            ShowBorder = true,
            Height = 32,
            Width = PillButton.MeasureWidth("Open config folder") + 16,
            Location = new Point(220, 268)
        };
        openConfig.Click += (_, _) => ShellHelper.OpenFolder(AppPaths.DataDir);
        _generalPanel.Controls.Add(openConfig);

        var reset = new PillButton
        {
            Text = "Reset defaults",
            BaseColor = Theme.Background,
            ShowBorder = true,
            Height = 32,
            Width = PillButton.MeasureWidth("Reset defaults") + 16,
            Location = new Point(408, 268)
        };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Reset all settings to defaults?", "DarkShot",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            LoadValues(new AppSettings());
        };
        _generalPanel.Controls.Add(reset);

        _generalInfo.AutoSize = false;
        _generalInfo.SetBounds(18, 330, 650, 180);
        _generalInfo.BackColor = Theme.Surface;
        _generalInfo.ForeColor = Theme.TextMuted;
        _generalInfo.Font = Theme.UiFont(9f);
        _generalInfo.Padding = new Padding(14, 12, 14, 12);
        _generalInfo.UseMnemonic = false;
        _generalInfo.Text =
            "Privacy & security\r\n\r\n" +
            "DarkShot is local-only: no telemetry, no cloud upload and no network client. " +
            "Screenshots can contain plaintext credentials, tokens and PII — they stay on this machine and in the folder you choose.\r\n\r\n" +
            "The watermark/stamp is useful for evidence traceability. Use Pixelate, Blur or Redact before sharing a PoC.\r\n\r\n" +
            "Config: " + AppPaths.SettingsFile;
        _generalPanel.Controls.Add(_generalInfo);

        var version = typeof(SettingsForm).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        AddMuted(_generalPanel, $"DarkShot {version}  ·  {Environment.OSVersion.VersionString}", 18, 530, 650);
    }

    private void AddTitle(Panel panel, string title, string subtitle)
    {
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Theme.Background,
            Font = Theme.UiFont(15f, FontStyle.Bold),
            Location = new Point(18, 14),
            UseMnemonic = false
        };
        panel.Controls.Add(titleLabel);
        var sub = new Label
        {
            Text = subtitle,
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Background,
            Font = Theme.UiFont(9f),
            Location = new Point(18, 44),
            UseMnemonic = false
        };
        panel.Controls.Add(sub);
    }

    private void AddHeading(Panel panel, string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Theme.Background,
            Font = Theme.UiFont(10f, FontStyle.Bold),
            Location = new Point(x, y),
            UseMnemonic = false
        };
        panel.Controls.Add(label);
    }

    private void AddFieldLabel(Panel panel, string text, int x, int y, int width = 0)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = width <= 0,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Background,
            Font = Theme.UiFont(9f),
            Location = new Point(x, y),
            UseMnemonic = false
        };
        if (width > 0) label.SetBounds(x, y, width, 20);
        panel.Controls.Add(label);
    }

    private void AddMuted(Panel panel, string text, int x, int y, int width)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = false,
            ForeColor = Theme.TextDim,
            BackColor = Theme.Background,
            Font = Theme.UiFont(8.6f),
            Location = new Point(x, y),
            Size = new Size(width, 40),
            UseMnemonic = false
        };
        panel.Controls.Add(label);
    }

    private void SetupHotkey(Panel panel, HotkeyInput input, int x, int y)
    {
        input.SetBounds(x, y, 260, 30);
        panel.Controls.Add(input);
    }

    private void SetupToggle(Panel panel, PillButton button, string text, int x, int y)
    {
        button.Text = text;
        button.BaseColor = Theme.Background;
        button.ShowBorder = true;
        button.Height = 30;
        button.Width = PillButton.MeasureWidth(text) + 16;
        button.Location = new Point(x, y);
        button.Click += (_, _) => { button.Checked = !button.Checked; UpdateOutputSummary(); };
        panel.Controls.Add(button);
    }

    private void SetupChoice(Panel panel, PillButton button, string text, int x, int y)
    {
        button.Text = text;
        button.BaseColor = Theme.Background;
        button.ShowBorder = true;
        button.Height = 30;
        button.Width = PillButton.MeasureWidth(text) + 16;
        button.Location = new Point(x, y);
        panel.Controls.Add(button);
    }

    private void SetupSpinner(Panel panel, ValueSpinner spinner, int x, int y, int min, int max, int step, string suffix)
    {
        spinner.Minimum = min;
        spinner.Maximum = max;
        spinner.Step = step;
        spinner.Suffix = suffix;
        spinner.BaseColor = Theme.Background;
        spinner.SetBounds(x, y, 140, 30);
        panel.Controls.Add(spinner);
    }

    private void SetupTextBox(Panel panel, TextBox box, int x, int y, int width)
    {
        box.SetBounds(x, y, width, 30);
        box.BackColor = Theme.Surface;
        box.ForeColor = Theme.Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = Theme.UiFont(9.6f);
        box.TextAlign = HorizontalAlignment.Left;
        panel.Controls.Add(box);
    }

    private void LoadValues(AppSettings settings)
    {
        _loading = true;
        try
        {
            _hotkeyRegion.HotkeyText = settings.Hotkeys.Region;
            _hotkeyFullscreen.HotkeyText = settings.Hotkeys.Fullscreen;
            _hotkeyWindow.HotkeyText = settings.Hotkeys.ActiveWindow;
            _hotkeyRepeat.HotkeyText = settings.Hotkeys.RepeatLast;

            _includeCursor.Checked = settings.IncludeCursor;
            _magnifier.Checked = settings.ShowMagnifier;
            _detectWindows.Checked = settings.DetectWindows;
            _delaySpinner.SetValueSilently(settings.CaptureDelaySeconds);

            bool scopeAll = settings.FullscreenScope == FullscreenScope.AllMonitors;
            _scopeCurrent.Checked = !scopeAll;
            _scopeAll.Checked = scopeAll;
            SelectAfterCapture(settings.AfterCapture);

            _saveFolder.Text = settings.SaveFolder;
            _filenamePattern.Text = settings.FilenamePattern;
            SelectFormat(settings.Export.Format);
            _widthSpinner.SetValueSilently(settings.Export.TargetWidth);
            _dpiSpinner.SetValueSilently(settings.Export.Dpi);
            _printSpinner.SetValueSilently((int)Math.Round(settings.Export.PrintWidthCm));
            _qualitySpinner.SetValueSilently(settings.Export.JpegQuality);
            _addTimestamp.Checked = settings.Export.AddTimestamp;
            _addBorder.Checked = settings.Export.AddBorder;
            _copyAfterSave.Checked = settings.CopyAfterSave;
            _openFolderAfterSave.Checked = settings.OpenFolderAfterSave;
            _timestampFormat.Text = settings.TimestampFormat;
            _evidenceLabel.Text = settings.EvidenceLabel;

            _startWithWindows.Checked = settings.StartWithWindows;
            _showNotifications.Checked = settings.ShowNotifications;
            _undoLimit.SetValueSilently(settings.EditorMaxUndo);
            _historyLimit.SetValueSilently(settings.HistoryLimit);

            PresetChecks();

            _generalInfo.Text =
                "Privacy & security\r\n\r\n" +
                "DarkShot is local-only: no telemetry, no cloud upload and no network client. " +
                "Screenshots can contain plaintext credentials, tokens and PII — they stay on this machine and in the folder you choose.\r\n\r\n" +
                "The watermark/stamp is useful for evidence traceability. Use Pixelate, Blur or Redact before sharing a PoC.\r\n\r\n" +
                "Config: " + AppPaths.SettingsFile;

            // After reset, reflect the new object in the fields that were not otherwise reloaded.
            _saveFolder.Text = settings.SaveFolder;
            _filenamePattern.Text = settings.FilenamePattern;
            _timestampFormat.Text = settings.TimestampFormat;
            _evidenceLabel.Text = settings.EvidenceLabel;
        }
        finally
        {
            _loading = false;
        }
        UpdateOutputSummary();
    }

    private void PresetChecks()
    {
        int width = _widthSpinner.Value;
        int print = _printSpinner.Value;
        _presetOriginal.Checked = width == 0 && print == 0;
        _presetWord1200.Checked = width == 1200 && print == 16;
        _presetWord1600.Checked = width == 1600 && print == 16;
        _presetWeb.Checked = width == 1920 && print == 0;
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where DarkShot saves screenshots",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_saveFolder.Text) ? _saveFolder.Text : _saveFolder.Text
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _saveFolder.Text = dialog.SelectedPath;
    }

    private void SaveAndClose()
    {
        if (!ValidateHotkeys(out string error))
        {
            MessageBox.Show(this, error, "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowSection(0);
            return;
        }

        string folder = _saveFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, "Choose a save folder.", "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowSection(1);
            return;
        }

        string pattern = _filenamePattern.Text.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            MessageBox.Show(this, "Filename pattern cannot be empty.", "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowSection(1);
            return;
        }

        try { Directory.CreateDirectory(folder); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Cannot create the save folder:\n\n" + ex.Message,
                "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ShowSection(1);
            return;
        }

        var result = _original.Clone();
        result.Hotkeys.Region = HotkeySpec.ParseOrEmpty(_hotkeyRegion.HotkeyText).IsEmpty ? "" : HotkeySpec.ParseOrEmpty(_hotkeyRegion.HotkeyText).ToString();
        result.Hotkeys.Fullscreen = HotkeySpec.ParseOrEmpty(_hotkeyFullscreen.HotkeyText).IsEmpty ? "" : HotkeySpec.ParseOrEmpty(_hotkeyFullscreen.HotkeyText).ToString();
        result.Hotkeys.ActiveWindow = HotkeySpec.ParseOrEmpty(_hotkeyWindow.HotkeyText).IsEmpty ? "" : HotkeySpec.ParseOrEmpty(_hotkeyWindow.HotkeyText).ToString();
        result.Hotkeys.RepeatLast = HotkeySpec.ParseOrEmpty(_hotkeyRepeat.HotkeyText).IsEmpty ? "" : HotkeySpec.ParseOrEmpty(_hotkeyRepeat.HotkeyText).ToString();

        result.IncludeCursor = _includeCursor.Checked;
        result.ShowMagnifier = _magnifier.Checked;
        result.DetectWindows = _detectWindows.Checked;
        result.CaptureDelaySeconds = _delaySpinner.Value;
        result.FullscreenScope = _scopeAll.Checked ? FullscreenScope.AllMonitors : FullscreenScope.CurrentMonitor;
        result.AfterCapture = _afterCopy.Checked ? AfterCaptureAction.CopyToClipboard
            : _afterSave.Checked ? AfterCaptureAction.SaveToFile
            : AfterCaptureAction.OpenEditor;

        result.SaveFolder = folder;
        result.FilenamePattern = pattern;
        result.Export.Format = _jpeg.Checked ? ImageFormatKind.Jpeg : _bmp.Checked ? ImageFormatKind.Bmp : ImageFormatKind.Png;
        result.Export.TargetWidth = _widthSpinner.Value;
        result.Export.Dpi = _dpiSpinner.Value;
        result.Export.PrintWidthCm = _printSpinner.Value;
        result.Export.JpegQuality = _qualitySpinner.Value;
        result.Export.AddTimestamp = _addTimestamp.Checked;
        result.Export.AddBorder = _addBorder.Checked;
        result.CopyAfterSave = _copyAfterSave.Checked;
        result.OpenFolderAfterSave = _openFolderAfterSave.Checked;
        result.TimestampFormat = string.IsNullOrWhiteSpace(_timestampFormat.Text) ? "yyyy-MM-dd HH:mm:ss" : _timestampFormat.Text.Trim();
        result.EvidenceLabel = _evidenceLabel.Text.Trim();

        result.StartWithWindows = _startWithWindows.Checked;
        result.ShowNotifications = _showNotifications.Checked;
        result.EditorMaxUndo = _undoLimit.Value;
        result.HistoryLimit = _historyLimit.Value;
        result.Normalize();
        _result = result;
        DialogResult = DialogResult.OK;
    }

    private bool ValidateHotkeys(out string error)
    {
        error = "";
        var inputs = new (string Name, HotkeyInput Input)[]
        {
            ("Region select", _hotkeyRegion),
            ("Full screen", _hotkeyFullscreen),
            ("Active window", _hotkeyWindow),
            ("Repeat last region", _hotkeyRepeat)
        };

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, input) in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.HotkeyText)) continue;
            if (!HotkeySpec.TryParse(input.HotkeyText, out var spec, out string parseError))
            {
                error = $"{name}: {parseError}";
                return false;
            }
            string normalized = spec.ToDisplayString();
            if (seen.TryGetValue(normalized, out string? other))
            {
                error = $"{name} and {other} use the same hotkey ({normalized}).";
                return false;
            }
            seen[normalized] = name;
        }
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
