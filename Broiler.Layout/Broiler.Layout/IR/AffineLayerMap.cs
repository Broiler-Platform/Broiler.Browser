using System;
using System.Drawing;

namespace Broiler.Layout.IR;

/// <summary>
/// The device-space affine map a rotated or skewed CSS <c>transform</c> becomes when its content
/// has been drawn, untransformed, into an offscreen layer: given a destination pixel it says which
/// point of that layer lands there (<see cref="InverseMap"/>), and given the layer's occupied
/// region it says where that region ends up (<see cref="MapBounds"/>).
/// </summary>
/// <remarks>
/// <para>
/// A raster canvas that maps a point per axis — <c>p → p·scale + translation</c>, which is what
/// Broiler's does — can express translation and axis-aligned scale, mirrors included, but nothing
/// with a <c>b</c> or <c>c</c> term. Those used to fall through to a compat backend that on a
/// headless host is an inert stub, so a rotated element and its whole subtree were not drawn
/// <em>at all</em>: <c>transform: rotate(45deg)</c> on a green square painted a blank page. The
/// graceful degradations beside it (opacity, filter, blend) had already been moved off that
/// fall-through; the transform one is this.
/// </para>
/// <para>
/// **Warping the finished layer, rather than teaching every primitive an affine matrix.** Content
/// is drawn into a full-surface offscreen with the ordinary axis-aligned mapping, so every
/// primitive — fills, text, images, clips — keeps the arithmetic it already has, and one resample
/// puts the result where the transform says. Inverse mapping (destination → source) is what makes
/// it hole-free: a forward scatter leaves gaps wherever the transform magnifies.
/// </para>
/// <para>
/// The map lives here rather than beside the canvas because it is arithmetic with no pixels in it,
/// and because the canvas is in a submodule: the caller there is a loop over
/// <see cref="MapBounds"/> calling <see cref="InverseMap"/>, and everything that can be got wrong
/// is on this side, under test.
/// </para>
/// </remarks>
public readonly struct AffineLayerMap
{
    // The device-space forward map, q = B·d + w, and the inverse of B.
    private readonly float _b00, _b01, _b10, _b11;
    private readonly float _wx, _wy;
    private readonly float _i00, _i01, _i10, _i11;

    private AffineLayerMap(
        float b00, float b01, float b10, float b11,
        float wx, float wy,
        float i00, float i01, float i10, float i11)
    {
        _b00 = b00; _b01 = b01; _b10 = b10; _b11 = b11;
        _wx = wx; _wy = wy;
        _i00 = i00; _i01 = i01; _i10 = i10; _i11 = i11;
    }

    /// <summary>
    /// The <c>b</c>/<c>c</c> magnitude below which a matrix is treated as axis-aligned, matching the
    /// canvas's own test so exactly one of the two paths claims a given matrix.
    /// </summary>
    public const float AxisAlignedEpsilon = 1e-4f;

    /// <summary>
    /// Whether <paramref name="matrix"/> is expressible as translation plus per-axis scale — the
    /// case a per-axis canvas handles directly, and which must therefore <em>not</em> take the
    /// layer path: warping a layer costs an offscreen surface and a resample, and would also
    /// resample content the direct path places exactly.
    /// </summary>
    public static bool IsAxisAligned(float[] matrix) =>
        matrix is null
        || matrix.Length < 6
        || (MathF.Abs(matrix[1]) <= AxisAlignedEpsilon && MathF.Abs(matrix[2]) <= AxisAlignedEpsilon);

    /// <summary>
    /// Builds the map for a CSS <c>matrix(a, b, c, d, e, f)</c> applied about
    /// (<paramref name="originX"/>, <paramref name="originY"/>) in canvas-local coordinates, on a
    /// canvas whose own mapping is <c>p → p·scale + translation</c>. Returns <see langword="false"/>
    /// for an axis-aligned matrix (the caller's fast path owns it) and for a singular one, which
    /// collapses the element to a line or a point and has nothing to sample back through.
    /// </summary>
    public static bool TryCreate(
        float[] matrix,
        float originX,
        float originY,
        float surfaceScaleX,
        float surfaceScaleY,
        float surfaceTranslateX,
        float surfaceTranslateY,
        out AffineLayerMap map)
    {
        map = default;

        if (IsAxisAligned(matrix))
            return false;
        if (surfaceScaleX == 0f || surfaceScaleY == 0f
            || !float.IsFinite(surfaceScaleX) || !float.IsFinite(surfaceScaleY)
            || !float.IsFinite(surfaceTranslateX) || !float.IsFinite(surfaceTranslateY)
            || !float.IsFinite(originX) || !float.IsFinite(originY))
            return false;

        float a = matrix[0], b = matrix[1], c = matrix[2], d = matrix[3], e = matrix[4], f = matrix[5];
        for (int i = 0; i < 6; i++)
        {
            if (!float.IsFinite(matrix[i]))
                return false;
        }

        // CSS matrix(a,b,c,d,e,f) is x' = a·x + c·y + e, y' = b·x + d·y + f — so the linear part is
        // A = [[a, c], [b, d]], read row-wise.
        //
        // Content sits in the layer at its *device* position D(p) = S·p + t, and must end up at
        // D(T(p)) where T(p) = A·(p − O) + O + E. Eliminating p gives a device-to-device map
        //     W(d) = B·d + w,   B = S·A·S⁻¹,   w = t − B·t + S·(O − A·O + E),
        // which is what this stores. Conjugating by S rather than assuming a uniform surface scale
        // keeps it exact when the two axes differ.
        float sx = surfaceScaleX, sy = surfaceScaleY;
        float b00 = a, b01 = c * sx / sy, b10 = b * sy / sx, b11 = d;

        float det = b00 * b11 - b01 * b10;
        if (MathF.Abs(det) < 1e-9f || !float.IsFinite(det))
            return false;

        // S·(O − A·O + E), per axis.
        float ax = a * originX + c * originY;
        float ay = b * originX + d * originY;
        float sOx = sx * (originX - ax + e);
        float sOy = sy * (originY - ay + f);

        float tx = surfaceTranslateX, ty = surfaceTranslateY;
        float wx = tx - (b00 * tx + b01 * ty) + sOx;
        float wy = ty - (b10 * tx + b11 * ty) + sOy;

        float inv = 1f / det;
        map = new AffineLayerMap(
            b00, b01, b10, b11,
            wx, wy,
            b11 * inv, -b01 * inv, -b10 * inv, b00 * inv);
        return true;
    }

    /// <summary>
    /// Where a device-space rectangle of layer content lands: the axis-aligned bounding box of its
    /// four mapped corners. A rotation makes that box larger than the rectangle, which is the point
    /// — the caller iterates it to find every destination pixel the content can reach.
    /// </summary>
    public RectangleF MapBounds(RectangleF source)
    {
        MapPoint(source.Left, source.Top, out float x0, out float y0);
        MapPoint(source.Right, source.Top, out float x1, out float y1);
        MapPoint(source.Right, source.Bottom, out float x2, out float y2);
        MapPoint(source.Left, source.Bottom, out float x3, out float y3);

        float minX = MathF.Min(MathF.Min(x0, x1), MathF.Min(x2, x3));
        float maxX = MathF.Max(MathF.Max(x0, x1), MathF.Max(x2, x3));
        float minY = MathF.Min(MathF.Min(y0, y1), MathF.Min(y2, y3));
        float maxY = MathF.Max(MathF.Max(y0, y1), MathF.Max(y2, y3));

        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// The other direction: which part of the layer can reach a destination rectangle. This is what
    /// keeps a warp layer affordable — the content drawn into it can be bounded to the pre-image of
    /// whatever is still visible, so a rasterizer inside the layer keeps a box to clip and cull
    /// against instead of walking the whole surface for every fill.
    /// </summary>
    public RectangleF InverseMapBounds(RectangleF destination)
    {
        InverseMap(destination.Left, destination.Top, out float x0, out float y0);
        InverseMap(destination.Right, destination.Top, out float x1, out float y1);
        InverseMap(destination.Right, destination.Bottom, out float x2, out float y2);
        InverseMap(destination.Left, destination.Bottom, out float x3, out float y3);

        float minX = MathF.Min(MathF.Min(x0, x1), MathF.Min(x2, x3));
        float maxX = MathF.Max(MathF.Max(x0, x1), MathF.Max(x2, x3));
        float minY = MathF.Min(MathF.Min(y0, y1), MathF.Min(y2, y3));
        float maxY = MathF.Max(MathF.Max(y0, y1), MathF.Max(y2, y3));

        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    /// <summary>Forward: where the layer's device point (<paramref name="x"/>, <paramref name="y"/>)
    /// is drawn.</summary>
    public void MapPoint(float x, float y, out float destX, out float destY)
    {
        destX = _b00 * x + _b01 * y + _wx;
        destY = _b10 * x + _b11 * y + _wy;
    }

    /// <summary>Inverse: which point of the layer is drawn at the destination point
    /// (<paramref name="destX"/>, <paramref name="destY"/>). This is the direction the resample runs
    /// in, so that every destination pixel gets a value and none is skipped.</summary>
    public void InverseMap(float destX, float destY, out float x, out float y)
    {
        float dx = destX - _wx, dy = destY - _wy;
        x = _i00 * dx + _i01 * dy;
        y = _i10 * dx + _i11 * dy;
    }
}
