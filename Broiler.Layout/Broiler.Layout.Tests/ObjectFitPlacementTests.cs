using System.Drawing;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Images 3 §5.5 <c>object-fit</c> and §5.6 <c>object-position</c>: nothing read either
/// property, so every replaced element stretched to its content box — <c>fill</c> behaviour under
/// whatever the author wrote. <c>object-fit-contain-svg-001i</c>, <c>-fill-svg-001i</c> and
/// <c>-none-svg-001i</c> differ only in that one declaration and rendered to byte-identical PNGs.
/// </summary>
/// <remarks>
/// The cases below are the ones the 42 <c>css-images/object-fit-*i</c> tests turn on: the four fit
/// keywords against a box both wider and narrower than the content's ratio, <c>scale-down</c>'s
/// choice between two of them, content that carries a ratio but no intrinsic size (an SVG with only
/// a <c>viewBox</c> — which reports the 300×150 default object size and must not be sized by it),
/// and the clip that an object overflowing its content box needs.
/// </remarks>
public sealed class ObjectFitPlacementTests
{
    /// <summary>A 16×8 content box's worth of image — the WPT tests' <c>colors-16x8.svg</c>.</summary>
    private static readonly SizeF Wide = new(16, 8);

    private static ObjectFitPlacement.Placement Resolve(
        RectangleF contentBox, SizeF intrinsic, string fit, string position = "50% 50%", float ratio = 0)
        => ObjectFitPlacement.Resolve(contentBox, intrinsic, ratio, fit, position, emSize: 16);

    // ───────────────────────────── the concrete object size ─────────────────────────────

    /// <summary><c>fill</c> is the initial value and stretches to the box, which is what every
    /// replaced element did before any of this was read.</summary>
    [Theory]
    [InlineData("fill")]
    [InlineData("")]
    [InlineData("not-a-keyword")]
    public void Fill_Stretches_To_The_Content_Box(string fit)
    {
        var placement = Resolve(new RectangleF(10, 20, 48, 32), Wide, fit);

        Assert.Equal(new RectangleF(10, 20, 48, 32), placement.Dest);
        Assert.False(placement.NeedsClip);
    }

    /// <summary>
    /// <c>contain</c> is the largest rectangle of the content's ratio that fits: a 2∶1 image in a
    /// 48×32 box is 48×24, and in a 32×48 box is 32×16.
    /// </summary>
    [Theory]
    [InlineData(48, 32, 48, 24)]
    [InlineData(32, 48, 32, 16)]
    [InlineData(8, 8, 8, 4)]
    public void Contain_Fits_Inside_The_Content_Box(float boxW, float boxH, float fitW, float fitH)
    {
        var placement = Resolve(new RectangleF(0, 0, boxW, boxH), Wide, "contain", "left top");

        Assert.Equal(new RectangleF(0, 0, fitW, fitH), placement.Dest);
        Assert.False(placement.NeedsClip);
    }

    /// <summary>
    /// <c>cover</c> is the smallest rectangle of the content's ratio that covers the box, so it
    /// always overflows on one axis unless the ratios match exactly.
    /// </summary>
    [Theory]
    [InlineData(48, 32, 64, 32)]
    [InlineData(32, 48, 96, 48)]
    public void Cover_Covers_The_Content_Box_And_Is_Clipped(float boxW, float boxH, float fitW, float fitH)
    {
        var placement = Resolve(new RectangleF(0, 0, boxW, boxH), Wide, "cover", "left top");

        Assert.Equal(new RectangleF(0, 0, fitW, fitH), placement.Dest);
        Assert.True(placement.NeedsClip);
    }

    /// <summary>A box already at the content's ratio needs no clip under <c>cover</c>.</summary>
    [Fact(Timeout = 600000)]
    public void Cover_At_The_Contents_Own_Ratio_Needs_No_Clip()
    {
        var placement = Resolve(new RectangleF(0, 0, 32, 16), Wide, "cover");

        Assert.Equal(new RectangleF(0, 0, 32, 16), placement.Dest);
        Assert.False(placement.NeedsClip);
    }

    /// <summary><c>none</c> is the intrinsic size, whether that overflows the box or not.</summary>
    [Theory]
    [InlineData(48, 32, false)]
    [InlineData(8, 8, true)]
    public void None_Uses_The_Intrinsic_Size(float boxW, float boxH, bool clipped)
    {
        var placement = Resolve(new RectangleF(0, 0, boxW, boxH), Wide, "none", "left top");

        Assert.Equal(new SizeF(16, 8), placement.Dest.Size);
        Assert.Equal(clipped, placement.NeedsClip);
    }

    /// <summary>
    /// <c>scale-down</c> is whichever of <c>none</c> and <c>contain</c> is smaller: the intrinsic
    /// size while it fits, and the contained size once it does not.
    /// </summary>
    [Theory]
    [InlineData(48, 32, 16, 8)]
    [InlineData(8, 8, 8, 4)]
    public void ScaleDown_Takes_The_Smaller_Of_None_And_Contain(float boxW, float boxH, float fitW, float fitH)
    {
        var placement = Resolve(new RectangleF(0, 0, boxW, boxH), Wide, "scale-down", "left top");

        Assert.Equal(new SizeF(fitW, fitH), placement.Dest.Size);
        Assert.False(placement.NeedsClip);
    }

    // ─────────────────────── content with a ratio but no intrinsic size ───────────────────────

    /// <summary>
    /// An SVG with a <c>viewBox</c> and no <c>width</c>/<c>height</c> has a ratio and no size, and
    /// the size reported for it is the 300×150 default object size — whose ratio is 2∶1 and has
    /// nothing to do with the content's. Sizing <c>contain</c> by it put a 1∶2 image in a 48×24 box
    /// instead of a 16×32 one, which is what regressed <c>object-fit-cover-svg-004i</c> when the
    /// ratio was first taken from the reported size.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Ratio_Without_An_Intrinsic_Size_Sizes_By_The_Ratio()
    {
        var placement = Resolve(
            new RectangleF(0, 0, 48, 32), SizeF.Empty, "contain", "left top", ratio: 0.5f);

        Assert.Equal(new RectangleF(0, 0, 16, 32), placement.Dest);
    }

    /// <summary>
    /// With no intrinsic size, <c>none</c> falls through the default sizing algorithm (§5.1) onto
    /// the default object size constrained by the ratio — the same answer as <c>contain</c>.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("scale-down")]
    [InlineData("contain")]
    public void Without_An_Intrinsic_Size_None_And_ScaleDown_Are_Contain(string fit)
    {
        var placement = Resolve(
            new RectangleF(0, 0, 48, 32), SizeF.Empty, fit, "left top", ratio: 0.5f);

        Assert.Equal(new RectangleF(0, 0, 16, 32), placement.Dest);
    }

    /// <summary>
    /// Content with neither a size nor a ratio has nothing to fit against, so every keyword is
    /// <c>fill</c> — the box itself, drawn exactly as it was before this property was read.
    /// </summary>
    [Theory]
    [InlineData("contain")]
    [InlineData("cover")]
    [InlineData("none")]
    public void Without_A_Size_Or_A_Ratio_Every_Keyword_Fills(string fit)
    {
        var placement = Resolve(new RectangleF(5, 5, 48, 32), SizeF.Empty, fit);

        Assert.Equal(new RectangleF(5, 5, 48, 32), placement.Dest);
        Assert.False(placement.NeedsClip);
    }

    // ───────────────────────────────── object-position ─────────────────────────────────

    /// <summary>
    /// <c>object-position</c> places the concrete object in the free space the fit left over. A 2∶1
    /// image contained in a 48×32 box is 48×24, so the 8px of free height is all there is to move
    /// through — and the width has none, which is what makes the <c>right</c> cases land at 0.
    /// </summary>
    [Theory]
    [InlineData("top right", 0, 0)]
    [InlineData("bottom left", 0, 8)]
    [InlineData("50% 50%", 0, 4)]
    [InlineData("top 25% left 25%", 0, 2)]
    [InlineData("top 3px left 50%", 0, 3)]
    [InlineData("top 50% right 25%", 0, 4)]
    public void Object_Position_Places_The_Object_In_The_Free_Space(string position, float x, float y)
    {
        var placement = Resolve(new RectangleF(100, 200, 48, 32), Wide, "contain", position);

        Assert.Equal(new PointF(100 + x, 200 + y), placement.Dest.Location);
    }

    /// <summary>
    /// An offset from the far edge can push the object out of its box when the free space on that
    /// axis is smaller than the offset — <c>bottom 1px right 2px</c> against zero free width is 2px
    /// off the left edge, and needs the clip that says so.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Offset_Past_The_Free_Space_Overflows_And_Clips()
    {
        var placement = Resolve(new RectangleF(0, 0, 48, 32), Wide, "contain", "bottom 1px right 2px");

        Assert.Equal(new RectangleF(-2, 7, 48, 24), placement.Dest);
        Assert.True(placement.NeedsClip);
    }

    /// <summary>
    /// <c>fill</c> leaves no free space, so a percentage position cannot move the object — but an
    /// absolute offset from an edge still does, and the fast path for "nothing declared" must not
    /// swallow it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Fill_Still_Honours_An_Absolute_Object_Position()
    {
        var placement = Resolve(new RectangleF(0, 0, 48, 32), Wide, "fill", "bottom 1px right 2px");

        Assert.Equal(new RectangleF(-2, -1, 48, 32), placement.Dest);
        Assert.True(placement.NeedsClip);
    }

    /// <summary>A degenerate content box has nothing to place into and is returned unchanged.</summary>
    [Theory]
    [InlineData(0, 32)]
    [InlineData(48, 0)]
    public void A_Degenerate_Content_Box_Paints_Nothing(float boxW, float boxH)
    {
        var placement = Resolve(new RectangleF(0, 0, boxW, boxH), Wide, "contain");

        Assert.True(placement.IsEmpty);
    }
}
