using System.Drawing;
using System.Linq;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the native CSS <c>zoom</c> SVG increment (increment 5): <see cref="SvgRenderer.RenderSvgContent"/>
/// seeds the SVG user-unit → CSS-pixel scale with the owning element's effective zoom, so a
/// <em>view-box-less</em> SVG (whose raw coordinates map 1:1 to CSS px) scales with the box. A view-boxed
/// SVG is unaffected — its scale derives from the (already-zoomed) bounds, so the view-box branch overrides
/// the seed rather than compounding it. The <c>effectiveZoom</c> default is <c>1.0</c>, so the render is
/// byte-identical while the native-zoom engine is off.
/// </summary>
public sealed class ZoomSvgRenderTests
{
    private static DrawSvgRectItem Rect(string svg, double zoom) =>
        SvgRenderer.RenderSvgContent(svg, new RectangleF(0, 0, 200, 200), zoom)
            .OfType<DrawSvgRectItem>().Single();

    [Fact(Timeout = 600000)]
    public void NoViewBox_RawGeometry_ScalesBy_EffectiveZoom()
    {
        const string svg = "<svg><rect x=\"10\" y=\"10\" width=\"20\" height=\"30\"/></svg>";

        var plain = Rect(svg, 1.0);
        Assert.Equal(10, plain.X, 3);
        Assert.Equal(20, plain.Width, 3);
        Assert.Equal(30, plain.Height, 3);

        var zoomed = Rect(svg, 2.0);
        Assert.Equal(20, zoomed.X, 3);
        Assert.Equal(40, zoomed.Width, 3);
        Assert.Equal(60, zoomed.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void ViewBox_ScaleFromBounds_DoesNotCompound_EffectiveZoom()
    {
        // A viewBox derives its scale from the (already-zoomed) bounds, so passing a larger effectiveZoom
        // must NOT change the result — the seed is overridden, not multiplied.
        const string svg = "<svg viewBox=\"0 0 100 100\"><rect x=\"10\" y=\"10\" width=\"20\" height=\"30\"/></svg>";

        var atOne = Rect(svg, 1.0);
        var atTwo = Rect(svg, 2.0);

        // bounds 200 / viewBox 100 → scale 2 in both cases.
        Assert.Equal(20, atOne.X, 3);
        Assert.Equal(40, atOne.Width, 3);
        Assert.Equal(atOne.X, atTwo.X, 3);
        Assert.Equal(atOne.Width, atTwo.Width, 3);
        Assert.Equal(atOne.Height, atTwo.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void Default_EffectiveZoom_IsOne_ByteIdentical()
    {
        const string svg = "<svg><rect x=\"5\" y=\"5\" width=\"15\" height=\"15\"/></svg>";
        var explicitOne = Rect(svg, 1.0);
        var defaulted = SvgRenderer.RenderSvgContent(svg, new RectangleF(0, 0, 200, 200))
            .OfType<DrawSvgRectItem>().Single();

        Assert.Equal(explicitOne.X, defaulted.X, 6);
        Assert.Equal(explicitOne.Width, defaulted.Width, 6);
    }
}
