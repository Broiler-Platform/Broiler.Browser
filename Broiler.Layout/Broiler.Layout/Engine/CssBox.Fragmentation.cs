using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// CSS Fragmentation 3 — page breaks in the block flow: the forced ones (§3) and the automatic
/// decision for content that must not be split (§4.1).
/// </summary>
/// <remarks>
/// <para>
/// The engine paints one continuous surface, and a paged render is that surface cut into
/// page-height strips: page <c>k</c> is the band from <c>k·H</c> to <c>(k+1)·H</c>. That is a real
/// model rather than a trick, because the container already separates the two sizes it needs —
/// <c>PageSize</c>, which is what <c>vw</c>/<c>vh</c> resolve against, from <c>MaxSize</c>, the
/// surface actually rasterised. A forced break is then a placement rule: move the box down to the
/// next band boundary and let the flow below it follow.
/// </para>
/// <para>
/// Inert unless a page size is in force. A screen render leaves <c>PageSize</c> at its
/// no-pagination default, so no boundary ever falls inside the content and every box stays where
/// the flow put it — which is why this can be applied unconditionally rather than behind a mode
/// flag.
/// </para>
/// <para>
/// <b>Automatic fragmentation costs almost nothing in this model, and that is the point.</b> A box
/// that runs past a boundary already continues at the top of the next band, because the bands are
/// cut from one continuous surface — splitting is what the geometry does by itself. The only thing
/// that has to be decided is what must <em>not</em> be cut, which is why the automatic half here is
/// a monolithic test and a nudge rather than a box-splitting engine.
/// </para>
/// <para>
/// <b>Where the boundaries come from.</b> They are defined by <c>@page</c> — its <c>size</c> and its
/// <c>margin</c> give the page area, and nothing else does. Paginating at the viewport instead is
/// not an approximation of that but a different set of boundaries: measured over the 409 print
/// reftests with the runner wired to the viewport, 252 → 228 passing, the losses concentrated in
/// <c>css/CSS2/pagination</c>, whose tests declare <c>@page { size: 5in 3in; margin: 0.5in }</c> —
/// a two-inch page area — and were being cut at 768px. The runner resolves the real box
/// (<c>WptPageBox</c>) and renders pages from it behind <c>WptTestRunner.PagedPrint</c>.
/// </para>
/// <para>
/// <b>What is still missing.</b> Named pages with per-name <c>@page</c> sizes, <c>@page</c> margin
/// boxes, and fragmentation of flex, grid and table content — each of which one part of the corpus
/// rests on. That is why the paged run is behind a lever and off by default: at 213 of 400 it is
/// still below the 252 the same tests score unpaginated, where a test and its reference are
/// unpaginated together and agree.
/// </para>
/// </remarks>
internal partial class CssBox
{
    /// <summary>
    /// A page size this large means "not paginated" — the value
    /// <c>HtmlContainer</c> installs for an ordinary screen render.
    /// </summary>
    private const double UnpaginatedPageExtent = 90000;

    /// <summary>
    /// Whether a <c>break-before</c>/<c>break-after</c> value forces a page break. The named page
    /// sides (<c>left</c>, <c>right</c>, <c>recto</c>, <c>verso</c>) force one and additionally
    /// constrain which side it lands on, which needs page numbering this does not have; forcing the
    /// break is the half that is representable, and it is the half these values share.
    /// </summary>
    private static bool ForcesPageBreak(string value)
    {
        var v = value?.Trim();
        return v is not null
            && (v.Equals("page", System.StringComparison.OrdinalIgnoreCase)
                || v.Equals("always", System.StringComparison.OrdinalIgnoreCase)
                || v.Equals(CssConstants.Left, System.StringComparison.OrdinalIgnoreCase)
                || v.Equals(CssConstants.Right, System.StringComparison.OrdinalIgnoreCase)
                || v.Equals("recto", System.StringComparison.OrdinalIgnoreCase)
                || v.Equals("verso", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// CSS Paged Media 3 §3.4: the used value of <c>page</c> — this box's own name when it declares
    /// one, otherwise the nearest ancestor's, and the empty name when none does.
    /// </summary>
    /// <remarks>
    /// The property is not inherited, but <c>auto</c> resolves up the box tree, which is not the
    /// same thing: an ancestor's name reaches a descendant that declares nothing, while a
    /// descendant that declares <c>auto</c> explicitly resolves the same way rather than to the
    /// initial value. Walking the chain is therefore the resolution, not a cache of it.
    /// </remarks>
    private string UsedPageName()
    {
        for (var box = this; box is not null; box = box.ParentBox)
        {
            if (!box.TakesAPageName())
                continue;

            var name = box.Page?.Trim();
            if (!string.IsNullOrEmpty(name)
                && !name.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Whether a page name declared on this box applies to it. <c>page</c> names the page a
    /// block-level box is laid out on, so an inline-level one — which shares its parent's line, and
    /// so its parent's page — declares a name that has nothing to name.
    /// </summary>
    /// <remarks>
    /// <c>css-page/page-name-img-001</c> is the case: <c>&lt;body page:a&gt;</c> with an
    /// <c>&lt;img page:b&gt;</c> followed by a <c>&lt;div page:b&gt;</c>. Its reference breaks
    /// between the image and the div, which only happens if the image's own <c>page:b</c> is
    /// ignored and it stays on the body's page <c>a</c>.
    /// </remarks>
    private bool TakesAPageName() =>
        Display is not (CssConstants.Inline or CssConstants.InlineBlock or CssConstants.InlineTable
            or "inline-flex" or "inline-grid" or CssConstants.None);

    /// <summary>
    /// The page name the <em>first</em> page this box occupies carries, and the one the
    /// <em>last</em> one does — its own used name when it has no in-flow content of its own, and
    /// otherwise the name propagated up from the descendant that starts (or ends) it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page name names the page a box is laid out on, so a box wrapping content that names a
    /// different page does not itself claim a page — the innermost name is the one the page has.
    /// <c>&lt;div page:b&gt;&lt;div page:c&gt;&lt;div page:a&gt;…</c> begins a page named <c>a</c>,
    /// not <c>b</c>, and WPT's <c>css-page/page-name-propagated-*</c> are written to pin exactly
    /// that: their references state the same layout with no page names at all, so an engine that
    /// read the outermost name would break where the reference does not.
    /// </para>
    /// <para>
    /// Both ends are needed because the two are different names on a box whose first and last
    /// descendants disagree, and the break rule compares the end of one sibling against the start
    /// of the next.
    /// </para>
    /// </remarks>
    private string StartPageName()
    {
        if (!PropagatesPageNamesFromChildren())
            return UsedPageName();

        foreach (var child in Boxes)
        {
            if (child.ParticipatesInPageNamePropagation())
                return child.StartPageName();
        }

        return UsedPageName();
    }

    /// <inheritdoc cref="StartPageName"/>
    private string EndPageName()
    {
        if (!PropagatesPageNamesFromChildren())
            return UsedPageName();

        for (int i = Boxes.Count - 1; i >= 0; i--)
        {
            if (Boxes[i].ParticipatesInPageNamePropagation())
                return Boxes[i].EndPageName();
        }

        return UsedPageName();
    }

    /// <summary>
    /// Whether this box carries a page name up to its ancestors — it is in the flow and generates
    /// something on a page.
    /// </summary>
    /// <remarks>
    /// Out-of-flow boxes are excluded because they are laid out against a containing block rather
    /// than the flow, and <c>page-name-propagated-003</c> and <c>-005</c> are built on that: each
    /// puts an absolutely positioned box where the first (or last) in-flow child would be, and its
    /// reference shows the name propagating past it. Collapsed whitespace is excluded for the more
    /// mundane reason that these tests are written with their markup indented, so the newline
    /// between two named <c>&lt;div&gt;</c>s must not be read as content of the box around them.
    /// </remarks>
    private bool ParticipatesInPageNamePropagation()
    {
        if (Display == CssConstants.None
            || Position is CssConstants.Absolute or CssConstants.Fixed)
        {
            return false;
        }

        if (Boxes.Count > 0)
            return true;

        foreach (var word in Words)
        {
            if (!word.IsSpaces || word.IsImage)
                return true;
        }

        return Size.Height > 0.01;
    }

    /// <summary>
    /// Whether this box must not be split across a fragmentation boundary — "monolithic" in
    /// CSS Fragmentation 3 §4.1 — so a boundary falling inside it moves the whole box instead.
    /// </summary>
    /// <remarks>
    /// Everything else is breakable, and in this model breakable content needs no work at all: the
    /// surface is continuous and the pages are bands cut from it, so a box that runs past a
    /// boundary already continues at the top of the next band. Fragmenting it is what the geometry
    /// does by itself; the only thing that has to be *decided* is what must not be cut.
    /// </remarks>
    private bool IsMonolithicForFragmentation()
    {
        if (BreakInsideAvoids(BreakInside) || BreakInsideAvoids(PageBreakInside))
            return true;

        // A scroll container fragments as a unit — its content scrolls rather than continuing on
        // the next page.
        if (!string.IsNullOrEmpty(Overflow)
            && !Overflow.Equals(CssConstants.Visible, StringComparison.OrdinalIgnoreCase))
            return true;

        // Size containment makes a box's contents unobservable from outside, so it cannot be
        // fragmented — the css-break monolithic tests are built on exactly this.
        if (HasSizeContainment(Contain))
            return true;

        // Atomic inlines and replaced content are single, indivisible boxes.
        return Display is CssConstants.InlineBlock or "inline-flex" or "inline-grid"
                or CssConstants.InlineTable
            || IsImage;
    }

    private static bool BreakInsideAvoids(string value)
    {
        var v = value?.Trim();
        return v is not null
            && (v.Equals(CssConstants.Avoid, StringComparison.OrdinalIgnoreCase)
                || v.Equals("avoid-page", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a <c>contain</c> value applies size containment.</summary>
    private static bool HasSizeContainment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var token in value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("size", StringComparison.OrdinalIgnoreCase)
                || token.Equals("strict", StringComparison.OrdinalIgnoreCase)
                || token.Equals("content", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Moves an unbreakable <paramref name="child"/> off a page boundary that falls inside it, to
    /// the top of the next page. Returns the distance moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only when the box would actually fit on a page of its own: one taller than the page has to
    /// be cut somewhere however monolithic it is, and pushing it would leave a blank page and then
    /// overflow anyway. That is the same judgement <c>BreakPage</c> already made, and the same
    /// reason it declines.
    /// </para>
    /// <para>
    /// Called from the block child loop, so it covers block-level children only. An atomic inline
    /// is monolithic as well, and so are grid and flex items — <c>css-break/grid/monolithic-overflow-print</c>
    /// is a grid of size-contained items and needs the grid path to do this at row granularity —
    /// but each is placed by its own layout path and none of those are hooked yet.
    /// </para>
    /// </remarks>
    internal double ApplyMonolithicPageFit(CssBox child)
    {
        if (LayoutEnvironment is not { } environment)
            return 0;

        double pageHeight = environment.PageSize.Height;
        if (pageHeight <= 0 || pageHeight >= UnpaginatedPageExtent)
            return 0;

        if (child.Position is CssConstants.Absolute or CssConstants.Fixed
            || child.Display == CssConstants.None
            || !child.IsMonolithicForFragmentation())
        {
            return 0;
        }

        double origin = environment.MarginTop;

        double top = child.Location.Y - origin;
        double height = child.ActualBottom - child.Location.Y;

        if (top < 0 || height <= 0 || height > pageHeight)
            return 0;

        double intoPage = top % pageHeight;
        double remaining = pageHeight - intoPage;

        // Fits in what is left of this page, or starts exactly on a boundary: nothing to do.
        if (intoPage <= 0.01 || height <= remaining + 0.01)
            return 0;

        child.OffsetTop(remaining);
        return remaining;
    }

    /// <summary>
    /// CSS Paged Media 3 §3.4: whether <paramref name="child"/> begins a different page type than
    /// the box before it, which forces a break between them.
    /// </summary>
    /// <remarks>
    /// This is the whole of what named pages mean for the flow. A page name selects which
    /// <c>@page</c> rule applies to the pages a box lands on, so two boxes with different used names
    /// cannot share one — the break is a consequence of the naming, not a separate declaration.
    /// WPT's <c>css-page/page-name-*</c> are built on exactly that: the test names a page and the
    /// reference states the same layout with <c>break-before: page</c>.
    /// </remarks>
    private bool StartsANewPageType(CssBox child, CssBox? previous) =>
        previous is not null
        && CarriesThePageFlow()
        && child.ParticipatesInPageNamePropagation()
        && !string.Equals(child.StartPageName(), previous.EndPageName(), StringComparison.Ordinal);

    /// <summary>
    /// The sibling a page-name change is measured against: the last one before <paramref name="child"/>
    /// that puts something on a page.
    /// </summary>
    /// <remarks>
    /// Not the same as the previous in-flow sibling, which is what a <c>break-after</c> is read from.
    /// A box that generates nothing — the collapsed whitespace between two elements, most of all —
    /// still carries a used page name, its ancestor's, and reading that name would insert a break
    /// wherever the markup happens to be indented. <c>page-name-img-003</c> is the case: the
    /// newline between <c>&lt;body page:a&gt;</c> and an <c>&lt;img page:b&gt;</c> would break
    /// before the image, where its reference has no break at all. A zero-height box that <em>is</em>
    /// written in the document still carries its own <c>break-after</c>, which is why this is a
    /// second lookup rather than a narrowing of the first.
    /// </remarks>
    private static CssBox? PreviousOnAPage(IReadOnlyList<CssBox> siblings, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            var sibling = siblings[i];
            if (sibling.Float == CssConstants.None && sibling.ParticipatesInPageNamePropagation())
                return sibling;
        }

        return null;
    }

    /// <summary>
    /// Whether the children of this box are laid out in the page's own block flow, so a page-name
    /// change between two of them is a page break.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things take a box's children out of that flow, and WPT has a <c>page-name</c> test for
    /// each. A flex or grid container places items rather than stacking them, so an item's page
    /// name names nothing to break at (<c>page-name-flex-001</c>, <c>-002</c>) — while a name on the
    /// container itself still breaks, because the container <em>is</em> in its parent's flow
    /// (<c>page-name-flex-003</c>). And a box whose block axis is not the page's stacks its children
    /// sideways, where a page boundary does not fall (<c>page-name-orthogonal-writing-003</c>) —
    /// though a box inside it that turns back upright is in the page's axis again, which is what
    /// <c>-004</c> pins.
    /// </para>
    /// <para>
    /// <b>Being out of flow is not a third.</b> An out-of-flow box does not carry its <em>own</em>
    /// name into its parent's flow — that is
    /// <see cref="ParticipatesInPageNamePropagation"/>'s job, and <c>page-name-abspos-001</c> and
    /// <c>-003</c> are built on it — but its children are stacked in its own block flow and a name
    /// change between two of them is still a page break. <c>page-name-003</c> states that, citing
    /// the Chromium bug that established it, and Chromium agrees: printed to PDF it gives that test
    /// two pages and its two-page reference two.
    /// </para>
    /// <para>
    /// <c>page-name-abspos-002</c> is the same markup asserting the opposite, and it is the odd one
    /// out rather than the rule — Chromium prints it as two pages against its one-page reference,
    /// so it fails there too. See
    /// <c>docs/wpt-rendering-gaps-wont-fix.md</c>.
    /// </para>
    /// <para>
    /// Only the page-<em>name</em> rule is gated here. An explicit <c>break-before: page</c> means
    /// what it says wherever it is written, and the references of these very tests are built from
    /// it.
    /// </para>
    /// </remarks>
    private bool CarriesThePageFlow()
    {
        if (Display is "flex" or "inline-flex" or "grid" or "inline-grid")
            return false;

        return string.IsNullOrEmpty(WritingMode)
            || WritingMode.Equals("horizontal-tb", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a page name propagates up out of this box's children. The same three exclusions as
    /// <see cref="CarriesThePageFlow"/>: a name that cannot break where it is written must not be
    /// carried out to a place where it would.
    /// </summary>
    private bool PropagatesPageNamesFromChildren() =>
        Display is not ("flex" or "inline-flex" or "grid" or "inline-grid");

    /// <summary>
    /// Moves <paramref name="child"/> to the top of the next page when a break is forced before it,
    /// or after the box that precedes it. Returns the distance moved, so the caller can carry it
    /// into the sibling that follows.
    /// </summary>
    /// <remarks>
    /// Applied after the child is laid out rather than before, because its position is only known
    /// then — and the whole laid-out subtree moves with it, which is what
    /// <see cref="OffsetTop"/> is for. Nothing else needs adjusting:
    /// <see cref="CssBoxProperties.ActualBottom"/> is derived from the box's origin
    /// (<c>Location.Y + Size.Height</c>), so moving the origin carries the bottom edge — the edge
    /// the next sibling positions itself from — with it. Advancing it as well, which this used to
    /// do, does not translate the box but *stretches* it: the setter resizes rather than moves, so
    /// every pushed box grew by the distance it was pushed and everything after it drifted. That is
    /// what made a two-page document paginate as five.
    /// </remarks>
    internal double ApplyForcedPageBreakBefore(CssBox child, int childIndex, CssBox? previous)
    {
        if (LayoutEnvironment is not { } environment)
            return 0;

        double pageHeight = environment.PageSize.Height;
        if (pageHeight <= 0 || pageHeight >= UnpaginatedPageExtent)
            return 0;

        if (child.Position is CssConstants.Absolute or CssConstants.Fixed
            || child.Display == CssConstants.None)
        {
            return 0;
        }

        bool afterAForcedBreak = previous is not null && ForcesPageBreak(previous.BreakAfter);

        if (child.Float != CssConstants.None)
        {
            // A float declares no break of its own — <c>break-before</c> does not apply to it, and
            // it carries no page name into the flow — but a break the sibling before it forces is a
            // break in the flow the float is placed in, so the float belongs after it too. Without
            // this a float is the one thing a forced break steps over: `page-size-007-print`'s
            // reference puts `break-after: page` on a div and a float immediately after it, and the
            // float stayed on the page the break had just ended while the text following it moved.
            if (!afterAForcedBreak)
                return 0;
        }
        else if (!ForcesPageBreak(child.BreakBefore)
            && !afterAForcedBreak
            && !StartsANewPageType(child, PreviousOnAPage(Boxes, childIndex)))
        {
            return 0;
        }

        double origin = environment.MarginTop;
        double offsetInFlow = child.Location.Y - origin;
        if (offsetInFlow < 0)
            return 0;

        // Already at the top of a page: the break is satisfied and moving would insert a blank one.
        double intoPage = offsetInFlow % pageHeight;
        if (intoPage <= 0.01)
            return 0;

        double delta = pageHeight - intoPage;
        child.OffsetTop(delta);
        return delta;
    }
}
