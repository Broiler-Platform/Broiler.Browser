namespace Broiler.Layout;

/// <summary>
/// Services a host refresh so that a refresh requested from <em>inside</em> that refresh is folded
/// into a bounded follow-up pass instead of recursing into it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ILayoutEnvironment.RequestRefresh"/> is called from inside a layout pass — a late
/// image load does it from <c>CssBoxImage.OnLoadImageComplete</c> and
/// <c>CssBox.Background.OnImageLoadComplete</c> — and the host's natural response is to lay the
/// document out again. That relayout re-enters the same code, which requests another refresh, and
/// nothing along the path (here, <c>Broiler.HTML.Dom.LayoutEnvironment</c>, or
/// <c>HtmlContainerInt.RequestRefresh</c>) bounded it. Every other recursion-capable path in the
/// engine carries a depth cap, a visiting set or a re-entrancy flag; this one carried none, so a
/// host that relayouts unconditionally in its handler recursed until the stack gave out.
/// </para>
/// <para>
/// A re-entrant request is <em>coalesced</em> rather than dropped. By the time it arrives the
/// in-flight pass has already been dispatched to the handler and cannot absorb it, so discarding it
/// would lose real work: a <c>layout: false</c> pass that uncovers a relayout would never run one.
/// Servicing it iteratively instead keeps the stack flat and puts a ceiling on the chain — the same
/// shape as the table-width redistribution and event-loop drain loops.
/// </para>
/// <para>
/// The pass chain is per thread and per coalescer. Per thread because an asynchronous image
/// completes on whichever thread finished it and must still be able to refresh while another thread
/// services one of its own; per coalescer because a nested browsing context's container is a
/// separate instance with a chain of its own. Nesting is honoured in both directions: a refresh of
/// container A whose handler refreshes frame B runs B's pass normally, and a further refresh of A
/// from inside B is folded back into A's pass, which is the cycle that has to be caught.
/// </para>
/// <para>
/// The type lives here, beside the interface whose contract it bounds, rather than in the renderer:
/// the renderer references <c>Broiler.Layout</c> and not the other way round, so this is the lowest
/// layer both the raise site and the request site can see.
/// </para>
/// </remarks>
public sealed class RefreshCoalescer
{
    /// <summary>
    /// How many times one <see cref="Request"/> raises the host refresh before the chain is cut.
    /// A late image load legitimately causes a relayout, and that relayout can complete a second
    /// deferred load which legitimately causes another, so a short chain is real work; a chain
    /// longer than this is a handler refreshing unconditionally, and running it to convergence is
    /// exactly the non-termination being prevented.
    /// </summary>
    public const int MaxPasses = 4;

    /// <summary>One in-flight <see cref="Request"/>, and the pass it was raised from, if any.</summary>
    private sealed class Pass(RefreshCoalescer owner, Pass? outer)
    {
        public RefreshCoalescer Owner { get; } = owner;

        public Pass? Outer { get; } = outer;

        /// <summary>Whether a refresh was requested while this pass was being serviced.</summary>
        public bool Pending;

        /// <summary>Whether any of those requests asked for a relayout.</summary>
        public bool PendingLayout;
    }

    [ThreadStatic]
    private static Pass? _current;

    /// <summary>
    /// Raises the host refresh through <paramref name="raise"/>, or — when this coalescer is
    /// already servicing a refresh on this thread — records the request against that pass and
    /// returns without raising.
    /// </summary>
    /// <param name="layout">Whether the host should lay out again, not merely repaint.</param>
    /// <param name="raise">
    /// Raises the host's refresh event. Called at most <see cref="MaxPasses"/> times per
    /// <see cref="Request"/>, always on the calling thread, and expected to swallow and report
    /// handler exceptions itself — this method does not catch, so a throw from it leaves the pass
    /// chain intact but abandons the remaining passes.
    /// </param>
    public void Request(bool layout, Action<bool> raise)
    {
        ArgumentNullException.ThrowIfNull(raise);

        for (var enclosing = _current; enclosing is not null; enclosing = enclosing.Outer)
        {
            if (ReferenceEquals(enclosing.Owner, this))
            {
                enclosing.Pending = true;
                enclosing.PendingLayout |= layout;
                return;
            }
        }

        var pass = new Pass(this, _current);
        _current = pass;
        try
        {
            for (var raised = 1; ; raised++)
            {
                // Cleared before the raise, not after: the re-entrant requests this pass is
                // collecting are the ones made *during* raise(), and a follow-up pass collects
                // its own.
                pass.Pending = false;
                pass.PendingLayout = false;

                raise(layout);

                if (!pass.Pending || raised >= MaxPasses)
                    break;

                layout = pass.PendingLayout;
            }
        }
        finally
        {
            _current = pass.Outer;
        }
    }
}
