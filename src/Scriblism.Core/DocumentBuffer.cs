using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Scriblism.Core;

public sealed record EditResult(string Text, int Caret);
internal sealed record TextEdit(int Start, string Removed, string Inserted, int BeforeCaret, int AfterCaret, DateTime At);

/// <summary>Text-only undo. Presentation formatting never becomes an undo step.</summary>
public sealed class DocumentBuffer
{
    private readonly List<TextEdit> _undo = [];
    private readonly List<TextEdit> _redo = [];
    private byte[] _savedHash;
    private int _savedLength;
    private int _lastCaret;
    private bool _breakGroup;
    private long _historyBytes;
    private bool _isDirty;
    public string Text { get; private set; }
    public bool IsDirty => _isDirty;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int Revision { get; private set; }
    public DocumentBuffer(string text = "")
    {
        Text = text;
        _savedLength = text.Length;
        _savedHash = Hash(text);
    }
    public void MarkSaved(string savedText)
    {
        _savedLength = savedText.Length;
        _savedHash = Hash(savedText);
        _isDirty = DiffersFromSaved();
        BreakUndoGroup();
    }
    private static byte[] Hash(string text) => SHA256.HashData(MemoryMarshal.AsBytes(text.AsSpan()));
    private bool DiffersFromSaved() => Text.Length != _savedLength || !Hash(Text).AsSpan().SequenceEqual(_savedHash);
    public void BreakUndoGroup() => _breakGroup = true;
    public void RememberCaret(int caret) => _lastCaret = Math.Clamp(caret, 0, Text.Length);

    public bool Update(string next, int caret, bool groupTyping = true)
    {
        if (next == Text) return false;
        var start = 0;
        while (start < Text.Length && start < next.Length && Text[start] == next[start]) start++;
        var oldEnd = Text.Length; var newEnd = next.Length;
        while (oldEnd > start && newEnd > start && Text[oldEnd - 1] == next[newEnd - 1]) { oldEnd--; newEnd--; }
        var change = new TextEdit(start, Text[start..oldEnd], next[start..newEnd], _lastCaret, caret, DateTime.UtcNow);
        if (!_breakGroup && groupTyping && _undo.Count > 0 && change.Removed.Length == 0 && change.Inserted.Length <= 4 && !change.Inserted.Contains('\n'))
        {
            var last = _undo[^1];
            if (last.Removed.Length == 0 && last.Start + last.Inserted.Length == change.Start &&
                (change.At - last.At).TotalMilliseconds < 750 && !last.Inserted.Contains('\n'))
            {
                _undo[^1] = last with { Inserted = last.Inserted + change.Inserted, AfterCaret = caret, At = change.At };
                _historyBytes += change.Inserted.Length * 2L;
            }
            else AddUndo(change);
        }
        else AddUndo(change);
        _breakGroup = false;
        _redo.Clear();
        while (_undo.Count > 1 && (_undo.Count > 1000 || _historyBytes > 32 * 1024 * 1024))
        {
            _historyBytes -= (_undo[0].Removed.Length + _undo[0].Inserted.Length) * 2L;
            _undo.RemoveAt(0);
        }
        Text = next; _isDirty = DiffersFromSaved(); _lastCaret = caret; Revision++;
        return true;
    }

    private void AddUndo(TextEdit change) { _undo.Add(change); _historyBytes += (change.Removed.Length + change.Inserted.Length) * 2L; }
    public EditResult Undo()
    {
        if (_undo.Count == 0) return new(Text, _lastCaret);
        var edit = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        _historyBytes -= (edit.Removed.Length + edit.Inserted.Length) * 2L;
        Text = Text.Remove(edit.Start, edit.Inserted.Length).Insert(edit.Start, edit.Removed);
        _isDirty = DiffersFromSaved();
        _redo.Add(edit); _lastCaret = Math.Clamp(edit.BeforeCaret, 0, Text.Length); Revision++; BreakUndoGroup();
        return new(Text, _lastCaret);
    }
    public EditResult Redo()
    {
        if (_redo.Count == 0) return new(Text, _lastCaret);
        var edit = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        Text = Text.Remove(edit.Start, edit.Removed.Length).Insert(edit.Start, edit.Inserted);
        _isDirty = DiffersFromSaved();
        AddUndo(edit); _lastCaret = Math.Clamp(edit.AfterCaret, 0, Text.Length); Revision++; BreakUndoGroup();
        return new(Text, _lastCaret);
    }
}
