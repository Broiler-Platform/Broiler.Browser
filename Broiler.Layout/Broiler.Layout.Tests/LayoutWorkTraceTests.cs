using System;
using System.Drawing;
using System.Threading;
using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The contract the layout-composition profile rests on: disabled the trace records nothing, and
/// enabled its operation totals are <em>disjoint</em>.
/// </summary>
/// <remarks>
/// <para>
/// The published composition subtracts the named operations from the measured pass and calls what is
/// left the block-flow walk. That subtraction is only meaningful if a scope opened inside another is
/// charged to exactly one of them — intrinsic sizing runs inside table layout, which runs inside the
/// block walk, so inclusive scopes would trible-count and the residual could go negative or, worse,
/// come out plausible and wrong. These cases assert the disjointness directly rather than trusting
/// that the published residual "looked about right".
/// </para>
/// <para>
/// The disabled case is the production contract. Every call site added for this profile is on a hot
/// layout path — one of them is inside <c>GetMinimumWidth_LongestWord</c>, which the corpus's
/// flex/grid page enters 28 449 times in a single pass — so "records nothing when off" has to be a
/// test and not a comment.
/// </para>
/// <para>
/// These tests take a process-wide latch (<see cref="LayoutWorkTrace.Enabled"/>) and are therefore
/// serialized into their own xUnit collection. Item #21 removed this assembly's blanket
/// <c>DisableTestParallelization</c> on the grounds that isolation should be structural; a static
/// latch is the exception that proves it, so it is named as one rather than re-disabling the
/// assembly.
/// </para>
/// </remarks>
[Collection(nameof(LayoutWorkTraceTests))]
[CollectionDefinition(nameof(LayoutWorkTraceTests), DisableParallelization = true)]
public sealed class LayoutWorkTraceTests : IDisposable
{
    private static readonly Uri BaseUrl = new("file:///work-trace.html");

    public LayoutWorkTraceTests()
    {
        LayoutWorkTrace.Enabled = false;
        LayoutWorkTrace.Reset();
    }

    public void Dispose()
    {
        LayoutWorkTrace.Enabled = false;
        LayoutWorkTrace.Reset();
    }

    [Fact(Timeout = 600000)]
    public void Disabled_it_records_nothing()
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.BoxesLaidOut, 17);
        LayoutWorkTrace.CountMax(LayoutWorkTrace.Counters.TreeDepth, 9);
        using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic))
        {
            Spin();
        }

        Assert.Empty(LayoutWorkTrace.Counts());
        Assert.Empty(LayoutWorkTrace.Times());
    }

    [Fact(Timeout = 600000)]
    public void A_whole_layout_pass_records_nothing_when_disabled()
    {
        // The call sites, not the API: the point of the disabled contract is that an engine carrying
        // this instrumentation is silent, and only a real pass exercises every site at once.
        var root = TextTree();

        LayoutWorkTrace.Reset();
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Empty(LayoutWorkTrace.Counts());
        Assert.Empty(LayoutWorkTrace.Times());
    }

    [Fact(Timeout = 600000)]
    public void A_nested_scope_is_charged_to_the_inner_operation_and_subtracted_from_the_outer()
    {
        LayoutWorkTrace.Enabled = true;

        using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Table))
        {
            Spin();
            using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic))
            {
                // Much longer than the outer scope's own work, so an inclusive accounting would
                // report the table as the larger of the two and this assertion would fail.
                Spin(20);
            }
        }

        var times = LayoutWorkTrace.Times();
        var table = times[LayoutWorkTrace.Ops.Table];
        var intrinsic = times[LayoutWorkTrace.Ops.Intrinsic];

        Assert.True(
            intrinsic > table,
            $"the inner scope did most of the work, so it must carry most of the time ({intrinsic} vs {table})");
    }

    [Fact(Timeout = 600000)]
    public void Self_times_never_exceed_the_span_that_contains_them()
    {
        LayoutWorkTrace.Enabled = true;

        var start = System.Diagnostics.Stopwatch.StartNew();
        using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.LineBreak))
        {
            Spin(5);
            using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.TextMeasure))
                Spin(5);
            using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic))
                Spin(5);
        }
        start.Stop();

        var times = LayoutWorkTrace.Times();
        var sum = times[LayoutWorkTrace.Ops.LineBreak]
                + times[LayoutWorkTrace.Ops.TextMeasure]
                + times[LayoutWorkTrace.Ops.Intrinsic];

        // This is the property that makes `pass - Σ(ops)` a residual rather than an artefact. A
        // small tolerance covers the timestamp taken outside the outermost scope.
        Assert.True(
            sum <= start.Elapsed.TotalMilliseconds + 1.0,
            $"disjoint self times must fit inside the span containing them ({sum} vs {start.Elapsed.TotalMilliseconds})");
    }

    [Fact(Timeout = 600000)]
    public void Repeated_scopes_of_one_operation_accumulate()
    {
        LayoutWorkTrace.Enabled = true;

        using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic))
            Spin(5);
        var once = LayoutWorkTrace.Times()[LayoutWorkTrace.Ops.Intrinsic];

        using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic))
            Spin(5);
        var twice = LayoutWorkTrace.Times()[LayoutWorkTrace.Ops.Intrinsic];

        Assert.True(twice > once, $"a second scope must add to the first ({twice} vs {once})");
    }

    [Fact(Timeout = 600000)]
    public void Counters_sum_and_CountMax_takes_the_maximum()
    {
        LayoutWorkTrace.Enabled = true;

        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.BoxesLaidOut, 3);
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.BoxesLaidOut, 4);
        LayoutWorkTrace.CountMax(LayoutWorkTrace.Counters.TreeDepth, 9);
        LayoutWorkTrace.CountMax(LayoutWorkTrace.Counters.TreeDepth, 4);

        var counts = LayoutWorkTrace.Counts();
        Assert.Equal(7, counts[LayoutWorkTrace.Counters.BoxesLaidOut]);
        Assert.Equal(9, counts[LayoutWorkTrace.Counters.TreeDepth]);
    }

    [Fact(Timeout = 600000)]
    public void Reset_discards_the_counts_and_the_times()
    {
        LayoutWorkTrace.Enabled = true;

        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.BoxesLaidOut);
        using (LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic))
            Spin();

        LayoutWorkTrace.Reset();

        Assert.Empty(LayoutWorkTrace.Counts());
        Assert.Empty(LayoutWorkTrace.Times());
    }

    [Fact(Timeout = 600000)]
    public void An_enabled_pass_counts_every_box_it_lays_out()
    {
        var root = TextTree();

        LayoutWorkTrace.Reset();
        LayoutWorkTrace.Enabled = true;
        root.PerformLayout(root.LayoutEnvironment);
        LayoutWorkTrace.Enabled = false;

        var counts = LayoutWorkTrace.Counts();
        Assert.True(
            counts[LayoutWorkTrace.Counters.BoxesLaidOut] > 0,
            "an enabled pass must count the boxes it entered");
        Assert.True(
            counts[LayoutWorkTrace.Counters.TreeBoxes] >= counts[LayoutWorkTrace.Counters.BoxesLaidOut],
            "the census walks the whole tree, so it cannot see fewer boxes than the pass entered");
    }

    /// <summary>
    /// The union counter is the one the published ceiling uses, so the case that separates it from a
    /// per-class sum is asserted: a box under two independent roots at once.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_box_under_two_independent_roots_is_counted_once()
    {
        var environment = new StubLayoutEnvironment();
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = environment,
        };

        var cell = Block(root);
        cell.Display = CssConstants.TableCell;

        var abspos = Block(cell);
        abspos.Position = CssConstants.Absolute;

        var leaf = Block(abspos);
        leaf.Height = "10px";

        LayoutWorkTrace.Reset();
        LayoutWorkTrace.Enabled = true;
        root.PerformLayout(root.LayoutEnvironment);
        LayoutWorkTrace.Enabled = false;

        var counts = LayoutWorkTrace.Counts();
        var tree = counts[LayoutWorkTrace.Counters.TreeBoxes];
        var independent = counts[LayoutWorkTrace.Counters.IndependentBoxes];

        // cell + abspos + leaf are independent; the root is not. Summing the per-class counters
        // would reach 3 + 2 = 5 against a 4-box tree, which is the failure this guards.
        Assert.Equal(4, tree);
        Assert.Equal(3, independent);
        Assert.True(independent <= tree, "the union cannot exceed the tree");
    }

    private static void Spin(int units = 1)
    {
        // Thread.Sleep would measure the scheduler; the profile times CPU-bound layout work, so the
        // scopes under test are given CPU-bound work.
        for (var i = 0; i < units; i++)
            Thread.SpinWait(200_000);
    }

    private static CssBox TextTree()
    {
        var environment = new StubLayoutEnvironment();
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = environment,
        };

        var html = Block(root);
        var body = Block(html);
        var block = Block(body);
        block.Height = "10px";

        return root;
    }

    private static CssBox Block(CssBox parent) =>
        new(parent, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
            Display = CssConstants.Block,
            FontSize = "16px",
        };

    private sealed class StubLayoutEnvironment : ILayoutEnvironment
    {
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new StubFont(size);
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;
        public ImageIntrinsics GetImageIntrinsics(object imageHandle) => default;
        public Broiler.Graphics.BColor ParseColor(string value) => default;
        public void RequestRefresh(bool relayout) { }
        public SizeF ViewportSize => new(1000, 1000);
        public PointF RootLocation => PointF.Empty;
        public SizeF ActualSize { get; set; }
        public bool AvoidGeometryAntialias => false;
        public SizeF PageSize => new(1000, 1000);
        public int MarginTop => 0;
        public void ReportLayoutError(string message, Exception? exception = null) { }
        public bool AvoidAsyncImagesLoading => true;
        public bool AvoidImagesLateLoading => true;
        public ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete) => null!;
        public string FormatListMarker(int number, string style) => string.Empty;
    }

    private sealed class StubFont(double size) : Broiler.Graphics.ILayoutFont
    {
        public double Size { get; } = size;
        public double Height => size;
        public double UnderlineOffset => 0;
        public double LeftPadding => 0;
        public string? FontFeatures => null;
    }
}
