using System.Drawing;
using System.Linq;
using Broiler.Graphics;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Covers <see cref="SvgColorFilter"/> and the <see cref="SvgRenderer"/> plumbing that reads it: an
/// SVG <c>&lt;filter&gt;</c> whose primitives only transform colour, applied to a shape with a solid
/// fill, is equivalent to recolouring that shape.
/// <para>
/// The case that drove it is WPT <c>css/filter-effects/fecolormatrix-negative</c> (issue #1562's
/// problem 27, 7.7% match): an <c>feColorMatrix</c> with negative entries followed by an arithmetic
/// <c>feComposite</c> with <c>k2="255"</c> turns <c>#ffaa00</c> into cyan, and Broiler painted the
/// unfiltered orange. The bail-out cases matter as much as the positive one — everything the model
/// does not cover must leave the shape rendering unfiltered rather than render it wrongly.
/// </para>
/// </summary>
public sealed class SvgColorFilterTests
{
    // The WPT test's filter, verbatim: invert each channel, then multiply by 255 and saturate.
    private const string InvertAndSaturate = @"
        <filter id=""invert"" color-interpolation-filters=""sRGB"">
            <feColorMatrix in=""SourceGraphic""
                values=""-1 0 0 0 1
                        0 -1 0 0 1
                        0 0 -1 0 1
                        0 0 0 1 0"" result=""ef0"">
            </feColorMatrix>
            <feComposite in=""ef0"" in2=""ef0"" operator=""arithmetic"" k2=""255"">
            </feComposite>
        </filter>";

    private static string Doc(string filter, string rect) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><defs>{filter}</defs>{rect}</svg>";

    /// <summary>Renders one SVG document and returns the fill of its single rect.</summary>
    private static BColor RenderedRectFill(string svgXml)
    {
        SvgFilterTable.Reset();
        SvgRenderer.CollectColorFilters(svgXml);
        SvgRenderer.CollectFloodFilters(svgXml);

        var items = SvgRenderer.RenderSvgContent(svgXml, new RectangleF(0, 0, 100, 100));
        var rect = items.OfType<DrawSvgRectItem>().Single();
        return rect.Fill;
    }

    private static string Describe(BColor c) => $"rgba({c.R},{c.G},{c.B},{c.A})";

    /// <summary>
    /// The WPT case. <c>#ffaa00</c> inverts to (0, 0.333, 1); the arithmetic composite multiplies the
    /// premultiplied channels by 255 and every non-zero one saturates, giving cyan.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void InvertThenSaturate_TurnsOrangeIntoCyan()
    {
        var fill = RenderedRectFill(Doc(InvertAndSaturate,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" filter=\"url(#invert)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 0, 255, 255), fill);
    }

    /// <summary>
    /// A shape that does not reference the filter is untouched — the table is keyed by
    /// <c>url(#id)</c>, not applied to everything in the document.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void UnreferencedFilter_LeavesTheShapeAlone()
    {
        var fill = RenderedRectFill(Doc(InvertAndSaturate,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 255, 170, 0), fill);
    }

    /// <summary>
    /// The default filter colour space is linearRGB, which this does not model, so a filter that does
    /// not declare sRGB is left unapplied rather than computed in the wrong space.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void WithoutExplicitSrgb_TheFilterIsNotModelled()
    {
        var filter = InvertAndSaturate.Replace(" color-interpolation-filters=\"sRGB\"", string.Empty);
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" filter=\"url(#invert)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 255, 170, 0), fill);
    }

    /// <summary>
    /// A primitive outside the modelled pair makes the whole chain unmodelled — a partial application
    /// would be worse than none, since the un-modelled primitive is exactly the one that changes the
    /// pixels the most.
    /// <para>
    /// <c>feFlood</c> is deliberately absent from the cases below. Adding one does stop the colour
    /// chain being modelled, but it then trips <see cref="SvgRenderer.CollectFloodFilters"/>, which
    /// takes the first <c>feFlood</c> in a filter body whatever else is in it — a pre-existing
    /// over-match this change neither introduces nor fixes, so asserting on it here would pin
    /// behaviour that is wrong for a different reason.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("<feGaussianBlur stdDeviation=\"4\"/>")]
    [InlineData("<feOffset dx=\"10\" dy=\"10\"/>")]
    public void AnUnmodelledPrimitiveInTheChain_LeavesTheShapeUnfiltered(string extra)
    {
        var filter = InvertAndSaturate.Replace("</filter>", extra + "</filter>");
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" filter=\"url(#invert)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 255, 170, 0), fill);
    }

    /// <summary>
    /// The shorthand <c>feColorMatrix</c> types are not the matrix form and are not modelled.
    /// </summary>
    [Theory]
    [InlineData("saturate")]
    [InlineData("hueRotate")]
    [InlineData("luminanceToAlpha")]
    public void AShorthandColorMatrixType_IsNotModelled(string type)
    {
        var filter = $@"<filter id=""f"" color-interpolation-filters=""sRGB"">
            <feColorMatrix type=""{type}"" values=""0"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" filter=\"url(#f)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 255, 170, 0), fill);
    }

    /// <summary>
    /// Only the arithmetic <c>feComposite</c> operator is a pure colour operation; the Porter-Duff
    /// operators combine two different inputs, which one colour cannot represent.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void APorterDuffComposite_IsNotModelled()
    {
        var filter = InvertAndSaturate.Replace("operator=\"arithmetic\" k2=\"255\"", "operator=\"in\"");
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" filter=\"url(#invert)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 255, 170, 0), fill);
    }

    /// <summary>
    /// The chain has to be straight. A primitive reading a result other than the previous one's is a
    /// graph, not a chain, so the model declines it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void APrimitiveReadingAnEarlierResult_IsNotModelled()
    {
        var filter = InvertAndSaturate.Replace(
            "in=\"ef0\" in2=\"ef0\"", "in=\"ef0\" in2=\"SourceGraphic\"");
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" filter=\"url(#invert)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 255, 170, 0), fill);
    }

    /// <summary>
    /// A stroked shape is not one colour, so recolouring its fill would not describe what the filter
    /// does to it. It is left unfiltered.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void AStrokedShape_IsLeftUnfiltered()
    {
        var fill = RenderedRectFill(Doc(InvertAndSaturate,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" stroke=\"blue\" stroke-width=\"2\" filter=\"url(#invert)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 255, 170, 0), fill);
    }

    /// <summary>
    /// A plain <c>feColorMatrix</c> with no composite: the identity matrix must round-trip the colour
    /// exactly, which is the guard against the arithmetic in <see cref="SvgColorFilter.Apply"/>
    /// drifting.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void AnIdentityColorMatrix_LeavesTheColourExactlyAsItWas()
    {
        var filter = @"<filter id=""f"" color-interpolation-filters=""sRGB"">
            <feColorMatrix values=""1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 1 0"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#3366cc\" filter=\"url(#f)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 0x33, 0x66, 0xCC), fill);
    }

    /// <summary>
    /// A matrix that zeroes the alpha row makes the shape fully transparent — the same rule that
    /// keeps the area *outside* a filtered shape transparent, which is why a colour-only chain does
    /// not need a filter region to be modelled correctly here.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void AMatrixThatZeroesAlpha_MakesTheShapeTransparent()
    {
        var filter = @"<filter id=""f"" color-interpolation-filters=""sRGB"">
            <feColorMatrix values=""1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 0 0"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#ffaa00\" filter=\"url(#f)\"></rect>"));

        Assert.True(fill.A == 0, $"expected a fully transparent fill, got {Describe(fill)}");
    }

    /// <summary>
    /// An <c>feFlood</c>-only filter still resolves through the flood path, which this must not have
    /// displaced: the two models cover disjoint filters.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void AFloodOnlyFilter_StillFloods()
    {
        var filter = "<filter id=\"f\"><feFlood flood-color=\"green\" /></filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"10\" y=\"10\" width=\"100\" height=\"100\" fill=\"red\" filter=\"url(#f)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, 0, 128, 0), fill);
    }

    // ── feFlood as a backdrop, not as the result ──────────────────────────

    /// <summary>
    /// A flood that another primitive consumes describes a <em>backdrop</em>, not the filter's
    /// result. Treating any filter containing an <c>feFlood</c> as flood-only replaced the shape
    /// with a solid flood-coloured rectangle 20% larger than itself — which is what the whole
    /// <c>css/filter-effects/tainting-*</c> family and
    /// <c>conformance-checkers/html-svg/filters-blend-01-b</c> rendered as.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void AFloodFeedingAnotherPrimitive_DoesNotFloodTheShape()
    {
        var filter = @"<filter id=""f"">
            <feFlood flood-color=""green"" result=""bg"" />
            <feOffset in=""bg"" dx=""1"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"red\" filter=\"url(#f)\"></rect>"));

        // feOffset is not modelled, so the shape renders unfiltered — but it must not be flooded.
        Assert.Equal(BColor.Red, fill);
    }

    [Theory]
    // Opaque lime over opaque blue: each mode is its own channel function.
    [InlineData("normal", 0, 0, 255)]
    [InlineData("multiply", 0, 0, 0)]
    [InlineData("screen", 0, 255, 255)]
    [InlineData("darken", 0, 0, 0)]
    [InlineData("lighten", 0, 255, 255)]
    public void AFloodBlendedUnderTheSource_ProducesTheBlendedColour(
        string mode, int r, int g, int b)
    {
        var filter = $@"<filter id=""f"" color-interpolation-filters=""sRGB"">
            <feFlood flood-color=""#0f0"" result=""bg"" />
            <feBlend in=""SourceGraphic"" in2=""bg"" mode=""{mode}"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#00f\" filter=\"url(#f)\"></rect>"));

        Assert.Equal(BColor.FromArgb(255, r, g, b), fill);
    }

    [Fact(Timeout = 600000)]
    public void AFloodsOpacityReachesTheBlend()
    {
        // A half-transparent lime backdrop under opaque blue: source-over leaves the source, so the
        // flood's alpha must not be dropped on the way in — it is what makes this differ from the
        // opaque case for every non-normal mode.
        var filter = @"<filter id=""f"" color-interpolation-filters=""sRGB"">
            <feFlood flood-color=""#0f0"" flood-opacity=""0.5"" result=""bg"" />
            <feBlend in=""SourceGraphic"" in2=""bg"" mode=""multiply"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#00f\" filter=\"url(#f)\"></rect>"));

        // αs = 1, so the result is fully opaque and is B(cb, cs) weighted by αb plus the source
        // where the backdrop is transparent: blue × 0.5 in the blue channel.
        Assert.Equal(255, fill.A);
        Assert.Equal(0, fill.R);
        Assert.Equal(0, fill.G);
        Assert.InRange(fill.B, 126, 129);
    }

    [Fact(Timeout = 600000)]
    public void AnUnmodelledBlendMode_LeavesTheShapeUnfiltered()
    {
        var filter = @"<filter id=""f"" color-interpolation-filters=""sRGB"">
            <feFlood flood-color=""#0f0"" result=""bg"" />
            <feBlend in=""SourceGraphic"" in2=""bg"" mode=""color-dodge"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#00f\" filter=\"url(#f)\"></rect>"));

        Assert.Equal(BColor.Blue, fill);
    }

    [Fact(Timeout = 600000)]
    public void ABlendWhoseBackdropIsNotAFlood_LeavesTheShapeUnfiltered()
    {
        // in2 names something this model cannot reduce to one colour, so the whole chain declines.
        var filter = @"<filter id=""f"" color-interpolation-filters=""sRGB"">
            <feBlend in=""SourceGraphic"" in2=""BackgroundImage"" mode=""multiply"" />
        </filter>";
        var fill = RenderedRectFill(Doc(filter,
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"#00f\" filter=\"url(#f)\"></rect>"));

        Assert.Equal(BColor.Blue, fill);
    }
}
