using System;
using System.Drawing;
using System.Globalization;

namespace Broiler.Layout.IR;

/// <summary>
/// CSS Transforms 1 §8: resolves a <c>transform-origin</c> declaration to the point, in the same
/// coordinate space as the reference box, that the element's <c>transform</c> is applied about.
/// </summary>
/// <remarks>
/// <para>
/// One resolver for three callers that each had their own reading of the grammar, and did not
/// agree: the SVG renderer's <c>transform-box</c> handling, the script bridge's transform chain
/// (what <c>getBoundingClientRect</c> reports), and the paint walker (what actually reaches the
/// canvas). The last of those did not read the property at all — it applied every CSS
/// <c>transform</c> about the box centre — so a page's painted output and the geometry its own
/// script measured disagreed about where a transformed element is:
/// <c>transform-origin: 0 0; transform: scale(0.5)</c> on a 100×100 box reported a rect at
/// (0, 0) and painted it at (25, 25).
/// </para>
/// <para>
/// The grammar is fussier than "split on whitespace and take two components", which is what the
/// bridge did. A vertical keyword may lead — <c>top left</c> is the same point as
/// <c>left top</c> — but only when the other half is a keyword too, because a
/// <c>&lt;length-percentage&gt;</c> may not follow one. So <c>top 100%</c> is not a re-ordered
/// <c>100% top</c>; it is invalid, and an invalid declaration is dropped whole and takes the
/// initial value. The twelve WPT <c>css-transforms/transform-origin/svg-origin-relative-length-invalid-*</c>
/// cases are built to catch exactly that distinction.
/// </para>
/// <para>
/// The <b>initial</b> value differs by caller, which is the one thing this cannot decide for
/// itself. A CSS box's is <c>50% 50%</c> — its centre. An SVG element has no CSS layout box, and
/// §8 gives one of those a used value of <c>0 0</c>: the reference box's own corner. Passing the
/// wrong one is not a rounding difference — it is the element in a different place — so the caller
/// states it.
/// </para>
/// </remarks>
internal static class CssTransformOrigin
{
    /// <summary>
    /// The transform origin <paramref name="value"/> names inside <paramref name="box"/>.
    /// </summary>
    /// <param name="value">The declaration; <see langword="null"/>, empty or invalid takes the initial value.</param>
    /// <param name="box">The reference box, in the coordinate space the result is wanted in.</param>
    /// <param name="initialIsBoxCorner">
    /// <see langword="true"/> for an element with no CSS layout box, whose initial and
    /// invalid-fallback origin is the box's corner (<c>0 0</c>); <see langword="false"/> for a CSS
    /// box, whose initial is its centre (<c>50% 50%</c>).
    /// </param>
    internal static PointF Resolve(string? value, RectangleF box, bool initialIsBoxCorner)
    {
        float centreX = box.X + box.Width / 2f;
        float centreY = box.Y + box.Height / 2f;

        var initial = initialIsBoxCorner ? new PointF(box.X, box.Y) : new PointF(centreX, centreY);

        if (string.IsNullOrWhiteSpace(value))
            return initial;

        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return initial;

        if (parts.Length == 1)
        {
            // One value sets that axis and centres the other. `top`/`bottom` name the vertical one,
            // so a lone one of those moves y rather than x.
            if (IsVerticalKeyword(parts[0]))
                return new PointF(centreX, ResolveComponent(parts[0], box.Y, box.Height, horizontal: false, centreY));

            return IsHorizontalCompatible(parts[0])
                ? new PointF(ResolveComponent(parts[0], box.X, box.Width, horizontal: true, centreX), centreY)
                : initial;
        }

        // The pair may be written the other way round — but only when *both* halves are keywords.
        string first = parts[0], second = parts[1];
        if (IsVerticalKeyword(first))
        {
            if (!IsHorizontalKeyword(second))
                return initial;

            (first, second) = (second, first);
        }

        if (!IsHorizontalCompatible(first) || !IsVerticalCompatible(second))
            return initial;

        return new PointF(
            ResolveComponent(first, box.X, box.Width, horizontal: true, centreX),
            ResolveComponent(second, box.Y, box.Height, horizontal: false, centreY));
    }

    /// <summary>A keyword naming a position on the horizontal axis.</summary>
    private static bool IsHorizontalKeyword(string token) =>
        token.Equals("left", StringComparison.OrdinalIgnoreCase)
        || token.Equals("right", StringComparison.OrdinalIgnoreCase)
        || token.Equals("center", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the token may occupy the horizontal slot: a keyword for it, or a length.</summary>
    private static bool IsHorizontalCompatible(string token) =>
        IsHorizontalKeyword(token) || IsLengthOrPercentage(token);

    /// <summary>Whether the token may occupy the vertical slot: a keyword for it, or a length.</summary>
    private static bool IsVerticalCompatible(string token) =>
        IsVerticalKeyword(token)
        || token.Equals("center", StringComparison.OrdinalIgnoreCase)
        || IsLengthOrPercentage(token);

    private static bool IsVerticalKeyword(string token) =>
        token.Equals("top", StringComparison.OrdinalIgnoreCase)
        || token.Equals("bottom", StringComparison.OrdinalIgnoreCase);

    private static bool IsLengthOrPercentage(string token)
    {
        var span = token.AsSpan().Trim();
        if (span.Length == 0)
            return false;

        if (span[^1] == '%')
            span = span[..^1];

        return float.TryParse(
            span.ToString().Replace("px", string.Empty), NumberStyles.Float,
            CultureInfo.InvariantCulture, out _);
    }

    private static float ResolveComponent(
        string token, float origin, float extent, bool horizontal, float fallback)
    {
        if (token.Equals("center", StringComparison.OrdinalIgnoreCase))
            return origin + extent / 2f;

        if (horizontal)
        {
            if (token.Equals("left", StringComparison.OrdinalIgnoreCase))
                return origin;
            if (token.Equals("right", StringComparison.OrdinalIgnoreCase))
                return origin + extent;
        }
        else
        {
            if (token.Equals("top", StringComparison.OrdinalIgnoreCase))
                return origin;
            if (token.Equals("bottom", StringComparison.OrdinalIgnoreCase))
                return origin + extent;
        }

        var span = token.AsSpan().Trim();
        if (span.Length > 0 && span[^1] == '%')
        {
            return float.TryParse(
                span[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                ? origin + extent * percent / 100f
                : fallback;
        }

        // A bare number, or one carrying a unit treated as pixels. Measured from the box's corner.
        return float.TryParse(
            token.Replace("px", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture,
            out float length)
            ? origin + length
            : fallback;
    }
}
