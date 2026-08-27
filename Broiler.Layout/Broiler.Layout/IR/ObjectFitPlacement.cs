using System.Drawing;

namespace Broiler.Layout.IR;

/// <summary>
/// CSS Images 3 §5.5 <c>object-fit</c> and §5.6 <c>object-position</c>: the rectangle a replaced
/// element's content is drawn into, given its content box and its intrinsic size.
/// </summary>
/// <remarks>
/// <para>
/// Two steps, in the order the spec states them. First <c>object-fit</c> picks the
/// <em>concrete object size</em> from the intrinsic size and the content box — stretch to it
/// (<c>fill</c>), scale to fit inside it (<c>contain</c>), scale to cover it (<c>cover</c>), leave it
/// alone (<c>none</c>), or the smaller of the last two (<c>scale-down</c>). Then
/// <c>object-position</c> places an object of that size inside the content box, by the same
/// <c>&lt;position&gt;</c> grammar <c>background-position</c> uses — see <see cref="CssPositionValue"/>.
/// </para>
/// <para>
/// The result can be larger than the content box (<c>cover</c> always is on one axis, and
/// <c>none</c> is whenever the image is bigger than its box), in which case the overflow is clipped
/// to the content box rather than cropped out of the source bitmap. Clipping is what the spec
/// describes, and it is also the only reading that stays correct when the decoded bitmap is not the
/// intrinsic size — an SVG rasterised at 2× for quality, say, whose bitmap is twice the size the
/// crop would have been measured in. <see cref="Placement.NeedsClip"/> says when the caller has to
/// emit one; the common case of an object that fits needs none, and paints exactly as it did before
/// this property was read at all.
/// </para>
/// </remarks>
public static class ObjectFitPlacement
{
    /// <summary>Where a replaced element's content paints, and whether it overflows its box.</summary>
    /// <param name="Dest">The rectangle to draw the whole image into, in absolute coordinates.</param>
    /// <param name="NeedsClip">
    /// Whether <paramref name="Dest"/> leaves the content box, so the caller must clip to it.
    /// </param>
    public readonly record struct Placement(RectangleF Dest, bool NeedsClip)
    {
        /// <summary>Whether the placement paints nothing — a degenerate or fully clipped box.</summary>
        public bool IsEmpty => Dest.Width <= 0 || Dest.Height <= 0;
    }

    /// <summary>
    /// Resolves the placement of <paramref name="intrinsicSize"/> inside <paramref name="contentBox"/>.
    /// </summary>
    /// <param name="contentBox">The element's content box, in absolute coordinates.</param>
    /// <param name="intrinsicSize">
    /// The content's intrinsic size in CSS px, or <see cref="SizeF.Empty"/> when it has none — an SVG
    /// with a <c>viewBox</c> and no <c>width</c>/<c>height</c> being the usual case.
    /// </param>
    /// <param name="intrinsicRatio">
    /// The content's intrinsic aspect ratio (width ÷ height), or 0 when it has none. Reported apart
    /// from the size because an image can carry a ratio without one, and because the two disagree
    /// when it does.
    /// </param>
    /// <param name="objectFit">The computed <c>object-fit</c>; <c>fill</c> when not declared.</param>
    /// <param name="objectPosition">The computed <c>object-position</c>; <c>50% 50%</c> when not declared.</param>
    /// <param name="emSize">Font size in px, for an <c>object-position</c> offset in font-relative units.</param>
    public static Placement Resolve(
        RectangleF contentBox,
        SizeF intrinsicSize,
        float intrinsicRatio,
        string? objectFit,
        string? objectPosition,
        double emSize)
    {
        if (contentBox.Width <= 0 || contentBox.Height <= 0)
            return new Placement(contentBox, NeedsClip: false);

        // Neither property declared is the overwhelmingly common case and the whole of the old
        // behaviour, so it answers before anything is parsed. It is a fast path rather than the
        // rule: `fill` still has to go through the general path once a position is stated, because
        // an offset in absolute units moves the object even across zero free space.
        if (IsInitial(objectFit, "fill") && IsInitial(objectPosition, "50% 50%"))
            return new Placement(contentBox, NeedsClip: false);

        var size = ConcreteObjectSize(contentBox.Size, intrinsicSize, intrinsicRatio, objectFit);

        var offset = CssPositionValue.Resolve(
            string.IsNullOrWhiteSpace(objectPosition) ? "50% 50%" : objectPosition,
            contentBox.Size,
            size,
            emSize);

        var dest = new RectangleF(contentBox.X + offset.X, contentBox.Y + offset.Y, size.Width, size.Height);

        bool needsClip = dest.X < contentBox.X
            || dest.Y < contentBox.Y
            || dest.Right > contentBox.Right
            || dest.Bottom > contentBox.Bottom;

        return new Placement(dest, needsClip);
    }

    /// <summary>Whether a declaration is absent or spelled exactly as its initial value.</summary>
    private static bool IsInitial(string? value, string initial) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Equals(initial, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CSS Images 3 §5.5: the concrete object size <c>object-fit</c> asks for. Returns the content
    /// box's own size whenever the fit cannot be applied — an unrecognised keyword, or content with
    /// neither an intrinsic size nor a ratio — which is <c>fill</c>, the initial value.
    /// </summary>
    private static SizeF ConcreteObjectSize(
        SizeF contentBox, SizeF intrinsicSize, float intrinsicRatio, string? objectFit)
    {
        bool hasSize = intrinsicSize.Width > 0 && intrinsicSize.Height > 0;

        // The ratio comes from the content when it states one and from its intrinsic size otherwise;
        // the two agree except for content that has a ratio and no size, which is why it is reported
        // separately at all.
        float ratio = intrinsicRatio > 0
            ? intrinsicRatio
            : hasSize ? intrinsicSize.Width / intrinsicSize.Height : 0;

        string fit = objectFit?.Trim() ?? string.Empty;

        // `scale-down` is whichever of `none` and `contain` gives the smaller concrete size, which
        // is `none` exactly when the content already fits — so it resolves to one of the other two
        // here rather than repeating their arithmetic. With no intrinsic size the two coincide.
        if (fit.Equals("scale-down", StringComparison.OrdinalIgnoreCase))
        {
            fit = hasSize
                  && intrinsicSize.Width <= contentBox.Width
                  && intrinsicSize.Height <= contentBox.Height
                ? "none"
                : "contain";
        }

        if (fit.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            // The default sizing algorithm (§5.1) with no specified size: the intrinsic size when
            // there is one, otherwise the default object size — the content box — constrained by
            // the ratio, which is the contain constraint below.
            if (hasSize)
                return intrinsicSize;

            fit = "contain";
        }

        bool contain = fit.Equals("contain", StringComparison.OrdinalIgnoreCase);
        bool cover = fit.Equals("cover", StringComparison.OrdinalIgnoreCase);

        // Both constraints in §5.1 are stated over the ratio alone — the largest (contain) or the
        // smallest (cover) rectangle of that ratio that fits within, or covers, the content box.
        // Without a ratio there is nothing to preserve and the constraint rectangle is the answer.
        if (ratio <= 0 || (!contain && !cover))
            return contentBox;

        float boxRatio = contentBox.Height > 0 ? contentBox.Width / contentBox.Height : 0;

        // A content box wider than the ratio has to lose width to fit and gain it to cover.
        return (boxRatio > ratio) == contain
            ? new SizeF(contentBox.Height * ratio, contentBox.Height)
            : new SizeF(contentBox.Width, contentBox.Width / ratio);
    }
}
