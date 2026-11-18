using System.Drawing;

namespace DarkShot.Annotations;

public sealed class EditorSnapshot
{
    public required List<Annotation> Annotations { get; init; }
    public required Rectangle CropRect { get; init; }
    public required Rectangle ImageBounds { get; init; }

    public EditorSnapshot Clone() => new()
    {
        Annotations = Annotations.Select(a => a.Clone()).ToList(),
        CropRect = CropRect,
        ImageBounds = ImageBounds
    };
}

public sealed class UndoHistory
{
    private readonly List<EditorSnapshot> _undo = new();
    private readonly List<EditorSnapshot> _redo = new();
    private readonly int _limit;

    public UndoHistory(int limit = 100) => _limit = Math.Clamp(limit, 10, 500);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Push(EditorSnapshot snapshot)
    {
        _undo.Add(snapshot.Clone());
        if (_undo.Count > _limit) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public EditorSnapshot? Undo(EditorSnapshot current)
    {
        if (_undo.Count == 0) return null;
        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current.Clone());
        return snapshot;
    }

    public EditorSnapshot? Redo(EditorSnapshot current)
    {
        if (_redo.Count == 0) return null;
        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current.Clone());
        return snapshot;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
