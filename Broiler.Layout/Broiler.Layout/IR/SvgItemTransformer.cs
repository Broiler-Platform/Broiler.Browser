#nullable disable
using System.Drawing;

namespace Broiler.Layout.IR;

/// <summary>
/// Rewrites already-positioned SVG display items through an affine transform, so a transformed
/// subtree can be drawn without asking the backend for a transform layer.
/// </summary>
/// <remarks>
/// <para>
/// The raster canvas takes only translations and uniform scales; anything else routes the whole
/// layer to a compat backend the image renderer stubs out, and the content disappears. Baking the
/// transform into the geometry avoids the layer entirely — and a rotated rectangle is still exactly
/// expressible, as the polygon primitive the backend already draws.
/// </para>
/// <para>
/// A rotated ellipse is not (the primitive has axis-aligned radii), so under a rotation those are
/// left in place rather than drawn somewhere wrong; that is the one shape this cannot carry.
/// </para>
/// </remarks>
internal static class SvgItemTransformer
{
    /// <summary>Maps every item it can express, in order, preserving the ones it cannot.</summary>
    public static List<DisplayItem> Map(List<DisplayItem> items, SvgTransform transform)
    {
        var mapped = new List<DisplayItem>(items.Count);
        foreach (var item in items)
            mapped.Add(MapItem(item, transform));

        return mapped;
    }

    /// <summary>Maps one item, returning it unchanged when the transform cannot be expressed on it.</summary>
    public static DisplayItem MapItem(DisplayItem item, SvgTransform transform)
    {
        switch (item)
        {
            case DrawSvgRectItem rect:
                return MapRect(rect, transform);

            case DrawSvgPolygonItem polygon:
                return new DrawSvgPolygonItem
                {
                    Bounds = polygon.Bounds,
                    Points = MapPoints(polygon.Bounds, polygon.Points, transform),
                    Fill = polygon.Fill,
                    Stroke = polygon.Stroke,
                    StrokeWidth = polygon.StrokeWidth,
                };

            case DrawSvgPolylineItem polyline:
                return new DrawSvgPolylineItem
                {
                    Bounds = polyline.Bounds,
                    Points = MapPoints(polyline.Bounds, polyline.Points, transform),
                    Fill = polyline.Fill,
                    Stroke = polyline.Stroke,
                    StrokeWidth = polyline.StrokeWidth,
                };

            case DrawSvgLineItem line:
            {
                var start = MapLocal(line.Bounds, line.X1, line.Y1, transform);
                var end = MapLocal(line.Bounds, line.X2, line.Y2, transform);
                return new DrawSvgLineItem
                {
                    Bounds = line.Bounds,
                    X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
                    Stroke = line.Stroke,
                    StrokeWidth = line.StrokeWidth,
                };
            }

            case DrawSvgEllipseItem ellipse when transform.IsAxisAligned:
            {
                var centre = MapLocal(ellipse.Bounds, ellipse.Cx, ellipse.Cy, transform);
                return new DrawSvgEllipseItem
                {
                    Bounds = ellipse.Bounds,
                    Cx = centre.X, Cy = centre.Y,
                    Rx = ellipse.Rx * Math.Abs(transform.A),
                    Ry = ellipse.Ry * Math.Abs(transform.D),
                    Fill = ellipse.Fill,
                    Stroke = ellipse.Stroke,
                    StrokeWidth = ellipse.StrokeWidth,
                };
            }

            case ClipItem clip when transform.IsAxisAligned:
            {
                // A nested pattern inside this one clips its own tiling; that clip has to move with
                // the tile. Under a rotation the mapped box is not a rectangle, so it is left alone
                // rather than narrowed to the wrong one.
                var mapped = transform.MapBounds(clip.ClipRect);
                return new ClipItem
                {
                    Bounds = mapped,
                    ClipRect = mapped,
                    Polygon = clip.Polygon,
                    CornerNw = clip.CornerNw, CornerNwY = clip.CornerNwY,
                    CornerNe = clip.CornerNe, CornerNeY = clip.CornerNeY,
                    CornerSe = clip.CornerSe, CornerSeY = clip.CornerSeY,
                    CornerSw = clip.CornerSw, CornerSwY = clip.CornerSwY,
                };
            }

            case DrawSvgTextItem text:
            {
                var origin = MapLocal(text.Bounds, text.X, text.Y, transform);
                return new DrawSvgTextItem
                {
                    Bounds = text.Bounds,
                    X = origin.X, Y = origin.Y,
                    FontSize = text.FontSize,
                    FontFamily = text.FontFamily,
                    FontHandle = text.FontHandle,
                    Fill = text.Fill,
                    Text = text.Text,
                };
            }

            default:
                return item;
        }
    }

    /// <summary>
    /// A rectangle stays a rectangle under an axis-aligned transform and becomes a quadrilateral
    /// under anything else — the case a rotated <c>patternTransform</c> produces.
    /// </summary>
    private static DisplayItem MapRect(DrawSvgRectItem rect, SvgTransform transform)
    {
        if (transform.IsAxisAligned)
        {
            var topLeft = MapLocal(rect.Bounds, rect.X, rect.Y, transform);
            var bottomRight = MapLocal(rect.Bounds, rect.X + rect.Width, rect.Y + rect.Height, transform);
            return new DrawSvgRectItem
            {
                Bounds = rect.Bounds,
                X = Math.Min(topLeft.X, bottomRight.X),
                Y = Math.Min(topLeft.Y, bottomRight.Y),
                Width = Math.Abs(bottomRight.X - topLeft.X),
                Height = Math.Abs(bottomRight.Y - topLeft.Y),
                Fill = rect.Fill,
                Stroke = rect.Stroke,
                StrokeWidth = rect.StrokeWidth,
            };
        }

        return new DrawSvgPolygonItem
        {
            Bounds = rect.Bounds,
            Points =
            [
                MapLocal(rect.Bounds, rect.X, rect.Y, transform),
                MapLocal(rect.Bounds, rect.X + rect.Width, rect.Y, transform),
                MapLocal(rect.Bounds, rect.X + rect.Width, rect.Y + rect.Height, transform),
                MapLocal(rect.Bounds, rect.X, rect.Y + rect.Height, transform),
            ],
            Fill = rect.Fill,
            Stroke = rect.Stroke,
            StrokeWidth = rect.StrokeWidth,
        };
    }

    private static List<PointF> MapPoints(
        RectangleF bounds, IReadOnlyList<PointF> points, SvgTransform transform)
    {
        if (points == null)
            return null;

        var mapped = new List<PointF>(points.Count);
        foreach (var point in points)
            mapped.Add(MapLocal(bounds, point.X, point.Y, transform));

        return mapped;
    }

    /// <summary>
    /// Maps a coordinate held relative to an item's <c>Bounds</c> origin. The transform is in page
    /// space, so the point is lifted to page space, mapped, and returned to the item's own frame.
    /// </summary>
    private static PointF MapLocal(RectangleF bounds, float x, float y, SvgTransform transform)
    {
        var page = transform.Map(bounds.X + x, bounds.Y + y);
        return new PointF(page.X - bounds.X, page.Y - bounds.Y);
    }
}
