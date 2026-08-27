using System;
using System.Collections.Generic;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// Document-wide registry of the SVG <c>&lt;filter&gt;</c> definitions that this renderer models,
/// resolvable by <c>id</c> for the current render pass. Only the <c>&lt;feFlood&gt;</c>-only filters
/// are captured — a filter whose sole primitive is an <c>feFlood</c> replaces its target with a solid
/// fill of the flood colour over the filter region, which is the subset the engine supports today.
/// <para>
/// A <c>filter="url(#id)"</c> reference may point at a <c>&lt;filter&gt;</c> defined in a different
/// <c>&lt;svg&gt;</c> subtree than the referencing shape (WPT
/// <c>css/filter-effects/svg-filter-primitive-units-user-space</c>), so resolution needs a
/// document-scoped table rather than the per-<c>&lt;svg&gt;</c> string the renderer sees. It is
/// populated once from the whole fragment tree (see <see cref="FragmentTreeBuilder.Build"/>) and read
/// by <see cref="SvgRenderer"/>. Thread-static so concurrent renders on different threads do not
/// share state.
/// </para>
/// </summary>
public static partial class SvgFilterTable
{
    /// <summary>A modelled filter: a single <c>feFlood</c> with the given resolved colour.</summary>
    public readonly record struct FloodFilter(BColor Color);

    [ThreadStatic]
    private static Dictionary<string, FloodFilter>? _table;

    [ThreadStatic]
    private static Dictionary<string, SvgColorFilter>? _colorTable;

    /// <summary>Clears the table at the start of a render pass.</summary>
    internal static void Reset()
    {
        _table = null;
        _colorTable = null;
        AmbientRenderState.MarkEstablished(AmbientRenderState.Slots.SvgTables);
    }

    /// <summary>
    /// <see cref="Reset"/>, reachable from <see cref="AmbientRenderState.Establish"/> outside this
    /// namespace. A worker thread establishes the tables by emptying them: an SVG reference on a
    /// thread that has run no fragment-tree build resolves to nothing, which is correct, whereas
    /// inheriting the previous document's table would resolve it to the wrong shape.
    /// </summary>
    public static void ResetForThread() => Reset();

    /// <summary>
    /// Records a colour-transform filter under its <c>id</c> — a chain whose primitives only change
    /// colour, so applied to a uniformly filled shape it recolours that shape (see
    /// <see cref="SvgColorFilter"/>). Later definitions win, matching document order.
    /// </summary>
    internal static void AddColorFilter(string id, SvgColorFilter filter)
    {
        if (string.IsNullOrEmpty(id))
            return;
        (_colorTable ??= new Dictionary<string, SvgColorFilter>(StringComparer.Ordinal))[id] = filter;
    }

    /// <summary>Resolves a <c>url(#id)</c> reference to a modelled colour-transform filter, if any.</summary>
    public static bool TryGetColorFilter(string id, out SvgColorFilter filter)
    {
        AmbientRenderState.AssertEstablished(AmbientRenderState.Slots.SvgTables, nameof(TryGetColorFilter));
        filter = null!;
        return _colorTable != null && id != null && _colorTable.TryGetValue(id, out filter!);
    }

    /// <summary>Records a flood filter under its <c>id</c> (later definitions win, matching document order).</summary>
    internal static void AddFlood(string id, BColor color)
    {
        if (string.IsNullOrEmpty(id))
            return;
        (_table ??= new Dictionary<string, FloodFilter>(StringComparer.Ordinal))[id] = new FloodFilter(color);
    }

    /// <summary>
    /// Resolves a <c>url(#id)</c> reference to a modelled flood filter, if any. Read by both
    /// <see cref="SvgRenderer"/> (for an SVG shape's <c>filter</c> attribute) and the HTML paint
    /// walker (for a CSS <c>filter: url(#id)</c>), so it is public across the assembly boundary.
    /// </summary>
    public static bool TryGetFlood(string id, out FloodFilter filter)
    {
        AmbientRenderState.AssertEstablished(AmbientRenderState.Slots.SvgTables, nameof(TryGetFlood));
        filter = default;
        return _table != null && id != null && _table.TryGetValue(id, out filter);
    }

    /// <summary>Extracts the <c>id</c> from a <c>url(#id)</c> CSS/SVG reference, or <c>null</c>.</summary>
    public static string? ExtractUrlReferenceId(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        var m = UrlRefRegex().Match(value);
        return m.Success ? m.Groups[1].Value : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"url\(\s*#([^\s)]+)\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex UrlRefRegex();
}
