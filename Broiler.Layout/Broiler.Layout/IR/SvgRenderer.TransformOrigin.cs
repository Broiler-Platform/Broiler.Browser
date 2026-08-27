using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace Broiler.Layout.IR;

/// <summary>
/// CSS Transforms 1 §6/§8: <c>transform-box</c> and <c>transform-origin</c> on an SVG element.
/// </summary>
/// <remarks>
/// <para>
/// An SVG <c>transform</c> was applied about the user-space origin and nothing else — which is
/// what SVG 1.1 did, and is still right for the initial <c>transform-box: view-box</c>. What was
/// missing is the box-relative case: <c>transform-box: fill-box</c> makes the element's own fill
/// bounding box the reference box, and <c>transform-origin</c> then names a point inside it (the
/// centre, <c>50% 50%</c>, when it is not written). The element's own transform has to be
/// conjugated by that point — <c>T(o) · M · T(-o)</c> — and it was not, so a
/// <c>rotate(90)</c> about a box centre turned about the viewport corner instead and swung the
/// element clean off its intended place.
/// </para>
/// <para>
/// The 45 <c>css-transforms/transform-origin/svg-origin-*</c> tests of the 2026-08-21 run scored an
/// identical 97.14% on this: each declares <c>transform-box: fill-box</c> on a 150×150 rect, and
/// 150×150 is 2.86% of the canvas — the whole of their disagreement.
/// </para>
/// <para>
/// Only the box-relative <c>transform-box</c> values take this path. Under the initial
/// <c>view-box</c> the transform keeps the meaning it already had, so a document that never
/// mentions <c>transform-box</c> renders exactly as before.
/// </para>
/// </remarks>
internal static partial class SvgRenderer
{
    /// <summary>
    /// The element's own transform, conjugated by its <c>transform-origin</c> when
    /// <c>transform-box</c> makes a box the reference. <paramref name="fillBox"/> is the element's
    /// fill bounding box in user units.
    /// </summary>
    private static SvgTransform OwnTransformAbout(
        Dictionary<string, string> attrs, RectangleF fillBox)
    {
        var own = SvgTransform.Parse(attrs.GetValueOrDefault("transform"));

        if (own.IsIdentity || !UsesBoxRelativeTransformBox(attrs))
            return own;

        var origin = ResolveTransformOrigin(GetPresentationValue(attrs, "transform-origin"), fillBox);
        if (Math.Abs(origin.X) < 1e-6f && Math.Abs(origin.Y) < 1e-6f)
            return own;

        var toOrigin = new SvgTransform(1, 0, 0, 1, origin.X, origin.Y);
        var fromOrigin = new SvgTransform(1, 0, 0, 1, -origin.X, -origin.Y);
        return toOrigin.Concat(own).Concat(fromOrigin);
    }

    /// <summary>
    /// Whether <c>transform-box</c> names a box of the element rather than the viewport. The
    /// initial value is <c>view-box</c>, under which the transform keeps SVG 1.1's meaning; every
    /// other value makes the element's own box the reference. <c>stroke-box</c>, <c>content-box</c>
    /// and <c>border-box</c> differ from <c>fill-box</c> only by the stroke and by box decorations
    /// this renderer does not give a shape, so they resolve to the same rectangle here.
    /// </summary>
    private static bool UsesBoxRelativeTransformBox(Dictionary<string, string> attrs)
    {
        string box = GetPresentationValue(attrs, "transform-box")?.Trim();
        return !string.IsNullOrEmpty(box)
            && !box.Equals("view-box", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CSS Transforms 1 §8: <c>transform-origin</c> as a point in user units, relative to
    /// <paramref name="box"/>.
    /// </summary>
    /// <remarks>
    /// The grammar lives in <see cref="CssTransformOrigin"/>, shared with the script bridge's
    /// transform chain and with the paint walker, so the three cannot drift apart. What is specific
    /// here is the <b>initial</b> value: an SVG element has no CSS layout box, and §8 gives one of
    /// those a used value of <c>0 0</c> — the reference box's own corner, not its centre. An
    /// invalid declaration is dropped whole and takes that same value. Falling back to the centre
    /// instead is what kept the twelve WPT
    /// <c>css-transforms/transform-origin/svg-origin-relative-length-invalid-*</c> cases failing:
    /// each is built so that its transform maps the square onto itself about <c>0 0</c>, matching a
    /// reference that draws the rect with no transform at all, and a centre origin threw the square
    /// hundreds of pixels away.
    /// </remarks>
    private static PointF ResolveTransformOrigin(string value, RectangleF box) =>
        CssTransformOrigin.Resolve(value, box, initialIsBoxCorner: true);
}
