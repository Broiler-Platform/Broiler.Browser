using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Broiler.Layout.Diagnostics;

/// <summary>
/// An opt-in, off-by-default timer for the sub-stages of <c>parse+cascade</c> — the one stage of the
/// headless render that has no public seam inside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it lives here and is called from there.</b> The scopes are opened in
/// <c>DomParser</c>, in the <c>Broiler.HTML</c> submodule; the type is in this assembly for the same
/// reason <see cref="Broiler.Layout.Engine.CssStyleRecalc"/> is, which is written up there. It has no
/// dependency on anything in <c>Broiler.Layout</c> and could live almost anywhere the submodules and
/// the stage profiler can both reach.
/// </para>
/// <para>
/// <b>Why this exists at all, given P0-a's method.</b> The stage profile in
/// <c>tests/render-stages</c> draws its boundaries at public pipeline calls precisely so it cannot
/// drift from the real path, and it derives the one split it cannot measure (the raster/paint-walk
/// split) by subtracting a second, out-of-band measurement of a pure function. Neither technique
/// reaches inside <c>SetHtmlWithStyleSet</c>: the HTML parse, the stylesheet parse, the cascade and
/// the box-fixup passes are four private calls in a row, none of them pure — each consumes the tree
/// the previous one produced and the style set the previous one grew. Re-running them out of band
/// would measure four operations that never see each other's state.
/// </para>
/// <para>
/// So this is instrumentation, and the drift risk P0-a names is answered differently here: the
/// timers are <em>on</em> the real path rather than around a second copy of it, so there is no
/// second implementation to drift. What a reader has to check instead is that the scopes tile the
/// stage without overlapping, which is why the sub-stage total is published against the measured
/// <c>parse+cascade</c> figure and the difference is reported as a residual rather than absorbed.
/// </para>
/// <para>
/// <b>Cost when off, which is always in production.</b> <see cref="Measure"/> reads one static
/// <see langword="bool"/> and returns a struct whose <see cref="Scope.Dispose"/> is a null check.
/// No allocation, no <see cref="Stopwatch"/> instance, and nothing to synchronise. When on it takes
/// two <see cref="Stopwatch.GetTimestamp"/> calls per sub-stage — four per render.
/// </para>
/// <para>
/// <b>Not thread-safe, and deliberately so.</b> The accumulator is <c>[ThreadStatic]</c>: a render
/// happens on one thread, and a collector that locked would measure its own lock on exactly the
/// paths this document is trying to parallelize. A concurrent renderer reads its own thread's
/// numbers, which is the only reading that means anything.
/// </para>
/// </remarks>
public static class RenderStageTrace
{
    /// <summary>Sub-stage names. These strings are the vocabulary the published profile uses.</summary>
    public static class SubStages
    {
        /// <summary>Tokenize, tree-build, and construct the box tree — <c>HtmlParser.ParseDocument</c>.</summary>
        public const string HtmlParse = "html parse";

        /// <summary>Collect and parse <c>&lt;style&gt;</c> / <c>&lt;link&gt;</c> stylesheets into the style set.</summary>
        public const string CssParse = "css parse";

        /// <summary>
        /// Resolve every element's cascade — the threaded half of item #12
        /// (<c>CssStyleRecalc.Warm</c>). Zero when the warm pass declines to run, in which case the
        /// same work happens inside <see cref="CascadeProject"/> one element at a time.
        /// </summary>
        public const string CascadeResolve = "cascade (resolve)";

        /// <summary>
        /// Build the style engines and walk the box tree projecting each element's cascade onto its
        /// box. Ordered and single-threaded by construction — see <c>CssStyleRecalc</c> — so this is
        /// the serial residue item #12's speedup is bounded by.
        /// </summary>
        public const string CascadeProject = "cascade (project)";

        /// <summary>The post-cascade box-correction passes (text, images, inline/block repair, replaced elements).</summary>
        public const string BoxFixups = "box fixups";
    }

    [ThreadStatic]
    private static Dictionary<string, double>? _accumulator;

    /// <summary>
    /// Whether the sub-stage timers run. Off by default; the stage profiler turns it on for the
    /// duration of a measured render and off again afterwards.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>Discards the calling thread's accumulated sub-stage totals.</summary>
    public static void Reset() => _accumulator?.Clear();

    /// <summary>
    /// The calling thread's accumulated sub-stage milliseconds since the last <see cref="Reset"/>.
    /// A copy, so a caller can hold it across the next render.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Totals() =>
        _accumulator is null
            ? new Dictionary<string, double>()
            : new Dictionary<string, double>(_accumulator);

    /// <summary>
    /// Times the <see langword="using"/> block into <paramref name="subStage"/> when
    /// <see cref="Enabled"/>; otherwise does nothing at all.
    /// </summary>
    public static Scope Measure(string subStage) => Enabled ? new Scope(subStage) : default;

    private static void Add(string subStage, long startTimestamp)
    {
        var ms = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        var totals = _accumulator ??= new Dictionary<string, double>(StringComparer.Ordinal);
        totals.TryGetValue(subStage, out var previous);
        totals[subStage] = previous + ms;
    }

    /// <summary>One timed sub-stage. <see langword="default"/> is the disabled, do-nothing form.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string? _subStage;
        private readonly long _start;

        internal Scope(string subStage)
        {
            _subStage = subStage;
            _start = Stopwatch.GetTimestamp();
        }

        /// <summary>Accumulates the elapsed time, or returns immediately when disabled.</summary>
        public void Dispose()
        {
            if (_subStage is not null)
                Add(_subStage, _start);
        }
    }
}
