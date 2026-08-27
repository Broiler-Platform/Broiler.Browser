using Broiler.Graphics;

namespace Broiler.Layout.Engine;

/// <summary>
/// The colour the canvas composites a translucent propagated background against — the paint that is
/// already on the surface underneath it, when the caller knows it is not the white the paint walker
/// otherwise assumes.
/// </summary>
/// <remarks>
/// <para>
/// CSS 2.1 §14.2 propagates the root element's (or <c>&lt;body&gt;</c>'s) background to the canvas,
/// and the paint walker emits it as one opaque fill: a half-transparent background is flattened
/// against the canvas backdrop first, which is white unless the document's used colour scheme is
/// dark. That is right for a render onto a blank surface and wrong for one onto a surface that
/// already carries paint — CSS Paged Media 3 §7.2's page background is exactly that case, and
/// <c>page-box-002-print</c> is the reftest that states it: <c>body { background: #f008 }</c> over
/// <c>@page { background: #00f }</c> must come out violet, not pink.
/// </para>
/// <para>
/// Thread-static and null by default, like the engine's other render levers
/// (<see cref="NativeZoom.Enabled"/>, <see cref="NativeAnchorPlacement.Enabled"/>): a caller sets it
/// around one render on one thread, and every render that does not is byte-identical to before. It
/// carries a single colour rather than the surface because the flatten produces a single colour —
/// so the WPT runner sets it only when the page background under the whole page area is one flat
/// colour, and leaves the white assumption standing when it is an image or a gradient.
/// </para>
/// </remarks>
internal static class CanvasBackdrop
{
    [System.ThreadStatic]
    public static BColor? Current;
}
