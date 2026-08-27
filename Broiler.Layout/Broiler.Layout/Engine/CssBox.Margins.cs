using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// CSS Box Model 4 §6.2: Applies <c>margin-trim</c> to this box by zeroing
    /// the block-start margin of its first in-flow block-level child and/or the
    /// block-end margin of its last in-flow block-level child, as requested by
    /// the property value (<c>block</c>, <c>block-start</c>, <c>block-end</c>).
    /// Inline-axis trimming is not yet supported.
    /// </summary>
    private void ApplyMarginTrim()
    {
        if (string.IsNullOrEmpty(MarginTrim) || MarginTrim == CssConstants.None)
            return;

        bool trimBlockStart = false;
        bool trimBlockEnd = false;

        foreach (var token in MarginTrim.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "block":
                    trimBlockStart = true;
                    trimBlockEnd = true;
                    break;

                case "block-start":
                    trimBlockStart = true;
                    break;

                case "block-end":
                    trimBlockEnd = true;
                    break;
            }
        }

        if (!trimBlockStart && !trimBlockEnd)
            return;

        CssBox first = null;
        CssBox last = null;

        foreach (var child in Boxes)
        {
            if (child.Display == CssConstants.None
                || child.Position == CssConstants.Absolute
                || child.Position == CssConstants.Fixed
                || child.Float != CssConstants.None
                || child.IsInline)
                continue;

            first ??= child;
            last = child;
        }

        if (trimBlockStart && first != null)
            first.MarginTop = "0";

        if (trimBlockEnd && last != null)
            last.MarginBottom = "0";
    }

    /// <summary>
    /// Clears the margin-collapse result this box carries from a previous layout pass, at the top of
    /// its next one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a layout needs this at all.</b> <see cref="CssBoxProperties.CollapsedMarginTop"/> is
    /// not an input; it is what <see cref="MarginTopCollapse"/> decided last time, kept so that a
    /// following sibling can subtract the part of a collapsed margin already spent and so that a
    /// first child can tell how much of its top margin the parent has already absorbed. Every writer
    /// sets it during a layout pass — and nothing cleared it between passes, so a <em>second</em>
    /// layout of the same box tree read the first pass's answer as if it were the parent's own
    /// margin.
    /// </para>
    /// <para>
    /// <b>What that cost.</b> The first in-flow child's top margin propagates by shifting the parent
    /// down (the branch below), and it does that only when the child's margin exceeds
    /// <c>max(parent margin, parent CollapsedMarginTop)</c>. On the first pass that comparison is
    /// against zero and the shift happens; on the second, the stale value equals the child's own
    /// margin, the comparison fails, and the shift does not — so the document lays out two pixels
    /// shorter the second time, for
    /// <c>&lt;div style="margin-top:2px"&gt;</c> directly inside <c>&lt;body&gt;</c>. Nothing in this
    /// repository laid the same tree out twice before item #14, which is why a pass-dependent result
    /// could sit here: every relayout rebuilt the tree from scratch and got a fresh box. The moment
    /// a rebuild can be skipped, "lay out the same tree again" has to mean what "build it and lay it
    /// out" meant, and <c>--relayout-parity</c> is what caught that it did not.
    /// </para>
    /// <para>
    /// Resetting at the top of the box's own layout is the point where no writer has run yet for this
    /// pass: a box's own value is written while it is being positioned, and the value a first child
    /// writes onto its <em>parent</em> is written after the parent's own layout has begun. Both are
    /// downstream of this.
    /// </para>
    /// </remarks>
    protected void ResetCollapsedMarginState() => CollapsedMarginTop = 0;

    protected double MarginTopCollapse(CssBoxProperties prevSibling)
    {
        double value;

        // How much margin ends up standing above this box's top edge, which is what a first
        // in-flow child of this box collapses against. It equals `value` except where a
        // collapse-through hands on a margin that was partly spent before it.
        double? spentAboveThisBox = null;

        if (prevSibling != null)
        {
            // CSS2.1 §8.3.1: When the previous sibling is an "empty" box
            // (zero content height, no borders/padding, height auto/0), its
            // own top and bottom margins — and its children's margins —
            // collapse through.  The resulting collapsed margin participates
            // in collapsing with this element's top margin.
            if (prevSibling is CssBox prevBox && CssBoxHelper.IsEmptyCollapsible(prevBox))
            {
                double maxPos = Math.Max(ActualMarginTop, 0);
                double maxNeg = Math.Min(ActualMarginTop, 0);
                CssBoxHelper.CollectEmptyBoxMargins(prevBox, ref maxPos, ref maxNeg);
                double collapsed = maxPos + maxNeg; // maxNeg <= 0

                // Subtract the portion of the collapsed margin already consumed when
                // positioning the empty box itself (its CollapsedMarginTop was recorded during
                // its own layout). The difference is taken whole — including when it comes out
                // negative, which is how this box lands *above* the empty one's border edge.
                // That is the correct outcome whenever the run's collapsed value is smaller
                // than what was spent placing the empty box, and CSS2.1 §8.3.1's
                // collapse-through rules produce exactly that as soon as anything in the run
                // carries a negative margin: Acid2's `.empty` (margin 6.25em, one child with
                // `margin-bottom: -6em`) spends 75px placing itself and collapses to 3px, so
                // the `.smile` after it belongs 72px higher — high enough for its `clear: both`
                // to take effect, without which the face was cut in half by a 15px gap.
                value = collapsed - prevBox.CollapsedMarginTop;

                // The guard the old clamp was really after: a collapse may not pull a box above
                // its parent's content edge unless the run itself collapsed to a negative
                // margin. Margin already spent placing the empty box can come from the *parent*
                // rather than from this sibling run — www.mediawiki.org's empty #centralNotice
                // sits under its container's 24px margin — and cancelling that would draw the
                // next box 18px above the box that owns it. Flooring the resulting position
                // keeps that honest while leaving genuine negative margins free to lift the box.
                if (_parentBox != null)
                {
                    double floorTop = _parentBox.ClientTop + Math.Min(collapsed, 0);
                    if (prevBox.ActualBottom + value < floorTop)
                        value = floorTop - prevBox.ActualBottom;
                }

                // What stands above this box's top edge is the whole set — the part this
                // collapse adds and the part that was already there. A first in-flow child of
                // *this* box collapses with that total, not with what was left to add, so
                // recording only `value` makes a margin one level down reappear as a gap.
                spentAboveThisBox = Math.Max(prevBox.CollapsedMarginTop, collapsed);
            }
            else
            {
                // CSS2.1 §8.3.1: Adjoining vertical margins collapse.
                // When both are positive → max(m1, m2).
                // When one is negative  → max(positives,0) + min(negatives,0).
                // When both are negative → 0 + min(m1,m2) = most-negative.
                // The general formula covers all three cases.
                // Use GetPropagatedMarginBottom so that a last-child's
                // bottom margin propagates through its parent when the
                // parent has no bottom border/padding and auto height
                // (CSS 2.1 §8.3.1 parent-child bottom-margin collapse).
                double prevMb = (prevSibling is CssBox prevSibBox)
                    ? CssBoxHelper.GetPropagatedMarginBottom(prevSibBox)
                    : prevSibling.ActualMarginBottom;
                double maxPos = Math.Max(
                    Math.Max(prevMb, 0),
                    Math.Max(ActualMarginTop, 0));
                double minNeg = Math.Min(
                    Math.Min(prevMb, 0),
                    Math.Min(ActualMarginTop, 0));

                value = maxPos + minNeg;
            }

            CollapsedMarginTop = spentAboveThisBox ?? value;

        }
        // CSS2.1 §8.3.1: "Margins of absolutely positioned boxes do not collapse." This branch is
        // reached by any box with no previous *in-flow* sibling — GetPreviousSibling already skips
        // out-of-flow ones — so an absolutely positioned first child was collapsing with its parent
        // and, worse, propagating its excess margin into the parent's Location below, which drags
        // every in-flow sibling down with it. A page whose first child is a fixed backdrop or an
        // abspos panel therefore laid its real content out at the wrong offset.
        else if (_parentBox != null && Position is not (CssConstants.Absolute or CssConstants.Fixed)
            // CSS2.1 §8.3.1 again, for the other kind of out-of-flow box: "margins of floating
            // boxes do not collapse". A float's margin was propagating into its parent's position
            // exactly as an abspos child's used to, so a block whose first child is a floated
            // element was drawn at the float's margin rather than its own — which is what left
            // css-flexbox/flexbox_item-bottom-float's *reference* an em below its test.
            && Float == CssConstants.None
            && _parentBox.ActualPaddingTop < 0.1 && _parentBox.ActualPaddingBottom < 0.1 && _parentBox.ActualBorderTopWidth < 0.1 && _parentBox.ActualBorderBottomWidth < 0.1
            // CSS2.1 §8.3.1: "margins of elements that establish new block formatting contexts
            // do not collapse with their in-flow children" — so a parent that establishes one
            // contains its first child's top margin instead of taking it as its own. The two
            // triggers spelled out here before, `overflow` other than `visible` (§9.4.1, e.g.
            // css-anchor-position anchor-center-scroll-001's scroller) and CSS Box Alignment
            // §5.4's `align-content`, are two of the set `EstablishesBfc` already answers for;
            // the ones it adds are what the rest of this run needed. A **float** parent: the
            // reference of css-flexbox/flex-lines/multi-line-wrap-with-column-reverse is three
            // floated columns of `margin-top: 10px` paragraphs, and each float was drawn at its
            // first paragraph's margin — then the next float below that, so the three columns
            // stepped 10px further down each. And a **flex or grid container**, which CSS
            // Flexbox §3 / CSS Grid §6 make an independent formatting context: that test itself
            // drew its whole flex container 10px below where it belongs.
            && !CssBoxHelper.EstablishesBfc(_parentBox))
        {
            double parentEffective = Math.Max(_parentBox.ActualMarginTop, _parentBox.CollapsedMarginTop);

            // CSS2.1 §8.3.1: First in-flow child's top margin collapses
            // with the parent's top margin when the parent has no top
            // border and no top padding.  When the child's margin
            // exceeds the parent's, propagate the excess upward by
            // shifting the parent's position down.  Only do this for
            // non-root containers (not html/body) to avoid disturbing
            // the root element's established position.
            if (ActualMarginTop > parentEffective + 0.1
                && _parentBox.ParentBox != null
                && _parentBox.ParentBox.ParentBox != null)
            {
                double propagation = ActualMarginTop - parentEffective;

                // Move what is already inside the parent with it. Only the parent's own origin
                // used to move, and anything positioned before this box got there — a preceding
                // float, or a whole subtree on a second layout pass — stayed where the old origin
                // had put it, so the box's content rendered outside its own border box.
                // www.mediawiki.org's site notice did exactly that: its border box moved down by
                // the margin its first block child propagated, and the notice text stayed above it.
                _parentBox.OffsetTop(propagation);
                _parentBox.CollapsedMarginTop = ActualMarginTop;

                value = 0;
            }
            else
            {
                value = Math.Max(0, ActualMarginTop - parentEffective);
            }

            // Record the margin already spent above this box's top edge — the collapsed set's,
            // not merely this box's own, since the set is what positioned the parent. An
            // empty-collapsible box hands its margins on to the next sibling (the prevSibling
            // branch above), and that sibling has to subtract what was already spent or it is
            // applied twice. www.mediawiki.org opens its article body with exactly that box —
            // an empty <p> holding only a <style> and two abspos spans, `margin: 0.5em 0 1em` —
            // and its 1em collapse-through was landing on top of the 1em already applied,
            // pushing the whole article down.
            CollapsedMarginTop = Math.Max(parentEffective, ActualMarginTop) - value;
        }
        else
        {
            value = ActualMarginTop;

            // When the parent establishes a BFC, the first child's margin is fully consumed for
            // positioning. Record it so that an empty-collapsible sibling can subtract
            // the already-consumed portion during its own collapse.
            if (_parentBox != null && CssBoxHelper.EstablishesBfc(_parentBox))
                CollapsedMarginTop = value;
        }

        // fix for hr tag
        if (value < 0.1 && HtmlTag != null && HtmlTag.Name == "hr")
            value = GetEmHeight() * 1.1f;

        return value;
    }

    public bool BreakPage()
    {
        var container = LayoutEnvironment;

        if (Size.Height >= container.PageSize.Height)
            return false;

        var remTop = (Location.Y - container.MarginTop) % container.PageSize.Height;
        var remBottom = (ActualBottom - container.MarginTop) % container.PageSize.Height;

        if (remTop > remBottom)
        {
            var diff = container.PageSize.Height - remTop;
            Location = new PointF(Location.X, (float)(Location.Y + diff + 1));
            
            return true;
        }

        return false;
    }

    private double CalculateActualRight()
    {
        if (ActualRight <= 90999)
            return ActualRight;

        var maxRight = 0d;

        foreach (var box in Boxes)
            maxRight = Math.Max(maxRight, box.ActualRight + box.ActualMarginRight);

        return maxRight + ActualPaddingRight + ActualMarginRight + ActualBorderRightWidth;
    }

    private double MarginBottomCollapse()
    {
        double margin = 0;

        // NOTE: When the last in-flow child's bottom margin collapses through
        // this box (computed below, once the last child is known) the collapsed
        // margin is NOT included in this box's height — it is external spacing
        // propagated to the parent via GetPropagatedMarginBottom().  The
        // `margin` variable stays 0.

        // CSS2.1 §10.6.3 / §10.6.7: Floated children contribute to the
        // height of their parent only when the parent establishes a new
        // block formatting context (BFC).  Non-BFC blocks (e.g. a plain
        // <ul> inside a floated <dd>) must not include descendant floats
        // in their height calculation.
        bool isBfc = CssBoxHelper.EstablishesBfc(this);

        // Use the maximum ActualBottom across all children to handle
        // floated children that may not be the last in source order.
        // Initialize to the content-area top so that padding is preserved
        // even when all children are floated (CSS2.1 §10.6.3: content
        // height is zero but padding is additive).
        double maxChildBottom = Location.Y + ActualBorderTopWidth + ActualPaddingTop;
        CssBox lastInFlowChild = null;
        
        foreach (var child in Boxes)
        {
            // CSS2.1 §10.6.3: Only children in the normal flow are taken
            // into account.  Absolutely positioned and fixed-position boxes
            // are out of flow and must not influence the parent's auto height.
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;

            if (!isBfc && child.Float != CssConstants.None)
                continue;

            // CSS2.1 §9.4.3: Relative positioning is visual-only and
            // does not affect the flow position used for auto-height
            // calculation.  Undo the relative offset so the parent
            // measures the child's normal-flow bottom.
            double childBottom = child.ActualBottom;

            if (child.Position == CssConstants.Relative)
                childBottom -= CssBoxHelper.GetRelativeOffsetY(child);

            maxChildBottom = Math.Max(maxChildBottom, childBottom);
            lastInFlowChild = child;
        }

        // CSS2.1 §10.6.7: When a BFC root auto-sizes its height it must
        // extend to contain all descendant floats — not only direct-child
        // floats.  Walk the subtree (stopping at nested BFC boundaries)
        // to find the maximum float bottom.
        if (isBfc)
        {
            double maxFloatDesc = maxChildBottom;

            FindMaxDescendantFloatBottom(this, ref maxFloatDesc);
            maxChildBottom = Math.Max(maxChildBottom, maxFloatDesc);
        }

        // CSS2.1 §8.3.1 / §10.6.3: The auto height extends to the bottom
        // margin-edge of the last in-flow child unless that child's bottom
        // margin collapses through this box.  Collapse-through happens when
        // this box has no bottom border or padding, an auto (or
        // auto-resolved) height, and a block-level last in-flow child.  This
        // must match the condition used by GetPropagatedMarginBottom() (which
        // propagates the same margin to the parent): otherwise the child's
        // margin is double-counted — once inside this box's height and once as
        // external spacing.  Note this does NOT depend on whether this box is
        // its own parent's last child, nor on this box's own bottom margin.
        bool autoHeight = Height == CssConstants.Auto || string.IsNullOrEmpty(Height)
            || (Height.Contains('%')
                && (ContainingBlock == null || ContainingBlock.Height == CssConstants.Auto
                    || string.IsNullOrEmpty(ContainingBlock.Height)));

        bool collapseThrough = lastInFlowChild != null
            && ActualPaddingBottom < 0.1 && ActualBorderBottomWidth < 0.1
            && autoHeight
            // CSS2.1 §8.3.1: margins of the root element's box do not collapse, so the
            // body's bottom margin stays inside the root's height instead of propagating
            // out of it (where nothing would ever contain it, shortening the canvas).
            && !CssBoxHelper.IsRootElement(this)
            && lastInFlowChild.Float == CssConstants.None
            && lastInFlowChild.Display != CssConstants.Inline
            && lastInFlowChild.Display != CssConstants.InlineBlock;

        if (!collapseThrough && lastInFlowChild != null)
            maxChildBottom += lastInFlowChild.ActualMarginBottom;

        return Math.Max(ActualBottom, maxChildBottom + margin + ActualPaddingBottom + ActualBorderBottomWidth);
    }
}
