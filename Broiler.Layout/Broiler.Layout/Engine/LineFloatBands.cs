using System;
using System.Collections.Generic;

using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// The horizontal space each line box in one block has left after the floats in its block
/// formatting context have taken theirs (CSS2.1 §9.5, §9.4.2).
/// </summary>
/// <remarks>
/// <para>
/// A float is out of flow but not out of the way: line boxes beside it are shortened, which is
/// the whole point of floating something. Line layout used one <c>startx</c> and one
/// <c>limitRight</c> for the entire block, so nothing was shortened and text ran straight under
/// every float on the page — on <c>www.mediawiki.org</c> the article ran the full column width
/// underneath the thumbnail instead of stopping beside it, so no line after the first wrapped
/// where Chromium wrapped it and the rest of the page was displaced.
/// </para>
/// <para>
/// The band is only ever consulted for the vertical slice one line occupies, so it does not need
/// the full float geometry: it needs the innermost left and right margin edges among the floats
/// that overlap that slice, and the next float bottom to drop to when even the empty band is too
/// narrow for the word that has to go on the line.
/// </para>
/// </remarks>
internal sealed class LineFloatBands
{
    private static readonly LineFloatBands EmptyBands = new([]);

    private readonly List<CssBox> _floats;

    private LineFloatBands(List<CssBox> floats) => _floats = floats;

    internal static LineFloatBands Empty => EmptyBands;

    internal bool IsEmpty => _floats.Count == 0;

    /// <summary>
    /// The floats that shorten <paramref name="blockBox"/>'s lines: the ones earlier in its block
    /// formatting context, plus its own floated children once they have been laid out.
    /// </summary>
    /// <remarks>
    /// A float with no used size yet contributes nothing — on the first line-box pass a block's
    /// own floats have not been placed (they are laid out from the block path once the inline
    /// content that decides their vertical position exists), and a band drawn from their zero
    /// rectangle at the origin would shorten every line for a float that is not there.
    /// </remarks>
    internal static LineFloatBands For(CssBox blockBox)
    {
        List<CssBox>? floats = null;

        foreach (var candidate in CssBoxHelper.CollectPrecedingFloatsInBfc(blockBox))
            Add(ref floats, candidate);

        foreach (var child in blockBox.Boxes)
            Add(ref floats, child);

        return floats == null ? EmptyBands : new LineFloatBands(floats);
    }

    private static void Add(ref List<CssBox>? floats, CssBox candidate)
    {
        if (candidate.Float == CssConstants.None
            || candidate.Display == CssConstants.None
            || candidate.Size.Width <= 0
            || candidate.Size.Height <= 0)
        {
            return;
        }

        (floats ??= []).Add(candidate);
    }

    /// <summary>
    /// The leftmost x a line occupying <c>[top, top + height)</c> may use, given that it may not
    /// start before <paramref name="contentLeft"/>.
    /// </summary>
    internal double LeftAt(double top, double height, double contentLeft)
    {
        double edge = contentLeft;

        foreach (var floatBox in _floats)
        {
            if (floatBox.Float == CssConstants.Left && Overlaps(floatBox, top, height))
                edge = Math.Max(edge, floatBox.ActualRight + floatBox.ActualMarginRight);
        }

        return edge;
    }

    /// <summary>
    /// The rightmost x a line occupying <c>[top, top + height)</c> may use, given that it may not
    /// end after <paramref name="contentRight"/>.
    /// </summary>
    internal double RightAt(double top, double height, double contentRight)
    {
        double edge = contentRight;

        foreach (var floatBox in _floats)
        {
            if (floatBox.Float == CssConstants.Right && Overlaps(floatBox, top, height))
                edge = Math.Min(edge, floatBox.Location.X - floatBox.ActualMarginLeft);
        }

        return edge;
    }

    /// <summary>
    /// The next y below <paramref name="top"/> at which the band gets wider — the bottom margin
    /// edge of the shallowest float still overlapping the line — or <see cref="double.NaN"/> when
    /// no float does. CSS2.1 §9.5: a line that cannot fit its first word beside the floats is
    /// moved down past them rather than overflowing.
    /// </summary>
    internal double NextBandBottom(double top, double height)
    {
        double next = double.NaN;

        foreach (var floatBox in _floats)
        {
            if (!Overlaps(floatBox, top, height))
                continue;

            double bottom = floatBox.ActualBottom + floatBox.ActualMarginBottom;

            if (bottom > top && (double.IsNaN(next) || bottom < next))
                next = bottom;
        }

        return next;
    }

    /// <summary>
    /// Whether a float's margin box shares any of the vertical slice <c>[top, top + height)</c>.
    /// A zero-height line still occupies its own y, so an empty slice is treated as a hairline —
    /// otherwise a line whose height is not known yet would see no float at all.
    /// </summary>
    private static bool Overlaps(CssBox floatBox, double top, double height)
    {
        double floatTop = floatBox.Location.Y - floatBox.ActualMarginTop;
        double floatBottom = floatBox.ActualBottom + floatBox.ActualMarginBottom;

        return floatTop < top + Math.Max(height, 0.01) && floatBottom > top;
    }
}
