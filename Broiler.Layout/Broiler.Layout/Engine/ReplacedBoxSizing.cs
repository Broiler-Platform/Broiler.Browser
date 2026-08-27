namespace Broiler.Layout.Engine;

/// <summary>
/// CSS2.1 §10.4/§10.7: the used width and height of a box once
/// <c>min-width</c>/<c>max-width</c>/<c>min-height</c>/<c>max-height</c> are applied to a
/// tentative size.
/// <para>For an ordinary box the two axes are independent — each is clamped on its own, with
/// <c>min-*</c> winning over <c>max-*</c>. For a <b>replaced</b> box with an intrinsic ratio the
/// axes are coupled: an axis the author left <c>auto</c> was derived from the other one through the
/// ratio, so clamping the stated axis has to re-derive it. When <em>both</em> axes are auto and both
/// constraints are violated, neither clamp can simply win — §10.4 settles it with a table that
/// compares how hard each constraint bites (<c>max-width/w</c> against <c>max-height/h</c>) and
/// keeps the shape square. That table is what
/// WPT <c>css-sizing/replaced-max-size-saturation</c> asserts: an 8000×8000 canvas under
/// <c>max-width: 120px; max-height: 100px</c> is <b>100×100</b>, not 120×100.</para>
/// </summary>
/// <remarks>
/// Shared by the two paths that size a replaced box, so they cannot drift: the inline replaced word
/// (<see cref="CssLayoutEngine.MeasureImageSize"/>, an <c>&lt;img&gt;</c>) and the atomic
/// inline-level box (<see cref="CssLayoutEngine"/>'s inline flow, which is where a
/// <c>&lt;canvas&gt;</c> and every other <c>inline-block</c> is sized). Ratio-less boxes get the
/// per-axis clamp, which is also the whole of the correct behaviour for a non-replaced
/// <c>inline-block</c>.
/// </remarks>
internal static class ReplacedBoxSizing
{
    /// <summary>The min/max bounds of one axis, in the same box-sizing frame as the size clamped.</summary>
    /// <param name="Min">Lower bound; <c>0</c> when <c>min-*</c> is at its initial value.</param>
    /// <param name="Max">Upper bound; <see cref="double.PositiveInfinity"/> for <c>max-*: none</c>
    /// (and for a percentage <c>max-*</c> that §10.7 turns into <c>none</c> against an indefinite
    /// containing block).</param>
    internal readonly record struct Bounds(double Min, double Max)
    {
        /// <summary>No constraint on this axis.</summary>
        public static Bounds Unconstrained => new(0, double.PositiveInfinity);

        /// <summary>Whether either bound would actually move a size.</summary>
        public bool IsUnconstrained => Min <= 0 && double.IsPositiveInfinity(Max);

        /// <summary>CSS2.1 §10.4: clamp to <c>[Min, Max]</c>, with <c>min-*</c> winning when it
        /// exceeds <c>max-*</c>.</summary>
        public double Clamp(double value)
        {
            if (value > Max) value = Max;
            if (value < Min) value = Min;
            return value;
        }
    }

    /// <summary>
    /// Applies the min/max constraints to a tentative <paramref name="width"/>/<paramref name="height"/>.
    /// </summary>
    /// <param name="widthIsAuto">Whether the tentative width was <em>derived</em> (from the intrinsic
    /// size or through the ratio) rather than stated by the author. A stated axis is never re-derived.</param>
    /// <param name="heightIsAuto">The same for the block axis.</param>
    /// <param name="ratio">The used width ÷ height ratio, or <c>0</c> when the box has none — in
    /// which case the axes are clamped independently.</param>
    internal static void ApplyMinMax(
        ref double width, ref double height,
        bool widthIsAuto, bool heightIsAuto, double ratio,
        Bounds widthBounds, Bounds heightBounds)
    {
        if (widthBounds.IsUnconstrained && heightBounds.IsUnconstrained)
            return;

        bool hasRatio = ratio > 0 && !double.IsInfinity(ratio) && width > 0 && height > 0;

        if (!hasRatio)
        {
            width = widthBounds.Clamp(width);
            height = heightBounds.Clamp(height);
            return;
        }

        // CSS2.1 §10.4, "for replaced elements with an intrinsic ratio and both 'width' and
        // 'height' specified as 'auto'": the constraint-violation table. Only this case needs it —
        // with one axis stated, that axis is clamped on its own terms and the auto one simply
        // follows it through the ratio (below), which is what Chromium does for
        // `height: 1000px; max-width: 100px` (100×1000: the height is stated, so max-width does
        // not shorten it).
        if (widthIsAuto && heightIsAuto)
        {
            ApplyConstraintTable(ref width, ref height, ratio, widthBounds, heightBounds);
            return;
        }

        // One axis is stated: clamp it, re-derive the auto one from the clamped value through the
        // ratio, then clamp that one too (it cannot push back on the stated axis).
        if (!widthIsAuto)
        {
            width = widthBounds.Clamp(width);
            if (heightIsAuto)
                height = width / ratio;
        }

        if (!heightIsAuto)
        {
            height = heightBounds.Clamp(height);
            if (widthIsAuto)
                width = height * ratio;
        }

        if (widthIsAuto)
            width = widthBounds.Clamp(width);
        if (heightIsAuto)
            height = heightBounds.Clamp(height);
    }

    /// <summary>
    /// CSS2.1 §10.4's constraint-violation table, verbatim: pick the resolved pair for whichever
    /// combination of the four constraints the tentative size violates.
    /// </summary>
    private static void ApplyConstraintTable(
        ref double width, ref double height, double ratio,
        Bounds widthBounds, Bounds heightBounds)
    {
        double w = width, h = height;
        double minW = widthBounds.Min, maxW = widthBounds.Max;
        double minH = heightBounds.Min, maxH = heightBounds.Max;

        bool overW = w > maxW, underW = w < minW;
        bool overH = h > maxH, underH = h < minH;

        // h/w and w/h are the intrinsic ratio the tentative size already carries; using it rather
        // than the measured quotient keeps a degenerate tentative size (a zero axis is filtered out
        // by the caller's `hasRatio`) from steering the result.
        double hOverW = 1 / ratio;
        double wOverH = ratio;

        if (overW && overH)
        {
            // Both upper bounds violated: the one that has to shrink the box *more* wins, and the
            // other axis follows through the ratio (floored by its own min).
            if (maxW / w <= maxH / h)
            {
                width = maxW;
                height = Math.Max(minH, maxW * hOverW);
            }
            else
            {
                width = Math.Max(minW, maxH * wOverH);
                height = maxH;
            }
        }
        else if (underW && underH)
        {
            if (minW / w <= minH / h)
            {
                width = Math.Min(maxW, minH * wOverH);
                height = minH;
            }
            else
            {
                width = minW;
                height = Math.Min(maxH, minW * hOverW);
            }
        }
        else if (underW && overH)
        {
            width = minW;
            height = maxH;
        }
        else if (overW && underH)
        {
            width = maxW;
            height = minH;
        }
        else if (overW)
        {
            width = maxW;
            height = Math.Max(maxW * hOverW, minH);
        }
        else if (underW)
        {
            width = minW;
            height = Math.Min(minW * hOverW, maxH);
        }
        else if (overH)
        {
            width = Math.Max(maxH * wOverH, minW);
            height = maxH;
        }
        else if (underH)
        {
            width = Math.Min(minH * wOverH, maxW);
            height = minH;
        }
    }
}
