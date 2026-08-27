using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// Whether this box was created by <see cref="SliceTallFragments"/> to carry one column's worth
    /// of a box taller than the column set. Marked so a second layout pass rebuilds the run against
    /// its own column height instead of slicing the slices.
    /// </summary>
    internal bool IsColumnSlice { get; private init; }

    /// <summary>
    /// CSS Multi-column Layout: Redistributes in-flow child boxes into
    /// multiple columns after single-column layout.  Walks down through
    /// single-child containers (e.g. html to body) to find the actual
    /// fragmentable children.
    /// </summary>
    private void ApplyMultiColumnLayout(int colCount)
    {
        // PROTOTYPE (BROILER_VERTICAL_FLOW), Stage 4: in a vertical writing
        // mode the whole subtree is laid out in a logical horizontal frame and
        // rotated into physical space after layout (ApplyVerticalWritingModeFlow).
        // Multi-column fragmentation runs here, in that logical frame, exactly as
        // it does for horizontal-tb: columns advance along the logical inline
        // axis (X). The post-layout rotation then maps the logical inline axis
        // onto the physical inline axis, so the columns stack along the writing
        // mode's inline direction — logical left→right becomes physical top→bottom.
        // Verified against Chromium for css-position/multicol/static-position/
        // vlr-in-multicol-ref.html: an 80×600 logical run fragmented across
        // 100px-tall columns rotates to a 100px-wide × 480px-tall vertical strip
        // (diff 17.3% → ~1.9% vs the legacy single-block run). Right→left modes
        // (vertical-rl / sideways-rl) fragment identically here and are then
        // block-start (right) aligned by ApplyVerticalWritingModeFlow.
        double columnGap = ResolveColumnGap();
        double contentWidth = Size.Width - ActualPaddingLeft - ActualPaddingRight
            - ActualBorderLeftWidth - ActualBorderRightWidth;
        double columnWidth = (contentWidth - (colCount - 1) * columnGap) / colCount;
        if (columnWidth <= 0) return;

        double containerTop = Location.Y + ActualPaddingTop + ActualBorderTopWidth;
        double columnLeft = Location.X + ActualPaddingLeft + ActualBorderLeftWidth;

        // Walk down through single-child containers (html -> body) to find
        // the level with multiple block children to distribute.
        var fragmentParent = FindMultiColumnFragmentParent();
        if (fragmentParent == null)
            return;

        var fragments = new List<CssBox>();

        foreach (var child in fragmentParent.Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (child.Display == CssConstants.None)
                continue;

            // The collapsed whitespace between two elements is an anonymous box of its own, and it
            // is not a fragment: counting it made a column set with one real child look like a
            // column set with three, which is the difference between leaving that child alone and
            // narrowing it to a column.
            if (!GeneratesAColumnFragment(child))
                continue;

            fragments.Add(child);
        }

        if (fragments.Count == 0)
            return;

        double firstTop = fragments[0].Location.Y;
        double lastBottom = GetVisualBottom(fragments[^1]);

        foreach (var frag in fragments)
        {
            double vb = GetVisualBottom(frag);

            if (vb > lastBottom)
                lastBottom = vb;
        }

        double totalContentHeight = lastBottom - firstTop;

        if (totalContentHeight <= 0)
            return;

        // Determine column height: balanced columns for auto/max-height,
        // or explicit height.
        bool hasMaxHeight = MaxHeight != "none" && !string.IsNullOrEmpty(MaxHeight);
        bool hasExplicitHeight = Height != CssConstants.Auto && !string.IsNullOrEmpty(Height);
        double maxAllowedHeight = double.MaxValue;

        if (hasMaxHeight)
        {
            double maxH = ParseUsedLength(MaxHeight, ContainingBlock?.Size.Height ?? Size.Height);
            maxAllowedHeight = maxH - ActualPaddingTop - ActualPaddingBottom - ActualBorderTopWidth - ActualBorderBottomWidth;
        }

        double columnHeight;
        if (hasExplicitHeight)
        {
            double h = ParseUsedLength(Height, ContainingBlock?.Size.Height ?? Size.Height);
            columnHeight = h;
        }
        else if (ColumnFill == "auto" && hasMaxHeight)
        {
            // column-fill: auto — fill columns sequentially up to max-height.
            columnHeight = maxAllowedHeight;
        }
        else
        {
            // Balanced column layout: find the minimum column height that
            // distributes all fragments across colCount columns.  Use a
            // binary search between (tallest fragment) and (total height).
            double lo = 0;

            foreach (var frag in fragments)
            {
                double fh = GetVisualBottom(frag) - frag.Location.Y;

                if (fh > lo)
                    lo = fh;
            }

            double hi = totalContentHeight;

            for (int iter = 0; iter < 20; iter++)
            {
                double mid = (lo + hi) / 2;
                int cols = CountColumnsNeededVisual(fragments, mid);

                if (cols <= colCount)
                    hi = mid;
                else
                    lo = mid + 0.5;
            }

            columnHeight = Math.Ceiling(hi);

            if (columnHeight > maxAllowedHeight)
                columnHeight = maxAllowedHeight;
        }

        if (columnHeight <= 0)
            return;

        // CSS Fragmentation §3: When fragments contain boxes with visible
        // overflow that exceeds the column height (e.g. height: 0 parents
        // with overflowing children), flatten the hierarchy by collecting
        // the deepest fragmentable blocks from inside those containers.
        bool needsDeepFragment = false;

        foreach (var frag in fragments)
        {
            double visualH = GetVisualBottom(frag) - frag.Location.Y;

            if (visualH > columnHeight + 0.5 && frag.Boxes.Count > 0)
            {
                needsDeepFragment = true;
                break;
            }
        }

        if (needsDeepFragment)
        {
            var deepFragments = new List<CssBox>();

            foreach (var frag in fragments)
            {
                double visualH = GetVisualBottom(frag) - frag.Location.Y;

                if (visualH > columnHeight + 0.5 && frag.Boxes.Count > 0)
                {
                    CollectFragmentableBlocksCore(frag, columnHeight, deepFragments, 0);
                }
                else
                {
                    deepFragments.Add(frag);
                }
            }

            if (deepFragments.Count > fragments.Count)
            {
                fragments = deepFragments;
                firstTop = fragments[0].Location.Y;
                lastBottom = firstTop;

                foreach (var frag in fragments)
                {
                    double vb = GetVisualBottom(frag);
                    if (vb > lastBottom)
                        lastBottom = vb;
                }

                totalContentHeight = lastBottom - firstTop;

                // Re-compute balanced column height for the new fragment set.
                if (!hasExplicitHeight && !(ColumnFill == "auto" && hasMaxHeight))
                {
                    double lo = 0;

                    foreach (var frag in fragments)
                    {
                        double fh = GetVisualBottom(frag) - frag.Location.Y;
                        if (fh > lo)
                            lo = fh;
                    }

                    double hi = totalContentHeight;

                    for (int iter = 0; iter < 20; iter++)
                    {
                        double mid = (lo + hi) / 2;
                        int cols = CountColumnsNeededVisual(fragments, mid);

                        if (cols <= colCount) hi = mid;
                        else lo = mid + 0.5;
                    }

                    columnHeight = Math.Ceiling(hi);

                    if (columnHeight > maxAllowedHeight)
                        columnHeight = maxAllowedHeight;
                }
            }
        }

        // One fragment that either fits in a column or cannot be cut has nothing to distribute and
        // nothing to fragment, so the column set leaves it exactly where the flow put it. Saying so
        // is what lets the search above accept a single child at all: columnising one anyway
        // narrows it to a column's width and moves it for no gain, which is what
        // `out-of-flow-in-multicolumn-019` and `overflowing-block-003` measured as a loss.
        if (fragments.Count == 1
            && (GetVisualBottom(fragments[0]) - fragments[0].Location.Y <= columnHeight + 0.5
                || !CanBeSlicedAcrossColumns(fragments[0])))
        {
            return;
        }

        // A box taller than the column it is in continues in the next one, which means cutting it
        // rather than moving it. Done here, before the distribution below, so the loop that places
        // fragments into columns sees a run of column-sized boxes and needs to know nothing about
        // fragmentation.
        fragments = SliceTallFragments(fragments, fragmentParent, columnHeight);

        // Distribute fragments across columns.
        int currentCol = 0;
        double currentY = containerTop;

        foreach (var frag in fragments)
        {
            double fragHeight = GetVisualBottom(frag) - frag.Location.Y;

            bool wouldOverflow = (currentY - containerTop) + fragHeight > columnHeight;
            // CSS Multi-column §3.3: column-count sets the column *width* but is
            // not a hard cap on the number of columns.  When content does not fit
            // in the determined number of columns (e.g. column-fill: auto with a
            // constrained block-size), additional "overflow columns" are created
            // in the inline direction rather than piling the remainder into the
            // last column.  Balance mode keeps content within colCount via the
            // height search above, so this only takes effect when genuinely
            // overflowing.
            if (wouldOverflow && currentY > containerTop + 0.5)
            {
                currentCol++;
                currentY = containerTop;
            }

            double targetX = columnLeft + currentCol * (columnWidth + columnGap)
                + (frag.Location.X - fragmentParent.Location.X);
            double targetY = currentY;

            double dx = targetX - frag.Location.X;
            double dy = targetY - frag.Location.Y;

            if (Math.Abs(dx) > 0.1 || Math.Abs(dy) > 0.1)
            {
                frag.OffsetLeft(dx);
                frag.OffsetTop(dy);
            }

            if (frag.Size.Width > columnWidth + 1)
            {
                frag.Size = new SizeF((float)columnWidth, frag.Size.Height);
            }

            currentY += fragHeight;
        }

        // Update container dimensions.
        double maxBottom = containerTop;
        foreach (var frag in fragments)
        {
            double vb = GetVisualBottom(frag);
            if (vb > maxBottom)
                maxBottom = vb;
        }

        double newBottom = maxBottom + ActualPaddingBottom + ActualBorderBottomWidth;
        if (newBottom < ActualBottom)
            ActualBottom = newBottom;

        if (fragmentParent != this)
        {
            double fpBottom = maxBottom + fragmentParent.ActualPaddingBottom + fragmentParent.ActualBorderBottomWidth;
            if (fpBottom < fragmentParent.ActualBottom)
                fragmentParent.ActualBottom = fpBottom;
        }

        // Overflow columns extend the inline (right) edge beyond the declared
        // column-count, so size the scrollable/overflow extent to the columns
        // actually used rather than the specified count.
        int usedCols = Math.Max(colCount, currentCol + 1);
        double rightEdge = columnLeft + usedCols * columnWidth + (usedCols - 1) * columnGap
            + ActualPaddingRight + ActualBorderRightWidth;

        if (rightEdge > ActualRight)
            ActualRight = rightEdge;
    }

    /// <summary>
    /// Cuts every fragment taller than one column into a run of column-tall pieces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CSS Fragmentation 3 §3: a box that does not fit in its fragmentainer continues in the next
    /// one. Distributing whole boxes covers a column set made of several blocks, but not the shape
    /// most of the fragmentation corpus is actually written in — <em>one</em> block, taller than the
    /// column set, whose decoration is the thing under test. <c>css-break/borders-003</c> is a
    /// 250px bordered box in three 100px columns; <c>css-page/fixedpos-011-print</c> is a 400px
    /// block in four; <c>css-break/background-image-000</c> is four of them at once. Without this
    /// the box stayed whole in the first column and ran out of the bottom of the column set.
    /// </para>
    /// <para>
    /// Only a box with nothing in it is cut. A slice is a real box in the tree rather than a paint
    /// instruction, so a box with content would have to have that content divided too — which is
    /// the general fragmentation this engine does not do. A box with no content of its own is
    /// entirely described by its own decoration, and is exactly what these tests are made of.
    /// </para>
    /// <para>
    /// The decoration is sliced, not repeated: <c>box-decoration-break</c>'s initial value is
    /// <c>slice</c> (§5.2), so the run is decorated as though it were one box and then cut — the
    /// border and its rounded corners belong to the two outer ends, and the cuts between them are
    /// square and open. That is what <c>borders-003</c> states in its own words: "The border should
    /// be rounded at the start (first column) and at the end (last column)."
    /// </para>
    /// </remarks>
    private static List<CssBox> SliceTallFragments(
        List<CssBox> fragments, CssBox fragmentParent, double columnHeight)
    {
        // Slices carry a column height that some earlier pass computed; this pass has computed its
        // own, so they are rebuilt rather than reused. Layout runs twice whenever the width is
        // unconstrained, and stale slices would otherwise accumulate.
        fragmentParent.Boxes.RemoveAll(box => box.IsColumnSlice);

        var sliced = new List<CssBox>();
        bool cut = false;

        foreach (var fragment in fragments)
        {
            if (fragment.IsColumnSlice)
                continue;

            double height = GetVisualBottom(fragment) - fragment.Location.Y;
            int pieces = columnHeight > 0 ? (int)Math.Ceiling(height / columnHeight - 0.001) : 1;

            if (pieces <= 1 || pieces > MaxColumnSlices || !CanBeSlicedAcrossColumns(fragment))
            {
                sliced.Add(fragment);
                continue;
            }

            cut = true;
            float width = fragment.Size.Width;

            for (int piece = 1; piece < pieces; piece++)
            {
                var slice = new CssBox(fragmentParent, fragment.HtmlTag, fragment.BaseUrl)
                {
                    IsColumnSlice = true,
                };

                slice.InheritStyle(fragment, everything: true);
                slice.LayoutEnvironment = fragment.LayoutEnvironment;
                slice.Location = fragment.Location;
                slice.Size = new SizeF(width, (float)Math.Min(columnHeight, height - piece * columnHeight));
                ShapeSlice(slice, first: false, last: piece == pieces - 1);
                sliced.Add(slice);
            }

            // The original keeps the first piece, so whatever else in the tree refers to this box
            // still refers to something in the flow.
            fragment.Size = new SizeF(width, (float)columnHeight);
            ShapeSlice(fragment, first: true, last: false);
            sliced.Insert(sliced.Count - (pieces - 1), fragment);
        }

        return cut ? sliced : fragments;

        static void ShapeSlice(CssBox slice, bool first, bool last)
        {
            if (!first)
            {
                slice.BorderTopStyle = CssConstants.None;
                slice.CornerNwRadius = "0";
                slice.CornerNeRadius = "0";
            }

            if (!last)
            {
                slice.BorderBottomStyle = CssConstants.None;
                slice.CornerSeRadius = "0";
                slice.CornerSwRadius = "0";
            }
        }
    }

    /// <summary>
    /// A bound on how many pieces one box is cut into — a runaway column height would otherwise
    /// turn a tall box into thousands of boxes.
    /// </summary>
    private const int MaxColumnSlices = 64;

    /// <summary>
    /// Whether a box puts anything in a column. The whitespace between two elements does not, and
    /// neither does an empty box with no size.
    /// </summary>
    private static bool GeneratesAColumnFragment(CssBox box)
    {
        if (box.Boxes.Count > 0)
            return true;

        foreach (var word in box.Words)
        {
            if (!word.IsSpaces || word.IsImage)
                return true;
        }

        return box.Size.Height > 0.01 || box.ActualBottom - box.Location.Y > 0.01;
    }

    /// <summary>
    /// Whether a box can be cut across a column boundary: it is in the flow, it is not monolithic,
    /// and it has no content of its own that would have to be divided with it.
    /// </summary>
    private static bool CanBeSlicedAcrossColumns(CssBox box)
    {
        if (box.Boxes.Count > 0 || box.Words.Count > 0)
            return false;

        if (box.Display == CssConstants.None
            || box.Float != CssConstants.None
            || box.Position is CssConstants.Absolute or CssConstants.Fixed
            || box.IsImage)
        {
            return false;
        }

        // CSS Fragmentation 3 §5.2: with `box-decoration-break: slice` the run is decorated as one
        // box and then cut, which a run of independent boxes can only imitate for decoration that
        // does not depend on where in the box it is drawn. A colour and a border do not; a
        // background image, a gradient and a box shadow are all positioned against the box they are
        // on, so slicing one paints it once per piece instead of once across the run — measured on
        // `css-break/background-image-000` through `-002` and `box-shadow-002` through `-005`,
        // every one of which got worse when they were cut. They keep the whole box until the paint
        // can be told what the unfragmented run looked like.
        if (!IsSliceableDecoration(box.BackgroundImage)
            || !IsSliceableDecoration(box.BackgroundGradient)
            || !IsSliceableDecoration(box.BoxShadow))
        {
            return false;
        }

        // CSS Fragmentation 3 §4.1: a scroll container and anything that says so outright is
        // monolithic — it moves to the next column whole rather than being cut.
        return (string.IsNullOrEmpty(box.Overflow)
                || box.Overflow.Equals(CssConstants.Visible, StringComparison.OrdinalIgnoreCase))
            && !BreakInsideAvoids(box.BreakInside)
            && !BreakInsideAvoids(box.PageBreakInside);
    }

    /// <summary>Whether a paint property is absent, so cutting the box does not disturb it.</summary>
    private static bool IsSliceableDecoration(string value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed)
            || trimmed.Equals(CssConstants.None, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks down through single-child containers to find the nearest
    /// descendant with multiple in-flow block children for multi-column
    /// fragmentation.
    /// </summary>
    private CssBox FindMultiColumnFragmentParent()
    {
        CssBox current = this;

        for (int depth = 0; depth < 10; depth++)
        {
            int inFlowCount = 0;
            CssBox onlyChild = null;

            foreach (var child in current.Boxes)
            {
                if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                    continue;

                if (child.Display == CssConstants.None)
                    continue;

                inFlowCount++;
                onlyChild = child;
            }

            if (inFlowCount > 1)
                return current;

            if (inFlowCount == 1 && onlyChild != null && onlyChild.Boxes.Count > 0)
            {
                current = onlyChild;
                continue;
            }

            break;
        }

        // Two boxes are enough to distribute. One is enough only if it can be *cut*, which is the
        // capability `SliceTallFragments` adds: the old rule — fewer than two, nothing to do — was
        // exactly right while a column set could only be filled by moving whole boxes into it, and
        // it meant a multi-column box with a single child was not columnised at all, however tall
        // that child was. That is the shape most of the fragmentation corpus is written in:
        // `css-break/borders-003` and `css-page/fixedpos-011-print` are each one tall block in a
        // column set, and each rendered as one column running out of the bottom of it.
        //
        // Accepting a single child that *cannot* be cut buys nothing and costs something: it
        // narrows the child to a column's width and moves it, for a column set that will still be
        // one column. `overflowing-block-003` and `out-of-flow-in-multicolumn-019` measured that as
        // a loss.
        //
        // The child is already the width of one column either way: the pre-layout pass in
        // `PerformLayout` narrows a multi-column container to a single column's width before its
        // children lay out, so its content is wrapped for a column and only its placement is left.
        if (current.Boxes.Count > 1)
            return current;

        return current.Boxes.Count == 1 && CanBeSlicedAcrossColumns(current.Boxes[0])
            ? current
            : null;
    }


    /// <summary>
    /// Counts columns needed using visual (overflow-aware) heights.
    /// </summary>
    private static int CountColumnsNeededVisual(List<CssBox> fragments, double columnHeight)
    {
        int cols = 1;
        double currentH = 0;

        foreach (var frag in fragments)
        {
            double fh = GetVisualBottom(frag) - frag.Location.Y;

            if (currentH + fh > columnHeight && currentH > 0.5)
            {
                cols++;
                currentH = fh;
            }
            else
            {
                currentH += fh;
            }
        }

        return cols;
    }

    /// <summary>
    /// Returns the visual bottom of a box, accounting for children that
    /// overflow a constrained height (e.g. height: 0 with visible overflow).
    /// </summary>
    private static double GetVisualBottom(CssBox box)
    {
        double bottom = box.ActualBottom;
        foreach (var child in box.Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (child.Display == CssConstants.None)
                continue;

            double cb = GetVisualBottom(child);

            if (cb > bottom)
                bottom = cb;
        }

        return bottom;
    }


    private static void CollectFragmentableBlocksCore(CssBox parent, double columnHeight, List<CssBox> result, int depth)
    {
        if (depth > 15)
            return; // safety limit

        foreach (var child in parent.Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (child.Display == CssConstants.None)
                continue;

            double childHeight = GetVisualBottom(child) - child.Location.Y;

            // If child fits in a column, or has break-inside: avoid, or
            // has no block children to further fragment, keep it as-is.
            bool avoidBreak = child.BreakInside == "avoid" || child.BreakInside == "avoid-column";
            bool hasBlockChildren = false;
            
            foreach (var gc in child.Boxes)
            {
                if (gc.Position != CssConstants.Absolute && gc.Position != CssConstants.Fixed
                    && gc.Display != CssConstants.None)
                {
                    hasBlockChildren = true;
                    break;
                }
            }

            if (childHeight <= columnHeight + 0.5 || avoidBreak || !hasBlockChildren)
            {
                result.Add(child);
            }
            else
            {
                // Recurse: this child is too tall and can be fragmented.
                CollectFragmentableBlocksCore(child, columnHeight, result, depth + 1);
            }
        }
    }
}
