using System;
using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// The quirks-mode table font reset from the HTML Standard's
/// <see href="https://html.spec.whatwg.org/multipage/rendering.html#tables-2">Rendering</see>
/// section: in quirks mode a <c>&lt;table&gt;</c> does not inherit its parent's font and text
/// properties, but starts again from their initial values.
/// </summary>
/// <remarks>
/// <para>
/// The spec states it as a UA rule that applies only in quirks mode:
/// </para>
/// <code>
/// table {
///   font-weight: initial; font-style: initial; font-variant: initial;
///   font-size: initial; line-height: initial; white-space: initial; text-align: initial;
/// }
/// </code>
/// <para>
/// <b>Why it matters.</b> Legacy pages size their text with compounding percentages and nest
/// tables for layout. www.7-zip.org is the canonical example: it is a quirks-mode document whose
/// sheet says <c>BODY { font-size: 80% }</c> and <c>TD { font-size: 80% }</c>, and it nests tables
/// three deep. Without the reset each <c>TD</c> resolves its 80% against the enclosing cell, so the
/// text shrinks once per level — 12.8px, then 10.24px, then 8.19px, then 6.55px — and the page
/// renders in a font nobody can read. With it, every table restarts at <c>medium</c> and every cell
/// on the page is 12.8px, which is what browsers show. The engine was not resolving percentages
/// wrongly: its chain matched the *standards-mode* chain exactly, and only the quirk was missing.
/// </para>
/// <para>
/// <b>Why this runs during inheritance rather than as a pass over the finished tree,</b> unlike its
/// neighbour <see cref="TablesInheritColorFromBodyQuirk"/>. That quirk has to run afterwards
/// because it reads the *document's* body, which need not be an ancestor of the table and may not
/// have been cascaded yet. This one reads nothing outside the box: it only declines to copy the
/// parent's values. Doing it inline also gets the ordering right for free — the cascade is strictly
/// top-down, so a cell's <c>80%</c> is resolved after its table has been reset and therefore
/// against the reset size. A later pass could not fix that without re-resolving every descendant's
/// font size, because <see cref="CssBoxProperties.FontSize"/> resolves percentages eagerly, at the
/// moment they are set.
/// </para>
/// <para>
/// <b>Author declarations still win.</b> The spec models this as a UA-origin rule, and the cascade
/// applies element declarations after <c>InheritStyle</c> — so <c>table { font-size: 20px }</c> is
/// applied over the reset, and an explicit <c>table { font-size: inherit }</c> still reaches the
/// parent's value. Resetting during inheritance reproduces UA-origin precedence without needing a
/// quirks-mode UA stylesheet, which the renderer does not have.
/// </para>
/// <para>
/// <b>What is deliberately not reset.</b> <c>font-family</c> is absent from the spec's list and is
/// inherited normally — a table on 7-Zip still gets the body's Verdana, only at the initial size.
/// So are <c>letter-spacing</c>, <c>word-spacing</c>, <c>text-transform</c> and <c>font-stretch</c>.
/// <c>color</c> is not reset here either; in quirks mode it has its own, different rule, which is
/// <see cref="TablesInheritColorFromBodyQuirk"/>. Blink additionally resets <c>text-indent</c>,
/// which is not in the spec's list; this follows the spec.
/// </para>
/// <para>
/// The element is matched by tag name, not by <c>display: table</c>, because the spec's selector is
/// an element selector — a <c>&lt;div style="display: table"&gt;</c> inherits normally.
/// </para>
/// </remarks>
internal static class TableFontInheritanceQuirk
{
    /// <summary>
    /// Whether <paramref name="box"/> is a <c>&lt;table&gt;</c> element in a quirks-mode document,
    /// and so starts its font and text properties from the initial values.
    /// </summary>
    /// <remarks>
    /// The tag test is first and the document-mode test second on purpose: this is consulted for
    /// every box that inherits, and <see cref="CssBox.DocumentQuirksMode"/> walks to the tree root
    /// and reads thread-affine state (<see cref="AmbientRenderState"/>). Ordering it after the tag
    /// name keeps that walk to the handful of boxes that are actually tables.
    /// </remarks>
    internal static bool AppliesTo(CssBox box) =>
        box.HtmlTag is { } tag
        && tag.Name.Equals("table", StringComparison.OrdinalIgnoreCase)
        && box.DocumentQuirksMode;

    /// <summary>
    /// Replaces the values <paramref name="box"/> just inherited from its parent with the initial
    /// values of the seven properties the spec's rule names.
    /// </summary>
    internal static void Apply(CssBox box)
    {
        box.FontWeight = CssConstants.Normal;
        box.FontStyle = CssConstants.Normal;
        box.FontVariant = CssConstants.Normal;
        box.FontSize = CssConstants.Medium;
        box.LineHeight = CssConstants.Normal;
        box.WhiteSpace = CssConstants.Normal;

        // text-align's initial value is represented as the empty string throughout the box model
        // (the layout code reads "" alongside "left"/"start"), not by a keyword.
        box.TextAlign = string.Empty;
    }
}
