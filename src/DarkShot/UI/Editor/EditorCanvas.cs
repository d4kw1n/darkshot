using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DarkShot.Annotations;
using DarkShot.Core;
using DarkShot.UI.Controls;

namespace DarkShot.UI.Editor;

public sealed class EditorCanvas : Control
{
    private enum DragMode { None, Create, MoveAnnotation, ResizeAnnotation, Crop, Pan }

    private readonly Bitmap _image;
    private readonly List<Annotation> _annotations = new();
    private readonly UndoHistory _history;

    private Rectangle _crop;
    private EditorTool _tool = EditorTool.Select;
    private Annotation? _selected;
    private int _selectedHandle = -1;

    private Color _color;
    private Color _highlighterColor;
    private Color _stepColor;
    private Color _redactColor = Color.Black;
    private float _strokeWidth;
    private float _highlighterWidth;
    private bool _filled;
    private float _fontSize;
    private bool _fontBold;
    private bool _textBackground;
    private int _pixelBlock;
    private int _blurRadius;
    private int _stepSize;
    private readonly int _stepStart;

    private float _zoom = 1f;
    private PointF _offset;
    private bool _autoFit = true;
    private Bitmap? _cache;
    private float _cacheZoom;
    private Rectangle _cacheCrop;

    private DragMode _drag;
    private PointF _dragStartImage;
    private PointF _dragCurrentImage;
    private RectangleF _cropDragRect;
    private Annotation? _preview;
    private List<PointF>? _freehandPoints;
    private EditorSnapshot? _pendingSnapshot;
    private Annotation? _dragOriginal;
    private bool _movedSinceDown;
    private bool _spaceDown;
    private Point _panStart;
    private PointF _panOffsetStart;

    private TextEditorPanel? _textPanel;
    private TextBox? _textBox;
    private Annotation? _editingAnnotation;
    private EditorSnapshot? _textEditSnapshot;
    private PointF _textAnchor;
    private bool _committingText;

    public EditorCanvas(Bitmap image, AppSettings settings)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        _crop = new Rectangle(0, 0, image.Width, image.Height);

        var annotation = settings.Annotation;
        _color = AnnotationDefaults.ParseColor(annotation.Color, Theme.Danger);
        _highlighterColor = AnnotationDefaults.ParseColor(annotation.HighlighterColor, Color.Gold);
        _stepColor = AnnotationDefaults.ParseColor(annotation.StepColor, Theme.Danger);
        _redactColor = Color.Black;
        _strokeWidth = annotation.StrokeWidth;
        _highlighterWidth = annotation.HighlighterWidth;
        _filled = annotation.FilledShapes;
        _fontSize = annotation.FontSize;
        _fontBold = annotation.FontBold;
        _textBackground = annotation.TextBackground;
        _pixelBlock = annotation.PixelBlock;
        _blurRadius = annotation.BlurRadius;
        _stepSize = annotation.StepSize;
        _stepStart = annotation.NextStepStart;
        _history = new UndoHistory(settings.EditorMaxUndo > 0 ? settings.EditorMaxUndo : 100);

        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Theme.Canvas;
        TabStop = true;
        AllowDrop = false;
    }

    public Bitmap Image => _image;
    public IReadOnlyList<Annotation> AnnotationsView => _annotations;
    public List<Annotation> Annotations => _annotations;
    public Rectangle CropRect => _crop;
    public Rectangle ImageBounds => new(0, 0, _image.Width, _image.Height);
    public EditorTool Tool => _tool;
    public Annotation? SelectedAnnotation => _selected;
    public bool HasSelection => _selected != null;
    public bool HasAnnotations => _annotations.Count > 0;
    public bool IsCropped => _crop != ImageBounds;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public float Zoom => _zoom;
    public int ZoomPercent => (int)Math.Round(_zoom * 100);
    public bool IsTextEditing => _textPanel != null;
    public int NextStepNumber => AnnotationDefaults.NextStepNumber(_annotations, _stepStart);

    public Color ActiveColor
    {
        get
        {
            if (_selected != null) return Opaque(_selected.Color);
            return _tool switch
            {
                EditorTool.Highlighter => _highlighterColor,
                EditorTool.Step => _stepColor,
                EditorTool.Redact => _redactColor,
                _ => _color
            };
        }
    }

    public float ActiveStrokeWidth
    {
        get
        {
            if (_selected is { Kind: AnnotationKind.Highlighter }) return _selected.StrokeWidth;
            if (_selected is { Kind: AnnotationKind.Pen or AnnotationKind.Line or AnnotationKind.Arrow
                or AnnotationKind.Rectangle or AnnotationKind.Ellipse }) return _selected.StrokeWidth;
            return _tool == EditorTool.Highlighter ? _highlighterWidth : _strokeWidth;
        }
    }

    public bool ActiveFill => _selected is { Kind: AnnotationKind.Rectangle or AnnotationKind.Ellipse }
        ? _selected.Filled
        : _filled;

    public float ActiveFontSize => _selected is { Kind: AnnotationKind.Text } ? _selected.FontSize : _fontSize;
    public bool ActiveFontBold => _selected is { Kind: AnnotationKind.Text } ? _selected.FontBold : _fontBold;
    public bool ActiveTextBackground => _selected is { Kind: AnnotationKind.Text } ? _selected.TextBackground : _textBackground;

    public int ActiveStrength
    {
        get
        {
            if (_selected is { Kind: AnnotationKind.Pixelate or AnnotationKind.Blur }) return _selected.Strength;
            return _tool == EditorTool.Blur ? _blurRadius : _pixelBlock;
        }
    }

    public int ActiveStepSize
    {
        get
        {
            if (_selected is { Kind: AnnotationKind.Step })
                return (int)Math.Round(Math.Min(_selected.Bounds.Width, _selected.Bounds.Height));
            return _stepSize;
        }
    }

    public Rectangle CurrentCrop => _crop;

    public event EventHandler? DocumentChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler? ZoomChanged;
    public event EventHandler? ToolChanged;
    public event EventHandler? OptionsChanged;
    public event EventHandler<string>? StatusMessage;

    public void SetTool(EditorTool tool)
    {
        if (_tool == tool && _drag == DragMode.None)
            return;

        CommitPendingEdits();
        CancelDragState();

        if (tool != EditorTool.Select && _selected != null)
            SelectAnnotationInternal(null);

        _tool = tool;
        _preview = null;
        _freehandPoints = null;
        _cropDragRect = RectangleF.Empty;
        Cursor = tool switch
        {
            EditorTool.Select => Cursors.Default,
            EditorTool.Text => Cursors.IBeam,
            _ => Cursors.Cross
        };
        ToolChanged?.Invoke(this, EventArgs.Empty);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SelectAnnotationInternal(Annotation? annotation)
    {
        if (ReferenceEquals(_selected, annotation)) return;
        _selected = annotation;
        _selectedHandle = -1;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SetActiveColor(Color color)
    {
        color = Color.FromArgb(255, color);
        Color effective = color;

        if (_selected != null)
        {
            switch (_selected.Kind)
            {
                case AnnotationKind.Highlighter:
                    _highlighterColor = color;
                    effective = Color.FromArgb(95, color);
                    break;
                case AnnotationKind.Step:
                    _stepColor = color;
                    break;
                case AnnotationKind.Redact:
                    _redactColor = color;
                    break;
                default:
                    _color = color;
                    break;
            }

            if (Opaque(_selected.Color) == color) return;
            PushUndo();
            _selected.Color = effective;
            MarkChanged();
            OptionsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        switch (_tool)
        {
            case EditorTool.Highlighter:
                _highlighterColor = color;
                break;
            case EditorTool.Step:
                _stepColor = color;
                break;
            case EditorTool.Redact:
                _redactColor = color;
                break;
            default:
                _color = color;
                break;
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SetActiveStrokeWidth(float width)
    {
        width = Math.Clamp(width, 1f, 80f);
        if (_selected != null && SupportsStroke(_selected.Kind))
        {
            if (Math.Abs(_selected.StrokeWidth - width) < 0.01f) return;
            PushUndo();
            _selected.StrokeWidth = width;
            if (_selected.Kind == AnnotationKind.Highlighter) _highlighterWidth = width;
            else _strokeWidth = width;
            MarkChanged();
            OptionsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_tool == EditorTool.Highlighter) _highlighterWidth = width;
        else _strokeWidth = width;
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetShapeFill(bool filled)
    {
        _filled = filled;
        if (_selected is { Kind: AnnotationKind.Rectangle or AnnotationKind.Ellipse })
        {
            if (_selected.Filled == filled) return;
            PushUndo();
            _selected.Filled = filled;
            MarkChanged();
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFontSize(float size)
    {
        size = Math.Clamp(size, 8f, 160f);
        _fontSize = size;
        if (_selected is { Kind: AnnotationKind.Text })
        {
            if (Math.Abs(_selected.FontSize - size) < 0.01f) return;
            PushUndo();
            _selected.FontSize = size;
            _selected.UpdateTextLayout();
            MarkChanged();
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFontBold(bool bold)
    {
        _fontBold = bold;
        if (_selected is { Kind: AnnotationKind.Text })
        {
            if (_selected.FontBold == bold) return;
            PushUndo();
            _selected.FontBold = bold;
            _selected.UpdateTextLayout();
            MarkChanged();
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTextBackground(bool background)
    {
        _textBackground = background;
        if (_selected is { Kind: AnnotationKind.Text })
        {
            if (_selected.TextBackground == background) return;
            PushUndo();
            _selected.TextBackground = background;
            MarkChanged();
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPixelStrength(int value)
    {
        value = Math.Clamp(value, 2, 120);
        _pixelBlock = value;
        if (_selected is { Kind: AnnotationKind.Pixelate })
        {
            if (_selected.Strength == value) return;
            PushUndo();
            _selected.Strength = value;
            MarkChanged();
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetBlurRadius(int value)
    {
        value = Math.Clamp(value, 2, 150);
        _blurRadius = value;
        if (_selected is { Kind: AnnotationKind.Blur })
        {
            if (_selected.Strength == value) return;
            PushUndo();
            _selected.Strength = value;
            MarkChanged();
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetStepSize(int value)
    {
        value = Math.Clamp(value, 10, 200);
        _stepSize = value;
        if (_selected is { Kind: AnnotationKind.Step })
        {
            float size = value;
            var center = GeometryUtil.Center(_selected.Bounds);
            PushUndo();
            _selected.Bounds = new RectangleF(center.X - size / 2f, center.Y - size / 2f, size, size);
            MarkChanged();
        }
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSelected()
    {
        if (_selected == null) return;
        var selected = _selected;
        PushUndo();
        _annotations.Remove(selected);
        SelectAnnotationInternal(null);
        MarkChanged();
        StatusMessage?.Invoke(this, "Deleted annotation");
    }

    public void DuplicateSelected()
    {
        if (_selected == null) return;
        PushUndo();
        var copy = _selected.Clone();
        copy.MoveBy(14, 14);
        _annotations.Add(copy);
        SelectAnnotationInternal(copy);
        MarkChanged();
        StatusMessage?.Invoke(this, "Duplicated annotation");
    }

    public void NudgeSelected(float dx, float dy)
    {
        if (_selected == null) return;
        PushUndo();
        _selected.MoveBy(dx, dy);
        MarkChanged();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        CommitPendingEdits();
        var snapshot = _history.Undo(CreateSnapshot());
        if (snapshot == null) return;
        bool cropChanged = snapshot.CropRect != _crop;
        ApplySnapshot(snapshot);
        if (cropChanged)
        {
            _autoFit = true;
            FitToWindow();
        }
        else
        {
            Invalidate();
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage?.Invoke(this, "Undo");
    }

    public void Redo()
    {
        CommitPendingEdits();
        var snapshot = _history.Redo(CreateSnapshot());
        if (snapshot == null) return;
        bool cropChanged = snapshot.CropRect != _crop;
        ApplySnapshot(snapshot);
        if (cropChanged)
        {
            _autoFit = true;
            FitToWindow();
        }
        else
        {
            Invalidate();
        }
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage?.Invoke(this, "Redo");
    }

    public void ResetCrop()
    {
        if (!IsCropped) return;
        PushUndo();
        _crop = ImageBounds;
        SelectAnnotationInternal(null);
        _autoFit = true;
        FitToWindow();
        MarkChanged();
        StatusMessage?.Invoke(this, "Crop reset");
    }

    public void CommitPendingEdits()
    {
        if (_textPanel != null) CommitTextEditor();
    }

    public void CancelTransient()
    {
        if (_textPanel != null) { CancelTextEditor(); return; }
        CancelDragState();
        SelectAnnotationInternal(null);
        Invalidate();
    }

    public void SaveDefaultsTo(AnnotationSettings settings)
    {
        settings.Color = AnnotationDefaults.ToHex(_color);
        settings.HighlighterColor = AnnotationDefaults.ToHex(_highlighterColor);
        settings.StepColor = AnnotationDefaults.ToHex(_stepColor);
        settings.StrokeWidth = _strokeWidth;
        settings.HighlighterWidth = _highlighterWidth;
        settings.FilledShapes = _filled;
        settings.FontSize = _fontSize;
        settings.FontBold = _fontBold;
        settings.TextBackground = _textBackground;
        settings.PixelBlock = _pixelBlock;
        settings.BlurRadius = _blurRadius;
        settings.StepSize = _stepSize;
    }

    public void FitToWindow()
    {
        if (ClientSize.Width < 20 || ClientSize.Height < 20) return;
        float availW = Math.Max(40, ClientSize.Width - 56);
        float availH = Math.Max(40, ClientSize.Height - 56);
        float zoom = Math.Min(availW / _crop.Width, availH / _crop.Height);
        zoom = Math.Clamp(zoom, 0.02f, 1f);
        _zoom = zoom;
        _offset = new PointF(
            (ClientSize.Width - _crop.Width * _zoom) / 2f,
            (ClientSize.Height - _crop.Height * _zoom) / 2f);
        _autoFit = true;
        DisposeCache();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void ActualSize()
    {
        SetZoom(1f, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
    }

    public void ZoomIn()
        => SetZoom(_zoom * 1.25f, new Point(ClientSize.Width / 2, ClientSize.Height / 2));

    public void ZoomOut()
        => SetZoom(_zoom / 1.25f, new Point(ClientSize.Width / 2, ClientSize.Height / 2));

    public void SetZoom(float zoom, Point anchorScreen)
    {
        zoom = Math.Clamp(zoom, 0.02f, 8f);
        if (Math.Abs(zoom - _zoom) < 0.0001f) return;

        var imagePoint = ScreenToImage(anchorScreen);
        _zoom = zoom;
        _autoFit = false;
        _offset = new PointF(
            anchorScreen.X - (imagePoint.X - _crop.X) * _zoom,
            anchorScreen.Y - (imagePoint.Y - _crop.Y) * _zoom);
        ClampOffset();
        DisposeCache();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public RectangleF GetImageScreenRect()
    {
        return new RectangleF(_offset.X, _offset.Y, _crop.Width * _zoom, _crop.Height * _zoom);
    }

    public PointF ScreenToImage(Point screen)
        => ScreenToImage(new PointF(screen.X, screen.Y));

    public PointF ScreenToImage(PointF screen)
        => new(_crop.X + (screen.X - _offset.X) / _zoom, _crop.Y + (screen.Y - _offset.Y) / _zoom);

    public PointF ImageToScreen(PointF image)
        => new(_offset.X + (image.X - _crop.X) * _zoom, _offset.Y + (image.Y - _crop.Y) * _zoom);

    public RectangleF ImageToScreen(RectangleF imageRect)
    {
        var p = ImageToScreen(new PointF(imageRect.X, imageRect.Y));
        return new RectangleF(p.X, p.Y, imageRect.Width * _zoom, imageRect.Height * _zoom);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Canvas);
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        EnsureBaseCache();
        var imageRect = GetImageScreenRect();

        using (var shadow = new SolidBrush(Color.FromArgb(65, 0, 0, 0)))
        using (var path = GeometryUtil.RoundedRect(
                   new RectangleF(imageRect.X + 5, imageRect.Y + 7, imageRect.Width, imageRect.Height), 4f))
            g.FillPath(shadow, path);

        var oldInterpolation = g.InterpolationMode;
        var oldOffset = g.PixelOffsetMode;
        if (_cache != null)
        {
            g.DrawImageUnscaled(_cache, (int)Math.Round(_offset.X), (int)Math.Round(_offset.Y));
        }
        else
        {
            g.InterpolationMode = Math.Abs(_zoom - 1f) < 0.0001f
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(_image, imageRect, _crop, GraphicsUnit.Pixel);
        }
        g.InterpolationMode = oldInterpolation;
        g.PixelOffsetMode = oldOffset;

        using (var border = new Pen(Theme.Border, 1f))
            g.DrawRectangle(border, imageRect.X, imageRect.Y, imageRect.Width, imageRect.Height);

        if (_annotations.Count > 0 || _preview != null)
        {
            var state = g.Save();
            try
            {
                g.SetClip(imageRect, CombineMode.Intersect);
                g.TranslateTransform(_offset.X, _offset.Y);
                g.ScaleTransform(_zoom, _zoom);
                g.TranslateTransform(-_crop.X, -_crop.Y);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                foreach (var annotation in _annotations)
                {
                    if (ReferenceEquals(annotation, _editingAnnotation)) continue;
                    AnnotationRenderer.DrawOne(g, _image, annotation);
                }
                if (_preview != null)
                    AnnotationRenderer.DrawOne(g, _image, _preview);
            }
            finally
            {
                g.Restore(state);
            }
        }

        if (_drag == DragMode.Crop && _cropDragRect.Width > 0 && _cropDragRect.Height > 0)
            DrawCropPreview(g);

        if (_selected != null && _editingAnnotation == null)
            DrawSelection(g);
    }

    private void EnsureBaseCache()
    {
        if (_crop.Width <= 0 || _crop.Height <= 0) return;
        int destW = Math.Max(1, (int)Math.Round(_crop.Width * _zoom));
        int destH = Math.Max(1, (int)Math.Round(_crop.Height * _zoom));

        if (destW == _crop.Width && destH == _crop.Height)
        {
            DisposeCache();
            return;
        }
        if ((long)destW * destH > 40_000_000)
        {
            DisposeCache();
            return;
        }
        if (_cache != null && Math.Abs(_cacheZoom - _zoom) < 0.0001f && _cacheCrop == _crop)
            return;

        DisposeCache();
        var bmp = new Bitmap(destW, destH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(_image, new Rectangle(0, 0, destW, destH), _crop, GraphicsUnit.Pixel);
        }
        _cache = bmp;
        _cacheZoom = _zoom;
        _cacheCrop = _crop;
    }

    private void DrawCropPreview(Graphics g)
    {
        var imageRect = GetImageScreenRect();
        var preview = ImageToScreen(_cropDragRect);
        var clipped = RectangleF.Intersect(imageRect, preview);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;

        var state = g.Save();
        try
        {
            g.SetClip(imageRect, CombineMode.Intersect);
            using var shade = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
            g.FillRectangle(shade, imageRect.X, imageRect.Y, imageRect.Width, clipped.Top - imageRect.Y);
            g.FillRectangle(shade, imageRect.X, clipped.Bottom, imageRect.Width, imageRect.Bottom - clipped.Bottom);
            g.FillRectangle(shade, imageRect.X, clipped.Top, clipped.Left - imageRect.X, clipped.Height);
            g.FillRectangle(shade, clipped.Right, clipped.Top, imageRect.Right - clipped.Right, clipped.Height);

            using var thirds = new Pen(Color.FromArgb(80, 255, 255, 255), 1f);
            for (int i = 1; i <= 2; i++)
            {
                float x = clipped.Left + clipped.Width * i / 3f;
                float y = clipped.Top + clipped.Height * i / 3f;
                g.DrawLine(thirds, x, clipped.Top, x, clipped.Bottom);
                g.DrawLine(thirds, clipped.Left, y, clipped.Right, y);
            }

            using var accent = new Pen(Theme.Selection, 2f);
            g.DrawRectangle(accent, clipped.X, clipped.Y, clipped.Width, clipped.Height);

            string label = $"{Math.Round(_cropDragRect.Width)} × {Math.Round(_cropDragRect.Height)}";
            var font = Theme.PixelFont(12, FontStyle.Bold);
            var size = g.MeasureString(label, font);
            var pill = new RectangleF(clipped.X, clipped.Y - size.Height - 14, size.Width + 20, size.Height + 8);
            if (pill.Y < imageRect.Y + 4) pill.Y = clipped.Y + 8;
            pill.X = Math.Clamp(pill.X, imageRect.X + 4, imageRect.Right - pill.Width - 4);
            using (var bg = new SolidBrush(Color.FromArgb(235, 18, 20, 25)))
            using (var path = GeometryUtil.RoundedRect(pill, 6f))
                g.FillPath(bg, path);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var text = new SolidBrush(Theme.Text);
            g.DrawString(label, font, text, pill, fmt);
        }
        finally
        {
            g.Restore(state);
        }
    }

    private void DrawSelection(Graphics g)
    {
        var bounds = ImageToScreen(_selected!.GetSelectionBounds());
        using (var white = new Pen(Color.FromArgb(190, 255, 255, 255), 1f))
            g.DrawRectangle(white, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        using (var pen = new Pen(Theme.Selection, 1.4f) { DashStyle = DashStyle.Dash })
            g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);

        var handles = _selected.GetHandles();
        if (handles.Length == 0) return;
        float size = HandleSize;
        foreach (var handle in handles)
        {
            var p = ImageToScreen(handle);
            var rect = new RectangleF(p.X - size / 2f, p.Y - size / 2f, size, size);
            using var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
            g.FillRectangle(shadow, rect.X + 1, rect.Y + 1, rect.Width, rect.Height);
            using var fill = new SolidBrush(Color.White);
            g.FillRectangle(fill, rect);
            using var border = new Pen(Theme.Selection, 1.4f);
            g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    private float HandleSize => Math.Clamp(9f * DeviceDpi / 96f, 8f, 13f);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (ClientSize.Width < 20 || ClientSize.Height < 20) return;
        if (_autoFit)
        {
            FitToWindow();
        }
        else
        {
            ClampOffset();
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Middle || (_spaceDown && e.Button == MouseButtons.Left))
        {
            StartPan(e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (_textPanel != null)
        {
            CommitTextEditor();
            if (IsDisposed) return;
        }

        var imagePoint = ScreenToImage(e.Location);

        if (_tool == EditorTool.Crop)
        {
            BeginCrop(imagePoint);
            return;
        }

        if (_tool == EditorTool.Step)
        {
            if (InCrop(imagePoint)) AddStep(imagePoint);
            return;
        }

        if (_tool == EditorTool.Text)
        {
            if (InCrop(imagePoint)) BeginTextEditing(ClampToCrop(imagePoint), null);
            return;
        }

        if (_tool == EditorTool.Select)
        {
            BeginSelect(e.Location, imagePoint);
            return;
        }

        BeginDraw(imagePoint);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_drag == DragMode.Pan)
        {
            var delta = new Size(e.Location.X - _panStart.X, e.Location.Y - _panStart.Y);
            _offset = new PointF(_panOffsetStart.X + delta.Width, _panOffsetStart.Y + delta.Height);
            _autoFit = false;
            ClampOffset();
            Invalidate();
            return;
        }

        if (_drag is DragMode.Create or DragMode.MoveAnnotation or DragMode.ResizeAnnotation or DragMode.Crop)
        {
            var imagePoint = ScreenToImage(e.Location);
            switch (_drag)
            {
                case DragMode.Create:
                    UpdatePreview(imagePoint);
                    break;
                case DragMode.MoveAnnotation:
                    UpdateMove(imagePoint);
                    break;
                case DragMode.ResizeAnnotation:
                    UpdateResize(imagePoint);
                    break;
                case DragMode.Crop:
                    UpdateCrop(imagePoint);
                    break;
            }
            UpdateCursor(e.Location);
            return;
        }

        UpdateCursor(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        switch (_drag)
        {
            case DragMode.Create:
                FinalizePreview();
                break;
            case DragMode.MoveAnnotation:
            case DragMode.ResizeAnnotation:
                if (_movedSinceDown)
                {
                    DocumentChanged?.Invoke(this, EventArgs.Empty);
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    OptionsChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
            case DragMode.Crop:
                ApplyCrop();
                break;
            case DragMode.Pan:
                break;
        }

        _drag = DragMode.None;
        _pendingSnapshot = null;
        _dragOriginal = null;
        _movedSinceDown = false;
        if (Capture) Capture = false;
        UpdateCursor(e.Location);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left || _tool != EditorTool.Select) return;

        var imagePoint = ScreenToImage(e.Location);
        var hit = _selected ?? HitAnnotation(imagePoint);
        if (hit is { Kind: AnnotationKind.Text } && hit.GetBounds().Contains(imagePoint))
        {
            SelectAnnotationInternal(hit);
            BeginTextEditing(new PointF(hit.Bounds.X, hit.Bounds.Y), hit);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) != 0)
        {
            float factor = (float)Math.Pow(1.18, e.Delta / 120.0);
            SetZoom(_zoom * factor, e.Location);
        }
        else
        {
            float amount = e.Delta * 0.65f;
            if ((ModifierKeys & Keys.Shift) != 0)
                _offset.X += amount;
            else
                _offset.Y += amount;
            _autoFit = false;
            ClampOffset();
            Invalidate();
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) is Keys.Space or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Delete)
            return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space)
        {
            _spaceDown = true;
            Cursor = Cursors.SizeAll;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_textPanel != null) CancelTextEditor();
            else if (_drag == DragMode.Crop)
            {
                _drag = DragMode.None;
                _cropDragRect = RectangleF.Empty;
                if (Capture) Capture = false;
                Invalidate();
            }
            else SelectAnnotationInternal(null);
        }
        else if (e.KeyCode == Keys.Delete)
        {
            DeleteSelected();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            _spaceDown = false;
            UpdateCursor(PointToClient(Cursor.Position));
        }
    }

    private void StartPan(Point location)
    {
        _drag = DragMode.Pan;
        _panStart = location;
        _panOffsetStart = _offset;
        _autoFit = false;
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    private void BeginSelect(Point screenPoint, PointF imagePoint)
    {
        if (_selected != null)
        {
            int handle = HitHandleScreen(screenPoint);
            if (handle >= 0)
            {
                _pendingSnapshot = CreateSnapshot();
                _dragOriginal = _selected.Clone();
                _selectedHandle = handle;
                _dragStartImage = imagePoint;
                _drag = DragMode.ResizeAnnotation;
                _movedSinceDown = false;
                Capture = true;
                UpdateCursor(screenPoint);
                return;
            }
        }

        var hit = HitAnnotation(imagePoint);
        if (hit != null)
        {
            SelectAnnotationInternal(hit);
            _pendingSnapshot = CreateSnapshot();
            _dragOriginal = hit.Clone();
            _dragStartImage = imagePoint;
            _drag = DragMode.MoveAnnotation;
            _movedSinceDown = false;
            Capture = true;
            UpdateCursor(screenPoint);
            return;
        }

        SelectAnnotationInternal(null);
        Invalidate();
    }

    private void BeginDraw(PointF imagePoint)
    {
        if (!InCrop(imagePoint))
        {
            if (_tool != EditorTool.Select && _selected != null) SelectAnnotationInternal(null);
            return;
        }

        _pendingSnapshot = CreateSnapshot();
        _dragStartImage = ClampToCrop(imagePoint);
        _dragCurrentImage = _dragStartImage;
        _preview = null;
        _freehandPoints = null;
        _movedSinceDown = false;

        if (_tool is EditorTool.Pen or EditorTool.Highlighter)
        {
            _freehandPoints = new List<PointF> { _dragStartImage };
            _preview = CreateFreehandPreview();
        }

        _drag = DragMode.Create;
        Capture = true;
        Invalidate();
    }

    private void UpdatePreview(PointF imagePoint)
    {
        var current = ClampToCrop(imagePoint);
        _dragCurrentImage = current;
        _movedSinceDown = true;

        if (_tool is EditorTool.Pen or EditorTool.Highlighter)
        {
            if (_freehandPoints == null) _freehandPoints = new List<PointF>();
            var last = _freehandPoints.Count > 0 ? _freehandPoints[^1] : current;
            if (GeometryUtil.Distance(last, current) >= Math.Max(0.7f, 1.2f / _zoom))
            {
                _freehandPoints.Add(current);
                _preview = CreateFreehandPreview();
            }
            Invalidate();
            return;
        }

        var rect = GeometryUtil.FromPoints(_dragStartImage, current);
        _preview = _tool switch
        {
            EditorTool.Line => Annotation.CreateLine(_dragStartImage, current, _color, _strokeWidth, arrow: false),
            EditorTool.Arrow => Annotation.CreateLine(_dragStartImage, current, _color, _strokeWidth, arrow: true),
            EditorTool.Rectangle => Annotation.CreateShape(AnnotationKind.Rectangle, rect, _color, _strokeWidth, _filled),
            EditorTool.Ellipse => Annotation.CreateShape(AnnotationKind.Ellipse, rect, _color, _strokeWidth, _filled),
            EditorTool.Pixelate => Annotation.CreateRedaction(AnnotationKind.Pixelate, rect, _color, _pixelBlock),
            EditorTool.Blur => Annotation.CreateRedaction(AnnotationKind.Blur, rect, _color, _blurRadius),
            EditorTool.Redact => Annotation.CreateRedaction(AnnotationKind.Redact, rect, _redactColor, 1),
            _ => _preview
        };
        Invalidate();
    }

    private Annotation CreateFreehandPreview()
    {
        bool highlighter = _tool == EditorTool.Highlighter;
        var color = highlighter ? Color.FromArgb(95, _highlighterColor) : _color;
        return Annotation.CreateFreehand(
            highlighter ? AnnotationKind.Highlighter : AnnotationKind.Pen,
            _freehandPoints ?? new List<PointF>(),
            color,
            highlighter ? _highlighterWidth : _strokeWidth);
    }

    private void FinalizePreview()
    {
        var preview = _preview;
        var snapshot = _pendingSnapshot;
        _preview = null;
        _freehandPoints = null;
        _pendingSnapshot = null;

        if (preview == null || snapshot == null) return;
        if (!IsPreviewValid(preview))
        {
            Invalidate();
            return;
        }

        _history.Push(snapshot);
        _annotations.Add(preview);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage?.Invoke(this, $"{EditorToolInfo.Name(_tool)} added");
        SelectAnnotationInternal(null);
        Invalidate();
    }

    private bool IsPreviewValid(Annotation preview)
    {
        return preview.Kind switch
        {
            AnnotationKind.Pen or AnnotationKind.Highlighter => preview.Points.Count >= 2 &&
                (preview.GetBounds().Width > 1.5f || preview.GetBounds().Height > 1.5f),
            AnnotationKind.Line or AnnotationKind.Arrow => GeometryUtil.Distance(preview.Start, preview.End) >= 3f,
            _ => preview.GetBounds().Width >= 2f && preview.GetBounds().Height >= 2f
        };
    }

    private void UpdateMove(PointF imagePoint)
    {
        if (_selected == null || _dragOriginal == null) return;
        float dx = imagePoint.X - _dragStartImage.X;
        float dy = imagePoint.Y - _dragStartImage.Y;
        if (!_movedSinceDown && Math.Abs(dx) * _zoom < 1f && Math.Abs(dy) * _zoom < 1f) return;

        if (!_movedSinceDown)
        {
            if (_pendingSnapshot != null) _history.Push(_pendingSnapshot);
            _pendingSnapshot = null;
            _movedSinceDown = true;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        _selected.CopyFrom(_dragOriginal);
        _selected.MoveBy(dx, dy);
        Invalidate();
    }

    private void UpdateResize(PointF imagePoint)
    {
        if (_selected == null || _dragOriginal == null) return;
        if (!_movedSinceDown &&
            Math.Abs(imagePoint.X - _dragStartImage.X) * _zoom < 1f &&
            Math.Abs(imagePoint.Y - _dragStartImage.Y) * _zoom < 1f) return;

        if (!_movedSinceDown)
        {
            if (_pendingSnapshot != null) _history.Push(_pendingSnapshot);
            _pendingSnapshot = null;
            _movedSinceDown = true;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        _selected.CopyFrom(_dragOriginal);
        _selected.MoveHandle(_selectedHandle, imagePoint);
        Invalidate();
    }

    private void BeginCrop(PointF imagePoint)
    {
        if (!InCrop(imagePoint)) return;
        _pendingSnapshot = CreateSnapshot();
        _dragStartImage = ClampToCrop(imagePoint);
        _dragCurrentImage = _dragStartImage;
        _cropDragRect = new RectangleF(_dragStartImage, SizeF.Empty);
        _drag = DragMode.Crop;
        Capture = true;
        Invalidate();
    }

    private void UpdateCrop(PointF imagePoint)
    {
        _dragCurrentImage = ClampToCrop(imagePoint);
        _cropDragRect = GeometryUtil.FromPoints(_dragStartImage, _dragCurrentImage);
        Invalidate();
    }

    private void ApplyCrop()
    {
        var snapshot = _pendingSnapshot;
        _pendingSnapshot = null;
        var rect = Rectangle.Intersect(_crop, Rectangle.Round(_cropDragRect));
        _cropDragRect = RectangleF.Empty;
        if (snapshot == null || rect.Width < 8 || rect.Height < 8 || rect == _crop)
        {
            Invalidate();
            return;
        }

        _history.Push(snapshot);
        _crop = rect;
        SelectAnnotationInternal(null);
        SetTool(EditorTool.Select);
        _autoFit = true;
        FitToWindow();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
        MarkChanged();
        StatusMessage?.Invoke(this, $"Cropped to {_crop.Width} × {_crop.Height} — Ctrl+Z to undo");
    }

    private void AddStep(PointF imagePoint)
    {
        int number = NextStepNumber;
        PushUndo();
        var step = Annotation.CreateStep(ClampToCrop(imagePoint), number, _stepColor, _stepSize);
        _annotations.Add(step);
        MarkChanged();
        OptionsChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage?.Invoke(this, $"Step {number} added");
    }

    private void BeginTextEditing(PointF imageAnchor, Annotation? existing)
    {
        if (_textPanel != null) CommitTextEditor();
        _textAnchor = existing != null ? new PointF(existing.Bounds.X, existing.Bounds.Y) : imageAnchor;
        _editingAnnotation = existing;
        _textEditSnapshot = CreateSnapshot();

        float fontSize = existing?.FontSize ?? _fontSize;
        bool bold = existing?.FontBold ?? _fontBold;
        var font = CreateTextBoxFont(fontSize, bold);
        var accent = existing?.Color ?? _color;

        var panel = new TextEditorPanel { AccentColor = Opaque(accent) };
        var box = new TextBox
        {
            Multiline = true,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = font,
            ScrollBars = ScrollBars.None,
            AcceptsReturn = true,
            AcceptsTab = false,
            Text = existing?.Text ?? string.Empty
        };
        panel.Controls.Add(box);
        Controls.Add(panel);
        panel.BringToFront();

        _textPanel = panel;
        _textBox = box;

        box.TextChanged += (_, _) => AutoSizeTextEditor();
        box.KeyDown += TextBox_KeyDown;
        box.Leave += (_, _) =>
        {
            if (!_committingText && _textPanel != null)
                CommitTextEditor();
        };

        AutoSizeTextEditor();
        Invalidate();
        BeginInvoke(new Action(() =>
        {
            if (_textBox != null && !_textBox.IsDisposed)
            {
                _textBox.Focus();
                _textBox.SelectionStart = _textBox.TextLength;
            }
        }));
    }

    private Font CreateTextBoxFont(float imagePixelSize, bool bold)
    {
        float screenPixels = Math.Clamp(imagePixelSize * _zoom, 11f, 180f);
        float points = screenPixels * 72f / Math.Max(96, DeviceDpi);
        return new Font("Segoe UI", points, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
    }

    private void AutoSizeTextEditor()
    {
        if (_textPanel == null || _textBox == null) return;
        var font = _textBox.Font;
        var lines = (_textBox.Text.Length == 0 ? "Type text…" : _textBox.Text)
            .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int width = 150;
        foreach (var line in lines)
        {
            var measured = TextRenderer.MeasureText(line.Length == 0 ? " " : line, font,
                new Size(2400, 400), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            width = Math.Max(width, measured.Width);
        }
        int lineHeight = font.Height + 1;
        int height = Math.Max(26, lines.Length * lineHeight);

        int maxWidth = Math.Max(150, ClientSize.Width - 24);
        width = Math.Min(width, maxWidth);
        int panelWidth = width + _textPanel.Padding.Horizontal + 8;
        int panelHeight = height + _textPanel.Padding.Vertical + 6;
        _textPanel.Size = new Size(panelWidth, panelHeight);
        _textBox.Size = new Size(
            Math.Max(80, _textPanel.ClientSize.Width - _textPanel.Padding.Horizontal),
            Math.Max(20, _textPanel.ClientSize.Height - _textPanel.Padding.Vertical));
        _textBox.Location = new Point(_textPanel.Padding.Left, _textPanel.Padding.Top);

        var anchor = ImageToScreen(_textAnchor);
        int left = (int)Math.Round(anchor.X);
        int top = (int)Math.Round(anchor.Y);
        left = Math.Clamp(left, 4, Math.Max(4, ClientSize.Width - _textPanel.Width - 4));
        top = Math.Clamp(top, 4, Math.Max(4, ClientSize.Height - _textPanel.Height - 4));
        _textPanel.Location = new Point(left, top);
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            CommitTextEditor();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            CancelTextEditor();
            e.SuppressKeyPress = true;
        }
    }

    private void CommitTextEditor()
    {
        if (_committingText || _textPanel == null || _textBox == null) return;
        _committingText = true;
        try
        {
            string text = _textBox.Text.TrimEnd('\r', '\n');
            var existing = _editingAnnotation;
            var snapshot = _textEditSnapshot;
            var panel = _textPanel;
            var box = _textBox;
            _textPanel = null;
            _textBox = null;
            _editingAnnotation = null;
            _textEditSnapshot = null;

            Controls.Remove(panel);
            panel.Dispose();
            box.Dispose();

            bool changed = false;
            if (existing != null)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (snapshot != null)
                    {
                        _history.Push(snapshot);
                        _annotations.Remove(existing);
                        HistoryChanged?.Invoke(this, EventArgs.Empty);
                        changed = true;
                        StatusMessage?.Invoke(this, "Text deleted");
                    }
                    SelectAnnotationInternal(null);
                }
                else if (!string.Equals(existing.Text, text, StringComparison.Ordinal))
                {
                    if (snapshot != null)
                    {
                        _history.Push(snapshot);
                        HistoryChanged?.Invoke(this, EventArgs.Empty);
                    }
                    existing.Text = text;
                    existing.UpdateTextLayout(new PointF(existing.Bounds.X, existing.Bounds.Y));
                    changed = true;
                    SelectAnnotationInternal(existing);
                    StatusMessage?.Invoke(this, "Text updated");
                }
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                if (snapshot != null)
                {
                    _history.Push(snapshot);
                    HistoryChanged?.Invoke(this, EventArgs.Empty);
                }
                var annotation = Annotation.CreateText(_textAnchor, text, _color, _fontSize, _fontBold, _textBackground);
                _annotations.Add(annotation);
                changed = true;
                SelectAnnotationInternal(null);
                StatusMessage?.Invoke(this, "Text added");
            }

            if (changed) DocumentChanged?.Invoke(this, EventArgs.Empty);
            OptionsChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
        finally
        {
            _committingText = false;
            if (!IsDisposed && IsHandleCreated) Focus();
        }
    }

    private void CancelTextEditor()
    {
        if (_textPanel == null) return;
        _committingText = true;
        try
        {
            var panel = _textPanel;
            var box = _textBox;
            _textPanel = null;
            _textBox = null;
            _editingAnnotation = null;
            _textEditSnapshot = null;
            Controls.Remove(panel);
            panel.Dispose();
            box?.Dispose();
            Invalidate();
        }
        finally
        {
            _committingText = false;
            Focus();
        }
    }

    private Annotation? HitAnnotation(PointF imagePoint)
    {
        float tolerance = 6f / Math.Max(_zoom, 0.05f);
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            var a = _annotations[i];
            if (ReferenceEquals(a, _editingAnnotation)) continue;
            if (a.HitTest(imagePoint, tolerance)) return a;
        }
        return null;
    }

    private int HitHandleScreen(Point screenPoint)
    {
        if (_selected == null) return -1;
        var handles = _selected.GetHandles();
        float tolerance = HandleSize / 2f + 2f;
        for (int i = 0; i < handles.Length; i++)
        {
            var p = ImageToScreen(handles[i]);
            if (Math.Abs(p.X - screenPoint.X) <= tolerance && Math.Abs(p.Y - screenPoint.Y) <= tolerance)
                return i;
        }
        return -1;
    }

    private void UpdateCursor(Point screenPoint)
    {
        if (_drag == DragMode.Pan || _drag == DragMode.MoveAnnotation)
        {
            Cursor = Cursors.SizeAll;
            return;
        }
        if (_drag == DragMode.ResizeAnnotation)
        {
            Cursor = CursorForHandle(_selectedHandle, _selected?.GetHandles().Length ?? 0);
            return;
        }
        if (_drag == DragMode.Create || _drag == DragMode.Crop)
        {
            Cursor = Cursors.Cross;
            return;
        }
        if (_spaceDown)
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        if (_tool == EditorTool.Select)
        {
            int handle = HitHandleScreen(screenPoint);
            if (handle >= 0)
            {
                Cursor = CursorForHandle(handle, _selected?.GetHandles().Length ?? 0);
                return;
            }
            var imagePoint = ScreenToImage(screenPoint);
            Cursor = HitAnnotation(imagePoint) != null ? Cursors.SizeAll : Cursors.Default;
            return;
        }

        Cursor = _tool == EditorTool.Text ? Cursors.IBeam : Cursors.Cross;
    }

    private static Cursor CursorForHandle(int handle, int count)
    {
        if (count <= 2) return Cursors.SizeAll;
        return handle switch
        {
            0 or 4 => Cursors.SizeNWSE,
            2 or 6 => Cursors.SizeNESW,
            1 or 5 => Cursors.SizeNS,
            _ => Cursors.SizeWE
        };
    }

    private void PushUndo()
    {
        _history.Push(CreateSnapshot());
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private EditorSnapshot CreateSnapshot()
        => new()
        {
            Annotations = _annotations.Select(a => a.Clone()).ToList(),
            CropRect = _crop,
            ImageBounds = ImageBounds
        };

    private void ApplySnapshot(EditorSnapshot snapshot)
    {
        _annotations.Clear();
        _annotations.AddRange(snapshot.Annotations);
        _crop = Rectangle.Intersect(snapshot.CropRect, ImageBounds);
        if (_crop.Width <= 0 || _crop.Height <= 0) _crop = ImageBounds;
        _selected = null;
        _selectedHandle = -1;
        _preview = null;
        _freehandPoints = null;
        _drag = DragMode.None;
        _pendingSnapshot = null;
        _dragOriginal = null;
        _movedSinceDown = false;
        DisposeCache();
    }

    private void MarkChanged()
    {
        Invalidate();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CancelDragState()
    {
        _drag = DragMode.None;
        _preview = null;
        _freehandPoints = null;
        _cropDragRect = RectangleF.Empty;
        _pendingSnapshot = null;
        _dragOriginal = null;
        _movedSinceDown = false;
        if (Capture) Capture = false;
    }

    private bool InCrop(PointF p)
        => p.X >= _crop.Left && p.X <= _crop.Right && p.Y >= _crop.Top && p.Y <= _crop.Bottom;

    private PointF ClampToCrop(PointF p)
        => new(Math.Clamp(p.X, _crop.Left, _crop.Right), Math.Clamp(p.Y, _crop.Top, _crop.Bottom));

    private void ClampOffset()
    {
        float w = _crop.Width * _zoom;
        float h = _crop.Height * _zoom;
        float minX = -w + 60;
        float maxX = ClientSize.Width - 60;
        _offset.X = minX > maxX ? (ClientSize.Width - w) / 2f : Math.Clamp(_offset.X, minX, maxX);
        float minY = -h + 60;
        float maxY = ClientSize.Height - 60;
        _offset.Y = minY > maxY ? (ClientSize.Height - h) / 2f : Math.Clamp(_offset.Y, minY, maxY);
    }

    private void DisposeCache()
    {
        _cache?.Dispose();
        _cache = null;
        _cacheZoom = 0;
        _cacheCrop = Rectangle.Empty;
    }

    private static Color Opaque(Color c) => Color.FromArgb(255, c.R, c.G, c.B);

    private static bool SupportsStroke(AnnotationKind kind)
        => kind is AnnotationKind.Pen or AnnotationKind.Highlighter or AnnotationKind.Line or AnnotationKind.Arrow
            or AnnotationKind.Rectangle or AnnotationKind.Ellipse;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _committingText = true;
            _textPanel?.Dispose();
            _textPanel = null;
            _textBox = null;
            DisposeCache();
        }
        base.Dispose(disposing);
    }
}
