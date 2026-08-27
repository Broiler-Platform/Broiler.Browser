using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal static class CssLayoutEngine
{
    /// <summary>
    /// Returns true when <paramref name="box"/> or any of its ancestors
    /// (up to but not including <paramref name="stop"/>) has
    /// <c>position:absolute</c> or <c>position:fixed</c>.
    /// </summary>
    private static bool IsInAbsposSubtree(CssBox box, CssBox stop)
    {
        for (var b = box; b != null && b != stop; b = b.ParentBox)
        {
            if (b.Position == CssConstants.Absolute || b.Position == CssConstants.Fixed)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Approximate ratio of font ascent to total font height for typical
    /// Latin fonts.  Used to compute baseline position when full font
    /// metrics are not directly available (CSS2.1 §10.8 strut).
    /// </summary>
    private const double TypicalAscentRatio = 0.8;

    /// <summary>
    /// Ratio to convert typographic points to CSS pixels (96 DPI / 72 DPI).
    /// Layout coordinates are in CSS px, but font metrics from the layout
    /// font are at pt-scale (the layout font is created in canvas units, and
    /// the layout font is created at pt size).  This factor bridges the gap
    /// for line-height calculations where font.Height is the fallback.
    /// </summary>
    private const double PtToCssPx = CssMetrics.PtToPx;

    /// <summary>
    /// Resolves a replaced element's specified width/height to a definite pixel
    /// length when it is neither <c>auto</c>, a percentage, nor an intrinsic-size
    /// keyword. Unlike a raw <see cref="CssLength"/> pixel check this resolves
    /// font- and viewport-relative units (em/rem, vw/vh, …) through the length
    /// parser, so e.g. <c>block-size: 55vw</c> sizes an image. Percentages are
    /// excluded here because they resolve against the containing block, which the
    /// caller handles separately.
    /// </summary>
    internal static bool TryResolveDefiniteImageLength(string value, double em, out double px)
    {
        px = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string t = value.Trim();

        // Only a numeric length (or calc()) is a definite tag size. Reject every
        // keyword size — auto, none, min/max/fit-content, stretch,
        // fill-available, … — which resolves against layout, not to a fixed px
        // value here (and a percentage, which the caller resolves against the
        // containing block). A definite length always begins with a digit, sign,
        // or decimal point.
        char c0 = t[0];
        bool looksNumeric = char.IsDigit(c0) || c0 == '+' || c0 == '-' || c0 == '.';
        bool isCalc = t.StartsWith("calc(", StringComparison.OrdinalIgnoreCase);

        if (!(looksNumeric || isCalc) || t.EndsWith('%'))
            return false;

        double v = CssLengthParser.ParseLength(t, 0, em);

        if (double.IsNaN(v) || double.IsInfinity(v) || v < 0)
            return false;

        px = v;

        return true;
    }

    /// <summary>
    /// Whether the block-size basis a percentage on <paramref name="box"/> would resolve against
    /// lies beyond a grid area — in which case there is no basis to use here.
    /// </summary>
    /// <remarks>
    /// A grid item's block size comes from the track that holds it, and its descendants' percentage
    /// heights resolve against that grid area. Broiler does not model a grid area as a containing
    /// block, so the basis walk goes straight past the grid to the next ancestor that states a
    /// height — which is a different, usually larger, number. An `img { height: 100% }` in a `1fr`
    /// row of a 200px grid would resolve to 200 rather than to the row's 100
    /// (css-grid/replaced-element-percentage-height-in-grid-nested-in-flex-001). Declining leaves
    /// the image at its natural size, which is what it had before any of this resolved at all.
    /// </remarks>
    private static bool PercentageBlockBasisCrossesAGridArea(CssBox box)
    {
        for (var cb = box.ContainingBlock; cb?.ParentBox != null; cb = cb.ContainingBlock)
        {
            bool heightIsAuto = cb.Height == CssConstants.Auto || string.IsNullOrEmpty(cb.Height);

            // A stated, non-percentage height is the basis the walk would stop at, so nothing
            // beyond it matters.
            if (!heightIsAuto && !cb.Height.Contains('%'))
                return false;

            if (cb.ParentBox.Display is "grid" or "inline-grid")
                return true;
        }

        return false;
    }

    public static void MeasureImageSize(ILayoutEnvironment g, CssRectImage imageWord)
    {
        ArgumentNullException.ThrowIfNull(imageWord);
        ArgumentNullException.ThrowIfNull(imageWord.OwnerBox);

        // Phase 2b: replaced-element intrinsics come from the layout environment
        // rather than reading RImage members directly (see roadmap §4).
        ImageIntrinsics? image = imageWord.Image is { } handle ? g.GetImageIntrinsics(handle) : null;

        // HTML §4.8.4.3: a `srcset` candidate states the density it is meant to be shown at, and the
        // element's natural size is the decoded bitmap divided by it — a `2x` candidate is laid out
        // at half the pixels it decodes to, and a `100w` candidate in a `sizes="400px"` slot at four
        // times them. `PixelDensity` is 1 for every image that did not come from a candidate list,
        // which leaves this arithmetic an identity for them. An infinite density (a candidate
        // selected against a zero-width slot) gives the zero-sized image the spec asks for.
        image = ApplyPixelDensity(image, imageWord.PixelDensity);

        // CSS Containment 2 §3.2: a size-contained replaced element "must be treated as having no
        // natural dimensions and no natural aspect ratio". Dropping the decoded bitmap here is the
        // whole of that: the auto branches below fall to the contained stand-ins, and `usedRatio`
        // has nothing left to derive one axis from the other with. An author's own `width`,
        // `height` or `aspect-ratio` still applies — containment hides the contents, not the box's
        // own declarations.
        bool sizeContained = imageWord.OwnerBox.AppliesSizeContainment;

        if (sizeContained)
            image = null;

        double em = imageWord.OwnerBox.GetEmHeight();

        // A specified, non-percentage size counts as a "tag" size — but resolve it
        // through the length parser rather than only accepting raw pixels, so
        // font- and viewport-relative units (em/rem, vw/vh, …) that map onto an
        // image's width/height (including the logical block-size/inline-size, e.g.
        // `block-size: 55vw`) size the image instead of silently falling through to
        // its intrinsic size. (WPT css-grid/nested-grid-item-block-size-001.)
        bool hasImageTagWidth = TryResolveDefiniteImageLength(imageWord.OwnerBox.Width, em, out double tagWidthPx);
        bool hasImageTagHeight = TryResolveDefiniteImageLength(imageWord.OwnerBox.Height, em, out double tagHeightPx);

        // A percentage width is a stated width too — resolved against the containing block rather
        // than read off the declaration, which is the only reason it is not a "tag" width here.
        // The aspect-ratio pass below must not treat it as `auto` and derive it back from the
        // height, or `<img width="100%" height="50">` comes out 50px wide.
        bool hasStatedWidth = hasImageTagWidth;

        if (hasImageTagWidth)
        {
            imageWord.Width = tagWidthPx;
        }
        else
        {
            // Parse the width as a CssLength only here: it is unused when a definite
            // tag width was resolved above, so this avoids a redundant parse per
            // sized image. (The percentage branch needs the raw fraction, which the
            // definite-length resolver above deliberately rejects.)
            var width = new CssLength(imageWord.OwnerBox.Width);
            if (width.Number > 0 && width.IsPercentage)
            {
                imageWord.Width = width.Number * imageWord.OwnerBox.ContainingBlock.Size.Width;

                // CSS 2.1 §10.4 uses the intrinsic ratio to fill in a dimension that is `auto`, not
                // to overrule one the author stated — and a percentage width is no different from a
                // length one in that. Treating it as auto made `<img width="100%" height="50">`
                // come out as tall as it was wide. That is the shape CSS2's own reference files use,
                // so the cost landed on the reference side of 85 reftests in css/CSS2/backgrounds
                // alone.
                hasStatedWidth = true;
            }
            else if (image != null)
            {
                imageWord.Width = imageWord.ImageRectangle == RectangleF.Empty
                    ? image.Value.Width
                    : imageWord.ImageRectangle.Width / imageWord.PixelDensity;

                // CSS2.1 §10.3.2: when width is auto the used value is the
                // intrinsic width.  Do NOT clamp to the containing block —
                // inline replaced elements are allowed to overflow their
                // container.  Authors use max-width:100% to opt into clamping.
            }
            else if (sizeContained)
            {
                imageWord.Width = imageWord.OwnerBox.ContainedIntrinsicContentWidth;
            }
            else
            {
                imageWord.Width = hasImageTagHeight ? tagHeightPx / 1.14f : 20;
            }
        }

        // A percentage height is a stated height too, on exactly the footing the percentage *width*
        // branch above puts one: it resolves against the nearest ancestor that states a definite
        // block size, and the ratio then carries it to the inline axis. Nothing resolved it — the
        // definite-length resolver rejects percentages by design — so `img { height: 100% }` kept
        // its bitmap's height, and with it its bitmap's width. Every box sized from that image's
        // intrinsic contribution then came out the bitmap's width: a `float` around a 200x200 image
        // asked to be 100px tall is 100px wide, not 200 (css-sizing/intrinsic-percent-replaced-*,
        // 40 of its 45 tests, and the shrink-to-fit half of the same rule in css-flexbox).
        double percentHeightPx = 0;
        bool hasPercentageHeight =
            !hasImageTagHeight
            && imageWord.OwnerBox.Height is { Length: > 0 } blockSize
            && blockSize.Contains('%')
            && !PercentageBlockBasisCrossesAGridArea(imageWord.OwnerBox)
            && imageWord.OwnerBox.TryResolveSpecifiedReplacedContentHeight(out percentHeightPx);

        bool hasStatedHeight = hasImageTagHeight || hasPercentageHeight;

        if (hasImageTagHeight)
        {
            imageWord.Height = tagHeightPx;
        }
        else if (hasPercentageHeight)
        {
            imageWord.Height = percentHeightPx;
        }
        else if (image != null)
        {
            imageWord.Height = imageWord.ImageRectangle == RectangleF.Empty
                ? image.Value.Height
                : imageWord.ImageRectangle.Height / imageWord.PixelDensity;
        }
        else if (sizeContained)
        {
            imageWord.Height = imageWord.OwnerBox.ContainedIntrinsicContentHeight;
        }
        else
        {
            imageWord.Height = imageWord.Width > 0 ? imageWord.Width * 1.14f : 22.8f;
        }

        // CSS Sizing 4 §4: an explicit `aspect-ratio` on a replaced element with a
        // natural aspect ratio overrides that natural ratio for computing the
        // dimension left `auto`. Derive the missing side from the specified one
        // through the CSS ratio (width/height); fall back to the intrinsic image
        // ratio when no `aspect-ratio` is declared. (WPT css-grid/
        // nested-grid-item-block-size-001: `aspect-ratio: 2/1` + `block-size: 55vw`.)
        bool hasCssAspectRatio =
            CssBox.TryParseAspectRatio(imageWord.OwnerBox.AspectRatio, out double cssAspectRatio)
            && cssAspectRatio > 0;

        // The ratio the box is sized by: the author's preferred one when declared, otherwise the
        // image's natural one. Zero when the box has neither, which leaves the two axes independent.
        // HTML §14.4: a size-contained replaced element has no natural ratio left, but the UA
        // stylesheet's `aspect-ratio: attr(width) / attr(height)` is a declaration and survives —
        // so `<img width=60 height=60 style="width: 100px; height: auto; contain: size">` is still
        // square (WPT css-contain/contain-size-replaced-007, whose assert says exactly that).
        double usedRatio = hasCssAspectRatio
            ? cssAspectRatio
            : image is { HasIntrinsicRatio: true, Height: > 0 } natural ? natural.Width / natural.Height
            : sizeContained && imageWord.OwnerBox.TryGetContainedPresentationalRatio(out double attributeRatio)
                ? attributeRatio
                : 0;

        bool widthDriven = hasStatedWidth && !hasStatedHeight;

        if (usedRatio > 0)
        {
            // If only the width was stated, the ratio fills in the height, and vice versa.
            if (widthDriven)
                imageWord.Height = imageWord.Width / usedRatio;
            else if (hasStatedHeight && !hasStatedWidth)
                imageWord.Width = imageWord.Height * usedRatio;
        }

        // CSS2.1 §10.4/§10.7: apply the min/max constraints to the tentative size settled above.
        // The two axes are coupled through the ratio, so this cannot be four independent clamps —
        // see ReplacedBoxSizing for the table §10.4 resolves a double violation with.
        double usedWidth = imageWord.Width;
        double usedHeight = imageWord.Height;

        ReplacedBoxSizing.ApplyMinMax(
            ref usedWidth, ref usedHeight,
            widthIsAuto: !hasStatedWidth, heightIsAuto: !hasStatedHeight, usedRatio,
            imageWord.OwnerBox.ResolveInlineSizeBounds(),
            imageWord.OwnerBox.ResolveBlockSizeBounds());

        imageWord.Width = usedWidth;
        imageWord.Height = usedHeight;

        imageWord.Height += imageWord.OwnerBox.ActualBorderBottomWidth + imageWord.OwnerBox.ActualBorderTopWidth + imageWord.OwnerBox.ActualPaddingTop + imageWord.OwnerBox.ActualPaddingBottom;
    }

    /// <summary>
    /// The decoded bitmap's dimensions divided by the density its <c>srcset</c> candidate was
    /// selected at — HTML §4.8.4.3's <b>density-corrected natural width and height</b>. The aspect
    /// ratio is a quotient of the two and so is untouched by a uniform scale, which is what keeps
    /// this from disturbing the replaced-sizing path for the overwhelming majority of images, whose
    /// density is exactly 1.
    /// </summary>
    private static ImageIntrinsics? ApplyPixelDensity(ImageIntrinsics? image, double density)
    {
        if (image is not { } intrinsics || density == 1.0 || double.IsNaN(density) || density <= 0)
            return image;

        return intrinsics with
        {
            Width = intrinsics.Width / density,
            Height = intrinsics.Height / density,
        };
    }

    public static void CreateLineBoxes(ILayoutEnvironment g, CssBox blockBox)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(blockBox);

        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.LineBreak);

        blockBox.LineBoxes.Clear();

        double limitRight = blockBox.ActualRight - blockBox.ActualPaddingRight - blockBox.ActualBorderRightWidth;

        //Get the start x and y of the blockBox
        double startx = blockBox.Location.X + blockBox.ActualPaddingLeft - 0 + blockBox.ActualBorderLeftWidth;
        double starty = blockBox.Location.Y + blockBox.ActualPaddingTop - 0 + blockBox.ActualBorderTopWidth;

        // CSS2.1 §9.5: the floats this block's lines have to share their width with. Collected
        // once per pass because every line consults them and the geometry does not change while
        // the block flows.
        blockBox.LineFloatBands = LineFloatBands.For(blockBox);

        double firstLineHeight = blockBox.ActualLineHeight > 0
            ? blockBox.ActualLineHeight
            : blockBox.ActualFont.Height * PtToCssPx;

        double curx = BandLeftAt(blockBox, starty, firstLineHeight, startx) + blockBox.ActualTextIndent;
        double cury = starty;

        //Reminds the maximum bottom reached
        double maxRight = startx;
        double maxBottom = starty;

        //First line box
        CssLineBox line = new(blockBox);

        //Flow words and boxes
        FlowBox(g, blockBox, blockBox, limitRight, 0, startx, ref line, ref curx, ref cury, ref maxRight, ref maxBottom);

        DropTrailingForcedBreakLine(blockBox);

        // if width is not restricted we need to lower it to the actual width
        if (blockBox.ActualRight >= 90999)
        {
            blockBox.ActualRight = maxRight + blockBox.ActualPaddingRight + blockBox.ActualBorderRightWidth;
        }

        //Gets the rectangles for each line-box
        bool plaintext = string.Equals(blockBox.UnicodeBidi, "plaintext", StringComparison.OrdinalIgnoreCase);
        // CSS Text §bidi-linebox: under unicode-bidi:plaintext each line is its own
        // bidi paragraph.  Its base direction is the first strong character's; a line
        // with no strong character inherits the previous paragraph's base direction,
        // or the containing block's direction when there is none.  Otherwise every
        // line shares the block's own direction.  Because a <br> splits content into
        // sibling anonymous blocks, the "previous paragraph" may live in an earlier
        // sibling — seed the running base direction from the most recent strong
        // character preceding this block in document order.
        bool baseRtl = plaintext
            ? SeedPlaintextBaseRtl(blockBox)
            : blockBox.Direction == CssConstants.Rtl;

        foreach (var linebox in blockBox.LineBoxes)
        {
            bool lineRtl = baseRtl;
            if (plaintext)
            {
                lineRtl = LineFirstStrongRtl(linebox) ?? baseRtl;
                baseRtl = lineRtl;
            }

            ApplyHorizontalAlignment(linebox, lineRtl);
            ApplyRightToLeft(linebox, lineRtl);
            BubbleRectangles(blockBox, linebox);
            ApplyVerticalAlignment(linebox);

            linebox.AssignRectanglesToBoxes();
        }

        // CSS2.1 §10.8: After vertical alignment adjusts inline-block
        // positions (e.g. vertical-align: 2em raises boxes), recalculate
        // maxBottom from the actual post-alignment positions.
        //
        // CSS2.1 §10.8.1: The line box height is the distance between
        // the uppermost box top and the lowermost box bottom.  When
        // positive vertical-align raises inline-blocks above the flow
        // start, the line box extends upward.  The full line box height
        // must be reflected in the block's content height so that
        // subsequent siblings are positioned correctly.
        //
        // Example: Acid3's .buckets div has font: 0/0 (baseline at
        // content edge) and bucket6 extends 162px above the baseline.
        // The line box height is 162px, so the div's auto height = 162px
        // (plus padding/border).
        maxBottom = starty;
        double minTop = starty;

        foreach (var linebox in blockBox.LineBoxes)
        {
            foreach (var rect in linebox.Rectangles)
            {
                // CSS2.1 §9.6.1: Absolutely/fixed positioned elements are
                // out of normal flow and must not affect the line box height.
                if (IsInAbsposSubtree(rect.Key, blockBox))
                    continue;

                maxBottom = Math.Max(maxBottom, InlineRectLineBoxBottom(rect.Key, rect.Value));
                // CSS2.1 §10.8: an atomic inline-block contributes its *margin*
                // box plus the line's strut descent below the baseline to the
                // line-box height.  InlineRectLineBoxBottom returns only the
                // border box (its rectangle excludes the bottom margin), so for
                // an *anonymous* block — which has no visual box of its own and
                // exists purely to position the next block-level sibling (e.g.
                // the anonymous wrappers a <br> splits inline content into) —
                // extend its content height to the true line-box bottom.  This
                // mirrors what the inline-block *wrap* path (FlowInlineBlock)
                // already does in-scope, so a <br>-separated row lands where a
                // wrapped one does.  Restricted to anonymous blocks to avoid
                // changing the rendered height of author boxes.
                if (blockBox.Kind == BoxKind.Anonymous
                    && (rect.Key.Display == CssConstants.InlineBlock
                        || rect.Key.Display is "inline-flex" or "inline-grid"))
                {
                    double lineStrut = blockBox.ActualLineHeight > 0
                        ? blockBox.ActualLineHeight
                        : blockBox.ActualFont.Height * PtToCssPx;
                    double marginBoxBottom = rect.Value.Bottom + rect.Key.ActualMarginBottom
                        + lineStrut * (1.0 - TypicalAscentRatio);

                    maxBottom = Math.Max(maxBottom, marginBoxBottom);
                }

                minTop = Math.Min(minTop, rect.Value.Top);
            }

            foreach (var word in linebox.Words)
            {
                if (IsInAbsposSubtree(word.OwnerBox, blockBox))
                    continue;

                maxBottom = Math.Max(maxBottom, InlineWordLineBoxBottom(word));

                // CSS2.1 §10.8: a baseline-aligned inline replaced element (image)
                // sits with its bottom on the baseline, so the line box still
                // extends below it by the strut's below-baseline descent.
                // InlineWordLineBoxBottom returns only the image bottom (the
                // baseline), so add that descent here — mirroring the inline-block
                // path above. Without it, a line whose height is set by a tall
                // image drops the text descent and every following line creeps up
                // (CSS2 visudet/replaced-elements-*).
                if (word.IsImage
                    && (word.OwnerBox == null
                        || string.IsNullOrEmpty(word.OwnerBox.VerticalAlign)
                        || word.OwnerBox.VerticalAlign == CssConstants.Baseline))
                {
                    double lineStrut = blockBox.ActualLineHeight > 0
                        ? blockBox.ActualLineHeight
                        : blockBox.ActualFont.Height * PtToCssPx;

                    maxBottom = Math.Max(maxBottom,
                        word.Bottom + ImageWordMarginBottom(word)
                        + lineStrut * (1.0 - TypicalAscentRatio));
                }

                minTop = Math.Min(minTop, word.Top - (word.IsImage ? ImageWordMarginTop(word) : 0));
            }

            if (blockBox.ActualLineHeight > 0)
            {
                double lineTop = double.MaxValue;
                bool hasLineContent = false;

                foreach (var rect in linebox.Rectangles)
                {
                    if (IsInAbsposSubtree(rect.Key, blockBox))
                        continue;

                    lineTop = Math.Min(lineTop, rect.Value.Top);
                    hasLineContent = true;
                }

                foreach (var word in linebox.Words)
                {
                    if (IsInAbsposSubtree(word.OwnerBox, blockBox))
                        continue;

                    lineTop = Math.Min(lineTop, word.Top);
                    hasLineContent = true;
                }

                if (hasLineContent)
                    maxBottom = Math.Max(maxBottom, lineTop + blockBox.ActualLineHeight);
            }
        }

        // CSS2.1 §10.8.1: The line box height is the distance between
        // the uppermost box top and the lowermost box bottom.  When
        // inline-level boxes overflow above the starting flow position
        // (minTop < starty), the full line box height must be reflected
        // in maxBottom so subsequent siblings are positioned correctly.
        if (minTop < starty)
        {
            double lineBoxHeight = maxBottom - minTop;
            maxBottom = Math.Max(maxBottom, starty + lineBoxHeight);

            // CSS2.1 §9.4.2: Line boxes are laid out beginning at the
            // top of the containing block.  When vertical-align raises
            // inline-blocks above the flow start, the entire line box
            // content must be shifted downward so it renders within the
            // block container's content area (from starty to
            // starty + lineBoxHeight) instead of overflowing above.
            // The shift amount is computed from the global minTop across
            // ALL line boxes in the block (lines 162-176), so it must be
            // applied uniformly to all line boxes.
            double shift = starty - minTop;
            foreach (var linebox in blockBox.LineBoxes)
            {
                // Shift line box rectangle positions
                var keys = new List<CssBox>(linebox.Rectangles.Keys);
                foreach (var box in keys)
                {
                    var r = linebox.Rectangles[box];
                    linebox.Rectangles[box] = new RectangleF(r.X, (float)(r.Y + shift), r.Width, r.Height);

                    // For inline-block boxes, also update the CssBox's
                    // own Location and ActualBottom (used by the paint system).
                    if (box.Display == CssConstants.InlineBlock)
                    {
                        box.Location = new PointF(box.Location.X, (float)(box.Location.Y + shift));
                        box.ActualBottom += shift;
                    }

                    // Update the box's own Rectangles copy (assigned
                    // earlier by AssignRectanglesToBoxes).
                    if (box.Rectangles.ContainsKey(linebox))
                        box.Rectangles[linebox] = linebox.Rectangles[box];
                }

                // Shift word positions
                foreach (var word in linebox.Words)
                    word.Top += shift;
            }
        }

        // CSS2.1 §10.8: The "strut" — each line box starts with an
        // imaginary zero-width inline box with the block container's font
        // and line-height properties.  This establishes the minimum line
        // box height for inline formatting contexts.
        // The strut only affects content height when height is 'auto';
        // an explicit height (CSS2.1 §10.6.3) overrides the content height.
        // CSS2.1 §9.4.2: The strut only contributes to height when the
        // inline formatting context has actual inline content (words or
        // inline-level boxes).  An empty block should have zero content
        // height from the IFC.
        bool hasExplicitHeight = blockBox.Height != null && blockBox.Height != CssConstants.Auto;
        bool hasInlineContent = false;
        foreach (var lb in blockBox.LineBoxes)
        {
            // CSS2.1 §9.6.1: Words and rectangles from absolutely/fixed
            // positioned elements are not in-flow inline content.
            foreach (var w in lb.Words)
            {
                if (!IsInAbsposSubtree(w.OwnerBox, blockBox))
                {
                    hasInlineContent = true;
                    break;
                }
            }

            if (hasInlineContent) break;

            foreach (var r in lb.Rectangles)
            {
                if (!IsInAbsposSubtree(r.Key, blockBox))
                {
                    hasInlineContent = true;
                    break;
                }
            }

            if (hasInlineContent) break;
        }

        if (blockBox.ActualLineHeight > 0 && !hasExplicitHeight && hasInlineContent)
            maxBottom = Math.Max(maxBottom, starty + blockBox.ActualLineHeight);

        blockBox.ActualBottom = maxBottom + blockBox.ActualPaddingBottom + blockBox.ActualBorderBottomWidth;

        // CSS2.1 §10.6.3: When height is not 'auto', the used value is the
        // specified value.  Content may overflow (controlled by 'overflow').
        // For overflow:hidden, overflow:auto, and overflow:scroll the box's
        // layout height is clamped to the specified height so that subsequent
        // siblings are not pushed down by overflowing content.
        if (hasExplicitHeight
            && blockBox.Overflow is CssConstants.Hidden or CssConstants.Auto or CssConstants.Scroll
            && blockBox.ActualBottom - blockBox.Location.Y > blockBox.ActualHeight)
            blockBox.ActualBottom = blockBox.Location.Y + blockBox.ActualHeight;

        // CSS2.1 §9.6.1 / §10.3.7: An out-of-flow (absolutely/fixed positioned)
        // descendant of this inline formatting context was flowed by FlowBox only
        // to establish its static line-box rectangle; it never received its own
        // PerformLayout, so its used size and inset-based position are unresolved.
        // Now that the context's line rectangles are assigned (so an inline
        // containing block such as a position:relative <span> has real geometry),
        // lay those boxes out. Without this an abspos inside an inline CB reports
        // its static line position instead of its top/left inset box.
        LayoutOutOfFlowInlineDescendants(g, blockBox);
    }

    /// <summary>
    /// CSS Text 3 §4.1: a forced line break at the <em>end</em> of a block
    /// generates no line box of its own. Removes the trailing line if the flow
    /// left one holding nothing but preserved newlines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A preserved <c>\n</c> is flowed as a zero-width word that opens the next
    /// line and is then reported onto it, so text ending in one leaves a final
    /// line box whose only content is that break — and the block was sized a
    /// whole line taller than every browser renders it. The engine already got
    /// this right for a trailing <c>&lt;br&gt;</c>, which is the same rule; only
    /// the <c>white-space: pre</c> spelling of it was wrong.
    /// </para>
    /// <para>
    /// Only the last line, only when something precedes it, and only when that
    /// something is real content. A break is only "at the end of the block" if
    /// the block had a line to end — a container whose entire content is one
    /// preserved newline still occupies a line box, and that line carries its
    /// inline's line-height (<c>quirks/line-height-preserved-segment-break</c>
    /// makes a 100px-line-height span holding nothing but a newline fill a
    /// 100px box). An interior empty line is content too: <c>"a\n\nb"</c> is
    /// three lines and stays three.
    /// </para>
    /// </remarks>
    private static void DropTrailingForcedBreakLine(CssBox blockBox)
    {
        if (blockBox.LineBoxes.Count < 2)
            return;

        var last = blockBox.LineBoxes[^1];

        if (last.Words.Count == 0 || last.Rectangles.Count > 0)
            return;

        foreach (var word in last.Words)
        {
            if (!word.IsLineBreak)
                return;
        }

        bool precededByContent = false;
        for (int i = 0; i < blockBox.LineBoxes.Count - 1 && !precededByContent; i++)
            precededByContent = blockBox.LineBoxes[i].Words.Count > 0
                || blockBox.LineBoxes[i].Rectangles.Count > 0;

        if (!precededByContent)
            return;

        blockBox.LineBoxes.RemoveAt(blockBox.LineBoxes.Count - 1);
    }

    /// <summary>
    /// Lays out the absolutely/fixed positioned boxes that hang off an inline
    /// formatting context established by <paramref name="ifcRoot"/>. FlowBox
    /// descends the inline subtree to establish each out-of-flow box's static
    /// position but does not run its block layout (size + inset position); this
    /// walk does, mirroring how the block path lays out its out-of-flow children
    /// via <see cref="CssBox.PerformLayout"/>. It descends only through in-flow,
    /// non-atomic inline boxes — the boxes FlowBox itself entered — because
    /// floats, atomic inlines (inline-block/-flex/-grid/-table), and block-level
    /// boxes run their own layout, which already resolves their out-of-flow
    /// descendants.
    /// </summary>
    private static void LayoutOutOfFlowInlineDescendants(ILayoutEnvironment g, CssBox ifcRoot)
    {
        foreach (var child in ifcRoot.Boxes)
            LayoutOutOfFlowInlineDescendantsCore(g, child);
    }

    private static void LayoutOutOfFlowInlineDescendantsCore(ILayoutEnvironment g, CssBox box)
    {
        if (box.Display == CssConstants.None)
            return;

        if (box.Position == CssConstants.Absolute || box.Position == CssConstants.Fixed)
        {
            // PerformLayout resolves the box's own size + inset position and
            // recurses into its subtree, so do not descend past it here.
            box.PerformLayout(g);
            return;
        }

        // Only plain inline (and anonymous inline) boxes are part of this inline
        // formatting context. Floats and atomic/block boxes establish their own
        // layout and must not be re-entered here. Anonymous *block* wrappers (from
        // a block-in-inline split) run their own CreateLineBoxes, so excluding
        // IsBlock avoids laying their out-of-flow descendants out twice.
        if (box.Float != CssConstants.None || box.IsBlock)
            return;

        if (box.Display == CssConstants.Inline || box.Kind == BoxKind.Anonymous)
        {
            foreach (var child in box.Boxes)
                LayoutOutOfFlowInlineDescendantsCore(g, child);
        }
    }

    public static void ApplyCellVerticalAlignment(ILayoutEnvironment g, CssBox cell)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(cell);

        if (cell.VerticalAlign == CssConstants.Top || cell.VerticalAlign == CssConstants.Baseline)
            return;

        double cellbot = cell.ClientBottom;
        double bottom = CssBoxHelper.GetMaximumBottom(cell, 0f);
        double dist = 0f;

        if (cell.VerticalAlign == CssConstants.Bottom)
        {
            dist = cellbot - bottom;
        }
        else if (cell.VerticalAlign == CssConstants.Middle)
        {
            dist = (cellbot - bottom) / 2;
        }

        // CSS Box Alignment §6.2: When align-content is 'normal' on a
        // table cell, vertical-align maps to safe alignment.  If the
        // content overflows the cell (dist < 0), safe alignment clamps
        // to start (top), preventing negative shifts.
        if (dist < 0 && (cell.AlignContent == null || cell.AlignContent == "normal"))
            dist = 0;

        foreach (CssBox b in cell.Boxes)
        {
            b.OffsetTop(dist);
        }
    }

    /// <summary>
    /// CSS Box Alignment §6.2: aligns a table cell's in-flow content along the
    /// block axis from the cell's explicit <c>align-content</c> (overriding the
    /// vertical-align mapping), falling back to <see cref="ApplyCellVerticalAlignment"/>
    /// when align-content is absent/<c>normal</c>. Used for cells whose final height
    /// is only known after the row-height pass — notably rowspan cells whose trailing
    /// rows are collapsed/empty, where the per-box align-content pass in
    /// <c>CssBox.PerformLayout</c> ran while the cell was still content-sized
    /// (zero free space) and therefore did nothing.
    /// </summary>
    public static void ApplyCellContentAlignment(ILayoutEnvironment g, CssBox cell)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(cell);

        string ac = cell.AlignContent?.Trim() ?? string.Empty;
        if (ac.Length == 0 || ac.Equals(CssConstants.Normal, StringComparison.OrdinalIgnoreCase))
        {
            ApplyCellVerticalAlignment(g, cell);
            return;
        }

        bool isUnsafe = ac.StartsWith("unsafe ", StringComparison.OrdinalIgnoreCase);
        bool isSafe = ac.StartsWith("safe ", StringComparison.OrdinalIgnoreCase);
        string kw = (isUnsafe ? ac[7..] : isSafe ? ac[5..] : ac).Trim().ToLowerInvariant();

        double free = cell.ClientBottom - CssBoxHelper.GetMaximumBottom(cell, 0f);
        double shift = kw switch
        {
            "center" or "space-around" or "space-evenly" => free / 2,
            "end" or "flex-end" => free,
            // start / flex-start / baseline / space-between / unknown → start edge.
            _ => 0,
        };

        // CSS Box Alignment §5.3: overflow alignment defaults to 'safe' (clamp to
        // the start edge) unless 'unsafe' is requested.
        if (!isUnsafe && shift < 0)
            shift = 0;

        if (Math.Abs(shift) <= 0.5)
            return;

        foreach (CssBox b in cell.Boxes)
        {
            if (b.Position == CssConstants.Absolute || b.Position == CssConstants.Fixed)
                continue;

            if (b.Display == CssConstants.None)
                continue;

            b.OffsetTop(shift);
        }
    }

    /// <summary>
    /// The left edge available to a line of <paramref name="blockbox"/> at <paramref name="top"/>,
    /// which is <paramref name="contentLeft"/> unless a left float in the block formatting context
    /// reaches that line (CSS2.1 §9.5).
    /// </summary>
    private static double BandLeftAt(CssBox blockbox, double top, double lineHeight, double contentLeft) =>
        blockbox.LineFloatBands is { IsEmpty: false } bands
            ? bands.LeftAt(top, lineHeight, contentLeft)
            : contentLeft;

    /// <summary>
    /// The right edge available to a line of <paramref name="blockbox"/> at <paramref name="top"/>.
    /// </summary>
    /// <remarks>
    /// A block still being measured at an unrestricted width (the shrink-to-fit sentinel that
    /// <see cref="CreateLineBoxes"/> narrows afterwards) has no meaningful right edge to subtract a
    /// float from, so it keeps the sentinel and wraps only where its content says.
    /// </remarks>
    private static double BandRightAt(CssBox blockbox, double top, double lineHeight, double contentRight) =>
        contentRight < 90999 && blockbox.LineFloatBands is { IsEmpty: false } bands
            ? bands.RightAt(top, lineHeight, contentRight)
            : contentRight;

    /// <summary>
    /// CSS2.1 §9.5: moves a line down past the floats beside it when <paramref name="needed"/>
    /// does not fit in the band there, and returns the y it settles at. Each step drops to the
    /// bottom of the shallowest float still in the way, so the walk is bounded by the float count.
    /// </summary>
    private static double DropLineBelowNarrowBands(
        CssBox blockbox,
        double top,
        double lineHeight,
        double contentLeft,
        double contentRight,
        double needed)
    {
        if (needed <= 0 || contentRight >= 90999 || blockbox.LineFloatBands is not { IsEmpty: false } bands)
            return top;

        // The word may be wider than the block itself, in which case no drop can help and the
        // line overflows where it is — the same answer the un-floated path gives.
        if (needed > contentRight - contentLeft)
            return top;

        for (int step = 0; step < 64; step++)
        {
            double left = bands.LeftAt(top, lineHeight, contentLeft);
            double right = bands.RightAt(top, lineHeight, contentRight);

            if (right - left >= needed)
                return top;

            double next = bands.NextBandBottom(top, lineHeight);

            if (double.IsNaN(next) || next <= top)
                return top;

            top = next;
        }

        return top;
    }

    private static void FlowBox(ILayoutEnvironment g, CssBox blockbox, CssBox box, double limitRight, double linespacing, double startx, ref CssLineBox line, ref double curx, ref double cury, ref double maxRight, ref double maxbottom)
    {
        var startX = curx;
        var startY = cury;
        box.FirstHostingLineBox = line;
        var localCurx = curx;
        var localMaxRight = maxRight;
        var localmaxbottom = maxbottom;

        foreach (CssBox b in box.Boxes)
        {
            // CSS2.1 §9.2.4: display:none elements generate no boxes and
            // must not participate in layout — skip them entirely.
            if (b.Display == CssConstants.None)
                continue;

            // CSS2.1 §9.5: Floated elements are out of normal flow and
            // must not participate in the inline formatting context.
            // Their positioning is handled separately in PerformLayoutImp.
            if (b.Float != CssConstants.None)
                continue;

            // CSS2.1 §9.6.1: Absolutely and fixed positioned elements are
            // out of normal flow.  Save the current flow state so we can
            // restore it after laying out the child — its words must not
            // shift subsequent siblings or inflate the parent's content
            // height.
            bool isAbsposChild = b.Position == CssConstants.Absolute
                || b.Position == CssConstants.Fixed;

            // CSS2.1 §10.3.7 / §10.6.4: record the out-of-flow child's static
            // position — the inline cursor it would occupy in this formatting
            // context — so its own block layout can honour it for auto-inset
            // axes (see CssBoxProperties.InlineStaticPosition).
            if (isAbsposChild)
                b.InlineStaticPosition = new PointF((float)curx, (float)cury);

            double childSaveCurx = curx;
            double childSaveCury = cury;
            double childSaveMaxRight = maxRight;
            double childSaveMaxBottom = maxbottom;
            CssLineBox childSaveLine = line;

            double leftspacing = !isAbsposChild ? b.ActualMarginLeft + b.ActualBorderLeftWidth + b.ActualPaddingLeft : 0;
            double rightspacing = !isAbsposChild ? b.ActualMarginRight + b.ActualBorderRightWidth + b.ActualPaddingRight : 0;

            b.RectanglesReset();
            b.MeasureWordsSize(g);

            curx += leftspacing;

            if (b.Words.Count > 0)
            {
                bool wrapNoWrapBox = false;

                if (b.WhiteSpace == CssConstants.NoWrap && curx > startx)
                {
                    var boxRight = curx;
                    foreach (var word in b.Words)
                        boxRight += word.FullWidth;

                    if (boxRight > limitRight)
                        wrapNoWrapBox = true;
                }

                if (LayoutBoxUtils.IsBoxHasWhitespace(b))
                    curx += box.ActualWordSpacing;

                foreach (var word in b.Words)
                {
                    // CSS2.1 §10.8: Every line box has a minimum height
                    // from the block container's line-height (the "strut").
                    // When line-height is 'normal' (ActualLineHeight == 0),
                    // the minimum comes from the font metrics, scaled to CSS
                    // px (font.Height is at pt-scale because the layout font
                    // is created at pt size in canvas units).
                    double boxLineHeight = box.ActualLineHeight > 0
                        ? box.ActualLineHeight
                        : box.ActualFont.Height * PtToCssPx;

                    if (maxbottom - cury < boxLineHeight)
                        maxbottom += boxLineHeight - (maxbottom - cury);

                    // CSS2.1 §10.8: The "strut" — each line box has a minimum
                    // height from the block container's font and line-height.
                    // For replaced inline elements (images), apply the block
                    // container's strut so that baseline alignment pushes the
                    // image down when the font is larger than the image.
                    double strutHeight = 0;

                    // CSS2.1 §10.8.1: it is the *margin* box of an inline replaced element that
                    // sits on the baseline, so its vertical margins take part in the line the
                    // same way its horizontal ones take part in the advance. They were dropped —
                    // the image was placed at the line's top and the line closed at the image's
                    // own bottom — so a thumbnail with `margin: 3px` (MediaWiki puts that on
                    // every one) sat 3px too high in a wrapper 6px too short.
                    double imageMarginTop = word.IsImage ? ImageWordMarginTop(word) : 0;
                    double imageMarginBottom = word.IsImage ? ImageWordMarginBottom(word) : 0;
                    double imageMarginBoxHeight = word.Height + imageMarginTop + imageMarginBottom;

                    if (word.IsImage)
                    {
                        strutHeight = blockbox.ActualLineHeight;

                        if (strutHeight <= 0)
                            strutHeight = blockbox.ActualFont.Height * PtToCssPx;

                        if (maxbottom - cury < strutHeight)
                            maxbottom += strutHeight - (maxbottom - cury);
                    }

                    // CSS Text §5.1: a line box may not be broken before its
                    // first inline content. An unbreakable word wider than the
                    // line stays on the (otherwise empty) line and overflows;
                    // it must not be pushed to a phantom second line, which
                    // would double the block's height (WPT max-height-109: a
                    // 3000px Ahem word in a 200px line was wrapping to a second
                    // line, so #red-parent grew to ~400px and red showed above
                    // the green). Words are added to the line via
                    // ReportExistanceOf below, so Words.Count == 0 means this
                    // word is the first on the current line.
                    bool lineHasContent = line.Words.Count > 0;
                    double lineRight = BandRightAt(blockbox, cury, boxLineHeight, limitRight);
                    if ((b.WhiteSpace != CssConstants.NoWrap && b.WhiteSpace != CssConstants.Pre && curx + word.Width + rightspacing > lineRight
                         && (b.WhiteSpace != CssConstants.PreWrap || !word.IsSpaces)
                         && (b.WhiteSpace != CssConstants.PreLine || !word.IsSpaces)
                         && lineHasContent) || word.IsLineBreak || wrapNoWrapBox)
                    {
                        wrapNoWrapBox = false;
                        cury = maxbottom + linespacing;

                        // CSS2.1 §9.5: the new line sits between the floats that reach it, and is
                        // pushed below them when what has to go on it does not fit even in the
                        // empty band. Both edges move, so the left one is re-read after the drop.
                        cury = DropLineBelowNarrowBands(
                            blockbox, cury, boxLineHeight, startx, limitRight,
                            word.Width + rightspacing);

                        curx = BandLeftAt(blockbox, cury, boxLineHeight, startx);

                        // handle if line is wrapped for the first text element where parent has left margin\padding
                        if (b == box.Boxes[0] && !word.IsLineBreak && (word == b.Words[0] || (box.ParentBox != null && box.ParentBox.IsBlock)))
                            curx += box.ActualMarginLeft + box.ActualBorderLeftWidth + box.ActualPaddingLeft;

                        line = new CssLineBox(blockbox);

                        if (word.IsImage || word.Equals(b.FirstWord))
                            curx += leftspacing;
                    }

                    line.ReportExistanceOf(word);

                    word.Left = curx;

                    // CSS2.1 §10.8.1: Replaced inline elements (images) are
                    // baseline-aligned by default — the bottom of the replaced
                    // element sits on the baseline.  The baseline position
                    // within the strut is at the font's ascent from the top.
                    if (word.IsImage && strutHeight > imageMarginBoxHeight)
                    {
                        double fontHeight = blockbox.ActualFont.Height * PtToCssPx;
                        double baseline = fontHeight * TypicalAscentRatio;
                        word.Top = Math.Max(cury, cury + baseline - imageMarginBoxHeight) + imageMarginTop;
                    }
                    else
                    {
                        word.Top = cury + imageMarginTop;
                    }

                    if (!box.IsFixed)
                    {
                        word.BreakPage();
                    }

                    curx = word.Left + word.FullWidth;

                    maxRight = Math.Max(maxRight, word.Right);
                    maxbottom = Math.Max(maxbottom, InlineWordLineBoxBottom(word));

                    // CSS2.1 §10.8: a baseline-aligned inline replaced element
                    // (image) sits with its bottom on the baseline, so the line
                    // box still extends below it by the strut's below-baseline
                    // descent. InlineWordLineBoxBottom returns only the image
                    // bottom (the baseline); without adding that descent the next
                    // wrapped line starts too high and the error accumulates down
                    // the block (CSS2 visudet/replaced-elements-*).
                    if (word.IsImage
                        && (word.OwnerBox == null
                            || string.IsNullOrEmpty(word.OwnerBox.VerticalAlign)
                            || word.OwnerBox.VerticalAlign == CssConstants.Baseline))
                    {
                        double lineStrut = blockbox.ActualLineHeight > 0
                            ? blockbox.ActualLineHeight
                            : blockbox.ActualFont.Height * PtToCssPx;
                        maxbottom = Math.Max(maxbottom,
                            word.Bottom + imageMarginBottom
                            + lineStrut * (1.0 - TypicalAscentRatio));
                    }

                    if (b.Position == CssConstants.Absolute)
                    {
                        word.Left += box.ActualMarginLeft;
                        word.Top += box.ActualMarginTop;
                    }
                }
            }
            else
            {
                // Determine if this child should use inline-block sizing:
                // 1. Explicit display:inline-block
                // 2. display:inline-flex / inline-grid (inline-level flex/grid)
                // 3. Direct child of a flex/grid container (all children
                //    become flex/grid items with shrink-to-fit sizing per
                //    CSS Flexbox §4 / CSS Grid §6; since Broiler lacks a
                //    true flex/grid engine, use FlowInlineBlock as a
                //    reasonable approximation)
                bool useInlineBlockFlow = b.Display == CssConstants.InlineBlock
                    || b.Display is "inline-flex" or "inline-grid"
                    || box.Display is "flex" or "inline-flex" or "grid" or "inline-grid";

                if (useInlineBlockFlow)
                {
                    // CSS 2.1 §10.3.9/§10.6.6: Inline-block boxes are laid
                    // out as blocks internally, then placed atomically in
                    // the inline flow (like replaced inline elements).
                    FlowInlineBlock(g, blockbox, b, limitRight, linespacing, startx,
                        leftspacing, rightspacing,
                        ref line, ref curx, ref cury, ref maxRight, ref maxbottom);

                    // CSS Flexbox §9.4: flex-direction:column stacks items
                    // vertically — force a line break after each flex item so
                    // the next item starts on a new row.
                    if (box.Display is "flex" or "inline-flex" or "grid" or "inline-grid"
                        && box.FlexDirection is "column" or "column-reverse")
                    {
                        cury = maxbottom;
                        curx = startx;
                        line = new CssLineBox(blockbox);
                    }
                }
                else
                {
                    // Block-level child inside inline flow: force a line break
                    // before and after the block (CSS2.1 §9.2.1.1 anonymous
                    // block boxes).  This ensures elements like <p> inside an
                    // inline <form> start on their own line.
                    //
                    // CSS2.1 §10.3.7/§10.6.4: An out-of-flow positioned child
                    // is laid out here only to establish its *static position*
                    // — the place a hypothetical inline box would occupy had
                    // the element been in flow.  It must therefore be placed at
                    // the current inline cursor, NOT forced onto its own line.
                    // (The surrounding flow state is restored after the child,
                    // so this break would be discarded anyway, but skipping it
                    // keeps the static position on the current line.)
                    if (b.IsBlock && !isAbsposChild)
                    {
                        if (curx > startx || maxbottom > cury)
                        {
                            cury = maxbottom;
                            curx = startx;
                            line = new CssLineBox(blockbox);
                        }
                    }

                    FlowBox(g, blockbox, b, limitRight, linespacing, startx, ref line, ref curx, ref cury, ref maxRight, ref maxbottom);

                    if (b.IsBlock && !isAbsposChild)
                    {
                        cury = maxbottom;
                        curx = startx;
                        line = new CssLineBox(blockbox);
                    }
                }
            }

            curx += rightspacing;

            // CSS2.1 §9.6.1: Restore flow state after an absolutely/fixed
            // positioned child so it does not affect siblings or parent
            // content height.  This includes the current line (`line`) and
            // block-axis cursor (`cury`): a block-level out-of-flow child
            // triggers the block line-break logic above to establish its
            // static position, but that break must not push subsequent
            // in-flow inline siblings onto a new line.
            if (isAbsposChild)
            {
                curx = childSaveCurx;
                cury = childSaveCury;
                maxRight = childSaveMaxRight;
                maxbottom = childSaveMaxBottom;
                line = childSaveLine;
            }
        }

        // handle height setting
        if (maxbottom - startY < box.ActualHeight)
            maxbottom += box.ActualHeight - (maxbottom - startY);

        // handle width setting
        // CSS 2.1 §10.3.9: inline-block boxes handle their own sizing in
        // FlowInlineBlock — do not register them here when processing
        // their own internal content (box == blockbox).
        if (box.IsInline && box != blockbox && 0 <= curx - startX && curx - startX < box.ActualWidth)
        {
            // hack for actual width handling
            curx += box.ActualWidth - (curx - startX);
            line.Rectangles.Add(box, new RectangleF((float)startX, (float)startY, (float)box.ActualWidth, (float)box.ActualHeight));
        }
        else if (box.IsInline && box != blockbox && !line.Rectangles.ContainsKey(box)
            && !InlineBoxHasInFlowContent(box))
        {
            // CSS Inline Layout §3 (invisible line boxes): an inline box whose only content
            // is out of flow — or which is empty — advances the inline cursor by nothing, so
            // the branch above (which pads a partially-filled inline out to its ActualWidth)
            // never records it and its position defaults to the document origin. Record its
            // line position on Location rather than as a line rectangle: a rectangle would make
            // the (otherwise empty, invisible) line box count as content and gain strut height,
            // shifting following blocks. Location leaves the line invisible while still giving
            // the box a real origin for getBoundingClientRect and, crucially, for an absolutely
            // positioned descendant that uses this inline as its containing block (which reads
            // Location when the inline has no line rectangles). WPT
            // css/css-inline/empty-span-scroll: an empty relative <span> holds the
            // scrollIntoView target, which must land on the line, not at the origin.
            box.Location = new PointF((float)startX, (float)startY);
        }

        // handle box that is only a whitespace
        if (box.Text.Length > 0 && box.Text.Span.IsWhiteSpace() && !box.IsImage && box.IsInline && box.Boxes.Count == 0 && box.Words.Count == 0)
            curx += box.ActualWordSpacing;

        // hack to support specific absolute position elements
        if (box.Position == CssConstants.Absolute)
        {
            curx = localCurx;
            maxRight = localMaxRight;
            maxbottom = localmaxbottom;

            // AdjustAbsolutePosition shifts the box's words by its own left/top so
            // an abspos child laid out at the *parent's* inline cursor (its static
            // position) lands at its CSS offset. But when the box flows its OWN
            // content (box == blockbox) AND PerformLayoutImp already advanced its
            // Location to the final left/top offset (AbsposLocationFinalized), the
            // words were flowed from startx = box.Location.X = the final origin, so
            // re-adding left/top would double the inset — painting content at ~2× the
            // offset while the border/background paint at the correct origin (auto-
            // sized abspos with inline content, e.g. css-anchor-position anchored
            // labels, issue #1163). Boxes that keep their static Location (e.g.
            // native form controls) still rely on the adjustment.
            if (!(box == blockbox && box.AbsposLocationFinalized))
                AdjustAbsolutePosition(box, 0, 0);
        }

        box.LastHostingLineBox = line;
    }

    /// <summary>
    /// True when an inline box contributes in-flow content to its line — its own words,
    /// or an in-flow child box (a nested inline, an inline-block, or a block-in-inline).
    /// Children that are out of flow (<c>display:none</c>, floated, or absolutely/fixed
    /// positioned) do not count: an inline whose only content is such a child is an empty
    /// (invisible) line box that still occupies a zero-width position on its line.
    /// </summary>
    private static bool InlineBoxHasInFlowContent(CssBox box)
    {
        if (box.Words.Count > 0)
            return true;

        foreach (var child in box.Boxes)
        {
            if (child.Display == CssConstants.None
                || child.Float != CssConstants.None
                || child.Position is CssConstants.Absolute or CssConstants.Fixed)
                continue;

            // An anonymous inline text run that collapsed to nothing — whitespace-only or
            // empty, with no words and no children of its own — contributes no rectangle to
            // the line, so it must not mask an otherwise-invisible inline box. Without this,
            // the whitespace an author leaves around an out-of-flow child (e.g. the newlines
            // between a relative <span> and its abspos target) counts as content, and the
            // empty-inline branch in FlowBox never records the span's flow position — it
            // defaults to the document origin, so the target (and scrollIntoView to it) lands
            // at 0 instead of on its line. WPT css/css-inline/empty-span-scroll.
            if (child.HtmlTag == null
                && child.Words.Count == 0
                && child.Boxes.Count == 0
                && (child.Text.IsEmpty || child.Text.Span.IsWhiteSpace()))
                continue;

            return true;
        }
        return false;
    }

    /// <summary>
    /// True when a grid container has at least one in-flow grid item that is an
    /// inline-level replaced element (an <c>&lt;img&gt;</c>). Such an item's word
    /// is positioned by the container's line-box flow, not by the item's own
    /// <see cref="CssBox.PerformLayout"/>, so the per-item block layout path in
    /// <see cref="FlowInlineBlock"/> would leave it orphaned and unpainted. When
    /// this is true the grid is instead laid out through the inline formatting
    /// context (<see cref="CreateLineBoxes"/>), matching the block-level grid path.
    /// </summary>
    private static bool GridHasInlineReplacedItem(CssBox grid)
    {
        foreach (var child in grid.Boxes)
        {
            if (child.Display == CssConstants.None
                || child.Position is CssConstants.Absolute or CssConstants.Fixed)
                continue;

            if (child.IsImage && child.IsInline)
                return true;
        }
        return false;
    }

    /// <summary>
    /// CSS 2.1 §10.3.9 / §10.6.6: Lay out an inline-block box as a
    /// block internally, then place it atomically in the inline flow.
    /// The inline-block establishes a new block formatting context for
    /// its children while participating in the parent's inline
    /// formatting context as a single opaque box.
    /// </summary>
    private static void FlowInlineBlock(ILayoutEnvironment g, CssBox blockbox, CssBox b,
        double limitRight, double linespacing, double startx,
        double leftspacing, double rightspacing,
        ref CssLineBox line, ref double curx, ref double cury,
        ref double maxRight, ref double maxbottom)
    {
        // Compute the container content width for resolving percentage and
        // em-based lengths on the inline-block.
        double containerWidth = blockbox.Size.Width
            - blockbox.ActualPaddingLeft - blockbox.ActualPaddingRight
            - blockbox.ActualBorderLeftWidth - blockbox.ActualBorderRightWidth;

        // CSS Images 3 §4 / CSS2.1 §10.4: a replaced atomic inline — a <canvas>, whose natural size
        // is its width/height content attributes — takes an auto axis from its natural size rather
        // than shrinking to fit its (absent) content, and keeps the two axes tied by the natural
        // ratio while min-*/max-* clamp them. Sized in one step below; the per-axis clamps that
        // follow are skipped for it.
        // CSS Containment 2 §3.2: size containment leaves a replaced element with no natural
        // dimensions and no natural ratio, so a contained <canvas>/<svg> is sized like any other
        // contained box rather than from its bitmap.
        var intrinsic = b.AppliesSizeContainment ? null : b.IntrinsicReplacedSize;
        bool isReplaced = intrinsic is { Width: > 0, Height: > 0 };

        // --- Compute inline-block content width ---
        // A replaced box settles both axes at once, constraints and all, so the per-axis clamps
        // below are skipped for it and the height branch reads the result back.
        double replacedContentHeight = 0;
        double ibContentWidth;
        if (isReplaced)
        {
            b.ResolveReplacedContentSize(intrinsic.Value, containerWidth, out ibContentWidth, out replacedContentHeight);
        }
        else if (b.Width != CssConstants.Auto && !string.IsNullOrEmpty(b.Width))
        {
            ibContentWidth = CssLengthParser.ParseLength(b.Width, containerWidth, b.GetEmHeight());
            if (b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase))
            {
                ibContentWidth -= b.ActualBorderLeftWidth + b.ActualBorderRightWidth
                    + b.ActualPaddingLeft + b.ActualPaddingRight;
                if (ibContentWidth < 0)
                    ibContentWidth = 0;
            }
        }
        else
        {
            // CSS 2.1 §10.3.9: auto-width inline-block uses shrink-to-fit.
            // Measure descendant words for intrinsic width computation.
            MeasureDescendantWords(g, b);
            b.GetMinMaxWidth(out double prefMin, out double prefMax);
            if (double.IsNaN(prefMin)) prefMin = 0;
            if (double.IsNaN(prefMax)) prefMax = 0;
            // GetMinMaxWidth returns border-box widths (content + padding +
            // border).  Convert to content-only widths so the shrink-to-fit
            // calculation matches the content-only `available` value and the
            // padding/border added back at ibBoxWidth below.
            double ownPaddingBorder = b.ActualBorderLeftWidth + b.ActualBorderRightWidth
                + b.ActualPaddingLeft + b.ActualPaddingRight;
            prefMin = Math.Max(0, prefMin - ownPaddingBorder);
            prefMax = Math.Max(0, prefMax - ownPaddingBorder);
            double available = Math.Max(0, limitRight - curx - rightspacing
                - b.ActualBorderLeftWidth - b.ActualBorderRightWidth
                - b.ActualPaddingLeft - b.ActualPaddingRight);
            ibContentWidth = Math.Min(Math.Max(prefMin, available), prefMax);
        }

        // CSS 2.1 §10.4: Apply min-width constraint.
        // min-width takes priority over computed width (including
        // shrink-to-fit for auto-width inline-blocks).
        if (!isReplaced && b.MinWidth != "0" && !string.IsNullOrEmpty(b.MinWidth))
        {
            double minW = CssLengthParser.ParseLength(b.MinWidth, containerWidth, b.GetEmHeight());
            double minContentW = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? minW - b.ActualBorderLeftWidth - b.ActualBorderRightWidth
                    - b.ActualPaddingLeft - b.ActualPaddingRight
                : minW;

            if (minContentW > ibContentWidth)
                ibContentWidth = minContentW;
        }

        // CSS 2.1 §10.4: Apply max-width constraint.
        // max-width limits the computed width from above.  When both
        // min-width and max-width are specified, min-width wins if it
        // exceeds max-width (CSS2.1 §10.4).
        if (!isReplaced && b.MaxWidth != "none" && !string.IsNullOrEmpty(b.MaxWidth))
        {
            double maxW = CssLengthParser.ParseLength(b.MaxWidth, containerWidth, b.GetEmHeight());
            double maxContentW = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? maxW - b.ActualBorderLeftWidth - b.ActualBorderRightWidth
                    - b.ActualPaddingLeft - b.ActualPaddingRight
                : maxW;
            if (maxContentW < ibContentWidth)
                ibContentWidth = maxContentW;
        }

        double ibBoxWidth = ibContentWidth
            + b.ActualBorderLeftWidth + b.ActualBorderRightWidth
            + b.ActualPaddingLeft + b.ActualPaddingRight;

        // --- Line wrap check ---
        // Total inline extent: margin-left + box-width + margin-right.
        // curx already includes leftspacing (margin+border+padding), so the
        // border-box left edge is at curx - border - padding.
        double ibBorderLeft = curx - b.ActualBorderLeftWidth - b.ActualPaddingLeft;
        double edgeBeforeBox = ibBorderLeft - b.ActualMarginLeft;
        double totalExtent = b.ActualMarginLeft + ibBoxWidth + b.ActualMarginRight;
        if (edgeBeforeBox + totalExtent > limitRight && edgeBeforeBox > startx)
        {
            double lineStrut = blockbox.ActualLineHeight > 0
                ? blockbox.ActualLineHeight
                : blockbox.ActualFont.Height * PtToCssPx;
            double baselineDescent = lineStrut * (1.0 - TypicalAscentRatio);

            curx = startx + leftspacing;
            cury = maxbottom + linespacing + baselineDescent;
            line = new CssLineBox(blockbox);
            ibBorderLeft = curx - b.ActualBorderLeftWidth - b.ActualPaddingLeft;
        }

        // --- Position and size the inline-block ---
        b.Location = new PointF((float)ibBorderLeft, (float)(cury + b.ActualMarginTop));
        b.Size = new SizeF((float)ibBoxWidth, 0);
        b.ActualBottom = b.Location.Y;

        // --- Lay out children inside the inline-block ---
        // `inline-flex` is a flex container in every way that matters here — only its outer display
        // differs, and this method is precisely the path an inline-level box arrives on. Testing
        // for `flex` alone meant an inline-flex container never ran the flex algorithm at all: its
        // items were flowed as ordinary inline-block content, so nothing sized them from the
        // container's cross size and nothing distributed its free space. Every
        // css-flexbox/aspect-ratio-intrinsic-size test builds its case on an `inline-flex` box.
        if (b.Display is "flex" or "inline-flex" && b.IsRowFlexContainer() && HasBlockLevelFlexItems(b))
        {
            b.PerformFlexRowLayout(g);
        }
        else if (b.Display is "grid" or "inline-grid")
        {
            // CSS Grid Level 1: Grid items should be laid out as blocks
            // (not inline-blocks) so that width:auto stretches to the
            // column width.  Use the block layout path, then apply grid
            // stacking or auto-placement to fix positioning.
            //
            // Exception: an inline replaced grid item (an <img>) is neither sized
            // nor painted by its own PerformLayout — a replaced inline element's
            // word is positioned by the *container's* line-box flow, which the
            // block path never runs, leaving the word orphaned (no container line
            // box owns it) so the image renders blank. Route such a grid through
            // the inline formatting context instead — the same CreateLineBoxes
            // path the block-level `display:grid` case in CssBox.PerformLayoutImp
            // uses — so the image's word lands in a container line box and is
            // painted; ApplyGridLayoutAfterInline then re-flows the items into
            // their tracks and re-stretches auto-width items. (WPT
            // css-grid/grid-items/grid-minimum-size-grid-items-021 and the other
            // inline-grid + <img> tests rendered blank before this.) Narrowed to
            // grids that actually contain such an item so every other grid keeps
            // its existing block-layout path.
            if (GridHasInlineReplacedItem(b))
            {
                CreateLineBoxes(g, b);
            }
            else
            {
                foreach (var child in b.Boxes)
                    child.PerformLayout(g);

                double childMaxBottom = b.Location.Y;
                foreach (var child in b.Boxes)
                    childMaxBottom = Math.Max(childMaxBottom, child.ActualBottom);

                b.ActualBottom = childMaxBottom;
            }

            b.ApplyGridLayoutAfterInline();
        }
        else if (b.Display == CssConstants.Table || b.Display == CssConstants.InlineTable)
        {
            // A table laid out through this atomic-inline path (an `inline-table`,
            // or a `display:table` blockified into a flex/grid item, which
            // ContainsInlinesOnly routes here) must run the table formatting
            // algorithm — its row/row-group/cell/caption boxes have no standalone
            // block layout. Without this the table's children were laid out
            // individually as blocks, so the rows and cells (and their text) were
            // never positioned and a `<table>` grid item rendered empty. (WPT
            // css-grid/table-grid-item-dynamic-002.)
            CssLayoutEngineTable.PerformLayout(g, b, b.BaseUrl);
        }
        else if (LayoutBoxUtils.ContainsInlinesOnly(b) || InlineContentWithBrsOnly(b))
        {
            // Inline-block content that is inline runs interrupted only by <br>
            // line breaks is an inline formatting context: lay it out with line
            // boxes so the breaks split it into lines. A <br> computes to a
            // block-level box in Broiler, which would otherwise route this to the
            // block-children branch below — that branch lays each anonymous inline
            // child out via its own PerformLayout (which sets no block height for
            // an inline box), collapsing the inline-block to zero height. This is
            // the shape of a multi-line grid item (e.g. `X<br>X`), which the
            // css-grid check-layout reference tests use throughout.
            CreateLineBoxes(g, b);
        }
        else if (b.Boxes.Count > 0)
        {
            foreach (var child in b.Boxes)
                child.PerformLayout(g);

            double childMaxBottom = b.Location.Y;

            foreach (var child in b.Boxes)
                childMaxBottom = Math.Max(childMaxBottom, child.ActualBottom);

            b.ActualBottom = childMaxBottom;
        }

        // --- Compute height ---
        double ibHeight;
        bool heightIsPercent = !string.IsNullOrEmpty(b.Height) && b.Height.Contains('%');

        // A grid item's percentage block size resolves against its grid *area*
        // (the track), which is not known here — the track pass / PlaceItemInArea
        // sizes percentage/auto grid items to their area later. So measure an
        // in-flow grid item at its content height for now instead of resolving
        // the percentage against the container *width* (the wrong basis in the
        // branch below): that basis made an auto-height grid item with
        // height:100% balloon to ~100% of the grid width and, clipped to the
        // viewport, paint a full-viewport box (WPT
        // css-grid/grid-items/whitespace-in-grid-item-001); it also handed the
        // §11 track pass an inflated block size that tripped its "did a narrowed
        // column reflow this?" guard into declining to the stacking
        // approximation. Restricted to in-flow grid items — every other box
        // (inline-block, flex item, replaced inline SVG/img, out-of-flow static
        // positions) keeps its existing sizing untouched.
        bool isInFlowGridItem =
            b.Position is not (CssConstants.Absolute or CssConstants.Fixed)
            && b.ParentBox != null
            && b.ParentBox.Display is "grid" or "inline-grid";

        if (isReplaced)
        {
            // Already settled with the width, constraints and all.
            ibHeight = replacedContentHeight
                + b.ActualBorderTopWidth + b.ActualBorderBottomWidth
                + b.ActualPaddingTop + b.ActualPaddingBottom;
        }
        else if (heightIsPercent && isInFlowGridItem)
        {
            ibHeight = Math.Max(0, b.ActualBottom - b.Location.Y);
        }
        else if (TryResolveAtomicInlineSpecifiedHeight(b, containerWidth, out double cssHeight))
        {
            ibHeight = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? cssHeight
                : cssHeight
                    + b.ActualBorderTopWidth + b.ActualBorderBottomWidth
                    + b.ActualPaddingTop + b.ActualPaddingBottom;
        }
        // CSS Sizing 4 §4: an auto block axis takes its used size from the used
        // inline size through the box's preferred aspect ratio. An atomic
        // inline-level box computes its height here rather than in
        // CssBox.ResolveUsedBlockHeight, so the transfer has to be repeated on this
        // path — without it an outer <svg>, whose SVG children are not CSS boxes,
        // measured its content height as zero and vanished. b.Size.Width is the
        // used border-box width settled above (min-/max-width already applied), so
        // the transfer reads the same width the box is painted at.
        else if (b.TryGetAspectRatioBlockHeight(out double ratioHeight))
        {
            ibHeight = ratioHeight;
        }
        // CSS Containment 2 §3.2: an atomic inline computes its height here rather than in
        // CssBox.ResolveUsedBlockHeight, so size containment has to be honoured on this path too —
        // otherwise the contents this box just laid out are measured straight back into it. Below
        // the ratio arm, because a preferred aspect ratio and a definite inline size settle the
        // block axis outright and `contain-intrinsic-size` only stands in for *contents*; above the
        // content-derived arm, which is precisely what containment removes.
        else if (b.AppliesSizeContainment)
        {
            ibHeight = b.ContainedIntrinsicContentHeight
                + b.ActualBorderTopWidth + b.ActualBorderBottomWidth
                + b.ActualPaddingTop + b.ActualPaddingBottom;
        }
        else
        {
            ibHeight = Math.Max(0, b.ActualBottom - b.Location.Y);

        }

        // CSS 2.1 §10.7: clamp the block axis to min-height / max-height. ibHeight is a border-box
        // height, so a content-box bound has the box's own border and padding added to it.
        // max-height had no arm here at all until now, which is why a `display: inline-block` box
        // with `height: 1000px; max-height: 60px` stayed 1000px tall while the same declarations on
        // a `display: block` box clamped correctly (WPT issue #1562 problem 30 recorded it as the
        // second of the three gaps behind css-sizing/replaced-max-size-saturation).
        var blockBounds = isReplaced ? ReplacedBoxSizing.Bounds.Unconstrained : b.ResolveBlockSizeBounds();
        if (!blockBounds.IsUnconstrained)
        {
            double borderAndPadding = b.BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                ? 0
                : b.ActualBorderTopWidth + b.ActualBorderBottomWidth
                  + b.ActualPaddingTop + b.ActualPaddingBottom;

            ibHeight = new ReplacedBoxSizing.Bounds(
                    blockBounds.Min > 0 ? blockBounds.Min + borderAndPadding : 0,
                    double.IsPositiveInfinity(blockBounds.Max) ? blockBounds.Max : blockBounds.Max + borderAndPadding)
                .Clamp(ibHeight);
        }

        b.ActualBottom = b.Location.Y + ibHeight;
        b.Size = new SizeF(b.Size.Width, (float)ibHeight);

        // --- Rotate a vertical writing-mode inline-block into physical space ---
        // An inline-block that is a vertical writing-mode root lays its content
        // out in the logical (horizontal) frame here (its Width/Height already
        // report the swapped logical extents via WillBeVerticalTransposed).
        // Unlike a block-level root, it never passes through CssBox.PerformLayout,
        // so the post-layout rotation was skipped and its content stayed in the
        // logical frame (e.g. a vertical-rl block child left-aligned instead of
        // block-start/right aligned). Rotate it in place now — its inline
        // position on the line is already correct — then advance the line by the
        // box's *physical* border-box width (Size.Width after the swap), which
        // differs from the logical ibBoxWidth for non-square boxes.
        double physicalBoxWidth = ibBoxWidth;
        if (VerticalFlowPrototype.Enabled
            && CssBoxProperties.IsVerticalWritingMode(b.WritingMode)
            && (b.ParentBox == null || !CssBoxProperties.IsVerticalWritingMode(b.ParentBox.WritingMode)))
        {
            b.ApplyVerticalWritingModeFlow();
            physicalBoxWidth = b.Size.Width;
        }

        // --- Register the inline-block as a rectangle in the line box ---
        line.Rectangles[b] = new RectangleF(b.Location.X, b.Location.Y,
            (float)physicalBoxWidth, (float)(b.ActualBottom - b.Location.Y));

        // --- Advance flow position ---
        // curx has leftspacing (margin+border+padding) already added.
        // After the inline-block, set curx so that after rightspacing
        // (margin+border+padding right) is added, we end up at the
        // right margin edge of the box.
        curx = ibBorderLeft + physicalBoxWidth
            - b.ActualBorderRightWidth - b.ActualPaddingRight;

        maxRight = Math.Max(maxRight, ibBorderLeft + physicalBoxWidth);
        maxbottom = Math.Max(maxbottom, b.ActualBottom + b.ActualMarginBottom);

        // CSS2.1 §9.4.3: position:relative shifts the box (and its subtree) visually
        // without affecting flow. FlowInlineBlock positions the inline-block from the
        // in-flow line position above (overwriting any offset the box's own layout
        // applied), so re-apply the relative offset here — after flow advancement and
        // the line rectangle were computed from the in-flow position. Applied to the
        // box subtree (OffsetLeft/Top) and to the line's own rectangle copy so paint
        // and getBoundingClientRect agree. Block-level boxes get this from
        // CssBox.ApplyRelativePositionOffset; inline-blocks never run that path.
        if (b.Position == CssConstants.Relative)
        {
            double rdx = CssBoxHelper.GetRelativeOffsetX(b);
            double rdy = CssBoxHelper.GetRelativeOffsetY(b);
            if (rdx != 0)
                b.OffsetLeft(rdx);
            if (rdy != 0)
                b.OffsetTop(rdy);
            if ((rdx != 0 || rdy != 0) && line.Rectangles.TryGetValue(b, out var ibRect))
                line.Rectangles[b] = new RectangleF(
                    (float)(ibRect.X + rdx), (float)(ibRect.Y + rdy), ibRect.Width, ibRect.Height);
        }
    }

    /// <summary>
    /// The specified block size an atomic inline-level box takes from its own <c>height</c>, or
    /// <see langword="false"/> when it has none the caller can use — an <c>auto</c> height, or a
    /// percentage the containing block cannot give a basis for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A percentage used to be measured against <paramref name="containerWidth"/>: the wrong axis
    /// outright, and the reason an outer <c>&lt;svg width="100%" height="100%"&gt;</c> — the shape
    /// every <c>conformance-checkers/html-svg</c> page has — came out as tall as the page is wide
    /// and then let "xMidYMid meet" centre its drawing a couple of hundred pixels down the box.
    /// The in-flow grid-item branch above already sidesteps this basis for its own case and names it
    /// as wrong.
    /// </para>
    /// <para>
    /// CSS 2.1 §10.5: with no definite basis the percentage computes to <c>auto</c>, so this
    /// declines and the caller falls through to the aspect-ratio transfer — which is what gives that
    /// <c>&lt;svg&gt;</c> the height its <c>viewBox</c> ratio implies, the same height the reference
    /// browser uses.
    /// </para>
    /// </remarks>
    private static bool TryResolveAtomicInlineSpecifiedHeight(
        CssBox b, double containerWidth, out double cssHeight)
    {
        cssHeight = 0;
        if (b.Height == CssConstants.Auto || string.IsNullOrEmpty(b.Height))
            return false;

        if (!b.Height.Contains('%'))
        {
            cssHeight = CssLengthParser.ParseLength(b.Height, containerWidth, b.GetEmHeight());
            return true;
        }

        if (!b.TryGetPercentageBlockSizeBasis(out double basis))
            return false;

        cssHeight = CssLengthParser.ParseLength(b.Height, basis, b.GetEmHeight());
        return true;
    }

    /// <summary>
    /// True when <paramref name="box"/>'s in-flow content is inline-level except
    /// for <c>&lt;br&gt;</c> elements — which compute to block-level boxes in
    /// Broiler but merely force a line break within an inline formatting context.
    /// Such a box should be laid out with <see cref="CreateLineBoxes"/> (the
    /// breaks split the inline content into lines) rather than the block-children
    /// path, which never establishes line boxes for the anonymous inline runs and
    /// so leaves the box zero-height. Requires at least one <c>&lt;br&gt;</c> so a
    /// genuinely block-only box is unaffected (it is not inline content).
    /// </summary>
    private static bool InlineContentWithBrsOnly(CssBox box)
    {
        bool sawBr = false;
        foreach (var child in box.Boxes)
        {
            if (child.Display == CssConstants.None)
                continue;

            if (child.Position is CssConstants.Absolute or CssConstants.Fixed)
                continue;

            if (child.IsBrElement)
            {
                sawBr = true;
                continue;
            }

            if (!child.IsInline && child.Float == CssConstants.None)
                return false;
        }

        return sawBr;
    }

    private static bool HasBlockLevelFlexItems(CssBox box)
    {
        foreach (var child in box.Boxes)
        {
            if (child.Display == CssConstants.None)
                continue;

            if (child.Position is CssConstants.Absolute or CssConstants.Fixed)
                continue;

            if (!child.IsInline)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Recursively measures word sizes on all descendant boxes so that
    /// intrinsic width calculations are reliable.
    /// </summary>
    private static void MeasureDescendantWords(ILayoutEnvironment g, CssBox box)
    {
        box.MeasureWordsSize(g);

        foreach (var child in box.Boxes)
            MeasureDescendantWords(g, child);
    }

    private static void AdjustAbsolutePosition(CssBox box, double left, double top)
    {
        left += box.ActualMarginLeft;
        top += box.ActualMarginTop;

        // CSS 2.1 §9.3.2: Apply 'top' and 'left' offsets for absolutely
        // positioned elements.
        if (box.Top != CssConstants.Auto && !string.IsNullOrEmpty(box.Top))
        {
            double topOffset = CssLengthParser.ParseLength(box.Top, box.Size.Height, box.GetEmHeight());

            if (!double.IsNaN(topOffset))
                top += topOffset;
        }

        if (box.Left != CssConstants.Auto && !string.IsNullOrEmpty(box.Left))
        {
            double leftOffset = CssLengthParser.ParseLength(box.Left, box.Size.Width, box.GetEmHeight());

            if (!double.IsNaN(leftOffset))
                left += leftOffset;
        }

        if (box.Words.Count > 0)
        {
            foreach (var word in box.Words)
            {
                word.Left += left;
                word.Top += top;
            }
        }
        else
        {
            foreach (var b in box.Boxes)
                AdjustAbsolutePosition(b, left, top);
        }
    }

    private static void BubbleRectangles(CssBox box, CssLineBox line)
    {
        if (box.Words.Count > 0)
        {
            double x = float.MaxValue, y = float.MaxValue, r = float.MinValue, b = float.MinValue;
            List<CssRect> words = line.WordsOf(box);

            if (words.Count <= 0)
                return;

            foreach (CssRect word in words)
            {
                // handle if line is wrapped for the first text element where parent has left margin\padding
                var left = word.Left;

                if (box == box.ParentBox.Boxes[0] && word == box.Words[0] && word == line.Words[0] && line != line.OwnerBox.LineBoxes[0] && !word.IsLineBreak)
                    left -= box.ParentBox.ActualMarginLeft + box.ParentBox.ActualBorderLeftWidth + box.ParentBox.ActualPaddingLeft;


                x = Math.Min(x, left);
                r = Math.Max(r, word.Right);
                y = Math.Min(y, word.Top);
                b = Math.Max(b, word.Bottom);
            }

            line.UpdateRectangle(box, x, y, r, b);
        }
        else
        {
            foreach (CssBox b in box.Boxes)
                BubbleRectangles(b, line);
        }
    }

    private static void ApplyHorizontalAlignment(CssLineBox lineBox, bool lineRtl)
    {
        var box = lineBox.OwnerBox;

        // CSS Text 4 §text-align / §text-align-last: text-align governs every line;
        // the *last* line of the block is instead governed by text-align-last.  The
        // shorthand value 'justify-all' additionally sets the last line to justify
        // (a plain 'justify' leaves the last line 'start'-aligned).  Resolve the
        // per-line alignment keyword first, then map the logical values below.
        bool isLastLine = lineBox.Equals(box.LineBoxes[^1]);
        bool justifyAll = string.Equals(box.TextAlign, "justify-all", StringComparison.OrdinalIgnoreCase);
        string effectiveAlign = isLastLine
            ? ResolveTextAlignLast(box, justifyAll)
            : (justifyAll ? CssConstants.Justify : box.TextAlign);

        // Resolve the logical 'start'/'end' keywords (and the initial value, which
        // is 'start') against the line's base direction.  In a left-to-right base,
        // start=left and end=right; in a right-to-left base they swap.  Physical
        // 'left'/'right'/'center'/'justify' values pass through unchanged.  Under
        // unicode-bidi:plaintext the base is the per-line resolved direction; this
        // is what keeps right-to-left lines aligned to the right edge and, without
        // it, an RTL box left its 'start'-aligned text on the left (CSS Text
        // §text-align).
        string resolvedAlign = effectiveAlign switch
        {
            null or "" or "start" => lineRtl ? CssConstants.Right : CssConstants.Left,
            "end" => lineRtl ? CssConstants.Left : CssConstants.Right,
            // Legacy -webkit-{left,right,center} align inline content like their
            // standard counterparts (they additionally drive block alignment,
            // handled in CssBox justify-self resolution).
            "-webkit-left" => CssConstants.Left,
            "-webkit-right" => CssConstants.Right,
            "-webkit-center" => CssConstants.Center,
            _ => effectiveAlign
        };

        switch (resolvedAlign)
        {
            case CssConstants.Right:
                ApplyRightAlignment(lineBox);
                break;

            case CssConstants.Center:
                ApplyCenterAlignment(lineBox);
                break;

            case CssConstants.Justify:
                // The caller only routes the last line here when text-align-last
                // resolved to justify (justify-all or text-align-last:justify), so
                // justify it too — no last-line skip needed at this point.
                ApplyJustifyAlignment(lineBox);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Resolves the effective alignment keyword for a block's <b>last</b> line per
    /// CSS Text 4 §text-align-last.  An explicit <c>text-align-last</c> value wins;
    /// the initial <c>auto</c> follows <c>text-align</c>, except that a plain
    /// <c>justify</c> text-align leaves the last line <c>start</c>-aligned (ragged)
    /// unless the shorthand value was <c>justify-all</c>, which justifies it too.
    /// </summary>
    private static string ResolveTextAlignLast(CssBox box, bool justifyAll)
    {
        string last = box.TextAlignLast;
        if (!string.IsNullOrEmpty(last)
            && !string.Equals(last, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return last;
        }

        // auto:
        if (justifyAll)
            return CssConstants.Justify;

        if (string.Equals(box.TextAlign, CssConstants.Justify, StringComparison.OrdinalIgnoreCase))
            return "start"; // a justified block's last line stays ragged (start-aligned)

        return box.TextAlign; // every other value applies to the last line as well
    }

    /// <summary>
    /// Returns the line's base direction as resolved from its first strong
    /// (Hebrew/Arabic vs. Latin/Greek/Cyrillic) character: <c>true</c> for
    /// right-to-left, <c>false</c> for left-to-right, or <c>null</c> when the line
    /// has no strong character (so the caller inherits the previous paragraph's or
    /// the containing block's direction).  Used for <c>unicode-bidi: plaintext</c>.
    /// </summary>
    private static bool? LineFirstStrongRtl(CssLineBox line)
    {
        foreach (CssRect word in line.Words)
        {
            string text = word.Text;

            if (string.IsNullOrEmpty(text))
                continue;

            foreach (char c in text)
            {
                if (IsRtlStrongChar(c))
                    return true;

                if (IsLtrStrongChar(c))
                    return false;
            }
        }

        return null; // no strong character → inherit base direction
    }

    /// <summary>
    /// Seeds the running base direction for a <c>unicode-bidi: plaintext</c> block
    /// whose first paragraph has no strong character of its own.  Such a paragraph
    /// inherits the previous paragraph's direction; because neutral paragraphs
    /// propagate that direction forward, the result equals the direction of the most
    /// recent strong character that appears before this block in document order.
    /// Falls back to the block's own direction when no preceding strong character
    /// exists (i.e. the containing block's direction).
    /// </summary>
    private static bool SeedPlaintextBaseRtl(CssBox blockBox)
    {
        var parent = blockBox.ParentBox;

        if (parent != null)
        {
            int index = parent.Boxes.IndexOf(blockBox);

            for (int i = index - 1; i >= 0; i--)
            {
                bool? strong = LastStrongRtl(parent.Boxes[i]);
                if (strong.HasValue)
                    return strong.Value;
            }
        }

        return blockBox.Direction == CssConstants.Rtl;
    }

    /// <summary>
    /// Returns the direction of the last strong character within <paramref name="box"/>'s
    /// subtree in document order (<c>true</c> RTL, <c>false</c> LTR), or <c>null</c>
    /// when the subtree has no strong character.
    /// </summary>
    private static bool? LastStrongRtl(CssBox box)
    {
        for (int i = box.Boxes.Count - 1; i >= 0; i--)
        {
            bool? strong = LastStrongRtl(box.Boxes[i]);
            if (strong.HasValue)
                return strong;
        }

        for (int i = box.Words.Count - 1; i >= 0; i--)
        {
            string text = box.Words[i].Text;
            if (string.IsNullOrEmpty(text))
                continue;

            for (int c = text.Length - 1; c >= 0; c--)
            {
                if (IsRtlStrongChar(text[c]))
                    return true;

                if (IsLtrStrongChar(text[c]))
                    return false;
            }
        }

        return null;
    }

    private static bool IsRtlStrongChar(char c) =>
        (c >= 0x0590 && c <= 0x05FF) ||   // Hebrew
        (c >= 0x0600 && c <= 0x06FF) ||   // Arabic
        (c >= 0x0750 && c <= 0x077F) ||   // Arabic Supplement
        (c >= 0x08A0 && c <= 0x08FF) ||   // Arabic Extended-A
        (c >= 0xFB1D && c <= 0xFDFF) ||   // Hebrew/Arabic presentation forms-A
        (c >= 0xFE70 && c <= 0xFEFF);     // Arabic presentation forms-B

    private static bool IsLtrStrongChar(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
        (c >= 0x00C0 && c <= 0x024F) ||   // Latin-1 supplement / extended
        (c >= 0x0370 && c <= 0x03FF) ||   // Greek
        (c >= 0x0400 && c <= 0x04FF);     // Cyrillic

    private static void ApplyRightToLeft(CssLineBox lineBox, bool lineRtl)
    {
        // When the line's base direction is right-to-left the whole line is
        // mirrored; otherwise only the individual inline boxes that opt into RTL
        // are reversed.  Under unicode-bidi:plaintext 'lineRtl' is the per-line
        // resolved direction, so left-to-right lines inside an RTL block stay on
        // the left instead of being mirrored to the right edge.
        if (lineRtl)
        {
            ApplyRightToLeftOnLine(lineBox);
        }
        else
        {
            foreach (var box in lineBox.RelatedBoxes)
            {
                if (box.Direction == CssConstants.Rtl)
                    ApplyRightToLeftOnSingleBox(lineBox, box);
            }
        }
    }

    private static void ApplyRightToLeftOnLine(CssLineBox line)
    {
        if (line.Words.Count <= 0)
            return;

        double left = line.Words[0].Left;
        double right = line.Words[^1].Right;

        foreach (CssRect word in line.Words)
        {
            double diff = word.Left - left;
            double wright = right - diff;

            word.Left = wright - word.Width;
        }
    }

    private static void ApplyRightToLeftOnSingleBox(CssLineBox lineBox, CssBox box)
    {
        int leftWordIdx = -1;
        int rightWordIdx = -1;

        for (int i = 0; i < lineBox.Words.Count; i++)
        {
            if (lineBox.Words[i].OwnerBox != box)
                continue;

            if (leftWordIdx < 0)
                leftWordIdx = i;

            rightWordIdx = i;
        }

        if (leftWordIdx <= -1 || rightWordIdx <= leftWordIdx)
            return;

        double left = lineBox.Words[leftWordIdx].Left;
        double right = lineBox.Words[rightWordIdx].Right;

        for (int i = leftWordIdx; i <= rightWordIdx; i++)
        {
            double diff = lineBox.Words[i].Left - left;
            double wright = right - diff;

            lineBox.Words[i].Left = wright - lineBox.Words[i].Width;
        }
    }

    /// <summary>
    /// CSS 2.1 §10.8: A text run contributes its inline box's <em>line-height</em>
    /// to the line box (and hence the block's content height), not its taller
    /// font content area.  When <c>line-height</c> is smaller than the content
    /// area the glyphs overflow the line box, but they must not increase it —
    /// otherwise an explicit small <c>line-height</c> (e.g. <c>line-height:1</c>
    /// on a font whose natural box is ~1.16em) produces a too-tall block.
    /// The glyph rectangle itself is left untouched, so glyph positions (and
    /// calibrated layouts) are unchanged; only the height contribution is
    /// clamped.  Replaced inline content (images) and runs with no positive
    /// line-height keep contributing their full box.
    /// </summary>
    private static double InlineWordLineBoxBottom(CssRect word)
    {
        double ownerLineHeight = word.OwnerBox?.ActualLineHeight ?? 0;
        if (word.IsImage)
            return word.Bottom + ImageWordMarginBottom(word);

        if (ownerLineHeight <= 0)
            return word.Bottom;

        return Math.Min(word.Bottom, word.Top + ownerLineHeight);
    }

    /// <summary>
    /// The block-start margin of an inline replaced element, which CSS2.1 §10.8.1 makes part of
    /// the margin box the line aligns. A percentage resolves against the containing block's
    /// <em>width</em> (CSS2.1 §8.3), which <c>ActualMarginTop</c> already does.
    /// </summary>
    private static double ImageWordMarginTop(CssRect word)
    {
        double margin = word.OwnerBox?.ActualMarginTop ?? 0;
        return double.IsNaN(margin) ? 0 : margin;
    }

    /// <summary>The block-end margin counterpart of <see cref="ImageWordMarginTop"/>.</summary>
    private static double ImageWordMarginBottom(CssRect word)
    {
        double margin = word.OwnerBox?.ActualMarginBottom ?? 0;
        return double.IsNaN(margin) ? 0 : margin;
    }

    /// <summary>
    /// Same line-height clamp as <see cref="InlineWordLineBoxBottom"/> but for a
    /// non-atomic inline box's accumulated rectangle.  Inline boxes (incl. the
    /// anonymous inline box that wraps a block's direct text) contribute their
    /// line-height to the line box, not their font content area.  Replaced
    /// inline content (images) and inline-block boxes keep their full margin
    /// box, which legitimately establishes the line box extent.
    /// </summary>
    private static double InlineRectLineBoxBottom(CssBox box, RectangleF rect)
    {
        if (box.IsImage
            || box.Display == CssConstants.InlineBlock
            || box.Display is "inline-flex" or "inline-grid"
            || !box.IsInline)
            return rect.Bottom;

        double lineHeight = box.ActualLineHeight;
        if (lineHeight <= 0)
            return rect.Bottom;

        return Math.Min(rect.Bottom, rect.Top + lineHeight);
    }

    /// <summary>
    /// Returns whether <paramref name="box"/> ends with atomic inline-level
    /// content (an <c>inline-block</c>/<c>inline-flex</c>/<c>inline-grid</c>
    /// box), looking through the anonymous block wrapper that the
    /// block-inside-inline correction generates around inline content split by a
    /// <c>&lt;br&gt;</c>.  Used to decide whether a following <c>&lt;br&gt;</c>'s
    /// empty-line spacer is spurious (it merely ends the inline-block's line).
    /// </summary>
    internal static bool EndsWithAtomicInlineBlock(CssBox box)
    {
        if (box == null)
            return false;

        if (box.Display == CssConstants.InlineBlock
            || box.Display is "inline-flex" or "inline-grid")
            return true;

        if (box.Kind != BoxKind.Anonymous)
            return false;

        for (int i = box.Boxes.Count - 1; i >= 0; i--)
        {
            var c = box.Boxes[i];
            if (c.Display == CssConstants.None
                || c.Position is CssConstants.Absolute or CssConstants.Fixed
                || c.Float != CssConstants.None)
                continue;

            return EndsWithAtomicInlineBlock(c);
        }

        return false;
    }

    /// <summary>
    /// How far below a box's top edge its baseline sits, for the purpose of aligning it on the line.
    /// </summary>
    /// <remarks>
    /// CSS2.1 §10.8 gives two answers. An ordinary inline box sits on the baseline of its own text,
    /// which is its font's ascent below its top. An <b>atomic</b> inline — an <c>inline-block</c>
    /// with no in-flow line boxes, and every inline <b>replaced</b> element — has its baseline at its
    /// bottom margin edge instead, so its whole height is ascent and it hangs above the line's
    /// baseline rather than straddling it.
    /// <para>
    /// Only the <c>inline-block</c> half of that was implemented, so an <c>&lt;img&gt;</c> was aligned
    /// by the ascent of a font it does not draw: every image on a line was placed the same ~13px
    /// below the line top regardless of its height, which reads as top-aligned and is what a page
    /// with images of two different heights on one line showed. The line-box <em>height</em> code has
    /// always assumed the other rule — <see cref="CreateLineBoxes"/> extends a line below a tall
    /// image by the strut's descent precisely because the image's bottom is the baseline — so the
    /// two halves disagreed with each other, not merely with the spec.
    /// </para>
    /// </remarks>
    private static double BaselineAscentOf(CssBox box, CssLineBox lineBox) =>
        IsAtomicInline(box) && lineBox.Rectangles.TryGetValue(box, out RectangleF rect)
            ? rect.Height
            : box.ActualFont.Height * PtToCssPx * TypicalAscentRatio;

    /// <summary>
    /// Whether the box is an atomic inline-level box whose baseline is its bottom margin edge: an
    /// <c>inline-block</c>, or an inline replaced element (an image — the only replaced content that
    /// reaches a line box as a word of its own).
    /// </summary>
    private static bool IsAtomicInline(CssBox box) =>
        box.Display == CssConstants.InlineBlock || box.IsImage;

    /// <summary>
    /// Whether a <c>vertical-align</c> value positions the box against the <em>parent's font
    /// metrics</em> rather than against the line's baseline — <c>middle</c>, <c>text-top</c> and
    /// <c>text-bottom</c> (CSS2.1 §10.8.1). <c>top</c>/<c>bottom</c> align against the line box
    /// itself and are handled by their own second pass; every other value, including
    /// <c>sub</c>/<c>super</c> and a length, is an offset <em>from</em> the baseline and so does
    /// help establish it.
    /// </summary>
    private static bool IsAlignedToParentFontMetrics(string verticalAlign) =>
        verticalAlign == CssConstants.Middle
        || verticalAlign == CssConstants.TextTop
        || verticalAlign == CssConstants.TextBottom;

    private static void ApplyVerticalAlignment(CssLineBox lineBox)
    {
        // CSS 2.1 §10.8: The baseline is where text sits, approximated as
        // the top of each box plus the font ascent. Most Latin fonts have
        // an ascent/height ratio near 0.8 (e.g. OS/2 sTypoAscender is
        // typically ~80% of UPM). This matches common browser heuristics.
        const double TypicalAscentRatio = 0.8;

        // CSS2.1 §10.8.1: Boxes with vertical-align:top or bottom do not
        // contribute to the initial line box height calculation.  Collect
        // them for a second positioning pass.
        var topBottomBoxes = new HashSet<CssBox>();
        foreach (var box in lineBox.Rectangles.Keys)
        {
            if (box.VerticalAlign == CssConstants.Top || box.VerticalAlign == CssConstants.Bottom)
                topBottomBoxes.Add(box);
        }

        // CSS2.1 §10.8: The "strut" — an imaginary zero-width inline box
        // with the block container's font and line-height — establishes
        // the initial baseline of the line box.  This is critical when the
        // parent has font-size: 0 (e.g. .buckets { font: 0/0 }): the strut
        // baseline is at the top of the content area and must not be
        // overridden by child inline-block font metrics.
        double lineTop = double.MaxValue;
        foreach (var kvp in lineBox.Rectangles)
        {
            if (!topBottomBoxes.Contains(kvp.Key))
                lineTop = Math.Min(lineTop, kvp.Value.Top);
        }

        // Start with the strut baseline (parent's font ascent from line top).
        double parentFontHeight = (lineBox.OwnerBox?.ActualFont.Height ?? 0) * PtToCssPx;
        double baseline = (lineTop < double.MaxValue)
            ? lineTop + parentFontHeight * TypicalAscentRatio
            : float.MinValue;

        // Non-inline-block boxes also contribute to the baseline — but only the ones that are
        // aligned *to* it. CSS2.1 §10.8.1: a box aligned `middle`, `text-top` or `text-bottom` is
        // positioned from the parent's font metrics, not from the line's baseline, so where its
        // own baseline would fall says nothing about where the line's is. Letting it push the
        // baseline down is self-defeating: the box is then placed relative to the baseline it just
        // moved, so it drives itself away from where it belongs. An `<img>` is the visible case,
        // because its baseline is its bottom edge — a `vertical-align: middle` image in a short
        // line put the baseline a whole image-height down and then centred the image on *that*,
        // leaving half an image of blank space above it. That is the band above the MediaWiki
        // thumbnail: the skin sets `line-height: 0` on the figure and `vertical-align: middle` on
        // the image, so the strut contributed nothing and the image's own bottom became the
        // baseline.
        foreach (var box in lineBox.Rectangles.Keys)
        {
            if (box.Display != CssConstants.InlineBlock
                && !topBottomBoxes.Contains(box)
                && !IsAlignedToParentFontMetrics(box.VerticalAlign))
            {
                double boxBaseline = lineBox.Rectangles[box].Top + BaselineAscentOf(box, lineBox);
                baseline = Math.Max(baseline, boxBaseline);
            }
        }

        // CSS2.1 §10.8.1: an atomic inline-block's baseline is its bottom margin edge, so two of
        // different heights on one line stand on a shared baseline — the shorter is pushed down
        // until their bottoms are flush. They were left where the flow put them, at the top of the
        // line, so they came out top-aligned instead; an inline <svg> beside a taller one is the
        // case this was found through, and a plain empty inline-block behaves identically.
        //
        // The shared bottom is taken from the atomic inlines themselves rather than from the
        // line's baseline. The strut's baseline is the spec's answer, but this engine computes it
        // without the half-leading that `line-height` contributes, so on a line whose strut is the
        // taller of the two it sits well below the text and would push an atomic inline that far
        // down the line — where today it is merely left at the top. Aligning them to each other
        // fixes the case that is wrong without resting on a number that is not yet right: it can
        // only ever move a box down onto a taller neighbour, and a line holding one atomic inline
        // (or none) is left exactly as it was.
        //
        // It is the *margin* box that stands on the baseline, and the rectangle here is the border
        // box, so the bottom margin is added on both sides of the comparison.
        double atomicInlineBottom = double.MinValue;
        foreach (var kvp in lineBox.Rectangles)
        {
            if (kvp.Key.UsesBottomMarginEdgeBaseline && !topBottomBoxes.Contains(kvp.Key))
                atomicInlineBottom = Math.Max(atomicInlineBottom, kvp.Value.Bottom + kvp.Key.ActualMarginBottom);
        }

        // --- Phase 1: Position all non-top/bottom boxes ---
        var boxes = new List<CssBox>(lineBox.Rectangles.Keys);
        foreach (CssBox box in boxes)
        {
            if (topBottomBoxes.Contains(box))
                continue;

            bool usesDefaultVerticalAlign = string.IsNullOrEmpty(box.VerticalAlign)
                || box.VerticalAlign == CssConstants.Baseline;

            if (usesDefaultVerticalAlign
                && box.UsesBottomMarginEdgeBaseline
                && atomicInlineBottom > double.MinValue)
            {
                lineBox.SetBaseLine(box,
                    atomicInlineBottom - box.ActualMarginBottom - lineBox.Rectangles[box].Height);
                continue;
            }

            // For inline text boxes, SetBaseLine receives the desired
            // word-top position, so baseline-relative values must be
            // converted from baseline Y to word-top Y by subtracting
            // the box's ascent.
            //
            // For inline-block and inline replaced boxes, CSS 2.1 §10.8.1: the
            // baseline of an inline-block with no in-flow line boxes, and of an
            // inline replaced element, is the bottom margin edge.  SetBaseLine
            // positions the box by its top, so we must subtract the box height to
            // convert from the desired bottom-edge position to the top-edge position.
            double boxAscent = BaselineAscentOf(box, lineBox);

            //Important notes on http://www.w3.org/TR/CSS21/tables.html#height-layout
            switch (box.VerticalAlign)
            {
                case CssConstants.Sub:
                    lineBox.SetBaseLine(box, baseline - boxAscent + lineBox.Rectangles[box].Height * .5f);
                    break;

                case CssConstants.Super:
                    lineBox.SetBaseLine(box, baseline - boxAscent - lineBox.Rectangles[box].Height * .2f);
                    break;

                case CssConstants.TextTop:
                    // CSS 2.1 §10.8.1: Align the top of the box with the
                    // top of the parent element's content area (font top).
                    if (baseline > float.MinValue)
                    {
                        double parentContentTop = baseline - parentFontHeight * TypicalAscentRatio;
                        lineBox.SetBaseLine(box, parentContentTop);
                    }
                    break;

                case CssConstants.TextBottom:
                    // CSS 2.1 §10.8.1: Align the bottom of the box with the
                    // bottom of the parent element's content area (font bottom).
                    if (baseline > float.MinValue && lineBox.Rectangles.TryGetValue(box, out RectangleF value))
                    {
                        double boxHeight = value.Height;
                        double parentContentBottom = baseline + parentFontHeight * (1.0 - TypicalAscentRatio);
                        lineBox.SetBaseLine(box, parentContentBottom - boxHeight);
                    }
                    break;

                case CssConstants.Middle:
                    // CSS 2.1 §10.8.1: Align the vertical midpoint of the box
                    // with the baseline plus half the x-height of the parent.
                    // x-height ≈ 0.5 × font height for Latin fonts; half of
                    // that is 0.25 × font height.
                    if (lineBox.Rectangles.TryGetValue(box, out RectangleF value1) && baseline > float.MinValue)
                    {
                        double boxHeight = value1.Height;
                        double parentFont = (box.ParentBox?.ActualFont.Height ?? 0) * PtToCssPx;
                        double halfXHeight = parentFont * 0.25;
                        lineBox.SetBaseLine(box, baseline + halfXHeight - boxHeight / 2);
                    }
                    break;

                default:
                    // CSS 2.1 §10.8.1: A <length> or <percentage> value
                    // raises (positive) or lowers (negative) the box by
                    // the given distance relative to the baseline.
                    // A percentage is calculated against the line-height
                    // of the element itself.
                    if (box.VerticalAlign != CssConstants.Baseline
                        && !string.IsNullOrEmpty(box.VerticalAlign))
                    {
                        double lineHeight = box.ActualLineHeight > 0
                            ? box.ActualLineHeight
                            : box.ActualFont.Height * PtToCssPx;
                        double offset = CssLengthParser.ParseLength(
                            box.VerticalAlign, lineHeight, box.GetEmHeight());

                        if (!double.IsNaN(offset) && offset != 0)
                        {
                            // Positive values move the box UP (raise).
                            lineBox.SetBaseLine(box, baseline - boxAscent - offset);
                            break;
                        }
                    }

                    //case: baseline
                    lineBox.SetBaseLine(box, baseline - boxAscent);
                    break;
            }
        }

        // --- Phase 2: Position top/bottom-aligned boxes ---
        // CSS 2.1 §10.8.1: After all other boxes are positioned, compute
        // the final line box extent and align top/bottom boxes within it.
        if (topBottomBoxes.Count > 0)
        {
            double finalTop = double.MaxValue;
            double finalBottom = double.MinValue;

            foreach (var kvp in lineBox.Rectangles)
            {
                if (!topBottomBoxes.Contains(kvp.Key))
                {
                    finalTop = Math.Min(finalTop, kvp.Value.Top);
                    finalBottom = Math.Max(finalBottom, kvp.Value.Bottom);
                }
            }

            // Also consider word positions for the final line box bounds.
            foreach (var word in lineBox.Words)
            {
                if (!topBottomBoxes.Contains(word.OwnerBox))
                {
                    finalTop = Math.Min(finalTop, word.Top);
                    finalBottom = Math.Max(finalBottom, word.Bottom);
                }
            }

            foreach (CssBox box in boxes)
            {
                if (!topBottomBoxes.Contains(box))
                    continue;

                if (box.VerticalAlign == CssConstants.Top)
                {
                    if (finalTop < double.MaxValue)
                        lineBox.SetBaseLine(box, finalTop);
                }
                else // Bottom
                {
                    if (finalBottom > double.MinValue && lineBox.Rectangles.TryGetValue(box, out RectangleF value))
                    {
                        double boxHeight = value.Height;
                        lineBox.SetBaseLine(box, finalBottom - boxHeight);
                    }
                }
            }
        }
    }

    private static void ApplyJustifyAlignment(CssLineBox lineBox)
    {
        // Whether the block's last line is justified is decided by the caller
        // (ApplyHorizontalAlignment): under a plain text-align:justify the last
        // line is routed to 'start' and never reaches here, so this method
        // unconditionally stretches whatever line it is given.  A single-word line
        // has nothing to stretch and is left flush at the line start below.
        double indent = lineBox.Equals(lineBox.OwnerBox.LineBoxes[0]) ? lineBox.OwnerBox.ActualTextIndent : 0f;
        double textSum = 0f;
        double words = 0f;
        double availWidth = lineBox.OwnerBox.ClientRectangle.Width - indent;

        // Gather text sum
        foreach (CssRect w in lineBox.Words)
        {
            textSum += w.Width;
            words += 1f;
        }

        if (words <= 0f)
            return; //Avoid Zero division

        double spacing = (availWidth - textSum) / words; //Spacing that will be used
        double curx = lineBox.OwnerBox.ClientLeft + indent;

        // A line with a single word (common on a justify-all / text-align-last:justify
        // last line) has no inter-word gaps to stretch: it stays flush at the start
        // edge rather than being pushed to the right edge.
        bool stretch = lineBox.Words.Count > 1;

        foreach (CssRect word in lineBox.Words)
        {
            word.Left = curx;
            curx = word.Right + spacing;

            if (stretch && word == lineBox.Words[^1])
                word.Left = lineBox.OwnerBox.ClientRight - word.Width;
        }
    }

    private static void ApplyCenterAlignment(CssLineBox line)
    {
        if (line.Words.Count == 0 && line.Rectangles.Count == 0)
            return;

        double right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth;

        // Find the rightmost content edge from both words and inline-block rectangles.
        // Lines may contain only inline-block elements (e.g. form controls inside
        // <center>) with no direct text words.
        double contentRight = 0;
        if (line.Words.Count > 0)
        {
            CssRect lastWord = line.Words[^1];
            contentRight = lastWord.Right + lastWord.OwnerBox.ActualBorderRightWidth + lastWord.OwnerBox.ActualPaddingRight;
        }

        foreach (var kvp in line.Rectangles)
        {
            if (kvp.Value.Right > contentRight)
                contentRight = kvp.Value.Right;
        }

        double diff = (right - contentRight) / 2;

        if (diff <= 0)
            return;

        foreach (CssRect word in line.Words)
            word.Left += diff;

        foreach (CssBox b in ToList(line.Rectangles.Keys))
        {
            RectangleF r = line.Rectangles[b];
            line.Rectangles[b] = new RectangleF((float)(r.X + diff), r.Y, r.Width, r.Height);
            ShiftInlineBlockBox(b, diff);
        }
    }

    private static void ApplyRightAlignment(CssLineBox line)
    {
        if (line.Words.Count == 0 && line.Rectangles.Count == 0)
            return;

        double right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth;

        // Find the rightmost content edge from both words and inline-block rectangles.
        double contentRight = 0;
        if (line.Words.Count > 0)
        {
            CssRect lastWord = line.Words[^1];
            contentRight = lastWord.Right + lastWord.OwnerBox.ActualBorderRightWidth + lastWord.OwnerBox.ActualPaddingRight;
        }

        foreach (var kvp in line.Rectangles)
        {
            if (kvp.Value.Right > contentRight)
                contentRight = kvp.Value.Right;
        }

        double diff = right - contentRight;

        if (diff <= 0)
            return;

        foreach (CssRect word in line.Words)
            word.Left += diff;

        foreach (CssBox b in ToList(line.Rectangles.Keys))
        {
            RectangleF r = line.Rectangles[b];
            line.Rectangles[b] = new RectangleF((float)(r.X + diff), r.Y, r.Width, r.Height);
            ShiftInlineBlockBox(b, diff);
        }
    }

    /// <summary>
    /// Shifts an inline-block box and all its descendant boxes horizontally.
    /// Called by <see cref="ApplyCenterAlignment"/> and <see cref="ApplyRightAlignment"/>
    /// to ensure the box's actual <see cref="CssBox.Location"/> matches the shifted
    /// line-box rectangle, so background, border, and child content paint at the
    /// correct position.  CSS 2.1 §9.4.2.
    /// </summary>
    private static void ShiftInlineBlockBox(CssBox b, double dx)
    {
        if (b.Display != CssConstants.InlineBlock)
            return;

        b.Location = new PointF((float)(b.Location.X + dx), b.Location.Y);

        // Shift all descendant boxes so child content (text, nested boxes)
        // renders at the correct position.
        ShiftDescendantBoxes(b, dx);

        // Shift rectangles already assigned to the inline-block from its own
        // line boxes (via BubbleRectangles + AssignRectanglesToBoxes that ran
        // before centering).  Without this, the FragmentTreeBuilder captures
        // stale InlineRects at the original position, causing double borders.
        ShiftAssignedRectangles(b, dx);
    }

    private static void ShiftDescendantBoxes(CssBox parent, double dx)
    {
        foreach (var child in parent.Boxes)
        {
            child.Location = new PointF((float)(child.Location.X + dx), child.Location.Y);

            // Shift rectangles already assigned to this child.
            ShiftAssignedRectangles(child, dx);

            ShiftDescendantBoxes(child, dx);
        }

        // Shift words and rectangles within this box's own line boxes.
        foreach (var lineBox in parent.LineBoxes)
        {
            foreach (var word in lineBox.Words)
                word.Left += dx;

            foreach (var key in ToList(lineBox.Rectangles.Keys))
            {
                var r = lineBox.Rectangles[key];
                lineBox.Rectangles[key] = new RectangleF((float)(r.X + dx), r.Y, r.Width, r.Height);
            }
        }
    }

    /// <summary>
    /// Shifts all per-line-box rectangles that have been assigned to a box
    /// (via <see cref="CssLineBox.AssignRectanglesToBoxes"/>) by <paramref name="dx"/>
    /// pixels horizontally.
    /// </summary>
    private static void ShiftAssignedRectangles(CssBox box, double dx)
    {
        if (box.Rectangles.Count == 0)
            return;

        foreach (var key in ToList(box.Rectangles.Keys))
        {
            var r = box.Rectangles[key];
            box.Rectangles[key] = new RectangleF((float)(r.X + dx), r.Y, r.Width, r.Height);
        }
    }

    /// <summary>
    /// todo: optimizate, not creating a list each time
    /// </summary>
    private static List<T> ToList<T>(IEnumerable<T> collection)
    {
        List<T> result = [.. collection];
        return result;
    }
}
