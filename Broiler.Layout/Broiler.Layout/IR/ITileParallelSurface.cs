using System.Drawing;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// A raster surface that can hand out independent views of disjoint rectangles of itself, so one
/// <see cref="DisplayList"/> can be replayed into several of them at once. Implemented by the
/// surface, driven by <see cref="TileParallelReplay"/>. Multithreading roadmap item #5.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the surface decides and not the backend.</b> Whether two threads may write pixels of the
/// same surface is a property of the surface — a managed pixel buffer tolerates it, a surface that
/// mirrors into a platform bitmap does not, and only the surface knows which it is. The replay
/// driver asks; a surface that does not implement this interface, or answers with an empty size, is
/// replayed sequentially exactly as before.
/// </para>
/// <para>
/// <b>A tile view is the surface in its current state, narrowed.</b> The replay starts partway
/// through a paint — a viewport clip is pushed, a document zoom may be composed — so a view that
/// began from a blank state would draw a different picture. <see cref="CreateTileView"/> is
/// therefore specified to carry the current transform and clip stack across, and add the tile as
/// one further clip. Everything else about the arithmetic is unchanged, which is what makes the
/// tiled image byte-identical to the sequential one rather than merely close to it.
/// </para>
/// </remarks>
public interface ITileParallelSurface
{
    /// <summary>
    /// Device-pixel size of the region that may be tiled, or <see cref="Size.Empty"/> when this
    /// surface cannot be drawn into from more than one thread right now.
    /// </summary>
    /// <remarks>
    /// Read once, before any tile is created. It is allowed to change over a surface's life — a
    /// surface that has materialized a platform bitmap stops being tileable — so the driver takes
    /// the answer for the replay it is about to run and does not cache it.
    /// </remarks>
    Size TileParallelSurfaceSize { get; }

    /// <summary>
    /// Creates an independent view of this surface that draws only inside <paramref name="tile"/>,
    /// carrying the current transform and clip state across. The caller disposes it when the tile's
    /// replay has finished.
    /// </summary>
    /// <param name="tile">Device-pixel rectangle the view may write, in surface coordinates.</param>
    RGraphics CreateTileView(Rectangle tile);
}
