using System;
using System.Diagnostics;
using Broiler.CSS;

namespace Broiler.Layout;

/// <summary>
/// The thread-affine state a thread must establish before it may run layout or paint, and the
/// debug-mode check that says whether it did.
/// </summary>
/// <remarks>
/// <para>
/// This is multithreading roadmap item P0-c's second half — <i>a debug-mode assertion that fires
/// when a thread-static ambient value is read before being set on that thread</i>. The audit's list
/// of what that state is, and the reasoning behind each classification, is
/// <c>docs/architecture/multithreading-static-state.md</c>.
/// </para>
/// <para>
/// <b>Why this needs an assertion rather than a convention.</b> Every value below is
/// <c>[ThreadStatic]</c>, so a worker thread that never sets one does not fail — it reads the
/// default. A viewport of zero silently makes every <c>vh</c>/<c>vw</c> length zero; an unset
/// quirks-mode flag silently means standards mode; an unset SVG table silently resolves every
/// <c>url(#id)</c> to nothing. None of those throw, and all of them produce a plausible-looking
/// wrong render. That is the failure mode parallel layout (items #12, #13) would introduce, and it
/// is invisible without a check.
/// </para>
/// <para>
/// <b>Armed by <see cref="EnforceOnThisThread"/>, and off by default.</b> It stays off because the
/// check is for threads that skip the contract, and arming it process-wide would make every
/// existing single-threaded render pay for a hazard it does not have; the switch is for the code
/// that introduces worker threads, which is also the code that calls <see cref="Establish"/>.
/// </para>
/// <para>
/// <b>Both slot gaps this used to name are now closed</b> (Phase 2, item #9). The HTML-string render
/// path publishes <see cref="DocumentModeContext.CurrentQuirksMode"/> at
/// <c>HtmlContainerInt.SetHtmlWithStyleSet</c>, so the flag no longer depends on whichever document
/// this pooled thread rendered last — the failure that made <see cref="DocumentModeContext"/>'s own
/// claim ("each parse overwrites it, so a stale <c>true</c> never leaks") true only for as long as
/// one thread rendered one document at a time. And <see cref="Slots.Viewport"/> now has a read-side
/// assertion, in <c>CssLengthParser</c> itself, because that is the assembly its eight thread-statics
/// live in; <see cref="EstablishedOnThisThread"/> reads the bit from there so that
/// <c>HtmlContainerInt.PerformLayout</c>, which sets the viewport directly, is credited for it.
/// </para>
/// <para>
/// <b>Per-slot rather than one flag.</b> Establishing the viewport does not establish the quirks
/// flag, and a single "this thread is ready" bit would let a reader of one slot be satisfied by a
/// writer of another — which is the class of mistake this is meant to find.
/// </para>
/// </remarks>
public static class AmbientRenderState
{
    /// <summary>The thread-affine slots a render thread has to fill.</summary>
    [Flags]
    public enum Slots
    {
        /// <summary>Nothing established.</summary>
        None = 0,

        /// <summary>
        /// <c>CssLengthParser</c>'s viewport factors — <c>vh</c>, <c>vw</c>, <c>vmin</c>,
        /// <c>vmax</c>, and the logical <c>vi</c>/<c>vb</c>, which also need the root writing mode.
        /// </summary>
        Viewport = 1,

        /// <summary><see cref="DocumentModeContext.CurrentQuirksMode"/>.</summary>
        DocumentMode = 2,

        /// <summary>
        /// <see cref="IR.SvgFilterTable"/>, <see cref="IR.SvgClipPathTable"/> and
        /// <see cref="IR.SvgTextEnvironment"/>.
        /// </summary>
        SvgTables = 4,

        /// <summary>Everything a thread running layout or paint needs.</summary>
        All = Viewport | DocumentMode | SvgTables,
    }

    [ThreadStatic]
    private static Slots _established;

    /// <summary>What this thread has established. Diagnostic; the assertion is the intended use.</summary>
    /// <remarks>
    /// The <see cref="Slots.Viewport"/> bit is read from <c>CssLengthParser</c> rather than from this
    /// record. That slot's state lives in Broiler.CSS, which cannot reference this assembly, and
    /// <c>HtmlContainerInt.PerformLayout</c> establishes it by calling
    /// <c>CssLengthParser.SetViewportSize</c> directly rather than through <see cref="Establish"/> —
    /// so crediting only what passed through here would report the repository's own render path as
    /// having skipped the contract it in fact satisfies.
    /// </remarks>
    public static Slots EstablishedOnThisThread =>
        _established
        | (CssLengthParser.ViewportEstablishedOnThisThread ? Slots.Viewport : Slots.None);

    [ThreadStatic]
    private static bool _enforce;

    /// <summary>
    /// Whether <see cref="AssertEstablished"/> reports on the calling thread. Off by default; see
    /// the class remarks for the reason, which is a real gap on the HTML-string path rather than a
    /// precaution.
    /// </summary>
    /// <remarks>
    /// <b>Per thread rather than process-wide, and that is not merely tidier.</b> The threads that
    /// want checking are the worker threads a parallel render creates, and they are exactly the
    /// threads that call <see cref="Establish"/> — so arming is one more line in the same place.
    /// A process-wide switch would additionally be unusable from a test: xunit runs an assembly's
    /// test classes concurrently, so a case that armed it would arm it for whatever layout test
    /// happened to be running beside it, and that test would throw on a perfectly ordinary read of
    /// <see cref="DocumentModeContext.CurrentQuirksMode"/>. A flaky assertion about thread safety
    /// would be a poor advertisement for the thing it is asserting.
    /// </remarks>
    public static bool EnforceOnThisThread
    {
        get => _enforce;
        set
        {
            _enforce = value;
            // The viewport slot asserts from inside Broiler.CSS, which has no way to
            // see this switch; arming both from one place keeps "enforce" a single
            // decision rather than two that can disagree.
            CssLengthParser.EnforceViewportAssertion = value;
        }
    }

    /// <summary>
    /// Fills every slot for the calling thread. This is the one call a worker thread that is about
    /// to run layout or paint should make.
    /// </summary>
    /// <param name="viewportWidth">Initial containing block width, in CSS pixels.</param>
    /// <param name="viewportHeight">Initial containing block height, in CSS pixels.</param>
    /// <param name="rootWritingMode">
    /// The <em>root</em> element's writing mode, which is what names the inline and block axes for
    /// <c>vi</c>/<c>vb</c> (CSS Values 4 section 6.1.4) — not the writing mode of whatever box is
    /// being laid out.
    /// </param>
    /// <param name="quirksMode">The document's quirks-mode flag.</param>
    public static void Establish(
        float viewportWidth, float viewportHeight, string? rootWritingMode, bool quirksMode)
    {
        CssLengthParser.SetViewportSize(viewportWidth, viewportHeight, rootWritingMode);
        DocumentModeContext.CurrentQuirksMode = quirksMode;
        IR.SvgFilterTable.ResetForThread();
        IR.SvgClipPathTable.ResetForThread();
        IR.SvgTextEnvironment.ResetForThread();
        _established = Slots.All;
    }

    /// <summary>
    /// Records that one slot has been filled on this thread. Called by the setters themselves, so a
    /// caller that establishes state the long way round is credited for it.
    /// </summary>
    internal static void MarkEstablished(Slots slot) => _established |= slot;

    /// <summary>
    /// Clears this thread's record, so a pooled thread returning to the pool does not vouch for
    /// state the next document has not set.
    /// </summary>
    /// <remarks>
    /// It deliberately does not clear the values themselves. Clearing them would turn a stale read
    /// into a zero-viewport read, which is just as wrong and harder to recognise; leaving them and
    /// clearing the record is what makes the next unestablished read assert instead.
    /// </remarks>
    public static void ClearThreadRecord()
    {
        _established = Slots.None;
        CssLengthParser.ClearViewportEstablishedRecord();
    }

    /// <summary>
    /// Fires when <paramref name="slot"/> is read on a thread that never set it, and only while
    /// <see cref="EnforceOnThisThread"/> is on for that thread.
    /// </summary>
    /// <param name="slot">The slot being read.</param>
    /// <param name="site">Reader name, for the message. Use the property or method being read.</param>
    [Conditional("DEBUG")]
    public static void AssertEstablished(Slots slot, string site)
    {
        if (!_enforce || (EstablishedOnThisThread & slot) == slot)
            return;

        throw new InvalidOperationException(
            $"{site} read the ambient {slot} state on thread "
            + $"{Environment.CurrentManagedThreadId}, which never established it. A thread that runs "
            + "layout or paint must call AmbientRenderState.Establish first; see "
            + "docs/architecture/multithreading-static-state.md.");
    }
}
