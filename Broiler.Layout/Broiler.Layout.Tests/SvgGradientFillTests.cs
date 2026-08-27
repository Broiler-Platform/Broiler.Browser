using System.Drawing;
using System.Linq;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// SVG gradient paint servers: <c>&lt;linearGradient&gt;</c> and <c>&lt;radialGradient&gt;</c>
/// reached through <c>fill="url(#id)"</c>.
///
/// <para>
/// A gradient fill painted <b>nothing at all</b>. <c>ResolveFill</c> recognised the reference,
/// looked for a <c>&lt;pattern&gt;</c> under that id, found none, and fell through to the fallback
/// paint — which for the ordinary <c>fill="url(#grad)"</c> with no colour written after it is "no
/// paint". Every gradient-filled shape was invisible.
/// </para>
///
/// <para>
/// Found triaging WPT issue #1770 by clustering the run's failures rather than by working down the
/// list: the 82 <c>css-transforms/transform-origin/svg-origin-*</c> tests all scored an identical
/// 97.21%, and they fill with a gradient precisely to avoid a false positive — so the shape was
/// blank on <em>both</em> sides of their own comparison.
/// </para>
///
/// <para>
/// The gradient is emitted as a <see cref="DrawTiledGradientItem"/>, the display item CSS
/// backgrounds already use, so the raster backend needs no new primitive.
/// </para>
/// </summary>
public class SvgGradientFillTests
{
    private static readonly RectangleF Viewport = new(0, 0, 200, 200);

    private static System.Collections.Generic.List<DisplayItem> Render(string body) =>
        SvgRenderer.RenderSvgContent(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">{body}</svg>",
            Viewport,
            1f);

    private static DrawTiledGradientItem Gradient(string body) =>
        Render(body).OfType<DrawTiledGradientItem>().FirstOrDefault();

    private const string LinearRedBlue =
        "<linearGradient id='g'><stop offset='0%' stop-color='red'/>"
        + "<stop offset='100%' stop-color='blue'/></linearGradient>";

    [Fact]
    public void GradientFill_EmitsAGradient()
    {
        var g = Gradient($"<defs>{LinearRedBlue}</defs><rect width='100' height='50' fill='url(#g)'/>");

        Assert.NotNull(g);
        Assert.Equal(2, g.Stops.Count);
        Assert.Equal(255, g.Stops[0].Color.R);
        Assert.Equal(255, g.Stops[1].Color.B);
    }

    /// <summary>
    /// SVG 1.1 §13.2.2 defaults x1,y1 = 0%,0% and x2,y2 = 100%,0% — left to right. In the CSS
    /// gradient angle this renderer emits, whose zero points to the top and which turns clockwise,
    /// that is 90°.
    /// </summary>
    [Fact]
    public void DefaultLinearGradient_RunsLeftToRight()
    {
        var g = Gradient($"<defs>{LinearRedBlue}</defs><rect width='100' height='50' fill='url(#g)'/>");
        Assert.Equal(90f, g.Angle, 1);
    }

    /// <summary>A vertical gradient vector is 180° — top to bottom.</summary>
    [Fact]
    public void VerticalLinearGradient_RunsTopToBottom()
    {
        var g = Gradient(
            "<defs><linearGradient id='g' x2='0%' y2='100%'>"
            + "<stop offset='0%' stop-color='red'/><stop offset='100%' stop-color='blue'/>"
            + "</linearGradient></defs><rect width='100' height='50' fill='url(#g)'/>");

        Assert.Equal(180f, g.Angle, 1);
    }

    [Fact]
    public void RadialGradient_IsMarkedRadial()
    {
        var g = Gradient(
            "<defs><radialGradient id='g'><stop offset='0%' stop-color='lime'/>"
            + "<stop offset='100%' stop-color='black'/></radialGradient></defs>"
            + "<rect width='100' height='100' fill='url(#g)'/>");

        Assert.NotNull(g);
        Assert.True(g.IsRadial);
        Assert.Equal(0.5f, g.CenterX, 2);
        Assert.Equal(0.5f, g.CenterY, 2);
    }

    /// <summary><c>stop-opacity</c> folds into the stop's alpha.</summary>
    [Fact]
    public void StopOpacity_FoldsIntoTheStopAlpha()
    {
        var g = Gradient(
            "<defs><linearGradient id='g'>"
            + "<stop offset='0%' stop-color='red' stop-opacity='0.5'/>"
            + "<stop offset='100%' stop-color='blue'/></linearGradient></defs>"
            + "<rect width='100' height='50' fill='url(#g)'/>");

        Assert.InRange(g.Stops[0].Color.A, 120, 136);
        Assert.Equal(255, g.Stops[1].Color.A);
    }

    /// <summary>SVG 1.1 §13.2.4: offsets are clamped to 0..1 and kept non-decreasing.</summary>
    [Fact]
    public void StopOffsets_AreClampedAndNonDecreasing()
    {
        var g = Gradient(
            "<defs><linearGradient id='g'>"
            + "<stop offset='-1' stop-color='red'/>"
            + "<stop offset='0.7' stop-color='green'/>"
            + "<stop offset='0.2' stop-color='blue'/>"
            + "<stop offset='5' stop-color='black'/></linearGradient></defs>"
            + "<rect width='100' height='50' fill='url(#g)'/>");

        Assert.Equal([0f, 0.7f, 0.7f, 1f], g.Stops.Select(s => s.Position).ToArray());
    }

    /// <summary>A gradient takes its stops from the one its <c>href</c> points at.</summary>
    [Fact]
    public void HrefInheritance_CopiesTheStops()
    {
        var g = Gradient(
            $"<defs>{LinearRedBlue}<linearGradient id='copy' href='#g'/></defs>"
            + "<rect width='100' height='50' fill='url(#copy)'/>");

        Assert.NotNull(g);
        Assert.Equal(2, g.Stops.Count);
        Assert.Equal(255, g.Stops[0].Color.R);
    }

    /// <summary>
    /// SVG 2 §6.5: where both spellings are present the unprefixed <c>href</c> wins. The attribute
    /// parser drops the namespace prefix, so both land under the same key and the last one written
    /// would otherwise win — which is what
    /// <c>svg/linking/reftests/href-gradient-element</c> is built to catch.
    /// </summary>
    [Fact]
    public void PlainHref_BeatsXlinkHref()
    {
        var g = Gradient(
            "<defs>"
            + "<linearGradient id='wanted'><stop offset='0%' stop-color='lime'/>"
            + "<stop offset='100%' stop-color='lime'/></linearGradient>"
            + "<linearGradient id='unwanted'><stop offset='0%' stop-color='red'/>"
            + "<stop offset='100%' stop-color='red'/></linearGradient>"
            + "<linearGradient id='copy' href='#wanted' xlink:href='#unwanted'/>"
            + "</defs><rect width='100' height='50' fill='url(#copy)'/>");

        Assert.NotNull(g);
        Assert.Equal(0, g.Stops[0].Color.R);
        Assert.True(g.Stops[0].Color.G > 200, "the href gradient's lime should win over xlink:href's red");
    }

    /// <summary>An <c>xlink:href</c> on its own still resolves.</summary>
    [Fact]
    public void XlinkHrefAlone_StillResolves()
    {
        var g = Gradient(
            $"<defs>{LinearRedBlue}<linearGradient id='copy' xlink:href='#g'/></defs>"
            + "<rect width='100' height='50' fill='url(#copy)'/>");

        Assert.NotNull(g);
        Assert.Equal(2, g.Stops.Count);
    }

    /// <summary>A reference that names nothing paints nothing — no gradient, and no black box.</summary>
    [Fact]
    public void UnresolvableReference_EmitsNoGradient()
    {
        Assert.Null(Gradient("<rect width='100' height='50' fill='url(#missing)'/>"));
    }

    /// <summary>
    /// A gradient with no stops cannot paint, so the shape keeps its fallback rather than getting
    /// an empty ramp.
    /// </summary>
    [Fact]
    public void GradientWithNoStops_EmitsNoGradient()
    {
        Assert.Null(Gradient(
            "<defs><linearGradient id='g'/></defs><rect width='100' height='50' fill='url(#g)'/>"));
    }

    /// <summary>A shape with an ordinary colour fill emits no gradient at all.</summary>
    [Fact]
    public void PlainColourFill_EmitsNoGradient()
    {
        Assert.Null(Gradient($"<defs>{LinearRedBlue}</defs><rect width='100' height='50' fill='blue'/>"));
    }

    /// <summary>
    /// The gradient is mapped by the element's own transform, exactly as AddShape maps the shape it
    /// fills: a quarter turn takes the default left-to-right ramp to top-to-bottom.
    /// </summary>
    [Fact]
    public void ElementTransform_TurnsTheGradientWithTheShape()
    {
        var g = Gradient(
            $"<defs>{LinearRedBlue}</defs>"
            + "<rect width='100' height='100' fill='url(#g)' transform='rotate(90)'/>");

        Assert.NotNull(g);
        Assert.Equal(180f, g.Angle, 1);
    }
}
