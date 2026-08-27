using Broiler.CSS;

namespace Broiler.Layout;

/// <summary>
/// The physical inset property an <c>anchor()</c> function is resolving into. Only
/// <see cref="Right"/> and <see cref="Bottom"/> change the result (a right/bottom
/// inset is the distance from the containing block's opposite edge); every other
/// property (including left/top) uses the raw edge coordinate.
/// </summary>
public enum AnchorInsetProperty
{
    Left,
    Top,
    Right,
    Bottom,
    Other,
}

/// <summary>
/// Used-value geometry for the CSS anchor-positioning query functions and
/// <c>@position-try</c> fallback fitting: resolving an <c>anchor()</c> edge or
/// <c>anchor-size()</c> dimension to a length, and the overflow/fit predicates that
/// drive position-try fallback selection.
/// </summary>
/// <remarks>
/// Moved into <c>Broiler.Layout</c> (HtmlBridge complexity-reduction roadmap, Phase 5
/// work item 3) alongside <see cref="PositionAreaGrid"/>. It consumes the
/// <c>Broiler.CSS</c> anchor-function models (<see cref="AnchorSide"/>,
/// <see cref="AnchorSizeDimension"/>) and produces lengths/booleans; it holds no DOM,
/// cascade, anchor-registry, or scroll-resolution knowledge — the caller supplies the
/// already-resolved anchor rect, containing-block dimensions, and scroll adjustment.
/// Unifies the two copies of the edge math that previously lived in the bridge's
/// <c>ResolveAnchorFunctions</c> and <c>ResolveAnchorEdge</c>.
/// </remarks>
public static class AnchorGeometry
{
    /// <summary>
    /// Resolves an <c>anchor(&lt;side&gt;)</c> reference to a length in the containing
    /// block's coordinate frame. The chosen anchor edge is shifted by the scroll
    /// adjustment (x for the inline edges, y for the block edges and
    /// <see cref="AnchorSide.Center"/>, which uses the anchor's vertical centre), then,
    /// when resolving a right/bottom inset (<paramref name="property"/>), converted to
    /// the distance from the containing block's opposite edge.
    /// </summary>
    public static double ResolveEdge(
        double anchorLeft, double anchorTop, double anchorRight, double anchorBottom,
        AnchorSide side, double scrollAdjX, double scrollAdjY,
        AnchorInsetProperty property, double cbWidth, double cbHeight)
    {
        double raw = side switch
        {
            AnchorSide.Top => anchorTop - scrollAdjY,
            AnchorSide.Right => anchorRight - scrollAdjX,
            AnchorSide.Bottom => anchorBottom - scrollAdjY,
            AnchorSide.Left => anchorLeft - scrollAdjX,
            AnchorSide.Center => (anchorTop + anchorBottom) / 2 - scrollAdjY,
            _ => 0,
        };

        return property switch
        {
            AnchorInsetProperty.Right => cbWidth - raw,
            AnchorInsetProperty.Bottom => cbHeight - raw,
            _ => raw,
        };
    }

    /// <summary>
    /// Resolves an <c>anchor-size(&lt;dimension&gt;)</c> reference to the anchor's
    /// width or height. Inline/self-inline map to width, block/self-block to height.
    /// </summary>
    public static double ResolveSize(AnchorSizeDimension dimension, double anchorWidth, double anchorHeight)
        => dimension switch
        {
            AnchorSizeDimension.Width or AnchorSizeDimension.Inline or AnchorSizeDimension.SelfInline => anchorWidth,
            AnchorSizeDimension.Height or AnchorSizeDimension.Block or AnchorSizeDimension.SelfBlock => anchorHeight,
            _ => 0,
        };

    /// <summary>
    /// Whether a box overflows its containing block for <c>@position-try</c> fallback
    /// purposes: any negative inset, an edge past the containing block, or a box larger
    /// than the (non-negative) inset-modified containing block on either axis.
    /// </summary>
    public static bool Overflows(
        double left, double top, double width, double height,
        double cbWidth, double cbHeight, double imcbWidth, double imcbHeight)
        => left < 0 || top < 0
           || left + width > cbWidth || top + height > cbHeight
           || (imcbWidth < width && imcbWidth >= 0)
           || (imcbHeight < height && imcbHeight >= 0);

    /// <summary>
    /// Whether a candidate <c>@position-try</c> fallback fits within the containing
    /// block (non-negative insets and both far edges within bounds).
    /// </summary>
    public static bool Fits(
        double left, double top, double width, double height, double cbWidth, double cbHeight)
        => left >= 0 && top >= 0 && left + width <= cbWidth && top + height <= cbHeight;
}
