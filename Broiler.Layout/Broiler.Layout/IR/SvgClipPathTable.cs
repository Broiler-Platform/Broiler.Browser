using System;
using System.Collections.Generic;

namespace Broiler.Layout.IR;

/// <summary>
/// Document-wide registry of the SVG <c>&lt;clipPath&gt;</c> definitions this renderer models,
/// resolvable by <c>id</c> for the current render pass — the shapes a CSS
/// <c>clip-path: url(#id)</c> can point at.
/// <para>
/// The reference almost never sits in the same subtree as the definition: WPT
/// <c>css/css-masking/clip-path/clip-path-element-*</c> clips an HTML <c>&lt;div&gt;</c> with a
/// <c>&lt;clipPath&gt;</c> declared in a sibling <c>&lt;svg&gt;</c>. Resolution therefore needs a
/// document-scoped table rather than the per-<c>&lt;svg&gt;</c> markup string the SVG renderer sees.
/// Populated once from the whole fragment tree (see <see cref="FragmentTreeBuilder.Build"/>) and read
/// by the HTML paint walker. Thread-static, so concurrent renders on different threads do not share
/// state — the same arrangement <see cref="SvgFilterTable"/> uses, for the same reasons.
/// </para>
/// </summary>
public static class SvgClipPathTable
{
    /// <summary>The <c>&lt;clipPath&gt;</c> child shapes that map onto a clip the rasterizer can
    /// apply. A <c>&lt;path&gt;</c> child is deliberately absent: its geometry is not modelled, and
    /// a definition holding one resolves to nothing rather than to a wrong shape.</summary>
    public enum ClipShapeKind
    {
        Rect,
        Circle,
        Ellipse,
        Polygon,
    }

    /// <summary>
    /// One modelled <c>&lt;clipPath&gt;</c>: its first shape child, held as raw SVG attributes
    /// because resolving them needs the referencing element's box and the viewport, neither of which
    /// exists yet at collection time.
    /// </summary>
    /// <param name="Kind">Which shape element the attributes came from.</param>
    /// <param name="Attributes">The shape element's attributes, keyed case-insensitively.</param>
    /// <param name="ObjectBoundingBox">
    /// <see langword="true"/> for <c>clipPathUnits="objectBoundingBox"</c>, where every coordinate is
    /// a fraction of the referencing element's bounding box; <see langword="false"/> for the default
    /// <c>userSpaceOnUse</c>, where they are lengths in the referencing element's user space and
    /// percentages resolve against the viewport.
    /// </param>
    public sealed record ClipShape(
        ClipShapeKind Kind,
        IReadOnlyDictionary<string, string> Attributes,
        bool ObjectBoundingBox);

    [ThreadStatic]
    private static Dictionary<string, ClipShape>? _table;

    /// <summary>Clears the table at the start of a render pass.</summary>
    internal static void Reset()
    {
        _table = null;
        AmbientRenderState.MarkEstablished(AmbientRenderState.Slots.SvgTables);
    }

    /// <summary>
    /// <see cref="Reset"/>, reachable from <see cref="AmbientRenderState.Establish"/> outside this
    /// namespace. See <see cref="SvgFilterTable.ResetForThread"/> for why emptying is the right way
    /// for a worker thread to establish the tables.
    /// </summary>
    public static void ResetForThread() => Reset();

    /// <summary>Records a clip path under its <c>id</c> (later definitions win, matching document order).</summary>
    internal static void Add(string id, ClipShape shape)
    {
        if (string.IsNullOrEmpty(id))
            return;
        (_table ??= new Dictionary<string, ClipShape>(StringComparer.Ordinal))[id] = shape;
    }

    /// <summary>
    /// Resolves a <c>url(#id)</c> reference to a modelled clip path, if any. Read by the HTML paint
    /// walker, so it is public across the assembly boundary.
    /// </summary>
    public static bool TryGet(string? id, out ClipShape shape)
    {
        AmbientRenderState.AssertEstablished(AmbientRenderState.Slots.SvgTables, nameof(TryGet));
        shape = null!;
        return _table != null && id != null && _table.TryGetValue(id, out shape!);
    }
}
