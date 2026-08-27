using Broiler.Code.Phase0.Prototype.Text;

namespace Broiler.Code.Phase0.Prototype.Classification;

/// <summary>
/// The Roslyn-free portable C# classifier, composed into every host including
/// the ones that cannot afford the semantic service. It is a line-oriented
/// lexer: each line is classified from the state the previous line ended in, so
/// an edit only forces relexing from the first changed line until the state
/// reconverges.
///
/// Known approximations, all deliberate. The semantic service is authoritative
/// where they differ, and a host running on this classifier alone reports
/// classification-only mode:
///
/// <list type="bullet">
/// <item>Contextual keywords are limited to an unambiguous subset. A local
/// variable named <c>value</c> or <c>from</c> stays an identifier rather than
/// being miscolored as a keyword.</item>
/// <item>Interpolation holes are classified as part of their string literal;
/// the expressions inside them are not lexed as code.</item>
/// <item>Identifiers get no span at all, so the editor paints them in the
/// default foreground. Distinguishing types from locals needs binding.</item>
/// <item><c>#if</c> regions are not dimmed: which branch is live depends on the
/// evaluated graph's defines, which this classifier deliberately cannot see.</item>
/// </list>
/// </summary>
public sealed class PortableCSharpClassifier
{
    // No instance state. Cancellation is cooperative, so a superseded run keeps
    // executing until it next checks the token — which means two runs overlap
    // routinely. A scratch buffer on the instance would be shared between them
    // and corrupt both. Each run owns its own.

    /// <summary>Classifies every line. Used for the first result of a document.</summary>
    public ClassificationResult ClassifyAll(TextSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var scratch = new List<ClassificationSpan>(64);
        var lines = new LineClassification[snapshot.LineCount];
        LineState state = LineState.Default;
        for (int i = 0; i < lines.Length; i++)
        {
            if ((i & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            lines[i] = ClassifyLine(scratch, snapshot.GetLineSpan(i), state, out state);
        }

        return new ClassificationResult(snapshot, lines, lines.Length, wasIncremental: false);
    }

    /// <summary>
    /// Classifies <paramref name="next"/> by reusing <paramref name="previous"/>
    /// for every line the change cannot have affected. Lines before the change
    /// are reused directly; lines after it are reused as soon as the recomputed
    /// start state matches the state the old result recorded at the
    /// corresponding line.
    /// </summary>
    public ClassificationResult ClassifyIncremental(
        ClassificationResult previous,
        TextSnapshot next,
        TextChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        if (previous.Snapshot.Version + 1 != next.Version)
            return ClassifyAll(next, cancellationToken);

        // Text before the change is identical in both snapshots, so line starts
        // up to the changed line are identical too.
        int firstLine = previous.Snapshot.GetLineFromPosition(change.Start);
        int oldLastLine = previous.Snapshot.GetLineFromPosition(change.Start + change.OldLength);
        int newLastLine = next.GetLineFromPosition(change.NewEnd);
        int lineDelta = next.LineCount - previous.Snapshot.LineCount;

        var scratch = new List<ClassificationSpan>(64);
        var lines = new LineClassification[next.LineCount];
        Array.Copy(previous.Lines, lines, firstLine);

        LineState state = firstLine == 0 ? LineState.Default : previous.Lines[firstLine].StartState;
        int lexed = 0;
        int i = firstLine;
        for (; i < lines.Length; i++)
        {
            if ((lexed & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            // Past the edited region, a matching start state means this line and
            // every line after it classify exactly as they did before.
            if (i > newLastLine)
            {
                int oldIndex = i - lineDelta;
                if (oldIndex > oldLastLine && oldIndex < previous.Lines.Length &&
                    previous.Lines[oldIndex].StartState == state)
                {
                    break;
                }
            }

            lines[i] = ClassifyLine(scratch, next.GetLineSpan(i), state, out state);
            lexed++;
        }

        if (i < lines.Length)
        {
            Array.Copy(previous.Lines, i - lineDelta, lines, i, lines.Length - i);
        }

        return new ClassificationResult(next, lines, lexed, wasIncremental: true);
    }

    private static LineClassification ClassifyLine(
        List<ClassificationSpan> scratch,
        ReadOnlySpan<char> line,
        LineState startState,
        out LineState endState)
    {
        scratch.Clear();
        endState = Lex(line, startState, scratch);
        return new LineClassification(
            startState,
            scratch.Count == 0 ? LineClassification.NoSpans : scratch.ToArray());
    }

    private static LineState Lex(
        ReadOnlySpan<char> line,
        LineState state,
        List<ClassificationSpan> output)
    {
        int i = 0;

        switch (state.Kind)
        {
            case LineStateKind.BlockComment:
            case LineStateKind.DocumentationBlockComment:
            {
                ClassificationKind kind = state.Kind == LineStateKind.BlockComment
                    ? ClassificationKind.Comment
                    : ClassificationKind.DocumentationComment;
                int end = IndexOfBlockCommentEnd(line);
                if (end < 0)
                {
                    Emit(output, 0, line.Length, kind);
                    return state;
                }

                Emit(output, 0, end, kind);
                i = end;
                state = LineState.Default;
                break;
            }

            case LineStateKind.VerbatimString:
            {
                i = ContinueVerbatimString(line, 0, state.DollarCount, output, out bool closed);
                if (!closed)
                    return state;
                state = LineState.Default;
                break;
            }

            case LineStateKind.RawString:
            {
                i = ContinueRawString(line, 0, state.QuoteCount, state.DollarCount, output, out bool closed);
                if (!closed)
                    return state;
                state = LineState.Default;
                break;
            }
        }

        // A directive is only a directive when '#' is the first non-whitespace
        // character of the line and nothing has been lexed on it yet.
        if (output.Count == 0)
        {
            int probe = i;
            while (probe < line.Length && (line[probe] == ' ' || line[probe] == '\t'))
                probe++;
            if (probe < line.Length && line[probe] == '#')
            {
                LexDirective(line, probe, output);
                return LineState.Default;
            }
        }

        while (i < line.Length)
        {
            char c = line[i];

            if (c is ' ' or '\t')
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < line.Length)
            {
                char n = line[i + 1];
                if (n == '/')
                {
                    bool doc = i + 2 < line.Length && line[i + 2] == '/' &&
                        (i + 3 >= line.Length || line[i + 3] != '/');
                    Emit(output, i, line.Length - i,
                        doc ? ClassificationKind.DocumentationComment : ClassificationKind.Comment);
                    return LineState.Default;
                }

                if (n == '*')
                {
                    bool doc = i + 2 < line.Length && line[i + 2] == '*' &&
                        (i + 3 >= line.Length || line[i + 3] != '/');
                    ClassificationKind kind = doc
                        ? ClassificationKind.DocumentationComment
                        : ClassificationKind.Comment;
                    int end = IndexOfBlockCommentEnd(line[(i + 2)..]);
                    if (end < 0)
                    {
                        Emit(output, i, line.Length - i, kind);
                        return new LineState(
                            doc ? LineStateKind.DocumentationBlockComment : LineStateKind.BlockComment,
                            0,
                            0);
                    }

                    Emit(output, i, end + 2, kind);
                    i += end + 2;
                    continue;
                }
            }

            if (c is '"' or '\'' or '@' or '$')
            {
                int consumed = LexLiteral(line, i, output, out LineState pending, out bool isLiteral);
                if (isLiteral)
                {
                    if (pending.Kind != LineStateKind.Default)
                        return pending;
                    i = consumed;
                    continue;
                }
            }

            if (char.IsAsciiDigit(c) ||
                (c == '.' && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1])))
            {
                int start = i;
                i = ScanNumber(line, i);
                Emit(output, start, i - start, ClassificationKind.NumericLiteral);
                continue;
            }

            if (IsIdentifierStart(c))
            {
                int start = i;
                bool verbatim = c == '@';
                if (verbatim)
                    i++;
                while (i < line.Length && IsIdentifierPart(line[i]))
                    i++;
                if (i == start)
                {
                    i++;
                    continue;
                }

                if (!verbatim)
                {
                    ClassificationKind kind = KeywordTable.Lookup(line[start..i]);
                    if (kind != ClassificationKind.None)
                        Emit(output, start, i - start, kind);
                }

                continue;
            }

            if (IsPunctuation(c))
            {
                Emit(output, i, 1, ClassificationKind.Punctuation);
                i++;
                continue;
            }

            if (IsOperator(c))
            {
                int start = i;
                while (i < line.Length && IsOperator(line[i]))
                    i++;
                Emit(output, start, i - start, ClassificationKind.Operator);
                continue;
            }

            i++;
        }

        return LineState.Default;
    }

    private static void LexDirective(ReadOnlySpan<char> line, int hash, List<ClassificationSpan> output)
    {
        int i = hash + 1;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        int nameStart = i;
        while (i < line.Length && char.IsAsciiLetter(line[i]))
            i++;

        // '#' and the directive name read as one keyword, matching how the
        // directive is written and spoken.
        Emit(output, hash, i - hash, ClassificationKind.PreprocessorKeyword);
        if (i > nameStart && i < line.Length)
            Emit(output, i, line.Length - i, ClassificationKind.PreprocessorText);
    }

    /// <summary>
    /// Lexes a string, character, or raw-string literal starting at
    /// <paramref name="i"/>. Sets <paramref name="isLiteral"/> to false when the
    /// character turned out to be an ordinary <c>@</c> or <c>$</c>.
    /// </summary>
    private static int LexLiteral(
        ReadOnlySpan<char> line,
        int i,
        List<ClassificationSpan> output,
        out LineState pending,
        out bool isLiteral)
    {
        pending = LineState.Default;
        isLiteral = true;

        int start = i;
        byte dollars = 0;
        bool verbatim = false;

        while (i < line.Length && (line[i] == '$' || line[i] == '@'))
        {
            if (line[i] == '@')
                verbatim = true;
            else
                dollars++;
            i++;
        }

        if (i >= line.Length)
        {
            isLiteral = false;
            return start;
        }

        char quote = line[i];
        if (quote == '\'' && !verbatim && dollars == 0)
            return LexCharacter(line, i, output);

        if (quote != '"')
        {
            isLiteral = false;
            return start;
        }

        int quoteRun = 0;
        while (i + quoteRun < line.Length && line[i + quoteRun] == '"')
            quoteRun++;

        if (quoteRun >= 3 && !verbatim)
        {
            Emit(output, start, (i - start) + quoteRun, ClassificationKind.StringLiteral);
            int after = ContinueRawString(
                line, i + quoteRun, (byte)quoteRun, dollars, output, out bool rawClosed);
            if (!rawClosed)
            {
                pending = new LineState(LineStateKind.RawString, (byte)quoteRun, dollars);
                return line.Length;
            }

            return after;
        }

        Emit(output, start, (i - start) + 1, ClassificationKind.StringLiteral);
        i++;

        if (verbatim)
        {
            int after = ContinueVerbatimString(line, i, dollars, output, out bool closed);
            if (!closed)
            {
                pending = new LineState(LineStateKind.VerbatimString, 0, dollars);
                return line.Length;
            }

            return after;
        }

        // A regular string never crosses a line: an unterminated one is
        // classified to end of line and the next line starts clean.
        int run = i;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                Flush(output, ref run, i, ClassificationKind.StringLiteral);
                Emit(output, i, 2, ClassificationKind.EscapeSequence);
                i += 2;
                run = i;
                continue;
            }

            if (dollars > 0 && (c == '{' || c == '}') && i + 1 < line.Length && line[i + 1] == c)
            {
                Flush(output, ref run, i, ClassificationKind.StringLiteral);
                Emit(output, i, 2, ClassificationKind.EscapeSequence);
                i += 2;
                run = i;
                continue;
            }

            if (c == '"')
            {
                i++;
                Flush(output, ref run, i, ClassificationKind.StringLiteral);
                return i;
            }

            i++;
        }

        Flush(output, ref run, i, ClassificationKind.StringLiteral);
        return i;
    }

    private static int LexCharacter(ReadOnlySpan<char> line, int i, List<ClassificationSpan> output)
    {
        int start = i;
        i++;
        while (i < line.Length)
        {
            if (line[i] == '\\' && i + 1 < line.Length)
            {
                i += 2;
                continue;
            }

            if (line[i] == '\'')
            {
                i++;
                break;
            }

            i++;
        }

        Emit(output, start, i - start, ClassificationKind.CharacterLiteral);
        return i;
    }

    private static int ContinueVerbatimString(
        ReadOnlySpan<char> line,
        int i,
        byte dollars,
        List<ClassificationSpan> output,
        out bool closed)
    {
        int run = i;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    Flush(output, ref run, i, ClassificationKind.StringLiteral);
                    Emit(output, i, 2, ClassificationKind.EscapeSequence);
                    i += 2;
                    run = i;
                    continue;
                }

                i++;
                Flush(output, ref run, i, ClassificationKind.StringLiteral);
                closed = true;
                return i;
            }

            if (dollars > 0 && (c == '{' || c == '}') && i + 1 < line.Length && line[i + 1] == c)
            {
                Flush(output, ref run, i, ClassificationKind.StringLiteral);
                Emit(output, i, 2, ClassificationKind.EscapeSequence);
                i += 2;
                run = i;
                continue;
            }

            i++;
        }

        Flush(output, ref run, i, ClassificationKind.StringLiteral);
        closed = false;
        return i;
    }

    private static int ContinueRawString(
        ReadOnlySpan<char> line,
        int i,
        byte quoteCount,
        byte dollars,
        List<ClassificationSpan> output,
        out bool closed)
    {
        int run = i;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '"')
            {
                int quotes = 0;
                while (i + quotes < line.Length && line[i + quotes] == '"')
                    quotes++;
                if (quotes >= quoteCount)
                {
                    i += quotes;
                    Flush(output, ref run, i, ClassificationKind.StringLiteral);
                    closed = true;
                    return i;
                }

                i += quotes;
                continue;
            }

            if (dollars > 0 && (c == '{' || c == '}') && i + 1 < line.Length && line[i + 1] == c)
            {
                Flush(output, ref run, i, ClassificationKind.StringLiteral);
                Emit(output, i, 2, ClassificationKind.EscapeSequence);
                i += 2;
                run = i;
                continue;
            }

            i++;
        }

        Flush(output, ref run, i, ClassificationKind.StringLiteral);
        closed = false;
        return i;
    }

    private static int ScanNumber(ReadOnlySpan<char> line, int i)
    {
        if (line[i] == '0' && i + 1 < line.Length && (line[i + 1] is 'x' or 'X' or 'b' or 'B'))
        {
            i += 2;
            while (i < line.Length && (char.IsAsciiLetterOrDigit(line[i]) || line[i] == '_'))
                i++;
            return i;
        }

        bool seenDot = false;
        while (i < line.Length)
        {
            char c = line[i];
            if (char.IsAsciiDigit(c) || c == '_')
            {
                i++;
                continue;
            }

            if (c == '.' && !seenDot && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1]))
            {
                seenDot = true;
                i++;
                continue;
            }

            if ((c is 'e' or 'E') && i + 1 < line.Length &&
                (char.IsAsciiDigit(line[i + 1]) || line[i + 1] is '+' or '-'))
            {
                i += 2;
                continue;
            }

            if (c is 'f' or 'F' or 'd' or 'D' or 'm' or 'M' or 'u' or 'U' or 'l' or 'L')
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    private static int IndexOfBlockCommentEnd(ReadOnlySpan<char> span)
    {
        int index = span.IndexOf("*/", StringComparison.Ordinal);
        return index < 0 ? -1 : index + 2;
    }

    private static void Emit(List<ClassificationSpan> output, int start, int length, ClassificationKind kind)
    {
        if (length > 0)
            output.Add(new ClassificationSpan(start, length, kind));
    }

    private static void Flush(
        List<ClassificationSpan> output,
        ref int runStart,
        int end,
        ClassificationKind kind)
    {
        if (end > runStart)
            output.Add(new ClassificationSpan(runStart, end - runStart, kind));
        runStart = end;
    }

    private static bool IsIdentifierStart(char c) =>
        c == '_' || c == '@' || char.IsLetter(c);

    private static bool IsIdentifierPart(char c) =>
        c == '_' || char.IsLetterOrDigit(c);

    private static bool IsPunctuation(char c) =>
        c is '(' or ')' or '[' or ']' or '{' or '}' or ';' or ',' or '.' or ':';

    private static bool IsOperator(char c) =>
        c is '+' or '-' or '*' or '/' or '%' or '=' or '<' or '>' or '!' or
            '&' or '|' or '^' or '~' or '?';
}
