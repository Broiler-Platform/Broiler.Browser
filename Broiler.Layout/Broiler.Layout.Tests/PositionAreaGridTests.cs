using Broiler.CSS;
using Broiler.Layout;

namespace Broiler.Layout.Tests;

/// <summary>
/// Behaviour-parity guard for <see cref="PositionAreaGrid"/>, the first
/// anchor-placement geometry moved into Broiler.Layout (Phase 5 item 3). Pins the
/// 3×3 grid-cell math and within-cell alignment ported verbatim from the bridge's
/// <c>ComputePositionAreaRect</c> / <c>ComputeAlignmentOffset</c>. Uses a simple
/// frame: containing block at (0,0) 100×100 and an anchor rect at (40,40)–(60,60),
/// so grid edges coincide with the CB edges (0 and 100) and the anchor edges (40/60).
/// </summary>
public sealed class PositionAreaGridTests
{
    private static PositionAreaCell Cell(PositionAreaSpan block, PositionAreaSpan inline) =>
        PositionAreaGrid.ComputeCell(
            cbX: 0, cbY: 0, cbWidth: 100, cbHeight: 100,
            anchorLeft: 40, anchorTop: 40, anchorRight: 60, anchorBottom: 60,
            new PositionAreaValue(block, inline));

    [Fact(Timeout = 600000)]
    public void Cell_TopLeft_IsBeforeTheAnchorOnBothAxes()
    {
        // block=Start (row before anchor: 0..40), inline=Start (col before anchor: 0..40)
        var c = Cell(PositionAreaSpan.Start, PositionAreaSpan.Start);
        Assert.Equal(0, c.Left);
        Assert.Equal(0, c.Top);
        Assert.Equal(40, c.Width);
        Assert.Equal(40, c.Height);
    }

    [Fact(Timeout = 600000)]
    public void Cell_BottomRight_IsAfterTheAnchorOnBothAxes()
    {
        // block=End (row 60..100), inline=End (col 60..100)
        var c = Cell(PositionAreaSpan.End, PositionAreaSpan.End);
        Assert.Equal(60, c.Left);
        Assert.Equal(60, c.Top);
        Assert.Equal(40, c.Width);
        Assert.Equal(40, c.Height);
    }

    [Fact(Timeout = 600000)]
    public void Cell_CenterCenter_IsTheAnchorSpan()
    {
        var c = Cell(PositionAreaSpan.Center, PositionAreaSpan.Center);
        Assert.Equal(40, c.Left);
        Assert.Equal(40, c.Top);
        Assert.Equal(20, c.Width);
        Assert.Equal(20, c.Height);
    }

    [Fact(Timeout = 600000)]
    public void Cell_SpanStart_CoversStartCellThroughAnchor()
    {
        // inline SpanStart: gridLeft..anchorRight = 0..60 ; block SpanStart: gridTop..anchorBottom = 0..60
        var c = Cell(PositionAreaSpan.SpanStart, PositionAreaSpan.SpanStart);
        Assert.Equal(0, c.Left);
        Assert.Equal(0, c.Top);
        Assert.Equal(60, c.Width);
        Assert.Equal(60, c.Height);
    }

    [Fact(Timeout = 600000)]
    public void Cell_SpanEnd_CoversAnchorThroughEndCell()
    {
        // inline SpanEnd: anchorLeft..gridRight = 40..100 ; block SpanEnd: anchorTop..gridBottom = 40..100
        var c = Cell(PositionAreaSpan.SpanEnd, PositionAreaSpan.SpanEnd);
        Assert.Equal(40, c.Left);
        Assert.Equal(40, c.Top);
        Assert.Equal(60, c.Width);
        Assert.Equal(60, c.Height);
    }

    [Fact(Timeout = 600000)]
    public void Cell_SpanAll_CoversTheWholeGrid()
    {
        var c = Cell(PositionAreaSpan.SpanAll, PositionAreaSpan.SpanAll);
        Assert.Equal(0, c.Left);
        Assert.Equal(0, c.Top);
        Assert.Equal(100, c.Width);
        Assert.Equal(100, c.Height);
    }

    [Fact(Timeout = 600000)]
    public void Cell_MixedAxes_TopCenter()
    {
        // block=Start (row 0..40), inline=Center (col 40..60)
        var c = Cell(PositionAreaSpan.Start, PositionAreaSpan.Center);
        Assert.Equal(40, c.Left);
        Assert.Equal(0, c.Top);
        Assert.Equal(20, c.Width);
        Assert.Equal(40, c.Height);
    }

    [Fact(Timeout = 600000)]
    public void Cell_GridExtendsToIncludeAnchorOutsideContainingBlock()
    {
        // Anchor to the right of and below the CB → grid edges extend to the anchor.
        var c = PositionAreaGrid.ComputeCell(
            cbX: 0, cbY: 0, cbWidth: 50, cbHeight: 50,
            anchorLeft: 80, anchorTop: 80, anchorRight: 100, anchorBottom: 100,
            new PositionAreaValue(PositionAreaSpan.End, PositionAreaSpan.End));
        // gridRight = max(50, 100) = 100 → inline End col = anchorRight..gridRight = 100..100 (empty)
        Assert.Equal(100, c.Left);
        Assert.Equal(100, c.Top);
        Assert.Equal(0, c.Width);
        Assert.Equal(0, c.Height);
    }

    private static readonly PositionAreaCell Cell100 = new(0, 0, 100, 100);
    private static readonly PositionAreaValue SpanBoth =
        new(PositionAreaSpan.SpanAll, PositionAreaSpan.SpanAll);

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_NoInsetsNoSize_FillsTheCell()
    {
        var box = PositionAreaGrid.ResolveElementBox(
            new PositionAreaCell(10, 20, 100, 80),
            0, 0, 0, 0, null, null, null, null, SpanBoth);
        Assert.Equal(10, box.ImcbLeft);
        Assert.Equal(20, box.ImcbTop);
        Assert.Equal(100, box.ImcbWidth);
        Assert.Equal(80, box.ImcbHeight);
        Assert.Equal(100, box.Width);
        Assert.Equal(80, box.Height);
        Assert.Equal(10, box.Left);   // SpanAll → offset 0
        Assert.Equal(20, box.Top);
    }

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_Insets_ShrinkTheImcb()
    {
        // insets top=10 right=20 bottom=30 left=40 on a 100×100 cell at origin.
        var box = PositionAreaGrid.ResolveElementBox(
            Cell100, 10, 20, 30, 40, null, null, null, null, SpanBoth);
        Assert.Equal(40, box.ImcbLeft);
        Assert.Equal(10, box.ImcbTop);
        Assert.Equal(40, box.ImcbWidth);   // 100 - 40 - 20
        Assert.Equal(60, box.ImcbHeight);  // 100 - 10 - 30
        Assert.Equal(40, box.Width);       // no explicit size → fills IMCB
        Assert.Equal(60, box.Height);
    }

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_OverlargeInsets_ClampImcbToZero()
    {
        var box = PositionAreaGrid.ResolveElementBox(
            new PositionAreaCell(0, 0, 10, 10), 0, 8, 0, 8, null, null, null, null, SpanBoth);
        Assert.Equal(0, box.ImcbWidth);
        Assert.Equal(0, box.Width);
    }

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_ExplicitWidth_ClampedToCellAndAligned()
    {
        // explicit width 30, inline Start → aligns to cell end: offset = 100 - 30 = 70.
        var area = new PositionAreaValue(PositionAreaSpan.SpanAll, PositionAreaSpan.Start);
        var box = PositionAreaGrid.ResolveElementBox(
            Cell100, 0, 0, 0, 0, 30, null, null, null, area);
        Assert.Equal(30, box.Width);
        Assert.Equal(70, box.Left);
        Assert.Equal(0, box.Top);   // block SpanAll → offset 0
    }

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_ExplicitWidthLargerThanCell_ClampedToCell()
    {
        var box = PositionAreaGrid.ResolveElementBox(
            Cell100, 0, 0, 0, 0, 150, null, null, null, SpanBoth);
        Assert.Equal(100, box.Width);
    }

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_PercentSize_AgainstTheCell()
    {
        var box = PositionAreaGrid.ResolveElementBox(
            Cell100, 0, 0, 0, 0, null, 50, null, 25, SpanBoth);
        Assert.Equal(50, box.Width);
        Assert.Equal(25, box.Height);
    }

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_CenterAlignsExplicitSize()
    {
        // Center on both axes, explicit 40×40 in a 100×100 cell → offset (100-40)/2 = 30.
        var area = new PositionAreaValue(PositionAreaSpan.Center, PositionAreaSpan.Center);
        var box = PositionAreaGrid.ResolveElementBox(
            Cell100, 0, 0, 0, 0, 40, null, 40, null, area);
        Assert.Equal(30, box.Left);
        Assert.Equal(30, box.Top);
    }

    [Fact(Timeout = 600000)]
    public void ResolveElementBox_PercentTakesPrecedenceOverExplicit()
    {
        // Both a percent and an explicit px supplied → percent wins (matches the bridge).
        var box = PositionAreaGrid.ResolveElementBox(
            Cell100, 0, 0, 0, 0, 30, 60, null, null, SpanBoth);
        Assert.Equal(60, box.Width);
    }

    [Fact(Timeout = 600000)]
    public void ContentSizeFillingImcb_SubtractsMarginBorderPadding()
    {
        // IMCB 100×80; margin 5 all sides, border 2 all sides, padding 3 all sides.
        var (w, h) = PositionAreaGrid.ContentSizeFillingImcb(
            100, 80,
            new PositionAreaEdges(5, 5, 5, 5),
            new PositionAreaEdges(2, 2, 2, 2),
            new PositionAreaEdges(3, 3, 3, 3));
        Assert.Equal(100 - (5 + 5) - (2 + 2) - (3 + 3), w); // 80
        Assert.Equal(80 - (5 + 5) - (2 + 2) - (3 + 3), h);  // 60
    }

    [Fact(Timeout = 600000)]
    public void ContentSizeFillingImcb_AsymmetricEdges()
    {
        var (w, h) = PositionAreaGrid.ContentSizeFillingImcb(
            100, 100,
            new PositionAreaEdges(1, 2, 3, 4),   // T R B L
            new PositionAreaEdges(0, 0, 0, 0),
            new PositionAreaEdges(0, 0, 0, 0));
        Assert.Equal(100 - 2 - 4, w); // right + left
        Assert.Equal(100 - 1 - 3, h); // top + bottom
    }

    [Fact(Timeout = 600000)]
    public void ContentSizeFillingImcb_ClampsToZero()
    {
        var (w, h) = PositionAreaGrid.ContentSizeFillingImcb(
            10, 10,
            new PositionAreaEdges(0, 8, 0, 8),
            new PositionAreaEdges(0, 0, 0, 0),
            new PositionAreaEdges(0, 0, 0, 0));
        Assert.Equal(0, w); // 10 - 8 - 8 < 0 → 0
        Assert.Equal(10, h);
    }

    [Fact(Timeout = 600000)]
    public void BorderBoxToContentSize_SubtractsBorderAndPadding()
    {
        var (w, h) = PositionAreaGrid.BorderBoxToContentSize(
            100, 50,
            new PositionAreaEdges(2, 4, 2, 4),   // border T R B L
            new PositionAreaEdges(1, 3, 1, 3));  // padding T R B L
        Assert.Equal(100 - (4 + 4) - (3 + 3), w); // 86
        Assert.Equal(50 - (2 + 2) - (1 + 1), h);  // 44
    }

    [Fact(Timeout = 600000)]
    public void BorderBoxToContentSize_ClampsToZero()
    {
        var (w, h) = PositionAreaGrid.BorderBoxToContentSize(
            10, 10,
            new PositionAreaEdges(0, 6, 0, 6),
            new PositionAreaEdges(0, 0, 0, 0));
        Assert.Equal(0, w);
        Assert.Equal(10, h);
    }

    [Theory]
    // Start cell aligns element to the cell end (toward the anchor): offset = slack.
    [InlineData(PositionAreaSpan.Start, 100.0, 30.0, 70.0)]
    // Center: offset = slack / 2.
    [InlineData(PositionAreaSpan.Center, 100.0, 30.0, 35.0)]
    // End and spanning selections align to the cell start: offset = 0.
    [InlineData(PositionAreaSpan.End, 100.0, 30.0, 0.0)]
    [InlineData(PositionAreaSpan.SpanStart, 100.0, 30.0, 0.0)]
    [InlineData(PositionAreaSpan.SpanEnd, 100.0, 30.0, 0.0)]
    [InlineData(PositionAreaSpan.SpanAll, 100.0, 30.0, 0.0)]
    // No slack (element ≥ cell) → zero regardless of selection.
    [InlineData(PositionAreaSpan.Start, 30.0, 30.0, 0.0)]
    [InlineData(PositionAreaSpan.Center, 20.0, 30.0, 0.0)]
    public void AlignmentOffset(PositionAreaSpan sel, double cellSize, double elementSize, double expected)
    {
        Assert.Equal(expected, PositionAreaGrid.ComputeAlignmentOffset(sel, cellSize, elementSize));
    }
}
