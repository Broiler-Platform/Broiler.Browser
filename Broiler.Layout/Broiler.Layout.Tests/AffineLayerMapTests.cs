using System;
using System.Drawing;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// <see cref="AffineLayerMap"/> — the device-space map a rotated or skewed CSS transform becomes
/// once its content has been drawn untransformed into an offscreen layer.
/// </summary>
/// <remarks>
/// The property every case here checks is the same one: a content point drawn at its untransformed
/// device position must be readable back at the device position CSS says the transform puts it, and
/// <see cref="AffineLayerMap.InverseMap"/> must be the exact inverse of
/// <see cref="AffineLayerMap.MapPoint"/> — a resample runs in the inverse direction, so an error
/// there does not misplace the content, it fetches the wrong pixel for every destination.
/// </remarks>
public sealed class AffineLayerMapTests
{
    private const float Tol = 1e-3f;

    private static AffineLayerMap Create(
        float[] matrix, float originX = 0f, float originY = 0f,
        float sx = 1f, float sy = 1f, float tx = 0f, float ty = 0f)
    {
        Assert.True(
            AffineLayerMap.TryCreate(matrix, originX, originY, sx, sy, tx, ty, out var map),
            "expected the matrix to need the layer path");
        return map;
    }

    // rotate(90deg) is matrix(0, 1, -1, 0, 0, 0): the x axis goes to +y and the y axis to −x.
    [Fact(Timeout = 600000)]
    public void A_Quarter_Turn_About_The_Origin_Sends_X_To_Y()
    {
        var map = Create([0f, 1f, -1f, 0f, 0f, 0f]);

        map.MapPoint(10f, 0f, out float x, out float y);
        Assert.Equal(0f, x, Tol);
        Assert.Equal(10f, y, Tol);

        map.MapPoint(0f, 10f, out x, out y);
        Assert.Equal(-10f, x, Tol);
        Assert.Equal(0f, y, Tol);
    }

    // The transform is applied about transform-origin, so that point is the one it does not move.
    [Fact(Timeout = 600000)]
    public void The_Transform_Origin_Is_The_Fixed_Point()
    {
        var map = Create([0f, 1f, -1f, 0f, 0f, 0f], originX: 40f, originY: 70f);

        map.MapPoint(40f, 70f, out float x, out float y);
        Assert.Equal(40f, x, Tol);
        Assert.Equal(70f, y, Tol);
    }

    // The worked case from css/css-transforms/transform-box/cssbox-content-box-001: a 200x200
    // border box at (300, 100) rotated a quarter turn about (300, 100) lands at (100, 100)-(300,
    // 300). The reference states the answer as "a square whose top-left corner is at 100,100".
    [Fact(Timeout = 600000)]
    public void A_Quarter_Turn_Moves_The_Box_Where_The_Reference_Says()
    {
        var map = Create([0f, 1f, -1f, 0f, 0f, 0f], originX: 300f, originY: 100f);

        var bounds = map.MapBounds(RectangleF.FromLTRB(300f, 100f, 500f, 300f));

        Assert.Equal(100f, bounds.Left, Tol);
        Assert.Equal(100f, bounds.Top, Tol);
        Assert.Equal(300f, bounds.Right, Tol);
        Assert.Equal(300f, bounds.Bottom, Tol);
    }

    // A 45-degree turn grows the axis-aligned box it needs by root two, which is the whole reason
    // the caller iterates MapBounds rather than the source rectangle.
    [Fact(Timeout = 600000)]
    public void A_Diagonal_Turn_Grows_The_Destination_Box()
    {
        float k = MathF.Sqrt(2f) / 2f;
        var map = Create([k, k, -k, k, 0f, 0f], originX: 50f, originY: 50f);

        var bounds = map.MapBounds(RectangleF.FromLTRB(0f, 0f, 100f, 100f));

        float grown = 100f * MathF.Sqrt(2f);
        Assert.Equal(grown, bounds.Width, 1e-2f);
        Assert.Equal(grown, bounds.Height, 1e-2f);
        Assert.Equal(50f, bounds.Left + bounds.Width / 2f, 1e-2f);
        Assert.Equal(50f, bounds.Top + bounds.Height / 2f, 1e-2f);
    }

    // The pre-image is what keeps a warp layer affordable: it is the part of the layer that can
    // reach the region still visible, so a rasterizer inside the layer keeps a box to bound its
    // loops with. Leaving it unbounded instead was measured at ten times the cost on the full suite.
    [Fact(Timeout = 600000)]
    public void The_Pre_Image_Of_A_Destination_Box_Round_Trips()
    {
        var map = Create([0f, 1f, -1f, 0f, 0f, 0f], originX: 300f, originY: 100f);
        var source = RectangleF.FromLTRB(300f, 100f, 500f, 300f);

        var destination = map.MapBounds(source);
        var preImage = map.InverseMapBounds(destination);

        Assert.Equal(source.Left, preImage.Left, 1e-2f);
        Assert.Equal(source.Top, preImage.Top, 1e-2f);
        Assert.Equal(source.Right, preImage.Right, 1e-2f);
        Assert.Equal(source.Bottom, preImage.Bottom, 1e-2f);
    }

    // It has to be a superset of what can land in the destination, or clipping to it would drop
    // content that should have been drawn: a diagonal turn's pre-image grows the same way its image
    // does, and every corner of the destination has to map back inside it.
    [Fact(Timeout = 600000)]
    public void The_Pre_Image_Contains_Everything_That_Can_Reach_The_Destination()
    {
        float k = MathF.Sqrt(2f) / 2f;
        var map = Create([k, k, -k, k, 0f, 0f], originX: 50f, originY: 50f);
        var destination = RectangleF.FromLTRB(0f, 0f, 100f, 100f);

        var preImage = map.InverseMapBounds(destination);

        foreach (var (dx, dy) in new[] { (0f, 0f), (100f, 0f), (100f, 100f), (0f, 100f), (50f, 50f) })
        {
            map.InverseMap(dx, dy, out float sx, out float sy);
            Assert.True(
                sx >= preImage.Left - 1e-3f && sx <= preImage.Right + 1e-3f
                && sy >= preImage.Top - 1e-3f && sy <= preImage.Bottom + 1e-3f,
                $"({sx}, {sy}) fell outside the pre-image {preImage}");
        }
    }

    [Theory]
    // rotation, skew, and a rotation composed with a scale — the three shapes that reach this path
    [InlineData(0f, 1f, -1f, 0f, 0f, 0f)]
    [InlineData(1f, 0f, 0.5f, 1f, 0f, 0f)]
    [InlineData(1.5f, 2f, -2f, 1.5f, 13f, -7f)]
    public void Inverse_Undoes_Forward(float a, float b, float c, float d, float e, float f)
    {
        var map = Create([a, b, c, d, e, f], originX: 25f, originY: 60f, sx: 2f, sy: 2f, tx: 11f, ty: 5f);

        foreach (var (px, py) in new[] { (0f, 0f), (100f, 40f), (-30f, 250f) })
        {
            map.MapPoint(px, py, out float qx, out float qy);
            map.InverseMap(qx, qy, out float rx, out float ry);
            Assert.Equal(px, rx, 1e-2f);
            Assert.Equal(py, ry, 1e-2f);
        }
    }

    // The surface mapping is conjugated, not assumed away: content drawn into the layer is already
    // in device space, so the transform has to be expressed there too. Under a 2x zoom a quarter
    // turn about a canvas-local origin still fixes that origin's *device* position.
    [Fact(Timeout = 600000)]
    public void The_Surface_Scale_And_Translation_Are_Carried_Through()
    {
        var map = Create([0f, 1f, -1f, 0f, 0f, 0f], originX: 30f, originY: 40f, sx: 2f, sy: 2f, tx: 7f, ty: 9f);

        // Device position of the canvas-local origin: (30·2 + 7, 40·2 + 9) = (67, 89).
        map.MapPoint(67f, 89f, out float x, out float y);
        Assert.Equal(67f, x, Tol);
        Assert.Equal(89f, y, Tol);

        // A canvas-local point 10 to the right of the origin — device (87, 89) — turns to 10 below
        // it in canvas-local terms, which under the 2x zoom is 20 device pixels: (67, 109).
        map.MapPoint(87f, 89f, out x, out y);
        Assert.Equal(67f, x, Tol);
        Assert.Equal(109f, y, Tol);
    }

    // A non-uniform surface scale is where assuming a single zoom factor would go wrong: a quarter
    // turn under sx != sy is not a quarter turn in device space, and the conjugation is what keeps
    // the mapped box the right shape.
    [Fact(Timeout = 600000)]
    public void A_Non_Uniform_Surface_Scale_Is_Conjugated_Not_Averaged()
    {
        var map = Create([0f, 1f, -1f, 0f, 0f, 0f], originX: 0f, originY: 0f, sx: 2f, sy: 4f);

        // Canvas-local (10, 0) is device (20, 0); a quarter turn sends it to canvas-local (0, 10),
        // which is device (0, 40).
        map.MapPoint(20f, 0f, out float x, out float y);
        Assert.Equal(0f, x, Tol);
        Assert.Equal(40f, y, Tol);
    }

    [Theory]
    // Translation and axis-aligned scale, mirrors included: the canvas draws these directly, and
    // claiming them here would cost an offscreen surface and resample content it places exactly.
    [InlineData(1f, 0f, 0f, 1f, 20f, 30f)]
    [InlineData(2f, 0f, 0f, 3f, 0f, 0f)]
    [InlineData(-1f, 0f, 0f, -1f, 0f, 0f)]
    [InlineData(1f, 0f, 0f, 1f, 0f, 0f)]
    public void An_Axis_Aligned_Matrix_Is_Declined(float a, float b, float c, float d, float e, float f)
    {
        float[] m = [a, b, c, d, e, f];
        Assert.True(AffineLayerMap.IsAxisAligned(m));
        Assert.False(AffineLayerMap.TryCreate(m, 0f, 0f, 1f, 1f, 0f, 0f, out _));
    }

    [Fact(Timeout = 600000)]
    public void A_Singular_Matrix_Is_Declined()
    {
        // scale(0) composed with a rotation: every point collapses onto one, so there is no source
        // point to fetch back for a destination pixel.
        Assert.False(AffineLayerMap.TryCreate([0f, 1f, 0f, 0f, 0f, 0f], 0f, 0f, 1f, 1f, 0f, 0f, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new float[] { 1f, 0f, 0f })]
    public void A_Malformed_Matrix_Is_Declined(float[] matrix)
    {
        Assert.True(AffineLayerMap.IsAxisAligned(matrix));
        Assert.False(AffineLayerMap.TryCreate(matrix, 0f, 0f, 1f, 1f, 0f, 0f, out _));
    }

    [Fact(Timeout = 600000)]
    public void A_Degenerate_Surface_Is_Declined()
    {
        Assert.False(AffineLayerMap.TryCreate([0f, 1f, -1f, 0f, 0f, 0f], 0f, 0f, 0f, 1f, 0f, 0f, out _));
        Assert.False(AffineLayerMap.TryCreate([0f, 1f, -1f, 0f, 0f, 0f], 0f, 0f, 1f, float.NaN, 0f, 0f, out _));
    }

    // The matrix's own translation terms are lengths in canvas-local units, so they scale with the
    // surface; its a/b/c/d terms are ratios and do not.
    [Fact(Timeout = 600000)]
    public void The_Matrix_Translation_Is_A_Length_And_Scales_With_The_Surface()
    {
        var map = Create([0f, 1f, -1f, 0f, 5f, 0f], originX: 0f, originY: 0f, sx: 3f, sy: 3f);

        map.MapPoint(0f, 0f, out float x, out float y);
        Assert.Equal(15f, x, Tol);
        Assert.Equal(0f, y, Tol);
    }
}
