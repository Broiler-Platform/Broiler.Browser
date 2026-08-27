using System;
using Broiler.CSS;

namespace Broiler.Layout;

/// <summary>
/// The rectangle of a resolved CSS <c>position-area</c> grid cell, in the same
/// coordinate space as the inputs to <see cref="PositionAreaGrid.ComputeCell"/>.
/// </summary>
public readonly record struct PositionAreaCell(double Left, double Top, double Width, double Height);

/// <summary>
/// The used geometry of an element positioned within a <c>position-area</c> grid
/// cell: the inset-modified containing block (IMCB), the resolved element size, and
/// the element's aligned position (left/top of the border box before any margin,
/// border, or box-sizing adjustment the caller applies). All values share the cell's
/// coordinate space.
/// </summary>
public readonly record struct PositionAreaBox(
    double ImcbLeft, double ImcbTop, double ImcbWidth, double ImcbHeight,
    double Width, double Height, double Left, double Top);

/// <summary>
/// The four physical edge sizes (top/right/bottom/left) of one box-model band —
/// margin, border, or padding — used by the <c>position-area</c> content-size
/// resolution.
/// </summary>
public readonly record struct PositionAreaEdges(double Top, double Right, double Bottom, double Left);

/// <summary>
/// Used-value geometry for CSS <c>position-area</c> placement: the 3×3 grid formed
/// by an anchor box and its containing block, and the alignment of an element
/// within a selected grid cell.
/// </summary>
/// <remarks>
/// The first anchor-placement geometry moved into <c>Broiler.Layout</c> (HtmlBridge
/// complexity-reduction roadmap, Phase 5 work item 3 — "Layout consumes those
/// [Broiler.CSS syntax] models and applies them to boxes"). It consumes the neutral
/// <see cref="PositionAreaValue"/> model (promoted in item 4) and produces box
/// geometry; it holds no DOM, cascade, or containing-block-resolution knowledge —
/// the caller supplies the already-resolved containing-block frame and anchor rect.
/// This is a pure function today; wiring it into the layout engine's absolute
/// positioning pass (so <c>position-area</c> is placed natively rather than
/// pre-baked by the bridge) is a later increment.
/// </remarks>
public static class PositionAreaGrid
{
    /// <summary>
    /// Computes the <c>position-area</c> grid cell for <paramref name="area"/>. The
    /// grid spans from the union of the containing block and the anchor box; the
    /// block- and inline-axis selections choose the cell (or span of cells) relative
    /// to the anchor's edges. Coordinates are whatever space the caller passes
    /// (the containing-block origin and the anchor edges must share one frame); the
    /// returned width/height are clamped to be non-negative.
    /// </summary>
    /// <param name="cbX">Containing-block origin x (grid frame).</param>
    /// <param name="cbY">Containing-block origin y (grid frame).</param>
    /// <param name="cbWidth">Containing-block width.</param>
    /// <param name="cbHeight">Containing-block height.</param>
    /// <param name="anchorLeft">Anchor box left edge.</param>
    /// <param name="anchorTop">Anchor box top edge.</param>
    /// <param name="anchorRight">Anchor box right edge.</param>
    /// <param name="anchorBottom">Anchor box bottom edge.</param>
    /// <param name="area">The parsed <c>position-area</c> block/inline selections.</param>
    public static PositionAreaCell ComputeCell(
        double cbX, double cbY, double cbWidth, double cbHeight,
        double anchorLeft, double anchorTop, double anchorRight, double anchorBottom,
        PositionAreaValue area)
    {
        // Grid edges: extend to include both the containing block and the anchor.
        double gridLeft = Math.Min(cbX, anchorLeft);
        double gridRight = Math.Max(cbX + cbWidth, anchorRight);
        double gridTop = Math.Min(cbY, anchorTop);
        double gridBottom = Math.Max(cbY + cbHeight, anchorBottom);

        (double colStart, double colEnd) = area.Inline switch
        {
            PositionAreaSpan.Start => (gridLeft, anchorLeft),
            PositionAreaSpan.Center => (anchorLeft, anchorRight),
            PositionAreaSpan.End => (anchorRight, gridRight),
            PositionAreaSpan.SpanStart => (gridLeft, anchorRight),
            PositionAreaSpan.SpanEnd => (anchorLeft, gridRight),
            PositionAreaSpan.SpanAll => (gridLeft, gridRight),
            _ => (gridLeft, gridRight),
        };

        (double rowStart, double rowEnd) = area.Block switch
        {
            PositionAreaSpan.Start => (gridTop, anchorTop),
            PositionAreaSpan.Center => (anchorTop, anchorBottom),
            PositionAreaSpan.End => (anchorBottom, gridBottom),
            PositionAreaSpan.SpanStart => (gridTop, anchorBottom),
            PositionAreaSpan.SpanEnd => (anchorTop, gridBottom),
            PositionAreaSpan.SpanAll => (gridTop, gridBottom),
            _ => (gridTop, gridBottom),
        };

        return new PositionAreaCell(
            colStart, rowStart,
            Math.Max(0, colEnd - colStart),
            Math.Max(0, rowEnd - rowStart));
    }

    /// <summary>
    /// Resolves an element's used box within a <c>position-area</c> grid
    /// <paramref name="cell"/>: applies the (already length-resolved) insets to form
    /// the inset-modified containing block, resolves the element's width/height, and
    /// aligns it within the cell. Percentage width/height resolve against the cell
    /// dimensions; an explicit positive length is clamped to the cell; otherwise the
    /// element fills the IMCB. Alignment uses <see cref="ComputeAlignmentOffset"/> per
    /// axis with the block/inline selections of <paramref name="area"/>.
    /// </summary>
    /// <param name="cell">The resolved grid cell.</param>
    /// <param name="insetTop">Resolved top inset (px).</param>
    /// <param name="insetRight">Resolved right inset (px).</param>
    /// <param name="insetBottom">Resolved bottom inset (px).</param>
    /// <param name="insetLeft">Resolved left inset (px).</param>
    /// <param name="explicitWidth">Explicit width in px, if the used value is a length; else null.</param>
    /// <param name="percentWidth">Width as a percentage number (e.g. 50 for 50%), if a percentage; else null.</param>
    /// <param name="explicitHeight">Explicit height in px, if the used value is a length; else null.</param>
    /// <param name="percentHeight">Height as a percentage number, if a percentage; else null.</param>
    /// <param name="area">The parsed <c>position-area</c> selections (drive alignment).</param>
    public static PositionAreaBox ResolveElementBox(
        PositionAreaCell cell,
        double insetTop, double insetRight, double insetBottom, double insetLeft,
        double? explicitWidth, double? percentWidth,
        double? explicitHeight, double? percentHeight,
        PositionAreaValue area)
    {
        double cellW = cell.Width;
        double cellH = cell.Height;

        // The IMCB (inset-modified containing block) is the cell after insets.
        double imcbLeft = cell.Left + insetLeft;
        double imcbTop = cell.Top + insetTop;
        double imcbW = Math.Max(0, cellW - insetLeft - insetRight);
        double imcbH = Math.Max(0, cellH - insetTop - insetBottom);

        // Resolve element dimensions: percentages against the cell, explicit lengths
        // clamped to the cell, otherwise fill the IMCB.
        double resolvedW = imcbW;
        double resolvedH = imcbH;
        if (percentWidth.HasValue)
            resolvedW = cellW * percentWidth.Value / 100.0;
        else if (explicitWidth.HasValue && explicitWidth.Value > 0)
            resolvedW = Math.Min(explicitWidth.Value, cellW);
        if (percentHeight.HasValue)
            resolvedH = cellH * percentHeight.Value / 100.0;
        else if (explicitHeight.HasValue && explicitHeight.Value > 0)
            resolvedH = Math.Min(explicitHeight.Value, cellH);

        double left = cell.Left + ComputeAlignmentOffset(area.Inline, cellW, resolvedW);
        double top = cell.Top + ComputeAlignmentOffset(area.Block, cellH, resolvedH);

        return new PositionAreaBox(imcbLeft, imcbTop, imcbW, imcbH, resolvedW, resolvedH, left, top);
    }

    /// <summary>
    /// Content-box size for an element that stretches to fill the inset-modified
    /// containing block (the <c>place-self: stretch</c> default for position-area):
    /// the IMCB minus margin, border, and padding on each axis, clamped to be
    /// non-negative. Used when the element has percentage-based box properties.
    /// </summary>
    public static (double Width, double Height) ContentSizeFillingImcb(
        double imcbWidth, double imcbHeight,
        PositionAreaEdges margin, PositionAreaEdges border, PositionAreaEdges padding)
    {
        double w = imcbWidth
            - margin.Left - margin.Right - border.Left - border.Right - padding.Left - padding.Right;
        double h = imcbHeight
            - margin.Top - margin.Bottom - border.Top - border.Bottom - padding.Top - padding.Bottom;
        return (Math.Max(0, w), Math.Max(0, h));
    }

    /// <summary>
    /// Converts a border-box dimension to its content-box size (for
    /// <c>box-sizing: border-box</c>): the border-box minus border and padding on
    /// each axis, clamped to be non-negative.
    /// </summary>
    public static (double Width, double Height) BorderBoxToContentSize(
        double borderBoxWidth, double borderBoxHeight,
        PositionAreaEdges border, PositionAreaEdges padding)
    {
        double w = borderBoxWidth - border.Left - border.Right - padding.Left - padding.Right;
        double h = borderBoxHeight - border.Top - border.Bottom - padding.Top - padding.Bottom;
        return (Math.Max(0, w), Math.Max(0, h));
    }

    /// <summary>
    /// Offset that aligns an element of size <paramref name="elementSize"/> within a
    /// grid cell of size <paramref name="cellSize"/> along one axis. A
    /// <see cref="PositionAreaSpan.Start"/> cell (nearest the anchor at the cell's
    /// far edge) pushes the element to the cell end; <see cref="PositionAreaSpan.Center"/>
    /// centres it; every other selection aligns to the cell start. No slack (element
    /// at least as large as the cell) yields zero.
    /// </summary>
    public static double ComputeAlignmentOffset(PositionAreaSpan selection, double cellSize, double elementSize)
    {
        double slack = cellSize - elementSize;
        if (slack <= 0) return 0;

        return selection switch
        {
            PositionAreaSpan.Start => slack,   // "top"/"left" cell: align toward the anchor (cell end).
            PositionAreaSpan.Center => slack / 2,
            _ => 0,                            // "end"/spanning cells: align to the cell start.
        };
    }
}
