using Broiler.CSS;
using Broiler.Layout.Engine;

namespace Broiler.Layout.Diagnostics;

/// <summary>
/// One walk of the finished box tree that counts how much of it sits under a subtree item #13
/// proposes to lay out in parallel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a census and not a timer.</b> Item #13's second shape is "parallel independent subtrees
/// (abspos/fixed, flex+grid items, table cells, multicol, subdocuments)". Whatever such a split is
/// worth, it is bounded above by the share of the tree that <em>is</em> in one of those subtrees:
/// threads cannot help with a box that is not in one. That share is a property of the document, not
/// of the clock, so it is counted exactly rather than inferred from a measurement that drifts —
/// Phase 4 §3's lesson, applied at the point where it is cheapest.
/// </para>
/// <para>
/// <b>The union, not the sum.</b> An abspos box inside a table cell is inside two independent
/// subtrees, and a naive per-class sum would count it twice and could report more independent boxes
/// than the tree contains. <see cref="LayoutWorkTrace.Counters.IndependentBoxes"/> counts a box once
/// if <em>any</em> ancestor-or-self is an independent root, which is the number the ceiling is
/// actually bounded by; the per-class counters are published next to it so a reader can see which
/// class supplies it.
/// </para>
/// <para>
/// <b>What it deliberately does not count.</b> Multi-column and subdocuments/iframes are on item
/// #13's list and are not classified here: a column is not a box in this engine (multicol is applied
/// as a post-layout pass over a single flow) and an iframe's subdocument is laid out by its own
/// container rather than as a box of this tree, so neither has a root this walk could name. Reporting
/// them as zero would read as "the corpus has none" rather than "this walk does not look"; they are
/// named in the published output instead.
/// </para>
/// </remarks>
internal static class LayoutTreeCensus
{
    /// <summary>Walks <paramref name="root"/> and records the structural counters.</summary>
    public static void Record(CssBox root)
    {
        Walk(root, depth: 1, underIndependent: false);
    }

    private static void Walk(CssBox box, int depth, bool underIndependent)
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.TreeBoxes);
        LayoutWorkTrace.CountMax(LayoutWorkTrace.Counters.TreeDepth, depth);

        var isAbspos = box.Position == CssConstants.Absolute || box.Position == CssConstants.Fixed;
        var isTableCell = box.Display == CssConstants.TableCell;
        var isFlexGridItem = IsFlexOrGridContainer(box.ParentBox?.Display);

        if (isAbspos)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.AbsposRoots);
        if (isTableCell)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.TableCellRoots);
        if (isFlexGridItem)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.FlexGridRoots);

        var independent = underIndependent || isAbspos || isTableCell || isFlexGridItem;
        if (independent)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IndependentBoxes);

        // Per-class subtree sizes are accumulated by re-walking from each root, which is why they can
        // exceed the tree size and the union counter above cannot.
        if (isAbspos)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.AbsposBoxes, CountSubtree(box));
        if (isTableCell)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.TableCellBoxes, CountSubtree(box));
        if (isFlexGridItem)
            LayoutWorkTrace.Count(LayoutWorkTrace.Counters.FlexGridBoxes, CountSubtree(box));

        foreach (var child in box.Boxes)
            Walk(child, depth + 1, independent);
    }

    private static int CountSubtree(CssBox box)
    {
        var total = 1;
        foreach (var child in box.Boxes)
            total += CountSubtree(child);
        return total;
    }

    private static bool IsFlexOrGridContainer(string display) =>
        display is "flex" or "inline-flex" or "grid" or "inline-grid";
}
