using System.Drawing;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Values 4 §6.9 <c>&lt;position&gt;</c>. Paint read the components positionally — first
/// horizontal, second vertical — which is only the one- and two-component forms; the three- and
/// four-component edge-offset forms were dropped on the floor, silently, resolving to the origin.
/// </summary>
/// <remarks>
/// Every <c>css-images/object-fit-*</c> reference states four of its seven positions in that syntax
/// (<c>top 25% left 25%</c>, <c>bottom 1px right 2px</c>, <c>top 3px center</c>,
/// <c>center right 25%</c>), so the reference side of those 200-odd reftests was wrong before the
/// test side had a chance to be.
/// </remarks>
public sealed class CssPositionValueTests
{
    /// <summary>A 100×100 area holding a 20×40 object: 80 of free width and 60 of free height.</summary>
    private static PointF Resolve(string position) =>
        CssPositionValue.Resolve(position, new SizeF(100, 100), new SizeF(20, 40), emSize: 16);

    // ─────────────────────────── one and two components ───────────────────────────

    [Theory]
    [InlineData("left", 0, 30)]
    [InlineData("right", 80, 30)]
    [InlineData("top", 40, 0)]
    [InlineData("bottom", 40, 60)]
    [InlineData("center", 40, 30)]
    [InlineData("25%", 20, 30)]
    public void A_Single_Component_Centres_The_Other_Axis(string position, float x, float y) =>
        Assert.Equal(new PointF(x, y), Resolve(position));

    /// <summary>A keyword pair may be written either way round; a length pair may not.</summary>
    [Theory]
    [InlineData("left top", 0, 0)]
    [InlineData("top left", 0, 0)]
    [InlineData("top right", 80, 0)]
    [InlineData("bottom left", 0, 60)]
    [InlineData("center right", 80, 30)]
    [InlineData("right center", 80, 30)]
    [InlineData("25% 50%", 20, 30)]
    [InlineData("10px 20px", 10, 20)]
    public void Two_Components_Are_Horizontal_Then_Vertical(string position, float x, float y) =>
        Assert.Equal(new PointF(x, y), Resolve(position));

    /// <summary>
    /// A percentage is a fraction of the free space, not of the area — so <c>100%</c> puts the
    /// object's far edge on the area's, rather than a whole area-width past it.
    /// </summary>
    [Theory]
    [InlineData("0% 0%", 0, 0)]
    [InlineData("50% 50%", 40, 30)]
    [InlineData("100% 100%", 80, 60)]
    public void A_Percentage_Resolves_Against_The_Free_Space(string position, float x, float y) =>
        Assert.Equal(new PointF(x, y), Resolve(position));

    // ───────────────────────── three and four components ─────────────────────────

    /// <summary>
    /// The edge-offset form names the edge each offset is measured from, so <c>right 25%</c> is 25%
    /// of the free space in from the right — 60 here, not 20.
    /// </summary>
    [Theory]
    [InlineData("top 25% left 25%", 20, 15)]
    [InlineData("left 25% top 25%", 20, 15)]
    [InlineData("bottom 1px right 2px", 78, 59)]
    [InlineData("top 3px center", 40, 3)]
    [InlineData("center right 25%", 60, 30)]
    [InlineData("right 25% center", 60, 30)]
    [InlineData("bottom 10px left 10px", 10, 50)]
    public void An_Edge_Keyword_Says_Which_Side_Its_Offset_Is_Measured_From(
        string position, float x, float y) =>
        Assert.Equal(new PointF(x, y), Resolve(position));

    /// <summary>
    /// Component count is the spec's own disambiguation, and the reason it cannot be skipped:
    /// <c>right 25%</c> is the two-component form (x flush right, y a quarter down) while
    /// <c>center right 25%</c> is the edge-offset one (a quarter of the free width in from the
    /// right). The same two tokens, read two ways.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Two_Components_Are_Positional_Where_Three_Are_Edge_Offsets()
    {
        Assert.Equal(new PointF(80, 15), Resolve("right 25%"));
        Assert.Equal(new PointF(60, 30), Resolve("center right 25%"));
    }

    /// <summary>An edge with no offset of its own is that edge flush.</summary>
    [Theory]
    [InlineData("right top 10px", 80, 10)]
    [InlineData("left 10px bottom", 10, 60)]
    public void An_Edge_Without_An_Offset_Is_Flush(string position, float x, float y) =>
        Assert.Equal(new PointF(x, y), Resolve(position));

    // ───────────────────────────────── edge cases ─────────────────────────────────

    /// <summary>
    /// An object wider than its area has negative free space, and an offset from the far edge
    /// subtracts from that: 20px in from the right of a 100px area holding a 120px object is −40.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Offset_May_Leave_The_Area() =>
        Assert.Equal(new PointF(-40, 60), CssPositionValue.Resolve(
            "bottom right 20px", new SizeF(100, 100), new SizeF(120, 40), emSize: 16));

    /// <summary>An object larger than its area has negative free space, and centring halves it.</summary>
    [Fact(Timeout = 600000)]
    public void An_Object_Larger_Than_Its_Area_Centres_Negatively() =>
        Assert.Equal(new PointF(-25, -25), CssPositionValue.Resolve(
            "center", new SizeF(100, 100), new SizeF(150, 150), emSize: 16));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Declaration_Resolves_To_The_Origin(string? position) =>
        Assert.Equal(PointF.Empty, CssPositionValue.Resolve(
            position, new SizeF(100, 100), new SizeF(20, 40), emSize: 16));

    /// <summary>An offset in font-relative units resolves against the element's font size.</summary>
    [Fact(Timeout = 600000)]
    public void An_Em_Offset_Resolves_Against_The_Font_Size() =>
        Assert.Equal(new PointF(32, 30), CssPositionValue.Resolve(
            "2em center", new SizeF(100, 100), new SizeF(20, 40), emSize: 16));

    /// <summary>
    /// A <c>calc()</c> is one component however much whitespace it contains. Splitting on
    /// whitespace alone made <c>calc(50% - 10px) center</c> four components, and four components
    /// are read as the edge-offset form — a different grammar, and an unrelated answer.
    /// </summary>
    [Theory]
    [InlineData("calc(50% - 10px) center", 30, 30)]
    [InlineData("calc(10px + 10px) calc(100% - 20px)", 20, 40)]
    public void A_Calc_Is_One_Component(string position, float x, float y) =>
        Assert.Equal(new PointF(x, y), Resolve(position));
}
