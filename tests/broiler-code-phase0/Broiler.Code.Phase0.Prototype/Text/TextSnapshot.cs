namespace Broiler.Code.Phase0.Prototype.Text;

/// <summary>
/// An immutable view of one source document at one version. Every analysis
/// result names the snapshot it was produced from; a result may only be applied
/// while that exact snapshot is still current.
/// </summary>
public sealed class TextSnapshot
{
    private readonly int[] _lineStarts;

    private TextSnapshot(string text, int version, int[] lineStarts)
    {
        Text = text;
        Version = version;
        _lineStarts = lineStarts;
    }

    /// <summary>The full text. Immutable for the lifetime of the snapshot.</summary>
    public string Text { get; }

    /// <summary>
    /// Monotonic per-document version. Never reused, never decremented, and not
    /// comparable across documents.
    /// </summary>
    public int Version { get; }

    public int Length => Text.Length;

    public int LineCount => _lineStarts.Length;

    public static TextSnapshot Create(string text, int version = 0) =>
        new(text, version, BuildLineStarts(text));

    public int GetLineStart(int line) => _lineStarts[line];

    /// <summary>Length of the line excluding its terminator.</summary>
    public int GetLineLength(int line)
    {
        int start = _lineStarts[line];
        int end = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] : Text.Length;
        // Trim the terminator the line-start table stepped over.
        if (end > start && Text[end - 1] == '\n')
        {
            end--;
            if (end > start && Text[end - 1] == '\r')
                end--;
        }
        else if (end > start && Text[end - 1] == '\r')
        {
            end--;
        }

        return end - start;
    }

    public ReadOnlySpan<char> GetLineSpan(int line) =>
        Text.AsSpan(_lineStarts[line], GetLineLength(line));

    /// <summary>
    /// Zero-based line containing <paramref name="position"/>. A position at the
    /// very end of the text belongs to the last line.
    /// </summary>
    public int GetLineFromPosition(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Text.Length);

        int low = 0;
        int high = _lineStarts.Length - 1;
        while (low < high)
        {
            int middle = low + ((high - low + 1) / 2);
            if (_lineStarts[middle] <= position)
                low = middle;
            else
                high = middle - 1;
        }

        return low;
    }

    /// <summary>Zero-based line and column, for the normalized diagnostic model.</summary>
    public (int Line, int Column) GetLineAndColumn(int position)
    {
        int line = GetLineFromPosition(position);
        return (line, position - _lineStarts[line]);
    }

    /// <summary>
    /// Produces the successor snapshot for one replacement. The caller is
    /// <see cref="SourceBuffer"/>; nothing else may create a successor, because
    /// version identity is the buffer's to hand out.
    /// </summary>
    internal TextSnapshot WithChange(TextChange change)
    {
        string text = string.Concat(
            Text.AsSpan(0, change.Start),
            change.NewText,
            Text.AsSpan(change.Start + change.OldLength));
        return new TextSnapshot(text, Version + 1, BuildLineStarts(text));
    }

    private static int[] BuildLineStarts(string text)
    {
        // One entry per line; a trailing terminator yields a final empty line,
        // which is what an editor shows and what line/column arithmetic needs.
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
                lines++;
            else if (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))
                lines++;
        }

        var starts = new int[lines];
        int next = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                starts[next++] = i + 1;
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    continue;
                starts[next++] = i + 1;
            }
        }

        return starts;
    }
}
