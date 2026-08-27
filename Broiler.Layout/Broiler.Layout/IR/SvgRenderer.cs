#nullable disable
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// Converts simplified SVG markup into <see cref="DisplayItem"/> entries
/// that can be rendered by <see cref="RGraphicsRasterBackend"/>.
/// </summary>
internal static partial class SvgRenderer
{
    /// <summary>
    /// Parses SVG XML content and returns display items positioned within
    /// the given <paramref name="bounds"/> rectangle.
    /// </summary>
    /// <param name="effectiveZoom">
    /// The owning element's compounded CSS <c>zoom</c> (<c>1.0</c> when the native-zoom engine is off).
    /// It seeds the SVG user-unit → CSS-pixel scale, so a <em>view-box-less</em> SVG's raw geometry
    /// (which maps 1:1 to CSS px) scales with the box. A view-boxed SVG is unaffected: its scale is
    /// derived from <paramref name="bounds"/>, which the layout already emits zoomed, so the view-box
    /// branch overrides the seed rather than compounding it.
    /// </param>
    public static List<DisplayItem> RenderSvgContent(string svgXml, RectangleF bounds, double effectiveZoom = 1.0)
    {
        var items = new List<DisplayItem>();
        if (string.IsNullOrEmpty(svgXml))
            return items;

        ParseElements(svgXml, bounds, items, effectiveZoom);
        return items;
    }

    private static void ParseElements(string svgXml, RectangleF bounds, List<DisplayItem> output, double effectiveZoom)
    {
        // Each element's items are collected against its start-tag offset and flushed in document
        // order at the end, because the per-shape passes below each sweep the whole markup and would
        // otherwise paint every rect before every circle before every text. See SvgPaintOrder.
        var order = new SvgPaintOrder();
        // Parse the viewBox from the root <svg> element to compute the
        // coordinate transform.  When a viewBox is present, SVG coordinates
        // are in viewBox space and must be scaled/translated to CSS bounds.
        // Default preserveAspectRatio is "xMidYMid meet" — scale uniformly
        // to fit, then centre in the viewport.
        // Seed the user-unit scale with the element's zoom: with no viewBox the raw SVG coordinates map
        // 1:1 to CSS pixels, so they must scale by zoom like any other used length. A viewBox overrides
        // this below with a scale derived from the (already-zoomed) bounds, so it is not compounded.
        float sx = (float)effectiveZoom, sy = (float)effectiveZoom, tx = 0f, ty = 0f;
        // How many device pixels one unit of the *viewport* coordinate system is — the basis a
        // non-scaling stroke is drawn in (SVG 2 §14.2), which is the user-unit scale only when no
        // view box remaps it. Derived below from the root's declared width against the box it was
        // laid out into, so a CSS zoom or a width in other units is already folded in.
        float viewportScale = (float)effectiveZoom;
        // The viewport a percentage length resolves against, in user units (SVG 1.1 §7.10). Zero until
        // a viewBox establishes one; the fallback below derives it from the bounds instead.
        float viewportW = 0f, viewportH = 0f;
        var pathStartsById = new Dictionary<string, PointF>(StringComparer.OrdinalIgnoreCase);
        var svgMatch = ParseRegex().Match(svgXml);
        if (svgMatch.Success)
        {
            var svgAttrs = ParseAttributes(svgMatch.Groups[1].Value);

            // A declared width names the viewport in its own units; the box it was laid out into is
            // that viewport in device pixels, so their ratio is the viewport scale. A percentage or
            // absent width leaves the zoom-derived seed, since neither states a unit count.
            if (svgAttrs.TryGetValue("width", out var declaredWidth)
                && !declaredWidth.Contains('%')
                && float.TryParse(
                    declaredWidth.Replace("px", string.Empty), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float widthUnits)
                && widthUnits > 0
                && bounds.Width > 0)
            {
                viewportScale = bounds.Width / widthUnits;
            }

            if (svgAttrs.TryGetValue("viewBox", out var vb))
            {
                var parts = vb.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbX) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbY) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbW) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float vbH) &&
                    vbW > 0 && vbH > 0)
                {
                    // SVG 1.1 §7.8: preserveAspectRatio decides how the view box maps onto the
                    // viewport. The default "xMidYMid meet" scales uniformly to fit and centres;
                    // `none` stretches each axis independently, which is what a <symbol> used at a
                    // different shape asks for, and `slice` fills instead of fitting.
                    (sx, sy, tx, ty) = ResolveViewBoxMapping(
                        svgAttrs.GetValueOrDefault("preserveAspectRatio"),
                        bounds, vbX, vbY, vbW, vbH);
                    // A viewBox establishes the viewport for its children, so a percentage inside it
                    // resolves against the viewBox extent rather than against the CSS box.
                    viewportW = vbW;
                    viewportH = vbH;
                }
            }
        }

        // No viewBox: user units map to CSS pixels through the same scale seeded above, so the
        // viewport in user units is the destination box divided by it.
        if (viewportW <= 0)
            viewportW = sx > 0 ? bounds.Width / sx : bounds.Width;
        if (viewportH <= 0)
            viewportH = sy > 0 ? bounds.Height / sy : bounds.Height;

        // SVG 1.1 §7.10: a percentage on a length in the horizontal axis resolves against the viewport
        // width, one in the vertical axis against its height, and one on a length that is in neither
        // (r, stroke-width, font-size) against the normalised diagonal.
        float pctW = viewportW, pctH = viewportH;
        float pctD = (float)(Math.Sqrt(viewportW * viewportW + viewportH * viewportH) / Math.Sqrt(2));

        // The nesting the per-element regexes below cannot see: which elements are inside a
        // <defs>/<symbol>/<pattern> (rendered only through a reference) or a comment, and which
        // presentation attributes each inherits from its ancestors.
        var structure = SvgStructure.Scan(svgXml);

        // The paint servers a fill may point at. Collected from the whole markup, not just the
        // rendered part: a <pattern> lives in <defs> precisely so it paints only through a reference.
        var patterns = CollectPatterns(svgXml);

        // The gradient paint servers, collected the same way and for the same reason.
        var gradients = CollectGradients(svgXml);

        // The <clipPath> rectangles a shape's own clip-path attribute may point at.
        var clipRects = CollectClipRects(svgXml);

        foreach (Match m in ParseSvgRegex().Matches(svgXml))
        {
            var attrs = ParseAttributes(m.Groups[1].Value);
            if (!attrs.TryGetValue("id", out var id) || !attrs.TryGetValue("d", out var pathData))
                continue;

            var start = TryGetPathStart(pathData);
            if (start.HasValue)
                pathStartsById[id] = start.Value;
        }

        // <rect ... /> or <rect ...></rect>
        foreach (Match m in ParseRectRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var items = order.Open(m.Index);

            // The rect's own geometry, needed before the transform because `transform-box:
            // fill-box` makes this rectangle the reference box its `transform-origin` is measured
            // in. It depends only on the attributes, so computing it first changes nothing else.
            var rectBox = new RectangleF(
                GetLength(attrs, "x", pctW), GetLength(attrs, "y", pctH),
                GetLength(attrs, "width", pctW), GetLength(attrs, "height", pctH));

            var elementTransform = structure.AncestorTransformAt(m.Index)
                .Concat(OwnTransformAbout(attrs, rectBox))
                .ToPageSpace(sx, sy, bounds.X + tx, bounds.Y + ty);
            if (ClipRectFor(attrs, clipRects, bounds, sx, sy, tx, ty, elementTransform) is { } clip)
                order.ClipLast(clip);

            // A rect referencing an feFlood filter is replaced by a solid fill of the flood colour
            // over the filter region — the default objectBoundingBox region is the shape's bounding
            // box expanded by -10%/+10% on each side (x-10%, y-10%, w+20%, h+20%).
            if (TryResolveFloodFilter(attrs, out var floodColor))
            {
                float bx = GetLength(attrs, "x", pctW), by = GetLength(attrs, "y", pctH);
                float bw = GetLength(attrs, "width", pctW), bh = GetLength(attrs, "height", pctH);
                float fx = bx - 0.1f * bw, fy = by - 0.1f * bh, fw = 1.2f * bw, fh = 1.2f * bh;
                AddShape(items, bounds, attrs, elementTransform, new DrawSvgRectItem
                {
                    Bounds = bounds,
                    X = fx * sx + tx,
                    Y = fy * sy + ty,
                    Width = fw * sx,
                    Height = fh * sy,
                    Fill = floodColor,
                    Stroke = BColor.Empty,
                    StrokeWidth = 0,
                });
                continue;
            }

            // A pattern fill paints as tiles behind the shape, so it is resolved before the rect
            // item is added and leaves the rect's own fill empty; a stroke then draws over it.
            var objectBounds = rectBox;
            var rectFill = ResolveFill(
                items, bounds, patterns, gradients, attrs, objectBounds, sx, sy, tx, ty, pctW, pctH,
                elementTransform);
            var rectStroke = GetPaint(attrs, "stroke", BColor.Empty);

            // A colour-only filter chain over a solid fill is equivalent to recolouring the shape;
            // its geometry is untouched (WPT css/filter-effects/fecolormatrix-negative, whose
            // feColorMatrix + arithmetic feComposite turn #ffaa00 into cyan).
            if (TryResolveColorFilter(attrs, rectFill, rectStroke, out var filteredFill))
                rectFill = filteredFill;

            AddShape(items, bounds, attrs, elementTransform, new DrawSvgRectItem
            {
                Bounds = bounds,
                X = GetLength(attrs, "x", pctW) * sx + tx,
                Y = GetLength(attrs, "y", pctH) * sy + ty,
                Width = GetLength(attrs, "width", pctW) * sx,
                Height = GetLength(attrs, "height", pctH) * sy,
                Fill = rectFill,
                Stroke = rectStroke,
                StrokeWidth = StrokeWidthOf(attrs, pctD, sx, sy, viewportScale),
            });
        }

        // <circle ... />
        foreach (Match m in ParseCircleRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var items = order.Open(m.Index);
            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);
            if (ClipRectFor(attrs, clipRects, bounds, sx, sy, tx, ty, elementTransform) is { } clip)
                order.ClipLast(clip);

            float r = GetLength(attrs, "r", pctD);
            AddShape(items, bounds, attrs, elementTransform, new DrawSvgEllipseItem
            {
                Bounds = bounds,
                Cx = GetLength(attrs, "cx", pctW) * sx + tx,
                Cy = GetLength(attrs, "cy", pctH) * sy + ty,
                Rx = r * sx,
                Ry = r * sy,
                Fill = ResolveFillWithoutPattern(attrs, BColor.Black),
                Stroke = GetPaint(attrs, "stroke", BColor.Empty),
                StrokeWidth = StrokeWidthOf(attrs, pctD, sx, sy, viewportScale),
            });
        }

        // <ellipse ... />
        foreach (Match m in ParseEllipseRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var items = order.Open(m.Index);
            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);
            if (ClipRectFor(attrs, clipRects, bounds, sx, sy, tx, ty, elementTransform) is { } clip)
                order.ClipLast(clip);

            AddShape(items, bounds, attrs, elementTransform, new DrawSvgEllipseItem
            {
                Bounds = bounds,
                Cx = GetLength(attrs, "cx", pctW) * sx + tx,
                Cy = GetLength(attrs, "cy", pctH) * sy + ty,
                Rx = GetLength(attrs, "rx", pctW) * sx,
                Ry = GetLength(attrs, "ry", pctH) * sy,
                Fill = ResolveFillWithoutPattern(attrs, BColor.Black),
                Stroke = GetPaint(attrs, "stroke", BColor.Empty),
                StrokeWidth = StrokeWidthOf(attrs, pctD, sx, sy, viewportScale),
            });
        }

        // <line ... />
        foreach (Match m in ParseLineRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var items = order.Open(m.Index);
            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);
            if (ClipRectFor(attrs, clipRects, bounds, sx, sy, tx, ty, elementTransform) is { } clip)
                order.ClipLast(clip);

            AddShape(items, bounds, attrs, elementTransform, new DrawSvgLineItem
            {
                Bounds = bounds,
                X1 = GetLength(attrs, "x1", pctW) * sx + tx,
                Y1 = GetLength(attrs, "y1", pctH) * sy + ty,
                X2 = GetLength(attrs, "x2", pctW) * sx + tx,
                Y2 = GetLength(attrs, "y2", pctH) * sy + ty,
                Stroke = GetPaint(attrs, "stroke", BColor.Black),
                StrokeWidth = StrokeWidthOf(attrs, pctD, sx, sy, viewportScale),
            });
        }

        foreach (Match m in ParsePolygonRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var items = order.Open(m.Index);
            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);
            if (ClipRectFor(attrs, clipRects, bounds, sx, sy, tx, ty, elementTransform) is { } clip)
                order.ClipLast(clip);

            AddShape(items, bounds, attrs, elementTransform, new DrawSvgPolygonItem
            {
                Bounds = bounds,
                Points = ParsePoints(attrs.GetValueOrDefault("points") ?? string.Empty, sx, sy, tx, ty),
                Fill = ResolveFillWithoutPattern(attrs, BColor.Black),
                Stroke = GetPaint(attrs, "stroke", BColor.Empty),
                StrokeWidth = StrokeWidthOf(attrs, pctD, sx, sy, viewportScale),
            });
        }

        foreach (Match m in ParsePolyLineRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var items = order.Open(m.Index);
            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);
            if (ClipRectFor(attrs, clipRects, bounds, sx, sy, tx, ty, elementTransform) is { } clip)
                order.ClipLast(clip);

            AddShape(items, bounds, attrs, elementTransform, new DrawSvgPolylineItem
            {
                Bounds = bounds,
                Points = ParsePoints(attrs.GetValueOrDefault("points") ?? string.Empty, sx, sy, tx, ty),
                Fill = ResolveFillWithoutPattern(attrs, BColor.Empty),
                Stroke = GetPaint(attrs, "stroke", BColor.Empty),
                StrokeWidth = StrokeWidthOf(attrs, pctD, sx, sy, viewportScale),
            });
        }

        // <path d="..." />
        foreach (Match m in PathElementRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var subPaths = SvgPathData.Flatten(attrs.GetValueOrDefault("d"));
            if (subPaths.Count == 0)
                continue;

            var items = order.Open(m.Index);
            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);
            if (ClipRectFor(attrs, clipRects, bounds, sx, sy, tx, ty, elementTransform) is { } pathClip)
                order.ClipLast(pathClip);

            var pathFill = ResolveFillWithoutPattern(attrs, BColor.Black);
            var pathStroke = GetPaint(attrs, "stroke", BColor.Empty);
            float pathStrokeWidth = StrokeWidthOf(attrs, pctD, sx, sy, viewportScale);

            var mapped = new List<List<PointF>>(subPaths.Count);
            foreach (var subPath in subPaths)
            {
                var points = new List<PointF>(subPath.Points.Count);
                foreach (var point in subPath.Points)
                    points.Add(new PointF(point.X * sx + tx, point.Y * sy + ty));
                mapped.Add(points);
            }

            // The whole path fills as one shape, so its subpaths go into a single polygon rather
            // than one each: the rasterizer's fill is an even-odd crossing test, which gives a
            // subpath enclosed by another the hole SVG's fill rules ask for, where separate opaque
            // polygons would paint over it. Filling each one alone turned the three overlapping
            // rings of conformance-checkers/html-svg/coords-viewattr-03-b into a solid blob.
            if (!pathFill.IsEmpty && pathFill.A > 0)
            {
                AddShape(items, bounds, attrs, elementTransform, new DrawSvgPolygonItem
                {
                    Bounds = bounds,
                    Points = StitchSubPaths(mapped),
                    Fill = pathFill,
                    Stroke = BColor.Empty,
                    StrokeWidth = 0,
                });
            }

            if (pathStroke.IsEmpty || pathStroke.A == 0 || pathStrokeWidth <= 0)
                continue;

            // The stroke, by contrast, is per subpath: it follows each one's own outline, and only
            // a subpath that Z closed is stroked all the way round — which is the difference
            // between the polygon item and the polyline item.
            for (int i = 0; i < mapped.Count; i++)
            {
                AddShape(items, bounds, attrs, elementTransform, subPaths[i].Closed
                    ? new DrawSvgPolygonItem
                    {
                        Bounds = bounds,
                        Points = mapped[i],
                        Fill = BColor.Empty,
                        Stroke = pathStroke,
                        StrokeWidth = pathStrokeWidth,
                    }
                    : new DrawSvgPolylineItem
                    {
                        Bounds = bounds,
                        Points = mapped[i],
                        Fill = BColor.Empty,
                        Stroke = pathStroke,
                        StrokeWidth = pathStrokeWidth,
                    });
            }
        }

        // <text ...>content</text>
        foreach (Match m in ParseTextRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);

            var textPathAttrs = ParseAttributes(m.Groups[2].Value);

            if (!textPathAttrs.TryGetValue("href", out var href) || !href.StartsWith('#'))
                continue;

            if (!pathStartsById.TryGetValue(href[1..], out var start))
                continue;

            float textPathSize = GetLength(attrs, "font-size", pctD, 16) * Math.Max(sx, sy);
            var textPathFont = ResolveFont(attrs, textPathSize);

            order.Open(m.Index).Add(new DrawSvgTextItem
            {
                Bounds = bounds,
                X = start.X * sx + tx,
                // The path's start point is on the baseline; the backend draws from the top of the
                // ascent box (see EmitTextElements).
                Y = start.Y * sy + ty - (float)AscentOf(textPathFont, textPathSize),
                FontSize = textPathSize,
                FontFamily = attrs.GetValueOrDefault("font-family") ?? "Arial",
                FontHandle = textPathFont,
                Fill = GetPaint(attrs, "fill", BColor.Black) is { IsEmpty: false } pathFill ? pathFill : BColor.Black,
                Text = FlattenTextContent(m.Groups[3].Value),
            });
        }

        EmitTextElements(svgXml, bounds, order, structure, sx, sy, tx, ty, pctW, pctH, pctD);
        EmitUseElements(svgXml, bounds, order, structure, sx, sy, tx, ty, pctW, pctH);
        EmitNestedViewports(svgXml, bounds, order, structure, sx, sy, tx, ty, pctW, pctH);

        order.Flush(output);
    }

    /// <summary>
    /// SVG 1.1 §7.8: the scale and translation that map a view box onto the viewport under a
    /// <c>preserveAspectRatio</c> value, as <c>(sx, sy, tx, ty)</c>.
    /// </summary>
    /// <remarks>
    /// Only <c>none</c> and the alignment/<c>meet</c>/<c>slice</c> forms are read; a value this
    /// cannot parse takes the initial <c>xMidYMid meet</c>, which is what the renderer applied
    /// unconditionally before.
    /// </remarks>
    private static (float Sx, float Sy, float Tx, float Ty) ResolveViewBoxMapping(
        string? preserveAspectRatio, RectangleF bounds, float vbX, float vbY, float vbW, float vbH)
    {
        var parts = (preserveAspectRatio ?? string.Empty)
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        // "defer" is a legacy prefix that only applies to <image>, and is ignored here.
        int first = parts.Length > 0 && parts[0].Equals("defer", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        string align = parts.Length > first ? parts[first] : "xMidYMid";
        bool slice = parts.Length > first + 1
            && parts[first + 1].Equals("slice", StringComparison.OrdinalIgnoreCase);

        float scaleX = bounds.Width / vbW;
        float scaleY = bounds.Height / vbH;

        if (align.Equals("none", StringComparison.OrdinalIgnoreCase))
            return (scaleX, scaleY, -vbX * scaleX, -vbY * scaleY);

        float scale = slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        float slackX = bounds.Width - vbW * scale;
        float slackY = bounds.Height - vbH * scale;

        float alignX = align.Contains("xMin", StringComparison.OrdinalIgnoreCase) ? 0f
            : align.Contains("xMax", StringComparison.OrdinalIgnoreCase) ? slackX
            : slackX / 2f;
        float alignY = align.Contains("YMin", StringComparison.Ordinal) ? 0f
            : align.Contains("YMax", StringComparison.Ordinal) ? slackY
            : slackY / 2f;

        return (scale, scale, -vbX * scale + alignX, -vbY * scale + alignY);
    }

    private static PointF? TryGetPathStart(string pathData)
    {
        var match = ParsePathRegEx().Match(pathData);
        if (!match.Success ||
            !float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            return null;
        }

        return new PointF(x, y);
    }

    private static List<PointF> ParsePoints(string value, float sx, float sy, float tx, float ty)
    {
        var points = new List<PointF>();
        var numbers = ParsePointRegex().Matches(value);
        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            if (!float.TryParse(numbers[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(numbers[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                continue;
            }

            points.Add(new PointF(x * sx + tx, y * sy + ty));
        }

        return points;
    }

    /// <summary>
    /// Scans one <c>&lt;svg&gt;</c> string for <c>&lt;clipPath&gt;</c> definitions and records each
    /// under its <c>id</c> in the document-wide <see cref="SvgClipPathTable"/>, so a CSS
    /// <c>clip-path: url(#id)</c> on an HTML element resolves to a <c>&lt;clipPath&gt;</c> declared
    /// in any <c>&lt;svg&gt;</c> subtree of the document.
    /// <para>
    /// Only the first shape child is taken. A <c>&lt;clipPath&gt;</c> may hold several children,
    /// whose union is the clip; the rasterizer pushes one clip region, so a multi-shape definition
    /// would need a union it cannot express. Taking the first is a narrowing of that union — never
    /// larger than the real clip — where taking all of them separately would intersect instead and
    /// erase nearly everything.
    /// </para>
    /// </summary>
    internal static void CollectClipPaths(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml) || svgXml.IndexOf("<clippath", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        foreach (Match cm in ClipPathBlockRegex().Matches(svgXml))
        {
            var clipAttrs = ParseAttributes(cm.Groups[1].Value);
            if (!clipAttrs.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
                continue;

            bool objectBoundingBox = clipAttrs.TryGetValue("clipPathUnits", out var units)
                && units.Trim().Equals("objectBoundingBox", StringComparison.OrdinalIgnoreCase);

            if (TryParseFirstClipShape(cm.Groups[2].Value, objectBoundingBox, out var shape))
                SvgClipPathTable.Add(id, shape);
        }
    }

    /// <summary>
    /// Finds whichever modelled shape element appears first in a <c>&lt;clipPath&gt;</c> body.
    /// Matching each element type separately and keeping the earliest hit preserves document order
    /// without needing a real parser, which is all the rest of this renderer has either.
    /// </summary>
    private static bool TryParseFirstClipShape(
        string body, bool objectBoundingBox, out SvgClipPathTable.ClipShape shape)
    {
        SvgClipPathTable.ClipShape best = null;
        int bestIndex = int.MaxValue;

        Consider(ParseRectRegex().Match(body), SvgClipPathTable.ClipShapeKind.Rect);
        Consider(ParseCircleRegex().Match(body), SvgClipPathTable.ClipShapeKind.Circle);
        Consider(ParseEllipseRegex().Match(body), SvgClipPathTable.ClipShapeKind.Ellipse);
        Consider(ParsePolygonRegex().Match(body), SvgClipPathTable.ClipShapeKind.Polygon);

        shape = best;
        return shape != null;

        void Consider(Match match, SvgClipPathTable.ClipShapeKind kind)
        {
            if (!match.Success || match.Index >= bestIndex)
                return;

            bestIndex = match.Index;
            best = new SvgClipPathTable.ClipShape(
                kind, ParseAttributes(match.Groups[1].Value), objectBoundingBox);
        }
    }

    /// <summary>
    /// Scans one <c>&lt;svg&gt;</c> string for <c>&lt;filter&gt;</c> definitions whose sole modelled
    /// primitive is an <c>&lt;feFlood&gt;</c>, and records each under its <c>id</c> in the document-wide
    /// <see cref="SvgFilterTable"/>. Called for every SVG-bearing fragment before painting so a
    /// <c>filter="url(#id)"</c> reference resolves even when the filter lives in another
    /// <c>&lt;svg&gt;</c> subtree.
    /// </summary>
    internal static void CollectFloodFilters(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml) || svgXml.IndexOf("<filter", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        foreach (Match fm in FilterBlockRegex().Matches(svgXml))
        {
            var filterAttrs = ParseAttributes(fm.Groups[1].Value);
            if (!filterAttrs.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
                continue;

            var body = fm.Groups[2].Value;
            var flood = FeFloodRegex().Match(body);
            if (!flood.Success)
                continue;

            // "A flood filter" means the flood is the *whole* filter. A flood that another
            // primitive then consumes describes a backdrop, not the result: registering
            // filters-blend-01-b's five feFlood+feBlend chains here replaced each thin blended
            // band with a solid flood-coloured rectangle 20% larger than the shape.
            if (FilterPrimitiveRegex().Matches(body).Count > 1)
                continue;

            SvgFilterTable.AddFlood(id, ParseFloodColor(ParseAttributes(flood.Groups[1].Value)));
        }
    }

    /// <summary>
    /// The colour an <c>&lt;feFlood&gt;</c> produces: its <c>flood-color</c> with
    /// <c>flood-opacity</c> folded into the alpha channel.
    /// </summary>
    /// <remarks>
    /// Takes the parsed attributes rather than the match, because the two regexes that find an
    /// <c>&lt;feFlood&gt;</c> put them in different groups — <see cref="FeFloodRegex"/> in group 1
    /// and <see cref="FilterPrimitiveRegex"/> in group 2, group 1 there being the element name.
    /// </remarks>
    private static BColor ParseFloodColor(Dictionary<string, string> floodAttrs)
    {
        var color = GetColor(floodAttrs, "flood-color", BColor.Black);

        if (floodAttrs.TryGetValue("flood-opacity", out var opStr)
            && float.TryParse(opStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var op))
        {
            color = BColor.FromArgb(
                (int)(Math.Clamp(op, 0f, 1f) * color.A), color.R, color.G, color.B);
        }

        return color;
    }

    /// <summary>
    /// Scans one <c>&lt;svg&gt;</c> string for <c>&lt;filter&gt;</c> definitions that only transform
    /// colour, and records each under its <c>id</c> in the document-wide <see cref="SvgFilterTable"/>.
    /// Runs alongside <see cref="CollectFloodFilters"/> — the two model disjoint filters, since a
    /// flood-only chain has no colour step and a colour chain has no flood.
    /// </summary>
    internal static void CollectColorFilters(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml) || svgXml.IndexOf("<filter", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        foreach (Match fm in FilterBlockRegex().Matches(svgXml))
        {
            var filterAttrs = ParseAttributes(fm.Groups[1].Value);
            if (!filterAttrs.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
                continue;

            if (TryParseColorFilterChain(fm.Groups[2].Value, filterAttrs) is { } filter)
                SvgFilterTable.AddColorFilter(id, filter);
        }
    }

    /// <summary>
    /// Reads a filter body as a straight chain of colour-only primitives, or returns <c>null</c> when
    /// it is anything else. See <see cref="SvgColorFilter"/> for why the modelled subset is this
    /// narrow; every bail-out below leaves the referencing shape rendering unfiltered, which is what
    /// it did before this existed.
    /// </summary>
    private static SvgColorFilter? TryParseColorFilterChain(
        string body, Dictionary<string, string> filterAttrs)
    {
        // The default colour space for filter operations is linearRGB, which this does not model.
        // Only an explicit sRGB filter is taken; anything else renders unfiltered rather than wrong.
        if (!filterAttrs.TryGetValue("color-interpolation-filters", out var space)
            || !space.Trim().Equals("sRGB", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var primitives = FilterPrimitiveRegex().Matches(body);
        if (primitives.Count == 0)
            return null;

        var steps = new List<SvgColorFilter.Step>(primitives.Count);
        string? previousResult = null;
        bool first = true;

        // The colours an feFlood in this filter produced, by its `result` name. A flood is a
        // constant over the whole filter region, so a later primitive that consumes one is
        // consuming a known colour rather than an image the model cannot represent.
        Dictionary<string, BColor>? floods = null;

        foreach (Match pm in primitives)
        {
            var name = pm.Groups[1].Value;
            var attrs = ParseAttributes(pm.Groups[2].Value);

            if (name.Equals("feFlood", StringComparison.OrdinalIgnoreCase))
            {
                // A flood ignores its `in` entirely, so it neither continues nor breaks the chain;
                // it contributes a named constant for a later feBlend to use as its backdrop.
                if (!attrs.TryGetValue("result", out var floodResult) || floodResult.Length == 0)
                    return null;

                floods ??= new Dictionary<string, BColor>(StringComparer.Ordinal);
                floods[floodResult] = ParseFloodColor(attrs);
                continue;
            }

            if (name.Equals("feBlend", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveBlendBackdrop(attrs, floods, previousResult, first, out var backdrop))
                    return null;

                string mode = (attrs.GetValueOrDefault("mode") ?? "normal").Trim().ToLowerInvariant();
                if (!SvgColorFilter.IsModelledBlendMode(mode))
                    return null;

                steps.Add(new SvgColorFilter.Step { BlendBackdrop = backdrop, BlendMode = mode });
                attrs.TryGetValue("result", out previousResult);
                first = false;
                continue;
            }

            // The chain must be straight: each primitive consumes the previous one's output (an
            // absent `in` means exactly that), and the first consumes the source graphic.
            if (!ConsumesPreviousResult(attrs, "in", previousResult, first)
                || (attrs.ContainsKey("in2") && !ConsumesPreviousResult(attrs, "in2", previousResult, first)))
            {
                return null;
            }

            if (name.Equals("feColorMatrix", StringComparison.OrdinalIgnoreCase))
            {
                // `matrix` is the default type; the shorthand types (saturate, hueRotate,
                // luminanceToAlpha) are not modelled.
                if (attrs.TryGetValue("type", out var type)
                    && !type.Trim().Equals("matrix", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                if (!attrs.TryGetValue("values", out var values)
                    || ParseFloatList(values) is not { Length: 20 } matrix)
                {
                    return null;
                }
                steps.Add(new SvgColorFilter.Step { Matrix = matrix });
            }
            else if (name.Equals("feComposite", StringComparison.OrdinalIgnoreCase))
            {
                // Only `arithmetic` is a pure colour operation; the Porter-Duff operators combine
                // two different inputs, which a single-colour model cannot represent.
                if (!attrs.TryGetValue("operator", out var op)
                    || !op.Trim().Equals("arithmetic", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                steps.Add(new SvgColorFilter.Step
                {
                    Arithmetic = (
                        GetFloat(attrs, "k1"), GetFloat(attrs, "k2"),
                        GetFloat(attrs, "k3"), GetFloat(attrs, "k4")),
                });
            }
            else
            {
                return null;
            }

            attrs.TryGetValue("result", out previousResult);
            first = false;
        }

        return new SvgColorFilter(steps);
    }

    /// <summary>
    /// Resolves an <c>&lt;feBlend&gt;</c>'s two inputs to "the running colour, over this constant
    /// backdrop".
    /// </summary>
    /// <remarks>
    /// One input must name an <c>&lt;feFlood&gt;</c> declared earlier in the same filter — the only
    /// second image a single-colour model can represent — and the other must be the running result.
    /// Either order is accepted: the blend is not commutative, but <c>in</c> is always the source
    /// and <c>in2</c> the backdrop, so the flood being named by <c>in</c> instead makes the flood
    /// the source and the running colour the backdrop.
    /// </remarks>
    private static bool TryResolveBlendBackdrop(
        Dictionary<string, string> attrs, Dictionary<string, BColor>? floods,
        string? previousResult, bool first, out BColor backdrop)
    {
        backdrop = default;
        if (floods == null)
            return false;

        string? input = attrs.GetValueOrDefault("in");
        string? input2 = attrs.GetValueOrDefault("in2");

        // in2 names the flood: the ordinary spelling, source over flood.
        if (input2 != null && floods.TryGetValue(input2, out backdrop))
            return ConsumesPreviousResult(attrs, "in", previousResult, first);

        // in names the flood instead. The running colour is then the backdrop, which this model
        // cannot express — it applies the blend to the running colour, not to the flood — so it is
        // declined rather than rendered the wrong way round.
        if (input != null && floods.ContainsKey(input))
            return false;

        return false;
    }

    /// <summary>
    /// Whether a primitive's <c>in</c>/<c>in2</c> names the previous primitive's result. An absent
    /// attribute means the previous result (or the source graphic for the first primitive), which is
    /// the spec default.
    /// </summary>
    private static bool ConsumesPreviousResult(
        Dictionary<string, string> attrs, string attribute, string? previousResult, bool first)
    {
        if (!attrs.TryGetValue(attribute, out var value) || string.IsNullOrWhiteSpace(value))
            return true;

        value = value.Trim();
        if (first)
            return value.Equals("SourceGraphic", StringComparison.OrdinalIgnoreCase);

        return previousResult != null && value.Equals(previousResult, StringComparison.Ordinal);
    }

    /// <summary>Parses a whitespace/comma separated list of numbers, or <c>null</c> if any is malformed.</summary>
    private static float[]? ParseFloatList(string value)
    {
        var parts = value.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                return null;
        }
        return result;
    }

    /// <summary>
    /// If <paramref name="attrs"/> carries a <c>filter="url(#id)"</c> that resolves to a modelled
    /// flood filter, returns its colour. Such a filter replaces the shape with a solid fill of that
    /// colour over the filter region.
    /// </summary>
    private static bool TryResolveFloodFilter(Dictionary<string, string> attrs, out BColor color)
    {
        color = default;
        if (!attrs.TryGetValue("filter", out var filterRef) || string.IsNullOrEmpty(filterRef))
            return false;
        var id = SvgFilterTable.ExtractUrlReferenceId(filterRef);
        return id != null && SvgFilterTable.TryGetFlood(id, out var flood) && (color = flood.Color).A > 0;
    }

    /// <summary>
    /// If <paramref name="attrs"/> carries a <c>filter="url(#id)"</c> that resolves to a modelled
    /// colour-transform filter, returns the colour the shape's solid <paramref name="fill"/> becomes.
    /// A stroked shape is not uniformly coloured, so it is left unfiltered.
    /// </summary>
    private static bool TryResolveColorFilter(
        Dictionary<string, string> attrs, BColor fill, BColor stroke, out BColor filtered)
    {
        filtered = default;
        if (stroke.A > 0
            || !attrs.TryGetValue("filter", out var filterRef)
            || string.IsNullOrEmpty(filterRef))
        {
            return false;
        }

        var id = SvgFilterTable.ExtractUrlReferenceId(filterRef);
        if (id == null || !SvgFilterTable.TryGetColorFilter(id, out var filter))
            return false;

        filtered = filter.Apply(fill);
        return true;
    }

    private static Dictionary<string, string> ParseAttributes(string attrStr)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in ParseAttrRegex().Matches(attrStr))
        {
            // Exactly one of the two quote styles matched; the other group is unset.
            var value = m.Groups["dq"];
            dict[m.Groups["name"].Value] = value.Success ? value.Value : m.Groups["sq"].Value;
        }
        return dict;
    }

    private static float GetFloat(Dictionary<string, string> attrs, string name, float defaultValue = 0)
    {
        if (attrs.TryGetValue(name, out var val) &&
            float.TryParse(val.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            return f;
        return defaultValue;
    }

    /// <summary>
    /// Reads a presentation property from the element's <c>style</c> declaration if it is there, and
    /// from the presentation attribute of the same name otherwise.
    /// </summary>
    /// <remarks>
    /// SVG 1.1 §6.4 ranks a <c>style</c> declaration above the matching presentation attribute, which
    /// is the order used here. Only the properties this renderer models are ever asked for, so this is
    /// a lookup rather than a cascade.
    /// </remarks>
    private static string? GetPresentationValue(Dictionary<string, string> attrs, string property)
    {
        if (attrs.TryGetValue("style", out var style) && !string.IsNullOrWhiteSpace(style))
        {
            foreach (var declaration in style.Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon <= 0)
                    continue;

                if (declaration.AsSpan(0, colon).Trim()
                        .Equals(property, StringComparison.OrdinalIgnoreCase))
                {
                    var value = declaration.AsSpan(colon + 1).Trim();
                    if (!value.IsEmpty)
                        return value.ToString();
                }
            }
        }

        return attrs.TryGetValue(property, out var attribute) ? attribute : null;
    }

    /// <summary>
    /// An SVG <c>&lt;alpha-value&gt;</c>: a number, or a percentage. Out-of-range values clamp rather
    /// than being rejected, which is what SVG 1.1 §4.4 asks for.
    /// </summary>
    private static float ParseAlpha(string? raw, float defaultValue = 1f)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        var value = raw.AsSpan().Trim();
        bool percent = value[^1] == '%';
        if (percent)
            value = value[..^1];

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            return defaultValue;

        return Math.Clamp(percent ? parsed / 100f : parsed, 0f, 1f);
    }

    /// <summary>
    /// Resolves a shape's <c>fill</c> or <c>stroke</c> to a colour with its
    /// <c>fill-opacity</c>/<c>stroke-opacity</c> folded into the alpha channel.
    /// </summary>
    /// <remarks>
    /// Folding the opacity into the colour rather than pushing an opacity layer is exact for a single
    /// shape, which is all this renderer draws: SVG 1.1 §14.5 applies <c>fill-opacity</c> to the fill
    /// operation itself, not to a group. It is <em>not</em> the same as the element's <c>opacity</c>
    /// property, which composites fill and stroke together and is deliberately not modelled here —
    /// with a translucent stroke over its own fill the two differ, and inventing the group behaviour
    /// would be wrong in the commoner case.
    /// </remarks>
    private static BColor GetPaint(Dictionary<string, string> attrs, string property, BColor defaultColor)
    {
        // An absent paint is "no paint", not the default colour — the default is only the fallback for
        // a value that is present and unparseable. Returning the default here instead would paint every
        // fill-less shape solid black; it cost 104 reftests when this was written the other way round.
        var raw = GetPresentationValue(attrs, property);
        BColor color = raw is null
            ? BColor.Empty
            : ParseColorValue(raw, defaultColor);

        if (color.IsEmpty || color.A == 0)
            return color;

        float opacity = ParseAlpha(GetPresentationValue(attrs, property + "-opacity"));
        if (opacity >= 1f)
            return color;

        return BColor.FromArgb((int)Math.Round(color.A * opacity), color.R, color.G, color.B);
    }

    /// <summary>
    /// Adds one shape, wrapped in a blend layer when the element carries a non-normal
    /// <c>mix-blend-mode</c>.
    /// </summary>
    /// <remarks>
    /// The layer pair is the same one the CSS <c>mix-blend-mode</c> path emits
    /// (<c>PaintWalker.Stacking</c>), so the raster backend already knows how to composite it and
    /// already reports which modes it can keep on the raster path. Emitting nothing for
    /// <c>normal</c> keeps the display list byte-identical for the overwhelming majority of shapes,
    /// which is what makes this safe to apply at every shape site.
    /// </remarks>
    private static void AddShape(
        List<DisplayItem> items, RectangleF bounds, Dictionary<string, string> attrs, DisplayItem shape)
    {
        AddShape(items, bounds, attrs, SvgTransform.Identity, shape);
    }

    /// <summary>
    /// <see cref="AddShape(List{DisplayItem}, RectangleF, Dictionary{string, string}, DisplayItem)"/>
    /// with the element's user-space <c>transform</c> baked into the shape's geometry.
    /// </summary>
    /// <remarks>
    /// Baked rather than pushed as a transform layer for the reason
    /// <see cref="SvgItemTransformer"/> records: the raster canvas takes only translations and
    /// uniform scales, and a layer it cannot take routes the enclosed drawing to a compat backend
    /// the image renderer stubs out, where it disappears.
    /// </remarks>
    private static void AddShape(
        List<DisplayItem> items, RectangleF bounds, Dictionary<string, string> attrs,
        SvgTransform pageTransform, DisplayItem shape)
    {
        if (!pageTransform.IsIdentity)
            shape = SvgItemTransformer.MapItem(shape, pageTransform);

        string? mode = GetPresentationValue(attrs, "mix-blend-mode");
        bool blended = !string.IsNullOrWhiteSpace(mode)
            && !mode.Equals("normal", StringComparison.OrdinalIgnoreCase);

        // SVG 1.1 §14.5 `opacity`: unlike `fill-opacity`, which GetPaint folds into the paint
        // colour, this one composites the element as a whole — fill and stroke together — and so
        // needs a real layer. Nothing emitted one, so `opacity` was dropped outright on every
        // shape: `<rect fill="blue" opacity="0.5">` painted solid blue. OpacityItem is the layer
        // the CSS paint path already pushes for the same property (PaintWalker.Stacking), so the
        // raster backend composites this exactly as it does an HTML element's opacity.
        // The element's own opacity, times whatever its ancestors contribute: a `<g opacity>` has
        // no group layer to hang its value on here, so SvgStructure carries the ancestors' product
        // down to the leaves for this multiplication (see SvgStructure.AncestorOpacityOf).
        float opacity = ParseAlpha(GetPresentationValue(attrs, "opacity"))
            * SvgStructure.AncestorOpacityOf(attrs);
        bool faded = opacity < 1f;

        if (faded)
            items.Add(new OpacityItem { Bounds = bounds, Opacity = opacity });

        if (blended)
            items.Add(new BlendModeItem { Bounds = bounds, Mode = mode! });

        items.Add(shape);

        if (blended)
            items.Add(new RestoreBlendModeItem { Bounds = bounds });

        if (faded)
            items.Add(new RestoreOpacityItem { Bounds = bounds });
    }

    /// <summary>
    /// Reads a geometric attribute as a length in user units, resolving a percentage against
    /// <paramref name="percentBasis"/> — the viewport extent for that length's axis (SVG 1.1 §7.10).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetFloat"/> deliberately: <c>k1</c>–<c>k4</c> on an
    /// <c>feComposite</c> are numbers rather than lengths, and a percentage there is not a length
    /// either, so they must keep going through the plain reader.
    /// <para>
    /// Before this existed every percentage fell through <c>float.TryParse</c> to the default — zero
    /// for a coordinate or an extent — so an SVG built on percentages (<c>&lt;rect width="100%"&gt;</c>)
    /// drew nothing at all. That is invisible for inline markup, which is nearly always authored in
    /// user units, and total for an SVG used as an image, where percentage sizing is the norm: it is
    /// what the <c>css-backgrounds/background-size/vector</c> family is built on. See issue #1627.
    /// </para>
    /// </remarks>
    private static float GetLength(
        Dictionary<string, string> attrs, string name, float percentBasis, float defaultValue = 0)
    {
        if (!attrs.TryGetValue(name, out var val))
            return defaultValue;

        var value = val.AsSpan().Trim();
        if (value.IsEmpty)
            return defaultValue;

        if (value[^1] == '%')
        {
            return float.TryParse(
                value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                ? percentBasis * percent / 100f
                : defaultValue;
        }

        return float.TryParse(
            val.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
            ? f
            : defaultValue;
    }

    /// <summary>
    /// The rectangles the document's <c>&lt;clipPath&gt;</c> definitions clip to, by <c>id</c>, in
    /// user units — the ones this renderer can express, which is a single <c>&lt;rect&gt;</c> child
    /// in <c>userSpaceOnUse</c> units.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CollectClipPaths"/>, which publishes to the document-wide table a
    /// CSS <c>clip-path</c> on an <em>HTML</em> element reads. This is the SVG side: a
    /// <c>clip-path</c> attribute on a shape, which nothing honoured — so the black 200×200 rect of
    /// <c>conformance-checkers/html-svg/masking-path-14-f</c> covered the panel it should have been
    /// clipped to a corner of.
    /// </remarks>
    private static Dictionary<string, RectangleF> CollectClipRects(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml)
            || svgXml.IndexOf("<clippath", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        Dictionary<string, RectangleF> rects = null;
        foreach (Match cm in ClipPathBlockRegex().Matches(svgXml))
        {
            var clipAttrs = ParseAttributes(cm.Groups[1].Value);
            if (!clipAttrs.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
                continue;

            // objectBoundingBox units would need each referencing shape's own box, which is not
            // known here; those keep painting unclipped rather than being clipped to the wrong box.
            if (clipAttrs.TryGetValue("clipPathUnits", out var units)
                && units.Trim().Equals("objectBoundingBox", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rectMatch = ParseRectRegex().Match(cm.Groups[2].Value);
            if (!rectMatch.Success)
                continue;

            var shapeAttrs = ParseAttributes(rectMatch.Groups[1].Value);
            float width = GetLength(shapeAttrs, "width", 0);
            float height = GetLength(shapeAttrs, "height", 0);
            if (width <= 0 || height <= 0)
                continue;

            rects ??= new Dictionary<string, RectangleF>(StringComparer.Ordinal);
            rects[id] = new RectangleF(
                GetLength(shapeAttrs, "x", 0), GetLength(shapeAttrs, "y", 0), width, height);
        }

        return rects;
    }

    /// <summary>
    /// The page-space region an element's <c>clip-path="url(#id)"</c> narrows its drawing to, or
    /// <see langword="null"/> when it carries none or the reference cannot be expressed as a rect.
    /// </summary>
    private static RectangleF? ClipRectFor(
        Dictionary<string, string> attrs, Dictionary<string, RectangleF> clipRects,
        RectangleF bounds, float sx, float sy, float tx, float ty, SvgTransform elementTransform)
    {
        if (clipRects == null)
            return null;

        string clipPath = GetPresentationValue(attrs, "clip-path");
        if (string.IsNullOrWhiteSpace(clipPath)
            || !TryParsePaintReference(clipPath, out var id, out _)
            || !clipRects.TryGetValue(id, out var rect))
        {
            return null;
        }

        return elementTransform.MapBounds(new RectangleF(
            bounds.X + rect.X * sx + tx, bounds.Y + rect.Y * sy + ty,
            rect.Width * sx, rect.Height * sy));
    }

    /// <summary>
    /// Joins a path's subpaths into the single closed ring the polygon item takes, so an even-odd
    /// fill over it is the fill of the whole path.
    /// </summary>
    /// <remarks>
    /// Each subpath is closed back to its own start and then followed by a return to the first
    /// subpath's start. That makes every bridge edge between subpaths appear twice, once in each
    /// direction, so its crossings cancel and it contributes nothing to the fill — the standard way
    /// to express a multi-contour region as one ring. Chaining the subpaths end to end instead
    /// would leave a closing edge that does not retrace any bridge, and it would toggle a spurious
    /// wedge between them.
    /// </remarks>
    private static List<PointF> StitchSubPaths(List<List<PointF>> subPaths)
    {
        if (subPaths.Count == 1)
            return subPaths[0];

        var anchor = subPaths[0][0];
        var stitched = new List<PointF>();

        foreach (var subPath in subPaths)
        {
            stitched.AddRange(subPath);
            stitched.Add(subPath[0]);
            stitched.Add(anchor);
        }

        return stitched;
    }

    /// <summary>
    /// A shape's stroke width in page pixels: the declared length taken through the user-unit scale,
    /// unless <c>vector-effect: non-scaling-stroke</c> holds it at its declared width.
    /// </summary>
    /// <remarks>
    /// SVG 2 §14.2 defines <c>non-scaling-stroke</c> as stroking in the viewport's coordinate system
    /// rather than the element's, so the user-space scale must not be applied. It only became visible
    /// once <c>&lt;path&gt;</c> painted at all: <c>svg/painting/reftests/non-scaling-stroke-precision-loss</c>
    /// draws a hairline under a view box 0.375 units tall over 344 pixels, and scaling its width by
    /// that 917× factor turns the hairline into a band across the whole canvas.
    /// </remarks>
    private static float StrokeWidthOf(
        Dictionary<string, string> attrs, float pctD, float sx, float sy, float viewportScale)
    {
        float declared = GetLength(attrs, "stroke-width", pctD, 1);
        string effect = GetPresentationValue(attrs, "vector-effect");
        bool nonScaling = effect != null
            && effect.Trim().Equals("non-scaling-stroke", StringComparison.OrdinalIgnoreCase);

        // "Non-scaling" means the view box does not scale it, not that nothing does: the stroke is
        // drawn in the viewport's coordinate system, so it still takes that viewport's own scale.
        // Dropping it too left non-scaling-stroke-008's 25-unit stroke at half its width, because
        // the zoom that doubles the viewport applies to the stroke as well.
        return nonScaling ? declared * viewportScale : declared * Math.Max(sx, sy);
    }

    private static BColor GetColor(Dictionary<string, string> attrs, string name, BColor defaultColor)
    {
        if (!attrs.TryGetValue(name, out var val))
            return BColor.Empty;

        return ParseColorValue(val, defaultColor);
    }

    /// <summary>
    /// The colour the root <c>&lt;svg&gt;</c> element declares as its background, if any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CSS Backgrounds §2.11.2 makes the root element's background the canvas's. For an SVG document
    /// used <em>as an image</em> that canvas is the destination box, and nothing painted it — a file
    /// opening <c>&lt;svg style="background: black"&gt;</c> came back transparent, so a document
    /// drawing tinted shapes over a dark ground rendered them on white instead.
    /// </para>
    /// <para>
    /// Only <see cref="SvgImageRaster"/> asks. An <em>inline</em> <c>&lt;svg&gt;</c> is an ordinary
    /// element whose CSS box already paints its background, so painting it here as well would double
    /// a translucent colour over itself; an image has no box to have done it.
    /// </para>
    /// <para>
    /// <c>background</c> is a shorthand, so its colour is taken as the last component that parses as
    /// one — which covers <c>background: green</c>, <c>background: #001</c> and
    /// <c>background: url(x) navy</c> alike. <c>background-color</c> is preferred when both are
    /// present, matching the cascade's own order for a longhand written after the shorthand.
    /// </para>
    /// </remarks>
    internal static bool TryGetRootBackgroundColor(string svgXml, out BColor color)
    {
        color = BColor.Empty;
        if (string.IsNullOrEmpty(svgXml))
            return false;

        var svgMatch = ParseRegex().Match(svgXml);
        if (!svgMatch.Success)
            return false;

        var attrs = ParseAttributes(svgMatch.Groups[1].Value);

        var longhand = GetPresentationValue(attrs, "background-color");
        if (!string.IsNullOrWhiteSpace(longhand))
        {
            color = ParseColorValue(longhand.Trim(), BColor.Empty);
            return !color.IsEmpty && color.A > 0;
        }

        var shorthand = GetPresentationValue(attrs, "background");
        if (string.IsNullOrWhiteSpace(shorthand))
            return false;

        foreach (var token in shorthand.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parsed = ParseColorValue(token.Trim(), BColor.Empty);
            if (!parsed.IsEmpty)
                color = parsed;
        }

        return !color.IsEmpty && color.A > 0;
    }

    /// <summary>Parses one already-resolved colour value; <c>none</c> and empty are "no paint".</summary>
    private static BColor ParseColorValue(string val, BColor defaultColor)
    {
        if (string.IsNullOrEmpty(val) || val == "none")
            return BColor.Empty;

        // rgba(r, g, b, a)
        if (val.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(')'))
        {
            string inner = val[5..^1];
            var parts = inner.Split(',');
            if (parts.Length == 4
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b)
                && float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
            {
                return BColor.FromArgb(
                    (int)(Math.Clamp(a, 0f, 1f) * 255),
                    Math.Clamp(r, 0, 255),
                    Math.Clamp(g, 0, 255),
                    Math.Clamp(b, 0, 255));
            }
        }

        // rgb(r, g, b)
        if (val.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && val.EndsWith(')'))
        {
            string inner = val[4..^1];
            var parts = inner.Split(',');
            if (parts.Length == 3
                && int.TryParse(parts[0].Trim(), out int r)
                && int.TryParse(parts[1].Trim(), out int g)
                && int.TryParse(parts[2].Trim(), out int b))
            {
                return BColor.FromArgb(
                    255,
                    Math.Clamp(r, 0, 255),
                    Math.Clamp(g, 0, 255),
                    Math.Clamp(b, 0, 255));
            }
        }

        // Handle hex colors (#rgb, #rgba, #rrggbb, #rrggbbaa)
        if (val.StartsWith('#'))
            return TryParseHexColor(val, out var hexColor) ? hexColor : defaultColor;

        // Handle named colors
        var named = BColor.FromName(val);
        if (!named.IsEmpty)
            return named;

        // A CSS system color (CSS Color 4 §6) is a <color> keyword like every other, and SVG paint
        // takes any <color>. The named-color table does not know them, so `fill="Window"` used to
        // fall through to the caller's default — solid black — and a page built out of them painted
        // as one black rectangle. Consulted after the named lookup so the two names CSS and X11
        // share keep their named-color meaning.
        if (Broiler.CSS.CssSystemColors.TryResolve(val, out var system))
            return BColor.FromArgb(system.Alpha, system.Red, system.Green, system.Blue);

        // SVG paint takes any CSS <color>, which since Color 4 includes oklch()/lab()/color()/…
        // None of the branches above reads one, and the fall-through below is the caller's default
        // — solid black for a fill — so an SVG styled in a modern colour space painted black rather
        // than the colour it names.
        if (CssColor4.TryParse(val, out var color4))
        {
            return BColor.FromArgb(
                (int)Math.Round(Math.Clamp(color4.A, 0, 1) * 255),
                (int)Math.Round(Math.Clamp(color4.R, 0, 255)),
                (int)Math.Round(Math.Clamp(color4.G, 0, 255)),
                (int)Math.Round(Math.Clamp(color4.B, 0, 255)));
        }

        return defaultColor;
    }

    private static bool TryParseHexColor(string val, out BColor color)
    {
        color = default;
        string hex = val.TrimStart('#');

        // Expand shorthand #rgb / #rgba to the full 6/8-digit form.
        if (hex.Length == 3 || hex.Length == 4)
        {
            string expanded = "";
            foreach (char ch in hex)
                expanded += new string(ch, 2);
            hex = expanded;
        }

        if (hex.Length != 6 && hex.Length != 8)
            return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return false;

        color = hex.Length == 6
            ? new BColor((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF))
            : new BColor((byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
        return true;
    }

    [GeneratedRegex(@"<svg\s+([^>]*?)\/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParseRegex();
    
    [GeneratedRegex(@"<path\s+([^/>]*)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParseSvgRegex();

    /// <summary>
    /// A <c>&lt;path&gt;</c> element. Quote-aware, unlike <see cref="ParseSvgRegex"/>, which stops
    /// at the first <c>/</c> — and so misses any path whose <c>d</c> holds one, as an
    /// <c>a</c> (arc) command written <c>a5,5 0 0 1 …</c> does not but a <c>url(…)</c> paint does.
    /// </summary>
    [GeneratedRegex(@"<path\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex PathElementRegex();

    /// <summary>
    /// The shape elements, matched quote-aware for the same reason <see cref="PathElementRegex"/>
    /// is: <c>[^/&gt;]*</c> excludes <c>/</c>, so an element with a slash anywhere in <em>any</em>
    /// attribute value never matched and was never drawn — <c>requiredFeatures="http://…"</c>,
    /// <c>fill="url(support/resources.svg#g)"</c>, <c>filter="url(support/hueRotate.svg#f)"</c>.
    /// Nothing reported it, because an element that does not match is not an error. The six were
    /// left behind when <c>&lt;path&gt;</c> was fixed.
    /// </summary>
    [GeneratedRegex(@"<rect\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParseRectRegex();

    [GeneratedRegex(@"<circle\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParseCircleRegex();

    [GeneratedRegex(@"<line\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParseLineRegex();

    [GeneratedRegex(@"<ellipse\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParseEllipseRegex();

    [GeneratedRegex(@"<polygon\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParsePolygonRegex();

    [GeneratedRegex(@"<polyline\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ParsePolyLineRegex();

    [GeneratedRegex(@"<text\s+([^>]*)>\s*<textpath\s+([^>]*)>(.*?)</textpath>\s*</text>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ParseTextRegex();

    [GeneratedRegex("<.*?>")]
    private static partial Regex DrawSvgTextRegex();

    [GeneratedRegex(@"M\s*(?<x>-?\d*\.?\d+)\s*,?\s*(?<y>-?\d*\.?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ParsePathRegEx();

    [GeneratedRegex(@"<\s*textpath\b", RegexOptions.IgnoreCase)]
    private static partial Regex ParseTextPathRegex();

    [GeneratedRegex(@"<filter\b([^>]*)>(.*?)</filter>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FilterBlockRegex();

    [GeneratedRegex(@"<clipPath\b([^>]*)>(.*?)</clipPath>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ClipPathBlockRegex();

    [GeneratedRegex(@"<feflood\b([^>]*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex FeFloodRegex();

    // Every filter primitive, in document order: the element name and its attribute text. Matches
    // both the self-closing and the open/close spellings (the closing tag is left to the next
    // iteration to skip, since `</feColorMatrix>` does not start with `<fe` + a name character).
    [GeneratedRegex(@"<(fe[A-Za-z]+)\b([^>]*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex FilterPrimitiveRegex();

    /// <summary>
    /// One attribute of an element's tag: a name, then a value in <em>either</em> quote style.
    /// </summary>
    /// <remarks>
    /// XML gives the two quote styles equal standing, and this matched double quotes only — so an
    /// element written <c>&lt;rect x='0' width='100'&gt;</c> parsed as an element with **no attributes
    /// at all** and drew nothing, silently. 101 documents under the directories the SVG sweep covers
    /// are written that way.
    /// <para>
    /// The alternation is deliberate over a backreference with a lazy body: a negated class is bounded
    /// by construction and lets the *other* quote appear inside a value (<c>title='He said "hi"'</c>),
    /// which is legal and which a <c>[^"']*</c> body would reject.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"(?<name>[\w\-]+)\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)')")]
    private static partial Regex ParseAttrRegex();

    [GeneratedRegex(@"-?\d*\.?\d+(?:[eE][+-]?\d+)?")]
    private static partial Regex ParsePointRegex();
}
