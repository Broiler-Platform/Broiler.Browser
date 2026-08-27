using System.Drawing;

namespace Broiler.Layout.IR;

/// <summary>
/// A surface that can say, before any work is done for a primitive, that nothing inside a given
/// rectangle can reach a pixel it is allowed to write.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the surface answers and not the caller.</b> Display-list geometry is in layout
/// coordinates and the surface maps it — a document zoom, a scroll offset, a transform layer — so
/// only the surface can put a rectangle in the same space as its clip. A caller comparing item
/// coordinates against a device rectangle of its own would be right only while no transform was in
/// effect.
/// </para>
/// <para>
/// <b>What it saves, and why that is more than skipping a loop.</b> Rejecting a primitive on its
/// bounds inside the rasterizer already avoids drawing it, but the caller has usually paid for it
/// by then: a tiled gradient allocates and renders a gradient tile bitmap before it draws a single
/// pixel with it. Under tile-parallel replay every tile walks the whole list, so that setup would
/// otherwise be paid once per tile for a primitive only one tile can see —
/// <see cref="TileParallelReplay"/> for the measurement that made this necessary rather than nice.
/// </para>
/// <para>
/// <b>It is an optimisation, so it errs towards drawing.</b> A surface that cannot answer returns
/// <c>false</c> and the primitive runs, and a caller must only offer bounds the primitive certainly
/// draws inside — the rectangle the renderer itself derives its geometry from, not an approximation
/// of it. Culling something visible is a missing pixel; culling nothing is a slow render.
/// </para>
/// </remarks>
public interface IBoundsCullingSurface
{
    /// <summary>
    /// Whether a primitive confined to <paramref name="bounds"/> — in the coordinate space
    /// display-list items use — can be skipped entirely.
    /// </summary>
    bool IsCulled(RectangleF bounds);
}
