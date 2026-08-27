using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// PROTOTYPE (BROILER_VERTICAL_FLOW): see
    /// <see cref="CssBoxProperties.WillBeVerticalTransposed"/>.  Walk up the box
    /// tree: the first vertical-writing-mode ancestor (or this box) whose own
    /// parent is NOT vertical is a rotation root and transposes this box.  An
    /// out-of-flow ancestor establishes its own rotation context, so if one is
    /// reached before any vertical root, the rotation of a further vertical
    /// ancestor does not reach this box — matching the runtime, where an abspos
    /// item in a vertical container is left untransposed (its container's
    /// rotation skips it and it is not a root itself).
    /// </summary>
    protected override bool WillBeVerticalTransposed()
    {
        if (!VerticalFlowPrototype.Enabled)
            return false;
        for (CssBox ctx = this; ctx != null; ctx = ctx.ParentBox)
        {
            bool ctxVertical = IsVerticalWritingMode(ctx.WritingMode);
            bool parentVertical = ctx.ParentBox != null
                && IsVerticalWritingMode(ctx.ParentBox.WritingMode);
            if (ctxVertical && !parentVertical)
                return true;
            if (ctx.Position == CssConstants.Absolute || ctx.Position == CssConstants.Fixed)
                return false;
        }
        return false;
    }

    /// <summary>
    /// PROTOTYPE (BROILER_VERTICAL_FLOW), Stage 1: rotate this vertical
    /// writing-mode root's subtree from the logical horizontal layout frame
    /// into physical space.  The inline axis (laid out left→right) becomes
    /// top→bottom; the block axis (laid out top→bottom) becomes left→right
    /// for <c>vertical-lr</c> or right→left for <c>vertical-rl</c>.
    ///
    /// Positions and box/line rectangle extents are swapped; glyph runs keep
    /// their horizontal size (no glyph rotation yet — Stage 2), so this is
    /// positionally correct for square fonts and an approximation otherwise.
    /// </summary>
    internal void ApplyVerticalWritingModeFlow()
    {
        // Capture the root's logical origin and block extent (its logical
        // height) before any coordinate is rewritten.  For vertical-rl the
        // block extent is mirrored so the first line sits at the right edge.
        float rootX = Location.X;
        float rootY = Location.Y;
        double logicalBlockExtent = ActualBottom - Location.Y;
        bool mirror = WritingMode is "vertical-rl" or "sideways-rl";

        // sideways-lr is the one vertical mode whose inline axis runs
        // bottom→top (CSS Writing Modes 4 §3.1): its block flow is left→right
        // like vertical-lr (so it is NOT `mirror`), but the first inline
        // position sits at the *bottom* and later characters stack upward, and
        // its glyphs face the opposite way (rotated 90° counter-clockwise —
        // handled at paint in FragmentTreeBuilder). The logical frame laid the
        // inline axis out left→right as usual; flip it about the box's inline
        // extent (its physical height = the pre-rotation frame width) so the
        // run reads bottom→top. Every other vertical mode keeps inline top→bottom.
        bool sidewaysLr = string.Equals(WritingMode?.Trim(), "sideways-lr", StringComparison.OrdinalIgnoreCase);
        double logicalInlineExtent = Size.Width;

        // Where the rotated root's border-box sits horizontally depends on whether
        // its writing mode is the *principal* (viewport) writing mode or a local
        // orthogonal flow:
        //
        //  • Principal writing mode — a vertical-rl root/body whose value
        //    propagates to the viewport (CSS Writing Modes §3.1). The whole page's
        //    block flow runs right→left, so its content begins at the viewport's
        //    right edge. This logical frame shrink-wrapped the root to its content
        //    width and left-aligned it, so shift the rotated subtree right until
        //    the root's block-start (right) edge meets the containing block's
        //    content-right — putting the first block at the top-right corner.
        //
        //  • Local orthogonal flow — a vertical-rl block nested inside a
        //    horizontal-tb (or vertical-lr) containing block. Its border-box is
        //    placed by that containing block's own flow (inline-start / left for an
        //    LTR horizontal container), independent of the box's own writing mode;
        //    Chromium left-aligns such a box even with a definite width. Its
        //    block-start being on the right only governs where its *content* flows
        //    (right→left, handled by the `mirror` transform on descendants and
        //    words below), not where the box itself is positioned — so no shift.
        //
        // The rotation-root test (this box vertical, its parent not) already
        // guarantees a `<body>` reaches here only when its parent `<html>` is
        // horizontal-tb, i.e. exactly when the body's writing mode propagates to
        // the viewport — so "root element or body" is the principal-WM signal.
        // Out-of-flow and inline-level roots are positioned by their own machinery
        // (abspos self-alignment, the inline formatting context) and keep offset 0.
        bool establishesPrincipalWm = ParentBox == null
            || (HtmlTag is { } tag
                && (tag.Name.Equals("body", StringComparison.OrdinalIgnoreCase)
                    || tag.Name.Equals("html", StringComparison.OrdinalIgnoreCase)));

        // A right-floated orthogonal box is a second right-anchored case. Float
        // placement ran in the logical horizontal frame, where it pinned the box's
        // *logical* right (its physical bottom) to the container's content-right —
        // leaving the rotated border-box short of that edge by the difference
        // between its logical and physical widths. Re-pin its physical right edge
        // to the container's content-right so it renders flush against it, matching
        // the block-start alignment the principal-WM shift performs. (Left floats
        // and in-flow boxes are already at their correct physical left, offset 0.)
        bool rightAnchored = establishesPrincipalWm || Float == CssConstants.Right;

        double blockOffset = 0;
        if (mirror && rightAnchored
            && Position != CssConstants.Absolute && Position != CssConstants.Fixed
            && Display != CssConstants.InlineBlock
            && Display != "inline-flex" && Display != "inline-grid"
            && ContainingBlock is { } cb)
        {
            double cbContentRight = cb.Location.X + cb.Size.Width
                - cb.ActualPaddingRight - cb.ActualBorderRightWidth;
            double desiredRootRight = cbContentRight - ActualMarginRight;
            double currentRootRight = rootX + logicalBlockExtent;

            blockOffset = desiredRootRight - currentRootRight;
        }

        TransformVerticalSubtree(this, rootX, rootY, logicalBlockExtent, mirror, blockOffset,
            sidewaysLr, logicalInlineExtent, isRoot: true);
    }

    private static void TransformVerticalSubtree(
        CssBox box, float rootX, float rootY, double blockExtent, bool mirror, double blockOffset,
        bool sidewaysLr, double inlineExtent, bool isRoot)
    {
        // --- Box border-box: Location + Size ---
        // logical (x,y) measured from root origin → physical (y,x): the inline
        // offset becomes vertical, the block offset becomes horizontal.
        double logicalLeft = box.Location.X - rootX;
        double logicalTop = box.Location.Y - rootY;
        double logicalWidth = box.Size.Width;
        double logicalHeight = box.ActualBottom - box.Location.Y;

        // The root keeps its own physical origin (the parent placed it in the
        // horizontal frame), apart from the right-alignment shift; only its size
        // is rotated.  Descendants rotate their position relative to the root.
        if (!isRoot)
        {
            double physLeft = mirror
                ? blockExtent - logicalTop - logicalHeight
                : logicalTop;

            box.Location = new PointF(rootX + (float)(blockOffset + physLeft), rootY + (float)logicalLeft);
        }
        else if (blockOffset != 0)
        {
            box.Location = new PointF(rootX + (float)blockOffset, rootY);
        }

        box.Size = new SizeF((float)logicalHeight, (float)logicalWidth);
        box.ActualBottom = box.Location.Y + logicalWidth;

        // Per-line rectangles cached on the box itself (inline backgrounds).
        var boxRectKeys = new List<CssLineBox>(box.Rectangles.Keys);

        foreach (var k in boxRectKeys)
            box.Rectangles[k] = RotateRect(box.Rectangles[k], rootX, rootY, blockExtent, mirror, blockOffset);

        // --- Line boxes owned by this box: words + per-box rectangles ---
        //
        // A run decomposed into per-glyph cells below replaces the original CssRect in the LINE's
        // word list, but a word lives in two lists: CssLineBox.Words (what paint walks) and
        // CssBox.Words (what OffsetTop/OffsetLeft walk). Leaving the box's copy behind desynchronises
        // them, and every post-rotation pass that translates a subtree then moves the run nobody
        // paints while the glyphs that are painted stay put. RunScrollSimulation is exactly such a
        // pass and runs after this one (PerformLayout calls the rotation, then the scroll post-pass),
        // so a scrolled vertical-writing-mode page painted its backgrounds at the scrolled offset and
        // its text at the unscrolled one — visible on WPT massive-element-left-of-viewport-
        // partially-onscreen-ref, whose green/blue bands scrolled while the Ahem glyphs stayed at the
        // document origin. Substituting the glyphs into the owner box's list keeps the two views of
        // the same run in agreement.
        Dictionary<CssBox, Dictionary<CssRect, List<CssRect>>>? decomposedByOwner = null;

        foreach (var line in box.LineBoxes)
        {
            var rotatedWords = new List<CssRect>(line.Words.Count);

            foreach (var word in line.Words)
            {
                // Column X = the line's block offset (lines advance left→right
                // for vertical-lr, right→left for vertical-rl).  Column Y top =
                // the word's inline offset (the inline axis runs top→bottom,
                // except sideways-lr which runs bottom→top — see InlineTop).
                double wLeft = word.Left - rootX;
                double wTop = word.Top - rootY;
                double colX = rootX + blockOffset + (mirror ? blockExtent - wTop - word.Height : wTop);

                // Map an inline offset+size onto the physical Y of a cell's top.
                // Normal vertical modes: inline runs top→bottom (Y = origin +
                // offset). sideways-lr: inline runs bottom→top, so flip the cell
                // about the box's inline extent.
                double InlineTop(double inlineOffset, double inlineSize) => sidewaysLr
                    ? rootY + inlineExtent - inlineOffset - inlineSize
                    : rootY + inlineOffset;

                // Stage 3: decompose a multi-glyph run into per-glyph cells
                // stacked along the inline (vertical) axis.  Each glyph advances
                // by its inline advance (≈ run width / glyph count — exact for
                // monospace/square fonts, approximate otherwise).  This is what
                // the position-only transform alone could not do: without it a
                // run paints horizontally and overlaps the next column.
                string text = word.Text;
                if (text != null && text.Length > 1 && !word.IsLineBreak)
                {
                    double advance = word.Width / text.Length;
                    var glyphs = new List<CssRect>(text.Length);

                    for (int i = 0; i < text.Length; i++)
                    {
                        var glyph = new CssRectWord(word.OwnerBox, text[i].ToString(), false, false)
                        {
                            Left = colX,
                            Top = InlineTop(wLeft + i * advance, advance),
                            Width = advance,
                            Height = word.Height,
                        };

                        glyphs.Add(glyph);
                        rotatedWords.Add(glyph);
                    }

                    if (word.OwnerBox is { } owner)
                    {
                        decomposedByOwner ??= [];
                        if (!decomposedByOwner.TryGetValue(owner, out var map))
                            decomposedByOwner[owner] = map = [];
                        map[word] = glyphs;
                    }
                }
                else
                {
                    word.Left = colX;
                    word.Top = InlineTop(wLeft, word.Width);
                    rotatedWords.Add(word);
                }
            }

            line.Words.Clear();
            line.Words.AddRange(rotatedWords);

            var keys = new List<CssBox>(line.Rectangles.Keys);

            foreach (var k in keys)
                line.Rectangles[k] = RotateRect(line.Rectangles[k], rootX, rootY, blockExtent, mirror, blockOffset);
        }

        // Mirror the line-level substitution onto each owner box's own word list, in place, so the
        // two stay the same run in the same order. A word not decomposed above (a single glyph, a
        // line break) was rotated by mutation and is already shared by both lists.
        if (decomposedByOwner is not null)
        {
            foreach (var (owner, map) in decomposedByOwner)
            {
                var rebuilt = new List<CssRect>(owner.Words.Count);

                foreach (var existing in owner.Words)
                {
                    if (map.TryGetValue(existing, out var glyphs))
                        rebuilt.AddRange(glyphs);
                    else
                        rebuilt.Add(existing);
                }

                owner.Words.Clear();
                owner.Words.AddRange(rebuilt);
            }
        }

        foreach (var child in box.Boxes)
        {
            if (child.Display == CssConstants.None)
                continue;

            // Out-of-flow descendants are excluded from the rotation: an
            // absolutely/fixed-positioned box is placed in *physical* space by
            // the abspos self-alignment (which is already writing-mode aware and,
            // per WillBeVerticalTransposed, treats abspos boxes as not
            // transposed). Rotating it here would apply the transform twice and
            // tear it away from its resolved position (WPT css-align/abspos
            // *-default-overflow-vrl-* regressed when an inline-block vertical
            // container began rotating its abspos children).
            if (child.Position is CssConstants.Absolute or CssConstants.Fixed)
                continue;

            TransformVerticalSubtree(child, rootX, rootY, blockExtent, mirror, blockOffset,
                sidewaysLr, inlineExtent, isRoot: false);
        }
    }

    private static RectangleF RotateRect(RectangleF r, float rootX, float rootY, double blockExtent, bool mirror, double blockOffset)
    {
        double logicalLeft = r.X - rootX;
        double logicalTop = r.Y - rootY;
        double physLeft = mirror ? blockExtent - logicalTop - r.Height : logicalTop;

        return new RectangleF(
            rootX + (float)(blockOffset + physLeft),
            rootY + (float)logicalLeft,
            r.Height,
            r.Width);
    }

}
