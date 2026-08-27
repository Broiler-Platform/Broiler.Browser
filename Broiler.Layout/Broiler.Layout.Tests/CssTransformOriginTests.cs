using System.Drawing;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Transforms 1 §8: the <c>transform-origin</c> grammar, shared by the three places that need
/// to agree about where an element's transform is applied — the paint walker, the script bridge's
/// transform chain (what <c>getBoundingClientRect</c> reports) and the SVG renderer's
/// <c>transform-box</c> handling.
/// </summary>
/// <remarks>
/// <para>
/// They did not agree. The paint walker did not read the property at all and used the box centre
/// for every element, so a page's painted output and the geometry its own script measured put a
/// transformed element in different places. The bridge read it, but by splitting on whitespace and
/// taking the components in order — which gets a lone <c>top</c>, the <c>top left</c> spelling and
/// an invalid pair all wrong.
/// </para>
/// <para>
/// The reference box below is deliberately offset from the origin (10, 20) as well as sized, so a
/// case that forgets to add the box's own corner is not hidden by a box at (0, 0).
/// </para>
/// </remarks>
public sealed class CssTransformOriginTests
{
    private static readonly RectangleF Box = new(10, 20, 100, 200);

    private static PointF ForCssBox(string value) =>
        CssTransformOrigin.Resolve(value, Box, initialIsBoxCorner: false);

    private static PointF ForSvgElement(string value) =>
        CssTransformOrigin.Resolve(value, Box, initialIsBoxCorner: true);

    private static void AssertPoint(float x, float y, PointF actual)
    {
        Assert.Equal(x, actual.X, 3);
        Assert.Equal(y, actual.Y, 3);
    }

    // ---- the initial value, which is what separates a CSS box from an SVG element ----

    /// <summary>A CSS box's initial <c>transform-origin</c> is <c>50% 50%</c> — its centre.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ACssBoxDefaultsToItsCentre(string? value) => AssertPoint(60, 120, ForCssBox(value));

    /// <summary>
    /// An SVG element has no CSS layout box, and §8 gives one of those a used value of <c>0 0</c>:
    /// the reference box's own corner. Same declaration, different point — which is why the caller
    /// states which it is rather than this guessing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnSvgElementDefaultsToTheBoxCorner(string? value) => AssertPoint(10, 20, ForSvgElement(value));

    // ---- lengths, percentages and keywords ----

    [Fact(Timeout = 600000)]
    public void ZeroZeroIsTheBoxCorner() => AssertPoint(10, 20, ForCssBox("0 0"));

    [Fact(Timeout = 600000)]
    public void PercentagesResolveAgainstTheBox() => AssertPoint(110, 220, ForCssBox("100% 100%"));

    [Fact(Timeout = 600000)]
    public void LengthsAreMeasuredFromTheBoxCorner() => AssertPoint(35, 60, ForCssBox("25px 40px"));

    [Theory]
    [InlineData("left top", 10, 20)]
    [InlineData("right bottom", 110, 220)]
    [InlineData("center center", 60, 120)]
    [InlineData("left bottom", 10, 220)]
    [InlineData("right top", 110, 20)]
    public void TheKeywordPairsNameTheCorners(string value, float x, float y) =>
        AssertPoint(x, y, ForCssBox(value));

    /// <summary>
    /// The keyword pair may be written the other way round: <c>top left</c> is the same point as
    /// <c>left top</c>. Taking the components in order put the vertical keyword on the horizontal
    /// axis, where <c>top</c> reads as <c>left</c> — the same answer by luck for that pair, and the
    /// wrong one for <c>bottom left</c>.
    /// </summary>
    [Theory]
    [InlineData("top left", 10, 20)]
    [InlineData("bottom left", 10, 220)]
    [InlineData("top right", 110, 20)]
    [InlineData("bottom right", 110, 220)]
    public void AVerticalKeywordMayLead(string value, float x, float y) =>
        AssertPoint(x, y, ForCssBox(value));

    // ---- one component ----

    /// <summary>One value sets that axis and centres the other.</summary>
    [Fact(Timeout = 600000)]
    public void ALoneHorizontalValueCentresTheOther() => AssertPoint(110, 120, ForCssBox("right"));

    /// <summary>
    /// And a lone <c>top</c> or <c>bottom</c> names the <b>vertical</b> axis, so it moves y — not
    /// x, which is where reading the first component as the horizontal one put it.
    /// </summary>
    [Theory]
    [InlineData("top", 60, 20)]
    [InlineData("bottom", 60, 220)]
    public void ALoneVerticalKeywordMovesTheVerticalAxis(string value, float x, float y) =>
        AssertPoint(x, y, ForCssBox(value));

    [Fact(Timeout = 600000)]
    public void ALoneLengthSetsXAndCentresY() => AssertPoint(40, 120, ForCssBox("30px"));

    // ---- invalid declarations are dropped whole ----

    /// <summary>
    /// A <c>&lt;length-percentage&gt;</c> may not follow a vertical keyword, so <c>top 100%</c> is
    /// not a re-ordered <c>100% top</c> — it is invalid, and an invalid declaration is dropped
    /// whole and takes the initial value. The twelve WPT
    /// <c>css-transforms/transform-origin/svg-origin-relative-length-invalid-*</c> cases are built
    /// to catch exactly that.
    /// </summary>
    [Theory]
    [InlineData("top 100%")]
    [InlineData("bottom 25px")]
    [InlineData("left right")]
    [InlineData("top bottom")]
    [InlineData("nonsense")]
    public void AnInvalidDeclarationTakesTheInitialValue(string value)
    {
        AssertPoint(60, 120, ForCssBox(value));
        AssertPoint(10, 20, ForSvgElement(value));
    }

    /// <summary>
    /// The asymmetry that makes <c>top 100%</c> invalid is not "a keyword and a length cannot mix":
    /// the two-value form is <c>[left|center|right|&lt;lp&gt;] [top|center|bottom|&lt;lp&gt;]</c>, so a
    /// length in the horizontal slot followed by a vertical keyword is perfectly valid. Only a
    /// <em>leading</em> vertical keyword needs its partner to be a keyword too.
    /// </summary>
    [Theory]
    [InlineData("10px top", 20, 20)]
    [InlineData("25% bottom", 35, 220)]
    [InlineData("left 30px", 10, 50)]
    public void ALengthMayShareTheDeclarationWithAKeyword(string value, float x, float y) =>
        AssertPoint(x, y, ForCssBox(value));

    /// <summary>The z component a third value carries is dropped — none of this is 3D.</summary>
    [Fact(Timeout = 600000)]
    public void AThirdComponentIsIgnored() => AssertPoint(10, 20, ForCssBox("left top 40px"));
}
