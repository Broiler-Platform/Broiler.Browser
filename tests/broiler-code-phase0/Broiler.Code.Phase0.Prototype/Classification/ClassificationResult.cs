using Broiler.Code.Phase0.Prototype.Text;

namespace Broiler.Code.Phase0.Prototype.Classification;

/// <summary>
/// One classified range. <see cref="Start"/> is relative to the start of its
/// line, not to the document: line-relative offsets survive an edit on another
/// line unchanged, so unaffected lines are reused by reference instead of being
/// rewritten with shifted positions.
/// </summary>
public readonly record struct ClassificationSpan(int Start, int Length, ClassificationKind Kind);

/// <summary>
/// The lexer state at the start of a line. Two lines with equal start state
/// classify identically for identical text, which is what lets incremental
/// classification stop as soon as the state reconverges.
/// </summary>
public readonly record struct LineState(LineStateKind Kind, byte QuoteCount, byte DollarCount)
{
    public static readonly LineState Default = new(LineStateKind.Default, 0, 0);
}

public enum LineStateKind : byte
{
    Default = 0,
    BlockComment,
    DocumentationBlockComment,
    VerbatimString,
    RawString,
}

/// <summary>Classifications for one line, plus the state it started in.</summary>
public sealed class LineClassification
{
    public static readonly ClassificationSpan[] NoSpans = [];

    public LineClassification(LineState startState, ClassificationSpan[] spans)
    {
        StartState = startState;
        Spans = spans;
    }

    public LineState StartState { get; }

    public ClassificationSpan[] Spans { get; }
}

/// <summary>
/// Classifications for one exact snapshot. It carries the snapshot rather than
/// a version number so a stale result cannot be mistaken for a current one by
/// an integer comparison that happens to match across documents.
/// </summary>
public sealed class ClassificationResult
{
    public ClassificationResult(
        TextSnapshot snapshot,
        LineClassification[] lines,
        int linesLexed,
        bool wasIncremental)
    {
        Snapshot = snapshot;
        Lines = lines;
        LinesLexed = linesLexed;
        WasIncremental = wasIncremental;
    }

    public TextSnapshot Snapshot { get; }

    public LineClassification[] Lines { get; }

    /// <summary>
    /// How many lines the lexer actually visited. The Phase 0 full-reparse
    /// budget is stated against this, not against elapsed time, because it is
    /// the only figure that is identical on every machine.
    /// </summary>
    public int LinesLexed { get; }

    public bool WasIncremental { get; }

    public int TotalSpans
    {
        get
        {
            int total = 0;
            foreach (LineClassification line in Lines)
                total += line.Spans.Length;
            return total;
        }
    }
}
