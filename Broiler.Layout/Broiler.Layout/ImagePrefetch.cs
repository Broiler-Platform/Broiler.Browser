using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Broiler.CSS;

namespace Broiler.Layout;

/// <summary>
/// The thread budget a document's image loads are issued on, and the diagnostics that say what the
/// walk decided. Multithreading roadmap item #8.
/// </summary>
/// <remarks>
/// <para>
/// <b>The item is a prefetch/consume split, not a <c>Parallel.For</c> over decoders.</b> Items #6
/// and #7 parallelize the inside of one decode; this parallelizes across the images a document has.
/// The blocker the master table named — <c>BImageRenderer._images</c> — was cleared by item #9, and
/// the one it did not name is what decides the shape: <c>ImageLoadHandler.SetImageFromFile</c>
/// already queues its decode to the thread pool <em>unless</em> <c>AvoidAsyncImagesLoading</c> is
/// set, and every headless entry point sets it, because the render is synchronous and must have
/// every image before layout measures a replaced box. So on exactly the paths this repository
/// measures, a document's images are read and decoded <b>one at a time, inline</b>, in the order
/// layout happens to reach them.
/// </para>
/// <para>
/// <b>Where the split lives, and why it needs no new seam.</b> Layout already asks the host for a
/// loader per image (<see cref="ILayoutEnvironment.CreateImageLoader"/>) and calls
/// <see cref="ILayoutImageLoader.LoadImage"/> on it — from inside <c>MeasureWordsSize</c>, which is
/// the inline serial point. The prefetch is that same pair of calls, for every image the document
/// names, issued from a worker before the layout pass starts; the consume site is
/// <c>MeasureWordsSize</c> itself, which finds the box's loader already populated and does nothing.
/// Nothing about which request is made, under which key, or what is done with the bytes changes —
/// only <em>when</em> each load starts and <em>on which thread</em> it runs. That is the same
/// property item #2's prefetcher has, and it is what makes the sequential path exactly reproducible
/// rather than approximately so.
/// </para>
/// <para>
/// <b>Two levels of decode parallelism compound here, unlike bands and tiles — and dividing them was
/// measured and does nothing.</b> A tile view runs its scanline bands inline, so a render spends one
/// or the other (<c>docs/architecture/multithreading.md</c> §7). Across-image and within-image decode
/// are not like that: <em>N</em> concurrent loads each splitting their own decode into items #6/#7's
/// <em>N</em> bands is <em>N²</em> runnable threads on <em>N</em> cores, so narrowing the inner budget
/// to <c>cores / loads</c> looks like the correction oversubscription obviously calls for. Over four
/// interleaved runs of the item's fixture it does not consistently do anything: layout undivided
/// 94.7 / 91.3 / 101.8 / 85.6 ms against divided 92.1 / 90.6 / 94.0 / 93.6, which is three runs
/// favouring the division by 1–8% and a fourth penalising it by 9%. The .NET pool already serialises
/// the excess, and the wave that runs with fewer than <em>N</em> loads left wants every core it can
/// get. So no division ships, the seam that would have exposed the decoder's budget to layout was
/// removed again, and <c>--image-prefetch-scaling</c> keeps the divided configuration as a column so
/// the null result stays re-runnable rather than remembered.
/// </para>
/// <para>
/// <b>A budget of 1 is the pre-change path, not an approximation of it.</b> At 1 the walk does not
/// run at all, so every box reaches <c>MeasureWordsSize</c> with no loader and creates one exactly
/// where it used to, in layout order, on the layout thread. That is what the exit gate compares
/// against.
/// </para>
/// </remarks>
public static class ImagePrefetch
{
    /// <summary>Environment variable that overrides the default thread budget.</summary>
    public const string ThreadsEnvironmentVariable = "BROILER_IMAGE_PREFETCH_THREADS";

    /// <summary>
    /// Images a document must name before the walk is worth running. Two, because one image
    /// concurrently is one image serially plus a task — there is nothing to overlap it with, since
    /// the layout pass that would have loaded it has not started.
    /// </summary>
    public const int MinimumImagesToOverlap = 2;

    private static int _maxDegreeOfParallelism = ReadConfiguredDegree();

    private static long _documentsWalked;
    private static long _loadsIssued;
    private static long _documentsBelowMinimum;
    private static long _documentsNotSynchronous;

    /// <summary>
    /// Maximum image loads in flight at once. <c>1</c> disables the walk entirely — see the class
    /// remarks; it is the pre-change path rather than a one-wide version of the new one.
    /// </summary>
    public static int MaxDegreeOfParallelism
    {
        get => _maxDegreeOfParallelism;
        set => _maxDegreeOfParallelism = Math.Max(1, value);
    }

    /// <summary>
    /// Whether to count what the walk decided. Off by default; read once per document, so the cost
    /// is a branch per render rather than per image.
    /// </summary>
    /// <remarks>
    /// The interesting question about this item is not how fast four concurrent decodes are — that
    /// is arithmetic — but <em>how many images a page gives the walk to overlap</em>, and how often
    /// a document is skipped for one of the two reasons it can be. A flat scaling row means
    /// something different in each case: "the loads did not overlap" and "there were no loads to
    /// overlap" call for opposite next steps.
    /// </remarks>
    public static bool CollectDiagnostics { get; set; }

    /// <summary>
    /// Documents whose images were issued by the walk, loads issued, documents skipped for naming
    /// fewer than <see cref="MinimumImagesToOverlap"/> images, and documents skipped because the
    /// host does not load images synchronously.
    /// </summary>
    /// <remarks>
    /// Process-wide and interlocked, unlike <c>BRasterParallelism</c>'s per-thread counters: this
    /// counts documents rather than fills, so there are a handful of writes per render rather than
    /// thousands, and a caller wants the total across the workers the walk itself created.
    /// </remarks>
    public static (long DocumentsWalked, long LoadsIssued, long DocumentsBelowMinimum, long DocumentsNotSynchronous)
        Diagnostics => (
            Interlocked.Read(ref _documentsWalked),
            Interlocked.Read(ref _loadsIssued),
            Interlocked.Read(ref _documentsBelowMinimum),
            Interlocked.Read(ref _documentsNotSynchronous));

    /// <summary>Zeroes the counters.</summary>
    public static void ResetDiagnostics()
    {
        Interlocked.Exchange(ref _documentsWalked, 0);
        Interlocked.Exchange(ref _loadsIssued, 0);
        Interlocked.Exchange(ref _documentsBelowMinimum, 0);
        Interlocked.Exchange(ref _documentsNotSynchronous, 0);
    }

    internal static void NoteSkippedBelowMinimum()
    {
        if (CollectDiagnostics)
            Interlocked.Increment(ref _documentsBelowMinimum);
    }

    internal static void NoteSkippedNotSynchronous()
    {
        if (CollectDiagnostics)
            Interlocked.Increment(ref _documentsNotSynchronous);
    }

    /// <summary>
    /// Whether a host's image-loading configuration is the one this item applies to: synchronous,
    /// with no late loading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>AvoidAsyncImagesLoading</c> is what makes the decode inline and serial, so it is what makes
    /// the item worth doing at all: with it clear, the host's loader already queues to the thread pool
    /// and there is nothing here to win.
    /// </para>
    /// <para>
    /// <b><c>AvoidImagesLateLoading</c> is in the test to bound the change, not because the change is
    /// unsafe without it.</b> A load that completes while late loading is permitted asks the host to
    /// refresh, and an earlier version of this item — which applied completions on the worker — would
    /// have made that call from a thread the host never agreed to be called back on.
    /// <see cref="DeferredImageLoad"/> removed that hazard by construction: every completion now runs
    /// on the layout thread at its original call site, refresh request included. The flag stays in the
    /// test because every headless entry point sets both, so requiring both keeps the item confined to
    /// the configuration it was measured in — and a host that permits late loading has an image
    /// lifecycle this item was not measured against.
    /// </para>
    /// </remarks>
    internal static bool AppliesTo(ILayoutEnvironment environment) =>
        environment.AvoidAsyncImagesLoading && environment.AvoidImagesLateLoading;

    /// <summary>
    /// Runs <paramref name="loads"/> concurrently, up to the budget, and returns once every one of
    /// them has finished. Each worker establishes the ambient render state before it runs a load and
    /// clears its record afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It joins, and the join is the correctness argument.</b> Every load has completed before
    /// this returns, so the layout pass that follows sees exactly the state it would have seen had
    /// each load run inline at the box that needed it: the same images, the same failures, the same
    /// error borders. What changes is that the loads happened at once and earlier. Returning before
    /// they finished would make a replaced box's intrinsic size depend on scheduling, which is the
    /// one thing a render must never do.
    /// </para>
    /// <para>
    /// <b>This is the first worker thread in the repository that runs render-path work, so it is the
    /// first that owes the ambient-state contract</b> (P0-c; <c>multithreading.md</c> §3 records the
    /// debt and says the item that creates a worker has to arm the enforcement in the same place it
    /// establishes). A load can reach SVG rasterization, which draws — so the thread is treated as a
    /// render thread whether or not today's decoders happen to read a thread-affine slot. Arming
    /// <see cref="AmbientRenderState.EnforceOnThisThread"/> is what turns "they happen not to" from
    /// an assumption into a checked claim, and it costs a debug-only branch.
    /// </para>
    /// </remarks>
    /// <param name="loads">One action per image load, each touching only its own box.</param>
    /// <param name="viewportWidth">Initial containing block width, for the workers' ambient state.</param>
    /// <param name="viewportHeight">Initial containing block height, for the workers' ambient state.</param>
    /// <param name="rootWritingMode">The root element's writing mode, which names the logical axes.</param>
    internal static void RunAll(
        IReadOnlyList<Action> loads,
        float viewportWidth,
        float viewportHeight,
        string? rootWritingMode)
    {
        ArgumentNullException.ThrowIfNull(loads);

        var threads = Math.Min(_maxDegreeOfParallelism, loads.Count);
        if (threads <= 1)
        {
            foreach (var load in loads)
                load();
            return;
        }

        // Read on the calling thread, which is the one that parsed the document; a worker that read
        // it would read whatever the last document on that pooled thread left behind.
        var quirksMode = DocumentModeContext.CurrentQuirksMode;

        if (CollectDiagnostics)
        {
            Interlocked.Increment(ref _documentsWalked);
            Interlocked.Add(ref _loadsIssued, loads.Count);
        }

        Parallel.For(
            0,
            loads.Count,
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            i =>
            {
                var enforcing = AmbientRenderState.EnforceOnThisThread;
                AmbientRenderState.Establish(viewportWidth, viewportHeight, rootWritingMode, quirksMode);
                AmbientRenderState.EnforceOnThisThread = true;
                try
                {
                    loads[i]();
                }
                finally
                {
                    // A pooled thread must not vouch for state the next document has not set, and
                    // must not leave the assertion armed for whatever runs on it next — this method
                    // is the only place in the process that arms it, so it is the only place that
                    // can be sure of turning it off.
                    AmbientRenderState.ClearThreadRecord();
                    AmbientRenderState.EnforceOnThisThread = enforcing;
                }
            });
    }

    private static int ReadConfiguredDegree()
    {
        var configured = Environment.GetEnvironmentVariable(ThreadsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) &&
            int.TryParse(configured, out var threads) &&
            threads > 0)
        {
            return threads;
        }

        return Environment.ProcessorCount;
    }
}
