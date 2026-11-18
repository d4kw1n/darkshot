using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using DarkShot.Core;
using DarkShot.UI.Controls;

namespace DarkShot.UI.Editor;

public sealed class ExportOptionsDialog : Form
{
    private readonly ExportSettings _source;
    private readonly Size _imageSize;
    private ExportSettings _result;

    private readonly Panel _titleBar = new();
    private readonly TextBox _widthBox = new();
    private readonly TextBox _dpiBox = new();
    private readonly TextBox _printWidthBox = new();
    private readonly TextBox _qualityBox = new();
    private readonly Label _summary = new();
    private readonly Label _dpiHint = new();
    private readonly Label _qualityLabel = new();
    private readonly PillButton _png = new();
    private readonly PillButton _jpeg = new();
    private readonly PillButton _bmp = new();
    private readonly PillButton _border = new();
    private readonly PillButton _timestamp = new();
    private bool _updating;

    public ExportSettings Result => _result;

    public ExportOptionsDialog(ExportSettings current, Size imageSize)
    {
        _source = current.Clone();
        _result = current.Clone();
        _imageSize = imageSize;

        Text = "DarkShot Export Settings";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(660, 600);
        BackColor = Theme.Background;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;

        BuildUi();
        LoadValues();
        UpdateSummary();
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
        _widthBox.Focus();
        _widthBox.SelectAll();
    }

    private void BuildUi()
    {
        _titleBar.SetBounds(6, 6, ClientSize.Width - 12, 44);
        _titleBar.BackColor = Theme.Surface;
        _titleBar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var title = new SolidBrush(Theme.Text);
            using var sub = new SolidBrush(Theme.TextMuted);
            g.DrawString("Export settings", Theme.UiFont(12f, FontStyle.Bold), title, 16, 3);
            g.DrawString("Resize once with clean DPI metadata for Word/PDF.", Theme.UiFont(8.5f), sub, 16, 24);
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
            Location = new Point(_titleBar.Width - 38, 6)
        };
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _titleBar.Controls.Add(close);

        AddHeading("Report presets", 18, 64);
        var original = Preset("Original", 18, 88, () => SetPreset(0, 96, 0));
        var word1200 = Preset("Word 1200 px", original.Right + 8, 88, () => SetPreset(1200, 191, 16));
        var word1600 = Preset("Word 1600 px", word1200.Right + 8, 88, () => SetPreset(1600, 254, 16));
        Preset("Web 1920 px", word1600.Right + 8, 88, () => SetPreset(1920, 96, 0));

        AddHeading("Image size", 18, 136);
        AddLabel("Target width (px) — 0 = original", 18, 162);
        AddTextBox(_widthBox, 18, 182, 280);
        AddLabel("DPI metadata", 330, 162);
        AddTextBox(_dpiBox, 330, 182, 140);
        AddLabel("Print width (cm) — 0 = off", 18, 224);
        AddTextBox(_printWidthBox, 18, 244, 280);
        _qualityLabel.Text = "JPEG quality";
        StyleLabel(_qualityLabel, 330, 224);
        Controls.Add(_qualityLabel);
        AddTextBox(_qualityBox, 330, 244, 140);

        _dpiHint.Text = "When print width is set, DPI is derived from pixel width ÷ physical size.";
        _dpiHint.SetBounds(18, 282, ClientSize.Width - 36, 20);
        _dpiHint.ForeColor = Theme.TextDim;
        _dpiHint.BackColor = Theme.Background;
        _dpiHint.Font = Theme.UiFont(8.4f);
        _dpiHint.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(_dpiHint);

        AddHeading("Format", 18, 316);
        _png.Text = "PNG";
        _jpeg.Text = "JPEG";
        _bmp.Text = "BMP";
        int formatX = 18;
        foreach (var pill in new[] { _png, _jpeg, _bmp })
        {
            pill.BaseColor = Theme.Background;
            pill.ShowBorder = true;
            pill.Height = 30;
            pill.Width = PillButton.MeasureWidth(pill.Text) + 14;
            pill.Location = new Point(formatX, 340);
            formatX += pill.Width + 8;
            Controls.Add(pill);
        }
        _png.Click += (_, _) => SetFormat(ImageFormatKind.Png);
        _jpeg.Click += (_, _) => SetFormat(ImageFormatKind.Jpeg);
        _bmp.Click += (_, _) => SetFormat(ImageFormatKind.Bmp);

        AddHeading("Evidence extras", 18, 388);
        _border.Text = "1 px border";
        _timestamp.Text = "Timestamp / label";
        int borderX = 18;
        foreach (var pill in new[] { _border, _timestamp })
        {
            pill.BaseColor = Theme.Background;
            pill.ShowBorder = true;
            pill.Height = 30;
            pill.Width = PillButton.MeasureWidth(pill.Text) + 14;
            pill.Location = new Point(borderX, 412);
            borderX += pill.Width + 10;
            pill.Click += (_, _) => { pill.Checked = !pill.Checked; UpdateSummary(); };
            Controls.Add(pill);
        }

        _summary.AutoSize = false;
        _summary.SetBounds(18, 462, ClientSize.Width - 36, 52);
        _summary.BackColor = Theme.Surface;
        _summary.ForeColor = Theme.Text;
        _summary.Font = Theme.UiFont(9.2f, FontStyle.Bold);
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        _summary.Padding = new Padding(12, 0, 12, 0);
        _summary.AutoEllipsis = true;
        _summary.UseMnemonic = false;
        Controls.Add(_summary);

        int cancelW = PillButton.MeasureWidth("Cancel") + 20;
        int okW = PillButton.MeasureWidth("Use settings") + 24;
        int buttonY = ClientSize.Height - 54;
        var cancel = new PillButton
        {
            Text = "Cancel",
            BaseColor = Theme.Background,
            ShowBorder = true,
            Width = cancelW,
            Height = 34,
            Location = new Point(ClientSize.Width - 18 - okW - 10 - cancelW, buttonY)
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        var ok = new PillButton
        {
            Text = "Use settings",
            BaseColor = Theme.Background,
            Checked = true,
            CheckedColor = Theme.Accent,
            Width = okW,
            Height = 34,
            Location = new Point(ClientSize.Width - 18 - okW, buttonY)
        };
        ok.Click += (_, _) => ApplyAndClose();
        Controls.Add(cancel);
        Controls.Add(ok);

        _widthBox.TextChanged += (_, _) => UpdateSummary();
        _dpiBox.TextChanged += (_, _) => UpdateSummary();
        _printWidthBox.TextChanged += (_, _) => UpdateSummary();
        _qualityBox.TextChanged += (_, _) => UpdateSummary();
    }

    private void AddHeading(string text, int x, int y)
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
        Controls.Add(label);
    }

    private void AddLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Background,
            Font = Theme.UiFont(8.8f),
            Location = new Point(x, y),
            UseMnemonic = false
        };
        Controls.Add(label);
    }

    private static void StyleLabel(Label label, int x, int y)
    {
        label.AutoSize = true;
        label.ForeColor = Theme.TextMuted;
        label.BackColor = Theme.Background;
        label.Font = Theme.UiFont(8.8f);
        label.Location = new Point(x, y);
        label.UseMnemonic = false;
    }

    private void AddTextBox(TextBox box, int x, int y, int width)
    {
        box.SetBounds(x, y, width, 30);
        box.BackColor = Theme.Surface;
        box.ForeColor = Theme.Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = Theme.UiFont(10f);
        box.TextAlign = HorizontalAlignment.Left;
        Controls.Add(box);
    }

    private PillButton Preset(string text, int x, int y, Action action)
    {
        var pill = new PillButton
        {
            Text = text,
            BaseColor = Theme.Background,
            ShowBorder = true,
            Height = 30,
            Width = PillButton.MeasureWidth(text) + 14,
            Location = new Point(x, y)
        };
        pill.Click += (_, _) => { action(); UpdateSummary(); };
        Controls.Add(pill);
        return pill;
    }

    private void LoadValues()
    {
        _updating = true;
        try
        {
            _widthBox.Text = _source.TargetWidth.ToString(CultureInfo.InvariantCulture);
            _dpiBox.Text = _source.Dpi.ToString(CultureInfo.InvariantCulture);
            _printWidthBox.Text = _source.PrintWidthCm.ToString("0.##", CultureInfo.InvariantCulture);
            _qualityBox.Text = _source.JpegQuality.ToString(CultureInfo.InvariantCulture);
            SetFormat(_source.Format);
            _border.Checked = _source.AddBorder;
            _timestamp.Checked = _source.AddTimestamp;
        }
        finally
        {
            _updating = false;
        }
    }

    private void SetFormat(ImageFormatKind kind)
    {
        _updating = true;
        try
        {
            _png.Checked = kind == ImageFormatKind.Png;
            _jpeg.Checked = kind == ImageFormatKind.Jpeg;
            _bmp.Checked = kind == ImageFormatKind.Bmp;
            _qualityBox.Enabled = kind == ImageFormatKind.Jpeg;
            _qualityLabel.ForeColor = kind == ImageFormatKind.Jpeg ? Theme.TextMuted : Theme.TextDim;
        }
        finally
        {
            _updating = false;
        }
    }

    private ImageFormatKind SelectedFormat()
        => _jpeg.Checked ? ImageFormatKind.Jpeg : _bmp.Checked ? ImageFormatKind.Bmp : ImageFormatKind.Png;

    private void SetPreset(int width, int dpi, double printCm)
    {
        _updating = true;
        try
        {
            _widthBox.Text = width.ToString(CultureInfo.InvariantCulture);
            _dpiBox.Text = dpi.ToString(CultureInfo.InvariantCulture);
            _printWidthBox.Text = printCm.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally
        {
            _updating = false;
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_updating) return;
        int sourceW = Math.Max(1, _imageSize.Width);
        int sourceH = Math.Max(1, _imageSize.Height);
        int width = ParseInt(_widthBox.Text, 0);
        int dpi = ParseInt(_dpiBox.Text, 96);
        double printCm = ParseDouble(_printWidthBox.Text, 0);
        var format = SelectedFormat();

        var draft = new ExportSettings
        {
            TargetWidth = Math.Clamp(width, 0, 12000),
            Dpi = Math.Clamp(dpi, 48, 1200),
            PrintWidthCm = Math.Clamp(printCm, 0, 200),
            Format = format
        };
        int finalWidth = draft.TargetWidth > 0 ? draft.TargetWidth : sourceW;
        int finalHeight = Math.Max(1, (int)Math.Round(sourceH * (double)finalWidth / sourceW));
        int effectiveDpi = ExportEngine.CalculateEffectiveDpi(finalWidth, draft);
        if (draft.PrintWidthCm > 0.1 && draft.TargetWidth > 0 &&
            !string.Equals(_dpiBox.Text.Trim(), effectiveDpi.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            _updating = true;
            _dpiBox.Text = effectiveDpi.ToString(CultureInfo.InvariantCulture);
            _updating = false;
        }

        var print = ExportEngine.CalculatePrintSize(new Size(finalWidth, finalHeight), effectiveDpi);
        string sizeText = print.IsEmpty
            ? $"{finalWidth} × {finalHeight} px"
            : $"{finalWidth} × {finalHeight} px  ·  {print.Width:0.0} × {print.Height:0.0} cm";
        _summary.Text = $"{ImageFormatInfo.DisplayName(format)}  ·  {sizeText}  ·  {effectiveDpi} DPI"
                        + (draft.AddTimestamp ? "  ·  timestamp" : "")
                        + (draft.AddBorder ? "  ·  border" : "");
    }

    private void ApplyAndClose()
    {
        int width = ParseInt(_widthBox.Text, -1);
        int dpi = ParseInt(_dpiBox.Text, -1);
        double printCm = ParseDouble(_printWidthBox.Text, -1);
        int quality = ParseInt(_qualityBox.Text, -1);

        if (width < 0 || width > 12000)
        {
            MessageBox.Show(this, "Target width must be 0 (original) or 64–12000 px.", "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (width > 0 && width < 64) width = 64;
        if (dpi < 48 || dpi > 1200)
        {
            MessageBox.Show(this, "DPI must be between 48 and 1200.", "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (printCm < 0 || printCm > 200)
        {
            MessageBox.Show(this, "Print width must be 0 (off) or 1–200 cm.", "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (quality < 10 || quality > 100)
        {
            MessageBox.Show(this, "JPEG quality must be between 10 and 100.", "DarkShot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _result = _source.Clone();
        _result.TargetWidth = width;
        _result.Dpi = dpi;
        _result.PrintWidthCm = printCm;
        _result.JpegQuality = quality;
        _result.Format = SelectedFormat();
        _result.AddBorder = _border.Checked;
        _result.AddTimestamp = _timestamp.Checked;
        DialogResult = DialogResult.OK;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }
        if (keyData == Keys.Enter)
        {
            ApplyAndClose();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static int ParseInt(string text, int fallback)
        => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    private static double ParseDouble(string text, double fallback)
    {
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return value;
        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return value;
        return fallback;
    }
}
