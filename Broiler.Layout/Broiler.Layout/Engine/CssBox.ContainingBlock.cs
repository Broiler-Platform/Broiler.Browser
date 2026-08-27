using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    public CssBox ContainingBlock
    {
        get
        {
            if (ParentBox == null)
                return this; //This is the initial containing block.

            var box = ParentBox;

            // CSS2.1 §10.1: The containing block for a box is the nearest
            // ancestor that is a block container.  Block containers include:
            //   - block-level boxes (display:block, flex, grid)
            //   - atomic inline-level boxes (inline-block, inline-table,
            //     inline-flex, inline-grid)
            //   - list-item boxes
            //   - table cells (display:table-cell)
            //   - table boxes (display:table)
            //   - table captions (display:table-caption)
            // An atomic inline-level box establishes an independent formatting
            // context for its contents (§9.4.1 for inline-block; CSS Display 3
            // §2.5 for the inline-* flex/grid/table forms), so its in-flow
            // descendants resolve their sizes and positions against it and not
            // against a further-up ancestor.  Listing only `inline-block` here
            // left the other three transparent, and a percentage height inside
            // one then resolved against whatever block ancestor lay beyond it:
            // an `inline-grid` of a stated height holding a `height: 100%` item
            // saw `<body>`'s auto height, so the percentage computed to auto —
            // which an `aspect-ratio` on the item then turned into the *body's*
            // width transferred through the ratio, escaping the grid entirely
            // (WPT css-grid/alignment/grid-item-aspect-ratio-justify-self-001,
            // where a 16×32 item rendered 24×2016).  A table caption is a
            // block container that establishes an independent BFC (CSS2.1
            // §17.4), so its in-flow children resolve their width and position
            // against the caption's content box — critical when the caption is
            // sized/rotated by its own writing mode (its children must inherit
            // its inline size, not the viewport's).
            while (!box.IsBlock
                   && !CssBoxHelper.IsAtomicInlineLevel(box.Display)
                   && box.Display != CssConstants.ListItem
                   && box.Display != CssConstants.Table
                   && box.Display != CssConstants.TableCell
                   && box.Display != CssConstants.TableCaption
                   && box.ParentBox != null)
            {
                box = box.ParentBox;
            }

            //Comment this following line to treat always superior box as block
            if (box == null)
                throw new Exception("There's no containing block on the chain");

            return box;
        }
    }

    /// <summary>
    /// CSS2.1 §10.1: For absolutely positioned elements, the containing
    /// block is the padding-box of the nearest ancestor with a computed
    /// position of <c>absolute</c>, <c>relative</c>, or <c>fixed</c>.
    /// Falls back to <see cref="ContainingBlock"/> if none is found.
    /// Also checks <see cref="SplitPositionedAncestor"/> which links back
    /// to positioned inlines that were restructured by the block-inside-
    /// inline correction (CSS2.1 §9.2.1.1).
    /// </summary>
    private CssBox FindPositionedContainingBlock()
    {
        // CSS Position 4 §top-layer: a top-layer box (open modal <dialog>, open popover, or a
        // ::backdrop) is laid out as though it were a child of the viewport, so its containing
        // block is the initial containing block no matter what its DOM ancestors do — an
        // ancestor's position/transform/containment never captures it. Without this an
        // absolutely-positioned ::backdrop inside a zero-sized `overflow: clip` parent resolved
        // `inset: 0` against that parent and landed at its origin instead of covering the
        // viewport (WPT the-dialog-element/top-layer-parent-overflow-clip).
        if (IsTopLayerBox)
            return TopLayerContainingBlock();

        var box = ParentBox;
        while (box != null)
        {
            if (box.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed || box.ParentBox == null)
                return box;

            // Native anchor mode (P5.8d.2b transform/contain CB expansion): a box that
            // establishes a containing block for absolutely-positioned descendants through a
            // non-position property — a non-none transform (CSS Transforms 1 §4) or
            // contain: layout/paint/strict/content (CSS Containment §2) — is that containing
            // block. On the baked path the bridge's EnsureContainingBlockPositioning pre-bakes
            // position:relative onto the same boxes, so the position check above already
            // returns them; recognising them here lets native mode drop that pre-bake. Gated
            // by the native-anchor lever so default-off layout is byte-identical.
            if (NativeAnchorPlacement.Enabled && box.EstablishesNonPositionAbsPosContainingBlock())
                return box;

            // RF-BRIDGE-1b Track 3.2: a nested browsing context's sub-viewport
            // (#subdoc-root) is the initial containing block for its subtree, so an
            // absolutely-positioned descendant with no positioned ancestor resolves
            // against it rather than climbing out into the top-level document.
            if (box.IsNestedViewportRoot)
                return box;

            // If the block-inside-inline correction split a positioned inline
            // and hoisted this branch out, SplitPositionedAncestor links back
            // to the original positioned inline ancestor.
            if (box.SplitPositionedAncestor is { } spa
                && spa.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed)
                return spa;

            box = box.ParentBox;
        }

        return ContainingBlock;
    }

    private bool IsInitialContainingBlock(CssBox cb) => cb.ParentBox == null && LayoutEnvironment != null;

    // The bridge marks a top-layer element with its top-layer order; a renderer-generated
    // top-layer box (a native ::backdrop, which has no element to carry the attribute) carries
    // the order in the box field instead. Mirrors FragmentTreeBuilder.GetTopLayerOrder, which
    // reads the same two sources when projecting the order onto the fragment.
    private const string TopLayerOrderAttr = "data-broiler-top-layer";

    internal bool IsTopLayerBox =>
        TopLayerOrder is not null || !string.IsNullOrEmpty(GetAttribute(TopLayerOrderAttr));

    /// <summary>
    /// The initial containing block a top-layer box resolves against: the nearest nested
    /// browsing context's sub-viewport (<see cref="IsNestedViewportRoot"/>) when the box lives
    /// inside an <c>&lt;iframe&gt;</c> — a frame has its own top layer — otherwise the root box,
    /// which <see cref="GetAbsoluteContainingBlockPaddingBox"/> reads as the viewport rectangle.
    /// </summary>
    private CssBox TopLayerContainingBlock()
    {
        var root = this;
        for (var box = ParentBox; box != null; box = box.ParentBox)
        {
            root = box;
            if (box.IsNestedViewportRoot)
                return box;
        }

        return root;
    }

    /// <summary>
    /// Whether this box establishes a containing block for absolutely-positioned
    /// descendants through a property other than <c>position</c> — a non-<c>none</c>
    /// <c>transform</c> (CSS Transforms 1 §4), a <c>contain</c> value of
    /// <c>layout</c>/<c>paint</c>/<c>strict</c>/<c>content</c> (CSS Containment §2), or
    /// <c>will-change: transform</c> (CSS Will Change 1 §3 — a will-change hint for a
    /// property that would itself create one). Mirrors the bridge's
    /// <c>EstablishesContainingBlock</c> exactly (minus the <c>position</c> branch handled
    /// by the caller), so the two paths agree. Consulted by
    /// <see cref="FindPositionedContainingBlock"/> only under the native-anchor lever.
    /// </summary>
    internal bool EstablishesNonPositionAbsPosContainingBlock() =>
        CssContainingBlock.CreatedByTransformContainOrWillChange(Transform, Contain, WillChange);

    /// <summary>
    /// A non-atomic inline box — <c>display: inline</c>, not one of the atomic inline-level
    /// displays (<c>inline-block</c>, <c>inline-flex</c>, …). Neither layout/paint containment
    /// (CSS Containment §2) nor a <c>transform</c> (CSS Transforms 1 §3, "transformable element")
    /// applies to one, so it never becomes a containing block for out-of-flow descendants through
    /// those properties. WPT css-contain/contain-paint-012 is exactly this case: <c>contain:
    /// paint</c> sits on a <c>&lt;span&gt;</c> wrapping a fixed-position child, and the child has
    /// to resolve against the transformed <em>block</em> above the span instead.
    /// <para>Deliberately checked at the call site rather than folded into
    /// <see cref="EstablishesNonPositionAbsPosContainingBlock"/>: that predicate is the engine
    /// mirror of the bridge's property-only <c>EstablishesContainingBlock</c>, and
    /// <c>NativeAnchorPlacementTests.EstablishesNonPositionAbsPosContainingBlock_MirrorsBridgePredicate</c>
    /// pins the two to the same answer for a given transform/contain/will-change triple.</para>
    /// </summary>
    private bool IsNonAtomicInline =>
        string.Equals(Display, CssConstants.Inline, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// RF-BRIDGE-1b Track 3.2: the viewport a <c>position:fixed</c> descendant of this
    /// box resolves against — its used size (for inset percentages and viewport-basis
    /// sizing) and its origin (for placement). When the box is inside a nested browsing
    /// context (an <c>&lt;iframe&gt;</c> whose <c>#subdoc-root</c> is marked
    /// <see cref="IsNestedViewportRoot"/>), this is that frame's content box (the
    /// sub-viewport); otherwise it is the top-level viewport at the origin
    /// (<c>0,0</c> + <see cref="ILayoutEnvironment.ViewportSize"/>) — byte-identical to
    /// the previous behaviour for every box in the top-level document. The sub-viewport
    /// origin is the root's <em>pre-translate</em> content origin; the later
    /// <see cref="LayoutSubdocument"/> translate composes the whole subtree (fixed box
    /// included) onto the frame's content origin, so the fixed box lands at
    /// <c>frameContentOrigin + inset</c>.
    /// </summary>
    private RectangleF FixedPositioningViewport()
    {
        for (var box = ParentBox; box != null; box = box.ParentBox)
        {
            // CSS Transforms 1 §4 / CSS Containment §2: a transform, a layout/paint containment,
            // or will-change:transform makes an ancestor the containing block for *all* its
            // descendants — fixed-position ones included, not just absolutely-positioned ones.
            // Only when no such ancestor exists does the viewport take over. Without this a fixed
            // box always sized and placed against the viewport, so WPT css-contain/contain-paint-012
            // (a `width/height: 100%` fixed child under a `transform: translateX(0)` 100x100 block)
            // painted green over the whole page instead of a 100px square.
            // Reuse the abspos padding-box resolution rather than recomputing it: heights resolve
            // bottom-up, so a containing block's ActualBottom is often still unsettled while its
            // out-of-flow children are being placed, and that method already recovers the height
            // from a definite specified one (and handles grid areas and the vertical-flow frame).
            if (!box.IsNonAtomicInline && box.EstablishesNonPositionAbsPosContainingBlock())
            {
                GetAbsoluteContainingBlockPaddingBox(box, out var cbLeft, out var cbTop, out var cbWidth, out var cbHeight);
                if (cbWidth > 0 && cbHeight > 0)
                    return new RectangleF((float)cbLeft, (float)cbTop, (float)cbWidth, (float)cbHeight);
            }

            if (!box.IsNestedViewportRoot)
                continue;

            // Origin tracks the live Location (composed onto the frame content origin by
            // the LayoutSubdocument translate); size comes from the pinned
            // NestedViewportSize because the box's used Size is transiently 0 while its
            // own subtree lays out.
            double left = box.Location.X + box.ActualBorderLeftWidth + box.ActualPaddingLeft;
            double top = box.Location.Y + box.ActualBorderTopWidth + box.ActualPaddingTop;
            return new RectangleF((float)left, (float)top,
                box.NestedViewportSize.Width, box.NestedViewportSize.Height);
        }

        var vp = LayoutEnvironment?.ViewportSize ?? SizeF.Empty;
        return new RectangleF(0, 0, vp.Width, vp.Height);
    }

    private void GetAbsoluteContainingBlockPaddingBox(CssBox cb,
        out double cbPadLeft,
        out double cbPadTop,
        out double cbPadWidth,
        out double cbPadHeight)
    {
        // CSS Grid §9: an absolutely-positioned grid item's containing block is
        // the grid area the grid container's track-sizing pass resolved for it,
        // not the container's padding box. All abspos size/offset resolution
        // routes through here, so returning the area makes width/height/inset
        // percentages and the static position use it uniformly.
        if (GridAreaContainingBlock is { } gridArea)
        {
            cbPadLeft = gridArea.Left;
            cbPadTop = gridArea.Top;
            cbPadWidth = gridArea.Width;
            cbPadHeight = gridArea.Height;
            return;
        }

        if (IsInlineContainingBlock(cb))
        {
            var bbox = GetInlineBoundingBox(cb);
            if (bbox != RectangleF.Empty)
            {
                cbPadLeft = bbox.Left;
                cbPadTop = bbox.Top;
                cbPadWidth = bbox.Width;
                cbPadHeight = bbox.Height;
                return;
            }
        }

        if (IsInitialContainingBlock(cb))
        {
            cbPadLeft = 0;
            cbPadTop = 0;
            cbPadWidth = LayoutEnvironment.ViewportSize.Width;
            cbPadHeight = LayoutEnvironment.ViewportSize.Height;
            return;
        }

        // RF-BRIDGE-1b Track 3.2: a nested browsing context's sub-viewport acts as the
        // initial containing block for its subtree — an abspos descendant with no
        // positioned ancestor resolves against the frame content box. Use the box's own
        // (pre-translate) content box: its used Size is the sub-viewport, and its
        // Location is composed onto the frame's content origin by the later
        // LayoutSubdocument translate, exactly as the abspos static position is.
        if (cb.IsNestedViewportRoot)
        {
            cbPadLeft = cb.Location.X + cb.ActualBorderLeftWidth + cb.ActualPaddingLeft;
            cbPadTop = cb.Location.Y + cb.ActualBorderTopWidth + cb.ActualPaddingTop;
            cbPadWidth = cb.NestedViewportSize.Width;
            cbPadHeight = cb.NestedViewportSize.Height;
            return;
        }

        cbPadLeft = cb.Location.X + cb.ActualBorderLeftWidth;
        cbPadTop = cb.Location.Y + cb.ActualBorderTopWidth;

        // The containing block is a vertical writing-mode box the BROILER_VERTICAL_FLOW prototype is
        // currently laying out in a *logical* horizontal frame (InVerticalFrameLayout), transposing to
        // physical only in a later post-pass (see CssBox.WritingMode ApplyVerticalWritingModeFlow). The
        // guard is the frame-layout flag, not just WillBeVerticalTransposed(): a vertical box whose
        // physical Size is already set (e.g. the native anchor-placement pass) must NOT be un-swapped.
        if (InVerticalFrameLayout && cb.WillBeVerticalTransposed())
        {
            // In the logical frame cb.Size.Width is the logical inline extent (physically its height),
            // and — because the prototype reuses the horizontal layout code by swapping the box's
            // width/height — its `Width`/`Height` CSS strings are swapped too (physical width is
            // `Height`). Absolutely positioned insets/sizes resolve in *physical* coordinates and the
            // abspos box is left untransposed, so use the physical extents — otherwise left/right and
            // top/bottom resolve against the swapped axis (WPT css-writing-modes
            // abs-pos-non-replaced-v{lr,rl}-*). The origin above is already physical (a rotation root
            // keeps its own physical Location).

            // Physical height = the logical inline extent (Size.Width), which resolves top-down.
            cbPadHeight = cb.Size.Width - cb.ActualBorderTopWidth - cb.ActualBorderBottomWidth;

            // Physical width = the logical block extent (ActualBottom − Location.Y). Abspos children
            // are placed before the CB's block size resolves, so that is usually still 0 here; fall
            // back to the CB's definite specified physical width — which the prototype's swap stores
            // in `Height`.
            cbPadWidth = (cb.ActualBottom - cb.Location.Y) - cb.ActualBorderLeftWidth - cb.ActualBorderRightWidth;
            if (cbPadWidth <= 0
                && cb.Height != CssConstants.Auto && !string.IsNullOrEmpty(cb.Height) && !cb.Height.Contains('%'))
            {
                double cssPhysicalWidth = CssLengthParser.ParseLength(cb.Height, 0, cb.GetEmHeight());
                double candidate = cb.ResolveSpecifiedWidthToBorderBox(cssPhysicalWidth)
                    - cb.ActualBorderLeftWidth - cb.ActualBorderRightWidth;
                if (candidate > cbPadWidth)
                    cbPadWidth = candidate;
            }
            return;
        }

        cbPadWidth = cb.Size.Width - cb.ActualBorderLeftWidth - cb.ActualBorderRightWidth;
        cbPadHeight = (cb.ActualBottom - cb.Location.Y) - cb.ActualBorderTopWidth - cb.ActualBorderBottomWidth;

        // Block-axis self-alignment of an absolutely positioned descendant can
        // run before the containing block has resolved its own block size:
        // heights resolve bottom-up, yet abspos children are positioned during
        // the CB's layout, so cb.ActualBottom may still equal cb.Location.Y and
        // cbPadHeight collapses to ~0 — leaving align-self with no IMCB to work
        // within (the box stays at its static position).  Widths resolve
        // top-down, so cbPadWidth is already correct; this only patches the
        // height.  When the CB carries a definite (non-percentage) specified
        // height, derive the padding-box height from it directly.
        if (cbPadHeight <= 0
            && cb.Height != CssConstants.Auto && !string.IsNullOrEmpty(cb.Height)
            && !cb.Height.Contains('%'))
        {
            double cssHeight = CssLengthParser.ParseLength(cb.Height, 0, cb.GetEmHeight());
            double borderBoxHeight = cb.ResolveSpecifiedHeightToBorderBox(cssHeight);
            double candidate = borderBoxHeight - cb.ActualBorderTopWidth - cb.ActualBorderBottomWidth;

            if (candidate > cbPadHeight)
                cbPadHeight = candidate;
        }

        // Same collapse, but the CB is itself an absolutely-positioned box with an *auto* height
        // sized by its own insets (both top and bottom set) — so the definite-height branch above
        // does not apply. Its used block size is CB-of-CB height − its top/bottom insets, and that is
        // determinate even though ActualBottom has not settled: derive the padding-box height from it.
        // Recurses up the abspos chain until a definite- or viewport-sized ancestor is reached (each
        // step drops to the definite/ICB branch), fixing nested `position:absolute; inset:0` boxes,
        // which otherwise collapse to zero height (WPT css-view-transitions nested tests).
        if (cbPadHeight <= 0
            && (cb.Position == CssConstants.Absolute || cb.Position == CssConstants.Fixed)
            && (string.IsNullOrEmpty(cb.Height) || cb.Height == CssConstants.Auto)
            && cb.Top is not (null or CssConstants.Auto)
            && cb.Bottom is not (null or CssConstants.Auto))
        {
            var cbCb = cb.FindPositionedContainingBlock();
            if (!ReferenceEquals(cbCb, cb))
            {
                cb.GetAbsoluteContainingBlockPaddingBox(cbCb, out _, out _, out _, out double cbCbPadHeight);
                if (cbCbPadHeight > 0)
                {
                    double cbTop = cb.ParseUsedLength(cb.Top, cbCbPadHeight);
                    double cbBottom = cb.ParseUsedLength(cb.Bottom, cbCbPadHeight);
                    double candidate = cbCbPadHeight - cbTop - cbBottom
                        - cb.ActualMarginTop - cb.ActualMarginBottom
                        - cb.ActualBorderTopWidth - cb.ActualBorderBottomWidth;

                    if (candidate > cbPadHeight)
                        cbPadHeight = candidate;
                }
            }
        }
    }

    /// <summary>
    /// CSS2.1 §10.1: When the containing block for an absolutely positioned
    /// element is formed by an inline-level element, the containing block is
    /// the bounding box around the padding boxes of the first and last inline
    /// boxes generated for that element.  Returns the bounding rectangle in
    /// absolute coordinates, or <see cref="RectangleF.Empty"/> if the inline
    /// has no line-box rectangles and no laid-out children.
    /// </summary>
    private static RectangleF GetInlineBoundingBox(CssBox cb)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        // Accumulate extents from one box (the original or a fragment).
        void AccumulateBox(CssBox box)
        {
            // Try the inline's own Rectangles (populated when the
            // inline element has direct text words).
            foreach (var rect in box.Rectangles.Values)
            {
                if (rect.Left < minX) minX = rect.Left;
                if (rect.Top < minY) minY = rect.Top;
                if (rect.Right > maxX) maxX = rect.Right;
                if (rect.Bottom > maxY) maxY = rect.Bottom;
            }

            // Also scan child boxes (inline-blocks etc.) for their
            // laid-out positions and sizes.
            foreach (var child in box.Boxes)
            {
                // CSS2.1 §10.1: the inline containing block's extent is the box
                // around the inline's own (in-flow) line boxes. An out-of-flow
                // (absolutely/fixed positioned) descendant is not part of that
                // extent — and while it is being positioned its transient static
                // Location would otherwise pollute the bounds it is measured
                // against (e.g. drag the CB top to 0), corrupting its own inset.
                if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                    continue;

                if (child.Size.Width <= 0 && child.Size.Height <= 0)
                    continue;

                float left = child.Location.X;
                float top = child.Location.Y;
                float right = left + child.Size.Width;
                float bottom = (float)child.ActualBottom;

                if (bottom <= top) bottom = top + child.Size.Height;

                if (left < minX) minX = left;
                if (top < minY) minY = top;
                if (right > maxX) maxX = right;
                if (bottom > maxY) maxY = bottom;
            }
        }

        // Scan the original box.
        AccumulateBox(cb);

        // If the positioned inline was split by the block-inside-inline
        // correction, also scan inline fragment copies that received its
        // children so the bounding box covers the full inline extent.
        // Only include fragments that are still inline — block-level
        // anonymous wrappers created during the split are structural
        // containers, not inline fragments.
        if (cb.SplitFragments != null)
        {
            foreach (var frag in cb.SplitFragments)
            {
                if (frag.Display == CssConstants.Inline)
                    AccumulateBox(frag);
            }
        }

        if (minX > maxX || minY > maxY)
            return RectangleF.Empty;

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Returns <c>true</c> when the given box is a pure inline element
    /// (not inline-block/inline-table etc.) whose containing-block extent
    /// must be computed from its line-box rectangles per CSS2.1 §10.1.
    /// </summary>
    private static bool IsInlineContainingBlock(CssBox cb) => cb.Display == CssConstants.Inline;

    /// <summary>
    /// Returns true when <see cref="Height"/> is a percentage that resolves
    /// to auto because the containing block's height is not explicitly
    /// specified (CSS 2.1 §10.5).  Callers must still verify that Height is
    /// not auto/empty before using this — the check only tests whether a
    /// non-auto percentage value should be treated as auto.
    /// </summary>
    internal bool HeightPercentageResolvesToAuto()
    {
        if (!Height.Contains('%'))
            return false;

        // CSS 2.1 §10.5: "A percentage height on the root element is
        // relative to the initial containing block."  The initial
        // containing block always has a definite height (the viewport),
        // so percentage heights on the root element never resolve to auto.
        if (ContainingBlock?.ParentBox == null)
            return false;

        // CSS 2.1 §10.5: the "resolves to auto when the containing block's
        // height is indefinite" rule applies only when "this element is not
        // absolutely positioned".  An absolutely (or fixed) positioned box's
        // containing block is the padding box of its positioned ancestor (or
        // the viewport for the initial containing block), whose height is
        // always definite — so a percentage height never resolves to auto.
        if (Position == CssConstants.Absolute || Position == CssConstants.Fixed)
            return false;

        // CSS Sizing 4 §4: a containing block whose height is auto but that has a
        // preferred aspect-ratio and a definite used width has a definite used
        // block size (its transferred aspect-ratio height), so a percentage height
        // resolves against it rather than to auto — matching the reference browser,
        // which sizes a filling child to the aspect-ratio square.
        if (ContainingBlock.HasDefiniteAspectRatioBlockHeight())
            return false;

        // CSS2.1 §10.6.4: a containing block that is out of flow with an `auto` height but both
        // `top` and `bottom` specified takes its used block size from the constraint equation, so
        // that size is definite even though nothing declared it. The `Height == auto` test below
        // cannot see that and would send every percentage inside such a box to `auto`.
        if (ContainingBlock.TryGetInsetDerivedContentHeight(out _))
            return false;

        return ContainingBlock.Height == CssConstants.Auto || string.IsNullOrEmpty(ContainingBlock.Height);
    }

    /// <summary>
    /// CSS2.1 §10.6.4: the used <em>content</em>-box height of an out-of-flow box whose
    /// <c>height</c> is <c>auto</c> but whose <c>top</c> and <c>bottom</c> are both specified —
    /// the constraint equation
    /// <c>top + margin-top + border + padding + height + padding + border + margin-bottom + bottom</c>
    /// <c> = containing-block height</c> leaves exactly one unknown, so the block size is
    /// <b>definite</b> even though no <c>height</c> declaration names it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The definiteness is what callers are after, not just the number. Percentage heights inside
    /// such a box resolve against it rather than falling back to <c>auto</c> (§10.5), and a flex or
    /// grid container sized this way has a definite main/block size to hand out to its items
    /// (CSS Flexbox §9.2). Both were reading the <c>height</c> declaration alone and so treated
    /// <c>position: absolute; inset: 0</c> — the shape a full-viewport app shell is written in — as
    /// content-sized: WPT <c>css-flexbox/percentage-heights-002</c> is a
    /// <c>position: absolute; top/right/bottom/left: 0</c> column flex container over a red
    /// backdrop, and every percentage in it collapsed to the text's own height, leaving the page
    /// red where the test says "you should see no red".
    /// </para>
    /// <para>
    /// Read at measurement time, before block sizes resolve bottom-up, so the containing block's
    /// extent comes from <see cref="GetAbsoluteContainingBlockPaddingBox"/> — which already walks
    /// up a chain of inset-sized ancestors — rather than from a <see cref="CssBoxProperties.Size"/>
    /// that has not settled. A box whose own used height is over-constrained to nothing returns
    /// <see langword="false"/>: a zero block size is indistinguishable here from one that has not
    /// been computed, and every caller treats it as indefinite anyway.
    /// </para>
    /// <para>
    /// Auto margins are not the §10.6.4 centring case: that applies only when none of the three of
    /// <c>top</c>, <c>height</c> and <c>bottom</c> is <c>auto</c>. With <c>height: auto</c> the
    /// spec sets an auto margin to zero and solves for the height, which is what
    /// <see cref="CssBoxProperties.ActualMarginTop"/> already holds.
    /// </para>
    /// </remarks>
    internal bool TryGetInsetDerivedContentHeight(out double contentHeight)
    {
        contentHeight = 0;

        if (Position is not (CssConstants.Absolute or CssConstants.Fixed))
            return false;

        if (!string.IsNullOrEmpty(Height) && Height != CssConstants.Auto)
            return false;

        if (Top is null or CssConstants.Auto || Bottom is null or CssConstants.Auto)
            return false;

        double cbHeight;

        if (Position == CssConstants.Fixed && LayoutEnvironment != null)
        {
            cbHeight = FixedPositioningViewport().Height;
        }
        else
        {
            var cb = FindPositionedContainingBlock();

            // A box that is its own positioned containing block has no outer extent to solve
            // against; the recursion in GetAbsoluteContainingBlockPaddingBox makes the same guard.
            if (ReferenceEquals(cb, this))
                return false;

            GetAbsoluteContainingBlockPaddingBox(cb, out _, out _, out _, out cbHeight);
        }

        if (cbHeight <= 0)
            return false;

        double resolved = cbHeight
            - ParseUsedLength(Top, cbHeight) - ParseUsedLength(Bottom, cbHeight)
            - ActualMarginTop - ActualMarginBottom
            - ActualPaddingTop - ActualPaddingBottom
            - ActualBorderTopWidth - ActualBorderBottomWidth;

        if (double.IsNaN(resolved) || double.IsInfinity(resolved))
            return false;

        // §10.7: the used height is then clamped, and the clamped value is the definite one.
        resolved = ClampInsetDerivedContentHeight(resolved, cbHeight);

        contentHeight = resolved;
        return contentHeight > 0;
    }

    /// <summary>
    /// CSS2.1 §10.7 over the §10.6.4 result: <c>min-height</c> and <c>max-height</c> clamp the
    /// height the inset pair solved for, in the content-box frame that solution is stated in.
    /// </summary>
    private double ClampInsetDerivedContentHeight(double contentHeight, double cbHeight)
    {
        double em = GetEmHeight();

        double? ToContentHeight(string declaration)
        {
            if (string.IsNullOrEmpty(declaration) || declaration == CssConstants.Auto
                || declaration.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            double length = CssLengthParser.ParseLength(declaration, cbHeight, em);
            return double.IsNaN(length) || double.IsInfinity(length)
                ? null
                : ResolveSpecifiedHeightToContentBox(length);
        }

        if (ToContentHeight(MaxHeight) is { } max && max < contentHeight)
            contentHeight = max;

        if (ToContentHeight(MinHeight) is { } min && min > contentHeight)
            contentHeight = min;

        return Math.Max(0, contentHeight);
    }

    /// <summary>CSS Sizing 4 §4: <c>true</c> when this box's block (height) axis is
    /// <c>auto</c> but resolvable from its used width and preferred
    /// <c>aspect-ratio</c>, so its used height is definite for percentage-height
    /// descendants. Scoped by <see cref="CanTransferAspectRatioToBlockHeight"/>,
    /// matching <see cref="TryResolveAspectRatioBlockHeight"/>'s applicability.</summary>
    internal bool HasDefiniteAspectRatioBlockHeight() =>
        TryGetAspectRatioBlockHeight(out _);

    /// <summary>CSS Sizing 4 §4: the used border-box height this box's auto block
    /// axis takes from its used width and preferred aspect ratio, for the layout
    /// paths that live outside <see cref="CssBox"/> — an atomic inline-level box
    /// computes its own height in
    /// <see cref="CssLayoutEngine"/>'s inline flow rather than in
    /// <see cref="ResolveUsedBlockHeight"/>.</summary>
    internal bool TryGetAspectRatioBlockHeight(out double borderBoxHeight)
    {
        borderBoxHeight = 0;
        return CanTransferAspectRatioToBlockHeight
            && TryResolveAspectRatioBlockHeight(out borderBoxHeight);
    }

    /// <summary>
    /// CSS2.1 §10.5: the containing-block height a percentage <c>height</c>
    /// (or percentage <c>min-/max-height</c>) resolves against.  For
    /// fixed-position boxes this is the viewport; for other absolutely
    /// positioned boxes it is the height of the <em>positioned</em> containing
    /// block's padding box (the viewport when that is the initial containing
    /// block) — an abspos box's containing block always has a definite height,
    /// unlike the flow containing block, whose height may be auto/indefinite.
    /// Otherwise the flow containing block's used <em>content</em> height is
    /// returned: §10.5 resolves a percentage against the containing block's
    /// content box, so the containing block's own padding and border are not part
    /// of the basis in either branch below.
    /// </summary>
    private double PercentageHeightContainingBlockHeight()
    {
        if (Position == CssConstants.Fixed && LayoutEnvironment != null)
            return FixedPositioningViewport().Height;

        if (Position == CssConstants.Absolute)
        {
            var cb = FindPositionedContainingBlock();
            GetAbsoluteContainingBlockPaddingBox(cb, out _, out _, out _, out double cbHeight);
            return cbHeight;
        }

        if (ContainingBlock?.ParentBox == null && LayoutEnvironment != null)
            return LayoutEnvironment.ViewportSize.Height;

        // A flow containing block with a definite (non-auto, non-percentage)
        // specified height exposes that height to its percentage-height
        // children even before its own block size is applied. Block heights
        // resolve bottom-up — children lay out (and resolve their percentages)
        // before the containing block sets its used height — and a fixed-height
        // box, unlike a percentage-height one, is not pre-resolved into
        // Size.Height (see the §10.5 pre-resolution in the layout pass). Reading
        // Size.Height here would then yield 0 and collapse every percentage-height
        // child. Derive the basis straight from the specification instead, the
        // same way the abspos IMCB fallback does for a definite containing block.
        var flowCb = ContainingBlock;
        if (flowCb != null && flowCb.Height != CssConstants.Auto && !string.IsNullOrEmpty(flowCb.Height)
            && !flowCb.Height.Contains('%'))
        {
            double cssHeight = CssLengthParser.ParseLength(flowCb.Height, 0, flowCb.GetEmHeight());

            // §10.5 resolves the percentage against the containing block's *content* box.
            // Normalising the specified height to a border box instead added the containing
            // block's own padding and border to every percentage inside it: a `height: 100%`
            // child of a `height: 32px` box with a 2px border came out 36px, and 42px with
            // 5px of padding. It went unnoticed because the two coincide whenever the
            // containing block has neither, and because `box-sizing: border-box` — where the
            // specified height already *is* the border box — takes the no-op path through
            // both conversions.
            if (cssHeight > 0)
                return flowCb.ResolveSpecifiedHeightToContentBox(cssHeight);
        }

        // §10.6.4 again: the containing block is out of flow with an auto height that its own
        // top/bottom pair makes definite. Its Size.Height is still 0 at this point for the same
        // bottom-up reason the definite-declaration branch above exists.
        if (flowCb != null && flowCb.TryGetInsetDerivedContentHeight(out double insetHeight))
            return insetHeight;

        // Size.Height is a border box, so the padding and border come off it to leave the
        // content height, exactly as TryGetPercentageBlockSizeBasis does for the same box.
        if (flowCb == null)
            return 0;

        return Math.Max(0, flowCb.Size.Height
            - flowCb.ActualPaddingTop - flowCb.ActualPaddingBottom
            - flowCb.ActualBorderTopWidth - flowCb.ActualBorderBottomWidth);
    }

    /// <summary>
    /// CSS2.1 §10.7: the block size a percentage <c>min-height</c>/<c>max-height</c> on this box
    /// resolves against, or <see langword="false"/> when the containing block's block size is
    /// indefinite — the percentage then takes its initial value (<c>0</c> for <c>min-height</c>,
    /// <c>none</c> for <c>max-height</c>).
    /// </summary>
    /// <remarks>
    /// <para>Unlike <see cref="PercentageHeightContainingBlockHeight"/> this walks <b>past anonymous
    /// boxes</b>. An anonymous block is not an element: it has no author height, so its block size
    /// is always content-derived, and stopping at one would make every percentage inside it resolve
    /// to the initial value. Browsers skip them and resolve against the nearest real element, which
    /// is exactly what WPT <c>css-sizing/image-percentage-max-height-in-anonymous-block</c> and
    /// <c>css-sizing/block-image-percentage-max-height-inside-inline</c> are built to check: in both
    /// the <c>&lt;img&gt;</c> lands in an anonymous block, and its <c>max-height: 100%</c> must still
    /// see the <c>height: 100px</c> <c>&lt;div&gt;</c> two boxes up.</para>
    /// <para>Read at measurement time, before block heights are resolved bottom-up, so a definite
    /// basis is derived from the <em>specified</em> height rather than from
    /// <see cref="CssBoxProperties.Size"/> — which is still 0 for an ancestor that has not been laid
    /// out yet.</para>
    /// </remarks>
    internal bool TryGetPercentageBlockSizeBasis(out double basis)
    {
        basis = 0;

        // An out-of-flow box's containing block is a padding box with a definite height (the
        // viewport for the initial containing block), so a percentage never resolves to the
        // initial value there.
        if (Position is CssConstants.Absolute or CssConstants.Fixed)
        {
            basis = PercentageHeightContainingBlockHeight();
            return basis > 0;
        }

        for (var cb = ContainingBlock; cb != null; cb = cb.ContainingBlock)
        {
            // The initial containing block is the viewport, which is always definite (§10.5).
            if (cb.ParentBox == null)
            {
                if (LayoutEnvironment == null)
                    return false;

                basis = LayoutEnvironment.ViewportSize.Height;
                return basis > 0;
            }

            bool heightIsAuto = cb.Height == CssConstants.Auto || string.IsNullOrEmpty(cb.Height);

            if (!heightIsAuto && !cb.Height.Contains('%'))
            {
                double cssHeight = CssLengthParser.ParseLength(cb.Height, 0, cb.GetEmHeight());
                if (cssHeight > 0)
                {
                    basis = cb.ResolveSpecifiedHeightToContentBox(cssHeight);
                    return true;
                }

                return false;
            }

            // CSS Sizing 4 §4: an auto block axis that the box's aspect ratio makes definite is a
            // definite basis, the same exception HeightPercentageResolvesToAuto makes.
            if (heightIsAuto && cb.HasDefiniteAspectRatioBlockHeight())
            {
                cb.TryGetAspectRatioBlockHeight(out basis);
                return basis > 0;
            }

            // CSS2.1 §10.6.4: so is an auto block axis an out-of-flow box's own top/bottom pair
            // solves for — the same exception, and made in the same two places.
            if (heightIsAuto && cb.TryGetInsetDerivedContentHeight(out basis))
                return true;

            // A real element with an auto (or already-resolved percentage) height: its used block
            // size, when layout has settled it, and otherwise indefinite. Size.Height is a border
            // box, so the padding and border come off it to leave the content height percentages
            // resolve against.
            if (cb.HtmlTag != null && !cb.IsBlockifiedInlineSplit)
            {
                basis = Math.Max(0, cb.Size.Height
                    - cb.ActualPaddingTop - cb.ActualPaddingBottom
                    - cb.ActualBorderTopWidth - cb.ActualBorderBottomWidth);
                return basis > 0 && !heightIsAuto;
            }

            // Anonymous, or an inline blockified by a block-inside-inline split: keep climbing.
        }

        return false;
    }

}
