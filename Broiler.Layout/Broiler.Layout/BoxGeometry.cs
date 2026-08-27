using System.Drawing;

namespace Broiler.Layout;

/// <summary>
/// Read-only box geometry for a single laid-out element, in the renderer's
/// document coordinate space. The three rectangles correspond to the CSS
/// box-model levels: the border box (used by <c>offsetWidth/Height</c> and
/// <c>getBoundingClientRect</c>), the padding box, and the content box (used by
/// <c>clientWidth/Height</c>).
/// </summary>
/// <remarks>
/// Produced by <see cref="HtmlContainerInt.CollectLayoutGeometry"/> from the real
/// layout tree, so a consumer (for example the script bridge) can read accurate
/// element geometry instead of re-deriving it from computed style. Scroll extents
/// (<c>scrollWidth/Height</c>) are intentionally not included yet — they require
/// descendant-overflow computation and are added in a later increment.
/// </remarks>
public readonly record struct BoxGeometry(
    RectangleF BorderBox,
    RectangleF PaddingBox,
    RectangleF ContentBox)
{
    /// <summary>
    /// The three box-model levels of an <em>inline</em> box, whose border box has been rebuilt from
    /// the union of its per-line rectangles because a <c>display: inline</c> box lays out as one
    /// rectangle per line rather than as a single box.
    /// </summary>
    /// <param name="borderBox">The union of the box's per-line rectangles.</param>
    /// <param name="box">The box the rectangles belong to, read for its used border and padding.</param>
    /// <remarks>
    /// <para>
    /// For a <b>non-replaced</b> inline the three levels genuinely coincide here: this engine's line
    /// geometry carries no box-model padding or border for one, so there is nothing to deflate out
    /// and all three are the union.
    /// </para>
    /// <para>
    /// For a <b>replaced</b> inline they do not, and treating them as if they did is what made
    /// <c>clientWidth</c>/<c>clientHeight</c> wrong on every inline image with a border or padding.
    /// <c>CssLineBox.UpdateRectangle</c> adds an image's border and padding to its line rectangle
    /// explicitly, and <c>MeasureImageSize</c> adds them in the block axis, so the union really is
    /// the border box and the other two levels have to be deflated out of it.
    /// <c>&lt;img style="width:50px;height:30px;border:3px solid"&gt;</c> reported
    /// <c>getBoundingClientRect</c> 56×36 — right — and <c>clientWidth</c>/<c>clientHeight</c> 56×36
    /// too, where the client box excludes the border and must be 50×30. The same declarations on a
    /// <c>&lt;div&gt;</c> or an <c>inline-block</c> already reported 50×30, so the two paths
    /// disagreed with each other as well as with the spec.
    /// </para>
    /// </remarks>
    internal static BoxGeometry ForInlineBox(RectangleF borderBox, Engine.CssBox box)
    {
        if (box is null || !box.IsImage)
            return new BoxGeometry(borderBox, borderBox, borderBox);

        var paddingBox = Deflate(borderBox,
            box.ActualBorderLeftWidth, box.ActualBorderTopWidth,
            box.ActualBorderRightWidth, box.ActualBorderBottomWidth);

        var contentBox = Deflate(paddingBox,
            box.ActualPaddingLeft, box.ActualPaddingTop,
            box.ActualPaddingRight, box.ActualPaddingBottom);

        return new BoxGeometry(borderBox, paddingBox, contentBox);
    }

    /// <summary>Shrinks a rectangle by the given edges, never past zero extent.</summary>
    private static RectangleF Deflate(RectangleF r, double left, double top, double right, double bottom)
    {
        float x = r.X + (float)left;
        float y = r.Y + (float)top;
        float width = Math.Max(0f, r.Width - (float)(left + right));
        float height = Math.Max(0f, r.Height - (float)(top + bottom));
        return new RectangleF(x, y, width, height);
    }
}
