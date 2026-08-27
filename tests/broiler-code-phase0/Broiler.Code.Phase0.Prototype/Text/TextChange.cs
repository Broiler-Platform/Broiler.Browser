namespace Broiler.Code.Phase0.Prototype.Text;

/// <summary>One replacement inside a snapshot: replace <paramref name="OldLength"/>
/// characters at <paramref name="Start"/> with <paramref name="NewText"/>.</summary>
public readonly record struct TextChange(int Start, int OldLength, string NewText)
{
    public int NewLength => NewText.Length;

    public int Delta => NewText.Length - OldLength;

    /// <summary>End of the changed region in the successor snapshot.</summary>
    public int NewEnd => Start + NewText.Length;

    public static TextChange Insert(int position, string text) => new(position, 0, text);

    public static TextChange Delete(int position, int length) => new(position, length, string.Empty);
}

/// <summary>
/// An edit transaction names the version it was composed against. The buffer
/// either produces exactly one successor snapshot or rejects the transaction as
/// stale; it never rebases silently.
/// </summary>
public readonly record struct EditTransaction(int BaseVersion, TextChange Change, string Name);

public enum EditRejectionReason
{
    None = 0,
    StaleBaseVersion,
    OutOfRange,
}

public readonly record struct EditResult(
    bool Accepted,
    TextSnapshot Snapshot,
    EditRejectionReason Reason)
{
    public static EditResult Rejected(TextSnapshot current, EditRejectionReason reason) =>
        new(false, current, reason);

    public static EditResult Applied(TextSnapshot snapshot) =>
        new(true, snapshot, EditRejectionReason.None);
}
