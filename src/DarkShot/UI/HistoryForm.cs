using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DarkShot.Core;
using DarkShot.UI.Controls;

namespace DarkShot.UI;

public sealed class HistoryForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action<string> _openFile;
    private readonly Panel _titleBar = new();
    private readonly Panel _toolbar = new();
    private readonly ListView _list = new();
    private readonly Panel _previewPanel = new();
    private readonly PictureBox _preview = new();
    private readonly Label _previewTitle = new();
    private readonly Label _previewInfo = new();
    private readonly Label _countLabel = new();
    private readonly Label _emptyLabel = new();
    private readonly ToolTip _toolTip = new();
    private readonly ImageList _rowHeight;
    private readonly List<FileInfo> _files = new();
    private string? _selectedPath;

    public HistoryForm(AppSettings settings, Action<string> openFile)
    {
        _settings = settings;
        _openFile = openFile;

        Text = "DarkShot History";
        Name = "DarkShotHistory";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(980, 700);
        BackColor = Theme.Background;
        DoubleBuffered = true;
        KeyPreview = true;

        var marker = new Bitmap(1, 27);
        _rowHeight = new ImageList { ImageSize = new Size(1, 27), ColorDepth = ColorDepth.Depth32Bit };
        _rowHeight.Images.Add(marker);

        BuildUi();
        ScanFiles();
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

    private void BuildUi()
    {
        _titleBar.SetBounds(8, 8, ClientSize.Width - 16, 44);
        _titleBar.BackColor = Theme.Surface;
        _titleBar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            IconPainter.Draw(g, IconKind.History, new RectangleF(16, 10, 24, 24), Theme.Text, 1.7f, Theme.Surface);
            using var title = new SolidBrush(Theme.Text);
            using var sub = new SolidBrush(Theme.TextMuted);
            g.DrawString("History", Theme.UiFont(12.5f, FontStyle.Bold), title, 50, 6);
            string folder = _settings.SaveFolder;
            if (folder.Length > 110) folder = "…" + folder[^108..];
            g.DrawString("Saved PoC screenshots  ·  " + folder, Theme.UiFont(8.6f), sub, 50, 26);
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
        close.Click += (_, _) => Close();
        _titleBar.Controls.Add(close);

        _toolbar.SetBounds(8, 60, ClientSize.Width - 16, 42);
        _toolbar.BackColor = Theme.Background;
        Controls.Add(_toolbar);

        var refresh = AddToolbarButton("Refresh", IconKind.Reset, 14);
        refresh.Click += (_, _) => ScanFiles();
        var open = AddToolbarButton("Open in editor", IconKind.Pen, 120);
        open.Click += (_, _) => OpenSelected();
        var reveal = AddToolbarButton("Reveal file", IconKind.Folder, 260);
        reveal.Click += (_, _) => RevealSelected();
        var copyPath = AddToolbarButton("Copy path", IconKind.Copy, 380);
        copyPath.Click += (_, _) => CopySelectedPath();
        var delete = AddToolbarButton("Delete", IconKind.Trash, 490);
        delete.Click += (_, _) => DeleteSelected();
        var openFolder = AddToolbarButton("Open captures folder", IconKind.Folder, 600);
        openFolder.Click += (_, _) => ShellHelper.OpenFolder(_settings.SaveFolder);

        _countLabel.AutoSize = true;
        _countLabel.ForeColor = Theme.TextMuted;
        _countLabel.BackColor = Theme.Background;
        _countLabel.Font = Theme.UiFont(8.8f);
        _countLabel.UseMnemonic = false;
        _countLabel.Location = new Point(_toolbar.Width - 218, 13);
        _toolbar.Controls.Add(_countLabel);

        _list.SetBounds(8, 108, 590, ClientSize.Height - 116);
        _list.BackColor = Theme.Surface;
        _list.ForeColor = Theme.Text;
        _list.BorderStyle = BorderStyle.None;
        _list.View = View.Details;
        _list.HeaderStyle = ColumnHeaderStyle.None;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.OwnerDraw = true;
        _list.SmallImageList = _rowHeight;
        _list.Font = Theme.UiFont(9.2f);
        _list.Columns.Add("Name", 340);
        _list.Columns.Add("Modified", 150);
        _list.Columns.Add("Size", 90);
        _list.DrawItem += List_DrawItem;
        _list.DrawSubItem += List_DrawSubItem;
        _list.SelectedIndexChanged += (_, _) => LoadPreview();
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
        };
        Controls.Add(_list);

        _emptyLabel.AutoSize = false;
        _emptyLabel.SetBounds(8, 108, 590, ClientSize.Height - 116);
        _emptyLabel.BackColor = Theme.Surface;
        _emptyLabel.ForeColor = Theme.TextMuted;
        _emptyLabel.Font = Theme.UiFont(10f);
        _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        _emptyLabel.UseMnemonic = false;
        _emptyLabel.Text = "No saved screenshots yet.\r\n\r\nPress PrintScreen and save your first PoC screenshot.";
        Controls.Add(_emptyLabel);

        _previewPanel.SetBounds(610, 108, ClientSize.Width - 618, ClientSize.Height - 116);
        _previewPanel.BackColor = Theme.Surface;
        _previewPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border, 1f);
            e.Graphics.DrawRectangle(pen, 0, 0, _previewPanel.Width - 1, _previewPanel.Height - 1);
        };
        Controls.Add(_previewPanel);

        _preview.SetBounds(14, 14, _previewPanel.Width - 28, 300);
        _preview.BackColor = Theme.Canvas;
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _previewPanel.Controls.Add(_preview);

        _previewTitle.AutoSize = false;
        _previewTitle.SetBounds(14, 326, _previewPanel.Width - 28, 28);
        _previewTitle.ForeColor = Theme.Text;
        _previewTitle.BackColor = Theme.Surface;
        _previewTitle.Font = Theme.UiFont(10f, FontStyle.Bold);
        _previewTitle.AutoEllipsis = true;
        _previewTitle.TextAlign = ContentAlignment.MiddleLeft;
        _previewTitle.UseMnemonic = false;
        _previewPanel.Controls.Add(_previewTitle);

        _previewInfo.AutoSize = false;
        _previewInfo.SetBounds(14, 356, _previewPanel.Width - 28, 120);
        _previewInfo.ForeColor = Theme.TextMuted;
        _previewInfo.BackColor = Theme.Surface;
        _previewInfo.Font = Theme.UiFont(8.8f);
        _previewInfo.TextAlign = ContentAlignment.TopLeft;
        _previewInfo.UseMnemonic = false;
        _previewPanel.Controls.Add(_previewInfo);
    }

    private PillButton AddToolbarButton(string text, IconKind icon, int x)
    {
        var button = new PillButton
        {
            Text = text,
            BaseColor = Theme.Background,
            ShowBorder = true,
            Height = 30,
            Width = PillButton.MeasureWidth(text) + 24,
            Location = new Point(x, 6)
        };
        _toolTip.SetToolTip(button, text);
        _toolbar.Controls.Add(button);
        return button;
    }

    private void ScanFiles()
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            _files.Clear();
            _list.Items.Clear();
            var folder = _settings.SaveFolder;
            if (Directory.Exists(folder))
            {
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp" };
                var files = new DirectoryInfo(folder)
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => extensions.Contains(f.Extension))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(Math.Clamp(_settings.HistoryLimit, 20, 1000))
                    .ToList();
                _files.AddRange(files);
            }

            _list.BeginUpdate();
            foreach (var file in _files)
            {
                string size = FormatSize(file.Length);
                var item = new ListViewItem(new[]
                {
                    file.Name,
                    file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    size
                })
                {
                    ImageIndex = 0,
                    Tag = file.FullName
                };
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            bool empty = _files.Count == 0;
            _emptyLabel.Visible = empty;
            _list.Visible = !empty;
            _countLabel.Text = empty ? "" : $"{_files.Count} file{(_files.Count == 1 ? "" : "s")}  ·  newest first";
            if (!empty)
            {
                _list.Items[0].Selected = true;
                _list.Select();
                LoadPreview();
            }
            else
            {
                ClearPreview();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "History scan failed");
            _emptyLabel.Text = "Could not read the captures folder:\r\n" + ex.Message;
            _emptyLabel.Visible = true;
            _list.Visible = false;
            ClearPreview();
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0} KB";
        return $"{bytes} B";
    }

    private void List_DrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        using var bg = new SolidBrush(e.Item?.Selected == true ? Theme.AccentSoft : Theme.Surface);
        e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Item?.Selected == true)
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
        }
    }

    private void List_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var color = e.Item?.Selected == true ? Theme.Text : (e.ColumnIndex == 0 ? Theme.Text : Theme.TextMuted);
        using var brush = new SolidBrush(color);
        var rect = new Rectangle(e.Bounds.X + (e.ColumnIndex == 0 ? 8 : 2), e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
        using var fmt = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        e.Graphics.DrawString(e.SubItem?.Text ?? "", Theme.UiFont(9.2f), brush, rect, fmt);
    }

    private void LoadPreview()
    {
        if (_list.SelectedItems.Count == 0)
        {
            ClearPreview();
            return;
        }

        var path = _list.SelectedItems[0].Tag as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ClearPreview();
            return;
        }

        _selectedPath = path;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var source = Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: true);
            int pw = Math.Max(32, _preview.Width);
            int ph = Math.Max(32, _preview.Height);
            var thumb = new Bitmap(pw, ph, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(thumb))
            {
                g.Clear(Theme.Canvas);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Min((float)pw / source.Width, (float)ph / source.Height);
                int w = Math.Max(1, (int)Math.Round(source.Width * scale));
                int h = Math.Max(1, (int)Math.Round(source.Height * scale));
                g.DrawImage(source, new Rectangle((pw - w) / 2, (ph - h) / 2, w, h));
            }

            var old = _preview.Image;
            _preview.Image = thumb;
            old?.Dispose();

            var info = new FileInfo(path);
            _previewTitle.Text = info.Name;
            _previewInfo.Text =
                $"{source.Width} × {source.Height} px\r\n" +
                $"{FormatSize(info.Length)}  ·  {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"{path}";
        }
        catch (Exception ex)
        {
            Log.Warn("History preview failed: " + ex.Message);
            ClearPreview();
            _previewInfo.Text = "Could not load preview:\r\n" + ex.Message;
        }
    }

    private void ClearPreview()
    {
        _selectedPath = null;
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        _previewTitle.Text = "";
        _previewInfo.Text = "";
    }

    private void OpenSelected()
    {
        if (string.IsNullOrWhiteSpace(_selectedPath) || !File.Exists(_selectedPath)) return;
        _openFile(_selectedPath);
    }

    private void RevealSelected()
    {
        if (!string.IsNullOrWhiteSpace(_selectedPath)) ShellHelper.SelectFile(_selectedPath);
    }

    private void CopySelectedPath()
    {
        if (!string.IsNullOrWhiteSpace(_selectedPath)) ClipboardHelper.CopyText(_selectedPath);
    }

    private void DeleteSelected()
    {
        if (string.IsNullOrWhiteSpace(_selectedPath) || !File.Exists(_selectedPath)) return;
        var result = MessageBox.Show(this,
            "Delete this screenshot?\n\n" + Path.GetFileName(_selectedPath) + "\n\nIt will be moved to the Recycle Bin.",
            "DarkShot History", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;
        if (ShellHelper.DeleteToRecycleBin(_selectedPath))
        {
            ScanFiles();
        }
        else
        {
            MessageBox.Show(this, "Could not delete the file.", "DarkShot History",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
