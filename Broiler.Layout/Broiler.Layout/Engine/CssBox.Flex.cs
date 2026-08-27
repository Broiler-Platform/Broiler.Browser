using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using System.Drawing;
using System.Globalization;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// CSS Multi-column §6.2: resolves <c>column-gap</c>.  The initial value
    /// 'normal' is ≈ 1em; an explicit length (including 0) overrides it.
    /// </summary>
    internal double ResolveColumnGap()
    {
        if (!string.IsNullOrEmpty(ColumnGap) && ColumnGap != "normal")
            // column-gap resolves against the multicol container's own (already zoom-scaled) content
            // width, so a percentage carries the full effective factor; an absolute gap scales by zoom.
            return ParseUsedLength(ColumnGap, Size.Width, percentAgainstContainingBlock: false);

        return GetEmHeight(); // 'normal' ≈ 1em, already zoomed via GetEmHeight (increment 2)
    }

    private sealed class FlexItemLayout
    {
        public CssBox Box { get; init; }
        public double Grow { get; init; }
        public double Shrink { get; init; }
        public double BaseOuterWidth { get; init; }
        public double TargetOuterWidth { get; set; }

        /// <summary>
        /// The item's cross-axis content height when that size is definite before layout — its own
        /// <c>height</c>, or the height it stretches to in a container whose cross size is definite.
        /// <see langword="null"/> when the cross size is content-based, which is when CSS Flexbox
        /// §4.5 has no transferred size suggestion to offer.
        /// </summary>
        public double? DefiniteCrossContentHeight { get; init; }
    }

    private sealed class FlexLineLayout
    {
        public List<FlexItemLayout> Items { get; } = [];
        public double BaseOuterWidth { get; set; }
        public double CrossSize { get; set; }
    }

    private bool IsFlexContainer() => Display is "flex" or "inline-flex";

    /// <summary>
    /// CSS Flexbox §5.4 / CSS Display §3: reorders this container's children into <em>order-modified
    /// document order</em> — ascending <c>order</c>, document order preserved within an ordinal group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done by sorting <see cref="CssBoxProperties.Boxes"/> in place rather than inside one layout
    /// algorithm, because the children reach the page by several routes: a row flex container walks
    /// this list itself, a column one falls through to ordinary block/inline flow over the same list
    /// (the line-break-per-item path in <c>CssLayoutEngine</c>), and painting walks it too. `order`
    /// is defined to affect layout *and* paint order, so moving the boxes once serves all three.
    /// </para>
    /// <para>
    /// The sort is stable, so equal <c>order</c> keeps document order, and it is idempotent: a list
    /// already in order-modified order re-sorts to itself, which matters because layout runs more
    /// than once per render. Boxes whose <c>order</c> is absent or not an integer count as 0, the
    /// initial value.
    /// </para>
    /// </remarks>
    private void ApplyOrderModifiedDocumentOrder()
    {
        if (Boxes.Count < 2)
            return;

        var orders = new int[Boxes.Count];
        var reordered = false;
        for (var index = 0; index < Boxes.Count; index++)
        {
            orders[index] = ParseOrder(Boxes[index].Order);
            if (index > 0 && orders[index] < orders[index - 1])
                reordered = true;
        }

        if (!reordered)
            return;

        // List<T>.Sort is unstable, so pair each box with its document index and break ties on it.
        var sorted = new List<CssBox>(Boxes.Count);
        var indices = new List<int>(Boxes.Count);
        for (var index = 0; index < Boxes.Count; index++)
            indices.Add(index);

        indices.Sort((left, right) =>
        {
            var byOrder = orders[left].CompareTo(orders[right]);
            return byOrder != 0 ? byOrder : left.CompareTo(right);
        });

        foreach (var index in indices)
            sorted.Add(Boxes[index]);

        Boxes.Clear();
        Boxes.AddRange(sorted);
    }

    private static int ParseOrder(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order)
            ? order
            : 0;

    internal bool IsRowFlexContainer()
    {
        if (!IsFlexContainer())
            return false;

        string direction = FlexDirection?.Trim().ToLowerInvariant() ?? "row";
        return direction is not ("column" or "column-reverse");
    }

    /// <summary>A flex container whose main axis is the block axis, so its cross axis is inline.</summary>
    internal bool IsColumnFlexContainer() => IsFlexContainer() && !IsRowFlexContainer();

    internal void PerformFlexRowLayout(ILayoutEnvironment g)
    {
        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Flex);

        EnsureDescendantWordsMeasured(g);

        double contentLeft = ClientLeft;
        double contentTop = ClientTop;
        double contentWidth = Math.Max(0, Size.Width
            - ActualBorderLeftWidth - ActualBorderRightWidth
            - ActualPaddingLeft - ActualPaddingRight);

        double columnGap = ResolveFlexGap(ColumnGap, contentWidth);
        double rowGap = ResolveFlexGap(RowGap, Size.Height);
        bool wrap = FlexWrap is "wrap" or "wrap-reverse";

        var lines = new List<FlexLineLayout>();
        var currentLine = new FlexLineLayout();

        // Read before the items are measured, not after: an item's *cross* size is what CSS
        // Flexbox §4.5 transfers through an aspect ratio to bound its main size, and the container's
        // definite content height is where a stretched item's cross size comes from.
        double? definiteContentHeight = TryGetDefiniteContentHeight();

        foreach (var child in Boxes)
        {
            if (!IsInFlowFlexItem(child))
                continue;

            double? itemCrossContentHeight =
                ResolveFlexItemDefiniteCrossContentHeight(child, definiteContentHeight);

            var item = new FlexItemLayout
            {
                Box = child,
                Grow = ParseFlexFactor(child.FlexGrow, 0),
                Shrink = ParseFlexFactor(child.FlexShrink, 1),
                DefiniteCrossContentHeight = itemCrossContentHeight,
                BaseOuterWidth = ResolveFlexItemBaseOuterWidth(child, contentWidth, itemCrossContentHeight)
            };

            item.TargetOuterWidth = item.BaseOuterWidth;

            double candidateWidth = currentLine.BaseOuterWidth + item.BaseOuterWidth
                + (currentLine.Items.Count > 0 ? columnGap : 0);

            if (wrap && currentLine.Items.Count > 0 && candidateWidth > contentWidth + 0.5)
            {
                lines.Add(currentLine);
                currentLine = new FlexLineLayout();
            }

            currentLine.Items.Add(item);
            currentLine.BaseOuterWidth += item.BaseOuterWidth;
        }

        if (currentLine.Items.Count > 0 || lines.Count == 0)
            lines.Add(currentLine);

        double cursorY = contentTop;
        bool reverse = FlexDirection?.Trim().Equals("row-reverse", StringComparison.OrdinalIgnoreCase) == true;

        foreach (var line in lines)
        {
            ResolveFlexLineWidths(line, contentWidth, columnGap);

            foreach (var item in line.Items)
            {
                LayoutFlexItemAtTargetWidth(g, item.Box, item.TargetOuterWidth);

                double itemHeight = GetFlexItemOuterHeight(item.Box);

                if (itemHeight > line.CrossSize)
                    line.CrossSize = itemHeight;
            }

            // CSS Flexbox §9.4 step 15: a single-line container's line *is* its content box across
            // the cross axis when that is definite, rather than the tallest item — so a 100px-tall
            // container gives its line 100px to stretch into even when nothing in it is that tall.
            if (lines.Count == 1 && definiteContentHeight is { } definite && definite > line.CrossSize)
                line.CrossSize = definite;

            StretchFlexLineItems(line);

            double usedWidth = 0;

            foreach (var item in line.Items)
                usedWidth += item.TargetOuterWidth;

            int itemCount = line.Items.Count;
            double freeSpace = contentWidth - usedWidth - Math.Max(0, itemCount - 1) * columnGap;

            if (freeSpace < 0)
                freeSpace = 0;

            ResolveJustifyContent(itemCount, freeSpace, columnGap, out double lineOffset, out double itemGap);

            double cursorX = contentLeft + lineOffset;

            if (reverse)
                cursorX = contentLeft + contentWidth - lineOffset;

            for (int i = 0; i < itemCount; i++)
            {
                int itemIndex = reverse ? itemCount - i - 1 : i;
                var item = line.Items[itemIndex];
                var child = item.Box;

                double marginBoxWidth = item.TargetOuterWidth;
                double borderBoxWidth = Math.Max(0, marginBoxWidth
                    - child.ActualMarginLeft - child.ActualMarginRight);
                double itemLeft = reverse
                    ? cursorX - marginBoxWidth + child.ActualMarginLeft
                    : cursorX + child.ActualMarginLeft;

                double crossOffset = ResolveFlexCrossOffset(child, line.CrossSize);
                double itemTop = cursorY + crossOffset + child.ActualMarginTop;

                double dx = itemLeft - child.Location.X;
                double dy = itemTop - child.Location.Y;

                if (Math.Abs(dx) > 0.1)
                    child.OffsetLeft(dx);

                if (Math.Abs(dy) > 0.1)
                    child.OffsetTop(dy);

                if (reverse)
                    cursorX -= marginBoxWidth + itemGap;
                else
                    cursorX += marginBoxWidth + itemGap;

                // The temporary width is restored after child layout; keep
                // the used border-box width in Size for painting/layout dumps.
                if (borderBoxWidth > 0 && Math.Abs(child.Size.Width - borderBoxWidth) > 0.5)
                    child.Size = new SizeF((float)borderBoxWidth, child.Size.Height);
            }

            cursorY += line.CrossSize + rowGap;
        }

        if (lines.Count > 0)
            cursorY -= rowGap;

        ActualBottom = cursorY + ActualPaddingBottom + ActualBorderBottomWidth;
        ActualRight = Location.X + Size.Width;

        LayoutOutOfFlowFlexChildren(g, contentLeft, contentTop);
    }

    /// <summary>
    /// CSS Flexbox §4.1: an absolutely-positioned child of a flex container takes no part in
    /// flex layout, but it is still laid out — and painted — with the container's content-box
    /// start corner as its static position.
    /// </summary>
    /// <remarks>
    /// This path replaces the ordinary block-flow child loop wholesale, and that loop is what
    /// calls <see cref="PerformLayout"/> on every child. Without this, an out-of-flow child of a
    /// flex container was never laid out at all and so never reached the canvas: a
    /// <c>body { display: flex }</c> holding a fixed backdrop and an abspos panel painted
    /// neither (WPT css-transforms/dynamic-fixed-pos-cb-change, whose test *and* reference both
    /// rendered a bare background).
    /// </remarks>
    private void LayoutOutOfFlowFlexChildren(ILayoutEnvironment g, double contentLeft, double contentTop)
    {
        foreach (var child in Boxes)
        {
            if (child.Display == CssConstants.None
                || child.Position is not (CssConstants.Absolute or CssConstants.Fixed))
                continue;

            child.Location = new PointF((float)contentLeft, (float)contentTop);
            child.ActualBottom = contentTop;
            child.PerformLayout(g);

            // §4.1 again, for the axes no inset pinned: the static position is where the child
            // would sit "as if it were the sole flex item", which is the container's content-box
            // start corner. PerformLayout derives a *block container's* static position instead —
            // the in-flow advance past the preceding sibling — so without this an all-`auto`
            // child of a flex container holding one 10px-tall item landed 10px too low.
            bool hasLeft = IsSpecifiedInset(child.Left), hasRight = IsSpecifiedInset(child.Right);
            bool hasTop = IsSpecifiedInset(child.Top), hasBottom = IsSpecifiedInset(child.Bottom);

            if (!hasLeft && !hasRight)
            {
                double dx = contentLeft + child.ActualMarginLeft - child.Location.X;
                if (Math.Abs(dx) > 0.1)
                    child.OffsetLeft(dx);
            }

            if (!hasTop && !hasBottom)
            {
                double dy = contentTop + child.ActualMarginTop - child.Location.Y;
                if (Math.Abs(dy) > 0.1)
                    child.OffsetTop(dy);
            }
        }
    }

    private static bool IsSpecifiedInset(string value) =>
        !string.IsNullOrEmpty(value) && value != CssConstants.Auto;

    private static bool IsInFlowFlexItem(CssBox child) =>
        child.Display != CssConstants.None
        && child.Position is not (CssConstants.Absolute or CssConstants.Fixed);

    private static double ResolveFlexItemBaseOuterWidth(
        CssBox child, double containerContentWidth, double? definiteCrossContentHeight)
    {
        double borderBoxWidth;
        string basis = child.FlexBasis?.Trim();

        if (!string.IsNullOrEmpty(basis)
            && !basis.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && !basis.Equals("content", StringComparison.OrdinalIgnoreCase))
        {
            borderBoxWidth = child.ResolveSpecifiedWidthToBorderBox(
                ParseFlexLengthOrZero(child, basis, containerContentWidth));
        }
        else if (child.Width != CssConstants.Auto && !string.IsNullOrEmpty(child.Width)
            && !IsIntrinsicWidthKeyword(child.Width))
        {
            borderBoxWidth = child.ResolveSpecifiedWidthToBorderBox(
                ParseFlexLengthOrZero(child, child.Width, containerContentWidth));
        }
        // §9.2 step 3.B: an item with an intrinsic aspect ratio, a flex base size that would come
        // from its content, and a *definite* cross size takes its flex base size from that cross
        // size through the ratio — not from its natural main size. A 16x16 image stretched to a
        // 40px-tall row is 40px wide, and one whose `max-height` caps the stretch at 10px is 10px
        // wide; both are what css-flexbox/image-as-flexitem-size-003 measures, and Broiler left
        // every such item at its bitmap's width.
        else if (TryGetTransferredSizeSuggestion(
            child, definiteCrossContentHeight, authorRatioCounts: true, out double fromRatio))
        {
            borderBoxWidth = fromRatio;
        }
        else
        {
            child.GetMinMaxWidth(out _, out double preferred);

            if (double.IsNaN(preferred) || preferred < 0)
                preferred = 0;

            borderBoxWidth = preferred + child.ActualBorderLeftWidth + child.ActualBorderRightWidth;
        }

        borderBoxWidth = ClampFlexItemBorderBoxWidth(
            child, borderBoxWidth, containerContentWidth, definiteCrossContentHeight);
        return borderBoxWidth + child.ActualMarginLeft + child.ActualMarginRight;
    }

    /// <summary>
    /// CSS Flexbox §4.5 <em>specified size suggestion</em>: the item's own <c>width</c> as a
    /// border-box length when that width is definite, which is what caps the content-based
    /// minimum. An <c>auto</c> width, an intrinsic keyword or an absent one has no suggestion —
    /// the minimum is then the content size suggestion alone.
    /// </summary>
    private static bool TryGetSpecifiedSizeSuggestion(
        CssBox child, double containerContentWidth, out double borderBoxWidth)
    {
        borderBoxWidth = 0;

        if (child.Width == CssConstants.Auto
            || string.IsNullOrEmpty(child.Width)
            || IsIntrinsicWidthKeyword(child.Width))
        {
            return false;
        }

        borderBoxWidth = child.ResolveSpecifiedWidthToBorderBox(
            ParseFlexLengthOrZero(child, child.Width, containerContentWidth));
        return true;
    }

    /// <summary>
    /// The cross-axis <em>content</em> height a row flex item is known to have before its main size
    /// is resolved, after <c>min-height</c>/<c>max-height</c>: its own <c>height</c>, or the height
    /// it stretches to in a container whose cross size is definite, or — for a replaced item — its
    /// natural height. <see langword="null"/> when it has none of those, which is when there is
    /// nothing for CSS Flexbox §4.5 to transfer through the aspect ratio.
    /// </summary>
    /// <remarks>
    /// The cross <em>constraints</em> are the reason the natural height is a source here at all. An
    /// <c>&lt;img&gt;</c> with <c>max-height</c> and an auto height has no stated cross size, but
    /// the height it will use is still bounded — and css-flexbox/flex-minimum-width-flex-items-010
    /// and -012 are exactly that shape, asserting that the implied minimum main size follows the
    /// clamped cross size through the ratio rather than the bitmap's own width.
    /// </remarks>
    private double? ResolveFlexItemDefiniteCrossContentHeight(
        CssBox child, double? containerDefiniteContentHeight)
    {
        double blockEdges = child.ActualBorderTopWidth + child.ActualBorderBottomWidth
                          + child.ActualPaddingTop + child.ActualPaddingBottom;

        double? crossContentHeight = null;

        if (!string.IsNullOrEmpty(child.Height) && child.Height != CssConstants.Auto)
        {
            double specified = ParseFlexLengthOrZero(
                child, child.Height, containerDefiniteContentHeight ?? 0);
            crossContentHeight = child.ResolveSpecifiedHeightToBorderBox(specified) - blockEdges;
        }
        else if (containerDefiniteContentHeight is { } lineCross
                 && ResolveFlexItemAlignment(child) == "stretch"
                 && !child.IsSpecifiedMarginTopAuto
                 && !child.IsSpecifiedMarginBottomAuto)
        {
            // §9.4 step 11: an item with an auto cross size, no auto cross margin and `stretch`
            // alignment is sized to the line — and a single line's cross size is the container's
            // own definite content height.
            crossContentHeight = lineCross
                - child.ActualMarginTop - child.ActualMarginBottom
                - blockEdges;
        }
        else if (child.TryGetNaturalReplacedSize(out SizeF natural) && natural.Height > 0)
        {
            crossContentHeight = natural.Height;
        }

        if (crossContentHeight is not { } cross)
            return null;

        cross = child.ToContentBoxBounds(child.ResolveBlockSizeBounds(), blockEdges).Clamp(cross);
        return cross > 0 ? cross : null;
    }

    /// <summary>
    /// CSS Flexbox §4.5 <em>transferred size suggestion</em>: for an item with a preferred aspect
    /// ratio and a definite cross size, the main size that cross size converts to through the ratio.
    /// It is what bounds the content size suggestion, so a replaced item stretched to a short flex
    /// line is allowed to shrink to the width that ratio asks for rather than to its bitmap's.
    /// </summary>
    /// <param name="authorRatioCounts">
    /// Whether an author <c>aspect-ratio</c> on a non-replaced box counts. It does for the flex base
    /// size (§9.2 step 3.B reads "a preferred aspect ratio", which CSS Sizing 4 §4 gives any box),
    /// and it does not for the automatic minimum: §4.5's transferred size suggestion is stated for
    /// an item with an <em>intrinsic</em> ratio, and css-sizing/aspect-ratio/flex-aspect-ratio-002
    /// says so in as many words — "no transferred size suggestion since the flex item is
    /// non-replaced" — with its <c>aspect-ratio: 1/2</c> item taking the 100px its content asks for
    /// rather than the 50px the ratio would transfer.
    /// </param>
    private static bool TryGetTransferredSizeSuggestion(
        CssBox child, double? definiteCrossContentHeight, bool authorRatioCounts, out double borderBoxWidth)
    {
        borderBoxWidth = 0;

        if (definiteCrossContentHeight is not { } crossContentHeight || crossContentHeight <= 0)
            return false;

        bool hasNatural = child.TryGetNaturalReplacedSize(out SizeF natural) && natural.Height > 0;
        if (!hasNatural && !authorRatioCounts)
            return false;

        double ratio = hasNatural ? natural.Width / natural.Height : 0;
        if (TryParseAspectRatio(child.AspectRatio, out double preferred) && preferred > 0)
            ratio = preferred;

        if (ratio <= 0)
            return false;

        borderBoxWidth = child.ResolveSpecifiedWidthToBorderBox(crossContentHeight * ratio);
        return borderBoxWidth > 0;
    }

    private static double ParseFlexLengthOrZero(CssBox box, string value, double percentBase)
    {
        try
        {
            return box.ParseLengthWithLineHeight(value, percentBase);
        }
        catch
        {
            return 0;
        }
    }

    private static double ClampFlexItemBorderBoxWidth(
        CssBox child,
        double borderBoxWidth,
        double containerContentWidth,
        double? definiteCrossContentHeight)
    {
        if (!string.IsNullOrEmpty(child.MaxWidth) && !child.MaxWidth.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            double length = ParseFlexLengthOrZero(child, child.MaxWidth, containerContentWidth);
            double maxWidth = child.ResolveSpecifiedWidthToBorderBox(length);

            if (borderBoxWidth > maxWidth)
                borderBoxWidth = maxWidth;
        }

        bool useAutomaticMinWidth = !child.IsMinWidthSpecified || child.MinWidth.Equals("auto", StringComparison.OrdinalIgnoreCase);

        if (useAutomaticMinWidth)
        {
            // CSS Flexbox §4.5 automatic minimum size. The content-based minimum is the *content
            // size suggestion* — the item's min-content size — and, when the item has a definite
            // specified main size, no larger than that *specified size suggestion* either.
            //
            // GetMinMaxWidth folds the box's own `width` into what it reports, so it answered the
            // specified width rather than the content's: a `width: 300px` item declared a 300px
            // floor, and `flex-shrink` — which defaults to 1 — could not move it at all. A 300px
            // item in a 100px container simply overflowed, and every flex layout that relies on
            // shrinking an explicitly-sized item was wrong by the amount it should have shrunk.
            // GetContentMinMaxWidth is the same measurement with the box's own width suppressed,
            // which is the suggestion §4.5 actually asks for. `min-width: 0` was the workaround
            // that worked, because it takes the branch below instead of this one.
            child.GetContentMinMaxWidth(out double contentSuggestion, out _);

            // §4.5 *content size suggestion*: the item's min-content main size. For a replaced item
            // that is its NATURAL inline size — the bitmap's width — not the width the author
            // declared. The measurement above reports the width the box was laid out at even with
            // the box's own width suppressed (its whole purpose), because a replaced box's single
            // word carries the used size; so an <img style="width:999px"> claimed a 999px
            // min-content size and pinned the item there however much its container asked it to
            // shrink. Only the flex minimum reads it this way — grid track sizing asks the same
            // method a different question — so the correction lives here.
            if (child.TryGetNaturalReplacedSize(out SizeF naturalItem) && naturalItem.Width > 0)
            {
                contentSuggestion = naturalItem.Width
                    + child.ActualBorderLeftWidth + child.ActualBorderRightWidth
                    + child.ActualPaddingLeft + child.ActualPaddingRight;
            }

            // §4.5 *transferred size suggestion*: when the item has a preferred aspect ratio and a
            // definite cross size, that cross size converted through the ratio bounds the content
            // size suggestion. An <img> stretched to a 50px-tall flex line has a min-content width
            // of 50 × 300/150 = 100px, not the 300px its bitmap is wide — which is what
            // css-flexbox/flex-minimum-width-flex-items-013 measures, and what decides whether an
            // image in a fixed-height flex row shrinks to its line or overflows it.
            if (TryGetTransferredSizeSuggestion(
                    child, definiteCrossContentHeight, authorRatioCounts: false, out double transferred)
                && transferred < contentSuggestion)
            {
                contentSuggestion = transferred;
            }

            double minContentWidth = contentSuggestion;

            if (TryGetSpecifiedSizeSuggestion(child, containerContentWidth, out double specified)
                && specified < minContentWidth)
            {
                minContentWidth = specified;
            }

            if (!double.IsNaN(minContentWidth) && minContentWidth > 0)
            {
                double minBorderBoxWidth = minContentWidth + child.ActualBorderLeftWidth + child.ActualBorderRightWidth;

                if (borderBoxWidth < minBorderBoxWidth)
                    borderBoxWidth = minBorderBoxWidth;
            }
        }
        else if (TryResolveIntrinsicMinWidth(child, containerContentWidth, out double intrinsicMinWidth))
        {
            if (borderBoxWidth < intrinsicMinWidth)
                borderBoxWidth = intrinsicMinWidth;
        }
        else if (!string.IsNullOrEmpty(child.MinWidth) && !child.MinWidth.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            double length = ParseFlexLengthOrZero(child, child.MinWidth, containerContentWidth);
            double minWidth = child.ResolveSpecifiedWidthToBorderBox(length);

            if (borderBoxWidth < minWidth)
                borderBoxWidth = minWidth;
        }

        return Math.Max(0, borderBoxWidth);
    }

    /// <summary>
    /// CSS Sizing 3 §5: resolves a <c>min-width</c> written as an intrinsic keyword —
    /// <c>min-content</c>, <c>max-content</c> or <c>fit-content</c>, including the
    /// <c>-webkit-</c>/<c>-moz-</c> spellings a component library still ships — to a used
    /// border-box length for the flex item's minimum.
    /// </summary>
    /// <remarks>
    /// Such a value is not a length, so the length parse below it returned 0 and the item was left
    /// with no minimum at all — the opposite of what the one spelling that exists to stop an item
    /// collapsing under its own content asks for. <c>min-width: fit-content</c> paired with
    /// <c>overflow: hidden</c> is how a chip or pill that must not clip its own label is written,
    /// and duckduckgo.com's search/Ask-AI mode chips are declared exactly that way (a later rule
    /// in their cascade then pins the width to a variable, so this is not what clips them; the
    /// same declaration on its own shrank to nothing and clipped in Broiler where it does not in
    /// Chromium).
    /// </remarks>
    private static bool TryResolveIntrinsicMinWidth(CssBox child, double containerContentWidth, out double borderBoxWidth)
    {
        borderBoxWidth = 0;

        var minWidth = child.MinWidth?.Trim();
        if (string.IsNullOrEmpty(minWidth))
            return false;

        bool isMinContent = minWidth.Equals("min-content", StringComparison.OrdinalIgnoreCase);
        bool isMaxContent = minWidth.Equals("max-content", StringComparison.OrdinalIgnoreCase);
        bool isFitContent = minWidth.Equals("fit-content", StringComparison.OrdinalIgnoreCase)
            || minWidth.Equals("-webkit-fit-content", StringComparison.OrdinalIgnoreCase)
            || minWidth.Equals("-moz-fit-content", StringComparison.OrdinalIgnoreCase);

        if (!isMinContent && !isMaxContent && !isFitContent)
            return false;

        child.GetMinMaxWidth(out double minContent, out double maxContent);

        if (double.IsNaN(minContent) || minContent < 0)
            minContent = 0;
        if (double.IsNaN(maxContent) || maxContent < minContent)
            maxContent = minContent;

        // fit-content(available) = min(max-content, max(min-content, available)), CSS Sizing 3 §5.1.
        double used = isMinContent
            ? minContent
            : isMaxContent
                ? maxContent
                : Math.Min(maxContent, Math.Max(minContent, containerContentWidth));

        borderBoxWidth = used + child.ActualBorderLeftWidth + child.ActualBorderRightWidth;
        return true;
    }

    private static void ResolveFlexLineWidths(FlexLineLayout line, double contentWidth, double columnGap)
    {
        int itemCount = line.Items.Count;

        if (itemCount == 0)
            return;

        double gapTotal = Math.Max(0, itemCount - 1) * columnGap;
        double freeSpace = contentWidth - line.BaseOuterWidth - gapTotal;

        if (freeSpace > 0.5)
        {
            double growTotal = 0;

            foreach (var item in line.Items)
                growTotal += item.Grow;

            if (growTotal > 0)
            {
                // §9.7 step 4: every item's target main size is clamped by its used min and max
                // main sizes, whether the distribution grew it or shrank it. Only the shrink
                // branch below clamped, so `flex: 1` grew an item straight past its own
                // `max-width` — a `max-width: 10px` item in a 40px row came out 40px wide
                // (css-flexbox/image-as-flexitem-size-005).
                foreach (var item in line.Items)
                {
                    double target = item.BaseOuterWidth + freeSpace * (item.Grow / growTotal);
                    double marginWidth = item.Box.ActualMarginLeft + item.Box.ActualMarginRight;

                    item.TargetOuterWidth = marginWidth + ClampFlexItemBorderBoxWidth(
                        item.Box,
                        Math.Max(0, target - marginWidth),
                        contentWidth,
                        item.DefiniteCrossContentHeight);
                }
            }
        }
        else if (freeSpace < -0.5)
        {
            double shrinkTotal = 0;

            foreach (var item in line.Items)
                shrinkTotal += item.Shrink * Math.Max(0, item.BaseOuterWidth);

            if (shrinkTotal > 0)
            {
                foreach (var item in line.Items)
                {
                    double shrinkShare = (item.Shrink * Math.Max(0, item.BaseOuterWidth)) / shrinkTotal;
                    double target = item.BaseOuterWidth + freeSpace * shrinkShare;
                    double marginWidth = item.Box.ActualMarginLeft + item.Box.ActualMarginRight;
                    double borderBoxWidth = ClampFlexItemBorderBoxWidth(
                        item.Box,
                        Math.Max(0, target - marginWidth),
                        contentWidth,
                        item.DefiniteCrossContentHeight);

                    item.TargetOuterWidth = borderBoxWidth + marginWidth;
                }
            }
        }
    }

    /// <summary>
    /// CSS Flexbox §9.7: distributes a <c>column</c> flex container's free space along its main
    /// (block) axis, growing its items into what is left over and shrinking them into what is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A column container has no flex algorithm of its own here — its children stack through
    /// ordinary block flow — so until this ran, <c>flex-grow</c> did nothing at all in a column:
    /// a lone <c>flex-grow: 1</c> item in a 100px-tall container stayed at its content height.
    /// This is the resolve-flexible-lengths step over that stack, applied afterwards.
    /// </para>
    /// <para>
    /// It runs only when the container's main size is <b>definite</b>, which per §9.2 means a
    /// specified <c>height</c> (then clamped by <c>min-height</c>/<c>max-height</c>) or a block
    /// size the box's own inset pair solves for — not a <c>min-height</c> alone. The distinction
    /// is exactly what css-flexbox/percentage-heights-003 pins: its <c>height: 0;
    /// min-height: 100%</c> containers flex their items to the clamped 100px, and its
    /// <c>min-height: 100%</c>-only containers, whose main size stays content-based, leave them at
    /// zero.
    /// </para>
    /// <para>
    /// Each flexed item is laid out again at its target height rather than resized in place,
    /// because a percentage-height descendant resolves against the item's used height — the
    /// <c>span { height: 100% }</c> the same test measures — and poking <c>Size</c> alone would
    /// leave it reading the pre-flex one. Single-line only: <c>column wrap</c> is
    /// <see cref="PerformFlexColumnLineLayout"/>'s.
    /// </para>
    /// </remarks>
    private void ApplyFlexColumnMainAxisSizing(ILayoutEnvironment g)
    {
        if (!IsFlexContainer() || IsRowFlexContainer())
            return;

        // `column` names the *block* axis, so under a vertical writing mode the main axis is
        // horizontal — the opposite of every axis this pass reads, starting with the height it
        // takes for the container's main size. PerformFlexColumnLineLayout declines for the same
        // reason; growing on the wrong axis was merely inert here (a vertical container's items
        // rarely leave vertical free space to hand out), but shrinking on it is not, and
        // css-flexbox/flexbox-column-row-gap-002's `vertical-lr` container squashed all six of its
        // 28px items to fit a main size that is not its main size.
        if (IsVerticalWritingMode(WritingMode))
            return;

        if (TryGetDefiniteMainAxisContentHeight() is not { } mainSize)
            return;

        var items = new List<CssBox>();

        foreach (var child in Boxes)
        {
            if (!IsInFlowFlexItem(child) || IsCollapsibleWhitespaceItem(child))
                continue;

            items.Add(child);
        }

        if (items.Count == 0)
            return;

        double rowGap = ResolveFlexGap(RowGap, mainSize);

        if (!ResolveFlexColumnMainSizes(g, items, mainSize, rowGap, out var targets))
            return;

        double cursorY = ClientTop;
        bool moved = false;

        for (int i = 0; i < items.Count; i++)
        {
            var child = items[i];

            if (Math.Abs(targets[i] - GetFlexItemOuterHeight(child)) > 0.5)
            {
                LayoutFlexItemAtTargetHeight(g, child, Math.Max(0, targets[i]));
                moved = true;
            }

            // Re-stack from the top even for an item that did not flex: an earlier sibling that
            // did has moved everything after it.
            double top = cursorY + child.ActualMarginTop;
            double dy = top - child.Location.Y;

            if (Math.Abs(dy) > 0.1)
            {
                child.OffsetTop(dy);
                moved = true;
            }

            cursorY += GetFlexItemOuterHeight(child) + rowGap;
        }

        if (!moved)
            return;

        cursorY -= rowGap;   // the trailing gap the loop added after the last item
        ActualBottom = Math.Max(ActualBottom, cursorY + ActualPaddingBottom + ActualBorderBottomWidth);
    }

    /// <summary>
    /// CSS Flexbox §9.2, §9.4 and §9.7 over one column flex line: each item's flex base size in
    /// the block axis, clamped to its hypothetical main size, and the target outer height that
    /// growing or shrinking into <paramref name="mainSize"/> then gives it.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when no item's target differs from the height block flow already
    /// gave it, so the caller can leave the stack exactly as it stands.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The base size is read from <c>flex-basis</c> (or, when that is <c>auto</c>, from the item's
    /// own <c>height</c>) rather than from the height block flow gave it. Those coincide for an
    /// item sized by its content, which is why the growth-only pass this replaces got away with
    /// measuring: they part company the moment <c>flex-basis</c> names a block size of its own,
    /// which block flow does not implement and so had simply ignored — WPT
    /// css-flexbox/percentage-heights-002's <c>flex-basis: 100%</c> pane sat at the height of the
    /// one line of text in it.
    /// </para>
    /// <para>
    /// Shrinking is the half of §9.7 that was left out here until now, on the grounds that §4.5's
    /// automatic minimum size did not exist in this axis to floor it with. It does now
    /// (<see cref="ClampFlexColumnItemBorderBoxHeight"/>), so an item that overflows its container
    /// shrinks the way <c>flex-shrink: 1</c> — the initial value, and therefore what nearly every
    /// column flex item on the web has — asks it to, and no further than its own content.
    /// </para>
    /// <para>
    /// That clamp is applied twice, and both are load-bearing: to the base size, giving §9.4
    /// step 3's hypothetical main size that free space is measured from, and again to each
    /// distributed target (§9.7 step 4). Clamping only the shrink branch is not enough — an item
    /// that takes no share at all still has a minimum and a maximum. <c>flex-basis: 0</c> with a
    /// 100px child in a 10px container (<c>flex-minimum-height-flex-items-011</c>) is floored at
    /// its content either way, and <c>max-height: min-content</c> over a <c>flex-basis: 200px</c>
    /// (<c>flex-item-max-height-min-content</c>) is capped at it.
    /// </para>
    /// </remarks>
    private bool ResolveFlexColumnMainSizes(
        ILayoutEnvironment g, List<CssBox> items, double mainSize, double rowGap, out double[] targets)
    {
        int count = items.Count;
        targets = new double[count];

        // The §4.5 content size suggestion, memoized: reading it re-lays an item out, and each is
        // read by both clamps below.
        var contentHeights = new double?[count];
        double usedOuterHeight = 0;

        for (int i = 0; i < count; i++)
        {
            targets[i] = ClampFlexColumnItemOuterHeight(
                g, items[i], ResolveFlexItemBaseOuterHeight(items[i], mainSize), mainSize, ref contentHeights[i]);

            usedOuterHeight += targets[i];
        }

        double freeSpace = mainSize - usedOuterHeight - Math.Max(0, count - 1) * rowGap;

        if (freeSpace > 0.5)
        {
            double growTotal = 0;

            foreach (var child in items)
                growTotal += ParseFlexFactor(child.FlexGrow, 0);

            if (growTotal > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    targets[i] = ClampFlexColumnItemOuterHeight(
                        g, items[i],
                        targets[i] + freeSpace * (ParseFlexFactor(items[i].FlexGrow, 0) / growTotal),
                        mainSize, ref contentHeights[i]);
                }
            }
        }
        else if (freeSpace < -0.5)
        {
            // §9.7 step 2: the shrink factor is scaled by the base size, so a large item gives up
            // more of the overflow than a small one with the same `flex-shrink`.
            double shrinkTotal = 0;

            for (int i = 0; i < count; i++)
                shrinkTotal += ParseFlexFactor(items[i].FlexShrink, 1) * Math.Max(0, targets[i]);

            if (shrinkTotal > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    double share = (ParseFlexFactor(items[i].FlexShrink, 1) * Math.Max(0, targets[i])) / shrinkTotal;

                    targets[i] = ClampFlexColumnItemOuterHeight(
                        g, items[i], targets[i] + freeSpace * share, mainSize, ref contentHeights[i]);
                }
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (Math.Abs(targets[i] - GetFlexItemOuterHeight(items[i])) > 0.5)
                return true;
        }

        return false;
    }

    /// <summary>
    /// <see cref="ClampFlexColumnItemBorderBoxHeight"/> over an <em>outer</em> height, since that
    /// is the currency §9.7 distributes free space in.
    /// </summary>
    private static double ClampFlexColumnItemOuterHeight(
        ILayoutEnvironment g, CssBox child, double outerHeight, double containerContentHeight,
        ref double? contentHeight)
    {
        double marginHeight = child.ActualMarginTop + child.ActualMarginBottom;
        double borderBoxHeight = ClampFlexColumnItemBorderBoxHeight(
            g, child, Math.Max(0, outerHeight - marginHeight), containerContentHeight, ref contentHeight);

        return borderBoxHeight + marginHeight;
    }

    /// <summary>
    /// CSS Flexbox §9.2 step 3 in the block axis: an item's flex base size, as an outer height.
    /// <c>flex-basis</c> when it names a length, the item's own definite <c>height</c> when it does
    /// not, and the block size its content produced when neither does.
    /// </summary>
    private static double ResolveFlexItemBaseOuterHeight(CssBox child, double containerContentHeight)
    {
        double marginHeight = child.ActualMarginTop + child.ActualMarginBottom;
        double borderBoxHeight;
        string basis = child.FlexBasis?.Trim();

        if (!string.IsNullOrEmpty(basis)
            && !basis.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && !basis.Equals("content", StringComparison.OrdinalIgnoreCase)
            && !IsIntrinsicSizingHeightKeyword(basis))
        {
            borderBoxHeight = child.ResolveSpecifiedHeightToBorderBox(
                ParseFlexLengthOrZero(child, basis, containerContentHeight));
        }
        else if (!string.IsNullOrEmpty(child.Height)
            && child.Height != CssConstants.Auto
            && !IsIntrinsicSizingHeightKeyword(child.Height)
            && !child.HeightPercentageResolvesToAuto())
        {
            borderBoxHeight = child.ResolveSpecifiedHeightToBorderBox(
                ParseFlexLengthOrZero(child, child.Height, containerContentHeight));
        }
        else
        {
            // The height block flow settled on, which for an auto-height item *is* its content
            // size — the third branch of §9.2 step 3, already measured.
            return GetFlexItemOuterHeight(child);
        }

        return Math.Max(0, borderBoxHeight) + marginHeight;
    }

    /// <summary>
    /// CSS Flexbox §4.5 and §9.7 step 4 in the block axis: clamps an item's target border-box
    /// height by its <c>min-height</c>/<c>max-height</c>, treating an undeclared
    /// <c>min-height</c> as the <c>auto</c> it computes to on a flex item — the automatic minimum
    /// size, which is the item's own content block size, and no larger than a definite
    /// <c>height</c> it declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the floor that makes column shrinking safe to turn on: without it a container
    /// smaller than its content squashes every item towards nothing, because <c>flex-shrink</c>
    /// defaults to <c>1</c>. The row axis has had the same clamp for as long as it has shrunk
    /// (<see cref="ClampFlexItemBorderBoxWidth"/>); this is its block-axis twin, and reads the
    /// content size the same way — by measuring the item with its own size declaration suppressed,
    /// so a <c>height: 300px</c> item does not declare a 300px floor and become unshrinkable.
    /// </para>
    /// <para>
    /// One thing it adds that the row clamp does not: §4.5 gives an automatic minimum size only to
    /// an item whose main-axis <c>overflow</c> is <c>visible</c>. A scroll container states that
    /// its content may be scrolled rather than that its box must fit it, and
    /// <c>flex: 1; overflow: auto</c> is how the scrolling pane of nearly every column layout on
    /// the web is written — flooring it at its content is what makes such a pane push its own
    /// container open instead of scrolling.
    /// </para>
    /// <para>
    /// <paramref name="contentHeight"/> memoizes the content measurement across the two clamps one
    /// item takes (base size, then distributed target), because taking it re-lays the item out.
    /// </para>
    /// </remarks>
    private static double ClampFlexColumnItemBorderBoxHeight(
        ILayoutEnvironment g, CssBox child, double borderBoxHeight, double containerContentHeight,
        ref double? contentHeight)
    {
        double? measured = contentHeight;

        double ContentHeight() => measured ??= MeasureFlexColumnItemContentBorderBoxHeight(g, child);

        if (!string.IsNullOrEmpty(child.MaxHeight) && !child.MaxHeight.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // An intrinsic `max-height` is a content measurement, not a length: parsing it as one
            // yields 0 and caps the item at nothing.
            double maxHeight = IsIntrinsicSizingHeightKeyword(child.MaxHeight)
                ? ContentHeight()
                : child.ResolveSpecifiedHeightToBorderBox(
                    ParseFlexLengthOrZero(child, child.MaxHeight, containerContentHeight));

            if (borderBoxHeight > maxHeight)
                borderBoxHeight = maxHeight;
        }

        bool useAutomaticMinHeight =
            !child.IsMinHeightSpecified || child.MinHeight.Equals("auto", StringComparison.OrdinalIgnoreCase);

        if (useAutomaticMinHeight)
        {
            if (!IsVisibleOverflow(child.Overflow))
            {
                contentHeight = measured;
                return Math.Max(0, borderBoxHeight);
            }

            double minBorderBoxHeight = ContentHeight();

            // §4.5 in the block axis, the twin of the two corrections
            // <see cref="ClampFlexItemBorderBoxWidth"/> makes: a replaced item's *content size
            // suggestion* is its natural block size rather than the height block flow settled on
            // (which is the height the container already squashed it to), and its *transferred size
            // suggestion* — its definite cross size through the aspect ratio — bounds that.
            if (child.TryGetNaturalReplacedSize(out SizeF naturalItem) && naturalItem.Height > 0)
                minBorderBoxHeight = child.ResolveSpecifiedHeightToBorderBox(naturalItem.Height);

            if (TryGetColumnTransferredSizeSuggestion(child, out double transferred)
                && transferred < minBorderBoxHeight)
            {
                minBorderBoxHeight = transferred;
            }

            if (TryGetSpecifiedBlockSizeSuggestion(child, containerContentHeight, out double specified)
                && specified < minBorderBoxHeight)
            {
                minBorderBoxHeight = specified;
            }

            if (borderBoxHeight < minBorderBoxHeight)
                borderBoxHeight = minBorderBoxHeight;
        }
        else if (!string.IsNullOrEmpty(child.MinHeight)
            && !child.MinHeight.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            double minHeight = IsIntrinsicSizingHeightKeyword(child.MinHeight)
                ? ContentHeight()
                : child.ResolveSpecifiedHeightToBorderBox(
                    ParseFlexLengthOrZero(child, child.MinHeight, containerContentHeight));

            if (borderBoxHeight < minHeight)
                borderBoxHeight = minHeight;
        }

        contentHeight = measured;
        return Math.Max(0, borderBoxHeight);
    }

    /// <summary>
    /// CSS Flexbox §4.5 <em>transferred size suggestion</em> in the block axis: the height a column
    /// flex item's cross (inline) size converts to through its preferred aspect ratio. The cross
    /// size is the width the item has already been given — a column container resolves its items'
    /// inline axis before their main sizes — clamped by <c>min-width</c>/<c>max-width</c>.
    /// </summary>
    private static bool TryGetColumnTransferredSizeSuggestion(CssBox child, out double borderBoxHeight)
    {
        borderBoxHeight = 0;

        if (!child.TryGetNaturalReplacedSize(out SizeF natural) || natural.Height <= 0)
            return false;

        double ratio = natural.Width / natural.Height;
        if (TryParseAspectRatio(child.AspectRatio, out double preferred) && preferred > 0)
            ratio = preferred;

        if (ratio <= 0)
            return false;

        double inlineEdges = child.ActualBorderLeftWidth + child.ActualBorderRightWidth
                           + child.ActualPaddingLeft + child.ActualPaddingRight;
        double crossContentWidth = child.Size.Width - inlineEdges;

        if (crossContentWidth <= 0)
            return false;

        crossContentWidth = child
            .ToContentBoxBounds(child.ResolveInlineSizeBounds(), inlineEdges)
            .Clamp(crossContentWidth);

        if (crossContentWidth <= 0)
            return false;

        borderBoxHeight = child.ResolveSpecifiedHeightToBorderBox(crossContentWidth / ratio);
        return borderBoxHeight > 0;
    }

    private static bool IsVisibleOverflow(string overflow) =>
        string.IsNullOrEmpty(overflow)
        || overflow.Equals("visible", StringComparison.OrdinalIgnoreCase)
        || overflow.Equals("clip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CSS Flexbox §4.5 <em>specified size suggestion</em> in the block axis — the block-axis twin
    /// of <see cref="TryGetSpecifiedSizeSuggestion"/>.
    /// </summary>
    private static bool TryGetSpecifiedBlockSizeSuggestion(
        CssBox child, double containerContentHeight, out double borderBoxHeight)
    {
        borderBoxHeight = 0;

        if (string.IsNullOrEmpty(child.Height)
            || child.Height == CssConstants.Auto
            || IsIntrinsicSizingHeightKeyword(child.Height)
            || child.HeightPercentageResolvesToAuto())
        {
            return false;
        }

        borderBoxHeight = child.ResolveSpecifiedHeightToBorderBox(
            ParseFlexLengthOrZero(child, child.Height, containerContentHeight));
        return true;
    }

    /// <summary>
    /// CSS Flexbox §4.5 <em>content size suggestion</em> in the block axis: the border-box height
    /// the item's content alone produces, measured by laying it out with its own <c>height</c>
    /// declaration suppressed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An item that declares no height needs no measurement at all — block flow has already
    /// produced exactly this number, and the box is left untouched.
    /// </para>
    /// <para>
    /// One that does is laid out again, and the inline axis is <b>pinned</b> across that pass. The
    /// item's used width at this point is the one the column path shrink-wrapped it to, and a
    /// re-layout would re-derive it from the <c>auto</c> declaration against the ordinary
    /// containing block instead — so measuring the block axis silently widened the item, and the
    /// cross sizes and offsets computed from that width before the measurement no longer described
    /// it (WPT css-flexbox/flex-lines/multi-line-wrap-with-column-reverse lost the 10px gutters
    /// between its three columns, which is what the widened items had swallowed). A size
    /// measurement must not move the axis it is not measuring.
    /// </para>
    /// <para>
    /// It does leave the item laid out at the measured content height, which is why
    /// <see cref="ResolveFlexColumnMainSizes"/>'s callers re-lay every item out whose target
    /// differs from its current height — and after a measurement, one whose target is its base
    /// size differs, so the pass restores it rather than leaving the measurement showing.
    /// </para>
    /// </remarks>
    private static double MeasureFlexColumnItemContentBorderBoxHeight(ILayoutEnvironment g, CssBox child)
    {
        double Measured() => Math.Max(0, child.ActualBottom - child.Location.Y);

        string savedHeight = child.Height;

        if (string.IsNullOrEmpty(savedHeight) || savedHeight == CssConstants.Auto)
            return Measured();

        string savedWidth = child.Width;
        double borderBoxWidth = child.Size.Width;
        double cssWidth = child.UsesBorderBoxSizing
            ? borderBoxWidth
            : borderBoxWidth
              - child.ActualPaddingLeft - child.ActualPaddingRight
              - child.ActualBorderLeftWidth - child.ActualBorderRightWidth;

        child.Width = FormatCssPx(Math.Max(0, cssWidth));
        child.Height = CssConstants.Auto;
        child.PerformLayout(g);
        child.Width = savedWidth;
        child.Height = savedHeight;

        return Measured();
    }

    private sealed class FlexColumnLine
    {
        public List<CssBox> Items { get; } = [];

        /// <summary>Sum of the items' outer heights and the gaps between them.</summary>
        public double MainSize { get; set; }

        /// <summary>The line's extent across the inline axis.</summary>
        public double CrossSize { get; set; }
    }

    /// <summary>
    /// CSS Flexbox §9.3/§9.5/§9.6 for a <c>column</c> flex container: breaks its items into flex
    /// lines along the main (block) axis, stacks those lines across the inline axis, and packs
    /// each line from the direction's main-start — the block-<em>end</em> edge, for
    /// <c>column-reverse</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A column container has no flex algorithm of its own here: its items stack through ordinary
    /// block flow, one per line (the line-break-per-item path in <c>CssLayoutEngine</c>), and the
    /// two passes that follow this one size and align that single stack. Neither of the two things
    /// this pass exists for is expressible that way. <c>flex-wrap</c> in a column needs the stack
    /// broken into several, which no post-pass over one line can do — so a wrapping column
    /// container rendered every item in one full-width column that overflowed the container
    /// (WPT css-flexbox/flex-lines/multi-line-wrap-with-column-reverse laid its six items out as
    /// six stacked bands instead of three columns). And <c>column-reverse</c> reverses the main
    /// axis, so its items are ordered bottom-to-top and packed from the block-end edge, which the
    /// block flow that produced them cannot express at all — the keyword was parsed, recognised
    /// everywhere it selects the column path, and then laid out exactly like <c>column</c>.
    /// </para>
    /// <para>
    /// Returns <see langword="false"/> — leaving the existing single-line passes to run — for the
    /// case they already handle: one line in ordinary <c>column</c> order. So this changes nothing
    /// about a container that does not wrap and does not reverse, which is nearly all of them.
    /// </para>
    /// <para>
    /// Line breaking needs a definite main size to break against (§9.3), so a column container
    /// whose <c>height</c> is <c>auto</c> keeps everything on one line however it is wrapped —
    /// its main size is what its content adds up to, and nothing can overflow it.
    /// </para>
    /// </remarks>
    private bool PerformFlexColumnLineLayout(ILayoutEnvironment g)
    {
        if (!IsFlexContainer() || IsRowFlexContainer())
            return false;

        // `column` names the *block* axis, so under a vertical writing mode the main axis is
        // horizontal and the cross axis vertical — the opposite of every axis this pass reads.
        // The single-line passes it would displace make the same horizontal assumption, so
        // declining here leaves a vertical container exactly as it was rather than transposed
        // (WPT css-flexbox/gap-003-lr and -rl are `vertical-lr`/`-rl` column containers).
        if (IsVerticalWritingMode(WritingMode))
            return false;

        double contentWidth = Math.Max(0, Size.Width
            - ActualBorderLeftWidth - ActualBorderRightWidth
            - ActualPaddingLeft - ActualPaddingRight);

        if (contentWidth <= 0)
            return false;

        var items = new List<CssBox>();

        foreach (var child in Boxes)
        {
            if (!IsInFlowFlexItem(child) || IsCollapsibleWhitespaceItem(child))
                continue;

            items.Add(child);
        }

        if (items.Count == 0)
            return false;

        bool reverse = string.Equals(FlexDirection?.Trim(), "column-reverse", StringComparison.OrdinalIgnoreCase);
        bool wrap = FlexWrap is "wrap" or "wrap-reverse";
        double? definiteMain = TryGetDefiniteMainAxisContentHeight();
        double rowGap = ResolveFlexGap(RowGap, definiteMain ?? 0);
        double columnGap = ResolveFlexGap(ColumnGap, contentWidth);

        var lines = CollectFlexColumnLines(items, wrap ? definiteMain : null, rowGap);

        if (lines.Count == 1 && !reverse)
            return false;

        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Flex);

        ResolveFlexColumnLineCrossSizes(lines, contentWidth, columnGap, out double lineOffset, out double lineGap);

        // `wrap-reverse` flips the cross axis, so the first line is placed at the cross-end.
        if (FlexWrap == "wrap-reverse")
            lines.Reverse();

        double cursorX = ClientLeft + lineOffset;
        double usedMain = 0;

        foreach (var line in lines)
        {
            // Each line is laid out against the container's own main size when that is definite;
            // that is the size the lines were broken against, and what an item's `flex-grow` share
            // and `justify-content`'s free space are measured from.
            double lineMain = definiteMain ?? line.MainSize;

            ResolveFlexColumnLineItemSizes(g, line, lineMain, rowGap);
            PlaceFlexColumnLineItems(line, lineMain, rowGap, cursorX, reverse);

            usedMain = Math.Max(usedMain, Math.Min(lineMain, line.MainSize));
            cursorX += line.CrossSize + lineGap;
        }

        // This pass owns the container's in-flow content extent now: without this the block flow's
        // one-item-per-line stack would still be the height reported, which for a wrapped
        // container is every line's content added up rather than the tallest line's.
        ActualBottom = ClientTop + usedMain + ActualPaddingBottom + ActualBorderBottomWidth;
        return true;
    }

    /// <summary>
    /// CSS Flexbox §9.3 step 5: collects the items into flex lines, breaking before an item that
    /// would not fit in the main size still available on the current line.
    /// </summary>
    /// <param name="mainLimit">
    /// The definite main size to break against, or <c>null</c> for a single-line container — which
    /// is both what <c>nowrap</c> asks for and all an indefinite main size can produce.
    /// </param>
    private static List<FlexColumnLine> CollectFlexColumnLines(List<CssBox> items, double? mainLimit, double rowGap)
    {
        var lines = new List<FlexColumnLine>();
        var current = new FlexColumnLine();

        foreach (var child in items)
        {
            double outerHeight = GetFlexItemOuterHeight(child);

            if (mainLimit is { } limit && current.Items.Count > 0
                && current.MainSize + rowGap + outerHeight > limit + 0.5)
            {
                lines.Add(current);
                current = new FlexColumnLine();
            }

            if (current.Items.Count > 0)
                current.MainSize += rowGap;

            current.Items.Add(child);
            current.MainSize += outerHeight;
        }

        lines.Add(current);
        return lines;
    }

    /// <summary>
    /// CSS Flexbox §9.4 step 8 and §9.6 step 15/16: sizes each line across the cross (inline) axis
    /// — the widest item's margin box — and then distributes the container's leftover cross space
    /// per <c>align-content</c>.
    /// </summary>
    private void ResolveFlexColumnLineCrossSizes(
        List<FlexColumnLine> lines, double contentWidth, double columnGap, out double lineOffset, out double lineGap)
    {
        double usedCross = 0;

        foreach (var line in lines)
        {
            double cross = 0;

            foreach (var child in line.Items)
                cross = Math.Max(cross, child.Size.Width + child.ActualMarginLeft + child.ActualMarginRight);

            line.CrossSize = cross;
            usedCross += cross;
        }

        double freeCross = contentWidth - usedCross - Math.Max(0, lines.Count - 1) * columnGap;
        string align = NormalizeBoxAlignment(AlignContent);

        // `normal` behaves as `stretch` for a flex container, and it is what an unstyled one has.
        if (align is "" or "auto" or "normal")
            align = "stretch";

        if (align == "stretch")
        {
            lineOffset = 0;
            lineGap = columnGap;

            if (freeCross > 0.5)
            {
                double share = freeCross / lines.Count;

                foreach (var line in lines)
                    line.CrossSize += share;
            }

            return;
        }

        ResolveContentDistribution(align, lines.Count, Math.Max(0, freeCross), columnGap, out lineOffset, out lineGap);
    }

    /// <summary>
    /// CSS Flexbox §9.7 and §9.4 step 11 for one column flex line: flexes the items into the
    /// line's main size, and stretches an <c>auto</c>-width item across the line's cross size.
    /// </summary>
    /// <remarks>
    /// Both are settled in a single re-layout per item, because they resize different axes of the
    /// same box: laying an item out at its stretched width and then again at its flexed height
    /// would let the second layout re-derive the width from the <c>auto</c> declaration and undo
    /// the stretch. The main-axis half is <see cref="ResolveFlexColumnMainSizes"/>, shared with the
    /// single-line pass so a wrapped container flexes by the same rules an unwrapped one does.
    /// </remarks>
    private void ResolveFlexColumnLineItemSizes(ILayoutEnvironment g, FlexColumnLine line, double lineMain, double rowGap)
    {
        bool flexes = ResolveFlexColumnMainSizes(g, line.Items, lineMain, rowGap, out double[] targets);
        double mainSize = 0;

        for (int i = 0; i < line.Items.Count; i++)
        {
            var child = line.Items[i];
            double? targetHeight = null;
            double? targetWidth = null;

            if (flexes && Math.Abs(targets[i] - GetFlexItemOuterHeight(child)) > 0.5)
                targetHeight = Math.Max(0, targets[i]);

            if (ShouldStretchFlexColumnItemAcrossLine(child, line.CrossSize))
                targetWidth = line.CrossSize;

            if (targetHeight is not null || targetWidth is not null)
                LayoutFlexItemAtTargetSize(g, child, targetWidth, targetHeight);

            mainSize += GetFlexItemOuterHeight(child) + (i > 0 ? rowGap : 0);
        }

        line.MainSize = mainSize;
    }

    /// <summary>
    /// CSS Flexbox §9.4 step 11 on the axis a column container crosses: whether an item's
    /// <c>auto</c> cross size should be stretched to its line.
    /// </summary>
    private bool ShouldStretchFlexColumnItemAcrossLine(CssBox child, double lineCrossSize)
    {
        if (ResolveFlexItemAlignment(child) != "stretch")
            return false;
        if (!string.IsNullOrEmpty(child.Width) && child.Width != CssConstants.Auto)
            return false;
        if (child.IsSpecifiedMarginLeftAuto || child.IsSpecifiedMarginRightAuto)
            return false;

        double outerWidth = child.Size.Width + child.ActualMarginLeft + child.ActualMarginRight;
        return lineCrossSize - outerWidth > 0.5;
    }

    /// <summary>
    /// CSS Flexbox §9.5: places one column flex line's items along the main axis per
    /// <c>justify-content</c>, and across the line per <c>align-items</c>/<c>align-self</c>.
    /// </summary>
    /// <remarks>
    /// For <c>column-reverse</c> the main-start is the line's block-end edge, so the items run
    /// bottom-to-top from there: the first item in order-modified document order sits lowest.
    /// </remarks>
    private void PlaceFlexColumnLineItems(
        FlexColumnLine line, double lineMain, double rowGap, double lineLeft, bool reverse)
    {
        double freeMain = Math.Max(0, lineMain - line.MainSize);
        ResolveJustifyContent(line.Items.Count, freeMain, rowGap, out double mainOffset, out double itemGap);

        double cursorY = reverse
            ? ClientTop + lineMain - mainOffset
            : ClientTop + mainOffset;

        foreach (var child in line.Items)
        {
            double outerHeight = GetFlexItemOuterHeight(child);
            double outerWidth = child.Size.Width + child.ActualMarginLeft + child.ActualMarginRight;
            double crossOffset = ResolveFlexColumnCrossOffset(child, line.CrossSize, outerWidth);

            double top = reverse ? cursorY - outerHeight : cursorY;
            double targetTop = top + child.ActualMarginTop;
            double targetLeft = lineLeft + crossOffset + child.ActualMarginLeft;

            double dy = targetTop - child.Location.Y;
            double dx = targetLeft - child.Location.X;

            if (Math.Abs(dy) > 0.1)
                child.OffsetTop(dy);

            if (Math.Abs(dx) > 0.1)
                child.OffsetLeft(dx);

            cursorY += reverse ? -(outerHeight + itemGap) : outerHeight + itemGap;
        }
    }

    /// <summary>
    /// Where an item sits across its column flex line, per the cross-axis alignment that
    /// <c>align-self</c>/<c>align-items</c> resolve to. A stretched item has already been sized to
    /// the line and lands at 0.
    /// </summary>
    private double ResolveFlexColumnCrossOffset(CssBox child, double lineCrossSize, double itemOuterWidth)
    {
        double freeSpace = lineCrossSize - itemOuterWidth;

        if (freeSpace <= 0)
            return 0;

        return ResolveFlexItemAlignment(child) switch
        {
            "center" => freeSpace / 2,
            "end" or "flex-end" or "self-end" or "right" => freeSpace,
            _ => 0
        };
    }

    /// <summary>
    /// Lays a flex item out again at a target outer width, a target outer height, or both — one
    /// layout, so neither axis re-derives from the declaration the other one is overriding.
    /// </summary>
    private static void LayoutFlexItemAtTargetSize(
        ILayoutEnvironment g, CssBox child, double? targetOuterWidth, double? targetOuterHeight)
    {
        string savedWidth = child.Width;
        string savedHeight = child.Height;

        if (targetOuterWidth is { } outerWidth)
        {
            double borderBoxWidth = Math.Max(0, outerWidth - child.ActualMarginLeft - child.ActualMarginRight);
            double cssWidth = child.UsesBorderBoxSizing
                ? borderBoxWidth
                : borderBoxWidth
                  - child.ActualPaddingLeft - child.ActualPaddingRight
                  - child.ActualBorderLeftWidth - child.ActualBorderRightWidth;

            child.Width = FormatCssPx(Math.Max(0, cssWidth));
        }

        if (targetOuterHeight is { } outerHeight)
        {
            double borderBoxHeight = Math.Max(0, outerHeight - child.ActualMarginTop - child.ActualMarginBottom);
            double cssHeight = child.UsesBorderBoxSizing
                ? borderBoxHeight
                : borderBoxHeight
                  - child.ActualPaddingTop - child.ActualPaddingBottom
                  - child.ActualBorderTopWidth - child.ActualBorderBottomWidth;

            child.Height = FormatCssPx(Math.Max(0, cssHeight));
        }

        child.PerformLayout(g);

        child.Width = savedWidth;
        child.Height = savedHeight;
    }

    /// <summary>
    /// The column flex container's definite main-axis content height — its specified
    /// <c>height</c> clamped by <c>min-height</c>/<c>max-height</c> — or <c>null</c> when
    /// <c>height</c> is <c>auto</c> and the main size is therefore content-based.
    /// </summary>
    /// <remarks>
    /// <see cref="TryGetDefiniteContentHeight"/> answers a narrower question and cannot be reused:
    /// it drops a zero result, and <c>height: 0</c> under a <c>min-height</c> is precisely the
    /// definite-but-clamped case this exists to catch.
    /// </remarks>
    private double? TryGetDefiniteMainAxisContentHeight()
    {
        if (string.IsNullOrEmpty(Height) || Height == CssConstants.Auto || HeightPercentageResolvesToAuto())
        {
            // §9.2 reads "definite", not "declared": an out-of-flow container with an auto height
            // and both insets set has its main size solved by CSS2.1 §10.6.4, already clamped.
            return TryGetInsetDerivedContentHeight(out double insetHeight) ? insetHeight : null;
        }

        double basis = PercentageHeightContainingBlockHeight();
        double em = GetEmHeight();

        double ToContentHeight(double specified) =>
            ResolveSpecifiedHeightToBorderBox(specified)
            - ActualPaddingTop - ActualPaddingBottom
            - ActualBorderTopWidth - ActualBorderBottomWidth;

        double height = ToContentHeight(CssLengthParser.ParseLength(Height, basis, em));

        if (double.IsNaN(height) || double.IsInfinity(height))
            return null;

        if (!string.IsNullOrEmpty(MinHeight) && MinHeight != CssConstants.Auto)
        {
            double min = ToContentHeight(CssLengthParser.ParseLength(MinHeight, basis, em));
            if (!double.IsNaN(min) && !double.IsInfinity(min) && min > height)
                height = min;
        }

        if (!string.IsNullOrEmpty(MaxHeight) && MaxHeight != CssConstants.Auto
            && !MaxHeight.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            double max = ToContentHeight(CssLengthParser.ParseLength(MaxHeight, basis, em));
            if (!double.IsNaN(max) && !double.IsInfinity(max) && max < height)
                height = max;
        }

        return height > 0 ? height : null;
    }

    private static void LayoutFlexItemAtTargetHeight(ILayoutEnvironment g, CssBox child, double targetOuterHeight)
    {
        double targetBorderBoxHeight = Math.Max(0, targetOuterHeight - child.ActualMarginTop - child.ActualMarginBottom);
        double cssHeight = child.UsesBorderBoxSizing
            ? targetBorderBoxHeight
            : targetBorderBoxHeight
              - child.ActualPaddingTop - child.ActualPaddingBottom
              - child.ActualBorderTopWidth - child.ActualBorderBottomWidth;

        string savedHeight = child.Height;

        child.Height = FormatCssPx(Math.Max(0, cssHeight));
        child.PerformLayout(g);
        child.Height = savedHeight;
    }

    private static void LayoutFlexItemAtTargetWidth(ILayoutEnvironment g, CssBox child, double targetOuterWidth)
    {
        double targetBorderBoxWidth = Math.Max(0, targetOuterWidth - child.ActualMarginLeft - child.ActualMarginRight);
        double cssWidth = child.UsesBorderBoxSizing
            ? targetBorderBoxWidth
            : targetBorderBoxWidth
              - child.ActualPaddingLeft - child.ActualPaddingRight
              - child.ActualBorderLeftWidth - child.ActualBorderRightWidth;

        string savedWidth = child.Width;

        child.Width = FormatCssPx(Math.Max(0, cssWidth));
        child.PerformLayout(g);
        child.Width = savedWidth;
    }

    private static string FormatCssPx(double value) => value.ToString("0.####", CultureInfo.InvariantCulture) + "px";

    private static double ParseFlexFactor(string value, double fallback)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed >= 0)
            return parsed;

        return fallback;
    }

    private double ResolveFlexGap(string value, double percentBase)
    {
        if (string.IsNullOrEmpty(value) || value.Equals("normal", StringComparison.OrdinalIgnoreCase))
            return 0;

        try
        {
            return CssLengthParser.ParseLength(value, percentBase, GetEmHeight());
        }
        catch
        {
            return 0;
        }
    }

    private void ResolveJustifyContent(int itemCount, double freeSpace, double baseGap, out double lineOffset, out double itemGap) =>
        ResolveContentDistribution(NormalizeBoxAlignment(JustifyContent), itemCount, freeSpace, baseGap,
            out lineOffset, out itemGap);

    /// <summary>
    /// CSS Box Alignment 3 §5: turns a content-distribution keyword and the free space on one
    /// axis into the offset before the first subject and the gap between subjects.
    /// </summary>
    /// <remarks>
    /// Shared by <c>justify-content</c> along a flex line's main axis and by
    /// <c>align-content</c> across a multi-line container's lines, which distribute the same way.
    /// <c>stretch</c> is not handled here — it resizes the subjects rather than spacing them, so
    /// each caller applies it to whatever it is distributing.
    /// </remarks>
    private static void ResolveContentDistribution(
        string keyword, int itemCount, double freeSpace, double baseGap, out double lineOffset, out double itemGap)
    {
        lineOffset = 0;
        itemGap = baseGap;

        switch (keyword)
        {
            case "center":
                lineOffset = freeSpace / 2;
                break;

            case "end":
            case "flex-end":
            case "right":
                lineOffset = freeSpace;
                break;

            case "space-between":
                if (itemCount > 1)
                    itemGap = baseGap + freeSpace / (itemCount - 1);
                break;

            case "space-around":
                if (itemCount > 0)
                {
                    itemGap = baseGap + freeSpace / itemCount;
                    lineOffset = (itemGap - baseGap) / 2;
                }
                break;

            case "space-evenly":
                if (itemCount > 0)
                {
                    double extraGap = freeSpace / (itemCount + 1);
                    itemGap = baseGap + extraGap;
                    lineOffset = extraGap;
                }
                break;
        }
    }

    /// <summary>
    /// This box's content-box height when its block size is definite, and <c>null</c> when it is
    /// not — the difference between a flex container that has cross-axis space to hand out and one
    /// that is sized by what it holds.
    /// </summary>
    /// <remarks>
    /// Read from the <c>height</c> declaration rather than from <see cref="CssBoxProperties.Size"/>,
    /// which is content-derived at this point: the specified height is only pre-resolved into
    /// <c>Size</c> for percentage values.
    /// </remarks>
    private double? TryGetDefiniteContentHeight()
    {
        if (string.IsNullOrEmpty(Height) || Height == CssConstants.Auto || HeightPercentageResolvesToAuto())
        {
            // Same §10.6.4 exception the main-axis helper above makes: an inset-sized out-of-flow
            // container has cross-axis space to hand out, so its items stretch rather than
            // collapsing to their own content.
            return TryGetInsetDerivedContentHeight(out double insetHeight) ? insetHeight : null;
        }

        double containingBlockHeight =
            Position == CssConstants.Fixed && LayoutEnvironment != null ? LayoutEnvironment.ViewportSize.Height
            : ContainingBlock?.ParentBox == null && LayoutEnvironment != null ? LayoutEnvironment.ViewportSize.Height
            : ContainingBlock?.Size.Height ?? 0;

        double length = CssLengthParser.ParseLength(Height, containingBlockHeight, GetEmHeight());
        double contentHeight = ResolveSpecifiedHeightToBorderBox(length)
            - ActualPaddingTop - ActualPaddingBottom
            - ActualBorderTopWidth - ActualBorderBottomWidth;

        return contentHeight > 0 ? contentHeight : null;
    }

    /// <summary>
    /// How a flex item is sized across its line, resolving <c>align-self: auto</c> against the
    /// container's <c>align-items</c> the way CSS Box Alignment 3 §6.2 does.
    /// </summary>
    private string ResolveFlexItemAlignment(CssBox child)
    {
        string align = NormalizeBoxAlignment(child.AlignSelf);
        if (align is "" or "auto" or "normal")
            align = NormalizeBoxAlignment(AlignItems);

        // `normal` behaves as `stretch` for a flex item, and it is what an unstyled container has.
        return align is "" or "auto" or "normal" ? "stretch" : align;
    }

    /// <summary>
    /// CSS Flexbox §9.4 step 11: a flex item whose cross size is auto is stretched to fill its
    /// line. This is the initial value of <c>align-items</c>, so it is what happens to most flex
    /// items on most pages, and until now Broiler did none of it — an item was left at the size its
    /// own content gave it, which for an empty one is nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stretching a laid-out box does not reflow it, and does not need to. The cross axis is the
    /// block axis here, so growing the box moves no line and re-breaks no text — the content stays
    /// at the block-start, which is exactly what stretching means.
    /// </para>
    /// <para>
    /// Three things opt an item out. A cross size of its own, obviously. An auto margin on either
    /// cross-axis side, which CSS Flexbox §9.6 gives the free space to instead. And an alignment
    /// that is not <c>stretch</c>, which asks for the item to be placed rather than sized.
    /// </para>
    /// </remarks>
    private void StretchFlexLineItems(FlexLineLayout line)
    {
        foreach (var item in line.Items)
        {
            var child = item.Box;

            if (ResolveFlexItemAlignment(child) != "stretch")
                continue;
            if (!string.IsNullOrEmpty(child.Height) && child.Height != CssConstants.Auto)
                continue;
            if (child.IsSpecifiedMarginTopAuto || child.IsSpecifiedMarginBottomAuto)
                continue;

            double target = line.CrossSize - child.ActualMarginTop - child.ActualMarginBottom;

            // §9.4 step 11 stretches "subject to the item's min/max cross size". Without the clamp
            // an item was stretched straight past its own `max-height`: every `max-height: 10px`
            // image in css-flexbox/image-as-flexitem-size-003 came out 40px tall, the full line.
            double blockEdges = child.ActualBorderTopWidth + child.ActualBorderBottomWidth
                              + child.ActualPaddingTop + child.ActualPaddingBottom;
            target = blockEdges + child
                .ToContentBoxBounds(child.ResolveBlockSizeBounds(), blockEdges)
                .Clamp(target - blockEdges);

            if (target <= 0 || target <= child.Size.Height + 0.5)
                continue;

            child.Size = new SizeF(child.Size.Width, (float)target);
        }
    }

    private double ResolveFlexCrossOffset(CssBox child, double lineCrossSize)
    {
        string align = NormalizeBoxAlignment(child.AlignSelf);
        if (align is "" or "auto" or "normal")
            align = NormalizeBoxAlignment(AlignItems);

        double itemOuterHeight = GetFlexItemOuterHeight(child);
        double freeSpace = lineCrossSize - itemOuterHeight;
        if (freeSpace <= 0)
            return 0;

        return align switch
        {
            "center" => freeSpace / 2,
            "end" or "flex-end" or "self-end" => freeSpace,
            _ => 0
        };
    }

    private static double GetFlexItemOuterHeight(CssBox child) =>
        Math.Max(0, child.ActualBottom - child.Location.Y)
        + child.ActualMarginTop + child.ActualMarginBottom;

    private void ApplyFlexColumnInlineAxisAlignment(ILayoutEnvironment g)
    {
        if (!IsFlexContainer())
            return;

        string direction = FlexDirection?.Trim().ToLowerInvariant() ?? "row";
        if (direction is not ("column" or "column-reverse"))
            return;

        double contentWidth = Math.Max(0, Size.Width
            - ActualBorderLeftWidth - ActualBorderRightWidth
            - ActualPaddingLeft - ActualPaddingRight);

        if (contentWidth <= 0)
            return;

        foreach (var child in Boxes)
        {
            if (!IsInFlowFlexItem(child))
                continue;

            string align = ResolveFlexItemAlignment(child);

            double marginBoxWidth = child.Size.Width + child.ActualMarginLeft + child.ActualMarginRight;
            double freeSpace = contentWidth - marginBoxWidth;

            if (freeSpace <= 0.5)
                continue;

            // CSS Flexbox §9.4 step 11 again, on the axis a column container crosses. A column
            // item reaches here shrink-wrapped — the column path lays items out as inline-blocks —
            // so an empty one is zero wide and paints nothing until it is stretched.
            if (align == "stretch")
            {
                if (!string.IsNullOrEmpty(child.Width) && child.Width != CssConstants.Auto)
                    continue;
                if (child.IsSpecifiedMarginLeftAuto || child.IsSpecifiedMarginRightAuto)
                    continue;

                // Laid out again at the stretched width rather than resized in place. A column
                // item reaches here through the inline path, where its used width has already been
                // written into the line box it sits in and into its own descendants; poking the
                // box's size alone leaves both saying the old one.
                double stretchedOuterWidth = contentWidth;
                double left = child.Location.X;
                double top = child.Location.Y;

                LayoutFlexItemAtTargetWidth(g, child, stretchedOuterWidth);

                double dxBack = left - child.Location.X;
                double dyBack = top - child.Location.Y;
                if (Math.Abs(dxBack) > 0.1)
                    child.OffsetLeft(dxBack);
                if (Math.Abs(dyBack) > 0.1)
                    child.OffsetTop(dyBack);

                continue;
            }

            double offset = align switch
            {
                "center" => freeSpace / 2,
                "end" or "flex-end" or "self-end" or "right" => freeSpace,
                _ => 0
            };

            if (offset <= 0.5)
                continue;

            double targetLeft = ClientLeft + offset + child.ActualMarginLeft;
            double dx = targetLeft - child.Location.X;

            if (Math.Abs(dx) > 0.5)
                child.OffsetLeft(dx);
        }
    }

    /// <summary>
    /// CSS Box Alignment Level 3 §6.2 / CSS Flexbox §8.3: position flex/grid
    /// items along the block (cross) axis according to <c>align-items</c>
    /// (overridable per item by <c>align-self</c>).  Broiler approximates
    /// flex/grid with an inline formatting context (FlowInlineBlock), which
    /// leaves every item at the content-box block-start; this pass shifts
    /// items to center/end when the container has a definite block size with
    /// free space.  Only the common horizontal-writing-mode case is handled
    /// (grid and row/row-reverse flex); column flex (cross axis = inline)
    /// and overflowing content fall back to start alignment.
    /// </summary>
    private void ApplyFlexGridCrossAxisAlignment()
    {
        if (Display is not ("flex" or "inline-flex" or "grid" or "inline-grid"))
            return;

        // The real definite-track grid pass already placed items in their areas
        // (including block-axis alignment); skip the approximate re-alignment.
        if (_gridTrackLayoutApplied)
            return;

        // The cross axis must be the block (vertical) axis: true for grid and
        // for row-direction flex.  Column flex aligns items on the inline
        // axis, which this approximation does not handle.
        if (Display is "flex" or "inline-flex" && FlexDirection is "column" or "column-reverse")
            return;

        // A definite block size is required to have free space to distribute.
        if (TryGetDefiniteContentHeight() is not { } contentHeight)
            return;

        double contentTop = ClientTop;

        string containerAlign = NormalizeBoxAlignment(AlignItems);

        foreach (CssBox item in Boxes)
        {
            if (item.Display == CssConstants.None)
                continue;

            // CSS2.1 §9.6.1 / §9.5: out-of-flow items are positioned separately.
            if (item.Position is CssConstants.Absolute or CssConstants.Fixed)
                continue;

            if (item.Float != CssConstants.None)
                continue;

            // 'align-self: auto' (and 'normal') resolve to the container's
            // 'align-items' (CSS Box Alignment §6.2).
            string self = NormalizeBoxAlignment(item.AlignSelf);
            string align = self is "" or "auto" or "normal" ? containerAlign : self;

            bool toCenter = align == "center";
            bool toEnd = align is "end" or "flex-end" or "self-end";

            if (!toCenter && !toEnd)
                continue; // start/flex-start/baseline/stretch → block-start

            double marginBoxHeight = (item.ActualBottom - item.Location.Y)
                + item.ActualMarginTop + item.ActualMarginBottom;
            double free = contentHeight - marginBoxHeight;

            if (free <= 0.5)
                continue; // safe alignment: no room → keep at start

            double marginBoxOffset = toCenter ? free / 2 : free;
            double targetTop = contentTop + marginBoxOffset + item.ActualMarginTop;
            double dy = targetTop - item.Location.Y;

            if (Math.Abs(dy) > 0.5)
            {
                item.OffsetTop(dy);
                item.ActualBottom += dy;
            }
        }
    }

    /// <summary>
    /// Normalises a CSS Box Alignment keyword: trims, strips a leading
    /// <c>safe</c>/<c>unsafe</c> overflow-alignment qualifier, and lower-cases.
    /// </summary>
    private static string NormalizeBoxAlignment(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        string v = value.Trim();

        if (v.StartsWith("safe ", StringComparison.OrdinalIgnoreCase))
            v = v[5..].Trim();
        else if (v.StartsWith("unsafe ", StringComparison.OrdinalIgnoreCase))
            v = v[7..].Trim();

        return v.ToLowerInvariant();
    }
}
