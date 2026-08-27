using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// CSS Grid Level 1 §8.5: When all grid items share the same
    /// grid-row and grid-column (e.g. grid-row: 1; grid-column: 1),
    /// they overlap in the same grid cell.  Reposition them to the
    /// container's content-area top-left so they stack visually with
    /// later items painted on top.
    /// </summary>
    /// <returns><c>true</c> if stacking was applied; <c>false</c>
    /// if items are not all in the same cell.</returns>
    private bool ApplyGridStacking()
    {
        bool allSameCell = true;
        string firstRow = null, firstCol = null;

        foreach (var child in Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (child.Display == CssConstants.None)
                continue;

            var cr = child.GridRow;
            var cc = child.GridColumn;

            // Items without explicit grid placement use auto.
            if (string.IsNullOrEmpty(cr) || cr == "auto" || string.IsNullOrEmpty(cc) || cc == "auto")
            {
                allSameCell = false;
                break;
            }

            if (firstRow == null)
            {
                firstRow = cr;
                firstCol = cc;
            }
            else if (cr != firstRow || cc != firstCol)
            {
                allSameCell = false;
                break;
            }
        }

        if (!allSameCell || firstRow == null)
            return false;

        double cellLeft = Location.X + ActualPaddingLeft + ActualBorderLeftWidth;
        double cellTop = Location.Y + ActualPaddingTop + ActualBorderTopWidth;
        double columnWidth = Size.Width - ActualPaddingLeft - ActualPaddingRight
            - ActualBorderLeftWidth - ActualBorderRightWidth;
        double maxBottom = cellTop;

        foreach (var child in Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (child.Display == CssConstants.None)
                continue;

            // CSS Grid Level 1 §6.1: an auto-width grid item with the default
            // (stretch) justify-self fills its column — the same rule the
            // multi-row auto-placement path applies. Same-cell items are still
            // stretched to the container's content width so they paint as the
            // full-width blue bars the check-layout grid reftests expect.
            StretchGridItemToColumnWidth(child, columnWidth);

            double dx = cellLeft + child.ActualMarginLeft - child.Location.X;
            double dy = cellTop + child.ActualMarginTop - child.Location.Y;

            if (Math.Abs(dx) > 0.1)
                child.OffsetLeft(dx);

            if (Math.Abs(dy) > 0.1)
                child.OffsetTop(dy);

            double childBottom = child.ActualBottom + child.ActualMarginBottom;

            if (childBottom > maxBottom)
                maxBottom = childBottom;
        }

        // If the grid declares explicit rows, size those tracks and place the
        // items in them (heights, block-axis alignment, container height). This
        // runs at the container's final width — unlike the definite-track pass,
        // which the shrink-to-fit measurement invokes at content width — so a
        // stretched-column grid sizes its percentage/fixed rows correctly here.
        // Declines (keeping the plain content-height stacking below) for a
        // none/auto or out-of-scope rows template.
        if (TryApplyStackingRowTracks(cellLeft, cellTop, columnWidth))
            return true;

        ActualBottom = maxBottom + ActualPaddingBottom + ActualBorderBottomWidth;
        return true;
    }

    /// <summary>
    /// Sizes an explicit <c>grid-template-rows</c> for the same-cell stacking
    /// path and re-places every in-flow item within its resolved row track (via
    /// <see cref="PlaceItemInArea"/>, so heights, block-axis alignment and the
    /// stale-inline-rect reset all match the definite-track pass). All items share
    /// one grid line here (the stacking precondition), so a single row band
    /// serves them. Sets the container's block size from the sized tracks and
    /// returns <c>true</c>; returns <c>false</c> to leave plain content-height
    /// stacking in place when the rows template is none/auto or out of scope.
    /// </summary>
    private bool TryApplyStackingRowTracks(double cellLeft, double cellTop, double columnWidth)
    {
        double em = GetEmHeight();
        List<GridTrackSpec> rowSpecs = ParseTrackList(GridTemplateRows, em);

        if (rowSpecs == null || rowSpecs.Count == 0 || rowSpecs.Count > MaxGridLine)
            return false;

        var (rowStartN, rowSpan) = ParseGridLine(GridRowOfFirstInFlowItem(), rowSpecs.Count);
        int rowStart = rowStartN ?? 0;

        if (rowStart < 0 || rowSpan < 1 || rowStart + rowSpan > MaxGridLine)
            return false;

        // Tallest same-cell item's margin-box height feeds the occupied track's
        // content contribution.
        double contentH = 0;

        foreach (var child in Boxes)
        {
            if (child.Position is CssConstants.Absolute or CssConstants.Fixed || child.Display == CssConstants.None)
                continue;

            double h = (child.ActualBottom - child.Location.Y) + child.ActualMarginTop + child.ActualMarginBottom;

            if (h > contentH)
                contentH = h;
        }

        int rowCount = Math.Max(rowSpecs.Count, rowStart + rowSpan);
        GridTrackSpec implicitRow = ParseSingleImplicitSpec(GridAutoRows, em);
        bool rowDefinite = TryGetDefiniteContentHeight(em, out double definiteHeight);
        double rowBasis = rowDefinite ? definiteHeight : 0;
        double rowGap = ResolveGridGap(RowGap, rowBasis, em);

        var rowItems = new List<AxisItem> { new(rowStart, rowSpan, contentH, contentH) };
        double[] rowSizes = ResolveTrackSizes(rowSpecs, implicitRow, rowCount,
            rowBasis, rowDefinite, rowGap, rowBasis, rowItems);

        if (rowSizes == null)
            return false;

        // CSS Grid §7.2.1: percentage rows against an indefinite block size are
        // sized as 'auto' for the intrinsic height above, then resolved against
        // that intrinsic height for layout while the container keeps it.
        double intrinsic = SumTrackSizes(rowSizes, rowGap);

        if (!rowDefinite)
            ResolvePercentRowTracksAgainstIntrinsic(rowSpecs, implicitRow, rowSizes, intrinsic);

        double[] rowStartEdge = BuildTrackEdges(rowSizes, rowGap, out double[] rowEndEdge);
        double areaTop = cellTop + rowStartEdge[rowStart];
        double areaBottom = cellTop + rowEndEdge[rowStart + rowSpan - 1];
        double areaHeight = areaBottom - areaTop;

        foreach (var child in Boxes)
        {
            if (child.Position is CssConstants.Absolute or CssConstants.Fixed || child.Display == CssConstants.None)
                continue;

            PlaceItemInArea(child, cellLeft, areaTop, columnWidth, areaHeight);
        }

        // A definite-height grid keeps its specified content height (rows may
        // overflow it or leave trailing space); an indefinite one sizes to the
        // intrinsic (pass-1) row height.
        double gridContentHeight = rowDefinite ? definiteHeight : intrinsic;
        ActualBottom = Location.Y + ActualBorderTopWidth + ActualPaddingTop
            + gridContentHeight + ActualPaddingBottom + ActualBorderBottomWidth;

        return true;
    }

    /// <summary>The <c>grid-row</c> of the first in-flow grid item (all in-flow
    /// items share it on the stacking path, so any one is representative).</summary>
    private string GridRowOfFirstInFlowItem()
    {
        foreach (var child in Boxes)
        {
            if (child.Position is CssConstants.Absolute or CssConstants.Fixed || child.Display == CssConstants.None)
                continue;

            return child.GridRow;
        }

        return null;
    }

    /// <summary>
    /// CSS Grid Level 1 §6.1 / CSS Box Alignment §6.2: stretch a grid item to
    /// its column's content width when it has an <c>auto</c> used width under the
    /// default (<c>normal</c>/<c>stretch</c>) justify-self. Shared by the
    /// same-cell stacking path and the multi-row auto-placement path so both
    /// fill the column the same way. Returns whether the item's justify-self
    /// resolved to stretch (so the caller can skip a redundant justify offset).
    /// </summary>
    private bool StretchGridItemToColumnWidth(CssBox child, double columnWidth) => StretchGridItemToColumnWidth(child, columnWidth, out _);

    private bool StretchGridItemToColumnWidth(CssBox child, double columnWidth, out string resolvedJustify)
    {
        bool isAutoWidth = child.Width == CssConstants.Auto || string.IsNullOrEmpty(child.Width);
        string js = child.JustifySelf?.Trim().ToLowerInvariant();

        // CSS Box Alignment §6.2: 'justify-self: auto' resolves to the grid
        // container's 'justify-items' (the intermediate display: contents
        // ancestor, having no box, does not contribute).
        if (string.IsNullOrEmpty(js) || js == "auto")
        {
            string ji = JustifyItems?.Trim().ToLowerInvariant();
            js = string.IsNullOrEmpty(ji) || ji == "auto" || ji == "legacy"
                ? "normal"
                : ji;
        }

        bool isStretch = js == "normal" || js == "stretch";

        if (isStretch && isAutoWidth)
        {
            double targetWidth = columnWidth - child.ActualMarginLeft - child.ActualMarginRight;

            if (targetWidth > 0 && Math.Abs(child.Size.Width - targetWidth) > 0.5)
            {
                child.Size = new SizeF((float)targetWidth, child.Size.Height);
                child.ActualRight = child.Location.X + targetWidth;

                // The inline layout path (CreateLineBoxes) recorded per-line-box
                // rectangles the paint walker uses for the item's own
                // background/border; leaving them would paint the item at its
                // pre-stretch inline width. Grid items are blockified, so clear
                // the stale inline rects and let paint fall back to Location+Size.
                child.RectanglesReset();

                // A stretched table grid item is sized to the column width like
                // any other stretched item, but — unlike a block — its cell grid
                // was already shrink-wrapped to content by the table formatting
                // algorithm and does not reflow from a bare Size change. Re-run
                // that algorithm with the filled width made definite so the
                // columns (and their centered cell content) span the column
                // instead of hugging the content (WPT table-grid-item-dynamic-002).
                if ((child.Display == CssConstants.Table || child.Display == CssConstants.InlineTable) && child.LayoutEnvironment != null)
                {
                    string savedWidth = child.Width;
                    float savedX = child.Location.X, savedY = child.Location.Y;
                    
                    // Make the filled width definite and re-run the box's own
                    // layout (not just the table algorithm) so the §10.7 min-height
                    // clamp still applies — a bare table-engine call would drop the
                    // table's min-height and collapse it to content height.
                    child.Width = targetWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "px";
                    child.PerformLayout(child.LayoutEnvironment);
                    child.Width = savedWidth;

                    // PerformLayout may reposition the box; the caller offsets it
                    // into its cell next, so restore the pre-relayout origin.
                    child.OffsetLeft(savedX - child.Location.X);
                    child.OffsetTop(savedY - child.Location.Y);
                    child.Size = new SizeF((float)targetWidth, child.Size.Height);
                    child.ActualRight = child.Location.X + targetWidth;
                }
            }
        }

        resolvedJustify = js;
        return isStretch;
    }

    /// <summary>
    /// Called from <see cref="CssLayoutEngine.FlowInlineBlock"/> after
    /// CreateLineBoxes to apply grid stacking or auto-placement for grid
    /// containers that are laid out via the inline-block path.
    /// </summary>
    internal void ApplyGridLayoutAfterInline()
    {
        // Prefer the real definite-track pass; it declines (returns false) unless
        // the container declares fixed explicit templates, in which case the
        // single-column approximation below runs unchanged.

        if (TryApplyGridTrackLayout())
            return;

        if (!ApplyGridStacking())
            ApplyGridAutoPlacement();
    }

    /// <summary>
    /// CSS Grid Level 1: Auto-placement for grid items that are not all
    /// in the same cell.  The inline layout path (CreateLineBoxes) places
    /// grid items as inline-blocks on a single line.  This method
    /// repositions them into proper grid rows (one item per row for a
    /// single-column grid) and applies justify-self within the column.
    /// </summary>
    private void ApplyGridAutoPlacement()
    {
        double cellLeft = Location.X + ActualPaddingLeft + ActualBorderLeftWidth;
        double cellTop = Location.Y + ActualPaddingTop + ActualBorderTopWidth;
        double columnWidth = Size.Width - ActualPaddingLeft - ActualPaddingRight
            - ActualBorderLeftWidth - ActualBorderRightWidth;

        if (columnWidth <= 0) 
            return;

        double currentY = cellTop;
        
        foreach (var child in Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (child.Display == CssConstants.None)
                continue;

            // CSS Grid Level 1 §6.1: Grid items with auto/normal/stretch
            // justify-self and auto width should stretch to fill the column.
            bool isStretch = StretchGridItemToColumnWidth(child, columnWidth, out string js);

            // Move child to the start of the current row.
            double dx = cellLeft + child.ActualMarginLeft - child.Location.X;
            double dy = currentY + child.ActualMarginTop - child.Location.Y;

            if (Math.Abs(dx) > 0.1)
                child.OffsetLeft(dx);

            if (Math.Abs(dy) > 0.1)
                child.OffsetTop(dy);

            // CSS Grid Level 1 §6.1: Apply justify-self to position the
            // item within its grid cell (column width).
            double boxWidth = child.ActualRight - child.Location.X;
            double freeSpace = columnWidth - boxWidth;

            if (freeSpace > 0.5 && !isStretch)
            {
                bool isElementRtl = child.Direction == "rtl";
                bool isContainerRtl = Direction == "rtl";

                double justifyDx = 0;

                switch (js)
                {
                    case "center":
                        justifyDx = freeSpace / 2;
                        break;

                    case "end":
                    case "flex-end":
                        justifyDx = isContainerRtl ? 0 : freeSpace;
                        break;

                    case "self-end":
                        justifyDx = isElementRtl ? 0 : freeSpace;
                        break;

                    case "right":
                        justifyDx = freeSpace;
                        break;

                    case "start":
                    case "flex-start":
                        justifyDx = isContainerRtl ? freeSpace : 0;
                        break;

                    case "self-start":
                        justifyDx = isElementRtl ? freeSpace : 0;
                        break;

                    case "left":
                        justifyDx = 0;
                        break;
                }

                if (Math.Abs(justifyDx) > 0.5)
                    child.OffsetLeft(justifyDx);
            }

            currentY = child.ActualBottom + child.ActualMarginBottom;
        }

        ActualBottom = currentY + ActualPaddingBottom + ActualBorderBottomWidth;
    }
}
