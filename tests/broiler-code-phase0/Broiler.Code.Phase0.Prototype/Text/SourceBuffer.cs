namespace Broiler.Code.Phase0.Prototype.Text;

/// <summary>
/// The sole authority for a document's text: edit transactions, versions, and
/// undo/redo. The editor control never mutates an independent copy — it submits
/// versioned intents here and renders the accepted snapshot.
/// </summary>
public sealed class SourceBuffer
{
    private readonly Stack<UndoEntry> _undo = new();
    private readonly Stack<UndoEntry> _redo = new();

    public SourceBuffer(string text)
    {
        Current = TextSnapshot.Create(text);
        SavedVersion = Current.Version;
    }

    /// <summary>Fired after a transaction is accepted, with the change that produced it.</summary>
    public event Action<TextSnapshot, TextChange>? Changed;

    public TextSnapshot Current { get; private set; }

    /// <summary>Version last written to storage; drives the dirty overlay.</summary>
    public int SavedVersion { get; private set; }

    public bool IsDirty => Current.Version != SavedVersion;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public EditResult Apply(EditTransaction transaction)
    {
        if (transaction.BaseVersion != Current.Version)
            return EditResult.Rejected(Current, EditRejectionReason.StaleBaseVersion);

        TextChange change = transaction.Change;
        if (change.Start < 0 || change.OldLength < 0 ||
            change.Start + change.OldLength > Current.Length)
        {
            return EditResult.Rejected(Current, EditRejectionReason.OutOfRange);
        }

        string replaced = Current.Text.Substring(change.Start, change.OldLength);
        TextSnapshot next = Current.WithChange(change);
        _undo.Push(new UndoEntry(transaction.Name, change, replaced));
        _redo.Clear();
        Current = next;
        Changed?.Invoke(next, change);
        return EditResult.Applied(next);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;

        UndoEntry entry = _undo.Pop();
        var inverse = new TextChange(entry.Change.Start, entry.Change.NewLength, entry.ReplacedText);
        TextSnapshot next = Current.WithChange(inverse);
        _redo.Push(entry);
        Current = next;
        Changed?.Invoke(next, inverse);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        UndoEntry entry = _redo.Pop();
        TextSnapshot next = Current.WithChange(entry.Change);
        _undo.Push(entry);
        Current = next;
        Changed?.Invoke(next, entry.Change);
        return true;
    }

    public void MarkSaved() => SavedVersion = Current.Version;

    private readonly record struct UndoEntry(string Name, TextChange Change, string ReplacedText);
}
