using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.Diagnostics;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// What one layout pass is made of, and how much of the box tree sits under a subtree item #13
/// proposes to lay out in parallel — the premise check the item has never had.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> Item #13's row proposes two shapes: "parallel intrinsic sizing" and
/// "parallel independent subtrees (abspos/fixed, flex+grid items, table cells, multicol,
/// subdocuments)". Phase 4 §1 established the stage's <em>size</em> (28–51 ms, 3.3–20.1% of a render)
/// and §2 retired the sequential step ranked above both, but nothing has ever established the two
/// facts that decide the item's shape: what fraction of a pass those operations are, and what
/// fraction of the tree those subtrees hold. Six items in this document have found that the
/// structure their row named was not the structure that could be split.
/// </para>
/// <para>
/// <b>The counters are the result.</b> Two of the three questions here are structural — how many
/// boxes intrinsic sizing visits per box laid out, and how many boxes are inside an independent
/// subtree — and a structural question measured with a counter is exact and cannot drift, which is
/// the general form Phase 4 §3 arrived at after a harness in this same directory nearly published a
/// false positive from a drifting median. The milliseconds corroborate: a visit ratio that looks
/// alarming and a self time that is 1% of the pass would mean the ratio is not worth acting on.
/// </para>
/// <para>
/// <b>Self time, and the residual is published.</b> <see cref="LayoutWorkTrace"/> charges nested
/// scopes to the scope that opened them, so the operation totals are disjoint and
/// <c>layout ms - Σ(ops)</c> is the block-flow walk itself rather than an accounting artefact. It is
/// reported as its own row for the reason P0-a gives: a profile whose total is the sum of its parts
/// passes its own gate by construction.
/// </para>
/// <para>
/// <b>One shape only, deliberately.</b> Every path this repository measures sets a viewport width
/// (Phase 4 §2, counted rather than assumed), so this profiles that one — the same configuration as
/// the stage profile, which makes the layout milliseconds here comparable with the layout column
/// there. The auto-size shape is <see cref="LayoutPassProfile"/>'s subject and adds a second pass
/// whose composition would have to be reported separately to mean anything.
/// </para>
/// </remarks>
internal static class LayoutComposition
{
    private const int Width = Corpus.ViewportWidth;
    private const int Height = Corpus.ViewportHeight;

    private static readonly string[] Ops =
    [
        LayoutWorkTrace.Ops.Intrinsic,
        LayoutWorkTrace.Ops.TextMeasure,
        LayoutWorkTrace.Ops.LineBreak,
        LayoutWorkTrace.Ops.Table,
        LayoutWorkTrace.Ops.Flex,
    ];

    private sealed record Sample(
        double LayoutMs,
        IReadOnlyDictionary<string, double> Times,
        IReadOnlyDictionary<string, long> Counts);

    private sealed record Row(
        string Page,
        double LayoutMs,
        double UntracedMs,
        IReadOnlyDictionary<string, double> Times,
        IReadOnlyDictionary<string, long> Counts);

    public static int Run(int iterations, int warmup)
    {
        var rows = new List<Row>();

        foreach (var page in Corpus.Pages)
        {
            for (var i = 0; i < warmup; i++)
            {
                MeasureOnce(page, traced: true);
                MeasureOnce(page, traced: false);
            }

            // Traced and untraced are measured *interleaved*, iteration by iteration, for the reason
            // Phase 2 §7 and Phase 4 §3 both record: this host drifts by tens of percent over tens of
            // seconds, and two medians taken in their own contiguous blocks are not comparable. The
            // untraced pass is the control that says how much of the traced pass is the trace — a
            // composition published without it is a composition of an instrumented engine.
            var samples = new List<Sample>(iterations);
            var untraced = new List<double>(iterations);
            for (var i = 0; i < iterations; i++)
            {
                samples.Add(MeasureOnce(page, traced: true));
                untraced.Add(MeasureOnce(page, traced: false).LayoutMs);
            }

            // Counts are structural: identical on every iteration of the same page. Asserting that
            // rather than assuming it — a count that moved between iterations would mean the pass
            // depends on state carried across renders, which is a finding and not something to
            // median away. (Phase 3 §12 found exactly such a defect from the opposite direction:
            // laying the same tree out twice was not idempotent.)
            var counts = samples[0].Counts;
            foreach (var s in samples)
            {
                foreach (var key in counts.Keys)
                {
                    if (!s.Counts.TryGetValue(key, out var value) || value != counts[key])
                        throw new InvalidOperationException(
                            $"{page.Name}: counter '{key}' varies between iterations " +
                            $"({counts[key]} then {(s.Counts.TryGetValue(key, out var v) ? v : 0)}) — " +
                            "the pass is stateful across renders.");
                }
            }

            var times = Ops.ToDictionary(
                op => op,
                op => Median(samples.Select(s => s.Times.TryGetValue(op, out var v) ? v : 0).ToArray()));

            rows.Add(new Row(
                page.Name,
                Median(samples.Select(s => s.LayoutMs).ToArray()),
                Median(untraced.ToArray()),
                times,
                counts));
        }

        WriteMarkdown(rows, iterations, warmup);
        return 0;
    }

    /// <summary>
    /// One viewport render. With <paramref name="traced"/> the trace is armed for the layout call
    /// only; without it the identical render runs with the trace off, which is the control.
    /// </summary>
    private static Sample MeasureOnce(Corpus.Page page, bool traced)
    {
        var clip = new RectangleF(0, 0, Width, Height);
        var bitmap = new BBitmap(Width, Height);
        using var container = new HtmlContainer();
        container.Location = new PointF(0, 0);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;
        container.MaxSize = new SizeF(Width, Height);

        container.SetHtmlWithStyleSet(page.Html, null, null);
        bitmap.Clear(BColor.White);

        LayoutWorkTrace.Reset();
        LayoutWorkTrace.Enabled = traced;
        var watch = Stopwatch.StartNew();
        try
        {
            container.PerformLayout(bitmap, clip);
        }
        finally
        {
            watch.Stop();
            LayoutWorkTrace.Enabled = false;
        }

        var sample = new Sample(
            watch.Elapsed.TotalMilliseconds,
            LayoutWorkTrace.Times(),
            LayoutWorkTrace.Counts());

        bitmap.Dispose();
        return sample;
    }

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        var mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2;
    }

    private static long Get(IReadOnlyDictionary<string, long> counts, string key) =>
        counts.TryGetValue(key, out var value) ? value : 0;

    private static string F(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Pct(double part, double whole) =>
        whole <= 0 ? "—" : (100.0 * part / whole).ToString("F1", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// What the instrumentation costs when it is <em>off</em>, which is its cost in production.
    /// </summary>
    /// <remarks>
    /// This deliberately does not A/B two whole renders. The disabled cost is a static <c>bool</c>
    /// read per call site, so it is somewhere around a nanosecond, while this host's layout stage
    /// drifts by 1.6-1.9x between consecutive runs of the identical configuration — a whole-render
    /// A/B cannot resolve the smaller quantity through the larger one, and running it anyway would
    /// produce a number whose sign was decided by drift. Instead the two factors are measured
    /// separately where each is measurable: the per-call cost over a large N, where the constant is
    /// the only thing left, and the call count per page exactly, because it is structural.
    /// </remarks>
    private static void WriteDisabledCost(IReadOnlyList<Row> rows)
    {
        const int Reps = 20_000_000;

        // The trace is off for this loop, which is the configuration being costed.
        LayoutWorkTrace.Enabled = false;
        for (var i = 0; i < 1_000_000; i++)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicVisits);

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < Reps; i++)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicVisits);
        watch.Stop();

        var nsPerCall = watch.Elapsed.TotalMilliseconds * 1_000_000.0 / Reps;

        Console.WriteLine("### What it costs when off");
        Console.WriteLine();
        Console.WriteLine($"A disabled `Count` measured over {Reps:N0} calls: **{nsPerCall.ToString("F2", CultureInfo.InvariantCulture)} ns**.");
        Console.WriteLine("Multiplied by the exact call count each page makes — structural, so no drift enters —");
        Console.WriteLine("that bounds what an engine carrying this instrumentation pays with it switched off:");
        Console.WriteLine();
        Console.WriteLine("| page | disabled calls | bound (ms) | of the pass |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (var r in rows)
        {
            var calls = Get(r.Counts, LayoutWorkTrace.Counters.BoxesLaidOut)
                      + Get(r.Counts, LayoutWorkTrace.Counters.IntrinsicCalls)
                      + Get(r.Counts, LayoutWorkTrace.Counters.IntrinsicVisits);
            var ms = calls * nsPerCall / 1_000_000.0;
            Console.WriteLine($"| {r.Page} | {calls} | {ms.ToString("F4", CultureInfo.InvariantCulture)} | {Pct(ms, r.UntracedMs)} |");
        }
        Console.WriteLine();
        Console.WriteLine("The scope-based `Measure` sites are not in that count and are fewer than the `Count`");
        Console.WriteLine("sites by more than an order of magnitude; disabled, `Measure` returns a `default` struct");
        Console.WriteLine("whose `Dispose` is a null check, so it is bounded by the same constant.");
        Console.WriteLine();
    }

    private static void WriteMarkdown(IReadOnlyList<Row> rows, int iterations, int warmup)
    {
        var instrumented = rows.Any(r => Get(r.Counts, LayoutWorkTrace.Counters.BoxesLaidOut) > 0);

        Console.WriteLine("# What a layout pass is made of");
        Console.WriteLine();
        Console.WriteLine($"- Viewport: {Width}x{Height}, fixed width — the shape every path in this repository uses");
        Console.WriteLine($"- Iterations: {iterations} measured, {warmup} warm-up; times are medians, counts are structural (asserted invariant across iterations)");
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine();

        if (!instrumented)
        {
            Console.WriteLine("> **The box counter read zero on every row, so this run measured nothing.**");
            Console.WriteLine("> `LayoutWorkTrace` is called from `Broiler.Layout`'s engine; a tree that predates");
            Console.WriteLine("> those calls leaves it silent. Re-run against an instrumented tree rather than");
            Console.WriteLine("> reading the times below as a composition.");
            Console.WriteLine();
        }

        Console.WriteLine("## Self time per operation");
        Console.WriteLine();
        Console.WriteLine("Disjoint: a scope opened inside another is charged to the inner one and subtracted");
        Console.WriteLine("from the outer, so `residual` is the block-flow walk itself rather than an artefact.");
        Console.WriteLine();
        Console.Write("| page | traced ms | untraced ms | trace cost |");
        foreach (var op in Ops)
            Console.Write($" {op} |");
        Console.WriteLine(" residual |");
        Console.Write("|---|---:|---:|---:|");
        foreach (var _ in Ops)
            Console.Write("---:|");
        Console.WriteLine("---:|");

        foreach (var r in rows)
        {
            var sum = Ops.Sum(op => r.Times[op]);
            var overhead = r.UntracedMs > 0 ? r.LayoutMs / r.UntracedMs : 0;
            Console.Write($"| {r.Page} | {F(r.LayoutMs)} | {F(r.UntracedMs)} | {F(overhead)}x |");
            foreach (var op in Ops)
                Console.Write($" {F(r.Times[op])} ({Pct(r.Times[op], r.LayoutMs)}) |");
            Console.WriteLine($" {F(r.LayoutMs - sum)} ({Pct(r.LayoutMs - sum, r.LayoutMs)}) |");
        }
        Console.WriteLine();

        var worst = rows.Count == 0 ? 0 : rows.Max(r => r.UntracedMs > 0 ? r.LayoutMs / r.UntracedMs : 0);
        var best = rows.Count == 0 ? 0 : rows.Min(r => r.UntracedMs > 0 ? r.LayoutMs / r.UntracedMs : 0);
        Console.WriteLine("`untraced ms` is the identical render with the trace off, measured interleaved with the");
        Console.WriteLine($"traced one. **Trace cost spans {F(best)}x–{F(worst)}x** — and a ratio below 1.00 is a");
        Console.WriteLine("tracer that made the pass faster, which is impossible, so the spread is this host's drift");
        Console.WriteLine("rather than the trace. The shares are shares of the traced pass, and the control is a");
        Console.WriteLine("column rather than a footnote because without it they would describe an instrumented");
        Console.WriteLine("engine with no way to tell.");
        Console.WriteLine();

        WriteDisabledCost(rows);

        Console.WriteLine("## Intrinsic sizing, counted");
        Console.WriteLine();
        Console.WriteLine("`visits` is how many boxes the recursive min/max-content helpers touch; `visits/box`");
        Console.WriteLine("divides it by the boxes the pass laid out. A pass that measured each box's content");
        Console.WriteLine("once would read 1.");
        Console.WriteLine();
        Console.WriteLine("| page | boxes laid out | intrinsic calls | intrinsic visits | visits/box | calls/box |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            var boxes = Get(r.Counts, LayoutWorkTrace.Counters.BoxesLaidOut);
            var calls = Get(r.Counts, LayoutWorkTrace.Counters.IntrinsicCalls);
            var visits = Get(r.Counts, LayoutWorkTrace.Counters.IntrinsicVisits);
            Console.WriteLine(
                $"| {r.Page} | {boxes} | {calls} | {visits} | " +
                $"{(boxes > 0 ? F((double)visits / boxes) : "—")} | " +
                $"{(boxes > 0 ? F((double)calls / boxes) : "—")} |");
        }
        Console.WriteLine();

        Console.WriteLine("## Independent subtrees, counted");
        Console.WriteLine();
        Console.WriteLine("`independent` is the union — a box under two independent roots is counted once — and it");
        Console.WriteLine("is the ceiling a subtree split is bounded by. Multi-column and subdocuments are on item");
        Console.WriteLine("#13's list and are **not** counted: a column is not a box in this engine and a");
        Console.WriteLine("subdocument is laid out by its own container, so neither has a root this walk can name.");
        Console.WriteLine();
        Console.WriteLine("| page | tree boxes | depth | abspos roots / boxes | table cells / boxes | flex+grid items / boxes | independent | share |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            var tree = Get(r.Counts, LayoutWorkTrace.Counters.TreeBoxes);
            var independent = Get(r.Counts, LayoutWorkTrace.Counters.IndependentBoxes);
            Console.WriteLine(
                $"| {r.Page} | {tree} | {Get(r.Counts, LayoutWorkTrace.Counters.TreeDepth)} | " +
                $"{Get(r.Counts, LayoutWorkTrace.Counters.AbsposRoots)} / {Get(r.Counts, LayoutWorkTrace.Counters.AbsposBoxes)} | " +
                $"{Get(r.Counts, LayoutWorkTrace.Counters.TableCellRoots)} / {Get(r.Counts, LayoutWorkTrace.Counters.TableCellBoxes)} | " +
                $"{Get(r.Counts, LayoutWorkTrace.Counters.FlexGridRoots)} / {Get(r.Counts, LayoutWorkTrace.Counters.FlexGridBoxes)} | " +
                $"{independent} | {Pct(independent, tree)} |");
        }
        Console.WriteLine();
    }
}
