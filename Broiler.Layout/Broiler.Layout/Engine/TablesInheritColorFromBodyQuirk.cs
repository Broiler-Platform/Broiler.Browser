using System.Collections.Generic;

namespace Broiler.Layout.Engine;

/// <summary>
/// The Quirks Mode Standard's
/// <see href="https://quirks.spec.whatwg.org/#the-tables-inherit-color-from-body-quirk">tables
/// inherit color from body quirk</see>: in quirks mode a <c>&lt;table&gt;</c> that specifies no
/// <c>color</c> of its own takes the document's <c>&lt;body&gt;</c> element's colour rather than
/// its parent's — and, when the document has no body element, the initial colour rather than
/// either.
/// </summary>
/// <remarks>
/// <para>
/// The spec models this as a special initial value, <c>quirk-inherit</c>: it behaves as
/// <c>inherit</c> for every element except a table, where it resolves to "the used value of the
/// color property of the document's body element". Both halves matter and WPT tests both — a table
/// inside <c>div { color: red }</c> under <c>body { color: green }</c> renders green
/// (<c>tables-inherit-color-from-body-quirk-001</c>), and the same table with no body in the
/// document renders the *initial* colour rather than the red it would otherwise inherit
/// (<c>-006</c>, <c>-007</c>).
/// </para>
/// <para>
/// <b>Why this is a pass over the finished box tree rather than part of inheritance.</b> The body
/// element is the document's, not an ancestor's: two of the tests move the table's subtree out of
/// <c>&lt;body&gt;</c> entirely and one of those puts it *before* body in document order
/// (<c>-003</c>). The cascade walks the tree top-down, so at the moment such a table inherits, the
/// body box has not been cascaded yet and its colour is not yet green. Reading it after the walk
/// is what makes the answer independent of where the table sits.
/// </para>
/// <para>
/// Running it at the root's layout entry rather than at the end of the cascade keeps the whole
/// quirk in this repository — the cascade walk itself lives in the <c>Broiler.HTML</c> submodule —
/// and is defensible on the spec's own terms, which define the result as a <em>used</em> value.
/// It is idempotent, so the second pass an auto-sized layout performs changes nothing.
/// </para>
/// </remarks>
internal static class TablesInheritColorFromBodyQuirk
{
    /// <summary>
    /// Applies the quirk to <paramref name="root"/>'s tree. A no-op outside quirks mode, and a
    /// single walk with no allocation when the document holds no table.
    /// </summary>
    internal static void Apply(CssBox root)
    {
        if (!root.DocumentQuirksMode)
            return;

        List<CssBox>? tables = null;
        CssBox? body = null;
        Survey(root, ref tables, ref body);

        if (tables is null)
            return;

        // "Return the used value of the color property of the document's body element" — or, with
        // no body element, the initial value, which is what a box that inherited nothing carries.
        var color = body is not null ? body.Color : CssBox.InitialColor;

        foreach (var table in tables)
        {
            if (table.ColorSpecified)
                continue;

            table.SetInheritedColor(color);
            ReinheritColor(table, color);
        }
    }

    /// <summary>
    /// Collects the table boxes and finds the body box in one walk. The body is the document's
    /// first <c>&lt;body&gt;</c> in tree order, matching what <c>document.body</c> names.
    /// </summary>
    private static void Survey(CssBox box, ref List<CssBox>? tables, ref CssBox? body)
    {
        if (box.HtmlTag is { } tag)
        {
            if (tag.Name.Equals("table", System.StringComparison.OrdinalIgnoreCase))
                (tables ??= []).Add(box);
            else if (body is null && tag.Name.Equals("body", System.StringComparison.OrdinalIgnoreCase))
                body = box;
        }

        foreach (var child in box.Boxes)
            Survey(child, ref tables, ref body);
    }

    /// <summary>
    /// Pushes <paramref name="color"/> down through the descendants that inherited the table's old
    /// one. The cascade has already run, so changing the table's colour does not re-inherit by
    /// itself — and the colour that is actually painted is a cell's, not the table's.
    /// </summary>
    /// <remarks>
    /// A descendant that specified its own colour keeps it, and its subtree already inherited from
    /// it, so that whole branch is skipped rather than walked. A nested table is left for the
    /// caller's loop: the quirk resolves per table element, so it takes the body's colour directly
    /// rather than whatever an enclosing table ended up with — the same value here, but for the
    /// right reason.
    /// </remarks>
    private static void ReinheritColor(CssBox box, string color)
    {
        foreach (var child in box.Boxes)
        {
            if (child.ColorSpecified)
                continue;

            child.SetInheritedColor(color);
            ReinheritColor(child, color);
        }
    }
}
