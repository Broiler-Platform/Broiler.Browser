using System;
using System.Drawing;

namespace Broiler.Layout;

/// <summary>
/// Holds an image load's completion until the layout thread reaches the point that would have run it,
/// then runs it there. Multithreading roadmap item #8.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what makes the prefetch's output identical rather than merely equivalent.</b> A load has
/// two halves: the expensive one (resolve the source, read the bytes, decode them) and the one that
/// touches the box (store the image, store its rectangle, set the error border on failure, and ask the
/// host to refresh). Only the first is safe to move: the second is where the serial path's <em>timing</em>
/// is observable, because a box that acquires a 2px error border before its width is resolved lays out
/// differently from one that acquires it after.
/// </para>
/// <para>
/// <b>That is not a hypothesis; it is what the exit gate caught.</b> With the completion applied on the
/// worker, a document of three broken <c>&lt;img&gt;</c>s rendered 6–10 pixels wider than the serial
/// render — the error borders landed early enough to widen boxes the serial path had already measured.
/// Every document with working images was byte-identical, which is exactly how this class of defect
/// survives a test suite: the failure path is the one whose callback does something.
/// </para>
/// <para>
/// <b>Why capture-and-replay rather than a two-phase loader.</b> The host's loader takes its completion
/// callback at construction and invokes it inside <c>LoadImage</c>; asking it to separate decoding from
/// notifying would change <c>ILayoutImageLoader</c> and every implementation of it. Wrapping the callback
/// needs neither, and it makes the identity argument structural: the real callback runs on the layout
/// thread, at the same call site, in the same order, with the arguments the loader produced. There is no
/// document for which that can differ, so nothing rests on which documents were tried.
/// </para>
/// <para>
/// <b>After <see cref="Release"/> it is a pass-through.</b> A host may complete a load more than once —
/// a late load replacing a placeholder — and those completions belong to whoever asked for them, not to
/// the prefetch. So the wrapper forwards anything that arrives after the deferred completion has been
/// applied, which also means a load that never completed during the prefetch is simply a load that
/// completes normally later.
/// </para>
/// </remarks>
internal sealed class DeferredImageLoad(Action<object?, RectangleF, bool> target)
{
    private readonly Action<object?, RectangleF, bool> _target =
        target ?? throw new ArgumentNullException(nameof(target));

    private readonly object _gate = new();
    private object? _image;
    private RectangleF _rectangle;
    private bool _async;
    private bool _captured;
    private bool _released;

    /// <summary>
    /// The callback the loader is given. Captures the completion while the load is a speculation and
    /// forwards it once it is not.
    /// </summary>
    /// <remarks>
    /// Locked rather than volatile, because it publishes three values that have to be seen together:
    /// a torn read here would apply one load's image with another's rectangle. The lock is taken once
    /// per image, which is not a rate worth optimising.
    /// </remarks>
    public void OnCompleted(object? image, RectangleF rectangle, bool async)
    {
        lock (_gate)
        {
            if (!_released)
            {
                _image = image;
                _rectangle = rectangle;
                _async = async;
                _captured = true;
                return;
            }
        }

        _target(image, rectangle, async);
    }

    /// <summary>
    /// Applies the captured completion, if the load produced one, and stops deferring. Called on the
    /// layout thread from the site the serial path would have completed at.
    /// </summary>
    public void Release()
    {
        object? image;
        RectangleF rectangle;
        bool async;

        lock (_gate)
        {
            if (_released)
                return;

            _released = true;
            if (!_captured)
                return;

            image = _image;
            rectangle = _rectangle;
            async = _async;
        }

        // Outside the lock: the callback runs arbitrary layout code (it sets an error border, and it
        // may ask the host to refresh), and none of that should run holding this object's gate.
        _target(image, rectangle, async);
    }
}
