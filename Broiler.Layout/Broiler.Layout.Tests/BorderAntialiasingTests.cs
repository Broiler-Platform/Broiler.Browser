using System.Drawing;
using Broiler.Layout.Engine;

namespace Broiler.Layout.Tests;

/// <summary>
/// The pixel-coverage rule behind corner-miter anti-aliasing: the area of a pixel that a border
/// side's trapezoid actually covers, which is what decides how far the two colours meeting at a
/// miter are blended into each other.
/// </summary>
public class BorderAntialiasingTests
{
    /// <summary>A square from (0,0) to (10,10), wound clockwise on screen (y down).</summary>
    private static PointF[] Square() =>
    [
        new(0, 0), new(10, 0), new(10, 10), new(0, 10),
    ];

    /// <summary>The same square wound the other way, which must not change any answer.</summary>
    private static PointF[] SquareReversed() =>
    [
        new(0, 10), new(10, 10), new(10, 0), new(0, 0),
    ];

    /// <summary>
    /// A 12px border's top side: the full width at the outer edge, mitred in by the left and right
    /// widths at the inner edge — the shape <c>RGraphicsRasterBackend</c> builds.
    /// </summary>
    private static PointF[] TopTrapezoid(float width = 100, float top = 12, float left = 12, float right = 12) =>
    [
        new(0, 0), new(width, 0), new(width - right, top), new(left, top),
    ];

    // ── Wholly inside and wholly outside ─────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Pixel_Well_Inside_Is_Fully_Covered()
    {
        Assert.Equal(1f, BorderAntialiasing.Coverage(Square(), 5, 5));
        Assert.Equal(1f, BorderAntialiasing.Coverage(SquareReversed(), 5, 5));
    }

    [Fact(Timeout = 600000)]
    public void A_Pixel_Well_Outside_Is_Not_Covered()
    {
        Assert.Equal(0f, BorderAntialiasing.Coverage(Square(), 20, 20));
        Assert.Equal(0f, BorderAntialiasing.Coverage(Square(), -5, 5));
        Assert.Equal(0f, BorderAntialiasing.Coverage(SquareReversed(), 20, 20));
    }

    /// <summary>A pixel exactly on an axis-aligned boundary is in or out, never partial.</summary>
    [Fact(Timeout = 600000)]
    public void An_Axis_Aligned_Edge_Needs_No_Blending()
    {
        Assert.Equal(1f, BorderAntialiasing.Coverage(Square(), 0, 0));
        Assert.Equal(1f, BorderAntialiasing.Coverage(Square(), 9, 9));
        Assert.Equal(0f, BorderAntialiasing.Coverage(Square(), 10, 0));
        Assert.Equal(0f, BorderAntialiasing.Coverage(Square(), 0, 10));
    }

    // ── Partial coverage ─────────────────────────────────────────────────────

    /// <summary>
    /// An axis-aligned edge is decided by the pixel centre even when it falls mid-pixel: a border's
    /// own edges are not blended, only its mitres. Feathering them would turn a 1px border sitting
    /// on a half-pixel into two grey rows instead of one solid one.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Mid_Pixel_Axis_Aligned_Edge_Is_Not_Blended()
    {
        PointF[] halfSquare = [new(0, 0), new(5.8f, 0), new(5.8f, 10), new(0, 10)];
        Assert.Equal(1f, BorderAntialiasing.Coverage(halfSquare, 5, 5));   // centre 5.5 < 5.8: in
        Assert.Equal(0f, BorderAntialiasing.Coverage(halfSquare, 6, 5));   // centre 6.5 > 5.8: out
    }

    /// <summary>A slanted edge through the middle of a pixel covers half of it.</summary>
    [Fact(Timeout = 600000)]
    public void A_Half_Covered_Pixel_Reads_One_Half()
    {
        // A triangle whose hypotenuse runs corner to corner through pixel (5,5).
        PointF[] wedge = [new(0, 0), new(11, 11), new(0, 11)];
        Assert.Equal(0.5f, BorderAntialiasing.Coverage(wedge, 5, 5), 3);
    }

    /// <summary>
    /// The 45° miter of an equal-width corner splits every pixel along it exactly in two, which is
    /// why a browser paints one 50/50 pixel per row down the diagonal.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(11)]
    public void An_Equal_Width_Miter_Halves_Each_Pixel_On_It(int step)
    {
        var top = TopTrapezoid();
        Assert.Equal(0.5f, BorderAntialiasing.Coverage(top, step, step), 3);

        // …and the pixels either side of it are whole.
        Assert.Equal(1f, BorderAntialiasing.Coverage(top, step + 1, step));
        if (step > 0)
            Assert.Equal(0f, BorderAntialiasing.Coverage(top, step - 1, step));
    }

    /// <summary>
    /// An unequal corner is not 45°: a 12px top against a 4px left slopes one-in-three, so the
    /// miter takes three rows to cross a pixel and the coverages step 1/6, 1/2, 5/6 — which is what
    /// Chromium shades (measured 0.158, 0.503, 0.842 of the way between the two colours).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Unequal_Miter_Steps_Three_Rows_Per_Pixel()
    {
        // Top 12px, left 4px: the miter runs (0,0) → (4,12).
        var top = TopTrapezoid(width: 100, top: 12, left: 4, right: 4);

        Assert.Equal(1f - 1f / 6f, BorderAntialiasing.Coverage(top, 0, 0), 2);
        Assert.Equal(0.5f, BorderAntialiasing.Coverage(top, 0, 1), 2);
        Assert.Equal(1f / 6f, BorderAntialiasing.Coverage(top, 0, 2), 2);
        Assert.Equal(0f, BorderAntialiasing.Coverage(top, 0, 3));

        // The next pixel column repeats the pattern one row lower.
        Assert.Equal(1f - 1f / 6f, BorderAntialiasing.Coverage(top, 1, 3), 2);
        Assert.Equal(0.5f, BorderAntialiasing.Coverage(top, 1, 4), 2);
        Assert.Equal(1f / 6f, BorderAntialiasing.Coverage(top, 1, 5), 2);
    }

    /// <summary>Winding order is not part of the answer.</summary>
    [Fact(Timeout = 600000)]
    public void Coverage_Does_Not_Depend_On_Winding()
    {
        var top = TopTrapezoid();
        PointF[] reversed = [top[3], top[2], top[1], top[0]];

        for (int step = 0; step < 12; step++)
        {
            Assert.Equal(
                BorderAntialiasing.Coverage(top, step, step),
                BorderAntialiasing.Coverage(reversed, step, step), 4);
        }
    }

    /// <summary>Coverage over a whole side sums to its area, so nothing is gained or lost.</summary>
    [Fact(Timeout = 600000)]
    public void Coverage_Sums_To_The_Trapezoids_Area()
    {
        var top = TopTrapezoid(width: 40, top: 8, left: 8, right: 8);
        float total = 0f;
        for (int y = -2; y < 12; y++)
            for (int x = -2; x < 44; x++)
                total += BorderAntialiasing.Coverage(top, x, y);

        // Trapezoid area: (40 + 24) / 2 * 8 = 256.
        Assert.Equal(256f, total, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Degenerate_Polygon_Covers_Nothing()
    {
        Assert.Equal(0f, BorderAntialiasing.Coverage(null!, 0, 0));
        Assert.Equal(0f, BorderAntialiasing.Coverage([new(0, 0), new(1, 1)], 0, 0));
    }

    // ── The lever ────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void Anti_Aliasing_Is_Off_Unless_Pinned()
    {
        Assert.False(BorderAntialiasing.Active);

        using (BorderAntialiasing.Pin())
        {
            Assert.True(BorderAntialiasing.Active);
            using (BorderAntialiasing.Pin())
                Assert.True(BorderAntialiasing.Active);
            Assert.True(BorderAntialiasing.Active);
        }

        Assert.False(BorderAntialiasing.Active);
    }
}
