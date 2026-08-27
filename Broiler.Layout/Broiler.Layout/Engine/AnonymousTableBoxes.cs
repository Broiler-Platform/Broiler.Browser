using Broiler.CSS;


namespace Broiler.Layout.Engine;

/// <summary>
/// CSS 2.1 §17.2.1 <em>generate missing parents</em>: wraps table-internal boxes whose
/// parent is not the table box they require in the anonymous <c>table-row</c> /
/// <c>table</c> boxes the spec calls for.
/// <para>
/// <see cref="CssLayoutEngineTable"/> already implements the other half of §17.2.1 —
/// <em>generate missing children</em> — but it only ever runs on a box that is already
/// a table, so it could not see the mirror-image case: a <c>table-row</c>,
/// <c>table-row-group</c> or <c>table-cell</c> sitting directly inside a block or an
/// inline. Such a box is neither <c>IsBlock</c> nor <c>IsInline</c>, so block layout
/// walked straight past it and it was **dropped** — it never laid out, never painted,
/// and contributed no height. WPT's <c>css/CSS2/tables/table-anonymous-objects-*</c>
/// family is built almost entirely out of that pattern (its green "should be no red"
/// layer is spans carrying nothing but <c>display: table-row-group</c>), which is why
/// those tests rendered a bare red table with the green overlay missing.
/// </para>
/// <para>
/// The pass runs over the whole box tree after the other box fix-ups, before the
/// inline/block corrections, so the anonymous table it inserts takes part in the
/// inline↔block splitting as the block-level (or inline-level) box it is.
/// </para>
/// </summary>
internal static class AnonymousTableBoxes
{
    /// <summary>
    /// Applies §17.2.1's missing-parent generation to <paramref name="root"/> and every
    /// box beneath it. Safe to call repeatedly: a tree that already has the required
    /// parents is left untouched.
    /// </summary>
    /// <param name="root">Root of the box tree to fix up.</param>
    /// <param name="baseUrl">Base URL carried onto the anonymous boxes.</param>
    internal static void Generate(CssBox root, Uri baseUrl)
    {
        if (root == null)
            return;

        GenerateMissingParents(root, baseUrl);
    }

    private static void GenerateMissingParents(CssBox box, Uri baseUrl)
    {
        // A proper table parent's interior is CssLayoutEngineTable's job: it generates the
        // missing *children* there (anonymous rows for stray cells in a table or a row
        // group, anonymous cells for stray content in a row). Leaving those alone keeps
        // this pass strictly additive — it only reaches boxes that are dropped today.
        //
        // A flex or grid container is skipped for a different reason: CSS Display 3 §2.7
        // blockifies its children, so a `display: table-cell` flex item computes to `block`
        // and there is no table-internal box left for §17.2.1 to find a parent for. Wrapping
        // one would put an anonymous table between the container and its item — the item
        // would stop being a flex/grid item at all, which is a worse answer than the
        // blockification Broiler has yet to implement, and it would land in css-grid and
        // css-flexbox, the two largest failing areas.
        if (!IsProperTableParent(box.Display) && !IsBlockifyingContainer(box.Display))
        {
            WrapCellRunsInAnonymousRows(box, baseUrl);
            WrapProperTableChildRunsInAnonymousTables(box, baseUrl);
        }

        // Indexed, and deliberately not over a copy: this pass runs on every layout, so a
        // defensive snapshot per box would be an allocation per box. The recursion below only
        // ever rewrites the *child's* own Boxes list, never this one — and the two passes
        // above have already finished rewriting it — so it is stable for the walk. The
        // anonymous boxes they inserted are walked too; their children are well parented by
        // construction, so those calls terminate immediately.
        for (int i = 0; i < box.Boxes.Count; i++)
            GenerateMissingParents(box.Boxes[i], baseUrl);
    }

    /// <summary>
    /// §17.2.1 missing parents, rule 1: a <c>table-cell</c> whose parent is not a
    /// <c>table-row</c> is wrapped — together with its consecutive <c>table-cell</c>
    /// siblings — in one anonymous <c>table-row</c>.
    /// </summary>
    private static void WrapCellRunsInAnonymousRows(CssBox box, Uri baseUrl) =>
        WrapRuns(
            box,
            baseUrl,
            isRunMember: static child => child.Display == CssConstants.TableCell && !IsBlockified(child),
            wrapperDisplay: CssConstants.TableRow);

    /// <summary>
    /// §17.2.1 missing parents, rule 2: a misparented proper table child is wrapped —
    /// together with its consecutive proper-table-child siblings — in one anonymous
    /// <c>table</c>, or <c>inline-table</c> when the parent is an inline box.
    /// <para>
    /// Every proper table child is misparented here by construction: this only runs on a
    /// box that is not a proper table parent, and each of §17.2.1's misparenting clauses
    /// ("a row group … is misparented if its parent is neither a 'table' box nor an
    /// 'inline-table' box", and likewise for rows and columns) holds for exactly those
    /// parents.
    /// </para>
    /// </summary>
    private static void WrapProperTableChildRunsInAnonymousTables(CssBox box, Uri baseUrl) =>
        WrapRuns(
            box,
            baseUrl,
            isRunMember: static child => IsProperTableChild(child.Display) && !IsBlockified(child),
            wrapperDisplay: box.Display == CssConstants.Inline
                ? CssConstants.InlineTable
                : CssConstants.Table);

    /// <summary>
    /// Replaces each maximal run of children matching <paramref name="isRunMember"/> with a
    /// single anonymous box of <paramref name="wrapperDisplay"/> holding that run, keeping
    /// document order for everything else.
    /// </summary>
    private static void WrapRuns(
        CssBox box,
        Uri baseUrl,
        Func<CssBox, bool> isRunMember,
        string wrapperDisplay)
    {
        bool anyRunMember = false;
        foreach (var child in box.Boxes)
        {
            if (isRunMember(child))
            {
                anyRunMember = true;
                break;
            }
        }

        if (!anyRunMember)
            return;

        var children = new List<CssBox>(box.Boxes);
        box.Boxes.Clear();

        // Pending run members, plus any whitespace-only boxes seen since the last run
        // member. §17.2.1: "anonymous inline boxes which contain only white space and are
        // between two immediate siblings each of which is a table-non-root element" are
        // treated as display:none — so whitespace *inside* a run is dropped, while
        // whitespace that turns out to be trailing is put back outside the wrapper.
        List<CssBox> pending = [];
        List<CssBox> pendingWhitespace = [];

        void Flush()
        {
            if (pending.Count > 0)
            {
                // The CssBox(parent, tag) constructor appends to box.Boxes, so creating the
                // wrapper here places it exactly where the run began.
                var wrapper = CssBoxHelper.CreateBox(box, baseUrl);
                wrapper.Display = wrapperDisplay;

                foreach (var member in pending)
                    member.ParentBox = wrapper;

                pending.Clear();
            }

            // Whitespace after the final run member was never between two run members, so
            // it is ordinary whitespace and keeps its place after the wrapper.
            foreach (var whitespace in pendingWhitespace)
                box.Boxes.Add(whitespace);

            pendingWhitespace.Clear();
        }

        foreach (var child in children)
        {
            if (isRunMember(child))
            {
                // Whitespace between two run members is dropped by simply not re-adding it.
                pendingWhitespace.Clear();
                pending.Add(child);
            }
            else if (pending.Count > 0 && IsIgnorableWhitespace(child))
            {
                pendingWhitespace.Add(child);
            }
            else
            {
                Flush();
                box.Boxes.Add(child);
            }
        }

        Flush();
    }

    /// <summary>
    /// Whether the box is an anonymous run of white space that §17.2.1 treats as
    /// <c>display:none</c> when it falls between two table-internal siblings. A
    /// <c>pre</c>-family <c>white-space</c> makes the run meaningful, so it is excluded.
    /// </summary>
    private static bool IsIgnorableWhitespace(CssBox box) =>
        box.Boxes.Count == 0
        && !box.Text.IsEmpty
        && box.Text.Span.IsWhiteSpace()
        && box.WhiteSpace != CssConstants.Pre
        && box.WhiteSpace != CssConstants.PreWrap
        && box.WhiteSpace != "pre-line"
        && box.WhiteSpace != "break-spaces";

    /// <summary>
    /// Whether an out-of-flow or floated box, whose computed <c>display</c> CSS Display 3
    /// §2.7 blockifies, so it is no longer a proper table child and neither joins a run nor
    /// starts one. It splits a run for the same reason.
    /// </summary>
    private static bool IsBlockified(CssBox box) =>
        box.Float != CssConstants.None
        || box.Position is CssConstants.Absolute or CssConstants.Fixed;

    /// <summary>
    /// Whether a box with this display blockifies its children (CSS Display 3 §2.7), leaving
    /// no table-internal child for §17.2.1 to act on.
    /// </summary>
    private static bool IsBlockifyingContainer(string display) =>
        display is "flex" or "inline-flex" or "grid" or "inline-grid";

    /// <summary>
    /// Whether a box with this display may legally parent table-internal boxes, i.e.
    /// whether <see cref="CssLayoutEngineTable"/> owns its interior.
    /// </summary>
    private static bool IsProperTableParent(string display) =>
        display is CssConstants.Table
            or CssConstants.InlineTable
            or CssConstants.TableRow
            or CssConstants.TableRowGroup
            or CssConstants.TableHeaderGroup
            or CssConstants.TableFooterGroup
            or CssConstants.TableColumnGroup;

    /// <summary>
    /// The "proper table child" set of CSS 2.1 §17.2.1 — the displays that require a
    /// <c>table</c> or <c>inline-table</c> ancestry. <c>table-cell</c> is deliberately
    /// absent: it needs a <c>table-row</c> parent, which rule 1 supplies first.
    /// </summary>
    private static bool IsProperTableChild(string display) =>
        display is CssConstants.TableRow
            or CssConstants.TableRowGroup
            or CssConstants.TableHeaderGroup
            or CssConstants.TableFooterGroup
            or CssConstants.TableColumn
            or CssConstants.TableColumnGroup
            or CssConstants.TableCaption;
}
