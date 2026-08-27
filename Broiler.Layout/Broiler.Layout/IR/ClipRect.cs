using System;
using System.Globalization;
using Broiler.CSS;
using Broiler.Layout.Engine;

namespace Broiler.Layout.IR;

/// <summary>
/// CSS 2.1 §11.1.2 <c>clip</c>: the legacy rectangular clip on an absolutely positioned element.
/// </summary>
/// <remarks>
/// <para>
/// <c>clip</c> and <c>clip-path: inset()</c> name the same operation — a rectangle the element and
/// everything inside it is clipped to, measured on the border box — so this resolves the one into
/// the other rather than adding a second clip to the paint. The projection happens once, in
/// <see cref="ComputedStyleBuilder"/>, where the element's used border box is already known; every
/// consumer downstream sees an ordinary <c>clip-path</c> and needs to know nothing about the legacy
/// spelling. CSS Masking 1 §7 makes that ordering right as well as convenient: a real
/// <c>clip-path</c> supersedes <c>clip</c>, so it is only consulted when <c>clip-path</c> is
/// <c>none</c>.
/// </para>
/// <para>
/// The geometry is stated from two edges, not four: <c>top</c> and <c>bottom</c> are offsets down
/// from the border box's top edge, <c>right</c> and <c>left</c> offsets right from its left edge —
/// so <c>rect(96px, 96px, 96px, 96px)</c> on a 96×96 box is an <em>empty</em> clip, not a full one,
/// and the twenty-odd <c>css/CSS2/visufx/clip-*</c> tests that state one under a different unit all
/// mean "nothing should be visible".
/// </para>
/// </remarks>
internal static class ClipRect
{
    /// <summary>
    /// The <c>clip-path</c> a box paints with: its own when it states one, otherwise the projection
    /// of its <c>clip</c>, otherwise its own unchanged.
    /// </summary>
    internal static string EffectiveClipPath(CssBoxProperties box)
    {
        var clipPath = box.ClipPath;
        if (!string.IsNullOrWhiteSpace(clipPath)
            && !clipPath.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return clipPath;
        }

        return TryProjectAsInset(box, out var projected) ? projected : clipPath;
    }

    /// <summary>
    /// Projects <paramref name="box"/>'s <c>clip</c> onto the equivalent
    /// <c>clip-path: inset(…)</c>, or returns <c>false</c> when it declares none, is not on a box
    /// <c>clip</c> applies to, or is not a valid <c>rect()</c>.
    /// </summary>
    internal static bool TryProjectAsInset(CssBoxProperties box, out string inset)
    {
        inset = string.Empty;

        // "Applies to: absolutely positioned elements" — `clip` on anything else is dropped, not
        // silently honoured, or a `clip` inherited into a static descendant would clip it too.
        var position = box.Position;
        if (!position.Equals("absolute", StringComparison.OrdinalIgnoreCase)
            && !position.Equals("fixed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The em height comes from the box's resolved font, so it is asked for only once a side
        // that needs it is in hand: this runs for every box the style phase builds, and all but a
        // handful say `auto`.
        if (!TryParseRect(box.Clip, box.GetEmHeight, out var top, out var right, out var bottom, out var left))
            return false;

        var (width, height) = BorderBoxSize(box);

        // `+ 0` collapses the negative zero `rect(-0px, …)` parses to, which would otherwise be
        // written out as `-0px`: the same geometry, spelled in a way no other length here is.
        double insetTop = (top ?? 0) + 0;
        double insetLeft = (left ?? 0) + 0;
        double insetRight = (right is { } r ? width - r : 0) + 0;
        double insetBottom = (bottom is { } b ? height - b : 0) + 0;

        inset = string.Create(
            CultureInfo.InvariantCulture,
            $"inset({insetTop:0.###}px {insetRight:0.###}px {insetBottom:0.###}px {insetLeft:0.###}px)");
        return true;
    }

    /// <summary>
    /// Parses <c>rect(&lt;top&gt;, &lt;right&gt;, &lt;bottom&gt;, &lt;left&gt;)</c>, each side a
    /// length or <c>auto</c> (returned as <c>null</c>). Commas are optional and may be mixed with
    /// spaces — CSS 2.1's grammar says commas, every engine has always taken either, and
    /// <c>clip-016</c>…<c>-021</c> exist to say so, one per mixture.
    /// </summary>
    private static bool TryParseRect(
        string? clip, Func<double> emHeight,
        out double? top, out double? right, out double? bottom, out double? left)
    {
        top = right = bottom = left = null;

        if (string.IsNullOrWhiteSpace(clip))
            return false;

        var value = clip.Trim();
        if (!value.StartsWith("rect(", StringComparison.OrdinalIgnoreCase)
            || !value.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var sides = value[5..^1].Split([',', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (sides.Length != 4)
            return false;

        var resolved = new double?[4];
        for (int i = 0; i < 4; i++)
        {
            if (!TryParseSide(sides[i], emHeight, out resolved[i]))
                return false;
        }

        (top, right, bottom, left) = (resolved[0], resolved[1], resolved[2], resolved[3]);
        return true;
    }

    /// <summary>One side of a <c>rect()</c>: a length, or <c>auto</c> — which is <c>null</c> here.</summary>
    /// <remarks>
    /// Percentages are not lengths in this grammar, and a declaration carrying one is invalid rather
    /// than resolved against something — so it is rejected here and the element is left unclipped,
    /// which is what dropping the declaration means.
    /// </remarks>
    private static bool TryParseSide(string side, Func<double> emHeight, out double? length)
    {
        length = null;

        if (side.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        // A `<length>` starts with its number. The length parser is forgiving — it falls back to
        // zero for a token it cannot read — so the grammar is checked here instead, or
        // `rect(0, 0, 0, red)` would be a valid clip of zero.
        if (side.Length == 0 || (!char.IsAsciiDigit(side[0]) && side[0] is not ('+' or '-' or '.')))
            return false;

        if (side.Contains('%', StringComparison.Ordinal))
            return false;

        double parsed = CssLengthParser.ParseLength(side, 0, emHeight());
        if (double.IsNaN(parsed) || double.IsInfinity(parsed))
            return false;

        length = parsed;
        return true;
    }

    /// <summary>
    /// The box's used border-box size, by the same reading <c>FragmentTreeBuilder</c> takes: a
    /// block-level box never has its <c>Height</c> set during layout and an auto-width absolutely
    /// positioned one can still carry a NaN width, so both fall back to the laid-out edges.
    /// </summary>
    private static (double Width, double Height) BorderBoxSize(CssBoxProperties box)
    {
        double width = box.Size.Width;
        if (double.IsNaN(width) || width <= 0)
            width = box.ActualRight - box.Location.X;

        double height = box.Size.Height;
        double laidOut = box.ActualBottom - box.Location.Y;
        if (double.IsNaN(height) || laidOut > height)
            height = laidOut;

        return (Math.Max(0, width), Math.Max(0, height));
    }
}
