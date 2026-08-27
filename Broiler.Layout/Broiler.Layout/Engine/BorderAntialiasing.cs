using System.Drawing;

namespace Broiler.Layout.Engine;

/// <summary>
/// Anti-aliasing for the diagonal a border's corner miter cuts across, and the pixel-coverage rule
/// behind it.
/// </summary>
/// <remarks>
/// <para>
/// A border side is painted as a trapezoid whose slanted ends are the miters it shares with its
/// neighbours (CSS 2.1 §8.5.4 — "the border is divided at the corners by a straight line"). Filled
/// by testing each pixel's centre, that diagonal comes out as a staircase: a 12px border with a red
/// top and an orange left steps orange one pixel further into red on each row, where a browser lays
/// one blended pixel per row along it. The staircase is the whole of the difference — the sides
/// themselves are axis-aligned and land on pixel boundaries either way.
/// </para>
/// <para>
/// <b>Coverage, not a special case for 45°.</b> The miter is only diagonal when the two sides are
/// the same width; a 12px top against a 4px left slopes one-in-three, and Chromium shades three
/// pixels per step accordingly. So the rule is the area of the pixel the trapezoid actually covers,
/// which reproduces both.
/// </para>
/// <para>
/// <b>Opt-in, because anti-aliasing shared edges conflates.</b> Two trapezoids meeting along a
/// miter each cover about half of the pixels on it; blended independently onto the page they leave
/// the background showing through the seam. The border painter avoids that by filling the corner
/// square with the first side's colour before either is drawn, so the second blends over an already
/// opaque corner — but nothing else that fills a polygon does, which is why this is a lever the
/// border path pins rather than something the rasteriser does for every shape.
/// </para>
/// <para>
/// Thread-static and scope-restoring, like the engine's other render levers
/// (<see cref="CanvasBackdrop.Current"/>, <see cref="EmbeddedCanvas.EmbedderColorScheme"/>): a
/// display list is replayed synchronously on the thread that owns the surface, and a tiled replay
/// gives each tile its own thread and its own scope.
/// </para>
/// </remarks>
internal static class BorderAntialiasing
{
    /// <summary>
    /// Whether the polygon being filled on this thread is a border side whose slanted ends should
    /// be anti-aliased. <see langword="false"/> by default, so every other fill is unchanged.
    /// </summary>
    [System.ThreadStatic]
    public static bool Active;

    /// <summary>
    /// Turns anti-aliasing on for the lifetime of the returned scope and restores the previous
    /// value when it is disposed.
    /// </summary>
    public static System.IDisposable Pin()
    {
        var scope = new Scope(Active);
        Active = true;
        return scope;
    }

    /// <summary>
    /// Half a pixel's diagonal. A pixel centre further inside every edge than this cannot be
    /// clipped by any of them, and one further outside any single edge cannot be touched at all —
    /// so the exact area is only computed for the handful of pixels a miter actually crosses.
    /// </summary>
    private const float HalfDiagonal = 0.7072f;

    /// <summary>
    /// The fraction of the pixel at (<paramref name="pixelX"/>, <paramref name="pixelY"/>) covered
    /// by <paramref name="polygon"/>, which must be convex — every border side is a trapezoid.
    /// Returns 0 or 1 for the pixels that are wholly out or wholly in.
    /// </summary>
    public static float Coverage(PointF[] polygon, int pixelX, int pixelY)
    {
        if (polygon is null || polygon.Length < 3)
            return 0f;

        // Orient the polygon so "inside" is consistently the positive side of every edge. The
        // shoelace sign says which way it winds; screen space has y down, so the test is written
        // against the sign rather than against a name that would mean the opposite on paper.
        bool invert = SignedArea(polygon) > 0;

        float centreX = pixelX + 0.5f, centreY = pixelY + 0.5f;
        var centre = new PointF(centreX, centreY);
        float nearest = float.PositiveInfinity;
        bool anySlanted = false;

        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float length = (float)System.Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0f)
                continue;

            // An axis-aligned edge is decided by the pixel centre, exactly as it always was. Only
            // the mitres are blended: a border's own edges are straight lines the layout puts where
            // it puts them, and feathering those turns a hairline sitting on a half-pixel into two
            // grey rows instead of one solid one — which is not what a browser draws, and is what a
            // page full of 1px form-control borders would look like.
            if (dx == 0f || dy == 0f)
            {
                if (Side(a, b, centre, invert) < 0f)
                    return 0f;
                continue;
            }

            anySlanted = true;

            // Signed distance from the centre to this edge, positive inside.
            float side = ((centreX - a.X) * dy - (centreY - a.Y) * dx) / length;
            if (invert)
                side = -side;

            if (side < -HalfDiagonal)
                return 0f;              // wholly outside this edge, so outside the polygon

            nearest = System.Math.Min(nearest, side);
        }

        if (!anySlanted || nearest > HalfDiagonal)
            return 1f;                  // clear of every mitre, so wholly inside

        return ClippedArea(polygon, invert, pixelX, pixelY);
    }

    /// <summary>
    /// The area of the unit pixel square left after clipping it against every edge of the polygon
    /// (Sutherland–Hodgman), which for a convex polygon is exactly the covered area.
    /// </summary>
    private static float ClippedArea(PointF[] polygon, bool invert, int pixelX, int pixelY)
    {
        // The pixel square, which the clip whittles down edge by edge.
        var subject = new PointF[16];
        int count = 4;
        subject[0] = new PointF(pixelX, pixelY);
        subject[1] = new PointF(pixelX + 1, pixelY);
        subject[2] = new PointF(pixelX + 1, pixelY + 1);
        subject[3] = new PointF(pixelX, pixelY + 1);

        var clipped = new PointF[16];

        for (int i = 0; i < polygon.Length && count > 0; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            // Only the mitres clip: the axis-aligned edges were already decided by the centre.
            if ((a.X == b.X && a.Y == b.Y) || a.X == b.X || a.Y == b.Y)
                continue;

            int written = 0;
            for (int j = 0; j < count; j++)
            {
                var current = subject[j];
                var previous = subject[(j + count - 1) % count];
                float currentSide = Side(a, b, current, invert);
                float previousSide = Side(a, b, previous, invert);

                if (currentSide >= 0f)
                {
                    if (previousSide < 0f && written < clipped.Length)
                        clipped[written++] = Intersect(previous, current, previousSide, currentSide);
                    if (written < clipped.Length)
                        clipped[written++] = current;
                }
                else if (previousSide >= 0f && written < clipped.Length)
                {
                    clipped[written++] = Intersect(previous, current, previousSide, currentSide);
                }
            }

            count = written;
            System.Array.Copy(clipped, subject, count);
        }

        if (count < 3)
            return 0f;

        return System.Math.Clamp(System.Math.Abs(SignedArea(subject, count)), 0f, 1f);
    }

    private static float Side(PointF a, PointF b, PointF p, bool invert)
    {
        float cross = (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);
        return invert ? -cross : cross;
    }

    private static PointF Intersect(PointF from, PointF to, float fromSide, float toSide)
    {
        float span = fromSide - toSide;
        float t = span == 0f ? 0f : fromSide / span;
        return new PointF(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
    }

    private static float SignedArea(PointF[] points) => SignedArea(points, points.Length);

    private static float SignedArea(PointF[] points, int count)
    {
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2f;
    }

    private sealed class Scope(bool previous) : System.IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Active = previous;
        }
    }
}
