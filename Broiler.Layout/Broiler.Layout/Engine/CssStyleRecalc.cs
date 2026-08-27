using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Broiler.Layout.Engine;

/// <summary>
/// Resolves every element's cascade on a thread budget before the box walk consumes it —
/// multithreading roadmap item #12, "parallel style recalc".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it lives here and is called from there.</b> The only caller is
/// <c>DomParser.PrepareCssTree</c>, in the <c>Broiler.HTML</c> submodule, which is the reason to
/// look for the box walk this talks about over there rather than in this assembly. The type is on
/// this side of the line because <c>Broiler.Layout</c> owns <c>CssBox</c> and already references the
/// style engine, and because a submodule patch that carries a whole feature is a patch that breaks
/// the main repository's build the moment it is reverted — the same split
/// <c>Broiler.Layout.IR.TileParallelReplay</c> uses for item #5.
/// </para>
/// <para>
/// <b>Why this is a prefetch/consume split and not a parallel tree walk.</b> The obvious reading of
/// item #12 is to thread <c>CascadeApplyStyles</c> itself, over sibling subtrees. That walk cannot
/// be split without changing what it produces: it calls <c>InheritStyle</c> from the parent box
/// before a child is touched, rewrites <c>display</c> when <c>float</c> is set so children observe
/// the corrected value, pushes <c>text-decoration</c> down onto children, hides a closed
/// <c>&lt;details&gt;</c>'s subtree <em>after</em> the subtree has been cascaded, and inserts
/// generated <c>::before</c>/<c>::after</c> boxes into child lists on the way back up. Every one of
/// those is an ordered mutation of shared tree state, and the ones that write to a node the walk
/// has already left are not even expressible as an independent-subtree claim.
/// </para>
/// <para>
/// The expensive part is not in the walk at all. Per box the walk does a handful of field
/// assignments and one call to <c>CssStyleEngine.GetCascadedStyle</c>, and that call is the cascade
/// — selector matching, custom-property and <c>var()</c> resolution, CSS-wide keywords, shorthand
/// expansion, relative font weights. It reads the canonical <see cref="Broiler.Dom"/> tree and the
/// registered stylesheets, neither of which the box walk mutates, so it is a pure function of state
/// that is already final when this runs. That is what gets threaded: this pass resolves it for every
/// element up front and leaves the results in the engine's memo, and the box walk afterwards is
/// byte-for-byte the walk that was there before, reading cache hits instead of computing.
/// </para>
/// <para>
/// <b>Determinism.</b> Each element's cascade is independent of every other element's, so the
/// results do not depend on the order they are resolved in or on how many threads resolve them.
/// Two threads that race the same ancestor both compute the same value and one store wins, which is
/// the benign duplicate the engine's cache already documents. The exit gate is the strong one: the
/// same computed style, and the same rendered pixels, at one thread and at N.
/// </para>
/// <para>
/// <b>Elements are handed out in document order.</b> Resolving an element recurses into its
/// ancestors' computed styles, so a chunk whose elements are contiguous in the document shares
/// almost all of its ancestor chain and pays for it once. Ranges over a document-ordered array give
/// that for free; a random partition would multiply the ancestor work by the thread count.
/// </para>
/// <para>
/// <b>Budget, and who else is spending it.</b> One thread per core by default, overridable with
/// <c>BROILER_STYLE_THREADS</c>. The same warning as the rasterizer's budget applies and matters
/// more here, because this runs earlier: a host already running several renders at once (the WPT
/// worker pool, the CLI's batch processes) should set it down, or N processes each spawning N style
/// threads compete for N cores.
/// </para>
/// </remarks>
internal static class CssStyleRecalc
{
    /// <summary>Environment variable that overrides the default thread budget.</summary>
    internal const string ThreadsEnvironmentVariable = "BROILER_STYLE_THREADS";

    /// <summary>
    /// Elements a document must have before the cascade is resolved in parallel at all.
    /// </summary>
    /// <remarks>
    /// A WPT reftest is frequently a dozen elements; dispatching those costs more than cascading
    /// them, and the runner renders thousands of such documents. The threshold is low enough that
    /// any page with real content clears it and high enough that the small-document case takes
    /// exactly the path it took before — one thread, no scheduler.
    /// </remarks>
    internal static int MinimumParallelElements { get; set; } =
        ReadConfiguredCount("BROILER_STYLE_MIN_ELEMENTS", 64);

    private static int _maxDegreeOfParallelism = ReadConfiguredDegree();

    /// <summary>Threads the warm pass may use. One per core unless overridden; 1 disables it.</summary>
    internal static int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = value < 1 ? 1 : value;
    }

    private static int ReadConfiguredDegree()
    {
        var configured = Environment.GetEnvironmentVariable(ThreadsEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, out var threads) && threads > 0
            ? threads
            : Environment.ProcessorCount;
    }

    private static int ReadConfiguredCount(string variable, int fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(configured) && int.TryParse(configured, out var count) && count > 0
            ? count
            : fallback;
    }

    /// <summary>
    /// Resolves the cascade for every element under <paramref name="root"/> into
    /// <paramref name="engine"/>'s memo, on <see cref="MaxDegreeOfParallelism"/> threads.
    /// A no-op below <see cref="MinimumParallelElements"/> or at a budget of one — in both cases the
    /// box walk computes as it goes, exactly as it did before this pass existed.
    /// </summary>
    internal static void Warm(CssBox root, Broiler.CSS.Dom.CssStyleEngine? engine)
    {
        var threads = MaxDegreeOfParallelism;
        if (engine is null || root is null || threads < 2)
            return;

        var elements = new List<Broiler.Dom.DomElement>();
        Collect(root, elements);
        if (elements.Count < MinimumParallelElements)
            return;

        try
        {
            // Ranges rather than one work item per element: the per-element cascade is tens of
            // microseconds, so a scheduler dispatch per element would be a visible share of it, and
            // contiguous ranges are also what keeps the shared ancestor chain warm (see the remarks).
            Parallel.ForEach(
                Partitioner.Create(0, elements.Count),
                new ParallelOptions { MaxDegreeOfParallelism = threads },
                range =>
                {
                    for (var i = range.Item1; i < range.Item2; i++)
                        engine.GetCascadedStyle(elements[i], includeInlineStyle: true);
                });
        }
        catch (Exception)
        {
            // A pass that only moves *when* work happens must not be able to change *whether* the
            // render succeeds. Every call above is one the box walk is about to make anyway, so an
            // element that throws here throws again at the same element a moment later, from the
            // walk, with the same exception and the same partially-styled tree behind it — which is
            // exactly what happened before this pass existed. Swallowing therefore loses no
            // diagnostic and removes the one way this could fail a render the sequential path would
            // have completed: an element the warm pass reaches and the walk does not. `Collect` is
            // already a subset of what the walk queries, so that case should not exist; this is the
            // guard that makes "should not" not matter. Item #17's scan is written the same way and
            // for the same reason.
        }
    }

    /// <summary>
    /// Collects the source elements of the box tree in document order, skipping the subtrees the
    /// box walk itself skips.
    /// </summary>
    /// <remarks>
    /// The <c>&lt;template&gt;</c> exclusion mirrors <c>CascadeParseStyles</c>: template contents
    /// are inert, produce no boxes, and must not be styled as part of the host document. Resolving
    /// them here would be wasted work rather than a wrong render — nothing reads the result — but a
    /// warm pass that walks a different tree from the walk it is warming is a bug waiting for the
    /// first consumer that notices.
    /// </remarks>
    private static void Collect(CssBox box, List<Broiler.Dom.DomElement> elements)
    {
        if (box.HtmlTag != null &&
            box.HtmlTag.Name.Equals("template", StringComparison.OrdinalIgnoreCase))
            return;

        if (box.HtmlTag != null && box.SourceElement is { } element)
            elements.Add(element);

        foreach (var child in box.Boxes)
            Collect(child, elements);
    }
}
