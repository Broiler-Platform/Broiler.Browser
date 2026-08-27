using Broiler.CSS;
using Broiler.Layout;

namespace Broiler.Layout.Tests;

/// <summary>
/// Behaviour-parity guard for <see cref="AnchorGeometry"/>, the anchor()/anchor-size()
/// edge-coordinate resolution and @position-try overflow/fit predicates moved into
/// Broiler.Layout (Phase 5 item 3). Pins the math ported verbatim from the bridge's
/// two former <c>ResolveAnchorEdge</c> copies and its position-try fallback checks.
/// Anchor rect: left=40, top=50, right=120, bottom=90.
/// </summary>
public sealed class AnchorGeometryTests
{
    private const double L = 40, T = 50, R = 120, B = 90;

    [Theory]
    // Raw edges (no scroll adjustment, Other property → raw coordinate).
    [InlineData(AnchorSide.Left, L)]
    [InlineData(AnchorSide.Top, T)]
    [InlineData(AnchorSide.Right, R)]
    [InlineData(AnchorSide.Bottom, B)]
    [InlineData(AnchorSide.Center, (T + B) / 2)] // 70 — always the vertical centre
    public void ResolveEdge_RawEdges(AnchorSide side, double expected)
    {
        Assert.Equal(expected, AnchorGeometry.ResolveEdge(
            L, T, R, B, side, 0, 0, AnchorInsetProperty.Other, 1000, 1000));
    }

    [Fact(Timeout = 600000)]
    public void ResolveEdge_RightProperty_FlipsAgainstCbWidth()
    {
        // property=Right → cbWidth - rawRight
        Assert.Equal(200 - R, AnchorGeometry.ResolveEdge(
            L, T, R, B, AnchorSide.Right, 0, 0, AnchorInsetProperty.Right, 200, 300));
    }

    [Fact(Timeout = 600000)]
    public void ResolveEdge_BottomProperty_FlipsAgainstCbHeight()
    {
        Assert.Equal(300 - B, AnchorGeometry.ResolveEdge(
            L, T, R, B, AnchorSide.Bottom, 0, 0, AnchorInsetProperty.Bottom, 200, 300));
    }

    [Fact(Timeout = 600000)]
    public void ResolveEdge_ScrollAdjustment_XForInlineYForBlock()
    {
        // Left/Right subtract scrollAdjX; Top/Bottom/Center subtract scrollAdjY.
        Assert.Equal(L - 5, AnchorGeometry.ResolveEdge(L, T, R, B, AnchorSide.Left, 5, 7, AnchorInsetProperty.Other, 1000, 1000));
        Assert.Equal(T - 7, AnchorGeometry.ResolveEdge(L, T, R, B, AnchorSide.Top, 5, 7, AnchorInsetProperty.Other, 1000, 1000));
        Assert.Equal((T + B) / 2 - 7, AnchorGeometry.ResolveEdge(L, T, R, B, AnchorSide.Center, 5, 7, AnchorInsetProperty.Other, 1000, 1000));
    }

    [Fact(Timeout = 600000)]
    public void ResolveEdge_RightProperty_AppliesScrollBeforeFlip()
    {
        // property=Right on the Right side → cbWidth - (right - scrollAdjX).
        Assert.Equal(200 - (R - 5), AnchorGeometry.ResolveEdge(
            L, T, R, B, AnchorSide.Right, 5, 0, AnchorInsetProperty.Right, 200, 300));
    }

    [Theory]
    [InlineData(AnchorSizeDimension.Width, 80)]
    [InlineData(AnchorSizeDimension.Inline, 80)]
    [InlineData(AnchorSizeDimension.SelfInline, 80)]
    [InlineData(AnchorSizeDimension.Height, 40)]
    [InlineData(AnchorSizeDimension.Block, 40)]
    [InlineData(AnchorSizeDimension.SelfBlock, 40)]
    public void ResolveSize_MapsDimensionToWidthOrHeight(AnchorSizeDimension dim, double expected)
    {
        Assert.Equal(expected, AnchorGeometry.ResolveSize(dim, anchorWidth: 80, anchorHeight: 40));
    }

    [Theory]
    // Fits entirely inside a 100×100 CB with a 100×100 IMCB → no overflow.
    [InlineData(0, 0, 50, 50, false)]
    // Negative inset.
    [InlineData(-1, 0, 10, 10, true)]
    [InlineData(0, -1, 10, 10, true)]
    // Far edge past the CB.
    [InlineData(60, 0, 50, 10, true)]
    [InlineData(0, 60, 10, 50, true)]
    public void Overflows_AgainstFullImcb(double l, double t, double w, double h, bool expected)
    {
        // imcb = full 100×100 CB so only the CB checks apply.
        Assert.Equal(expected, AnchorGeometry.Overflows(l, t, w, h, 100, 100, 100, 100));
    }

    [Fact(Timeout = 600000)]
    public void Overflows_WhenImcbSmallerThanBox()
    {
        // Box 40×40 fits the CB but not the 30-wide IMCB.
        Assert.True(AnchorGeometry.Overflows(0, 0, 40, 40, 100, 100, 30, 100));
        // Negative IMCB is ignored (the >= 0 guard).
        Assert.False(AnchorGeometry.Overflows(0, 0, 40, 40, 100, 100, -5, 100));
    }

    [Theory]
    [InlineData(0, 0, 100, 100, true)]   // exactly fills
    [InlineData(10, 10, 80, 80, true)]
    [InlineData(-1, 0, 10, 10, false)]   // negative inset
    [InlineData(60, 0, 50, 10, false)]   // past the right edge
    [InlineData(0, 60, 10, 50, false)]   // past the bottom edge
    public void Fits_WithinContainingBlock(double l, double t, double w, double h, bool expected)
    {
        Assert.Equal(expected, AnchorGeometry.Fits(l, t, w, h, 100, 100));
    }
}
