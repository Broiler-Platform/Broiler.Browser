using Broiler.Code.Phase0.Prototype.Analysis;
using Broiler.Code.Phase0.Prototype.Classification;
using Broiler.Code.Phase0.Prototype.Fixtures;
using Broiler.Code.Phase0.Prototype.Text;

namespace Broiler.Code.Phase0.Prototype;

public sealed record CheckResult(string Name, bool Passed, string Detail);

/// <summary>
/// The incremental classifier is only useful if it is indistinguishable from
/// the full one. Every check here asserts exactly that: after an edit, the
/// incremental result must equal the result a full classification of the same
/// snapshot would produce — same line count, same start states, same spans.
/// Speed without this property is worthless, so these run before any
/// measurement and a failure fails the process.
/// </summary>
public static class CorrectnessChecks
{
    public static List<CheckResult> RunAll()
    {
        var results = new List<CheckResult>();
        results.AddRange(MultiLineStateAgreement());
        results.Add(LargeFileAgreement());
        results.Add(StaleTransactionRejected());
        results.Add(OutOfRangeTransactionRejected());
        results.Add(UndoRedoRoundTrip());
        results.Add(LineIndexAgreesWithSplit());
        results.Add(CompletedStaleRunIsRejected());
        return results;
    }

    /// <summary>
    /// The rejection path, made deterministic.
    ///
    /// Cancellation alone does not cover this. A superseded run is usually
    /// cancelled before it finishes, but not always: the window between a run
    /// completing and its completion being delivered on the dispatcher is real,
    /// and an edit can land inside it. This check puts an edit exactly there —
    /// the run finishes uncancelled, the buffer then moves on, and only then is
    /// the completion delivered. The controller must discard it.
    /// </summary>
    private static CheckResult CompletedStaleRunIsRejected()
    {
        // Above the synchronous limit, so the background path is taken.
        var buffer = new SourceBuffer(SourceFixtures.BuildCSharpFile(3_000));
        var dispatcher = new DeferredDispatcher();
        var scheduler = new ManualScheduler();
        using var controller = new ClassificationController(buffer, dispatcher, scheduler);

        scheduler.ReleaseAll();
        dispatcher.Drain();
        if (controller.Result is null)
            return new CheckResult("completed-stale-run-rejected", false, "no initial result");

        buffer.Apply(new EditTransaction(buffer.Current.Version, TextChange.Insert(0, "/*"), "first"));

        // The run for that edit finishes normally and posts its completion. It
        // is not drained, so it is still in flight.
        scheduler.ReleaseAll();
        if (dispatcher.PendingCount == 0)
            return new CheckResult("completed-stale-run-rejected", false, "no completion was posted");

        // The buffer moves on while that completion is queued.
        buffer.Apply(new EditTransaction(buffer.Current.Version, TextChange.Insert(0, "x"), "second"));
        scheduler.ReleaseAll();
        dispatcher.Drain();

        bool rejected = controller.RejectedResults >= 1;
        bool paintedCurrent = ReferenceEquals(controller.Result?.Snapshot, buffer.Current);
        bool passed = rejected && paintedCurrent;
        return new CheckResult(
            "completed-stale-run-rejected",
            passed,
            passed
                ? $"{controller.RejectedResults} completed run(s) discarded; " +
                  "the painted result is the current snapshot"
                : $"rejected={controller.RejectedResults} paintedCurrent={paintedCurrent}");
    }

    /// <summary>
    /// Runs scheduled work only when told to, so a test can state exactly when
    /// a run finishes relative to an edit. Faults are routed into the task the
    /// way a real scheduler routes them, rather than escaping into the caller
    /// of <see cref="ReleaseAll"/>.
    /// </summary>
    private sealed class ManualScheduler : IAnalysisScheduler
    {
        private readonly Queue<Action> _pending = new();

        public Task<T> Schedule<T>(Func<T> work, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<T>();
            _pending.Enqueue(() =>
            {
                try
                {
                    completion.SetResult(work());
                }
                catch (OperationCanceledException)
                {
                    completion.SetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
            return completion.Task;
        }

        public int ReleaseAll()
        {
            int released = 0;
            while (_pending.Count > 0)
            {
                _pending.Dequeue()();
                released++;
            }

            return released;
        }
    }

    private static IEnumerable<CheckResult> MultiLineStateAgreement()
    {
        var classifier = new PortableCSharpClassifier();
        foreach ((string name, string text) in SourceFixtures.MultiLineStateSources())
        {
            string? failure = null;
            var buffer = new SourceBuffer(text);
            ClassificationResult result = classifier.ClassifyAll(buffer.Current, CancellationToken.None);

            // Walk an edit across every position in the source, which is where a
            // line-state classifier gets multi-line constructs wrong.
            for (int position = 0; position <= buffer.Current.Length && failure is null; position++)
            {
                var insert = new EditTransaction(
                    buffer.Current.Version, TextChange.Insert(position, "\"/*"), "probe-insert");
                EditResult applied = buffer.Apply(insert);
                if (!applied.Accepted)
                {
                    failure = $"insert at {position} was rejected: {applied.Reason}";
                    break;
                }

                result = classifier.ClassifyIncremental(
                    result, buffer.Current, insert.Change, CancellationToken.None);
                failure = Compare(
                    result,
                    classifier.ClassifyAll(buffer.Current, CancellationToken.None),
                    $"after inserting at {position}");
                if (failure is not null)
                    break;

                var remove = new EditTransaction(
                    buffer.Current.Version, TextChange.Delete(position, 3), "probe-delete");
                applied = buffer.Apply(remove);
                if (!applied.Accepted)
                {
                    failure = $"delete at {position} was rejected: {applied.Reason}";
                    break;
                }

                result = classifier.ClassifyIncremental(
                    result, buffer.Current, remove.Change, CancellationToken.None);
                failure = Compare(
                    result,
                    classifier.ClassifyAll(buffer.Current, CancellationToken.None),
                    $"after deleting at {position}");
            }

            yield return new CheckResult(
                $"incremental-equals-full/{name}",
                failure is null,
                failure ?? $"{buffer.Current.Length + 1} insert/delete positions agree");
        }
    }

    private static CheckResult LargeFileAgreement()
    {
        var classifier = new PortableCSharpClassifier();
        var buffer = new SourceBuffer(SourceFixtures.BuildCSharpFile(4_000));
        ClassificationResult result = classifier.ClassifyAll(buffer.Current, CancellationToken.None);

        // Edits chosen to hit each interesting construct: inside a block
        // comment, inside a raw string, at a directive, and at end of file.
        int[] lines = [0, 11, 12, 13, 40, 41, 1_000, 2_500, buffer.Current.LineCount - 1];
        foreach (int line in lines)
        {
            foreach (string text in new[] { "/*", "*/", "\"\"\"", "@\"", "#if X", "\n" })
            {
                int position = buffer.Current.GetLineStart(line);
                var transaction = new EditTransaction(
                    buffer.Current.Version, TextChange.Insert(position, text), "large-probe");
                if (!buffer.Apply(transaction).Accepted)
                    return new CheckResult("incremental-equals-full/large", false, "edit rejected");

                result = classifier.ClassifyIncremental(
                    result, buffer.Current, transaction.Change, CancellationToken.None);
                string? failure = Compare(
                    result,
                    classifier.ClassifyAll(buffer.Current, CancellationToken.None),
                    $"after inserting {text.Replace("\n", "\\n")} at line {line}");
                if (failure is not null)
                    return new CheckResult("incremental-equals-full/large", false, failure);
            }
        }

        return new CheckResult(
            "incremental-equals-full/large",
            true,
            $"{lines.Length * 6} edits across a {buffer.Current.LineCount}-line file agree");
    }

    private static CheckResult StaleTransactionRejected()
    {
        var buffer = new SourceBuffer("var a = 1;\n");
        int baseVersion = buffer.Current.Version;
        buffer.Apply(new EditTransaction(baseVersion, TextChange.Insert(0, "x"), "first"));

        EditResult second = buffer.Apply(
            new EditTransaction(baseVersion, TextChange.Insert(0, "y"), "stale"));

        bool passed = !second.Accepted &&
            second.Reason == EditRejectionReason.StaleBaseVersion &&
            !buffer.Current.Text.Contains('y');
        return new CheckResult(
            "stale-transaction-rejected",
            passed,
            passed
                ? "a transaction against a superseded version is refused, not rebased"
                : $"accepted={second.Accepted} reason={second.Reason}");
    }

    private static CheckResult OutOfRangeTransactionRejected()
    {
        var buffer = new SourceBuffer("abc");
        EditResult result = buffer.Apply(
            new EditTransaction(buffer.Current.Version, new TextChange(2, 99, "z"), "overlong"));
        bool passed = !result.Accepted && result.Reason == EditRejectionReason.OutOfRange;
        return new CheckResult(
            "out-of-range-transaction-rejected",
            passed,
            passed ? "a span past end of text is refused" : $"reason={result.Reason}");
    }

    private static CheckResult UndoRedoRoundTrip()
    {
        const string original = "class C\n{\n    void M() { }\n}\n";
        var buffer = new SourceBuffer(original);
        int startVersion = buffer.Current.Version;

        for (int i = 0; i < 8; i++)
        {
            buffer.Apply(new EditTransaction(
                buffer.Current.Version, TextChange.Insert(8 + i, "/*"), $"edit{i}"));
        }

        string edited = buffer.Current.Text;
        for (int i = 0; i < 8; i++)
            buffer.Undo();
        bool undone = buffer.Current.Text == original;

        for (int i = 0; i < 8; i++)
            buffer.Redo();
        bool redone = buffer.Current.Text == edited;

        // Undo produces a new version rather than restoring the old snapshot, so
        // an analysis result captured before the undo can never reattach.
        bool versionsMoveForward = buffer.Current.Version > startVersion + 8;

        bool passed = undone && redone && versionsMoveForward;
        return new CheckResult(
            "undo-redo-round-trip",
            passed,
            passed
                ? "undo and redo restore text while versions keep moving forward"
                : $"undone={undone} redone={redone} versionsMoveForward={versionsMoveForward}");
    }

    private static CheckResult LineIndexAgreesWithSplit()
    {
        foreach (string text in new[]
        {
            "a\nb\nc", "a\r\nb\r\nc", "a\rb\rc", "\n", "\r\n", string.Empty, "trailing\n",
        })
        {
            TextSnapshot snapshot = TextSnapshot.Create(text);
            string[] expected = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (snapshot.LineCount != expected.Length)
            {
                return new CheckResult(
                    "line-index-agrees-with-split",
                    false,
                    $"'{Escape(text)}': indexed {snapshot.LineCount} lines, expected {expected.Length}");
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (!snapshot.GetLineSpan(i).SequenceEqual(expected[i]))
                {
                    return new CheckResult(
                        "line-index-agrees-with-split",
                        false,
                        $"'{Escape(text)}' line {i}: '{Escape(snapshot.GetLineSpan(i).ToString())}'");
                }

                int start = snapshot.GetLineStart(i);
                if (snapshot.GetLineFromPosition(start) != i)
                {
                    return new CheckResult(
                        "line-index-agrees-with-split",
                        false,
                        $"'{Escape(text)}': position {start} did not map back to line {i}");
                }
            }
        }

        return new CheckResult(
            "line-index-agrees-with-split",
            true,
            "LF, CRLF, CR, empty, and trailing-terminator texts index identically");
    }

    private static string? Compare(ClassificationResult incremental, ClassificationResult full, string context)
    {
        if (incremental.Lines.Length != full.Lines.Length)
        {
            return $"{context}: {incremental.Lines.Length} lines versus {full.Lines.Length}";
        }

        for (int i = 0; i < full.Lines.Length; i++)
        {
            LineClassification a = incremental.Lines[i];
            LineClassification b = full.Lines[i];
            if (a.StartState != b.StartState)
                return $"{context}: line {i} start state {a.StartState} versus {b.StartState}";
            if (!a.Spans.AsSpan().SequenceEqual(b.Spans))
                return $"{context}: line {i} spans differ ({a.Spans.Length} versus {b.Spans.Length})";
        }

        return null;
    }

    private static string Escape(string text) =>
        text.Replace("\r", "\\r").Replace("\n", "\\n");
}
