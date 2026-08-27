using Broiler.Graphics;
using Broiler.Layout.Engine;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS 2.1 §8.5.3: <c>inset</c> and <c>outset</c> paint a bevel rather than four flat sides. The
/// spec leaves the shades to the UA, so every expectation here is a number measured off Chromium
/// directly — <c>&lt;div style="border:8px inset …"&gt;</c> screenshotted and sampled per side.
/// </summary>
public class BorderBevelTests
{
    private static BColor Rgb(int r, int g, int b) => BColor.FromArgb(255, (byte)r, (byte)g, (byte)b);

    private static (int R, int G, int B) Tuple(BColor c) => (c.R, c.G, c.B);

    // ── Which styles bevel ───────────────────────────────────────────────────

    [Theory]
    [InlineData("inset", true)]
    [InlineData("outset", true)]
    [InlineData("groove", true)]
    [InlineData("ridge", true)]
    [InlineData("INSET", true)]
    [InlineData("solid", false)]
    [InlineData("dashed", false)]
    [InlineData("double", false)]
    [InlineData("none", false)]
    [InlineData(null, false)]
    public void IsBevelled_Covers_The_Four_3D_Styles(string? style, bool expected) =>
        Assert.Equal(expected, BorderBevel.IsBevelled(style));

    [Fact(Timeout = 600000)]
    public void A_Style_That_Does_Not_Bevel_Passes_Its_Colour_Through()
    {
        var color = Rgb(200, 100, 50);
        foreach (var style in new[] { "solid", "dashed", "double", "none", null })
        {
            Assert.Equal(Tuple(color), Tuple(BorderBevel.SideColor(style, isTopOrLeft: true, color)));
            Assert.Equal(Tuple(color), Tuple(BorderBevel.SideColor(style, isTopOrLeft: false, color)));
        }
    }

    // ── groove and ridge split each side lengthwise ──────────────────────────

    /// <summary>Only groove and ridge split, and only when there is room for two halves.</summary>
    [Theory]
    [InlineData("groove", 8, true)]
    [InlineData("ridge", 8, true)]
    [InlineData("groove", 2, true)]
    [InlineData("groove", 1, false)]     // no room — collapses to one stroke
    [InlineData("ridge", 1, false)]
    [InlineData("inset", 8, false)]      // bevelled, but one shade per side
    [InlineData("outset", 8, false)]
    [InlineData("solid", 8, false)]
    public void SplitsIntoHalves_Needs_A_Split_Style_And_Two_Pixels(
        string style, double width, bool expected) =>
        Assert.Equal(expected, BorderBevel.SplitsIntoHalves(style, width));

    /// <summary>
    /// The split sits at <c>ceil(width / 2)</c> from the outer edge — measured on Chromium, which
    /// paints a 3px groove as two dark rows then one light, and a 5px one as three then two.
    /// </summary>
    [Theory]
    [InlineData(2, 1, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(8, 4, 4)]
    [InlineData(9, 5, 4)]
    public void HalfWidth_Splits_At_The_Ceiling(double width, double outer, double inner)
    {
        Assert.Equal(outer, BorderBevel.HalfWidth("groove", width, outerHalf: true));
        Assert.Equal(inner, BorderBevel.HalfWidth("groove", width, outerHalf: false));
        Assert.Equal(outer, BorderBevel.HalfWidth("ridge", width, outerHalf: true));
        Assert.Equal(inner, BorderBevel.HalfWidth("ridge", width, outerHalf: false));
    }

    /// <summary>
    /// A style that does not split has an outer "half" that is the whole side and no inner one, so
    /// it still emits exactly one ring.
    /// </summary>
    [Theory]
    [InlineData("solid")]
    [InlineData("inset")]
    [InlineData("outset")]
    [InlineData(null)]
    public void An_Unsplit_Style_Puts_Its_Whole_Width_In_The_Outer_Half(string? style)
    {
        Assert.Equal(8, BorderBevel.HalfWidth(style, 8, outerHalf: true));
        Assert.Equal(0, BorderBevel.HalfWidth(style, 8, outerHalf: false));
    }

    /// <summary>
    /// A groove is carved into the canvas: its outer half reads as <c>inset</c> (top and left in
    /// shadow) and its inner half as <c>outset</c>. Chromium paints an 8px grey groove's top as
    /// four rows of <c>rgb(44,44,44)</c> then four of <c>rgb(128,128,128)</c>, and its right as the
    /// reverse.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Groove_Is_Inset_Outside_And_Outset_Inside()
    {
        var grey = Rgb(128, 128, 128);
        Assert.Equal((44, 44, 44), Tuple(BorderBevel.HalfColor("groove", true, grey, 8, outerHalf: true)));
        Assert.Equal((128, 128, 128), Tuple(BorderBevel.HalfColor("groove", true, grey, 8, outerHalf: false)));
        Assert.Equal((128, 128, 128), Tuple(BorderBevel.HalfColor("groove", false, grey, 8, outerHalf: true)));
        Assert.Equal((44, 44, 44), Tuple(BorderBevel.HalfColor("groove", false, grey, 8, outerHalf: false)));
    }

    /// <summary>A ridge stands proud of the canvas, so it is the groove the other way round.</summary>
    [Fact(Timeout = 600000)]
    public void Ridge_Is_Outset_Outside_And_Inset_Inside()
    {
        var grey = Rgb(128, 128, 128);
        Assert.Equal((128, 128, 128), Tuple(BorderBevel.HalfColor("ridge", true, grey, 8, outerHalf: true)));
        Assert.Equal((44, 44, 44), Tuple(BorderBevel.HalfColor("ridge", true, grey, 8, outerHalf: false)));
        Assert.Equal((44, 44, 44), Tuple(BorderBevel.HalfColor("ridge", false, grey, 8, outerHalf: true)));
        Assert.Equal((128, 128, 128), Tuple(BorderBevel.HalfColor("ridge", false, grey, 8, outerHalf: false)));
    }

    /// <summary>
    /// At 1px there is no room for two halves, and Chromium paints the lit shade on <em>all four</em>
    /// sides — not the outer half's shade, which would differ per side.
    /// </summary>
    [Theory]
    [InlineData("groove")]
    [InlineData("ridge")]
    public void A_One_Pixel_Groove_Or_Ridge_Is_A_Single_Lit_Stroke(string style)
    {
        var grey = Rgb(128, 128, 128);
        foreach (var isTopOrLeft in new[] { true, false })
        {
            Assert.Equal((128, 128, 128),
                Tuple(BorderBevel.HalfColor(style, isTopOrLeft, grey, 1, outerHalf: true)));
        }
    }

    // ── The shades themselves ────────────────────────────────────────────────

    /// <summary>
    /// Every one of these is a Chromium measurement. The darkened side scales all three channels by
    /// the factor that takes the <em>largest</em> one down by 0.33 of full intensity, which is what
    /// keeps the hue: <c>rgb(200,100,50)</c> goes to <c>rgb(116,58,29)</c> — all ×0.58 — where a
    /// per-channel subtraction would have given <c>rgb(116,16,0)</c>.
    /// </summary>
    [Theory]
    [InlineData(255, 255, 255, 171, 171, 171)]
    [InlineData(200, 200, 200, 116, 116, 116)]
    [InlineData(128, 128, 128, 44, 44, 44)]
    [InlineData(255, 0, 0, 171, 0, 0)]
    [InlineData(0, 128, 0, 0, 44, 0)]
    [InlineData(200, 100, 50, 116, 58, 29)]
    [InlineData(238, 238, 238, 154, 154, 154)]   // the UA bevel base — an <hr>/<iframe> border
    [InlineData(64, 64, 64, 0, 0, 0)]            // already darker than the step: clamps to black
    [InlineData(0, 0, 0, 0, 0, 0)]               // black has nothing left to darken
    public void Darken_Matches_Chromium(int r, int g, int b, int dr, int dg, int db) =>
        Assert.Equal((dr, dg, db), Tuple(BorderBevel.Darken(Rgb(r, g, b))));

    /// <summary>
    /// The lit side is the colour itself — except black, which would otherwise be indistinguishable
    /// from its own darkened side and leave no bevel at all.
    /// </summary>
    [Theory]
    [InlineData(255, 255, 255, 255, 255, 255)]
    [InlineData(128, 128, 128, 128, 128, 128)]
    [InlineData(200, 100, 50, 200, 100, 50)]
    [InlineData(238, 238, 238, 238, 238, 238)]
    [InlineData(0, 0, 0, 0x54, 0x54, 0x54)]
    public void Lighten_Is_The_Colour_Itself_Except_Black(int r, int g, int b, int lr, int lg, int lb) =>
        Assert.Equal((lr, lg, lb), Tuple(BorderBevel.Lighten(Rgb(r, g, b))));

    // ── Which side gets which shade ──────────────────────────────────────────

    /// <summary>
    /// <c>inset</c> sinks the box: the top and left are in shadow, the bottom and right catch the
    /// light. Measured on Chromium as <c>rgb(44,44,44)</c> / <c>rgb(128,128,128)</c> for a grey.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Inset_Darkens_The_Top_And_Left()
    {
        var grey = Rgb(128, 128, 128);
        Assert.Equal((44, 44, 44), Tuple(BorderBevel.SideColor("inset", isTopOrLeft: true, grey)));
        Assert.Equal((128, 128, 128), Tuple(BorderBevel.SideColor("inset", isTopOrLeft: false, grey)));
    }

    /// <summary><c>outset</c> raises it, so the shading is the other way round.</summary>
    [Fact(Timeout = 600000)]
    public void Outset_Darkens_The_Bottom_And_Right()
    {
        var grey = Rgb(128, 128, 128);
        Assert.Equal((128, 128, 128), Tuple(BorderBevel.SideColor("outset", isTopOrLeft: true, grey)));
        Assert.Equal((44, 44, 44), Tuple(BorderBevel.SideColor("outset", isTopOrLeft: false, grey)));
    }

    /// <summary>
    /// The default <c>&lt;iframe&gt;</c> and <c>&lt;hr&gt;</c> border, end to end: the UA stylesheet
    /// states <c>#EEEEEE</c> as the bevel base and the shading turns it into the <c>#9A9A9A</c> /
    /// <c>#EEEEEE</c> pair every browser paints. This pair is what
    /// <c>css/css-color-adjust/…/color-scheme-iframe-background</c> is scored against.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_Ua_Bevel_Base_Produces_The_Default_Frame_Border()
    {
        var basis = Rgb(0xEE, 0xEE, 0xEE);
        Assert.Equal((0x9A, 0x9A, 0x9A), Tuple(BorderBevel.SideColor("inset", isTopOrLeft: true, basis)));
        Assert.Equal((0xEE, 0xEE, 0xEE), Tuple(BorderBevel.SideColor("inset", isTopOrLeft: false, basis)));
    }

    /// <summary>An explicitly black bevel keeps Chromium's black / <c>#545454</c> pair.</summary>
    [Fact(Timeout = 600000)]
    public void An_Explicitly_Black_Bevel_Lightens_Rather_Than_Darkens()
    {
        var black = Rgb(0, 0, 0);
        Assert.Equal((0, 0, 0), Tuple(BorderBevel.SideColor("inset", isTopOrLeft: true, black)));
        Assert.Equal((0x54, 0x54, 0x54), Tuple(BorderBevel.SideColor("inset", isTopOrLeft: false, black)));
    }

    /// <summary>Alpha rides through both shades, so a translucent bevel stays translucent.</summary>
    [Fact(Timeout = 600000)]
    public void Alpha_Is_Preserved()
    {
        var translucent = BColor.FromArgb(128, 200, 100, 50);
        Assert.Equal(128, BorderBevel.SideColor("inset", isTopOrLeft: true, translucent).A);
        Assert.Equal(128, BorderBevel.SideColor("inset", isTopOrLeft: false, translucent).A);

        var translucentBlack = BColor.FromArgb(64, 0, 0, 0);
        Assert.Equal(64, BorderBevel.Lighten(translucentBlack).A);
        Assert.Equal(64, BorderBevel.Darken(translucentBlack).A);
    }
}
