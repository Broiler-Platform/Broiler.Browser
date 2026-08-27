using System.Drawing;
using System.Linq;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// SVG 1.1 §14.5 <c>opacity</c>. It is not <c>fill-opacity</c>: that one is folded into the paint
/// colour by <c>GetPaint</c>, which is exact because it applies to the fill operation itself, while
/// <c>opacity</c> composites the element as a whole — fill and stroke together — and so needs a
/// layer. Nothing emitted one, so the property was dropped outright and
/// <c>&lt;rect fill="blue" opacity="0.5"&gt;</c> painted solid blue.
///
/// <para>
/// It reaches a group the long way round. <c>opacity</c> does not inherit, so it is deliberately
/// absent from <c>SvgStructure.InheritableProperties</c> — but this renderer matches one regex per
/// shape type and draws leaves, so a <c>&lt;g opacity&gt;</c> has no group of its own to hang a
/// layer on. <c>SvgStructure</c> therefore carries the ancestors' composed opacity down to each
/// shape, which multiplies it into its own. That is exact while a group's children do not overlap
/// one another; where they do, true group compositing would flatten first and fade once.
/// </para>
///
/// <para>
/// Found while triaging the <c>conformance-checkers/html-svg</c> cluster of the 2026-08-21 WPT run
/// (issue #1770): <c>filters-blend-01-b-isvalid</c> draws seven <c>opacity="0.5"</c> bars and
/// Broiler painted every one of them opaque.
/// </para>
/// </summary>
public class SvgElementOpacityTests
{
    private static readonly RectangleF Viewport = new(0, 0, 200, 200);

    private static System.Collections.Generic.List<DisplayItem> Render(string body) =>
        SvgRenderer.RenderSvgContent(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"200\">{body}</svg>",
            Viewport,
            1f);

    /// <summary>
    /// The opacity of the layer wrapping the first shape, or <c>null</c> when no layer was pushed.
    /// </summary>
    private static float? LayerOpacity(string body)
    {
        var items = Render(body);
        var opacity = items.OfType<OpacityItem>().FirstOrDefault();
        return opacity?.Opacity;
    }

    private static void AssertLayer(string body, float expected)
    {
        var actual = LayerOpacity(body);
        Assert.True(actual is not null, $"expected an opacity layer for: {body}");
        Assert.Equal(expected, actual!.Value, 3);
    }

    [Fact]
    public void PlainShape_PushesNoLayer()
    {
        Assert.Null(LayerOpacity("<rect width='50' height='50' fill='blue'/>"));
    }

    [Fact]
    public void OpacityOne_PushesNoLayer()
    {
        Assert.Null(LayerOpacity("<rect width='50' height='50' fill='blue' opacity='1'/>"));
    }

    /// <summary>
    /// <c>fill-opacity</c> keeps its old route — folded into the paint, no layer — so the two
    /// properties stay distinct rather than one starting to double for the other.
    /// </summary>
    [Fact]
    public void FillOpacity_StillFoldsIntoThePaintAndPushesNoLayer()
    {
        Assert.Null(LayerOpacity("<rect width='50' height='50' fill='blue' fill-opacity='0.5'/>"));
    }

    [Fact]
    public void OpacityAttribute_PushesTheLayer()
    {
        AssertLayer("<rect width='50' height='50' fill='blue' opacity='0.5'/>", 0.5f);
    }

    /// <summary>SVG 1.1 §6.4 gives a <c>style</c> declaration precedence over the attribute.</summary>
    [Fact]
    public void OpacityInStyleAttribute_PushesTheLayer()
    {
        AssertLayer("<rect width='50' height='50' fill='blue' style='opacity:0.25'/>", 0.25f);
    }

    [Fact]
    public void PercentageOpacity_Resolves()
    {
        AssertLayer("<rect width='50' height='50' fill='blue' opacity='50%'/>", 0.5f);
    }

    /// <summary>SVG 1.1 §4.4 clamps rather than rejecting an out-of-range alpha.</summary>
    [Theory]
    [InlineData("-1", 0f)]
    [InlineData("0", 0f)]
    [InlineData("2", 1f)]
    public void OutOfRangeOpacity_Clamps(string declared, float expected)
    {
        string body = $"<rect width='50' height='50' fill='blue' opacity='{declared}'/>";
        if (expected >= 1f)
            Assert.Null(LayerOpacity(body));
        else
            AssertLayer(body, expected);
    }

    [Fact]
    public void GroupOpacity_ReachesTheShapeInside()
    {
        AssertLayer("<g opacity='0.5'><rect width='50' height='50' fill='blue'/></g>", 0.5f);
    }

    [Fact]
    public void GroupOpacity_MultipliesWithTheShapesOwn()
    {
        AssertLayer(
            "<g opacity='0.5'><rect width='50' height='50' fill='blue' opacity='0.5'/></g>", 0.25f);
    }

    [Fact]
    public void NestedGroupOpacity_Multiplies()
    {
        AssertLayer(
            "<g opacity='0.5'><g opacity='0.5'><rect width='50' height='50' fill='blue'/></g></g>",
            0.25f);
    }

    /// <summary>
    /// The group's opacity must not leak sideways: a shape outside the faded group keeps its own.
    /// </summary>
    [Fact]
    public void GroupOpacity_DoesNotEscapeTheGroup()
    {
        var items = Render(
            "<g opacity='0.5'><rect width='10' height='10' fill='blue'/></g>"
            + "<rect x='20' width='10' height='10' fill='green'/>");

        Assert.Single(items.OfType<OpacityItem>());
    }

    /// <summary>Every layer pushed is popped, so the display list stays balanced.</summary>
    [Fact]
    public void EveryOpacityLayer_IsRestored()
    {
        var items = Render(
            "<g opacity='0.5'>"
            + "<rect width='10' height='10' fill='blue' opacity='0.5'/>"
            + "<circle cx='30' cy='30' r='5' fill='red'/>"
            + "</g>");

        Assert.Equal(items.OfType<OpacityItem>().Count(), items.OfType<RestoreOpacityItem>().Count());
        Assert.Equal(2, items.OfType<OpacityItem>().Count());
    }
}
