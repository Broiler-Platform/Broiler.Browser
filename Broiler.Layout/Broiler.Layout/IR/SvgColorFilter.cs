using System;
using System.Collections.Generic;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// An SVG <c>&lt;filter&gt;</c> whose primitives only transform colour, reduced to the one thing
/// such a chain does to a <em>uniformly filled</em> shape: map its fill colour to another colour.
/// <para>
/// This is the same kind of modelling <see cref="SvgFilterTable.FloodFilter"/> already applies to an
/// <c>&lt;feFlood&gt;</c>-only filter — no raster filter pipeline, just the closed-form answer for the
/// input the engine can characterise exactly. A shape filled with one solid colour has a source
/// graphic that is that colour everywhere inside it and transparent black everywhere outside, so a
/// chain of per-pixel colour operations produces exactly two colours, and the outside one stays
/// transparent for every chain modelled here (each step maps zero alpha to zero alpha).
/// </para>
/// <para>
/// Deliberately narrow. Only <c>feColorMatrix</c> (<c>type="matrix"</c>, which is also its default)
/// and <c>feComposite</c> (<c>operator="arithmetic"</c>) are modelled, only in a straight chain where
/// each primitive consumes the previous one's result, and only when the filter declares
/// <c>color-interpolation-filters="sRGB"</c> — the default is linearRGB, and the conversion is not
/// modelled, so a filter that does not say sRGB is left unfiltered rather than rendered wrongly.
/// Anything outside that is not modelled and the shape renders unfiltered, exactly as before.
/// </para>
/// </summary>
public sealed class SvgColorFilter
{
    private readonly List<Step> _steps;

    internal SvgColorFilter(List<Step> steps) => _steps = steps;

    /// <summary>One modelled primitive. Exactly one of the three forms is populated.</summary>
    internal readonly struct Step
    {
        /// <summary>The 20 <c>feColorMatrix</c> values in row-major order, or <c>null</c> for a composite.</summary>
        public float[]? Matrix { get; init; }

        /// <summary><c>feComposite operator="arithmetic"</c> coefficients k1..k4.</summary>
        public (float K1, float K2, float K3, float K4) Arithmetic { get; init; }

        /// <summary>
        /// An <c>&lt;feBlend&gt;</c> of the running colour over a constant backdrop — the colour an
        /// <c>&lt;feFlood&gt;</c> earlier in the same filter produced. Null when this step is not a
        /// blend.
        /// </summary>
        public BColor? BlendBackdrop { get; init; }

        /// <summary>The <c>feBlend</c> mode, when <see cref="BlendBackdrop"/> is set.</summary>
        public string? BlendMode { get; init; }
    }

    /// <summary>
    /// The colour a shape uniformly filled with <paramref name="source"/> renders as once this filter
    /// has been applied.
    /// </summary>
    public BColor Apply(BColor source)
    {
        float r = source.R / 255f, g = source.G / 255f, b = source.B / 255f, a = source.A / 255f;

        foreach (var step in _steps)
        {
            if (step.Matrix is { } m)
            {
                // Filter Effects §feColorMatrix: a 5x4 matrix over non-premultiplied RGBA,
                // with the fifth column an additive term.
                float nr = m[0] * r + m[1] * g + m[2] * b + m[3] * a + m[4];
                float ng = m[5] * r + m[6] * g + m[7] * b + m[8] * a + m[9];
                float nb = m[10] * r + m[11] * g + m[12] * b + m[13] * a + m[14];
                float na = m[15] * r + m[16] * g + m[17] * b + m[18] * a + m[19];
                r = Clamp01(nr); g = Clamp01(ng); b = Clamp01(nb); a = Clamp01(na);
                continue;
            }

            if (step.BlendBackdrop is { } backdrop)
            {
                (r, g, b, a) = Blend(r, g, b, a, backdrop, step.BlendMode);
                continue;
            }

            // Filter Effects §feComposite: the arithmetic operator works on *premultiplied*
            // values. Both inputs are the previous result here (the chain is straight), so
            // i1 == i2 and the k1 term is that value squared.
            var (k1, k2, k3, k4) = step.Arithmetic;
            float pr = r * a, pg = g * a, pb = b * a;

            float or_ = Clamp01(k1 * pr * pr + k2 * pr + k3 * pr + k4);
            float og = Clamp01(k1 * pg * pg + k2 * pg + k3 * pg + k4);
            float ob = Clamp01(k1 * pb * pb + k2 * pb + k3 * pb + k4);
            float oa = Clamp01(k1 * a * a + k2 * a + k3 * a + k4);

            if (oa <= 0f)
            {
                r = g = b = a = 0f;
                continue;
            }

            r = Clamp01(or_ / oa); g = Clamp01(og / oa); b = Clamp01(ob / oa); a = oa;
        }

        return BColor.FromArgb(
            (int)MathF.Round(a * 255f),
            (int)MathF.Round(r * 255f),
            (int)MathF.Round(g * 255f),
            (int)MathF.Round(b * 255f));
    }

    /// <summary>
    /// Filter Effects §feBlend: the source colour composited over a constant backdrop under one of
    /// the separable blend modes, in non-premultiplied sRGB.
    /// </summary>
    /// <remarks>
    /// The general primitive blends two images; this is the case where the backdrop is a single
    /// colour, which is what an <c>&lt;feFlood&gt;</c> produces over the whole filter region — the
    /// shape <c>conformance-checkers/html-svg/filters-blend-01-b</c> uses five times over. Only the
    /// five separable modes SVG 1.1 defines are taken; anything else leaves the colour alone rather
    /// than guessing.
    /// <para>
    /// The compositing is Porter-Duff source-over with the blended colour, per the Compositing and
    /// Blending model: the blend function applies where both are opaque, and each input shows
    /// through unblended where the other is transparent.
    /// </para>
    /// </remarks>
    private static (float R, float G, float B, float A) Blend(
        float r, float g, float b, float a, BColor backdrop, string? mode)
    {
        float br = backdrop.R / 255f, bg = backdrop.G / 255f, bb = backdrop.B / 255f;
        float ba = backdrop.A / 255f;

        float outA = a + ba * (1 - a);
        if (outA <= 0f)
            return (0f, 0f, 0f, 0f);

        return (
            Clamp01(Composite(r, br)), Clamp01(Composite(g, bg)), Clamp01(Composite(b, bb)),
            Clamp01(outA));

        float Composite(float source, float back)
        {
            float blended = BlendChannel(source, back, mode);

            // Compositing and Blending §9: co = cs × αs × (1 − αb) + cb × αb × (1 − αs)
            //                                  + B(cb, cs) × αs × αb, then un-premultiply.
            float premultiplied =
                source * a * (1 - ba) + back * ba * (1 - a) + blended * a * ba;
            return premultiplied / outA;
        }
    }

    private static float BlendChannel(float source, float backdrop, string? mode) => mode switch
    {
        "multiply" => source * backdrop,
        "screen" => source + backdrop - source * backdrop,
        "darken" => MathF.Min(source, backdrop),
        "lighten" => MathF.Max(source, backdrop),
        _ => source,
    };

    /// <summary>The <c>feBlend</c> modes this models — SVG 1.1's five separable ones.</summary>
    internal static bool IsModelledBlendMode(string mode) =>
        mode is "normal" or "multiply" or "screen" or "darken" or "lighten";

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
