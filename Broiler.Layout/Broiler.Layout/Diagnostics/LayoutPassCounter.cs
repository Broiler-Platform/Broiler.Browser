using System;

namespace Broiler.Layout.Diagnostics;

/// <summary>
/// An opt-in, off-by-default count of how many times a single <c>PerformLayout</c> call lays the box
/// tree out from the root.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> The <c>Broiler.Layout</c> roadmap's step 1 — "stop laying out the whole
/// tree twice" — is written as the highest-value sequential item in Phase 4, worth more than the two
/// parallel steps combined. It names one site: <c>HtmlContainerInt.PerformLayout</c> runs
/// <c>Root.PerformLayout</c> a second time whenever <c>MaxSize.Width &lt;= 0.1</c>, so that the first
/// pass can discover the shrink-to-fit width the second lays out against. Nothing in this repository
/// had ever counted those passes, so the item's size was an inference from a source reading rather
/// than a measurement — and the first question a measurement has to answer is not "how much does the
/// second pass cost" but <em>whether the paths this repository measures reach it at all</em>.
/// </para>
/// <para>
/// <b>Why a counter and not a timer.</b> The stage profile already times layout, and
/// <see cref="RenderStageTrace"/> already splits the stage above it. What neither can see is
/// <em>multiplicity</em>: a layout stage of 51 ms is one pass of 51 or two of 25, and the two readings
/// imply opposite work. A count separates them with no timing noise at all, and it is the number the
/// roadmap step is actually stated in ("twice").
/// </para>
/// <para>
/// <b>Why it lives here and is called from there.</b> Same reason as
/// <see cref="RenderStageTrace"/> and <see cref="Broiler.Layout.Engine.CssStyleRecalc"/>: the call
/// site is <c>HtmlContainerInt</c>, in the <c>Broiler.HTML</c> submodule, but a type defined in the
/// submodule would put the whole feature behind a patch and would stop the main repository's
/// benchmarks compiling the moment the submodule tree is reverted. Defining the type here and
/// patching in the one-line call keeps the patch to a line and keeps this assembly buildable on its
/// own. It has no dependency on anything else in <c>Broiler.Layout</c>.
/// </para>
/// <para>
/// <b>Cost when off, which is always in production.</b> <see cref="Record"/> reads one static
/// <see langword="bool"/> and returns. No allocation, nothing to synchronise.
/// </para>
/// <para>
/// <b>Not thread-safe, and deliberately so</b> — the accumulator is <c>[ThreadStatic]</c>, for the
/// reason <see cref="RenderStageTrace"/> gives at length: a render happens on one thread, and a
/// collector that locked would measure its own lock on exactly the paths the multithreading roadmap
/// is trying to parallelize. A concurrent renderer reads its own thread's count.
/// </para>
/// </remarks>
public static class LayoutPassCounter
{
    [ThreadStatic]
    private static int _passes;

    [ThreadStatic]
    private static int _calls;

    /// <summary>
    /// Whether the counter records. Off by default; the layout-pass profiler turns it on for the
    /// duration of a measured render and off again afterwards.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>Discards the calling thread's counts.</summary>
    public static void Reset()
    {
        _passes = 0;
        _calls = 0;
    }

    /// <summary>
    /// Full-tree layout passes on the calling thread since the last <see cref="Reset"/>. One
    /// <c>PerformLayout</c> call contributes one pass normally and two on the shrink-to-fit path.
    /// </summary>
    public static int Passes => _passes;

    /// <summary>
    /// <c>PerformLayout</c> <em>calls</em> on the calling thread since the last <see cref="Reset"/>.
    /// Published next to <see cref="Passes"/> because the two answer different questions: a host that
    /// calls <c>PerformLayout</c> three times to resolve an auto-size (as
    /// <c>HtmlRendererUtils.MeasureHtmlByRestrictions</c> does) multiplies the passes by three, and a
    /// pass count alone cannot say whether the multiplicity came from the engine or from the caller.
    /// </summary>
    public static int Calls => _calls;

    /// <summary>
    /// Records one full-tree layout pass. Called immediately before each <c>Root.PerformLayout</c>.
    /// </summary>
    public static void Record()
    {
        if (Enabled)
            _passes++;
    }

    /// <summary>
    /// Records entry to one <c>PerformLayout</c> call, before any pass it performs.
    /// </summary>
    public static void RecordCall()
    {
        if (Enabled)
            _calls++;
    }
}
