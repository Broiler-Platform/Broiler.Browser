#nullable disable
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// SVG paint-server references — <c>fill="url(#id)"</c> — and the <c>&lt;pattern&gt;</c> tiling
/// they resolve to.
/// </summary>
/// <remarks>
/// Before this, <c>url(#id)</c> reached the colour parser, matched no named colour and fell through
/// to the caller's default, so every pattern-filled shape came out solid black — which is what the
/// <c>conformance-checkers/html-svg/pservers-*</c> family renders as almost its entire canvas. The
/// tiles are emitted as ordinary display items clipped to the shape, so no new backend primitive is
/// needed for a paint server the raster backend has never modelled.
/// </remarks>
internal static partial class SvgRenderer
{
    /// <summary>
    /// Upper bound on the tiles one pattern fill expands to. A pattern whose tile is a fraction of
    /// a pixel over a large shape would otherwise emit millions of items for a result no denser
    /// than the pixels underneath it; past this many the fill is left to its fallback.
    /// </summary>
    private const int MaxPatternTiles = 8192;

    /// <summary>How deep a pattern may reference another pattern in its own content.</summary>
    private const int MaxPatternDepth = 3;

    [ThreadStatic]
    private static int _patternDepth;

    /// <summary>One <c>&lt;pattern&gt;</c> definition: its attributes and its content markup.</summary>
    private readonly record struct SvgPattern(Dictionary<string, string> Attributes, string Content);

    /// <summary>
    /// Splits a paint value into its <c>url(#id)</c> reference and the fallback paint that may
    /// follow it (SVG 1.1 §13.2.2 / CSS Values 3 <c>&lt;url&gt; &lt;color&gt;?</c>).
    /// </summary>
    private static bool TryParsePaintReference(string paint, out string id, out string fallback)
    {
        id = null;
        fallback = null;
        if (string.IsNullOrEmpty(paint))
            return false;

        var m = PaintReferenceRegex().Match(paint);
        if (!m.Success)
            return false;

        id = m.Groups["id"].Value;
        var rest = m.Groups["fallback"].Value.Trim();
        fallback = rest.Length > 0 ? rest : null;
        return true;
    }

    /// <summary>
    /// Collects every <c>&lt;pattern&gt;</c> in the markup by <c>id</c>, resolving the
    /// <c>href</c>/<c>xlink:href</c> chain SVG 1.1 §13.3 defines: a pattern takes any attribute it
    /// does not declare, and its content when it has none, from the pattern it points at.
    /// </summary>
    private static Dictionary<string, SvgPattern> CollectPatterns(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml)
            || svgXml.IndexOf("<pattern", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        var patterns = new Dictionary<string, SvgPattern>(StringComparer.Ordinal);
        foreach (Match m in PatternBlockRegex().Matches(svgXml))
        {
            var attrs = ParseAttributes(m.Groups["attrs"].Value);
            if (!attrs.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
                continue;

            patterns[id] = new SvgPattern(attrs, m.Groups["content"].Success ? m.Groups["content"].Value : string.Empty);
        }

        // Resolve the reference chains after collection so a pattern may point forwards.
        foreach (var id in patterns.Keys.ToList())
            patterns[id] = ResolveInheritance(patterns, id, MaxPatternDepth);

        return patterns;
    }

    private static SvgPattern ResolveInheritance(
        Dictionary<string, SvgPattern> patterns, string id, int budget)
    {
        var pattern = patterns[id];
        if (budget <= 0)
            return pattern;

        string href = pattern.Attributes.GetValueOrDefault("href")
            ?? pattern.Attributes.GetValueOrDefault("xlink:href");
        if (string.IsNullOrEmpty(href) || href.Length < 2 || href[0] != '#')
            return pattern;

        string target = href[1..];
        if (target == id || !patterns.ContainsKey(target))
            return pattern;

        var parent = ResolveInheritance(patterns, target, budget - 1);
        var merged = new Dictionary<string, string>(pattern.Attributes, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parent.Attributes)
        {
            if (key is not ("id" or "href" or "xlink:href"))
                merged.TryAdd(key, value);
        }

        string content = string.IsNullOrWhiteSpace(pattern.Content) ? parent.Content : pattern.Content;
        return new SvgPattern(merged, content);
    }

    /// <summary>
    /// Resolves a shape's <c>fill</c>, painting the referenced pattern into
    /// <paramref name="items"/> when there is one.
    /// </summary>
    /// <param name="objectBounds">The shape's bounding box in user units — the basis for
    /// <c>objectBoundingBox</c> units and the region the tiles are clipped to.</param>
    /// <param name="elementTransform">The page-space transform the shape itself is drawn under. The
    /// tiles are separate display items from the shape, so they need it applied too — otherwise a
    /// pattern-filled shape inside a <c>&lt;g transform&gt;</c> paints its fill where the shape
    /// would have been without the transform.</param>
    /// <returns>
    /// The colour the shape itself should be filled with: empty when the pattern was painted (the
    /// tiles are the fill), the declared fallback when the reference does not resolve to anything
    /// paintable, and the plainly parsed value when it is not a reference at all.
    /// </returns>
    private static BColor ResolveFill(
        List<DisplayItem> items, RectangleF bounds, Dictionary<string, SvgPattern> patterns,
        Dictionary<string, SvgGradient> gradients,
        Dictionary<string, string> attrs, RectangleF objectBounds,
        float sx, float sy, float tx, float ty, float pctW, float pctH,
        SvgTransform elementTransform)
    {
        string paint = GetPresentationValue(attrs, "fill");
        if (!TryParsePaintReference(paint, out var id, out var fallback))
            return GetPaint(attrs, "fill", BColor.Black);

        if (patterns != null && patterns.TryGetValue(id, out var pattern)
            && EmitPatternTiles(
                items, bounds, pattern, objectBounds, sx, sy, tx, ty, pctW, pctH, elementTransform))
        {
            return BColor.Empty;
        }

        // The other paint server a fill may name. Tried after patterns because the two never share
        // an id, and before the fallback because reaching the fallback with a gradient present is
        // exactly the bug: `fill="url(#grad)"` has no colour after it, so the shape painted nothing.
        if (TryEmitGradientFill(
                items, bounds, gradients, id, objectBounds, sx, sy, tx, ty, pctW, pctH, elementTransform))
            return BColor.Empty;

        return FallbackPaint(fallback);
    }

    /// <summary>
    /// A shape's <c>fill</c> where a pattern cannot be tiled — every shape but <c>&lt;rect&gt;</c>,
    /// whose bounding box is the only one this renderer can clip a tiling to.
    /// </summary>
    private static BColor ResolveFillWithoutPattern(Dictionary<string, string> attrs, BColor defaultColor)
        => TryParsePaintReference(GetPresentationValue(attrs, "fill"), out _, out var fallback)
            ? FallbackPaint(fallback)
            : GetPaint(attrs, "fill", defaultColor);

    /// <summary>
    /// SVG 2 §13.2: a reference that resolves to nothing paintable falls back to the paint declared
    /// after it, and to no paint when none is — not to the initial black, which is what turned every
    /// pattern-filled shape into a solid black rectangle.
    /// </summary>
    private static BColor FallbackPaint(string fallback)
        => fallback != null ? ParseColorValue(fallback, BColor.Empty) : BColor.Empty;

    /// <summary>
    /// Paints one pattern over <paramref name="objectBounds"/>, clipped to it.
    /// </summary>
    /// <returns><see langword="false"/> when the pattern cannot paint anything — an empty or
    /// zero-sized tile, or a tile count past <see cref="MaxPatternTiles"/> — so the caller can use
    /// the fallback paint instead.</returns>
    private static bool EmitPatternTiles(
        List<DisplayItem> items, RectangleF bounds, SvgPattern pattern, RectangleF objectBounds,
        float sx, float sy, float tx, float ty, float pctW, float pctH,
        SvgTransform elementTransform)
    {
        if (_patternDepth >= MaxPatternDepth
            || string.IsNullOrWhiteSpace(pattern.Content)
            || objectBounds.Width <= 0 || objectBounds.Height <= 0)
        {
            return false;
        }

        var attrs = pattern.Attributes;
        bool objectBoundingBoxUnits = !string.Equals(
            attrs.GetValueOrDefault("patternUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);

        // The tile rectangle in user units. objectBoundingBox — the default — makes x/y/width/height
        // fractions of the shape's bounding box; userSpaceOnUse makes them plain lengths.
        float tileX, tileY, tileW, tileH;
        if (objectBoundingBoxUnits)
        {
            tileX = objectBounds.X + GetFraction(attrs, "x") * objectBounds.Width;
            tileY = objectBounds.Y + GetFraction(attrs, "y") * objectBounds.Height;
            tileW = GetFraction(attrs, "width") * objectBounds.Width;
            tileH = GetFraction(attrs, "height") * objectBounds.Height;
        }
        else
        {
            tileX = GetLength(attrs, "x", pctW);
            tileY = GetLength(attrs, "y", pctH);
            tileW = GetLength(attrs, "width", pctW);
            tileH = GetLength(attrs, "height", pctH);
        }

        if (tileW <= 0 || tileH <= 0)
            return false;

        // The content's own coordinate system, expressed as the viewBox of the tile-sized document
        // each tile is rendered as. A viewBox on the pattern is that system outright; otherwise
        // patternContentUnits decides whether content coordinates are user units (the default, so
        // the viewBox is the tile itself) or fractions of the shape's bounding box.
        string viewBox = attrs.GetValueOrDefault("viewBox");
        if (string.IsNullOrWhiteSpace(viewBox))
        {
            bool contentInObjectBoundingBox = string.Equals(
                attrs.GetValueOrDefault("patternContentUnits"), "objectBoundingBox",
                StringComparison.OrdinalIgnoreCase);

            viewBox = contentInObjectBoundingBox
                ? FormattableString.Invariant(
                    $"0 0 {tileW / objectBounds.Width} {tileH / objectBounds.Height}")
                : FormattableString.Invariant($"0 0 {tileW} {tileH}");
        }

        var transform = SvgTransform.Parse(attrs.GetValueOrDefault("patternTransform"));

        // A transformed tiling has to cover the shape's bounding box in the *pattern's* space, which
        // is the box pulled back through the transform.
        var coverage = transform.Inverse().MapBounds(objectBounds);
        int firstColumn = (int)Math.Floor((coverage.Left - tileX) / tileW);
        int lastColumn = (int)Math.Ceiling((coverage.Right - tileX) / tileW);
        int firstRow = (int)Math.Floor((coverage.Top - tileY) / tileH);
        int lastRow = (int)Math.Ceiling((coverage.Bottom - tileY) / tileH);

        long columns = (long)lastColumn - firstColumn;
        long rows = (long)lastRow - firstRow;
        if (columns <= 0 || rows <= 0 || columns * rows > MaxPatternTiles)
            return false;

        string tileDocument = "<svg viewBox=\"" + viewBox + "\">" + pattern.Content + "</svg>";
        var clipRect = new RectangleF(
            bounds.X + objectBounds.X * sx + tx, bounds.Y + objectBounds.Y * sy + ty,
            objectBounds.Width * sx, objectBounds.Height * sy);

        // The same transform in page pixels. Applied to the tiles' own geometry rather than pushed
        // as a transform layer: the raster canvas cannot express a rotation or a skew, and a layer
        // it cannot take falls through to a compat backend that the image renderer stubs out — so a
        // rotated patternTransform drew nothing at all rather than drawing it rotated.
        // The shape's own transform applies outside the pattern's, since the pattern is expressed
        // in the shape's user space.
        var pageTransform = elementTransform.Concat(
            transform.ToPageSpace(sx, sy, bounds.X + tx, bounds.Y + ty));

        // A rotated element clips to the mapped box rather than the mapped shape: ClipItem is a
        // rectangle, so this is the tightest clip it can express.
        clipRect = elementTransform.MapBounds(clipRect);
        items.Add(new ClipItem { Bounds = clipRect, ClipRect = clipRect });

        // The tile's content is the same markup at every position, so it is parsed and rendered
        // once and each further tile is that render translated. Re-rendering per tile parses the
        // markup up to MaxPatternTiles times for one fill, which a dense pattern over a large shape
        // reaches easily.
        _patternDepth++;
        List<DisplayItem> firstTile;
        try
        {
            firstTile = RenderSvgContent(
                tileDocument,
                new RectangleF(
                    bounds.X + (tileX + firstColumn * tileW) * sx + tx,
                    bounds.Y + (tileY + firstRow * tileH) * sy + ty,
                    tileW * sx,
                    tileH * sy));
        }
        finally
        {
            _patternDepth--;
        }

        for (int row = firstRow; row < lastRow; row++)
        {
            for (int column = firstColumn; column < lastColumn; column++)
            {
                var placement = pageTransform.Concat(new SvgTransform(
                    1, 0, 0, 1,
                    (column - firstColumn) * tileW * sx,
                    (row - firstRow) * tileH * sy));

                // Identity holds only for the first cell of an untransformed tiling, where the
                // render is already in place.
                items.AddRange(placement.IsIdentity
                    ? firstTile
                    : SvgItemTransformer.Map(firstTile, placement));
            }
        }

        items.Add(new RestoreItem { Bounds = clipRect });
        return true;
    }

    /// <summary>
    /// An <c>objectBoundingBox</c>-unit attribute: a number or a percentage of the bounding box,
    /// defaulting to zero, which is what makes an unsized pattern paint nothing.
    /// </summary>
    private static float GetFraction(Dictionary<string, string> attrs, string name)
    {
        if (!attrs.TryGetValue(name, out var raw))
            return 0f;

        var value = raw.AsSpan().Trim();
        if (value.IsEmpty)
            return 0f;

        bool percent = value[^1] == '%';
        if (percent)
            value = value[..^1];

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? (percent ? parsed / 100f : parsed)
            : 0f;
    }

    [GeneratedRegex(@"^\s*url\(\s*['""]?#(?<id>[^)'""\s]+)['""]?\s*\)(?<fallback>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PaintReferenceRegex();

    /// <summary>A <c>&lt;pattern&gt;</c>, whether it has content or is a bare self-closing reference.</summary>
    [GeneratedRegex(
        @"<pattern\b(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*?)(?:/>|>(?<content>.*?)</pattern\s*>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PatternBlockRegex();
}
