using Broiler.CSS;


namespace Broiler.Layout.Engine;

/// <summary>
/// Box-tree navigation helpers used by the layout engine (previous-sibling /
/// in-flow-sibling resolution, inline-content and whitespace checks). Split out
/// of <c>DomUtils</c> — which also holds renderer-only serialization — so this
/// pure <see cref="CssBox"/>-tree logic can travel with the layout code into
/// <c>Broiler.Layout</c> (see <c>Broiler.Layout/docs/roadmap.md</c>).
/// </summary>
internal static class LayoutBoxUtils
{
    public static bool ContainsInlinesOnly(CssBox box)
    {
        // CSS Flexbox §4 / CSS Grid §6: All direct children of a flex or
        // grid container become flex/grid items, which are sized using
        // shrink-to-fit (similar to inline-block).  Since Broiler does
        // not implement a true flex/grid layout engine, force the inline
        // formatting context so children are routed to FlowInlineBlock
        // for content-based sizing instead of the block path (which
        // would make them expand to full container width).
        if (box.Display is "flex" or "inline-flex" or "grid" or "inline-grid")
            return true;

        foreach (CssBox b in box.Boxes)
        {
            // CSS2.1 §9.2.4: A 'display:none' box generates no box at all, so it
            // is transparent to the inline-only test — an invisible <style>,
            // <script>, or display:none <span> between two inline-level siblings
            // must not force the container onto the block path (which wraps the
            // siblings in anonymous blocks and stacks them vertically).
            if (b.Display == CssConstants.None)
                continue;

            // CSS2.1 §9.5: Floats are out-of-flow.  Their computed display
            // is promoted to 'block' (§9.7) but they participate in the
            // inline formatting context of their parent for line-box
            // purposes.  Treat them as inline-compatible so that a parent
            // with only text + floats is laid out using CreateLineBoxes
            // instead of the block path (which would split the text around
            // the float and introduce unwanted line breaks).
            if (!b.IsInline && b.Float == CssConstants.None)
                return false;
        }

        return true;
    }

    public static CssBox GetPreviousSibling(CssBox b)
    {
        if (b.ParentBox == null)
            return null;

        int index = b.ParentBox.Boxes.IndexOf(b);

        if (index > 0)
        {
            int diff = 1;
            CssBox sib = b.ParentBox.Boxes[index - diff];

            while ((sib.Display == CssConstants.None || sib.Position == CssConstants.Absolute || sib.Position == CssConstants.Fixed) && index - diff - 1 >= 0)
                sib = b.ParentBox.Boxes[index - ++diff];

            return (sib.Display == CssConstants.None || sib.Position == CssConstants.Absolute || sib.Position == CssConstants.Fixed) ? null : sib;
        }

        return null;
    }

    /// <summary>
    /// Returns the previous in-flow sibling of <paramref name="b"/>,
    /// skipping floated, display:none, and absolutely/fixed positioned
    /// elements (CSS2.1 §9.5, §9.6.1). Relatively positioned elements
    /// remain in flow and are not skipped (CSS2.1 §9.3.1).
    /// </summary>
    public static CssBox GetPreviousInFlowSibling(CssBox b)
    {
        if (b.ParentBox == null)
            return null;

        int index = b.ParentBox.Boxes.IndexOf(b);

        for (int i = index - 1; i >= 0; i--)
        {
            var sib = b.ParentBox.Boxes[i];
            if (sib.Display == CssConstants.None
                || sib.Position == CssConstants.Absolute
                || sib.Position == CssConstants.Fixed
                || sib.Float != CssConstants.None)
                continue;
            return sib;
        }

        return null;
    }

    public static CssBox GetPreviousContainingBlockSibling(CssBox b)
    {
        var conBlock = b;
        int index = conBlock.ParentBox.Boxes.IndexOf(conBlock);

        while (conBlock.ParentBox != null && index < 1 && conBlock.Display != CssConstants.Block && conBlock.Display != CssConstants.Table && conBlock.Display != CssConstants.TableCell && conBlock.Display != CssConstants.ListItem)
        {
            conBlock = conBlock.ParentBox;
            index = conBlock.ParentBox != null ? conBlock.ParentBox.Boxes.IndexOf(conBlock) : -1;
        }

        conBlock = conBlock.ParentBox;

        if (conBlock != null && index > 0)
        {
            int diff = 1;
            CssBox sib = conBlock.Boxes[index - diff];

            while ((sib.Display == CssConstants.None || sib.Position == CssConstants.Absolute || sib.Position == CssConstants.Fixed) && index - diff - 1 >= 0)
                sib = conBlock.Boxes[index - ++diff];

            return sib.Display == CssConstants.None ? null : sib;
        }

        return null;
    }

    public static bool IsBoxHasWhitespace(CssBox box)
    {
        if (box.Words[0].IsImage || !box.Words[0].HasSpaceBefore || !box.IsInline)
            return false;

        var sib = GetPreviousContainingBlockSibling(box);
        if (sib != null && sib.IsInline)
            return true;

        return false;
    }
}
