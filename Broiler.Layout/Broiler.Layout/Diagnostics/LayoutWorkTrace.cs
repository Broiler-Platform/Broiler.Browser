using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Broiler.Layout.Diagnostics;

/// <summary>
/// An opt-in, off-by-default breakdown of what one layout pass spends its time on, and of how much
/// of the box tree sits under a subtree item #13 proposes to claim.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists before any of item #13 is written.</b> The item's row proposes two shapes —
/// "parallel intrinsic sizing" and "parallel independent subtrees" — and both are claims about
/// structure that nothing in this repository has ever measured. The stage profile times the layout
/// stage as a whole (28–51 ms, 3.3–20.1% of a render) and <see cref="LayoutPassCounter"/> counts how
/// many times the tree is walked, but neither can say what a single pass is made of. Six items in
/// this document have now found that the structure their row named was not the structure that could
/// be split; the cheapest hour in an item is the one that checks its premise.
/// </para>
/// <para>
/// <b>Self time, not inclusive time.</b> Intrinsic sizing is called from inside table layout, which
/// is called from inside the block walk, so inclusive scopes would triple-count and their sum would
/// exceed the pass. Each scope subtracts the time of the scopes opened inside it and records the
/// remainder against its own name, so the totals are disjoint and the pass minus their sum is a
/// residual that means something — the same reason <see cref="RenderStageTrace"/> publishes its
/// residual rather than absorbing it.
/// </para>
/// <para>
/// <b>The counters are the result; the milliseconds are the corroboration.</b> Phase 4 §3 records a
/// harness that nearly published a false positive because this host's throughput drifts by tens of
/// percent over tens of seconds, and closes with the general form: measure a structural question with
/// a counter rather than inferring it from a time. "How many boxes does intrinsic sizing visit per
/// box laid out" is structural — it is identical on every iteration and cannot drift — and it is what
/// decides whether the intrinsic half of item #13 is a threading problem or a repetition problem. The
/// times are here so a counter that looks alarming can be checked against a cost that is or is not
/// there.
/// </para>
/// <para>
/// <b>Cost when off, which is always in production.</b> <see cref="Measure"/> reads one static
/// <see langword="bool"/> and returns a struct whose <see cref="Scope.Dispose"/> is a null check;
/// <see cref="Count"/> reads the same bool and returns. No allocation and nothing to synchronise.
/// </para>
/// <para>
/// <b>Not thread-safe, and deliberately so</b> — every accumulator is <c>[ThreadStatic]</c>, for the
/// reason <see cref="RenderStageTrace"/> gives at length: a collector that locked would measure its
/// own lock on exactly the path this document is trying to parallelize. A concurrent layout reads its
/// own thread's numbers, which is the only reading that would mean anything.
/// </para>
/// </remarks>
public static class LayoutWorkTrace
{
    /// <summary>Timed operations. These strings are the vocabulary the published profile uses.</summary>
    public static class Ops
    {
        /// <summary>
        /// Min/max-content measurement — <c>GetMinimumWidth</c>, <c>GetMinMaxWidth</c>,
        /// <c>GetContentMinMaxWidth</c> and <c>ComputeShrinkToFitWidth</c>, each of which walks a
        /// subtree. This is the operation item #13's first shape ("parallel intrinsic sizing") names.
        /// </summary>
        public const string Intrinsic = "intrinsic sizing";

        /// <summary>Measuring a box's own words — <c>MeasureWordsSize</c>, the text-shaping entry.</summary>
        public const string TextMeasure = "text measure";

        /// <summary>Breaking an inline formatting context into lines — <c>CssLayoutEngine.CreateLineBoxes</c>.</summary>
        public const string LineBreak = "line breaking";

        /// <summary>The table layout algorithm — <c>CssLayoutEngineTable.PerformLayout</c>.</summary>
        public const string Table = "table layout";

        /// <summary>Row flex layout — <c>PerformFlexRowLayout</c>.</summary>
        public const string Flex = "flex layout";
    }

    /// <summary>Structural counters. Exact, identical every iteration, and independent of the clock.</summary>
    public static class Counters
    {
        /// <summary>Boxes entered by <c>PerformLayoutImp</c> during the pass.</summary>
        public const string BoxesLaidOut = "boxes laid out";

        /// <summary>Top-level entries into one of the four intrinsic-sizing routines.</summary>
        public const string IntrinsicCalls = "intrinsic calls";

        /// <summary>
        /// Boxes visited by the recursive helpers those calls run
        /// (<c>GetMinimumWidth_LongestWord</c>, <c>GetMinMaxSumWords</c>, and
        /// <c>ComputeShrinkToFitWidth</c>'s child loop). Against
        /// <see cref="BoxesLaidOut"/> this is the ratio that says whether intrinsic sizing is
        /// linear in the tree or quadratic in it.
        /// </summary>
        public const string IntrinsicVisits = "intrinsic visits";

        /// <summary>Boxes in the finished tree, counted by the census walk.</summary>
        public const string TreeBoxes = "tree boxes";

        /// <summary>Deepest box-tree nesting, counted by the census walk.</summary>
        public const string TreeDepth = "tree depth";

        /// <summary>Roots of an out-of-flow (<c>position:absolute</c>/<c>fixed</c>) subtree.</summary>
        public const string AbsposRoots = "abspos roots";

        /// <summary>Boxes inside those subtrees, the roots included.</summary>
        public const string AbsposBoxes = "abspos boxes";

        /// <summary>Table cells, which lay out independently of their siblings once columns are sized.</summary>
        public const string TableCellRoots = "table cell roots";

        /// <summary>Boxes inside table cells, the cells included.</summary>
        public const string TableCellBoxes = "table cell boxes";

        /// <summary>Flex and grid items — children of a flex/grid container.</summary>
        public const string FlexGridRoots = "flex/grid item roots";

        /// <summary>Boxes inside flex/grid items, the items included.</summary>
        public const string FlexGridBoxes = "flex/grid item boxes";

        /// <summary>
        /// Boxes under <em>any</em> independent root, counted once each. Not the sum of the three
        /// above: an abspos box inside a table cell would otherwise be counted twice, and the number
        /// item #13's ceiling is bounded by is the union, not the sum.
        /// </summary>
        public const string IndependentBoxes = "independent boxes";
    }

    [ThreadStatic]
    private static Dictionary<string, double>? _times;

    [ThreadStatic]
    private static Dictionary<string, long>? _counts;

    /// <summary>Timestamp and accumulated child time of each open scope, outermost first.</summary>
    [ThreadStatic]
    private static List<(string Op, long Start, long Children)>? _stack;

    /// <summary>
    /// Whether the trace records. Off by default; the layout-composition profiler turns it on for the
    /// duration of a measured pass and off again afterwards.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>Discards the calling thread's accumulated times and counts.</summary>
    public static void Reset()
    {
        _times?.Clear();
        _counts?.Clear();
        _stack?.Clear();
    }

    /// <summary>
    /// The calling thread's self time per operation, in milliseconds, since the last
    /// <see cref="Reset"/>. A copy, so a caller can hold it across the next pass.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Times() =>
        _times is null ? new Dictionary<string, double>() : new Dictionary<string, double>(_times);

    /// <summary>The calling thread's counters since the last <see cref="Reset"/>. A copy.</summary>
    public static IReadOnlyDictionary<string, long> Counts() =>
        _counts is null ? new Dictionary<string, long>() : new Dictionary<string, long>(_counts);

    /// <summary>Adds to a counter when enabled; otherwise does nothing at all.</summary>
    public static void Count(string counter, long delta = 1)
    {
        if (!Enabled)
            return;

        var counts = _counts ??= new Dictionary<string, long>(StringComparer.Ordinal);
        counts.TryGetValue(counter, out var previous);
        counts[counter] = previous + delta;
    }

    /// <summary>Records a counter's maximum rather than its sum — used for tree depth.</summary>
    public static void CountMax(string counter, long value)
    {
        if (!Enabled)
            return;

        var counts = _counts ??= new Dictionary<string, long>(StringComparer.Ordinal);
        counts.TryGetValue(counter, out var previous);
        if (value > previous)
            counts[counter] = value;
    }

    /// <summary>
    /// Times the <see langword="using"/> block into <paramref name="op"/>'s self time when
    /// <see cref="Enabled"/>; otherwise does nothing at all.
    /// </summary>
    public static Scope Measure(string op) => Enabled ? new Scope(op) : default;

    private static void Push(string op, long start)
    {
        var stack = _stack ??= new List<(string, long, long)>(16);
        stack.Add((op, start, 0));
    }

    private static void Pop(string op, long start)
    {
        var stack = _stack;
        if (stack is null || stack.Count == 0)
            return;

        var frame = stack[^1];
        stack.RemoveAt(stack.Count - 1);

        var elapsed = Stopwatch.GetTimestamp() - start;
        var self = elapsed - frame.Children;
        if (self < 0)
            self = 0;

        var times = _times ??= new Dictionary<string, double>(StringComparer.Ordinal);
        times.TryGetValue(op, out var previous);
        times[op] = previous + (self * 1000.0 / Stopwatch.Frequency);

        // Charge the whole (inclusive) span to the enclosing scope's child total, so that scope
        // records only what it did itself.
        if (stack.Count > 0)
        {
            var parent = stack[^1];
            stack[^1] = (parent.Op, parent.Start, parent.Children + elapsed);
        }
    }

    /// <summary>Whether any scope is currently open on this thread.</summary>
    internal static bool InScope => _stack is { Count: > 0 };

    /// <summary>One timed operation. <see langword="default"/> is the disabled, do-nothing form.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string? _op;
        private readonly long _start;

        internal Scope(string op)
        {
            _op = op;
            _start = Stopwatch.GetTimestamp();
            Push(op, _start);
        }

        /// <summary>Accumulates the elapsed self time, or returns immediately when disabled.</summary>
        public void Dispose()
        {
            if (_op is not null)
                Pop(_op, _start);
        }
    }
}
