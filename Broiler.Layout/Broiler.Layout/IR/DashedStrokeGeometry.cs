using System;
using System.Collections.Generic;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// Splits a straight stroke into the sub-segments a dashed or dotted
/// <see cref="DashStyle"/> actually paints.
/// </summary>
/// <remarks>
/// <para>
/// The raster canvas strokes solid lines only. Anything else used to fall through to the
/// graphics compatibility seam, which on a host with no OS backend resolves to an inert stub
/// — so a <c>border-style: dashed</c> or <c>dotted</c> edge painted nothing at all, while
/// <c>solid</c>, <c>double</c>, and <c>groove</c> painted normally. Reducing the dash to a
/// list of solid runs keeps it on the raster path and removes the only user-visible
/// consequence of that seam being a stub.
/// </para>
/// <para>
/// The pattern is expressed in multiples of the stroke width, which is how the CSS border
/// styles behave across engines: a dash scales with the border it belongs to rather than
/// staying a fixed length. <see cref="DashStyle.Dash"/> is a 3:3 on/off run and
/// <see cref="DashStyle.Dot"/> a 1:1 run — square dots, not round ones, matching the way the
/// raster canvas strokes. <see cref="DashStyle.DashDot"/> and
/// <see cref="DashStyle.DashDotDot"/> follow the same convention as the GDI+ enum they are
/// named after. These are approximations of a browser's exact dash placement, chosen so a
/// dashed edge reads as dashed at typical border widths; matching a reference engine
/// pixel-for-pixel is a separate exercise that needs reference images, not a constant.
/// </para>
/// <para>
/// The run always starts painted and is clipped to the segment's length, so a stroke shorter
/// than one dash paints a single shortened run rather than disappearing.
/// </para>
/// </remarks>
public static class DashedStrokeGeometry
{
    /// <summary>A painted run of a dashed stroke, in the caller's coordinate space.</summary>
    public readonly record struct Segment(float X1, float Y1, float X2, float Y2);

    /// <summary>
    /// The on/off run lengths for <paramref name="style"/> at <paramref name="strokeWidth"/>,
    /// or an empty span when the style paints one solid run.
    /// </summary>
    public static IReadOnlyList<float> PatternFor(DashStyle style, float strokeWidth)
    {
        var unit = Math.Max(strokeWidth, 1f);
        return style switch
        {
            DashStyle.Dash => [3f * unit, 3f * unit],
            DashStyle.Dot => [unit, unit],
            DashStyle.DashDot => [3f * unit, unit, unit, unit],
            DashStyle.DashDotDot => [3f * unit, unit, unit, unit, unit, unit],
            _ => [],
        };
    }

    /// <summary>
    /// Enumerates the painted runs of the stroke from
    /// (<paramref name="x1"/>, <paramref name="y1"/>) to (<paramref name="x2"/>,
    /// <paramref name="y2"/>). An empty or all-zero <paramref name="pattern"/> yields the whole
    /// stroke as one run, so a caller can pass the pattern through without branching on solid.
    /// </summary>
    public static IReadOnlyList<Segment> Segments(
        float x1,
        float y1,
        float x2,
        float y2,
        IReadOnlyList<float> pattern)
    {
        var whole = new Segment(x1, y1, x2, y2);

        if (pattern is null || pattern.Count == 0)
            return [whole];

        var total = 0f;
        foreach (var run in pattern)
        {
            if (run < 0f || float.IsNaN(run) || float.IsInfinity(run))
                return [whole];

            total += run;
        }

        if (total <= 0f)
            return [whole];

        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0f || float.IsNaN(length) || float.IsInfinity(length))
            return [];

        // A stroke shorter than the first painted run is one shortened dash, not nothing.
        if (length <= pattern[0])
            return [whole];

        var ux = dx / length;
        var uy = dy / length;

        var segments = new List<Segment>();
        var travelled = 0f;
        var index = 0;
        var painted = true;

        while (travelled < length)
        {
            var run = pattern[index];
            var end = MathF.Min(travelled + run, length);

            if (painted && end > travelled)
            {
                segments.Add(new Segment(
                    x1 + (ux * travelled),
                    y1 + (uy * travelled),
                    x1 + (ux * end),
                    y1 + (uy * end)));
            }

            // A zero-length run would not advance; skipping it still flips painted state,
            // which is what a pattern like [w, 0] means — a solid line expressed as a dash.
            travelled = end;
            index = (index + 1) % pattern.Count;
            painted = !painted;
        }

        return segments;
    }
}
