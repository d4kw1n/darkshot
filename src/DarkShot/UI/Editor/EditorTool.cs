namespace DarkShot.UI.Editor;

public enum EditorTool
{
    Select,
    Pen,
    Highlighter,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
    Step,
    Pixelate,
    Blur,
    Redact,
    Crop
}

public static class EditorToolInfo
{
    public static IconKind Icon(EditorTool tool) => tool switch
    {
        EditorTool.Select => IconKind.Cursor,
        EditorTool.Pen => IconKind.Pen,
        EditorTool.Highlighter => IconKind.Highlighter,
        EditorTool.Line => IconKind.Line,
        EditorTool.Arrow => IconKind.Arrow,
        EditorTool.Rectangle => IconKind.Rectangle,
        EditorTool.Ellipse => IconKind.Ellipse,
        EditorTool.Text => IconKind.Text,
        EditorTool.Step => IconKind.Step,
        EditorTool.Pixelate => IconKind.Pixelate,
        EditorTool.Blur => IconKind.Blur,
        EditorTool.Redact => IconKind.Redact,
        EditorTool.Crop => IconKind.Crop,
        _ => IconKind.Cursor
    };

    public static string Name(EditorTool tool) => tool switch
    {
        EditorTool.Select => "Select",
        EditorTool.Pen => "Pen",
        EditorTool.Highlighter => "Highlighter",
        EditorTool.Line => "Line",
        EditorTool.Arrow => "Arrow",
        EditorTool.Rectangle => "Rectangle",
        EditorTool.Ellipse => "Ellipse",
        EditorTool.Text => "Text",
        EditorTool.Step => "Step number",
        EditorTool.Pixelate => "Pixelate",
        EditorTool.Blur => "Blur",
        EditorTool.Redact => "Redact",
        EditorTool.Crop => "Crop",
        _ => tool.ToString()
    };

    public static string Shortcut(EditorTool tool) => tool switch
    {
        EditorTool.Select => "V",
        EditorTool.Pen => "P",
        EditorTool.Highlighter => "H",
        EditorTool.Line => "L",
        EditorTool.Arrow => "A",
        EditorTool.Rectangle => "R",
        EditorTool.Ellipse => "O",
        EditorTool.Text => "T",
        EditorTool.Step => "N",
        EditorTool.Pixelate => "M",
        EditorTool.Blur => "B",
        EditorTool.Redact => "D",
        EditorTool.Crop => "C",
        _ => ""
    };

    public static string Tooltip(EditorTool tool)
        => $"{Name(tool)}  ({Shortcut(tool)})";

    public static bool IsDrawTool(EditorTool tool)
        => tool is EditorTool.Pen or EditorTool.Highlighter or EditorTool.Line or EditorTool.Arrow
            or EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Pixelate or EditorTool.Blur
            or EditorTool.Redact;
}
