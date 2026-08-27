using System;
using System.Drawing;
using System.Globalization;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Sizing 4 §4: a box with a preferred <c>aspect-ratio</c> and a definite block size takes
/// its <c>auto</c> inline size from the ratio, rather than stretching to its containing block.
/// </summary>
/// <remarks>
/// <para>
/// The engine only had the transfer in the other direction — an auto height derived from a width
/// that had already filled the containing block — so <c>&lt;div style="height: 100px;
/// aspect-ratio: 1/1"&gt;</c> painted a viewport-wide 100px band where every engine paints a
/// 100px square. That is the shape WPT's <c>css-sizing/aspect-ratio</c> tests are built from:
/// they compare against a filled green 100px square, so the whole assertion is the width.
/// </para>
/// <para>
/// The expected values here were read out of Chromium (<c>getBoundingClientRect</c> on the same
/// declarations) rather than derived from the prose, because the interesting half of this rule is
/// which constraint wins where: the transfer reads the <em>used</em> block size, so
/// <c>min-height</c>/<c>max-height</c> clamp before it, and <c>min-width</c>/<c>max-width</c>
/// clamp after it — in the box <c>box-sizing</c> names, which is why a padded
/// <c>content-box</c> box is wider than its <c>max-width</c>.
/// </para>
/// </remarks>
public sealed class AspectRatioInlineSizeTests
{
    private static readonly Uri BaseUrl = new("file:///aspect-ratio.html");

    private const float ContainingWidth = 400;

    /// <summary>The rule itself: a definite height and a ratio settle the width.</summary>
    [Theory]
    [InlineData("100px", "1/1", 100)]
    [InlineData("50px", "2/1", 100)]
    [InlineData("100px", "1/2", 50)]
    [InlineData("100px", "auto 4/1", 400)]
    public void A_Definite_Height_And_A_Ratio_Size_The_Inline_Axis(
        string height, string ratio, double expectedWidth)
    {
        var box = BlockIn(Page(), height: height, aspectRatio: ratio);

        Layout(box);

        Assert.Equal(expectedWidth, box.Size.Width, 1);
    }

    /// <summary>The control, and the behaviour that was already right: with no block size to
    /// transfer, the auto width still fills the containing block and the ratio gives the
    /// height.</summary>
    [Fact(Timeout = 600000)]
    public void An_Auto_Height_Still_Stretches_And_Transfers_The_Other_Way()
    {
        var box = BlockIn(Page(), height: null, aspectRatio: "1/1");

        Layout(box);

        Assert.Equal(ContainingWidth, box.Size.Width, 1);
    }

    /// <summary>A stated width is a stated width — the ratio never overrules one.</summary>
    [Fact(Timeout = 600000)]
    public void A_Specified_Width_Wins_Over_The_Ratio()
    {
        var box = BlockIn(Page(), height: "100px", aspectRatio: "1/1");
        box.Width = "250px";

        Layout(box);

        Assert.Equal(250, box.Size.Width, 1);
    }

    /// <summary>The transfer reads the <em>used</em> block size, so the §10.7 block-axis clamp
    /// happens first and is carried through the ratio.</summary>
    [Theory]
    [InlineData("200px", null, 200)]
    [InlineData(null, "40px", 40)]
    public void The_Block_Axis_Clamp_Is_Transferred_Too(string minHeight, string maxHeight, double expectedWidth)
    {
        var box = BlockIn(Page(), height: "100px", aspectRatio: "1/1");
        if (minHeight != null)
            box.MinHeight = minHeight;
        if (maxHeight != null)
            box.MaxHeight = maxHeight;

        Layout(box);

        Assert.Equal(expectedWidth, box.Size.Width, 1);
    }

    /// <summary>CSS2.1 §10.4 still clamps the derived width afterwards, and does not feed the
    /// clamped value back through the ratio: the height stays what it was declared.</summary>
    [Theory]
    [InlineData("150px", null, 150)]
    [InlineData(null, "40px", 40)]
    public void The_Inline_Axis_Clamp_Applies_After_The_Transfer(string minWidth, string maxWidth, double expectedWidth)
    {
        var box = BlockIn(Page(), height: "100px", aspectRatio: "1/1");
        if (minWidth != null)
            box.MinWidth = minWidth;
        if (maxWidth != null)
            box.MaxWidth = maxWidth;

        Layout(box);

        Assert.Equal(expectedWidth, box.Size.Width, 1);
    }

    /// <summary>The ratio relates the two sizes of the box <c>box-sizing</c> names, so a
    /// <c>content-box</c> box's padding is outside the square and a <c>border-box</c> one's is
    /// inside it.</summary>
    [Theory]
    [InlineData(false, 140)]
    [InlineData(true, 100)]
    public void The_Ratio_Applies_To_The_Box_Box_Sizing_Names(bool borderBox, double expectedWidth)
    {
        var box = BlockIn(Page(), height: "100px", aspectRatio: "1/1");
        box.PaddingLeft = "20px";
        box.PaddingRight = "20px";
        if (borderBox)
            box.BoxSizing = "border-box";

        Layout(box);

        Assert.Equal(expectedWidth, box.Size.Width, 1);
    }

    /// <summary>And so does the inline-axis clamp, which is why a padded <c>content-box</c> box
    /// ends up wider than its own <c>max-width</c>: the bound caps the content box, and the
    /// padding is added to the result.</summary>
    [Fact(Timeout = 600000)]
    public void The_Inline_Clamp_Caps_The_Same_Box_The_Ratio_Sized()
    {
        var box = BlockIn(Page(), height: "100px", aspectRatio: "1/1");
        box.PaddingLeft = "20px";
        box.PaddingRight = "20px";
        box.MaxWidth = "60px";

        Layout(box);

        Assert.Equal(100, box.Size.Width, 1);
    }

    /// <summary>CSS2.1 §10.3.3: the free space a ratio-derived width leaves is still free space,
    /// so <c>margin: 0 auto</c> centres such a box exactly as it centres a stated one.</summary>
    [Fact(Timeout = 600000)]
    public void Auto_Margins_Centre_A_Ratio_Derived_Width()
    {
        var page = Page();
        var box = BlockIn(page, height: "100px", aspectRatio: "1/1");
        box.MarginLeft = CssConstants.Auto;
        box.MarginRight = CssConstants.Auto;

        Layout(box);

        Assert.Equal(100, box.Size.Width, 1);
        Assert.Equal((ContainingWidth - 100) / 2, box.ActualMarginLeft, 1);
    }

    /// <summary>A percentage height counts only when it has something definite to resolve
    /// against (CSS2.1 §10.5); against an auto-height containing block it computes to
    /// <c>auto</c> and there is nothing to transfer.</summary>
    [Fact(Timeout = 600000)]
    public void A_Percentage_Height_Transfers_Against_A_Definite_Containing_Block()
    {
        var page = Page();
        page.Height = "300px";

        var box = BlockIn(page, height: "50%", aspectRatio: "1/1");

        Layout(box);

        Assert.Equal(150, box.Size.Width, 1);
    }

    [Fact(Timeout = 600000)]
    public void A_Percentage_Height_Against_An_Auto_Height_Container_Does_Not()
    {
        var box = BlockIn(Page(), height: "50%", aspectRatio: "1/1");

        Layout(box);

        Assert.Equal(ContainingWidth, box.Size.Width, 1);
    }

    /// <summary>A box with no ratio is untouched — the whole rule is gated on one.</summary>
    [Fact(Timeout = 600000)]
    public void Without_A_Ratio_An_Auto_Width_Still_Fills_The_Containing_Block()
    {
        var box = BlockIn(Page(), height: "100px", aspectRatio: null);

        Layout(box);

        Assert.Equal(ContainingWidth, box.Size.Width, 1);
    }

    /// <summary>An absolutely positioned box takes the ratio over the §10.3.7 inset equation:
    /// the equation answers "how wide is a box with no width of its own?", and a ratio plus a
    /// definite height is such a width.</summary>
    [Fact(Timeout = 600000)]
    public void An_Out_Of_Flow_Box_Takes_The_Ratio_Over_Its_Insets()
    {
        var page = Page();
        page.Position = CssConstants.Relative;

        var box = BlockIn(page, height: "100px", aspectRatio: "1/1");
        box.Position = CssConstants.Absolute;
        box.Left = "0";
        box.Right = "0";
        box.Top = "0";

        Layout(box);

        Assert.Equal(100, box.Size.Width, 1);
    }

    /// <summary>
    /// The <c>/</c> is a token of the grammar rather than a character of the numbers around it,
    /// so whitespace may sit on either side of it. <c>1 / 2</c> is how most of the WPT
    /// <c>css-sizing</c> tests spell the value, and it used to parse as no ratio at all — the
    /// lone slash token carried no number, and rejecting it rejected the declaration.
    /// </summary>
    [Theory]
    [InlineData("1/2", 0.5)]
    [InlineData("1 / 2", 0.5)]
    [InlineData("1 /2", 0.5)]
    [InlineData("1/ 2", 0.5)]
    [InlineData("auto 4 / 1", 4)]
    [InlineData("4 / 1 auto", 4)]
    [InlineData("2", 2)]
    [InlineData("0.5", 0.5)]
    public void A_Slash_Parses_With_Or_Without_Space_Around_It(string value, double expected)
    {
        Assert.True(CssBox.TryParseAspectRatio(value, out double ratio), value);
        Assert.Equal(expected, ratio, 4);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("none")]
    [InlineData("")]
    [InlineData("0 / 1")]
    [InlineData("1 / 0")]
    [InlineData("green")]
    public void A_Value_That_Names_No_Ratio_Parses_As_None(string value)
    {
        Assert.False(CssBox.TryParseAspectRatio(value, out _), value);
    }

    /// <summary>The end-to-end consequence of the two spellings agreeing.</summary>
    [Fact(Timeout = 600000)]
    public void A_Spaced_Ratio_Sizes_The_Inline_Axis_Like_An_Unspaced_One()
    {
        var spaced = BlockIn(Page(), height: "100px", aspectRatio: "1 / 2");
        var unspaced = BlockIn(Page(), height: "100px", aspectRatio: "1/2");

        Layout(spaced);
        Layout(unspaced);

        Assert.Equal(50, spaced.Size.Width, 1);
        Assert.Equal(unspaced.Size.Width, spaced.Size.Width, 1);
    }

    // ───────────────────────────────── the shape ─────────────────────────────────

    private static void Layout(CssBox box)
    {
        var root = box;
        while (root.ParentBox != null)
            root = root.ParentBox;

        root.PerformLayout(root.LayoutEnvironment);
    }

    /// <summary>root → body, so a percentage height has an ordinary auto-height containing block
    /// above it rather than the initial one, whose block size is the viewport's and therefore
    /// always definite. Returns the body: the box under test is its child.</summary>
    private static CssBox Page()
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = PointF.Empty,
            Size = new SizeF(ContainingWidth, 0),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new PagedLayoutEnvironment(99999, pageWidth: ContainingWidth),
        };

        return new CssBox(root, new HtmlTag("body", false, null), BaseUrl)
        {
            Location = PointF.Empty,
            Display = CssConstants.Block,
            FontSize = "16px",
        };
    }

    private static CssBox BlockIn(CssBox parent, string height, string aspectRatio)
    {
        var box = new CssBox(parent, new HtmlTag("div", false, null), BaseUrl)
        {
            Location = PointF.Empty,
            Display = CssConstants.Block,
            FontSize = "16px",
        };

        if (height != null)
            box.Height = height;
        if (aspectRatio != null)
            box.AspectRatio = aspectRatio;

        return box;
    }
}
