using Broiler.Code.Phase0.Prototype.Analysis;
using Broiler.Code.Phase0.Prototype.Classification;
using Broiler.Code.Phase0.Prototype.Fixtures;
using Broiler.Code.Phase0.Prototype.Text;
using Broiler.Code.Phase0.Prototype.Traces;

namespace Broiler.Code.Phase0.Prototype;

public sealed record TraceResult(
    string Id,
    string Budget,
    int FixtureLines,
    int Edits,
    int MaxLinesLexed,
    int MedianLinesLexed,
    int PublishedResults,
    int RejectedResults,
    int CancelledRuns,
    int FinalSpanCount,
    bool IncrementalMatchedFull,
    bool OnlyCurrentSnapshotPainted,
    Measurement SnapshotUpdate,
    Measurement Analysis);

/// <summary>
/// Replays a committed trace against the buffer/controller pair and records
/// what each edit cost. The controller is driven the way a host would drive it,
/// including the dispatcher hop, so the recorded latency is the latency of the
/// real path rather than of a direct call to the classifier.
///
/// Producing the successor snapshot and analysing it are timed separately.
/// They have different owners and different fixes, and a single combined figure
/// hides which one a regression came from.
/// </summary>
public static class TraceRunner
{
    public static TraceResult Run(EditTrace trace, string source)
    {
        var buffer = new SourceBuffer(source);
        var dispatcher = new DeferredDispatcher();
        using var controller = new ClassificationController(buffer, dispatcher);

        // The initial classification has to land before the first edit is
        // timed. Draining without awaiting leaves it in flight, the first edit
        // cancels it, and that edit then pays for a full classification it did
        // not cause — which is exactly how a trace ends up reporting a
        // full-reparse that never happened.
        controller.CurrentWork?.GetAwaiter().GetResult();
        dispatcher.Drain();

        var snapshotSampler = new Sampler();
        var analysisSampler = new Sampler();
        var linesLexed = new List<int>();
        var paintedStaleSnapshot = false;
        int published = 0;
        int edits = 0;

        controller.ResultPublished += result =>
        {
            published++;
            if (!ReferenceEquals(result.Snapshot, buffer.Current))
                paintedStaleSnapshot = true;
        };

        foreach (TraceStep step in trace.Steps)
        {
            // Resolved once per step, outside the sampled region: an anchor
            // search is a full scan and would swamp the figure it is helping
            // to produce.
            int line = ResolveLine(buffer.Current, step);
            int repeat = Math.Max(1, step.Repeat);

            for (int r = 0; r < repeat; r++)
            {
                snapshotSampler.Begin();
                bool applied = ApplyStep(buffer, step, line, r);
                snapshotSampler.End();
                if (!applied)
                    continue;

                edits++;
                if (step.WithoutDraining)
                    continue;

                analysisSampler.Begin();
                controller.CurrentWork?.GetAwaiter().GetResult();
                dispatcher.Drain();
                analysisSampler.End();

                if (controller.Result is { } result)
                    linesLexed.Add(result.LinesLexed);
            }
        }

        // Whatever the trace left queued has to land before the run is read.
        controller.CurrentWork?.GetAwaiter().GetResult();
        dispatcher.Drain();

        ClassificationResult? final = controller.Result;
        bool matched = final is not null && MatchesFullClassification(final, buffer.Current);

        linesLexed.Sort();
        return new TraceResult(
            trace.Id,
            trace.Budget,
            trace.FixtureLines,
            edits,
            linesLexed.Count == 0 ? 0 : linesLexed[^1],
            linesLexed.Count == 0 ? 0 : linesLexed[linesLexed.Count / 2],
            published,
            controller.RejectedResults,
            controller.CancelledRuns,
            final?.TotalSpans ?? 0,
            matched,
            !paintedStaleSnapshot,
            snapshotSampler.Summarize(),
            analysisSampler.Summarize());
    }

    private static int ResolveLine(TextSnapshot snapshot, TraceStep step)
    {
        if (step.Kind is TraceStepKind.Undo or TraceStepKind.Redo)
            return 0;

        if (string.IsNullOrEmpty(step.AnchorText))
            return Math.Clamp(step.Line, 0, snapshot.LineCount - 1);

        int wanted = Math.Max(1, step.AnchorOccurrence);
        int seen = 0;
        for (int i = 0; i < snapshot.LineCount; i++)
        {
            if (snapshot.GetLineSpan(i).IndexOf(step.AnchorText, StringComparison.Ordinal) < 0)
                continue;
            if (++seen == wanted)
                return i;
        }

        throw new InvalidOperationException(
            $"Trace anchor '{step.AnchorText}' occurrence {wanted} was not found. " +
            "The trace and the fixture generator have diverged.");
    }

    private static bool ApplyStep(SourceBuffer buffer, TraceStep step, int line, int repetition)
    {
        switch (step.Kind)
        {
            case TraceStepKind.Undo:
                return buffer.Undo();
            case TraceStepKind.Redo:
                return buffer.Redo();
        }

        TextSnapshot snapshot = buffer.Current;
        line = Math.Clamp(line, 0, snapshot.LineCount - 1);
        int start = snapshot.GetLineStart(line) +
            Math.Clamp(step.Column, 0, snapshot.GetLineLength(line));

        // Repeated insertions advance by what has already been typed, the way a
        // caret does; a repeated delete keeps eating at the same position.
        if (step.Kind == TraceStepKind.Insert)
            start += repetition * step.Text.Length;

        start = Math.Clamp(start, 0, snapshot.Length);
        int deleteLength = step.Kind == TraceStepKind.Insert
            ? 0
            : Math.Clamp(step.DeleteLength, 0, snapshot.Length - start);

        string text = step.Kind == TraceStepKind.Delete ? string.Empty : step.Text;
        var transaction = new EditTransaction(
            snapshot.Version, new TextChange(start, deleteLength, text), step.Kind.ToString());
        return buffer.Apply(transaction).Accepted;
    }

    private static bool MatchesFullClassification(ClassificationResult result, TextSnapshot current)
    {
        if (!ReferenceEquals(result.Snapshot, current))
            return false;

        ClassificationResult full = new PortableCSharpClassifier()
            .ClassifyAll(current, CancellationToken.None);
        if (full.Lines.Length != result.Lines.Length)
            return false;

        for (int i = 0; i < full.Lines.Length; i++)
        {
            if (result.Lines[i].StartState != full.Lines[i].StartState)
                return false;
            if (!result.Lines[i].Spans.AsSpan().SequenceEqual(full.Lines[i].Spans))
                return false;
        }

        return true;
    }

    public static string ResolveFixture(EditTrace trace) => trace.Fixture switch
    {
        "csharp-100k-lines" => SourceFixtures.BuildCSharpFile(trace.FixtureLines),
        "csharp-100k-lines-wide" => SourceFixtures.BuildCSharpFile(
            trace.FixtureLines, seed: 0, minimumLineWidth: 104),
        _ => throw new InvalidOperationException($"Unknown fixture '{trace.Fixture}'."),
    };
}
