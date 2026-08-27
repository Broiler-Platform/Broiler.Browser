using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS2.1 §10.4/§10.7: the min/max constraint pass over a tentative replaced-element size.
/// </summary>
/// <remarks>
/// The interesting half is that the two axes are <em>coupled</em> for a box with an intrinsic ratio.
/// Clamping them independently is what made WPT
/// <c>css-sizing/replaced-max-size-saturation</c> — an 8000×8000 <c>&lt;canvas&gt;</c> under
/// <c>max-width: 120px; max-height: 100px</c> — come out 120×100 (a rectangle) where every browser
/// renders the 100×100 square the test's reference draws.
/// </remarks>
public sealed class ReplacedBoxSizingTests
{
    private static (double Width, double Height) Constrain(
        double width, double height, double ratio,
        double minWidth = 0, double maxWidth = double.PositiveInfinity,
        double minHeight = 0, double maxHeight = double.PositiveInfinity,
        bool widthIsAuto = true, bool heightIsAuto = true)
    {
        ReplacedBoxSizing.ApplyMinMax(
            ref width, ref height, widthIsAuto, heightIsAuto, ratio,
            new ReplacedBoxSizing.Bounds(minWidth, maxWidth),
            new ReplacedBoxSizing.Bounds(minHeight, maxHeight));

        return (width, height);
    }

    [Fact(Timeout = 600000)]
    public void An_Unconstrained_Size_Is_Left_Alone()
    {
        Assert.Equal((400d, 200d), Constrain(400, 200, ratio: 2));
    }

    // The saturation test itself: both upper bounds are violated, and max-height bites harder
    // (100/8000 < 120/8000), so the height wins and the width follows the 1:1 ratio.
    [Fact(Timeout = 600000)]
    public void Both_Maximums_Violated_Keeps_The_Ratio_Of_Whichever_Bites_Harder()
    {
        Assert.Equal((100d, 100d), Constrain(8000, 8000, ratio: 1, maxWidth: 120, maxHeight: 100));
    }

    [Fact(Timeout = 600000)]
    public void Both_Maximums_Violated_The_Other_Way_Round_Lets_The_Width_Win()
    {
        // 200×800 (ratio 1/4) under 100×700: max-width/w = 0.5 ≤ max-height/h = 0.875, so the width
        // is the binding constraint and the height follows it down through the ratio.
        Assert.Equal((100d, 400d), Constrain(200, 800, ratio: 0.25, maxWidth: 100, maxHeight: 700));
    }

    [Fact(Timeout = 600000)]
    public void An_Over_Wide_Box_Pulls_Its_Auto_Height_Down_Through_The_Ratio()
    {
        Assert.Equal((100d, 50d), Constrain(400, 200, ratio: 2, maxWidth: 100));
    }

    [Fact(Timeout = 600000)]
    public void An_Over_Tall_Box_Pulls_Its_Auto_Width_Down_Through_The_Ratio()
    {
        Assert.Equal((100d, 50d), Constrain(400, 200, ratio: 2, maxHeight: 50));
    }

    [Fact(Timeout = 600000)]
    public void An_Under_Wide_Box_Grows_Both_Axes()
    {
        Assert.Equal((400d, 200d), Constrain(40, 20, ratio: 2, minWidth: 400));
    }

    [Fact(Timeout = 600000)]
    public void An_Under_Tall_Box_Grows_Both_Axes()
    {
        Assert.Equal((400d, 200d), Constrain(40, 20, ratio: 2, minHeight: 200));
    }

    // The ratio-preserving growth is still bounded by the other axis's maximum.
    [Fact(Timeout = 600000)]
    public void Growing_To_Min_Width_Stops_At_Max_Height()
    {
        Assert.Equal((400d, 100d), Constrain(40, 20, ratio: 2, minWidth: 400, maxHeight: 100));
    }

    [Fact(Timeout = 600000)]
    public void Both_Minimums_Violated_Keeps_The_Ratio_Of_Whichever_Bites_Harder()
    {
        // min-width/w = 10 > min-height/h = 5, so the width is the binding constraint.
        Assert.Equal((400d, 200d), Constrain(40, 20, ratio: 2, minWidth: 400, minHeight: 100));
    }

    [Fact(Timeout = 600000)]
    public void Both_Minimums_Violated_The_Other_Way_Round_Lets_The_Height_Win()
    {
        // min-height/h = 10 ≥ min-width/w = 5, so the height is the binding constraint.
        Assert.Equal((400d, 200d), Constrain(40, 20, ratio: 2, minWidth: 200, minHeight: 200));
    }

    [Fact(Timeout = 600000)]
    public void Opposing_Violations_Take_Both_Bounds_And_Drop_The_Ratio()
    {
        Assert.Equal((200d, 50d), Constrain(40, 200, ratio: 0.2, minWidth: 200, maxHeight: 50));
        Assert.Equal((100d, 80d), Constrain(400, 20, ratio: 20, maxWidth: 100, minHeight: 80));
    }

    // With no intrinsic ratio the axes are independent — which is also the whole of the correct
    // behaviour for a non-replaced inline-block.
    [Fact(Timeout = 600000)]
    public void Without_A_Ratio_Each_Axis_Is_Clamped_On_Its_Own()
    {
        Assert.Equal((120d, 100d), Constrain(8000, 8000, ratio: 0, maxWidth: 120, maxHeight: 100));
    }

    [Fact(Timeout = 600000)]
    public void A_Minimum_Beats_A_Smaller_Maximum()
    {
        Assert.Equal((80d, 40d), Constrain(10, 5, ratio: 0, minWidth: 80, maxWidth: 20, minHeight: 40, maxHeight: 10));
    }

    // A stated axis is the author's; clamping it moves the auto one through the ratio, never the
    // other way round. Chromium renders `height: 1000px; max-width: 100px` on a 1:1 image as
    // 100×1000 — the max-width shortens the width only.
    [Fact(Timeout = 600000)]
    public void A_Stated_Height_Is_Not_Shortened_By_Max_Width()
    {
        Assert.Equal((100d, 1000d), Constrain(1000, 1000, ratio: 1, maxWidth: 100, heightIsAuto: false));
    }

    [Fact(Timeout = 600000)]
    public void Clamping_A_Stated_Height_Re_Derives_The_Auto_Width()
    {
        Assert.Equal((100d, 100d), Constrain(1000, 1000, ratio: 1, maxHeight: 100, heightIsAuto: false));
    }

    [Fact(Timeout = 600000)]
    public void Clamping_A_Stated_Width_Re_Derives_The_Auto_Height()
    {
        Assert.Equal((100d, 50d), Constrain(1000, 500, ratio: 2, maxWidth: 100, widthIsAuto: false));
    }

    [Fact(Timeout = 600000)]
    public void With_Both_Axes_Stated_Neither_Follows_The_Other()
    {
        Assert.Equal((120d, 100d), Constrain(8000, 8000, ratio: 1, maxWidth: 120, maxHeight: 100,
            widthIsAuto: false, heightIsAuto: false));
    }
}
