using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using System.Drawing;
using System.Globalization;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// CSS2.1 §9.7: a float has its computed <c>display</c> blockified, exactly as an out-of-flow box
    /// does. Only the out-of-flow half of that rule was implemented, and this is the part of the
    /// missing half that is a rendering defect rather than a refinement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A floated <em>replaced</em> element — an <c>&lt;img&gt;</c>, a <c>&lt;canvas&gt;</c> — stayed
    /// inline-level and took <see cref="PerformLayoutImp"/>'s else-branch, which resolves no size of
    /// its own: an inline box is sized from its words, and a replaced box has none. It laid out 0×0
    /// at (0, 0) and painted nothing whatsoever — no image, no border, no background — and occupied
    /// no layout space either, so any floated image on a page simply vanished. Measured before the
    /// fix: an <c>&lt;img&gt;</c> that paints 4 096 pixels unfloated painted zero anywhere on the
    /// canvas with <c>float: left</c>. Found on WPT <c>css-images/object-fit-contain-svg-001i</c>,
    /// which floats every one of its images and so rendered a blank page.
    /// </para>
    /// <para>
    /// Narrowed to replaced boxes deliberately. A floated non-replaced inline is sized from its words
    /// today and does render, so blockifying <em>every</em> float is a wider behavioural change than
    /// this defect needs. §9.7 asks for that too and it is worth doing — on its own evidence and its
    /// own sweep, not smuggled in here.
    /// </para>
    /// </remarks>
    internal bool IsBlockifiedFloatedReplaced =>
        Float != CssConstants.None && (IsImage || IntrinsicReplacedSize is { Width: > 0, Height: > 0 });

    /// <summary>
    /// Whether a concrete <c>justify-self</c> alignment (one that actually shifts
    /// the box) is in effect, after resolving <c>auto</c> to the parent's
    /// <c>justify-items</c> and the legacy <c>text-align:-webkit-*</c> fallback.
    /// Mirrors the resolution in <see cref="PerformLayoutImp"/>'s block
    /// justify-self step; used to avoid double-applying alignment with the
    /// CSS2.1 §10.3.3 over-constrained-margin positioning.
    /// </summary>
    private bool HasConcreteJustifySelf()
    {
        string js = JustifySelf?.Trim().ToLowerInvariant() ?? "auto";
        if (js == "auto")
            js = ParentBox?.JustifyItems?.Trim().ToLowerInvariant() ?? "normal";
        if (js is "normal" or "stretch" or "auto" or "legacy")
        {
            js = (ParentBox?.TextAlign?.Trim().ToLowerInvariant()) switch
            {
                "-webkit-right" => "right",
                "-webkit-center" => "center",
                "-webkit-left" => "left",
                _ => js,
            };
        }
        return js is "center" or "end" or "flex-end" or "self-end" or "right"
            or "start" or "flex-start" or "self-start" or "left";
    }

    /// <summary>
    /// CSS Overflow 3 §3.3 (overflow viewport propagation): when the root element's <c>overflow</c>
    /// is <c>visible</c>, the <c>&lt;body&gt;</c>'s is applied to the <b>viewport</b> instead, and
    /// the body's own used value becomes <c>visible</c>. The root element's own non-<c>visible</c>
    /// value is propagated the same way and the body's is then left alone.
    /// </summary>
    /// <remarks>
    /// Broiler had no propagation at all, so <c>body { overflow: hidden }</c> — one of the most
    /// common declarations there is — clipped the body's own box instead of the viewport. WPT
    /// <c>css-overflow/overflow-body-propagation-009</c> is the sharp version: a 30×30 body clipped
    /// a 10 000px child down to 0.3 % of the canvas where the reference fills 82 %. Idempotent, so
    /// running it on every layout pass is safe: once the body reads <c>visible</c> there is nothing
    /// left to move.
    /// </remarks>
    internal void ApplyViewportOverflowPropagation()
    {
        if (HtmlTag == null || !HtmlTag.Name.Equals("body", StringComparison.OrdinalIgnoreCase))
            return;

        if (ParentBox is not { HtmlTag: not null } root
            || !root.HtmlTag.Name.Equals("html", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrEmpty(Overflow) || Overflow == CssConstants.Visible)
            return;

        // The root's own value wins and is what the viewport already uses; the body's is then not
        // propagated, and (per §3.3) it keeps it.
        if (!string.IsNullOrEmpty(root.Overflow) && root.Overflow != CssConstants.Visible)
            return;

        // Only the *first* <body> propagates, and only if it generates a box. A document with two
        // of them (WPT css-overflow/overflow-body-propagation-016) must leave the second one's
        // `overflow: hidden` clipping its own contents — and if the first generates no box, nothing
        // propagates at all rather than the turn passing to the second.
        foreach (var child in root.Boxes)
        {
            if (child.HtmlTag == null
                || !child.HtmlTag.Name.Equals("body", StringComparison.OrdinalIgnoreCase))
                continue;

            if (child != this || child.Display == CssConstants.None)
                return;

            break;
        }

        // The value goes to the *viewport*, not to the root element's box: Broiler's canvas already
        // clips there, so the propagation is exactly the removal of the body's own clip. Putting it
        // on the root box instead would clip at the root's border box, which is not the same
        // rectangle once the body has margins.
        Overflow = CssConstants.Visible;
    }

    protected virtual void PerformLayoutImp(ILayoutEnvironment g)
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.BoxesLaidOut);

        ResetCollapsedMarginState();

        ApplyViewportOverflowPropagation();

        if (Display != CssConstants.None)
        {
            RectanglesReset();
            MeasureWordsSize(g);
        }

        // CI fallback for the Broiler.HTML submodule <br> patch
        // (patches/0002-broiler-html-br-after-inline-block.patch): DomParser
        // gives a <br> a ".95em" empty-line height when it "follows a block".
        // An atomic inline-block carries no text words, so it is misclassified
        // as block-level and a <br> after it spuriously inserts a full empty
        // line, pushing every following block sibling ~1em down.  Such a <br>
        // merely ends the inline-block's line, so drop its empty-line height.
        // The previous in-flow sibling (an anonymous block wrapping the
        // inline-block, or the inline-block itself) is already laid out by the
        // time this block runs.  Harmless once the submodule patch lands (the
        // <br> then carries no .95em height to drop).
        if (IsBrElement && !string.IsNullOrEmpty(Height) && Height != CssConstants.Auto
            && CssLayoutEngine.EndsWithAtomicInlineBlock(LayoutBoxUtils.GetPreviousSibling(this)))
        {
            Height = CssConstants.Auto;
        }

        // CSS Box Model 4 §6.2: margin-trim zeroes the block-axis margins of
        // this container's first/last in-flow block-level children before they
        // are laid out, so the trimmed margins collapse to nothing.
        ApplyMarginTrim();

        // CSS2.1 §9.7: an out-of-flow (absolutely/fixed positioned) box has its
        // computed 'display' blockified — an inline-level abspos element (e.g. a
        // positioned <a> or <span>) is laid out as a block. Route it through the
        // block path so it resolves its own used width (shrink-to-fit per §10.3.7)
        // and its inset position (ComputeStaticAndFloatPosition), rather than the
        // inline else-branch which would leave its Size/Location uncomputed and let
        // it report the static line-box rectangle instead of its inset box.
        bool isOutOfFlow = Position == CssConstants.Absolute || Position == CssConstants.Fixed;

        if (IsBlock || isOutOfFlow || IsBlockifiedFloatedReplaced || Display == CssConstants.ListItem || Display == CssConstants.Table || Display == CssConstants.InlineTable || Display == CssConstants.TableCell || Display == CssConstants.TableCaption)
        {
            // Because their width and height are set by CssTable.
            //
            // Except when the table is out of flow: CSS2.1 §10.3.5/§10.3.7 shrink-to-fit
            // a floated or absolutely positioned table just like any other such box, and
            // ComputeStaticAndFloatPosition below needs that width *before* it places the
            // box — a right float is positioned at (container right − width). Skipping the
            // resolution left Size.Width at zero there, so a `float: right` table was
            // placed hard against the right edge and then grew off the viewport: the
            // MediaWiki thumbnail (`figure[typeof~='mw:File/Thumb']`, which the skin makes
            // `display: table; float: right`) vanished entirely, taking its reserved space
            // with it so the article text reflowed across the full column. The table
            // algorithm still sizes the columns afterwards, exactly as it already does for
            // `display: inline-table`, which reaches this branch and has always been right.
            bool widthComesFromTableAlgorithm = Display == CssConstants.Table
                && Float == CssConstants.None
                && !isOutOfFlow;

            if (Display != CssConstants.TableCell && !widthComesFromTableAlgorithm)
            {
                ResolveBlockUsedWidth(g);
            }

            if (Display != CssConstants.TableCell)
            {
                ComputeStaticAndFloatPosition();
            }

            double widthAtPlacement = Size.Width;

            PreResolveDefiniteHeightForDescendants();
            LayoutBlockChildren(g);

            RealignRightFloatAfterContentSizing(widthAtPlacement);
        }
        else
        {
            var prevSibling = LayoutBoxUtils.GetPreviousSibling(this);
            if (prevSibling != null)
            {
                if (Location == PointF.Empty)
                    Location = prevSibling.Location;

                ActualBottom = prevSibling.ActualBottom;
            }
        }

        // CSS Overflow 4 §5: the clamp runs once the children are laid out (the
        // line count it cuts on exists only then) and before the height is
        // resolved, so the container is sized to the lines it kept.
        ApplyLineClamp(g);

        // HTML §15.5.13: a fieldset's rendered legend belongs to the block-start border, not to the
        // content. Before the column pass and the height, so both see the flow the legend leaves.
        ApplyFieldsetLegendPlacement();

        ApplyMultiColumnPostLayout();
        ResolveUsedBlockHeight();
        ApplyMinMaxHeightConstraints();
        ApplyFloatExplicitHeight();
        PositionAbsoluteBox();
        ApplyBlockAlignContent();
        ApplyBlockJustifySelf();
        ApplyRelativePositionOffset();
        CreateListItemBox(g);

        if (!IsFixed)
        {
            var actualWidth = Math.Max(GetMinimumWidth() + CssBoxHelper.GetWidthMarginDeep(this), Size.Width < 90999 ? ActualRight - LayoutEnvironment.RootLocation.X : 0);
            LayoutEnvironment.ActualSize = CommonUtils.Max(LayoutEnvironment.ActualSize, new SizeF((float)actualWidth, (float)(ActualBottom - LayoutEnvironment.RootLocation.Y)));
        }
    }


    /// <summary>
    /// Resolve the used inline size (width) of this block-level box: explicit /
    /// intrinsic-keyword/ shrink-to-fit (abspos, float, orthogonal) widths, the
    /// min/max-widthclamps, and auto-margin centering. Sets Size.Width.
    /// </summary>
    private void ResolveBlockUsedWidth(ILayoutEnvironment g)
    {
        // CSS2.1 §9.6.1: The containing block for a fixed-position
        // element is the viewport (initial containing block).
        // CSS2.1 §10.1: For absolutely positioned elements, the
        // containing block is the padding-box of the nearest
        // positioned ancestor.
        // Use the viewport width for percentage/auto resolution.
        double width;

        if (Position == CssConstants.Fixed && LayoutEnvironment != null)
        {
            width = FixedPositioningViewport().Width;
        }
        else if (Position == CssConstants.Absolute)
        {
            var cb = FindPositionedContainingBlock();
            GetAbsoluteContainingBlockPaddingBox(cb, out _, out _, out width, out _);
        }
        else
        {
            width = ContainingBlock.Size.Width
                    - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                    - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth;
        }

        // CSS2.1 §10.3.4/§10.3.8: a block-level or out-of-flow *replaced* element resolves its width
        // with the inline rules (§10.3.2) — an auto width is its natural width, not the containing
        // block's, and for an absolutely positioned one it is not the inset constraint equation
        // either (`right` is what gives way). The min/max pass inside settles both axes together,
        // and ResolveUsedBlockHeight reads the block one back.
        if (TryResolveReplacedBorderBoxSize(width, out double replacedWidth, out _))
        {
            width = replacedWidth;
        }
        else if (IsIntrinsicWidthKeyword(Width) && Float == CssConstants.None)
        {
            // CSS Sizing 3 §5: width resolves to an intrinsic size
            // (min-content / max-content / fit-content). This also applies to an
            // out-of-flow (absolutely/fixed positioned) box: an intrinsic-keyword width
            // shrink-wraps to content rather than filling the inset-modified containing
            // block, so a `dialog:modal { inset:0; width:fit-content; margin:auto }` box
            // sizes to its content and the auto margins then centre it (§10.3.7). For such a
            // box with both opposing insets, fit-content clamps to the space between the
            // insets (the inset-modified containing block), not the full containing block.
            double availableForIntrinsic = width;
            if ((Position == CssConstants.Absolute || Position == CssConstants.Fixed)
                && Left != null && Left != CssConstants.Auto
                && Right != null && Right != CssConstants.Auto)
            {
                double insetLeft = ParseUsedLength(Left, width);
                double insetRight = ParseUsedLength(Right, width);
                availableForIntrinsic = Math.Max(0, width - insetLeft - insetRight);
            }

            width = ResolveIntrinsicWidth(g, Width, availableForIntrinsic);
        }
        else if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width) && !IsIntrinsicWidthKeyword(Width))
        {
            double containingWidth = width;

            width = string.Equals(Width, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                ? GetParent().ActualWidth
                : ParseLengthWithLineHeight(Width, containingWidth, percentAgainstContainingBlock: true);

            // CSS2.1 §10.4: Apply max-width constraint
            if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
            {
                double maxW = ResolveMaxWidthLength(containingWidth);

                if (width > maxW)
                    width = maxW;
            }

            // CSS2.1 §10.4: Apply min-width constraint (min wins over max per §10.4)
            if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
            {
                double minW = ResolveMinWidthLength(containingWidth);
                if (width < minW) width = minW;
            }

            width = ResolveSpecifiedWidthToBorderBox(width);
        }
        else if ((Position == CssConstants.Absolute || Position == CssConstants.Fixed)
            && Left != null && Left != CssConstants.Auto
            && Right != null && Right != CssConstants.Auto)
        {
            // CSS2.1 §10.3.7: For absolutely positioned, non-replaced
            // elements when width is auto and both left and right are
            // specified, compute width from the constraint equation:
            // left + margin-left + width + margin-right + right = CB width
            double cbContentWidth = width;

            if (Position == CssConstants.Fixed && LayoutEnvironment != null)
                cbContentWidth = FixedPositioningViewport().Width;

            double cssLeft = ParseUsedLength(Left, cbContentWidth);
            double cssRight = ParseUsedLength(Right, cbContentWidth);

            width = cbContentWidth - cssLeft - cssRight - ActualMarginLeft - ActualMarginRight;

            if (width < 0)
                width = 0;

            width = ResolveSpecifiedWidthToBorderBox(width);
        }

        // CSS2.1 §10.4: Apply max-width constraint even when
        // Width is auto — the tentative used width must not exceed
        // max-width. A replaced box has already had both bounds applied to both axes together
        // above, and re-clamping here would use a different percentage basis.
        bool replacedSizeSettled = TryGetNaturalReplacedSize(out _);

        if (!replacedSizeSettled && MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
        {
            double maxW = ResolveMaxWidthLength(width);
            maxW = ResolveSpecifiedWidthToBorderBox(maxW);
            if (width > maxW) width = maxW;
        }

        // CSS2.1 §10.4: Apply min-width constraint (min wins over
        // max per §10.4) — also when Width is auto.
        if (!replacedSizeSettled && MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
        {
            double minW = ResolveMinWidthLength(width);

            minW = ResolveSpecifiedWidthToBorderBox(minW);

            if (width < minW)
                width = minW;
        }

        // CSS Sizing 4 §4: a box with a preferred aspect ratio and a definite block size takes
        // its auto inline size from the ratio instead of the containing block. The transfer
        // supersedes every auto-width rule below — the block-level stretch-fit, the §10.3.7
        // inset equation, and the float/abspos shrink-to-fit — because each of those answers
        // "how wide is a box with no width of its own?", and a ratio plus a definite height is
        // exactly such a width. Without it `height: 100px; aspect-ratio: 1/1` painted a
        // viewport-wide band where every engine paints a 100px square.
        bool inlineSizeFromAspectRatio = false;

        if (!replacedSizeSettled && TryResolveAspectRatioAutoInlineWidth(out double aspectRatioInlineWidth))
        {
            inlineSizeFromAspectRatio = true;
            width = aspectRatioInlineWidth;
        }

        Size = new SizeF((float)width, Size.Height);

        // CSS2.1 §10.3.3: For block-level, non-replaced elements in
        // normal flow with an explicit width and auto margins, resolve
        // the auto margins so the element is centered horizontally.
        // A ratio-derived width counts as a resolved one here: it leaves the same free space
        // for `margin: 0 auto` to split, and CSS Sizing 4 §4 does not exempt it.
        if ((inlineSizeFromAspectRatio || (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width)))
            && Float == CssConstants.None
            && Position != CssConstants.Absolute && Position != CssConstants.Fixed)
        {
            double containingContentWidth = ContainingBlock.Size.Width
                - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth;
            double remainingSpace = containingContentWidth - Size.Width;

            if (MarginLeft == CssConstants.Auto && MarginRight == CssConstants.Auto)
            {
                if (remainingSpace >= 0)
                {
                    string halfMargin = (remainingSpace / 2).ToString("F4", CultureInfo.InvariantCulture) + "px";

                    MarginLeft = halfMargin;
                    MarginRight = halfMargin;
                }
                else
                {
                    MarginLeft = "0";
                    MarginRight = "0";
                }
            }
            else if (MarginLeft == CssConstants.Auto)
            {
                double rightMargin = ActualMarginRight;
                double leftMargin = Math.Max(0, remainingSpace - rightMargin);

                MarginLeft = leftMargin.ToString("F4", CultureInfo.InvariantCulture) + "px";

            }

            else if (MarginRight == CssConstants.Auto)
            {
                double leftMargin = ActualMarginLeft;
                double rightMargin = Math.Max(0, remainingSpace - leftMargin);

                MarginRight = rightMargin.ToString("F4", CultureInfo.InvariantCulture) + "px";
            }
            else if ((IsBlock || Display == CssConstants.ListItem) && remainingSpace >= 0
                     && ContainingBlock?.Position != CssConstants.Absolute
                     && ContainingBlock?.Position != CssConstants.Fixed
                     && !IsVerticalWritingMode(ContainingBlock?.WritingMode ?? WritingMode)
                     && (ContainingBlock?.Direction ?? Direction) == "rtl"
                     && !HasConcreteJustifySelf())
            {
                // CSS2.1 §10.3.3: when width and both margins are
                // specified the box is over-constrained, so one used
                // margin is ignored and solved for. In a left-to-right
                // containing block that is margin-right (and the box
                // stays at its margin-left, which the X computation
                // already honours, so no adjustment is needed). In a
                // right-to-left containing block margin-LEFT is the one
                // ignored, so recompute it from the remaining space —
                // this positions the box against the right edge instead
                // of the left (e.g. a fixed-width block in a dir=rtl
                // container; WPT css-anchor-position/anchor-position-borders).
                // Skipped when a concrete justify-self alignment applies,
                // because that is resolved later (see ApplyBlockJustifySelf)
                // and would otherwise be double-applied.
                double leftMargin = remainingSpace - ActualMarginRight;

                MarginLeft = leftMargin.ToString("F4", CultureInfo.InvariantCulture) + "px";
            }
        }

        if (inlineSizeFromAspectRatio)
        {
            // The ratio already settled this box's inline size, and every branch of this chain
            // is an answer to the question it just answered — each would overwrite the derived
            // width with the containing block's or with the content's.
        }
        // CSS2.1 §10.3.7: Absolutely positioned non-replaced elements
        // with auto width use shrink-to-fit when at least one of
        // left/right is auto.  Shrink-to-fit =
        //   min(max(preferred_minimum, available), preferred)
        //
        // Non-replaced is the operative word, and it was not being honoured: a replaced box's auto
        // width is its natural width (§10.3.8), and shrink-to-fit measures *children*, of which an
        // <img> or <canvas> has none — so an `<img position:absolute; left:4em; top:4em>` came out
        // zero pixels wide and painted nothing at all. With `right` also set the branch was skipped
        // and the same image stretched across the inset box instead; both are now the natural size.
        else if (!replacedSizeSettled
            && (Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
            && (Position == CssConstants.Absolute || Position == CssConstants.Fixed)
            && (Left == null || Left == CssConstants.Auto
             || Right == null || Right == CssConstants.Auto))
        {
            // Ensure descendant word sizes (and ActualWordSpacing) are
            // measured before computing intrinsic min/max widths.
            // Without this, word.FullWidth may be NaN because
            // ActualWordSpacing defaults to NaN until MeasureWordSpacing
            // runs, causing the entire shrink-to-fit result to be NaN.
            EnsureDescendantWordsMeasured(g);

            // Compute preferred width by independently measuring each
            // direct child and taking the maximum.  This correctly
            // treats each block/float child as its own "line" and avoids
            // the additive accumulation in GetMinMaxSumWords where a
            // float's width would incorrectly sum with a preceding
            // block child's width.
            double preferred = ComputeShrinkToFitWidth();
            double available = width - ActualMarginLeft - ActualMarginRight;

            GetMinMaxWidth(out double prefMin, out _);

            // Guard against NaN from unmeasured descendants
            if (double.IsNaN(prefMin))
                prefMin = 0;

            if (double.IsNaN(preferred))
                preferred = 0;

            double stfWidth = Math.Min(Math.Max(prefMin, available), preferred);

            if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
            {
                double maxW = ResolveMaxWidthLength(width);

                if (stfWidth > maxW)
                    stfWidth = maxW;
            }

            if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
            {
                double minW = ResolveMinWidthLength(width);

                if (stfWidth < minW)
                    stfWidth = minW;
            }

            // CSS2.1 §10.3.7: Shrink-to-fit gives the content
            // width; add own borders and padding for the border-box
            // width that Size.Width represents.
            stfWidth += ActualBorderLeftWidth + ActualBorderRightWidth
                      + ActualPaddingLeft + ActualPaddingRight;

            Size = new SizeF((float)stfWidth, Size.Height);
        }
        else if (!replacedSizeSettled
            && (Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
            && Float != CssConstants.None)
        {
            // CSS2.1 §10.3.5: Floating non-replaced elements with
            // 'width: auto' use shrink-to-fit width.
            //
            // Non-replaced is the operative word here as much as it is in the §10.3.7 branch above:
            // §10.3.6 gives a *floating replaced* box its natural width, and shrink-to-fit measures
            // children, of which an <img> or a <canvas> has none. Without the guard a
            // `float: left` image with no stated width settled at zero — its border box collapsed,
            // so it reserved no space for text to flow around and drew no border or background,
            // even though its word still carried the natural size. ResolveBlockUsedWidth had
            // already resolved the right answer a few lines up and this branch overwrote it.
            EnsureDescendantWordsMeasured(g);

            double preferred = ComputeShrinkToFitWidth();
            double available = width - ActualMarginLeft - ActualMarginRight;

            GetMinMaxWidth(out double prefMin, out _);

            if (double.IsNaN(prefMin))
                prefMin = 0;

            if (double.IsNaN(preferred))
                preferred = 0;

            double stfWidth = Math.Min(Math.Max(prefMin, available), preferred);

            if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
            {
                double maxW = ResolveMaxWidthLength(width);

                if (stfWidth > maxW)
                    stfWidth = maxW;
            }

            if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
            {
                double minW = ResolveMinWidthLength(width);

                if (stfWidth < minW)
                    stfWidth = minW;
            }

            stfWidth += ActualBorderLeftWidth + ActualBorderRightWidth
                      + ActualPaddingLeft + ActualPaddingRight;

            Size = new SizeF((float)stfWidth, Size.Height);
        }
        else if (!replacedSizeSettled
            && (Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
            && IsRenderedLegend)
        {
            // HTML §15.5.13: "If the computed value of 'inline-size' is 'auto', then the used
            // value is the fit-content inline size." A rendered legend is blockified, but unlike
            // an ordinary block child it does not stretch to the fieldset's content width — it
            // shrink-wraps, and the fieldset's block-start border is then painted around it.
            // Without this the legend filled the fieldset, so WPT's
            // `the-fieldset-and-legend-elements/legend-block-position-centering` drew a
            // fieldset-wide legend border where every engine draws a content-wide one.
            EnsureDescendantWordsMeasured(g);

            double ownPadBorder = ActualBorderLeftWidth + ActualBorderRightWidth
                                + ActualPaddingLeft + ActualPaddingRight;

            // Same framing as the intrinsic-keyword branch below: ComputeShrinkToFitWidth is a
            // content-box width and GetMinMaxWidth a border-box one, so strip this box's own
            // padding and border off the min side before combining the two.
            double maxContent = ComputeShrinkToFitWidth();

            GetMinMaxWidth(out double legendMinBorderBox, out _);

            if (double.IsNaN(legendMinBorderBox))
                legendMinBorderBox = 0;

            if (double.IsNaN(maxContent))
                maxContent = 0;

            double legendMinContent = Math.Max(0, legendMinBorderBox - ownPadBorder);
            double legendAvailable = Math.Max(0, width - ActualMarginLeft - ActualMarginRight - ownPadBorder);

            double legendWidth = Math.Min(Math.Max(legendMinContent, legendAvailable), maxContent);

            if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
            {
                double maxW = ResolveMaxWidthLength(width);

                if (legendWidth > maxW)
                    legendWidth = maxW;
            }

            if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
            {
                double minW = ResolveMinWidthLength(width);

                if (legendWidth < minW)
                    legendWidth = minW;
            }

            legendWidth += ownPadBorder;
            Size = new SizeF((float)legendWidth, Size.Height);
        }
        else if ((Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
            && Float == CssConstants.None
            && Position != CssConstants.Absolute && Position != CssConstants.Fixed
            && VerticalFlowPrototype.Enabled
            && IsVerticalWritingMode(WritingMode)
            && (ParentBox == null || !IsVerticalWritingMode(ParentBox.WritingMode))
            && ContainingBlock is { ParentBox: not null } orthoCb
            && (string.IsNullOrEmpty(orthoCb.Height) || orthoCb.Height == CssConstants.Auto))
        {
            // CSS Writing Modes 4 §7.3 (auto-sizing in orthogonal flows):
            // a box establishing an orthogonal flow — here a vertical
            // writing-mode box inside a non-vertical containing block — with
            // an auto inline size is sized to fit-content, NOT stretched to
            // the containing block's (perpendicular) inline size. In the
            // vertical-flow prototype this box is laid out in a logical
            // horizontal frame where its logical width IS its inline size, so
            // compute that width as shrink-to-fit; the post-layout rotation
            // (ApplyVerticalWritingModeFlow) then maps it onto physical height.
            // Gated on an indefinite containing-block block size (an auto-height
            // in-flow ancestor) so a definite orthogonal size — a root box
            // filling the viewport, or an explicit-height container — keeps the
            // existing fill behaviour. Without this, an empty (or short)
            // vertical box fills the container width and rotates into a
            // viewport-tall strip instead of collapsing to its content
            // (WPT css-grid/grid-lanes row-subgrid-auto-fill-007).
            EnsureDescendantWordsMeasured(g);

            double preferred = ComputeShrinkToFitWidth();

            // Indefinite orthogonal available inline size falls back to the
            // initial containing block (viewport) block size — here the
            // viewport height, the vertical inline axis's extent.
            double available = LayoutEnvironment?.ViewportSize.Height ?? width;

            GetMinMaxWidth(out double prefMin, out _);

            if (double.IsNaN(prefMin))
                prefMin = 0;

            if (double.IsNaN(preferred))
                preferred = 0;

            double stfWidth = Math.Min(Math.Max(prefMin, available), preferred);

            // Border/padding added for the border-box width Size.Width holds
            // (shrink-to-fit yields a content width). max-width/min-width are
            // physical-width (block-size) constraints and do not clamp the
            // inline size resolved here.
            stfWidth += ActualBorderLeftWidth + ActualBorderRightWidth
                      + ActualPaddingLeft + ActualPaddingRight;

            Size = new SizeF((float)stfWidth, Size.Height);
        }
        else if (IsIntrinsicSizingWidthKeyword(Width))
        {
            // CSS Sizing 3 §5.1: an intrinsic-sizing keyword width resolves
            // to the box's content-based size, not the containing block.
            //   min-content → the min-content (preferred-minimum) width,
            //   max-content → the max-content (preferred) width,
            //   fit-content → min(max(min-content, available), max-content).
            // Without this these keywords fell through to the stretched
            // container width (e.g. a shrink-to-fit grid stayed 1024 instead
            // of its min-width — WPT css-grid grid-auto-repeat-min-size-001).
            // Mirrors the float shrink-to-fit path (content widths + own
            // border/padding for the border-box Size.Width, then min/max-width).
            EnsureDescendantWordsMeasured(g);

            double ownPadBorder = ActualBorderLeftWidth + ActualBorderRightWidth
                                + ActualPaddingLeft + ActualPaddingRight;

            // Both contributions must be in the same frame: ComputeShrinkToFitWidth
            // returns a content-box width, but GetMinMaxWidth returns a border-box
            // one, so strip this box's own padding/border off the min side before
            // combining — otherwise fit-content double-counts it.

            double maxContent = ComputeShrinkToFitWidth();

            GetMinMaxWidth(out double minContentBorderBox, out _);

            if (double.IsNaN(minContentBorderBox))
                minContentBorderBox = 0;

            if (double.IsNaN(maxContent))
                maxContent = 0;

            double minContent = Math.Max(0, minContentBorderBox - ownPadBorder);
            double available = width - ActualMarginLeft - ActualMarginRight;

            double resolved = Width.StartsWith("min-content", StringComparison.OrdinalIgnoreCase)
                ? minContent
                : Width.StartsWith("max-content", StringComparison.OrdinalIgnoreCase)
                    ? maxContent
                    : Math.Min(Math.Max(minContent, available), maxContent); // fit-content

            if (MaxWidth != "none" && !string.IsNullOrEmpty(MaxWidth))
            {
                double maxW = ResolveMaxWidthLength(width);

                if (resolved > maxW)
                    resolved = maxW;
            }

            if (MinWidth != "0" && !string.IsNullOrEmpty(MinWidth))
            {
                double minW = ResolveMinWidthLength(width);

                if (resolved < minW)
                    resolved = minW;
            }

            resolved += ownPadBorder;
            Size = new SizeF((float)resolved, Size.Height);
        }
        else if (Width == CssConstants.Auto || string.IsNullOrEmpty(Width))
        {
            // Margins reduce the box width only for auto-width elements.
            // For explicit widths, margins affect position only (CSS1 box model).
            Size = new SizeF((float)(width - ActualMarginLeft - ActualMarginRight), Size.Height);
        }
    }

    /// <summary>
    /// Keeps a right float's <em>right</em> edge where placement put it when laying out its
    /// contents changed its width (CSS2.1 §9.5.1 rule 3: the right outer edge is what a right
    /// float aligns).
    /// </summary>
    /// <remarks>
    /// A right float is positioned as <c>container right − width</c>, so its left edge is derived
    /// and its width has to be known first. For a table that width is only provisional at
    /// placement time: the shrink-to-fit measurement estimates it from the box's content, and the
    /// table algorithm then sizes the columns and settles on the real one. The difference is pure
    /// displacement — the MediaWiki thumbnail measured 502px wide, was placed at
    /// <c>1001 − 502</c>, and then became the 328px it really is, leaving it 174px to the left of
    /// the margin it was supposed to sit against.
    /// <para>
    /// Shifting is the whole correction: the contents were laid out relative to this box, so
    /// moving box and subtree together by the same delta leaves them consistent, and nothing that
    /// has already consulted this float can be disturbed by it — a float's own placement only ever
    /// reads floats <em>earlier</em> in the formatting context.
    /// </para>
    /// </remarks>
    private void RealignRightFloatAfterContentSizing(double widthAtPlacement)
    {
        if (Float != CssConstants.Right || ContainingBlock == null)
            return;

        double delta = widthAtPlacement - Size.Width;

        if (Math.Abs(delta) <= 0.1)
            return;

        // Only reclaim space the box gave back. A box that grew past its placement would have to
        // re-run collision resolution against the floats beside it, which is placement's job and
        // not a shift's.
        if (delta < 0)
            return;

        OffsetLeft(delta);
    }

    /// <summary>
    /// CSS2.1 §9.5.1 rules 3/5: when a float will not fit beside the floats already placed,
    /// it moves down until it does. Returns the border-box top it must move to, or
    /// <paramref name="top"/> when nothing overlaps it.
    /// </summary>
    /// <remarks>
    /// The rules are stated on <em>outer</em> (margin) edges, so the move is computed there and
    /// converted back: the moved float's outer top lands on the lowest overlapping float's outer
    /// bottom, which puts its border-box top a further <c>margin-top</c> down. Comparing
    /// border-box edges instead swallowed the moved float's own top margin — Acid1's second row
    /// of floats (<c>#baz</c>, the <c>blockquote</c> and the <c>h1</c>, each with
    /// <c>margin-top: 1em</c>) all sat 10px too high.
    /// </remarks>
    private double MoveBelowOverlappingFloats(IReadOnlyList<CssBox> precedingFloats, double top, double floatHeight)
    {
        double outerTop = top - ActualMarginTop;
        double maxOuterBottom = outerTop;

        foreach (var floatBox in precedingFloats)
        {
            double fBottom = floatBox.ActualBottom;

            if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                maxOuterBottom = Math.Max(maxOuterBottom, fBottom + floatBox.ActualMarginBottom);
        }

        // No overlap: report the unchanged border-box top so the caller's `<= top` guard
        // terminates the collision loop.
        return maxOuterBottom <= outerTop ? top : maxOuterBottom + ActualMarginTop;
    }

    /// <summary>
    /// Compute this box's in-flow static position, then apply float placement
    /// (CSS2.1§9.5 collision resolution), clearance, BFC float avoidance, and
    /// theabsolute/fixed offset overrides. Sets Location / ActualBottom.
    /// </summary>
    private void ComputeStaticAndFloatPosition()
    {
        var prevSibling = LayoutBoxUtils.GetPreviousSibling(this);

        // Compute the static position for all elements (including
        // position:fixed).  Fixed elements need the static position
        // as fallback when offset properties are auto (CSS2.1 §10.6.4).
        {
            double left = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ActualMarginLeft + ContainingBlock.ActualBorderLeftWidth;

            // CSS2.1 §9.5: floats are out of normal flow. Non-floated
            // blocks must be positioned as if preceding floats do not
            // exist.  For cleared elements this also prevents margin
            // collapsing with the float (CSS2.1 §8.3.1).

            var flowPrev = prevSibling;

            if (Float == CssConstants.None && flowPrev != null && flowPrev.Float != CssConstants.None)
            {
                flowPrev = LayoutBoxUtils.GetPreviousInFlowSibling(flowPrev);
            }

            // CSS2.1 §9.4.3: Relative positioning is visual-only.
            // Use the flow-position bottom (before relative offset)
            // when computing the next sibling's position.
            double flowPrevBottom = flowPrev?.ActualBottom ?? 0;

            if (flowPrev is CssBox flowPrevBox && flowPrevBox.Position == CssConstants.Relative)
                flowPrevBottom -= CssBoxHelper.GetRelativeOffsetY(flowPrevBox);

            // CSS2.1 §8.3.1: MarginTopCollapse may propagate margins
            // and update the parent's Location, so compute it before
            // reading ParentBox.ClientTop.
            double marginCollapse = MarginTopCollapse(flowPrev);

            // The static top is the parent's content top plus the in-flow advance
            // past any preceding sibling. A block preceding sibling records an
            // absolute ActualBottom, so its advance is (flowPrevBottom - baseTop);
            // an inline/text preceding sibling records no block bottom
            // (flowPrevBottom == 0), which must NOT drag the box above the parent's
            // content top — clamp the advance to ≥ 0. This keeps a block-after-block
            // static position byte-identical while fixing an abspos box that follows
            // inline content in a nested block (e.g. `<div>text<div
            // style="position:absolute"></div></div>`), which previously resolved to
            // the containing block's top (y = 0) instead of its parent's content top.
            double baseTop = ParentBox == null ? Location.Y : ParentBox.ClientTop;
            double top = baseTop + marginCollapse
                + (flowPrev != null ? Math.Max(0, flowPrevBottom - baseTop) : 0);

            // CSS2.1 §10.3.7 / §10.6.4: an out-of-flow box that was flowed
            // through an inline formatting context takes its *static* position
            // from the inline cursor recorded at flow time, not from the block
            // stacking computed above (which does not model inline placement).
            // An axis with auto insets keeps this static position; an explicit
            // inset overrides it below. Without this an abspos with auto insets
            // inside inline content would re-flow its own content from the top
            // of its containing block instead of its in-flow line position.
            if ((Position == CssConstants.Absolute || Position == CssConstants.Fixed)
                && InlineStaticPosition is { } inlineStatic)
            {
                left = inlineStatic.X + ActualMarginLeft;
                top = inlineStatic.Y + ActualMarginTop;
            }

            // --- Float positioning ---
            if (Float != CssConstants.None)
            {
                // Align Y with previous float sibling if consecutive
                if (prevSibling != null && prevSibling.Float != CssConstants.None)
                    top = prevSibling.Location.Y;

                double containerLeft = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ContainingBlock.ActualBorderLeftWidth;
                double containerRight = ContainingBlock.ClientLeft + ContainingBlock.AvailableWidth;

                double floatHeight = Math.Max(ActualHeight + ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth, 1);

                // Collect all preceding floats in the BFC, including
                // those nested inside non-BFC siblings (CSS2.1 §9.5.1).
                var precedingFloats = CssBoxHelper.CollectPrecedingFloatsInBfc(this);

                // CSS2.1 §9.5.1 rule 4: A floating box's outer top
                // (margin edge) may not be higher than the top of its
                // containing block.  `top` already includes the margin
                // contribution (from MarginTopCollapse), so the outer
                // (margin-edge) top = top - ActualMarginTop.  The
                // constraint outer_top >= ClientTop translates to:
                //   top >= ClientTop + ActualMarginTop
                // This allows negative margins to pull the float above
                // the content-area edge while still honoring the rule.
                if (ParentBox != null)
                    top = Math.Max(top, ParentBox.ClientTop + ActualMarginTop);

                // CSS2.1 §9.5.1 rule 6: The outer top of a floating
                // box may not be higher than the outer top of any
                // block or floated box generated by an element earlier
                // in the source document.
                foreach (var pf in precedingFloats)
                    top = Math.Max(top, pf.Location.Y);

                if (Float == CssConstants.Left)
                {
                    // Iteratively resolve collisions with all prior floats (CSS1 §5.5.25)
                    for (int iter = 0; iter < 100; iter++)
                    {
                        left = containerLeft + ActualMarginLeft;

                        foreach (var floatBox in precedingFloats)
                        {
                            if (floatBox.Float == CssConstants.Left)
                            {
                                double fBottom = floatBox.ActualBottom;

                                if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                    left = Math.Max(left, floatBox.Location.X + floatBox.Size.Width + floatBox.ActualMarginRight + ActualMarginLeft);
                            }
                        }

                        // Also ensure left float doesn't overlap with right floats
                        double effectiveRight = containerRight;

                        foreach (var floatBox in precedingFloats)
                        {
                            if (floatBox.Float == CssConstants.Right)
                            {
                                double fBottom = floatBox.ActualBottom;

                                if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                    effectiveRight = Math.Min(effectiveRight, floatBox.Location.X - floatBox.ActualMarginLeft);
                            }
                        }

                        if (left + Size.Width <= effectiveRight)
                            break;

                        // Move below the lowest overlapping float
                        double maxBottom = MoveBelowOverlappingFloats(precedingFloats, top, floatHeight);

                        if (maxBottom <= top)
                            break;

                        top = maxBottom;
                    }
                }
                else if (Float == CssConstants.Right)
                {
                    // Iteratively resolve collisions with all prior floats (CSS1 §5.5.26)
                    for (int iter = 0; iter < 100; iter++)
                    {
                        left = containerRight - Size.Width - ActualMarginRight;

                        // Avoid overlapping with preceding right floats
                        foreach (var floatBox in precedingFloats)
                        {
                            if (floatBox.Float == CssConstants.Right)
                            {
                                double fBottom = floatBox.ActualBottom;

                                if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                    left = Math.Min(left, floatBox.Location.X - floatBox.ActualMarginLeft - Size.Width - ActualMarginRight);
                            }
                        }

                        // Ensure right float doesn't overlap with left floats
                        double leftFloatEdge = containerLeft;

                        foreach (var floatBox in precedingFloats)
                        {
                            if (floatBox.Float == CssConstants.Left)
                            {
                                double fBottom = floatBox.ActualBottom;

                                if (top < fBottom && top + floatHeight > floatBox.Location.Y)
                                    leftFloatEdge = Math.Max(leftFloatEdge, floatBox.Location.X + floatBox.Size.Width + floatBox.ActualMarginRight);
                            }
                        }

                        if (left >= leftFloatEdge)
                            break;

                        // Move below the lowest overlapping float
                        double maxBottom = MoveBelowOverlappingFloats(precedingFloats, top, floatHeight);

                        if (maxBottom <= top)
                            break;

                        top = maxBottom;
                    }
                }
            }


            // CSS2.1 §8.3.1/§9.5.2: Handle clear property.  Clearance
            // inhibits margin collapsing and pushes the border edge of the
            // cleared element below the bottom outer edge of the relevant
            // floats.  Clearance can be negative when the uncollapsed
            // position is already past the float.
            if (Clear != CssConstants.None)
            {
                double maxFloatBottom = CssBoxHelper.GetMaxFloatBottom(this);

                if (maxFloatBottom > 0)
                {
                    double hypotheticalTop = top;

                    // Compute uncollapsed position: margins are NOT
                    // collapsed when clearance is present (§8.3.1).
                    // Use the effective margin for empty collapsible
                    // boxes (§8.3.1 margin-through-collapse).
                    double uncollapsedTop;

                    if (flowPrev != null)
                    {
                        double prevMarginBottom = (flowPrev is CssBox fpb)
                            ? CssBoxHelper.GetEffectiveMarginBottom(fpb)
                            : flowPrev.ActualMarginBottom;

                        uncollapsedTop = flowPrevBottom
                            + prevMarginBottom
                            + ActualMarginTop;
                    }
                    else if (ParentBox != null)
                    {
                        uncollapsedTop = ParentBox.ClientTop + ActualMarginTop;
                    }
                    else
                    {
                        uncollapsedTop = hypotheticalTop;
                    }

                    // CSS2.2 §9.5.2: Only introduce clearance when the
                    // hypothetical position (where the top border edge
                    // would be if 'clear' were 'none') is NOT past the
                    // relevant floats.  When the margin alone already
                    // places the element past the float, no clearance is
                    // needed and margin collapsing is preserved.
                    if (hypotheticalTop < maxFloatBottom)
                    {
                        // clearance = max(amount to clear float, amount to
                        // reach hypothetical position).  This can be negative.
                        double clearance = Math.Max(
                            maxFloatBottom - uncollapsedTop,
                            hypotheticalTop - uncollapsedTop);

                        top = uncollapsedTop + clearance;
                    }
                }
            }

            // CSS2.1 §9.5: The border box of an element in normal
            // flow that establishes a new BFC must not overlap the
            // margin box of any floats in the same BFC.  Shift the
            // block right past left floats and narrow it to avoid
            // right floats.  If it cannot fit beside the floats,
            // clear below them.
            if (Float == CssConstants.None && Position != CssConstants.Absolute && Position != CssConstants.Fixed)
            {
                bool isBfcRoot = CssBoxHelper.EstablishesBfc(this);

                if (isBfcRoot)
                {
                    var precedingFloats = CssBoxHelper.CollectPrecedingFloatsInBfc(this);

                    if (precedingFloats.Count > 0)
                    {
                        double containerLeft = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ContainingBlock.ActualBorderLeftWidth;
                        double containerRight = ContainingBlock.ClientLeft + ContainingBlock.AvailableWidth;
                        double boxHeight = Math.Max(Size.Height, GetEmHeight());

                        // Try to fit beside floats; if not possible, clear
                        // below them.  100 iterations is a safe upper bound
                        // since each iteration advances past at least one
                        // float's bottom edge.
                        for (int bfcIter = 0; bfcIter < 100; bfcIter++)
                        {
                            double leftEdge = containerLeft + ActualMarginLeft;
                            double rightEdge = containerRight - ActualMarginRight;

                            foreach (var fb in precedingFloats)
                            {
                                double fbBottom = fb.ActualBottom + fb.ActualMarginBottom;

                                if (top < fbBottom && top + boxHeight > fb.Location.Y - fb.ActualMarginTop)
                                {
                                    if (fb.Float == CssConstants.Left)
                                        leftEdge = Math.Max(leftEdge, fb.Location.X + fb.Size.Width + fb.ActualMarginRight + ActualMarginLeft);
                                    else if (fb.Float == CssConstants.Right)
                                        rightEdge = Math.Min(rightEdge, fb.Location.X - fb.ActualMarginLeft - ActualMarginRight);
                                }
                            }

                            double availableWidth = rightEdge - leftEdge;

                            if (availableWidth >= Size.Width || availableWidth >= 0)
                            {
                                left = leftEdge;

                                if (availableWidth < Size.Width && (Width == CssConstants.Auto || string.IsNullOrEmpty(Width)))
                                    Size = new SizeF((float)availableWidth, Size.Height);

                                break;
                            }

                            // Cannot fit beside floats — clear below them.
                            double maxFb = top;

                            foreach (var fb in precedingFloats)
                            {
                                double fbBottom = fb.ActualBottom + fb.ActualMarginBottom;

                                if (top < fbBottom && top + boxHeight > fb.Location.Y - fb.ActualMarginTop)
                                    maxFb = Math.Max(maxFb, fbBottom);
                            }

                            if (maxFb <= top)
                                break;

                            top = maxFb;
                        }
                    }
                }
            }

            Location = new PointF((float)left, (float)top);
            ActualBottom = top;
            AbsposLocationFinalized = false;

            // CSS2.1 §10.3.7 / §10.6.4: For absolutely positioned
            // elements with explicit 'top'/'left', override the static
            // position with the CSS-specified offset from the containing
            // block's padding edge.
            if (Position == CssConstants.Absolute)
            {
                var cb = FindPositionedContainingBlock();

                GetAbsoluteContainingBlockPaddingBox(cb, out double cbPadLeft, out double cbPadTop, out double cbPadWidth, out double cbPadHeight);

                ResolveOverconstrainedAutoMargins(cbPadWidth, cbPadHeight);

                float newX = Location.X, newY = Location.Y;

                if (Left != null && Left != CssConstants.Auto)
                {
                    double cssLeft = ParseUsedLength(Left, cbPadWidth);
                    newX = (float)(cbPadLeft + cssLeft + ActualMarginLeft);
                }
                else if (Right != null && Right != CssConstants.Auto)
                {
                    // CSS2.1 §10.3.7: When left is auto and right is
                    // specified, position from the right padding edge.
                    double cssRight = ParseUsedLength(Right, cbPadWidth);
                    newX = (float)(cbPadLeft + cbPadWidth - cssRight - ActualMarginRight - Size.Width);
                }

                if (Top != null && Top != CssConstants.Auto)
                {
                    double cssTop = ParseUsedLength(Top, cbPadHeight);
                    newY = (float)(cbPadTop + cssTop + ActualMarginTop);
                }
                else if (Bottom != null && Bottom != CssConstants.Auto)
                {
                    // CSS2.1 §10.6.4: When top is auto and bottom is
                    // specified, position from the bottom padding edge.
                    double cssBottom = ParseUsedLength(Bottom, cbPadHeight);
                    double boxHeight = ActualBottom - Location.Y;

                    // boxHeight may be zero when the box position was
                    // just initialised and children have not yet been
                    // laid out.  Fall back to Size.Height which reflects
                    // any explicit CSS height already applied.
                    if (boxHeight <= 0)
                        boxHeight = Size.Height;

                    // Still nothing: the box is sized by content that has not been laid out yet, so
                    // any placement made now is wrong by the height it is about to get.
                    // `PositionAbsoluteBox` re-places it once that height is known, and until then
                    // the static position is the better guess — placing it at the containing
                    // block's bottom edge instead puts the whole subtree *below* that edge while
                    // the children lay out, and `LayoutEnvironment.ActualSize` is a running maximum
                    // that keeps the overshoot after the box is corrected. In a paged render that
                    // is a whole extra blank page: `fixedpos-009-print`'s reference is two pages of
                    // `height: 100vh` and came out three, its content ending 36px — one masked
                    // pencil — past the end.
                    if (boxHeight > 0)
                        newY = (float)(cbPadTop + cbPadHeight - cssBottom - ActualMarginBottom - boxHeight);
                }

                Location = new PointF(newX, newY);
                ActualBottom = newY;

                // Location now holds the final left/top offset; the content
                // flow below starts here, so AdjustAbsolutePosition must not
                // add the offset again (WPT css-anchor-position anchor-scroll).
                if (Left != null && Left != CssConstants.Auto || Top != null && Top != CssConstants.Auto)
                    AbsposLocationFinalized = true;
            }

            // CSS2.1 §10.6.4 / §9.6.1: For fixed-position elements,
            // the containing block is the viewport.  When top/left/
            // bottom/right are explicitly set, use those offsets from
            // the viewport edge.  When they are auto, the static
            // position (computed above) is kept.
            if (Position == CssConstants.Fixed && LayoutEnvironment != null)
            {
                bool hasLeft = Left != null && Left != CssConstants.Auto;
                bool hasRight = Right != null && Right != CssConstants.Auto;
                bool hasTop = Top != null && Top != CssConstants.Auto;
                bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;

                if (hasLeft || hasRight || hasTop || hasBottom)
                {
                    // RF-BRIDGE-1b Track 3.2: a fixed box anchors to its viewport — the
                    // top-level viewport at the origin normally, or the enclosing nested
                    // browsing context's sub-viewport when inside an <iframe>. The
                    // sub-viewport rect carries both the origin (composed onto the frame
                    // content box by the later LayoutSubdocument translate) and the size.
                    var vp = FixedPositioningViewport();

                    ResolveOverconstrainedAutoMargins(vp.Width, vp.Height);

                    float newX = Location.X, newY = Location.Y;

                    if (hasLeft)
                    {
                        double cssLeft = ParseUsedLength(Left, vp.Width);
                        newX = (float)(vp.X + cssLeft + ActualMarginLeft);
                    }
                    else if (hasRight)
                    {
                        double cssRight = ParseUsedLength(Right, vp.Width);
                        newX = (float)(vp.X + vp.Width - cssRight - ActualMarginRight - Size.Width);
                    }

                    if (hasTop)
                    {
                        double cssTop = ParseUsedLength(Top, vp.Height);
                        newY = (float)(vp.Y + cssTop + ActualMarginTop);
                    }
                    else if (hasBottom)
                    {
                        double cssBottom = ParseUsedLength(Bottom, vp.Height);
                        double boxHeight = ActualBottom - Location.Y;

                        if (boxHeight <= 0)
                            boxHeight = Size.Height;

                        // CSS2.1 §10.6.4: a bottom-anchored fixed box is positioned by its
                        // bottom margin edge, so its used height must be subtracted. When
                        // the box is placed before its block size resolves (both the used
                        // height and Size.Height are still 0), derive the border-box height
                        // from an explicit, non-percentage CSS height — otherwise the box is
                        // anchored by its top edge to the viewport bottom (off by its own
                        // height). Mirrors the abspos IMCB definite-height fallback.
                        if (boxHeight <= 0
                            && Height != CssConstants.Auto && !string.IsNullOrEmpty(Height)
                            && !Height.Contains('%'))
                        {
                            double cssHeight = ParseUsedLength(Height, 0);
                            boxHeight = ResolveSpecifiedHeightToBorderBox(cssHeight);
                        }

                        newY = (float)(vp.Y + vp.Height - cssBottom - ActualMarginBottom - boxHeight);
                    }

                    Location = new PointF(newX, newY);
                    ActualBottom = newY;

                    if (hasLeft || hasTop)
                        AbsposLocationFinalized = true;
                }

                // When all offsets are auto, keep the static position
                // (Location is already set from normal-flow
                // calculation above).
            }
        }
    }

    /// <summary>
    /// Pre-resolve a percentage or aspect-ratio block size from the used width
    /// BEFOREchild layout, so a percentage-height descendant can resolve against
    /// thiscontainer's definite height (CSS2.1 §10.5 / Sizing 4 §4).
    /// </summary>
    private void PreResolveDefiniteHeightForDescendants()
    {
        // CSS2.1 §10.5: Pre-resolve percentage heights so that children
        // can use ContainingBlock.Size.Height for their own percentage
        // height resolution.  This must run AFTER position assignment
        // (which resets Size.Height to 0 via ActualBottom = top) but
        // BEFORE child layout so descendants see the correct height.
        if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height) && Height.Contains('%') && !HeightPercentageResolvesToAuto())
        {
            double cbHeight = PercentageHeightContainingBlockHeight();
            double length = ParseUsedLength(Height, cbHeight);
            double preHeight = ResolveSpecifiedHeightToBorderBox(length);

            Size = new SizeF(Size.Width, (float)preHeight);
        }

        // CSS Sizing 4 §4: likewise pre-resolve an aspect-ratio block size from
        // the used width so a percentage-height child (e.g. a filling
        // background element) can resolve against the container's definite
        // aspect-ratio height. The final ActualBottom is re-established after
        // child layout below; this only makes the height visible to
        // descendants beforehand, mirroring the §10.5 pre-resolution above.
        else if (CanTransferAspectRatioToBlockHeight
            && TryResolveAspectRatioBlockHeight(out double aspectRatioPreHeight))
        {
            Size = new SizeF(Size.Width, (float)aspectRatioPreHeight);
        }
    }

    /// <summary>
    /// Lay out this block's contents: table, row-flex,
    /// inline-formatting-context,or block children (with multi-column
    /// pre-constraint),then run the flex/grid alignment passes.
    /// </summary>
    private void LayoutBlockChildren(ILayoutEnvironment g)
    {
        //If we're talking about a table here..
        if (Display == CssConstants.Table || Display == CssConstants.InlineTable)
        {
            CssLayoutEngineTable.PerformLayout(g, this, BaseUrl);
        }
        else
        {
            // CSS Flexbox §8.2/§8.4: Map flex alignment properties to
            // CSS2.1 text-align so that the inline formatting context
            // fallback (FlowInlineBlock) produces visually aligned items.
            // This only applies when the author has not set text-align
            // explicitly (i.e. it still has the default 'left' value).
            if (Display is "flex" or "inline-flex" or "grid" or "inline-grid")
            {
                if (JustifyContent is "center" && TextAlign is CssConstants.Left or "start" or "")
                {
                    TextAlign = CssConstants.Center;
                }
                else if (JustifyContent is "flex-end" or "end" && TextAlign is CssConstants.Left or "start" or "")
                {
                    TextAlign = CssConstants.Right;
                }

                ApplyOrderModifiedDocumentOrder();
            }

            if (IsRowFlexContainer())
            {
                PerformFlexRowLayout(g);
            }

            //If there's just inline boxes, create LineBoxes
            else if (LayoutBoxUtils.ContainsInlinesOnly(this))
            {
                ActualBottom = Location.Y;
                CssLayoutEngine.CreateLineBoxes(g, this); //This will automatically set the bottom of this block

                // CSS2.1 §9.5: Floated children were skipped by
                // CreateLineBoxes (they are out-of-flow).  Lay them out
                // now so they are positioned and painted.
                bool laidOutOwnFloat = false;

                foreach (var childBox in Boxes)
                {
                    if (childBox.Float != CssConstants.None)
                    {
                        childBox.PerformLayout(g);
                        laidOutOwnFloat |= childBox.Display != CssConstants.None
                            && childBox.Size is { Width: > 0, Height: > 0 };

                        // CSS2.1 §13.3.1: When page-break-inside:avoid is
                        // set on a float's containing block, move the float
                        // to the next page if it would otherwise cross a
                        // page boundary.
                        if (PageBreakInside == CssConstants.Avoid)
                            childBox.BreakPage();
                    }
                }

                // A float's own vertical position is decided by the content before it, so it can
                // only be placed after that content has flowed — which leaves the lines that
                // should have been shortened beside it already flowed at full width. Re-flow them
                // now that the geometry exists; the float's position does not depend on the
                // narrower lines (its top is where the flow had already reached), so one extra
                // pass settles it rather than oscillating. Blocks with no float of their own —
                // nearly all of them — never pay for this.
                if (laidOutOwnFloat)
                {
                    ActualBottom = Location.Y;
                    CssLayoutEngine.CreateLineBoxes(g, this);
                }

                // CSS2.1 §9.4.3: a relatively positioned inline-level box is offset
                // visually once its line boxes exist. PerformLayout does that for every box
                // it lays out, but an inline-level box is laid out by CreateLineBoxes and
                // never goes through PerformLayout — so nothing moved an inline <span> or
                // <img> that asked to be moved, and `position: relative; left: 160px` on one
                // rendered at its static position.
                if (!CssWritingMode.IsVertical(WritingMode))
                    OffsetRelativeInlineDescendants();

                // CSS2.1 §10.6.7: Elements that establish a new block
                // formatting context (BFC) must include descendant floats
                // in their auto-height calculation.  The inline path above
                // does not call MarginBottomCollapse(), so BFC elements
                // with only floated children would otherwise have zero
                // content height.
                if (CssBoxHelper.EstablishesBfc(this))
                {
                    ActualBottom = MarginBottomCollapse();
                }

                // CSS Grid Level 1 §8.5: When all grid items share
                // the same grid-row and grid-column, reposition them
                // to the container's content-area origin so they
                // overlap visually.  (This duplicates the same logic
                // in the block path below; it is needed here because
                // ContainsInlinesOnly() forces grid containers into
                // the inline layout path for shrink-to-fit sizing.)
                if (Display is "grid" or "inline-grid")
                    ApplyGridLayoutAfterInline();

                // CSS Box Alignment §6.2: distribute flex/grid items along
                // the block (cross) axis per align-items / align-self.
                ApplyFlexGridCrossAxisAlignment();

                // CSS Flexbox §9.3/§9.5: a wrapping or reversed column flex container needs its
                // items broken into lines and packed from the main-start, neither of which a
                // post-pass over the single block-flow stack below can express. It takes the
                // placement over when it applies; the one case it declines — a single line in
                // ordinary `column` order — is what those two passes already handle.
                if (!PerformFlexColumnLineLayout(g))
                {
                    ApplyFlexColumnInlineAxisAlignment(g);
                    ApplyFlexColumnMainAxisSizing(g);
                }
            }
            else if (Boxes.Count > 0)
            {
                // CSS Multi-column: Pre-constrain width so children
                // lay out at column width instead of full container width.
                float savedWidth = Size.Width;
                int preColCount = 0;

                bool hasExplicitColCount = ColumnCount != null && ColumnCount != "auto"
                    && int.TryParse(ColumnCount, out preColCount) && preColCount > 1;
                bool hasColWidth = ColumnWidth != null && ColumnWidth != "auto"
                    && !string.IsNullOrEmpty(ColumnWidth);

                bool isMultiColumn = hasExplicitColCount || hasColWidth;

                if (isMultiColumn && !hasExplicitColCount && hasColWidth)
                {
                    // Auto column-count from column-width: compute the
                    // number of columns so we can pre-constrain width.
                    double cwVal = ParseUsedLength(ColumnWidth, Size.Width, percentAgainstContainingBlock: false);
                    double gap = ResolveColumnGap();
                    double available = Size.Width - ActualPaddingLeft - ActualPaddingRight
                        - ActualBorderLeftWidth - ActualBorderRightWidth;

                    if (cwVal > 0 && available > 0)
                        preColCount = Math.Max(1, (int)Math.Floor((available + gap) / (cwVal + gap)));

                    isMultiColumn = preColCount > 1;
                }

                if (isMultiColumn && preColCount > 1)
                {
                    double columnGap = ResolveColumnGap();
                    double cw = Size.Width - ActualPaddingLeft - ActualPaddingRight
                        - ActualBorderLeftWidth - ActualBorderRightWidth;

                    double colWidth = (cw - (preColCount - 1) * columnGap) / preColCount;

                    if (colWidth > 0)
                        Size = new SizeF((float)colWidth, Size.Height);
                }

                CssBox? previousInFlow = null;
                for (int childIndex = 0; childIndex < Boxes.Count; childIndex++)
                {
                    var childBox = Boxes[childIndex];
                    childBox.PerformLayout(g);

                    // CSS Fragmentation 3 §3: a forced page break before this child (or after the
                    // one before it) moves it to the top of the next page. Applied here, between
                    // siblings, so the child that follows lays out from the moved bottom edge.
                    // CSS Paged Media 3 §3.4's page-name change is the same break, which is why it
                    // is decided here too — from the index, since the sibling it is measured
                    // against is not the same one a break-after is read from.
                    ApplyForcedPageBreakBefore(childBox, childIndex, previousInFlow);

                    // CSS Fragmentation 3 §4.1: and a page boundary falling inside an unbreakable
                    // child moves the whole child past it. Breakable content needs nothing — the
                    // surface is continuous, so it already resumes at the top of the next page.
                    ApplyMonolithicPageFit(childBox);

                    if (childBox.Display != CssConstants.None
                        && childBox.Position is not (CssConstants.Absolute or CssConstants.Fixed)
                        && childBox.Float == CssConstants.None)
                    {
                        previousInFlow = childBox;
                    }

                    // CSS2.1 §13.3.1: When page-break-inside:avoid is
                    // set, move floated children to the next page if they
                    // would cross a page boundary.
                    if (childBox.Float != CssConstants.None && PageBreakInside == CssConstants.Avoid)
                        childBox.BreakPage();
                }

                // Restore original width after children are laid out.
                if (isMultiColumn)
                    Size = new SizeF(savedWidth, Size.Height);

                ActualRight = CalculateActualRight();
                ActualBottom = MarginBottomCollapse();

                if (Display is "grid" or "inline-grid")
                    ApplyGridLayoutAfterInline();

                // A column flex container's children stack through this block path, so the
                // resolve-flexible-lengths step over them has to run here, once they are laid
                // out and their content heights are known.
                ApplyFlexColumnMainAxisSizing(g);
            }
        }
    }

    /// <summary>
    /// CSS Multi-column §3: post-layout redistribution of in-flow children into
    /// multiplecolumns when column-count > 1 or column-width is specified.
    /// </summary>
    private void ApplyMultiColumnPostLayout()
    {
        // CSS Multi-column Layout §3: When column-count > 1 or column-width
        // is specified, redistribute in-flow children into multiple columns.
        // This is a post-layout transformation that moves children
        // horizontally and vertically to simulate multi-column flow.
        {
            int colCount = 0;

            bool hasExplicitCount = ColumnCount != null && ColumnCount != "auto"
                && int.TryParse(ColumnCount, out colCount) && colCount > 1;

            bool hasColumnWidth = ColumnWidth != null && ColumnWidth != "auto"
                && !string.IsNullOrEmpty(ColumnWidth);

            if (!hasExplicitCount && hasColumnWidth)
            {
                // Auto column-count from column-width: CSS Multi-column §3.4
                double cw = ParseUsedLength(ColumnWidth, Size.Width, percentAgainstContainingBlock: false);
                double gap = GetEmHeight();
                double available = Size.Width - ActualPaddingLeft - ActualPaddingRight
                    - ActualBorderLeftWidth - ActualBorderRightWidth;

                if (cw > 0 && available > 0)
                    colCount = Math.Max(1, (int)Math.Floor((available + gap) / (cw + gap)));
            }

            if (colCount > 1 && Boxes.Count > 0)
            {
                ApplyMultiColumnLayout(colCount);
            }
        }
    }

    /// <summary>
    /// Resolve the used block size (height): explicit/percentage height, the
    /// abspostop+bottom constraint height, aspect-ratio transfer, and the
    /// quirks-modehtml/body viewport-fill floor.
    /// </summary>
    private void ResolveUsedBlockHeight()
    {
        // CSS Containment 2 §3.2: a size-contained box is sized as though it had no contents, so
        // the content height the child loop just accumulated into ActualBottom is not this box's
        // to report — `contain-intrinsic-height` stands in for it, or zero when nothing does.
        // Stated up front rather than in an auto-height branch of its own: everything below
        // (an explicit height, the inset-pair constraint, an aspect-ratio transfer, the
        // quirks-mode floor) is the box's own size talking and still wins over its contents,
        // exactly as it does without containment.
        if (AppliesSizeContainment)
        {
            double containedContentHeight = ContainedIntrinsicContentHeight;

            ActualBottom = Location.Y + containedContentHeight
                + ActualPaddingTop + ActualPaddingBottom
                + ActualBorderTopWidth + ActualBorderBottomWidth;
        }

        // CSS content-box model: 'height' specifies the content height only;
        // padding and border are additive (CSS2.1 §10.6.3). An intrinsic-sizing
        // height keyword (min-/max-/fit-content) is not a length — the content
        // height already in ActualBottom is its used value, so leave it be.
        if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height) && !IsIntrinsicSizingHeightKeyword(Height))
        {
            // CSS2.1 §10.5: If height is a percentage and the containing
            // block's height is not explicitly specified (auto), the
            // percentage resolves to auto and this constraint is skipped.
            if (!HeightPercentageResolvesToAuto())
            {
                // CSS2.1 §10.5: Percentage heights resolve against the
                // containing block's height, not the element's own size.
                // ActualHeight uses Size.Height (the element's own height
                // from child layout), which is wrong for percentage values.
                // Resolve against the containing block's height instead.
                double contentHeight;

                if (Height.Contains('%'))
                {
                    double cbHeight = PercentageHeightContainingBlockHeight();
                    contentHeight = ParseUsedLength(Height, cbHeight);
                }
                else
                {
                    contentHeight = string.Equals(Height, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                        ? GetParent().ActualHeight
                        : ActualHeight;
                }

                double borderBoxHeight = ResolveSpecifiedHeightToBorderBox(contentHeight);

                // CSS2.1 §10.6.3: An explicit height sets the content box
                // height.  Content that exceeds this height overflows
                // (visible by default) but does not affect sibling
                // positioning.  Use direct assignment so that explicit
                // height (e.g. height:0) can override the height computed
                // by CreateLineBoxes (e.g. from line-height).
                ActualBottom = Location.Y + borderBoxHeight;
            }
        }
        else if ((Position == CssConstants.Absolute || Position == CssConstants.Fixed)
            && Top != null && Top != CssConstants.Auto
            && Bottom != null && Bottom != CssConstants.Auto
            && (Height == CssConstants.Auto || string.IsNullOrEmpty(Height)))
        {
            // CSS2.1 §10.6.4: For absolutely positioned, non-replaced
            // elements when height is auto and both top and bottom are
            // specified, compute height from the constraint equation:
            // top + margin-top + height + margin-bottom + bottom = CB height
            double cbHeight;

            if (Position == CssConstants.Fixed && LayoutEnvironment != null)
                cbHeight = FixedPositioningViewport().Height;
            else
            {
                var cb = FindPositionedContainingBlock();
                GetAbsoluteContainingBlockPaddingBox(cb, out _, out _, out _, out cbHeight);
            }

            double cssTop = ParseUsedLength(Top, cbHeight);
            double cssBottom = ParseUsedLength(Bottom, cbHeight);
            double resolvedHeight = cbHeight - cssTop - cssBottom - ActualMarginTop - ActualMarginBottom
                - ActualPaddingTop - ActualPaddingBottom - ActualBorderTopWidth - ActualBorderBottomWidth;

            if (resolvedHeight < 0) 
                resolvedHeight = 0;

            double borderBoxH = resolvedHeight + ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth;

            ActualBottom = Location.Y + borderBoxH;
        }

        // CSS2.1 §10.6.2/§10.6.5: a block-level or out-of-flow replaced box's block size comes from
        // its natural size and ratio, not from its (hidden) content and not from a top/bottom inset
        // pair — the same pass that settled its width in ResolveBlockUsedWidth, re-run now that
        // Size.Width is final. Without it a `display: block` <canvas> measured its content height as
        // zero and vanished, and an absolutely positioned <img> with four insets stretched to fill
        // them.
        {
            double availableInlineSize = ContainingBlock.Size.Width
                - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth;

            if (TryResolveReplacedBorderBoxSize(availableInlineSize, out _, out double replacedHeight))
                ActualBottom = Location.Y + replacedHeight;
        }

        // CSS Sizing 4 §4: a box with a preferred aspect-ratio and an auto block
        // (height) axis derives its used height from its used inline (width) size.
        // Runs after the explicit-height paths above (so an author height still
        // wins) and before the §10.7 min-/max-height clamp below (so e.g. a
        // min-height floors the transferred square). Scoped by
        // CanTransferAspectRatioToBlockHeight to boxes whose used width is already
        // resolved and does not itself depend on the aspect ratio; <img> and the
        // other intrinsically-sized replaced elements keep their own ratio sizing.
        if (CanTransferAspectRatioToBlockHeight
            && TryResolveAspectRatioBlockHeight(out double aspectRatioBorderBoxHeight))
        {
            double contentBottom = ActualBottom;
            ActualBottom = Location.Y + aspectRatioBorderBoxHeight;

            // CSS Sizing 4 §5.1: the automatic minimum size of a box with a preferred aspect ratio
            // is its content-based minimum, so a ratio that would make the box shorter than its own
            // content does not — the ratio gives way. `min-height` says otherwise the moment it is
            // given a value, and a scroll container's content does not push its box out at all.
            if (AutomaticMinimumSizeApplies && contentBottom > ActualBottom)
                ActualBottom = contentBottom;
        }

        // Quirks-mode "the body element fills the html element" / "the html
        // element fills the viewport" quirks (https://quirks.spec.whatwg.org/):
        // in quirks mode an auto-height root <html> fills the viewport (minus its
        // margins) and an auto-height <body> fills the html element's content box
        // (minus its own margins), instead of shrink-wrapping to content. Acts as
        // a floor (content taller than the fill still overflows/scrolls).
        if ((Height == CssConstants.Auto || string.IsNullOrEmpty(Height))
            && DocumentQuirksMode
            && LayoutEnvironment != null
            && Position is not (CssConstants.Absolute or CssConstants.Fixed)
            && Float == CssConstants.None
            && HtmlTag != null)
        {
            double? fillBorderBoxHeight = null;

            if (HtmlTag.Name.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                fillBorderBoxHeight = LayoutEnvironment.ViewportSize.Height - ActualMarginTop - ActualMarginBottom;
            }
            else if (HtmlTag.Name.Equals("body", StringComparison.OrdinalIgnoreCase)
                && ParentBox is { HtmlTag: { } parentTag }
                && parentTag.Name.Equals("html", StringComparison.OrdinalIgnoreCase))
            {
                var html = ParentBox;
                double htmlContentHeight = LayoutEnvironment.ViewportSize.Height
                    - html.ActualMarginTop - html.ActualMarginBottom
                    - html.ActualBorderTopWidth - html.ActualBorderBottomWidth
                    - html.ActualPaddingTop - html.ActualPaddingBottom;

                fillBorderBoxHeight = htmlContentHeight - ActualMarginTop - ActualMarginBottom;
            }

            if (fillBorderBoxHeight is { } fillH && fillH > ActualBottom - Location.Y)
                ActualBottom = Location.Y + fillH;
        }
    }

    /// <summary>
    /// CSS2.1 §10.7: clamp the content height to min-height / max-height (min
    /// winswhen min > max).
    /// </summary>
    private void ApplyMinMaxHeightConstraints()
    {
        // CSS2.1 §10.7: Apply min-height / max-height constraints.
        // When min-height > max-height, min-height wins.
        //
        // ResolveBlockSizeBounds carries the §10.7 rules, including the one that turns a percentage
        // into the property's initial value when the containing block's block size is indefinite —
        // and it looks that basis up through TryGetPercentageBlockSizeBasis, which climbs past
        // *anonymous* boxes. Reading ContainingBlock.Height directly, as this used to, stopped at
        // the anonymous block a block-inside-inline split leaves around a `display: block` <img>
        // and so treated its `max-height: 100%` as `none` (WPT
        // css-sizing/block-image-percentage-max-height-inside-inline).
        var bounds = ResolveBlockSizeBounds();
        if (bounds.IsUnconstrained)
            return;

        double contentHeight = ActualBottom - Location.Y
            - ActualPaddingTop - ActualPaddingBottom - ActualBorderTopWidth - ActualBorderBottomWidth;
        double clamped = bounds.Clamp(contentHeight);

        if (clamped != contentHeight)
            ActualBottom = Location.Y + ResolveSpecifiedHeightToBorderBox(clamped);
    }

    /// <summary>
    /// A float with an explicit (non-auto) height establishes a BFC and takes
    /// itsstated height rather than child-float overflow (CSS2.1 §10.6.1),
    /// re-clampedto min/max-height.
    /// </summary>
    private void ApplyFloatExplicitHeight()
    {
        // Floats with an explicit CSS height establish a new BFC.
        // Their ActualBottom should reflect the stated height, not
        // content overflow from child floats (CSS2.1 §10.6.1).
        // CSS2.1 §10.5: Percentage heights resolve to auto when
        // the containing block's height is not explicitly specified.
        if (Float != CssConstants.None && Height != CssConstants.Auto && !string.IsNullOrEmpty(Height)
            && !IsIntrinsicSizingHeightKeyword(Height))
        {
            if (!HeightPercentageResolvesToAuto())
            {
                // For percentage heights, resolve against the containing
                // block's height directly.  ActualHeight resolves against
                // Size.Height which may have been cached before the
                // percentage height pre-resolution step set the correct
                // Size.Height (CSS2.1 §10.5).
                double contentHeight;

                if (Height.Contains('%'))
                {
                    double cbHeight = PercentageHeightContainingBlockHeight();
                    contentHeight = ParseUsedLength(Height, cbHeight);
                }
                else
                {
                    contentHeight = ActualHeight;
                }

                // CSS2.1 §10.7: min-height/max-height also constrain a float's
                // explicit height. This override runs after the §10.7 clamp above,
                // so without re-clamping here a float with height:100; min-height:200
                // kept 100 (e.g. a float:left grid whose auto-fill row count already
                // grew to min-height — WPT css-grid grid-auto-repeat-min-size-001).
                // height and min/max-height share the box-sizing frame, so clamp the
                // specified value; ResolveSpecifiedHeightToBorderBox normalizes it.
                contentHeight = ClampSpecifiedHeightToMinMax(contentHeight);

                double borderBoxHeight = ResolveSpecifiedHeightToBorderBox(contentHeight);

                ActualBottom = Location.Y + borderBoxHeight;
            }
        }
    }

    /// <summary>
    /// Absolute-position completion: solve the remaining inset (right/bottom
    /// anchored)offsets, then apply CSS Box Alignment §6.1 justify-self /
    /// align-selfself-alignment within the inset-modified containing block.
    /// </summary>
    /// <summary>
    /// CSS2.1 §10.3.7 (inline axis) / §10.6.4 (block axis): for an absolutely-positioned or fixed
    /// box that over-constrains an axis — both opposing insets set and a definite size — with
    /// <em>both</em> margins on that axis <c>auto</c>, the auto margins take equal shares of the
    /// leftover space, centring the box. Resolves those auto margins to the equal px value (and
    /// refreshes the cached used margins) so the normal positioning below — which adds the used
    /// margin to the inset — places the box centred. Only the exact over-constrained + both-auto
    /// case is touched; a one-inset box, or one with a non-auto margin, is left to the existing
    /// rules, so ordinary abspos layout is unchanged. Powers native modal <c>&lt;dialog&gt;</c>
    /// centring (<c>inset:0; margin:auto</c>).
    /// </summary>
    internal void ResolveOverconstrainedAutoMargins(double cbWidth, double cbHeight)
    {
        bool changed = false;

        bool hasLeft = Left != null && Left != CssConstants.Auto;
        bool hasRight = Right != null && Right != CssConstants.Auto;
        if (hasLeft && hasRight && IsSpecifiedMarginLeftAuto && IsSpecifiedMarginRightAuto
            && IsDefiniteBorderBoxWidth())
        {
            double l = ParseUsedLength(Left, cbWidth);
            double r = ParseUsedLength(Right, cbWidth);
            // The insets and Size are used (zoom-scaled) values, so this is the used centring margin;
            // ActualMargin* re-applies EffectiveZoom to the stored string, so store the pre-zoom value
            // (÷ EffectiveZoom, a no-op while NativeZoom is off since EffectiveZoom is then 1.0) so the
            // read-back restores the used margin rather than double-counting the zoom.
            var m = (Math.Max(0, cbWidth - l - r - Size.Width) / 2 / EffectiveZoom)
                .ToString("F4", CultureInfo.InvariantCulture) + "px";
            MarginLeft = m;
            MarginRight = m;
            changed = true;
        }

        bool hasTop = Top != null && Top != CssConstants.Auto;
        bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;
        if (hasTop && hasBottom && IsSpecifiedMarginTopAuto && IsSpecifiedMarginBottomAuto
            && IsDefiniteBorderBoxHeight(out double boxHeight))
        {
            double t = ParseUsedLength(Top, cbHeight);
            double b = ParseUsedLength(Bottom, cbHeight);
            // Pre-zoom the stored margin (÷ EffectiveZoom) for the same reason as the inline axis above.
            var m = (Math.Max(0, cbHeight - t - b - boxHeight) / 2 / EffectiveZoom)
                .ToString("F4", CultureInfo.InvariantCulture) + "px";
            MarginTop = m;
            MarginBottom = m;
            changed = true;
        }

        if (changed)
            InvalidateActualMargins();
    }

    // The used border-box inline size is known (Size.Width has been resolved by positioning time) —
    // the case §10.3.7 centring needs — for an explicit length/percentage width AND for an
    // intrinsic-keyword (min-/max-/fit-content) width, which ResolveBlockUsedWidth now shrink-wraps
    // into Size.Width before positioning. Only `width:auto` is excluded: with both insets it fills the
    // inset-modified containing block (auto margins resolve to 0), so there is no free space to centre.
    private bool IsDefiniteBorderBoxWidth() =>
        Width != CssConstants.Auto && !string.IsNullOrEmpty(Width);

    // The used border-box block size for §10.6.4 centring: Size.Height once resolved, else derived
    // from an explicit non-percentage height (mirrors the bottom-anchored fixed/abspos fallback).
    // False when the height is auto/percentage and not yet resolved — vertical centring is skipped.
    // Also false for an intrinsic-keyword (fit-content/min-/max-content) height: its content height is
    // not known until layout completes (the pre-layout Size.Height holds only the box chrome), so
    // block-axis centring for such a box is deferred to the CenterOutOfFlowBlockAxis root post-pass,
    // which sees the final height. Centring it here against the chrome-only size would mis-centre it.
    private bool IsDefiniteBorderBoxHeight(out double boxHeight)
    {
        boxHeight = 0;
        if (IsIntrinsicSizingHeightKeyword(Height))
            return false;
        boxHeight = Size.Height;
        if (boxHeight > 0)
            return true;
        if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height) && !Height.Contains('%'))
        {
            boxHeight = ResolveSpecifiedHeightToBorderBox(ParseUsedLength(Height, 0));
            return boxHeight > 0;
        }
        return false;
    }

    private void PositionAbsoluteBox()
    {
        // A fixed box is placed the same way and needs the same second look: its `right`/`bottom`
        // are resolved against its own used width and height, and both are still zero when it is
        // first positioned. The earlier pass recovers a height from an explicit, non-percentage
        // `height` and from nothing else, so a fixed box sized by its content and anchored with
        // `bottom` was left anchored by its *top* edge to the viewport's bottom — off by its own
        // height, which in a paged render puts it at the top of the next page. WPT's
        // `css-page/fixedpos-001-print` and its neighbours are written on `bottom: 0` with no
        // height at all. Only the containing block differs: the viewport, not an ancestor.
        if (Position is CssConstants.Absolute or CssConstants.Fixed)
        {
            bool hasLeft = Left != null && Left != CssConstants.Auto;
            bool hasRight = Right != null && Right != CssConstants.Auto;
            bool hasTop = Top != null && Top != CssConstants.Auto;
            bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;

            if ((!hasLeft && hasRight) || (!hasTop && hasBottom))
            {
                double cbPadLeft, cbPadTop, cbPadWidth, cbPadHeight;
                if (Position == CssConstants.Fixed)
                {
                    var viewport = FixedPositioningViewport();
                    (cbPadLeft, cbPadTop) = (viewport.X, viewport.Y);
                    (cbPadWidth, cbPadHeight) = (viewport.Width, viewport.Height);
                }
                else
                {
                    var cb = FindPositionedContainingBlock();
                    GetAbsoluteContainingBlockPaddingBox(cb, out cbPadLeft, out cbPadTop, out cbPadWidth, out cbPadHeight);
                }

                float newX = Location.X;
                float newY = Location.Y;

                if (!hasLeft && hasRight)
                {
                    double boxWidth = ActualRight - Location.X;
                    if (boxWidth <= 0)
                        boxWidth = Size.Width;

                    double cssRight = ParseUsedLength(Right, cbPadWidth);
                    newX = (float)(cbPadLeft + cbPadWidth - cssRight - ActualMarginRight - boxWidth);
                }

                if (!hasTop && hasBottom)
                {
                    double boxHeight = ActualBottom - Location.Y;

                    if (boxHeight <= 0)
                        boxHeight = Size.Height;

                    double cssBottom = ParseUsedLength(Bottom, cbPadHeight);
                    newY = (float)(cbPadTop + cbPadHeight - cssBottom - ActualMarginBottom - boxHeight);
                }

                float deltaX = newX - Location.X;
                float deltaY = newY - Location.Y;

                if (deltaX != 0)
                    OffsetLeft(deltaX);

                if (deltaY != 0)
                {
                    // OffsetTop already shifts Location.Y, and ActualBottom is a
                    // derived value (ActualBottom => Location.Y + Size.Height), so the
                    // box's bottom edge follows the move automatically.  A further
                    // "ActualBottom += deltaY" would double-apply the shift — its
                    // setter writes Size.Height = ActualBottom - Location.Y, growing
                    // (or, as here for a bottom-anchored full-height abspos box,
                    // collapsing) the height by deltaY.  Mirror the horizontal branch
                    // above, which offsets without touching ActualRight.
                    OffsetTop(deltaY);
                }
            }

            // CSS Box Alignment Level 3 §6.1: Post-layout self-alignment for
            // absolutely positioned elements.  After children are laid out,
            // shrink the box to fit-content size and align within the IMCB.
            // This must run after child layout so content dimensions are known.
            string jsPost = JustifySelf?.Trim().ToLowerInvariant() ?? "auto";
            bool jsPostNonDefault = jsPost != "auto" && jsPost != "normal" && jsPost != "stretch";
            string asPost = AlignSelf?.Trim().ToLowerInvariant() ?? "auto";
            bool asPostNonDefault = asPost != "auto" && asPost != "normal" && asPost != "stretch";

            if (jsPostNonDefault || asPostNonDefault)
            {
                var cb = FindPositionedContainingBlock();
                GetAbsoluteContainingBlockPaddingBox(cb, out double cbPadLeft, out double cbPadTop, out double cbPadWidth, out double cbPadHeight);

                bool hasL = Left != null && Left != CssConstants.Auto;
                bool hasR = Right != null && Right != CssConstants.Auto;
                bool hasT = Top != null && Top != CssConstants.Auto;
                bool hasB = Bottom != null && Bottom != CssConstants.Auto;

                // CSS Writing Modes Level 4: the containing block's writing mode
                // determines which physical axis corresponds to justify-self (inline)
                // and align-self (block).
                bool cbVertical = cb.WritingMode == "vertical-rl" || cb.WritingMode == "vertical-lr";

                float newX = Location.X, newY = Location.Y;

                // When align-self resolves the block axis to a non-stretch value,
                // the box uses its content (shrink-to-fit) block size rather than
                // the stretched inset size; record the resolved border-box height
                // so the apply step can shrink it (mirrors how the inline branch
                // sets Size.Width).  Null = leave the block size untouched.
                double? alignBlockBorderBoxHeight = null;

                // justify-self controls the inline axis:
                //   horizontal-tb → horizontal (L/R insets)
                //   vertical-rl/lr → vertical (T/B insets)
                if (jsPostNonDefault)
                {
                    if (!cbVertical && hasL && hasR)
                    {
                        double cssLeft = ParseUsedLength(Left, cbPadWidth);
                        double cssRight = ParseUsedLength(Right, cbPadWidth);
                        double imcbLeft = cbPadLeft + cssLeft;
                        double imcbWidth = cbPadWidth - cssLeft - cssRight;

                        double boxWidth = GetShrinkToFitWidth();
                        Size = new SizeF((float)boxWidth, Size.Height);

                        // For a box the vertical-flow rotation will transpose, the
                        // alignment runs on the CB's inline (horizontal) axis but
                        // the item's PHYSICAL width is its logical HEIGHT (the
                        // rotation swaps them). Align with the physical extent so
                        // an overflowing vrl item (laid out with a small logical
                        // width) is centered/clamped by its true width.
                        double alignWidth = WillBeVerticalTransposed()
                            ? GetShrinkToFitHeight() : boxWidth;

                        // Inline-axis start edge follows the CB's direction (start/end);
                        // self-start/self-end follow the ITEM's start in this horizontal
                        // axis — its inline axis when horizontal-tb (right under rtl), or
                        // its block axis when vertical (vertical-rl starts on the right).
                        bool startIsLow = cb.Direction != "rtl";
                        bool itemStartIsHigh = WritingMode switch
                        {
                            "vertical-rl" => true,
                            "vertical-lr" => false,
                            _ => Direction == "rtl",
                        };

                        double dx = ResolveAbsposSelfAlignment(
                            jsPost, imcbLeft, imcbWidth, cbPadLeft, cbPadWidth,
                            alignWidth, isRtl: !startIsLow, startIsLow,
                            selfStartIsHigh: itemStartIsHigh);

                        newX = (float)(imcbLeft + dx + ActualMarginLeft);
                    }
                    else if (cbVertical && hasT && hasB)
                    {
                        double cssTop = ParseUsedLength(Top, cbPadHeight);
                        double cssBottom = ParseUsedLength(Bottom, cbPadHeight);
                        double imcbTop = cbPadTop + cssTop;
                        double imcbHeight = cbPadHeight - cssTop - cssBottom;

                        double boxHeight = GetShrinkToFitHeight();

                        // Non-stretch justify-self on the vertical inline axis →
                        // the box uses its content (shrink-to-fit) height, not the
                        // top-to-bottom inset-stretched height. Record it so the
                        // shared apply step restores the height after the offset
                        // (mirrors the width un-stretch in the !cbVertical inline
                        // branch and the height un-stretch in the align-self
                        // block-axis branch). Without this the box stays stretched
                        // to the IMCB height and renders as a tall bar.
                        alignBlockBorderBoxHeight = boxHeight;

                        // Inline axis is vertical here; its start runs top→bottom
                        // unless the CB's direction is rtl. start/end follow the CB's
                        // inline direction (so the flip is !startIsLow, mirroring the
                        // align-self block-axis branch); self-start/self-end follow the
                        // ITEM's inline direction — for a vertical-wm item the vertical
                        // axis is its inline axis (start at the bottom under rtl), while
                        // for a horizontal-tb item it is the block axis (start at top).
                        bool startIsLow = cb.Direction != "rtl";
                        bool itemStartIsHigh =
                            (WritingMode == "vertical-lr" || WritingMode == "vertical-rl")
                            && Direction == "rtl";

                        double dy = ResolveAbsposSelfAlignment(
                            jsPost, imcbTop, imcbHeight, cbPadTop, cbPadHeight,
                            boxHeight, isRtl: !startIsLow, startIsLow,
                            selfStartIsHigh: itemStartIsHigh);

                        newY = (float)(imcbTop + dy + ActualMarginTop);
                    }
                    else if (!cbVertical && !hasL && !hasR && ParentBox != null)
                    {
                        // Inline insets are auto → the box is at its static
                        // position; justify-self aligns it within the
                        // static-position rectangle, whose inline extent is the
                        // in-flow parent's content box (CSS Position 3
                        // §abspos-alignment). The box keeps its own inline size.
                        double rectStart = ParentBox.ClientLeft;
                        double rectWidth = ParentBox.ClientRight - ParentBox.ClientLeft;
                        double marginBoxWidth = Size.Width + ActualMarginLeft + ActualMarginRight;
                        bool isRtl = Direction == "rtl";
                        bool startIsLow = cb.Direction != "rtl";

                        double dx = ResolveAbsposSelfAlignment("unsafe " + StripSafeUnsafe(jsPost),
                            rectStart, rectWidth, rectStart, rectWidth,
                            marginBoxWidth, isRtl, startIsLow);

                        newX = (float)(rectStart + dx + ActualMarginLeft);
                    }
                }
                else if (!cbVertical && !hasL && !hasR && ParentBox != null
                         && cb.Direction == "rtl")
                {
                    // justify-self:auto (default) + auto inline insets → the box
                    // rests at its static position: the inline-START edge of the
                    // static-position rectangle (the in-flow parent's content
                    // box). That start edge follows the containing block's
                    // direction — for ltr it is the left edge (already set by
                    // base layout), for rtl it is the RIGHT edge. Without this,
                    // abspos items in rtl containers render flush-left, shifted
                    // left by the free inline width
                    // (WPT css-align/abspos/*-rtl-*, issue #1131).
                    double rectStart = ParentBox.ClientLeft;
                    double rectWidth = ParentBox.ClientRight - ParentBox.ClientLeft;

                    // Use the physical width for a box the rotation will transpose
                    // (its physical width is the logical height).
                    double boxW = WillBeVerticalTransposed() ? GetShrinkToFitHeight() : Size.Width;
                    double marginBoxWidth = boxW + ActualMarginLeft + ActualMarginRight;
                    double dx = ResolveAbsposSelfAlignment(
                        "unsafe start", rectStart, rectWidth, rectStart, rectWidth,
                        marginBoxWidth, isRtl: true, startIsLow: false);

                    newX = (float)(rectStart + dx + ActualMarginLeft);
                }
                else if (cbVertical && !hasT && !hasB && cb.Direction == "rtl")
                {
                    // vertical-rl/lr container: the inline axis is VERTICAL.
                    // justify-self:auto + auto block insets (top/bottom) → the box
                    // rests at its static position: the inline-START edge of the
                    // static-position rectangle. That start follows the inline
                    // direction — for ltr the top (Broiler's default), for rtl the
                    // inline axis is reversed so the start is the BOTTOM. Use the
                    // CB padding box (cbPadTop/cbPadHeight) for the vertical
                    // extent: block-axis sizes resolve bottom-up, so ParentBox's
                    // ActualBottom is not final here, whereas cbPad* carries the
                    // definite-height patch. Without this, abspos items in
                    // vertical-rl+rtl containers render flush-top
                    // (WPT css-align/abspos/*-vrl-rtl-*, issue #1131).
                    double marginBoxHeight = Size.Height + ActualMarginTop + ActualMarginBottom;
                    double dy = ResolveAbsposSelfAlignment(
                        "unsafe start", cbPadTop, cbPadHeight, cbPadTop, cbPadHeight,
                        marginBoxHeight, isRtl: true, startIsLow: false);

                    newY = (float)(cbPadTop + dy + ActualMarginTop);

                    // Preserve the box's own (shrink-to-fit) block size: the apply
                    // step shifts ActualBottom by the same delta as Location, so
                    // record the height to restore it (mirrors the align-self
                    // block-axis un-stretch).
                    alignBlockBorderBoxHeight = Size.Height;
                }

                // align-self controls the block axis:
                //   horizontal-tb → vertical (T/B insets)
                //   vertical-rl/lr → horizontal (L/R insets)
                if (asPostNonDefault)
                {
                    if (!cbVertical && hasT && hasB)
                    {
                        double cssTop = ParseUsedLength(Top, cbPadHeight);
                        double cssBottom = ParseUsedLength(Bottom, cbPadHeight);
                        double imcbTop = cbPadTop + cssTop;
                        double imcbHeight = cbPadHeight - cssTop - cssBottom;

                        double boxHeight = GetShrinkToFitHeight();

                        // Non-stretch align-self → the box is its content height,
                        // not the stretched top-to-bottom inset height.
                        alignBlockBorderBoxHeight = boxHeight;

                        // For a box the vertical-flow rotation will transpose, the
                        // alignment runs on the CB's block (vertical) axis but the
                        // item's PHYSICAL height is its logical WIDTH (the rotation
                        // swaps them); align with the physical extent.
                        double alignHeight = WillBeVerticalTransposed()
                            ? GetShrinkToFitWidth() : boxHeight;

                        // Block-axis start is the top edge for horizontal-tb. self-start/
                        // self-end use the ITEM's start in this vertical axis: its block
                        // axis when horizontal-tb (top), or its inline axis when vertical
                        // (bottom under direction:rtl).
                        bool itemStartIsHigh =
                            (WritingMode == "vertical-lr" || WritingMode == "vertical-rl")
                            && Direction == "rtl";

                        double dy = ResolveAbsposSelfAlignment(
                            asPost, imcbTop, imcbHeight, cbPadTop, cbPadHeight,
                            alignHeight, isRtl: false, startIsLow: true,
                            selfStartIsHigh: itemStartIsHigh);

                        newY = (float)(imcbTop + dy + ActualMarginTop);
                    }
                    else if (cbVertical && hasL && hasR)
                    {
                        double cssLeft = ParseUsedLength(Left, cbPadWidth);
                        double cssRight = ParseUsedLength(Right, cbPadWidth);
                        double imcbLeft = cbPadLeft + cssLeft;
                        double imcbWidth = cbPadWidth - cssLeft - cssRight;

                        double boxWidth = GetShrinkToFitWidth();

                        Size = new SizeF((float)boxWidth, Size.Height);

                        // Block-axis start runs L→R for vertical-lr, R→L for
                        // vertical-rl (so the low/left edge is the start only for lr).
                        // align-self acts on the containing block's BLOCK axis, whose
                        // flow is fixed by writing-mode; `direction` (rtl/ltr) is an
                        // inline-axis property and must NOT flip block-axis start/end
                        // (WPT css-align/abspos/align-self-{vlr,vrl}-*). So the start↔end
                        // flip is driven purely by the writing mode: start sits on the
                        // high edge exactly when the block start is not the low edge.
                        bool startIsLow = cb.WritingMode == "vertical-lr";

                        // self-start/self-end use the ITEM's start edge in this
                        // (horizontal) alignment axis: for a vertical-wm item the
                        // horizontal axis is its block axis (vertical-rl starts on the
                        // right/high edge, vertical-lr on the left/low edge); for a
                        // horizontal-tb item it is the inline axis, whose start is the
                        // right/high edge under direction:rtl.
                        bool itemStartIsHigh = WritingMode switch
                        {
                            "vertical-rl" => true,
                            "vertical-lr" => false,
                            _ => Direction == "rtl",
                        };
                        double dx = ResolveAbsposSelfAlignment(
                            asPost, imcbLeft, imcbWidth, cbPadLeft, cbPadWidth,
                            boxWidth, isRtl: !startIsLow, startIsLow,
                            selfStartIsHigh: itemStartIsHigh);

                        newX = (float)(imcbLeft + dx + ActualMarginLeft);
                    }
                    else if (!cbVertical && !hasT && !hasB)
                    {
                        // Block insets are auto → the box is at its static
                        // position; align-self aligns it within the
                        // static-position rectangle, which has ZERO block size
                        // at the static position (free space = −margin-box
                        // height), so start keeps the box put while center/end
                        // pull it up by half / all of its height (CSS Position 3
                        // §abspos-alignment). The box keeps its own block size:
                        // record it so the shared apply step's ActualBottom
                        // bookkeeping restores the height after the offset
                        // (otherwise moving up shrinks the box by the delta).
                        alignBlockBorderBoxHeight = Size.Height;

                        double marginBoxStart = Location.Y - ActualMarginTop;
                        double marginBoxHeight = Size.Height + ActualMarginTop + ActualMarginBottom;
                        double dy = ResolveAbsposSelfAlignment(
                            "unsafe " + StripSafeUnsafe(asPost),
                            marginBoxStart, 0, marginBoxStart, 0,
                            marginBoxHeight, false, startIsLow: true);

                        newY = (float)(marginBoxStart + dy + ActualMarginTop);
                    }
                }
                else if (cbVertical && !hasL && !hasR && ParentBox != null
                         && cb.WritingMode == "vertical-rl")
                {
                    // align-self:auto (default) + auto block insets (left/right):
                    // for a vertical-rl container the BLOCK axis is horizontal and
                    // flows right-to-left, so the block-START edge is the RIGHT.
                    // The box rests at that block static position, but Broiler's
                    // base layout placed it flush-left, so flush it right within
                    // the parent content box. (vertical-lr keeps the left edge —
                    // its block start — which is Broiler's default, so no branch
                    // is needed there.) The inline (vertical) axis is handled by
                    // the justify-self branch above. Mirrors the rtl inline-axis
                    // static-position fix (WPT css-align/abspos/justify-self-*-vrl-*,
                    // issue #1131). Widths resolve top-down, so ParentBox's
                    // horizontal extent is reliable here (unlike its vertical one).
                    double rectStart = ParentBox.ClientLeft;
                    double rectWidth = ParentBox.ClientRight - ParentBox.ClientLeft;
                    double marginBoxWidth = Size.Width + ActualMarginLeft + ActualMarginRight;
                    double dx = ResolveAbsposSelfAlignment(
                        "unsafe start", rectStart, rectWidth, rectStart, rectWidth,
                        marginBoxWidth, isRtl: true, startIsLow: false);
                    newX = (float)(rectStart + dx + ActualMarginLeft);
                }

                if (newX != Location.X || newY != Location.Y)
                {
                    float deltaX = newX - Location.X;
                    float deltaY = newY - Location.Y;

                    if (deltaX != 0)
                        OffsetLeft(deltaX);

                    if (deltaY != 0)
                    {
                        OffsetTop(deltaY);
                        ActualBottom += deltaY;
                    }
                }

                // Un-stretch the block axis to the content height for non-stretch
                // align-self.  Runs even when the offset was zero (align-self:start
                // keeps the box at the start edge but still shrinks it), so it is
                // outside the offset guard above.
                if (alignBlockBorderBoxHeight is double abh)
                    ActualBottom = Location.Y + abh;
            }
        }
    }

    /// <summary>
    /// CSS Box Alignment §5.4: shift in-flow content vertically for
    /// align-contenton a definite-height block container.
    /// </summary>
    private void ApplyBlockAlignContent()
    {
        // CSS Box Alignment Level 3 §5.4: align-content on block containers
        // shifts the in-flow content vertically when the container has a
        // definite height larger than the content.  Values:
        //   normal/start/baseline/flex-start → no shift (top-aligned)
        //   center                           → center vertically
        //   end/flex-end/last baseline       → bottom-aligned
        //   space-between/space-around/space-evenly → distribute space
        // The "unsafe" and "safe" prefixes are stripped; safe alignment
        // falls back to start when content overflows, but for blocks this
        // is handled implicitly (shift is clamped to ≥ 0).
        if (AlignContent != null && AlignContent != "normal"
            // The definite-track grid pass distributes align-content across its
            // row tracks itself; this block-level shift would double it.
            && !_gridTrackLayoutApplied
            && (IsBlock || Display == CssConstants.ListItem || Display == CssConstants.InlineBlock
                || Display == CssConstants.TableCell)
            && Boxes.Count > 0
            && (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height)
                || Display == CssConstants.TableCell))
        {
            double borderBoxHeight = ActualBottom - Location.Y;
            double containerContentHeight = borderBoxHeight
                - ActualPaddingTop - ActualPaddingBottom
                - ActualBorderTopWidth - ActualBorderBottomWidth;

            // Compute the extent of the in-flow content (excluding absolutely
            // positioned and fixed elements).  Per CSS Box Alignment §5.4 the
            // alignment subject is the content's *margin* box, so the leading
            // child's top margin and the trailing child's bottom margin count
            // toward the consumed space — measuring only border boxes would
            // overstate the free space and shift the content too far.
            double contentTop = double.MaxValue;
            double contentBottom = double.MinValue;
            foreach (var child in Boxes)
            {
                if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                    continue;

                if (child.Display == CssConstants.None)
                    continue;

                double childTop = child.Location.Y - child.ActualMarginTop;
                double childBottom = child.ActualBottom + child.ActualMarginBottom;

                if (childTop < contentTop)
                    contentTop = childTop;

                if (childBottom > contentBottom)
                    contentBottom = childBottom;
            }

            if (contentTop < double.MaxValue && contentBottom > double.MinValue)
            {
                double usedContentHeight = contentBottom - contentTop;
                double freeSpace = containerContentHeight - usedContentHeight;

                // Normalise the align-content value: strip safe/unsafe prefix.
                string ac = AlignContent.Trim();
                bool explicitUnsafe = ac.StartsWith("unsafe ", StringComparison.OrdinalIgnoreCase);
                bool explicitSafe = ac.StartsWith("safe ", StringComparison.OrdinalIgnoreCase);

                if (explicitSafe)
                    ac = ac[5..].Trim();

                else if (explicitUnsafe)
                    ac = ac[7..].Trim();

                // CSS Box Alignment §5.3: when no explicit safe/unsafe keyword
                // is present, the default overflow alignment is "safe".
                bool isSafe = !explicitUnsafe;

                // Only compute shift when there's free space, or when unsafe
                // mode allows shifting even into overflow.
                if (freeSpace > 0.5 || (!isSafe && freeSpace < -0.5))
                {
                    double shift = 0;

                    switch (ac.ToLowerInvariant())
                    {
                        case "center":
                            shift = freeSpace / 2;
                            break;

                        case "end":
                        case "flex-end":
                            shift = freeSpace;
                            break;

                        // baseline / last baseline: with no baseline-sharing group
                        // (each container is independent), both fall back to the
                        // start edge — matching the reference rendering.
                        case "space-between":
                            // Single content group → same as start (no shift).
                            break;

                        case "space-around":
                            shift = freeSpace / 2;
                            break;

                        case "space-evenly":
                            shift = freeSpace / 2;
                            break;

                            // start, flex-start, baseline, normal → no shift.
                    }

                    // Safe alignment: clamp shift to 0 to prevent overflow.
                    if (isSafe && shift < 0)
                        shift = 0;

                    if (Math.Abs(shift) > 0.5)
                    {
                        foreach (var child in Boxes)
                        {
                            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                                continue;

                            if (child.Display == CssConstants.None)
                                continue;

                            child.OffsetTop(shift);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// CSS Box Alignment §6.1: justify-self on a block-level box — the explicit
    /// safe/unsafepath (incl. vertical containers) and the legacy justify-items
    /// /text-align resolution.
    /// </summary>
    private void ApplyBlockJustifySelf()
    {
        // CSS Box Alignment Level 3 §6.1: justify-self on block-level boxes.
        // When a non-replaced block has an explicit width narrower than its
        // containing block, 'justify-self' shifts the box horizontally within
        // the containing block's content area.  Values:
        //   auto/normal/stretch → default behaviour (no shift)
        //   start/flex-start/self-start/left → left-aligned (no shift in LTR)
        //   end/flex-end/self-end/right → right-aligned
        //   center → centered
        // Floated and absolutely/fixed positioned boxes are unaffected.
        //
        // 'auto' and 'normal' are not literally no-ops — they resolve:
        //   • justify-self:auto → the containing block's 'justify-items'
        //     (CSS Box Alignment §justify-self).
        //   • a still-unresolved 'normal'/'stretch' on a definite-width block →
        //     the parent's legacy 'text-align:-webkit-{left,right,center}' block
        //     alignment, if any (non-standard but widely supported; WPT
        //     css-align/blocks/justify-self-text-align exercises it).
        if (Float == CssConstants.None
            && Position != CssConstants.Absolute && Position != CssConstants.Fixed
            && (IsBlock || Display == CssConstants.ListItem)
            && ParentBox != null)
        {
            // CSS Box Alignment §5.3 + §6.1: an explicit overflow-alignment
            // keyword (safe/unsafe) on a block-level box. Unlike the legacy
            // path below, this handles the containing block's inline axis when
            // it is VERTICAL (writing-mode: vertical-*) — where justify-self
            // shifts the box along Y — and it honours overflow: `safe` clamps
            // to start when the box is larger than the alignment container,
            // while `unsafe` keeps the requested edge (allowing a negative
            // shift past the start edge). The keyword-less path is left
            // untouched below to avoid perturbing existing block layout.
            string rawJs = JustifySelf?.Trim().ToLowerInvariant() ?? "auto";

            if (rawJs.StartsWith("safe ", StringComparison.Ordinal)
                || rawJs.StartsWith("unsafe ", StringComparison.Ordinal))
            {
                bool explicitSafe = rawJs.StartsWith("safe ", StringComparison.Ordinal);
                string alignKw = StripSafeUnsafe(rawJs);

                if (alignKw is "center" or "end" or "flex-end" or "self-end" or "right"
                    or "start" or "flex-start" or "self-start" or "left")
                {
                    // When the box will be rotated by the vertical-flow transform
                    // (WillBeVerticalTransposed), layout is happening in the logical
                    // (horizontal) frame, so justify-self is applied along the
                    // logical inline axis (X) here and the transform rotates it onto
                    // the physical vertical axis. Only when the transform is NOT in
                    // play (prototype disabled) does a vertical container require a
                    // direct physical-Y shift.
                    bool containerVertical = IsVerticalWritingMode(ParentBox.WritingMode)
                        && !WillBeVerticalTransposed();

                    double boxSize = containerVertical
                        ? ActualBottom - Location.Y
                        : ActualRight - Location.X;

                    double marginStart = containerVertical ? ActualMarginTop : ActualMarginLeft;
                    double marginEnd = containerVertical ? ActualMarginBottom : ActualMarginRight;

                    // The vertical inline-axis extent must come from ActualHeight
                    // (the resolved content height), not ClientRectangle.Height:
                    // block-axis geometry resolves bottom-up, so the container's
                    // ActualBottom — and thus ClientRectangle.Height — is still 0
                    // when its in-flow child is being aligned.

                    double containerSize = containerVertical
                        ? ParentBox.ActualHeight
                        : ParentBox.ClientRectangle.Width;

                    double axisFree = containerSize - boxSize - marginStart - marginEnd;

                    // 'safe' falls back to 'start' when the box overflows.
                    if (explicitSafe && axisFree < 0)
                        alignKw = "start";

                    bool selfRtl = Direction == "rtl";
                    bool cbRtl = ParentBox?.Direction == "rtl";
                    double d = alignKw switch
                    {
                        "center" => axisFree / 2,
                        "end" or "flex-end" => cbRtl ? 0 : axisFree,
                        "self-end" => selfRtl ? 0 : axisFree,
                        "right" => axisFree,
                        "start" or "flex-start" => cbRtl ? axisFree : 0,
                        "self-start" => selfRtl ? axisFree : 0,
                        _ => 0, // left
                    };

                    if (Math.Abs(d) > 0.5)
                    {
                        if (containerVertical)
                            OffsetTop(d);
                        else
                            OffsetLeft(d);
                    }
                }

                // The keyword-less legacy path below is a no-op for an explicit
                // safe/unsafe value ("safe end" etc. is not a concrete keyword,
                // so it resolves to null there); fall through so any
                // position:relative offset later in this method still applies.
            }

            string js = JustifySelf?.Trim().ToLowerInvariant() ?? "auto";

            if (js == "auto")
                js = ParentBox.JustifyItems?.Trim().ToLowerInvariant() ?? "normal";

            if (js is "normal" or "stretch" or "auto" or "legacy")
            {
                js = (ParentBox.TextAlign?.Trim().ToLowerInvariant()) switch
                {
                    "-webkit-right" => "right",
                    "-webkit-center" => "center",
                    "-webkit-left" => "left",
                    _ => js,
                };
            }

            // Only a concrete edge/center alignment actually moves the box;
            // normal/stretch/baseline leave it at its in-flow position.
            if (js is not ("center" or "end" or "flex-end" or "self-end" or "right"
                or "start" or "flex-start" or "self-start" or "left"))
                js = null!;

            double boxWidth = ActualRight - Location.X;
            double containerWidth = ParentBox.ClientRectangle.Width;

            // Free space is what remains AFTER the box's own margins. Auto margins
            // are resolved during block layout (e.g. margin:auto centres the box by
            // splitting the free space), so they leave nothing here — which makes
            // 'justify-self' a no-op, per CSS Box Alignment §justify-abspos ("auto
            // margins make justify-self have no effect"). Accounting for margins
            // also keeps explicit-margin boxes aligned to the correct edge.
            double freeSpace = containerWidth - boxWidth
                - ActualMarginLeft - ActualMarginRight;

            if (js != null && freeSpace > 0.5)
            {
                // CSS Box Alignment §6.1: 'start'/'end' use the containing
                // block's writing direction; 'self-start'/'self-end' use the
                // element's own writing direction.
                bool isElementRtl = Direction == "rtl";
                bool isContainerRtl = ParentBox?.Direction == "rtl";

                double dx = 0;

                switch (js)
                {
                    case "center":
                        dx = freeSpace / 2;
                        break;

                    case "end":
                    case "flex-end":
                        dx = isContainerRtl ? 0 : freeSpace;
                        break;

                    case "self-end":
                        dx = isElementRtl ? 0 : freeSpace;
                        break;

                    case "right":
                        dx = freeSpace;
                        break;

                    case "start":
                    case "flex-start":
                        dx = isContainerRtl ? freeSpace : 0;
                        break;

                    case "self-start":
                        dx = isElementRtl ? freeSpace : 0;
                        break;

                    case "left":
                        dx = 0;
                        break;
                }

                if (dx > 0.5)
                    OffsetLeft(dx);
            }
        }
    }

    /// <summary>
    /// CSS2.1 §9.4.3: apply the visual position:relative offset after layout
    /// (doesnot affect flow).
    /// </summary>
    /// <summary>
    /// Applies <c>position: relative</c> offsets to the inline-level boxes inside this container's
    /// line boxes — the ones <see cref="PerformLayout"/> never sees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OffsetLeft"/> carries a shift into the box's own words, its line-box rectangles
    /// and its descendants, so a relative box nested inside another moves by both — which is what
    /// compounding offsets means, and it comes out the same whichever end of the chain is offset
    /// first, translations being additive. Each box's own offset is applied exactly once, because
    /// only the box that declares one is ever offset.
    /// </para>
    /// <para>
    /// Only <c>display: inline</c>, and the walk stops at everything else. A block-level box, a
    /// float, and an <c>inline-block</c> (or <c>inline-flex</c>/<c>inline-grid</c>) all get their
    /// own <see cref="PerformLayout"/> and apply their own offset there — an inline-block is
    /// inline-<em>level</em> but is laid out as a block, so including it applied the offset twice
    /// and moved it by double: <c>CSS2/margin-padding-clear/margin-collapse-001</c>'s reference
    /// stacks two <c>position: relative</c> inline-blocks and is what caught it. Such a box still
    /// moves with a relative <em>ancestor</em>, because <see cref="OffsetLeft"/> carries the
    /// ancestor's shift into every descendant regardless of display.
    /// </para>
    /// <para>
    /// <b>Horizontal containers only.</b> <c>left</c> and <c>top</c> are physical, but a vertical
    /// container's words are positioned in the engine's rotated space, so the offset would arrive
    /// turned a quarter turn: measured on <c>vertical-rl</c>, a <c>left: 60px; top: 30px</c> came
    /// out as a visual <c>(-30, +60)</c>, and on <c>vertical-lr</c> as <c>(+30, +60)</c>. Applying
    /// it there needs that mapping per writing mode — including the two <c>sideways-*</c> modes,
    /// which this has not measured — so a vertical container is left alone rather than moved
    /// wrongly. It is what happened before this existed, and
    /// <c>css-writing-modes/vrl-inline-paint-invalidation</c> is the pair that says so.
    /// </para>
    /// </remarks>
    private void OffsetRelativeInlineDescendants()
    {
        foreach (CssBox child in Boxes)
        {
            if (child.Display != CssConstants.Inline || child.Float != CssConstants.None
                || child.Position is CssConstants.Absolute or CssConstants.Fixed)
            {
                continue;
            }

            child.OffsetRelativeInlineDescendants();
            child.ApplyRelativePositionOffset();
        }
    }

    private void ApplyRelativePositionOffset()
    {
        // Apply position:relative offset after layout (visual only, does not affect flow)
        // CSS2.1 §9.4.3: For relative positioning, 'left'/'right' and
        // 'top'/'bottom' form constraint pairs.  When 'top' is auto and
        // 'bottom' is not, dy = -bottom.  When both are non-auto, 'bottom'
        // is ignored (in LTR).  Same logic applies to left/right.
        if (Position == CssConstants.Relative)
        {
            double dx = 0, dy = 0;

            bool hasLeft = Left != null && Left != CssConstants.Auto;
            bool hasRight = Right != null && Right != CssConstants.Auto;
            bool hasTop = Top != null && Top != CssConstants.Auto;
            bool hasBottom = Bottom != null && Bottom != CssConstants.Auto;

            if (hasLeft)
                dx = ParseUsedLength(Left, Size.Width, percentAgainstContainingBlock: false);
            else if (hasRight)
                dx = -ParseUsedLength(Right, Size.Width, percentAgainstContainingBlock: false);

            if (hasTop)
                dy = ParseUsedLength(Top, Size.Height, percentAgainstContainingBlock: false);
            else if (hasBottom)
                dy = -ParseUsedLength(Bottom, Size.Height, percentAgainstContainingBlock: false);

            if (dx != 0)
                OffsetLeft(dx);

            if (dy != 0)
                OffsetTop(dy);
        }
    }
}
