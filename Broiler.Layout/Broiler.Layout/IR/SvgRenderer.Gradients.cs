using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// SVG gradient paint servers — <c>&lt;linearGradient&gt;</c> and <c>&lt;radialGradient&gt;</c>
/// reached through <c>fill="url(#id)"</c>.
/// </summary>
/// <remarks>
/// <para>
/// A gradient fill painted <b>nothing at all</b>: <see cref="ResolveFill"/> recognised the
/// reference, found no <c>&lt;pattern&gt;</c> under that id, and fell through to the fallback paint
/// — which for the ordinary <c>fill="url(#grad)"</c> with no colour after it is "no paint". Every
/// gradient-filled shape was therefore invisible, and a test that fills with a gradient
/// specifically to avoid a false positive (the whole
/// <c>css-transforms/transform-origin/svg-origin-*</c> family, 82 of them scoring an identical
/// 97.21%) rendered a blank box on both sides of its own comparison.
/// </para>
/// <para>
/// The gradient is emitted as a <see cref="DrawTiledGradientItem"/> — the same display item CSS
/// backgrounds already use, so the raster backend needs no new primitive — sized to a single
/// non-repeating tile over the shape's bounding box. Like <see cref="EmitPatternTiles"/> this is
/// offered for <c>&lt;rect&gt;</c> only: the tile is clipped to a rectangle, which is the shape
/// whose bounding box *is* its geometry. Any other shape would take the gradient over its bounding
/// box rather than its outline, which is worse than the fallback it keeps instead.
/// </para>
/// </remarks>
internal static partial class SvgRenderer
{
    /// <summary>How far an <c>href</c> chain between gradients is followed (SVG 1.1 §13.2.4).</summary>
    private const int MaxGradientHrefDepth = 4;

    /// <summary>One gradient definition: its attributes, its stop markup, and which kind it is.</summary>
    private readonly record struct SvgGradient(
        Dictionary<string, string> Attributes, string Content, bool IsRadial);

    /// <summary>
    /// Collects every gradient in the markup by <c>id</c>. Like <c>&lt;pattern&gt;</c>, a gradient
    /// takes any attribute it does not declare — and its stops when it declares none — from the
    /// gradient its <c>href</c>/<c>xlink:href</c> points at, so the chains are resolved after the
    /// whole document has been seen and a gradient may point forwards.
    /// </summary>
    private static Dictionary<string, SvgGradient> CollectGradients(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml)
            || svgXml.IndexOf("Gradient", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        var gradients = new Dictionary<string, SvgGradient>(StringComparer.Ordinal);
        foreach (Match m in GradientBlockRegex().Matches(svgXml))
        {
            var attrs = ParseAttributes(m.Groups["attrs"].Value);
            if (!attrs.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
                continue;

            // ParseAttributes' name pattern excludes ':', so `xlink:href` lands under the same
            // "href" key as a plain `href` and whichever is written last wins. SVG 2 §6.5 says the
            // unprefixed one wins whenever both are present, so it is recovered from the raw
            // attribute text and reinstated. svg/linking/reftests/href-gradient-element states
            // exactly this, with the two spellings pointing at different gradients on purpose.
            var plainHref = UnprefixedHrefRegex().Match(m.Groups["attrs"].Value);
            if (plainHref.Success)
                attrs["href"] = plainHref.Groups["value"].Value;

            bool radial = m.Groups["kind"].Value.StartsWith("radial", StringComparison.OrdinalIgnoreCase);
            gradients[id] = new SvgGradient(
                attrs, m.Groups["content"].Success ? m.Groups["content"].Value : string.Empty, radial);
        }

        if (gradients.Count == 0)
            return null;

        foreach (var id in gradients.Keys.ToList())
            gradients[id] = ResolveGradientInheritance(gradients, id, MaxGradientHrefDepth);

        return gradients;
    }

    private static SvgGradient ResolveGradientInheritance(
        Dictionary<string, SvgGradient> gradients, string id, int budget)
    {
        var gradient = gradients[id];
        if (budget <= 0)
            return gradient;

        // Either spelling arrives under "href" (see CollectGradients), with the unprefixed one
        // already preferred where a gradient carries both.
        string href = gradient.Attributes.GetValueOrDefault("href");

        if (string.IsNullOrWhiteSpace(href) || href.Length < 2 || href[0] != '#')
            return gradient;

        string parentId = href[1..];
        if (!gradients.TryGetValue(parentId, out _) || string.Equals(parentId, id, StringComparison.Ordinal))
            return gradient;

        var parent = ResolveGradientInheritance(gradients, parentId, budget - 1);

        var merged = new Dictionary<string, string>(parent.Attributes, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in gradient.Attributes)
            merged[key] = value;

        string content = string.IsNullOrWhiteSpace(gradient.Content) ? parent.Content : gradient.Content;
        return new SvgGradient(merged, content, gradient.IsRadial);
    }

    /// <summary>
    /// Paints the gradient <paramref name="id"/> names over <paramref name="objectBounds"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the id names no gradient, or it carries fewer than the two
    /// stops a ramp needs, so the caller can use the fallback paint instead.
    /// </returns>
    private static bool TryEmitGradientFill(
        List<DisplayItem> items, RectangleF bounds, Dictionary<string, SvgGradient> gradients,
        string id, RectangleF objectBounds, float sx, float sy, float tx, float ty,
        float pctW, float pctH, SvgTransform elementTransform)
    {
        if (gradients == null
            || !gradients.TryGetValue(id, out var gradient)
            || objectBounds.Width <= 0 || objectBounds.Height <= 0)
        {
            return false;
        }

        var stops = ParseGradientStops(gradient.Content);
        if (stops.Count == 0)
            return false;

        // A single stop is a solid fill of that colour, which the ramp below cannot express.
        if (stops.Count == 1)
            stops = [stops[0], new GradientStop { Color = stops[0].Color, Position = 1f }];

        var attrs = gradient.Attributes;
        bool objectBoundingBoxUnits = !string.Equals(
            attrs.GetValueOrDefault("gradientUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);

        // The gradient's geometry in user units, whichever system it was written in. Fractions of
        // the shape's bounding box is the default (SVG 1.1 §13.2.2); userSpaceOnUse means the
        // attributes are plain lengths already.
        float Coordinate(string name, float defaultFraction, bool horizontal)
        {
            if (objectBoundingBoxUnits)
            {
                float fraction = attrs.ContainsKey(name) ? GetFraction(attrs, name) : defaultFraction;
                return horizontal
                    ? objectBounds.X + fraction * objectBounds.Width
                    : objectBounds.Y + fraction * objectBounds.Height;
            }

            if (!attrs.ContainsKey(name))
            {
                return horizontal
                    ? objectBounds.X + defaultFraction * objectBounds.Width
                    : objectBounds.Y + defaultFraction * objectBounds.Height;
            }

            return GetLength(attrs, name, horizontal ? pctW : pctH);
        }

        // Page pixels, then through the element's own transform — the shape it fills is mapped the
        // same way by AddShape. Without this a `rotate(90)` rect kept an unrotated ramp, which is
        // what css-transforms/transform-origin/svg-origin-* measures: the whole point of those
        // tests is that the gradient turns with the box.
        PointF ToPage(float ux, float uy) =>
            elementTransform.Map(bounds.X + ux * sx + tx, bounds.Y + uy * sy + ty);

        var fillRect = elementTransform.MapBounds(new RectangleF(
            bounds.X + objectBounds.X * sx + tx,
            bounds.Y + objectBounds.Y * sy + ty,
            objectBounds.Width * sx,
            objectBounds.Height * sy));

        float angle = 90f;
        float centerX = 0.5f, centerY = 0.5f;

        if (gradient.IsRadial)
        {
            // SVG 1.1 §13.2.3 defaults: cx/cy at the centre of the bounding box.
            var centre = ToPage(
                Coordinate("cx", 0.5f, horizontal: true), Coordinate("cy", 0.5f, horizontal: false));
            centerX = fillRect.Width > 0 ? (centre.X - fillRect.X) / fillRect.Width : 0.5f;
            centerY = fillRect.Height > 0 ? (centre.Y - fillRect.Y) / fillRect.Height : 0.5f;
        }
        else
        {
            // SVG 1.1 §13.2.2 defaults: x1,y1 = 0%,0% and x2,y2 = 100%,0% — a left-to-right ramp.
            var start = ToPage(
                Coordinate("x1", 0f, horizontal: true), Coordinate("y1", 0f, horizontal: false));
            var end = ToPage(
                Coordinate("x2", 1f, horizontal: true), Coordinate("y2", 0f, horizontal: false));
            angle = GradientAngleDegrees(end.X - start.X, end.Y - start.Y);
        }

        items.Add(new DrawTiledGradientItem
        {
            Bounds = bounds,
            FillRect = fillRect,
            TileOrigin = new PointF(fillRect.X, fillRect.Y),
            TileWidth = Math.Max(1f, fillRect.Width),
            TileHeight = Math.Max(1f, fillRect.Height),
            Repeat = "no-repeat",
            Stops = stops,
            Angle = angle,
            IsRadial = gradient.IsRadial,
            CenterX = Math.Clamp(centerX, 0f, 1f),
            CenterY = Math.Clamp(centerY, 0f, 1f),
        });

        return true;
    }

    /// <summary>
    /// The gradient vector as a CSS gradient angle, whose zero points to the top and which turns
    /// clockwise — so a vector pointing right (SVG's default) is 90°. Screen coordinates run down,
    /// which is why the y component is negated. A degenerate vector keeps the default.
    /// </summary>
    private static float GradientAngleDegrees(float dx, float dy)
    {
        if (Math.Abs(dx) < 1e-6f && Math.Abs(dy) < 1e-6f)
            return 90f;

        double degrees = Math.Atan2(dx, -dy) * (180.0 / Math.PI);
        if (degrees < 0)
            degrees += 360.0;

        return (float)degrees;
    }

    /// <summary>
    /// Reads the <c>&lt;stop&gt;</c> children of a gradient in document order. <c>stop-color</c>
    /// and <c>stop-opacity</c> are presentation attributes, so a <c>style</c> declaration on the
    /// stop wins over the attribute of the same name; the opacity folds into the stop's alpha.
    /// Offsets are clamped to 0..1 and kept non-decreasing, which is what SVG 1.1 §13.2.4 asks for.
    /// </summary>
    private static List<GradientStop> ParseGradientStops(string content)
    {
        var stops = new List<GradientStop>();
        if (string.IsNullOrWhiteSpace(content))
            return stops;

        float previous = 0f;
        foreach (Match m in GradientStopRegex().Matches(content))
        {
            var attrs = ParseAttributes(m.Groups["attrs"].Value);

            float offset = Math.Clamp(GetFraction(attrs, "offset"), 0f, 1f);
            if (offset < previous)
                offset = previous;
            previous = offset;

            var color = ParseColorValue(GetPresentationValue(attrs, "stop-color") ?? "black", BColor.Black);
            float opacity = ParseAlpha(GetPresentationValue(attrs, "stop-opacity"));
            if (opacity < 1f)
                color = BColor.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);

            stops.Add(new GradientStop { Color = color, Position = offset });
        }

        return stops;
    }

    /// <summary>A <c>&lt;linearGradient&gt;</c> or <c>&lt;radialGradient&gt;</c>, with or without stops.</summary>
    [GeneratedRegex(
        @"<(?<kind>linear|radial)Gradient\b(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*?)(?:/>|>(?<content>.*?)</(?:linear|radial)Gradient\s*>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex GradientBlockRegex();

    /// <summary>
    /// An <c>href</c> attribute with no namespace prefix — the lookbehind is what keeps it from
    /// matching the <c>href</c> inside <c>xlink:href</c>.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\w:.-])href\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')",
        RegexOptions.IgnoreCase)]
    private static partial Regex UnprefixedHrefRegex();

    /// <summary>One <c>&lt;stop&gt;</c> inside a gradient.</summary>
    [GeneratedRegex(
        @"<stop\b(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*?)(?:/>|>.*?</stop\s*>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex GradientStopRegex();
}
