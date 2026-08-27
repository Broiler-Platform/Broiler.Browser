using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// CSS Display 3 §2.7 <em>blockification</em>: a flex or grid container's in-flow children are
/// flex/grid items, and an item's computed <c>display</c> is its <em>blockified</em> display —
/// <c>inline</c> becomes <c>block</c>, <c>inline-block</c> becomes <c>block</c>,
/// <c>inline-table</c> becomes <c>table</c>, and so on.
/// </summary>
/// <remarks>
/// <para>
/// Nothing performed that step, and the consequence was not a mis-sized item but a missing one.
/// <see cref="CssBox.PerformLayout"/> routes a box down the block path only when it satisfies the
/// block predicate; a child that is still <c>display: inline</c> takes the else-branch, which
/// copies a sibling's position and returns without ever resolving a size. The flex pass then
/// arranges a zero-sized box. Every inline-level child of a flex container — the overwhelmingly
/// common case, since <c>&lt;a&gt;</c> and <c>&lt;span&gt;</c> are what toolbars are built out of —
/// therefore laid out as nothing at all.
/// </para>
/// <para>
/// On <c>www.mediawiki.org</c> that is the whole Vector 2022 header: the hamburger button, the
/// wordmark link, the search, language and appearance buttons and the account links are
/// <c>&lt;a&gt;</c>/<c>&lt;span&gt;</c> children of flex containers, so the header rendered empty
/// except for the parts that happened to be block-level already.
/// </para>
/// <para>
/// The pass runs with the other box fix-ups, before the inline/block corrections, so a blockified
/// item takes part in them as the block-level box it has become — in particular it no longer
/// looks like inline content that the block-inside-inline split should wrap.
/// </para>
/// </remarks>
internal static class FlexGridItemBlockification
{
    /// <summary>
    /// Blockifies the flex/grid items under <paramref name="root"/>. Idempotent: a tree whose
    /// items are already block-level is left untouched.
    /// </summary>
    internal static void Generate(CssBox root)
    {
        if (root == null)
            return;

        // The `display: contents` splice has to have happened before anything below reads a
        // child's display, because a box spliced in from a `contents` wrapper is a flex/grid item
        // of this container and has to be blockified as one. It is driven from here rather than
        // from its own line in `DomParser.PrepareCssTree` because that method is in the
        // Broiler.HTML submodule, which this session cannot push to — and this call is the first
        // thing the box fix-up sequence there runs, so the pass lands at exactly the point in the
        // pipeline it belongs at. Idempotent, so adding the direct call later is a no-op here.
        DisplayContentsBoxes.Generate(root);

        // Likewise driven from here rather than from `DomParser`, and for the same reason: the
        // <foreignObject> boxes the style pass hid with the rest of the SVG subtree are lifted back
        // out and placed before layout reads them. Idempotent, so a direct call added later is a
        // no-op. It runs after the `contents` splice so a foreign object spliced in from a wrapper
        // is placed too.
        SvgForeignObjectBoxes.Generate(root);

        Blockify(root);
    }

    private static void Blockify(CssBox box)
    {
        bool blockifiesChildren = IsFlexOrGridContainer(box.Display);

        if (blockifiesChildren)
            WrapTextRunsInAnonymousItems(box);

        for (int i = 0; i < box.Boxes.Count; i++)
        {
            CssBox child = box.Boxes[i];

            if (blockifiesChildren && IsInFlowItem(child))
            {
                // CSS Flexbox §3 / CSS Grid §6: `float` and `clear` have no effect on an item.
                // Neutralising them here, before layout, is what makes the child an item at all:
                // a box left with `float: left` is taken out of flow by the block pass, so the
                // container sizes as though the item were not there.
                child.Float = CssConstants.None;
                child.Clear = CssConstants.None;
                child.Display = BlockifiedDisplay(child.Display);
            }

            Blockify(child);
        }
    }

    private static bool IsFlexOrGridContainer(string display) =>
        display is "flex" or "inline-flex" or "grid" or "inline-grid";

    /// <summary>
    /// Whether <paramref name="box"/> is an in-flow item of a <em>row</em> flex container — a child
    /// this pass blockified, rather than a box that merely became block-level on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blockification runs before the box fix-ups, so an <c>&lt;img&gt;</c> item reaches them as
    /// <c>display: block</c> — and the fix-up for a block-level replaced element wraps it in an
    /// anonymous block, because that is how such a box is laid out here. For an item that wrapper is
    /// fatal rather than helpful: it becomes the flex item, so every size the flex algorithm
    /// resolves is applied to <em>it</em> and the image inside keeps whatever width it was declared
    /// with. A <c>&lt;img style="width:999px"&gt;</c> in a zero-width flex row could not shrink at
    /// all — not even with <c>min-width: 0</c>, which is the one spelling that always lets an item
    /// collapse — and simply overflowed its container, which is the shape of nearly every image in
    /// a flex row on the real web.
    /// </para>
    /// <para>
    /// A <em>column</em> container is deliberately not included. Its items are placed by ordinary
    /// block flow rather than by the arranging loop a row container runs, and block flow is what
    /// cannot position a block-level replaced box — which is why the wrapper exists at all. Without
    /// it a column container's image landed at the document origin, on top of whatever preceded the
    /// container. A row container arranges its items itself, so it needs no such help.
    /// </para>
    /// <para>
    /// This is the predicate that fix-up asks before wrapping. It lives here, with the pass that
    /// created the block display in the first place, so the two cannot disagree about what an item
    /// is.
    /// </para>
    /// </remarks>
    internal static bool IsRowFlexItem(CssBox box) =>
        box?.ParentBox is { } parent
        && parent.IsRowFlexContainer()
        && IsInFlowItem(box);

    /// <summary>
    /// Whether a replaced box that is about to be wrapped in an anonymous block — because it is a
    /// <em>column</em> flex item, which <see cref="IsRowFlexItem"/> deliberately does not exempt —
    /// must be made to fill that wrapper, because the item it becomes will be stretched across the
    /// flex line (CSS Flexbox §9.4 step 11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wrapper is an implementation artifact: to the spec the replaced element <em>is</em> the
    /// flex item, and stretching it is what gives it its cross size. With the wrapper in between,
    /// the stretch lands on the wrapper and the image inside keeps an <c>auto</c> width — which for
    /// an inline replaced box with no intrinsic size means the 300×150 default object size. So
    /// <c>&lt;div style="display:flex;flex-direction:column"&gt;&lt;img&gt;</c> holding an SVG sized
    /// <c>100%</c> with a 2:1 <c>viewBox</c> painted a 300×150 rectangle where every browser paints
    /// one the full width of the container, its height derived through the ratio
    /// (<c>css-flexbox/aspect-ratio-intrinsic-size-007</c>).
    /// </para>
    /// <para>
    /// The conditions are §9.4 step 11's own, asked before layout: the item resolves to
    /// <c>stretch</c>, its cross size is <c>auto</c>, and neither cross-axis margin is <c>auto</c>.
    /// A container that aligns its items any other way leaves the wrapper to shrink-wrap, and
    /// filling it would then be circular. It is the same trade the auto-side-margin case in the
    /// wrapping fix-up already makes, and it lives here beside <see cref="IsRowFlexItem"/> so the
    /// two readings of "what is the item" cannot drift apart.
    /// </para>
    /// <para>
    /// <b>Two further conditions are what make <c>width: 100%</c> an exact stand-in for the
    /// stretch rather than an approximation of it, and both were found by measuring.</b> A
    /// percentage width sizes the <em>content</em> box while a stretch sizes the <em>border</em>
    /// box, so the two agree only when the replaced element has no inline-axis padding or border —
    /// with <c>img { padding: 20px }</c> in a 240px container the shortcut overflows by exactly the
    /// padding (<c>flex-aspect-ratio-intrinsic-padding-001</c>, whose assertion names the content
    /// box outright). And the container's cross size has to come from outside rather than from its
    /// own contents: an <c>inline-flex</c> column container shrink-wraps to its items, so an item
    /// declared <c>100%</c> of it contributes nothing to the size it is a percentage of, and the
    /// container collapses (<c>inline-flex-column-image-load</c>). Both of those pass today and
    /// must keep passing.
    /// </para>
    /// <para>
    /// Outside those conditions the stretch still belongs on the replaced element and is still not
    /// applied — that is the general form of this gap, and it wants the cross size pushed onto the
    /// box during flex layout rather than a declaration rewritten before it.
    /// </para>
    /// </remarks>
    internal static bool IsStretchedColumnFlexItem(CssBox box)
    {
        if (box?.ParentBox is not { } parent)
            return false;

        // `inline-flex` is deliberately excluded: its cross size is shrink-to-fit, so a
        // percentage against it is circular. Only a block-level `flex` container's cross size is
        // settled without consulting the item.
        if (parent.Display != "flex" || !parent.IsColumnFlexContainer() || !IsInFlowItem(box))
            return false;

        // `align-self: auto` defers to the container's `align-items`; `normal` behaves as
        // `stretch` for a flex item, and it is what an unstyled container has.
        var align = box.AlignSelf;
        if (string.IsNullOrEmpty(align) || align is "auto")
            align = parent.AlignItems;
        if (!(string.IsNullOrEmpty(align) || align is "normal" or "stretch"))
            return false;

        if (!string.IsNullOrEmpty(box.Width) && box.Width != CssConstants.Auto)
            return false;

        if (box.MarginLeft == CssConstants.Auto || box.MarginRight == CssConstants.Auto)
            return false;

        return !HasInlineAxisSpacing(box);
    }

    /// <summary>
    /// Whether the box has any inline-axis padding or border, which is what separates a
    /// content-box percentage width from a border-box stretch.
    /// </summary>
    private static bool HasInlineAxisSpacing(CssBox box) =>
        !IsZeroLength(box.PaddingLeft)
        || !IsZeroLength(box.PaddingRight)
        || DrawsBorder(box.BorderLeftStyle, box.BorderLeftWidth)
        || DrawsBorder(box.BorderRightStyle, box.BorderRightWidth);

    private static bool IsZeroLength(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) || v is "0" or "0px" or "0%" or "0em" or "0rem";
    }

    /// <summary>A border occupies space only when its style is neither <c>none</c> nor
    /// <c>hidden</c>; the <c>medium</c> default width means nothing on its own.</summary>
    private static bool DrawsBorder(string? style, string? width)
    {
        var s = style?.Trim();
        if (string.IsNullOrEmpty(s) || s is CssConstants.None or "hidden")
            return false;
        return !IsZeroLength(width);
    }

    /// <summary>
    /// CSS Flexbox §4 / CSS Grid §6: "each contiguous sequence of child text runs is wrapped in an
    /// anonymous block container flex item… However, if the entire sequence of child text runs
    /// contains only white space it is instead not rendered."
    /// </summary>
    /// <remarks>
    /// <para>
    /// A text run is not an element, so it carries no <c>display</c> for <see cref="Blockify"/> to
    /// blockify — and the inline/block correction that would ordinarily wrap it never reaches it
    /// either, because a flex/grid container's children are items rather than inline content. The
    /// run therefore stayed a display-less box, which <see cref="CssBox.PerformLayout"/> resolves
    /// no size for: text written directly inside a flex or grid container simply did not appear.
    /// </para>
    /// <para>
    /// duckduckgo.com's start page writes its search-mode chips that way —
    /// <c>&lt;span style="display:flex"&gt;&lt;button&gt;&lt;svg/&gt;&lt;/button&gt;Search&lt;/span&gt;</c>
    /// — so the icons rendered and the "Search" and "Ask AI" labels beside them did not.
    /// </para>
    /// <para>
    /// A white-space-only sequence is left unwrapped rather than deleted: it is not rendered either
    /// way (a display-less box lays out as nothing, and <c>CorrectTextBoxes</c> drops it a moment
    /// later), and leaving it in place keeps this pass from changing anything about a tree that has
    /// no real text run in a flex or grid container.
    /// </para>
    /// </remarks>
    private static void WrapTextRunsInAnonymousItems(CssBox box)
    {
        bool anyRenderedRun = false;
        foreach (var child in box.Boxes)
        {
            if (IsTextRun(child) && !IsWhiteSpaceOnly(child))
            {
                anyRenderedRun = true;
                break;
            }
        }

        if (!anyRenderedRun)
            return;

        // Rebuilt rather than spliced in place, for the reason AnonymousTableBoxes.WrapRuns is:
        // creating the wrapper appends it to this list, which is what puts it where the run began.
        var children = new List<CssBox>(box.Boxes);
        box.Boxes.Clear();

        List<CssBox> pending = [];

        void Flush()
        {
            if (pending.Count == 0)
                return;

            bool rendered = false;
            foreach (var run in pending)
                rendered |= !IsWhiteSpaceOnly(run);

            if (rendered)
            {
                var wrapper = CssBoxHelper.CreateBox(box, box.BaseUrl);
                wrapper.Display = CssConstants.Block;

                foreach (var run in pending)
                    run.ParentBox = wrapper;
            }
            else
            {
                foreach (var run in pending)
                    run.ParentBox = box;
            }

            pending.Clear();
        }

        foreach (var child in children)
        {
            if (IsTextRun(child))
            {
                pending.Add(child);
                continue;
            }

            Flush();
            child.ParentBox = box;
        }

        Flush();
    }

    /// <summary>
    /// Whether the box is one of the anonymous boxes the DOM→box walk makes for a run of text:
    /// no element of its own, no children, and text to show.
    /// </summary>
    private static bool IsTextRun(CssBox box) =>
        box.HtmlTag == null
        && box.Boxes.Count == 0
        && !box.Text.IsEmpty
        && box.Display != CssConstants.None;

    private static bool IsWhiteSpaceOnly(CssBox box) =>
        box.Text.Span.IsWhiteSpace()
        && box.WhiteSpace != CssConstants.Pre
        && box.WhiteSpace != CssConstants.PreWrap
        && box.WhiteSpace != "pre-line"
        && box.WhiteSpace != "break-spaces";

    /// <summary>
    /// CSS Display 3 §2.7 / CSS Flexbox §4: only in-flow children become items. A
    /// <c>display: none</c> child generates no box, and an absolutely positioned one is not an
    /// item at all (it is blockified in its own right, by the out-of-flow rule, where it already
    /// is). A <c>display: contents</c> child is not a box either — its children are the items —
    /// so blockifying it would give the box a formatting role the spec says it does not have.
    /// <para>
    /// A <em>floated</em> child is in flow here, and deliberately so: CSS Flexbox §3 says
    /// <c>float</c> does not take a flex item out of flow, so it is an item like any other.
    /// </para>
    /// </summary>
    private static bool IsInFlowItem(CssBox child) =>
        child.Display != CssConstants.None
        && child.Display != "contents"
        && child.Position is not (CssConstants.Absolute or CssConstants.Fixed);

    /// <summary>
    /// The blockified equivalent of a computed <c>display</c>: the inline-level displays map to
    /// their block-level counterparts and everything else — including a display that is already
    /// block-level, and the layout-internal table displays, which blockify to themselves — is
    /// returned unchanged.
    /// </summary>
    /// <remarks>
    /// An anonymous text run carries no display of its own. It is inline-level content, and the
    /// spec wraps such a run in an anonymous block; the engine's own inline/block correction does
    /// exactly that a moment later, so leaving the empty display alone lets that pass handle it
    /// rather than turning a text run into a block box with no text semantics.
    /// </remarks>
    private static string BlockifiedDisplay(string display) => display switch
    {
        CssConstants.Inline or CssConstants.InlineBlock or "flow-root" => CssConstants.Block,
        "inline-flex" => "flex",
        "inline-grid" => "grid",
        CssConstants.InlineTable => CssConstants.Table,
        // A list item stays a list item; blockification only changes the outer display type, and
        // `list-item` is already block-level on the outside.
        _ => display,
    };
}
