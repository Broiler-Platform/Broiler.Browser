using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Broiler.CSS;

namespace Broiler.Layout.IR;

/// <summary>
/// CSS Values 4 §6.9 <c>&lt;position&gt;</c>: where an object of a known size sits inside an area of
/// a known size. The grammar is shared by <c>background-position</c> and <c>object-position</c>, and
/// this resolves both.
/// </summary>
/// <remarks>
/// <para>
/// The whole grammar is four forms, and the last one is what engines commonly skip:
/// </para>
/// <code>
///   [ left | center | right | top | bottom | &lt;length-percentage&gt; ]
/// | [ left | center | right ] &amp;&amp; [ top | center | bottom ]
/// | [ left | center | right | &lt;length-percentage&gt; ] [ top | center | bottom | &lt;length-percentage&gt; ]
/// | [ center | [ left | right ] &lt;length-percentage&gt;? ] &amp;&amp;
///   [ center | [ top | bottom ] &lt;length-percentage&gt;? ]
/// </code>
/// <para>
/// The last form names the edge an offset is measured <em>from</em>, so <c>bottom 1px right 2px</c>
/// is 1px up from the bottom edge and 2px left of the right one — an offset the two-component form
/// has no way to express, and one that every <c>css-images/object-fit-*</c> reference uses.
/// Component count is what tells the two readings apart, and the spec's own disambiguation:
/// <c>right 25%</c> is two components, so it is <c>x: right, y: 25%</c>, while
/// <c>center right 25%</c> is three and so is 25% of the free space in from the right edge.
/// </para>
/// <para>
/// A percentage resolves against the <em>free space</em> (area − object), not the area, so
/// <c>100%</c> aligns the object's far edge with the area's rather than pushing it a full
/// area-width out. That is CSS 2.1 §14.2.1's rule for <c>background-position</c>, and CSS Images 3
/// §5.6 restates it for <c>object-position</c>.
/// </para>
/// </remarks>
public static class CssPositionValue
{
    /// <summary>
    /// Resolves <paramref name="position"/> to the object's offset from the area's top-left corner.
    /// </summary>
    /// <param name="position">The declared <c>&lt;position&gt;</c>, e.g. <c>top 25% left 25%</c>.</param>
    /// <param name="areaSize">The positioning area — a content box, or a background positioning area.</param>
    /// <param name="objectSize">The object being placed — a concrete object size, or a tile.</param>
    /// <param name="emSize">Font size in px, for an offset stated in font-relative units.</param>
    /// <returns>
    /// The offset in px, which may be negative or exceed the free space when the declaration says so.
    /// An empty declaration resolves to the origin, which is what dropping it means.
    /// </returns>
    public static PointF Resolve(string? position, SizeF areaSize, SizeF objectSize, double emSize)
    {
        if (string.IsNullOrWhiteSpace(position))
            return PointF.Empty;

        var tokens = Tokenize(position);
        if (tokens.Count == 0)
            return PointF.Empty;

        float freeX = areaSize.Width - objectSize.Width;
        float freeY = areaSize.Height - objectSize.Height;

        return tokens.Count >= 3
            ? ResolveEdgeOffsets(tokens, freeX, freeY, emSize)
            : ResolveShort(tokens, freeX, freeY, emSize);
    }

    /// <summary>
    /// Splits the declaration on whitespace, but not inside parentheses — the component count is
    /// what tells the two- and three-component forms apart, so a <c>calc(50% - 10px)</c> counted as
    /// three tokens would be read as an edge-offset form and resolve to something unrelated.
    /// </summary>
    private static List<string> Tokenize(string position)
    {
        var tokens = new List<string>(4);
        int depth = 0, start = -1;

        for (int i = 0; i < position.Length; i++)
        {
            char c = position[i];

            if (c == '(')
                depth++;
            else if (c == ')' && depth > 0)
                depth--;

            if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (start >= 0)
                {
                    tokens.Add(position[start..i]);
                    start = -1;
                }

                continue;
            }

            if (start < 0)
                start = i;
        }

        if (start >= 0)
            tokens.Add(position[start..]);

        return tokens;
    }

    /// <summary>
    /// The one- and two-component forms: each component is a keyword or a length, the horizontal one
    /// first unless a pair of keywords states otherwise (<c>top right</c> is as valid as
    /// <c>right top</c>). An omitted component is <c>center</c>.
    /// </summary>
    private static PointF ResolveShort(List<string> tokens, float freeX, float freeY, double emSize)
    {
        string? x, y;

        if (tokens.Count == 1)
        {
            // A lone vertical keyword sets the vertical axis; everything else — a horizontal keyword,
            // `center`, or a length — sets the horizontal one. The other axis centres.
            (x, y) = IsVerticalKeyword(tokens[0]) ? (null, tokens[0]) : (tokens[0], (string?)null);
        }
        else if (IsVerticalKeyword(tokens[0]) || IsHorizontalKeyword(tokens[1]))
        {
            // A keyword pair may be written either way round. A length is only ever positional, so a
            // swap is considered only when a keyword puts the axes beyond doubt.
            (x, y) = (tokens[1], tokens[0]);
        }
        else
        {
            (x, y) = (tokens[0], tokens[1]);
        }

        return new PointF(
            ResolveComponent(x, fromStartEdge: true, freeX, emSize),
            ResolveComponent(y, fromStartEdge: true, freeY, emSize));
    }

    /// <summary>
    /// The three- and four-component form: a sequence of groups, each an edge keyword with an
    /// optional offset, or a bare <c>center</c>. Each edge keyword fixes its own axis, so an axis
    /// left without one centres — which is exactly what a <c>center</c> in the other group asks for,
    /// and is why <c>center right 25%</c> and <c>right 25% center</c> mean the same thing.
    /// </summary>
    private static PointF ResolveEdgeOffsets(List<string> tokens, float freeX, float freeY, double emSize)
    {
        string? xEdge = null, xOffset = null, yEdge = null, yOffset = null;

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (!IsEdgeKeyword(token))
                continue;

            // The offset is the next token when there is one and it is not itself a keyword:
            // `left top` is two bare edges, `left 25% top 1px` two edge-and-offset groups.
            string? offset = i + 1 < tokens.Count && !IsKeyword(tokens[i + 1]) ? tokens[++i] : null;

            if (IsHorizontalKeyword(token))
                (xEdge, xOffset) = (token, offset);
            else
                (yEdge, yOffset) = (token, offset);
        }

        return new PointF(
            ResolveAxis(xEdge, xOffset, freeX, emSize),
            ResolveAxis(yEdge, yOffset, freeY, emSize));
    }

    /// <summary>
    /// One axis of the edge-offset form: no edge centres, and an edge without an offset is that edge
    /// flush (<c>right</c> alone is <c>right 0</c>).
    /// </summary>
    private static float ResolveAxis(string? edge, string? offset, float freeSpace, double emSize)
    {
        if (edge == null)
            return freeSpace / 2f;

        return ResolveComponent(offset ?? "0", fromStartEdge: !IsFarEdge(edge), freeSpace, emSize);
    }

    /// <summary>
    /// One component: a keyword, or an offset measured from <paramref name="fromStartEdge"/>'s side
    /// of the free space. A null component centres, which is the initial value on both axes.
    /// </summary>
    private static float ResolveComponent(string? value, bool fromStartEdge, float freeSpace, double emSize)
    {
        if (string.IsNullOrEmpty(value))
            return freeSpace / 2f;

        if (value.Equals("left", StringComparison.OrdinalIgnoreCase)
            || value.Equals("top", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (IsFarEdge(value))
            return freeSpace;

        if (value.Equals("center", StringComparison.OrdinalIgnoreCase))
            return freeSpace / 2f;

        // A percentage is a fraction of the free space; every other unit is an absolute offset. The
        // length parser reads both, given the free space as its 100% basis.
        double offset = CssLengthParser.ParseLength(value, freeSpace, emSize, defaultUnit: null);

        // A unitless number is not a valid <length-percentage> and the parser answers 0 for it.
        // Reading it as px is a tolerance the background-position code carried, kept here so
        // consolidating the two readings changes only what it set out to change.
        if (offset == 0
            && !value.Equals("0", StringComparison.Ordinal)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
        {
            offset = raw;
        }

        if (double.IsNaN(offset) || double.IsInfinity(offset))
            return fromStartEdge ? 0 : freeSpace;

        return fromStartEdge ? (float)offset : freeSpace - (float)offset;
    }

    /// <summary>The edges an offset is measured backwards from — <c>right</c> and <c>bottom</c>.</summary>
    private static bool IsFarEdge(string value) =>
        value.Equals("right", StringComparison.OrdinalIgnoreCase)
        || value.Equals("bottom", StringComparison.OrdinalIgnoreCase);

    private static bool IsHorizontalKeyword(string value) =>
        value.Equals("left", StringComparison.OrdinalIgnoreCase)
        || value.Equals("right", StringComparison.OrdinalIgnoreCase);

    private static bool IsVerticalKeyword(string value) =>
        value.Equals("top", StringComparison.OrdinalIgnoreCase)
        || value.Equals("bottom", StringComparison.OrdinalIgnoreCase);

    /// <summary>An edge keyword — the four that can carry an offset, excluding <c>center</c>.</summary>
    private static bool IsEdgeKeyword(string value) =>
        IsHorizontalKeyword(value) || IsVerticalKeyword(value);

    private static bool IsKeyword(string value) =>
        IsEdgeKeyword(value) || value.Equals("center", StringComparison.OrdinalIgnoreCase);
}
