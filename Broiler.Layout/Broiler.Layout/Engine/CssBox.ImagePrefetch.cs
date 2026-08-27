using System;
using System.Collections.Generic;
using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// The document-wide image walk that turns a serial run of inline decodes into one concurrent batch.
/// Multithreading roadmap item #8; the budget, the diagnostics and the reasoning are on
/// <see cref="ImagePrefetch"/>.
/// </summary>
internal partial class CssBox
{
    /// <summary>
    /// Issues every image load this document is certain to need, concurrently, before the layout
    /// pass reaches any of them. Called at the document root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>"Certain to need" is the whole of the correctness argument, and it is deliberately
    /// one-sided.</b> A box the walk skips loads its image exactly where it did before — inline, in
    /// <c>MeasureWordsSize</c> — so under-collecting costs speed and nothing else. Over-collecting
    /// costs correctness: an image loaded for a box layout never measures is a request on the wire
    /// the document did not make, and, for a broken source, an error reported that the host would
    /// never have seen. So the walk claims a box only when it can see that layout will reach it, and
    /// resolves everything else to "leave it alone".
    /// </para>
    /// <para>
    /// <b>What that rules out, concretely.</b> <c>PerformLayoutImp</c> measures words only when
    /// <c>Display != none</c>, so a <c>display:none</c> subtree is not entered. That is not the same
    /// as saying such a box never loads — <c>EnsureDescendantWordsMeasured</c>, the shrink-to-fit
    /// pre-pass, walks descendants without consulting <c>Display</c>, so an image inside a hidden
    /// subtree of an auto-width box does still load. Prefetching it would be correct on that page and
    /// wrong on the next one, and the walk cannot tell them apart without running layout, so it
    /// declines the whole class and those images load inline as they always did.
    /// </para>
    /// </remarks>
    internal static void PrefetchDocumentImages(CssBox root, ILayoutEnvironment g)
    {
        if (ImagePrefetch.MaxDegreeOfParallelism <= 1)
            return;

        if (!ImagePrefetch.AppliesTo(g))
        {
            ImagePrefetch.NoteSkippedNotSynchronous();
            return;
        }

        var pending = new List<CssBox>();
        CollectPendingImageBoxes(root, pending);

        if (pending.Count < ImagePrefetch.MinimumImagesToOverlap)
        {
            if (pending.Count > 0)
                ImagePrefetch.NoteSkippedBelowMinimum();
            return;
        }

        var loads = new Action[pending.Count];
        for (var i = 0; i < pending.Count; i++)
        {
            var box = pending[i];
            loads[i] = () => box.LoadImagesForPrefetch(g);
        }

        var viewport = g.ViewportSize;
        ImagePrefetch.RunAll(loads, viewport.Width, viewport.Height, root.WritingMode);
    }

    /// <summary>
    /// Collects the boxes with an unstarted image load, skipping <c>display:none</c> subtrees.
    /// Iterative rather than recursive: a deep document is a deep box tree, and the walk runs at the
    /// root of every render.
    /// </summary>
    private static void CollectPendingImageBoxes(CssBox root, List<CssBox> pending)
    {
        var stack = new Stack<CssBox>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var box = stack.Pop();
            if (box.Display == CssConstants.None)
                continue;

            if (box.HasPendingImageLoad)
                pending.Add(box);

            foreach (var child in box.Boxes)
                stack.Push(child);
        }
    }

    /// <summary>
    /// Whether this box has an image load that has not been started yet. The background-image test is
    /// the same pair of conditions <see cref="LoadBackgroundImageIfNeeded"/> opens with, so a box that
    /// would do nothing is never claimed; <see cref="CssBoxImage"/> adds its replaced-content image.
    /// </summary>
    internal virtual bool HasPendingImageLoad =>
        BackgroundImage != CssConstants.None && !_backgroundImagesInitialized;

    /// <summary>
    /// Runs this box's pending image loads on a prefetch worker. Overridden by
    /// <see cref="CssBoxImage"/>, whose replaced content is a second load.
    /// </summary>
    /// <remarks>
    /// <b>It calls the same methods layout would.</b> Not a copy of them: the flags they set
    /// (<c>_backgroundImagesInitialized</c>, <c>_imageLoadHandler</c>) are what makes the later
    /// <c>MeasureWordsSize</c> skip the load for this box, so the prefetch and the consume site cannot
    /// drift apart into two ways of loading an image. What the consume site still does is apply the
    /// completion this worker captured — the decode moves, the callback does not
    /// (<see cref="DeferredImageLoad"/>).
    /// </remarks>
    internal virtual void LoadImagesForPrefetch(ILayoutEnvironment g) =>
        LoadBackgroundImages(deferCompletions: true);
}
