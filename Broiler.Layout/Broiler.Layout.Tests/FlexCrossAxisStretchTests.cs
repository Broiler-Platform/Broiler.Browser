using System;
using System.Drawing;
using System.Globalization;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Flexbox §9.4 step 11 — <c>align-items: stretch</c>, the initial value: a flex item whose
/// cross size is auto fills its line.
/// </summary>
/// <remarks>
/// <para>
/// This is what happens to most flex items on most pages, and the engine did none of it. An item
/// was left at the size its own content gave it, so one with a width and no height came out
/// zero-tall and painted nothing at all — which is why a flex container used as a strip, the shape
/// half of WPT's own reference files are built from, rendered blank.
/// </para>
/// <para>
/// Measured over <c>css/css-flexbox</c>: 470 → 568 of 994 reftests, 123 won against 25 lost. The
/// losses are worth naming, because they are not this rule going wrong: they are tests whose
/// <em>reference</em> states the expected layout with absolutely positioned boxes that Broiler
/// still sizes to their text, so the two sides used to be wrong together and now only one of them
/// is. <c>flexbox_align-items-stretch</c> is exactly that — the render is now right to the pixel
/// and the reference is not.
/// </para>
/// </remarks>
public sealed class FlexCrossAxisStretchTests
{
    private static readonly Uri BaseUrl = new("file:///flex.html");

    private const double ContainerHeight = 100;
    private const double ItemWidth = 40;

    [Fact(Timeout = 600000)]
    public void An_Item_With_No_Height_Fills_A_Definite_Container()
    {
        var (root, item) = RowContainer();
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ContainerHeight, item.Size.Height, 1);
    }

    [Fact(Timeout = 600000)]
    public void An_Item_With_A_Height_Of_Its_Own_Is_Left_Alone()
    {
        var (root, item) = RowContainer();
        item.Height = "30px";
        item.Size = new SizeF(item.Size.Width, 30);
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(30, item.Size.Height, 1);
    }

    // Stretch is what `align-items` means when nothing says otherwise — including when it says
    // `normal`, which is the initial value and behaves as stretch for a flex item.
    [Theory]
    [InlineData("")]
    [InlineData("normal")]
    [InlineData("stretch")]
    public void The_Default_And_Normal_Both_Stretch(string alignItems)
    {
        var (root, item) = RowContainer();
        root.AlignItems = alignItems;
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ContainerHeight, item.Size.Height, 1);
    }

    // An alignment that places the item asks for it to be placed, not sized.
    [Theory]
    [InlineData("center")]
    [InlineData("flex-end")]
    [InlineData("flex-start")]
    public void A_Placing_Alignment_Does_Not_Stretch(string alignItems)
    {
        var (root, item) = RowContainer();
        root.AlignItems = alignItems;
        root.PerformLayout(root.LayoutEnvironment);

        Assert.True(item.Size.Height < ContainerHeight - 1,
            $"align-items: {alignItems} should place the item, not size it");
    }

    [Fact(Timeout = 600000)]
    public void Align_Self_Overrides_The_Containers_Align_Items()
    {
        var (root, item) = RowContainer();
        root.AlignItems = "center";
        item.AlignSelf = "stretch";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ContainerHeight, item.Size.Height, 1);
    }

    // CSS Flexbox §9.6 gives the cross-axis free space to an auto margin before the item.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void An_Auto_Cross_Margin_Takes_The_Space_Instead(bool top, bool bottom)
    {
        var (root, item) = RowContainer();
        if (top)
            item.MarginTop = CssConstants.Auto;
        if (bottom)
            item.MarginBottom = CssConstants.Auto;

        root.PerformLayout(root.LayoutEnvironment);

        Assert.True(item.Size.Height < ContainerHeight - 1, "an auto margin should absorb the space");
    }

    // With no definite container height the line is as tall as its tallest item, and the shorter
    // ones stretch to that rather than to nothing.
    [Fact(Timeout = 600000)]
    public void Without_A_Definite_Container_The_Line_Is_Its_Tallest_Item()
    {
        var root = Container(height: null);
        var tall = Item(root, height: 60);
        var short_ = Item(root, height: null);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(60, tall.Size.Height, 1);
        Assert.Equal(60, short_.Size.Height, 1);
    }

    // ---- the shorthands the direction and the flex factors arrive through ----

    // `flex-flow` is the only way many documents state their direction, and a container whose
    // direction never arrives is laid out as a row — the wrong axis for everything in it.
    [Theory]
    [InlineData("column", "column", "nowrap")]
    [InlineData("column wrap", "column", "wrap")]
    [InlineData("wrap column-reverse", "column-reverse", "wrap")]
    [InlineData("row-reverse", "row-reverse", "nowrap")]
    public void Flex_Flow_Expands_To_A_Direction_And_A_Wrap(
        string value, string direction, string wrap)
    {
        var box = Container(height: null);
        box.FlexDirection = "row";
        box.FlexWrap = "nowrap";
        CssUtils.SetPropertyValue(box, "flex-flow", value);

        Assert.Equal(direction, box.FlexDirection);
        Assert.Equal(wrap, box.FlexWrap);
    }

    // CSS Flexbox §7.1: the shorthand's own defaults, which are not the longhands' — `flex: 1` is
    // `1 1 0%`, not `1 0 auto`.
    [Theory]
    [InlineData("1", "1", "1", "0%")]
    [InlineData("2 3", "2", "3", "0%")]
    [InlineData("1 1 auto", "1", "1", "auto")]
    [InlineData("0 0 200px", "0", "0", "200px")]
    [InlineData("none", "0", "0", "auto")]
    [InlineData("auto", "1", "1", "auto")]
    public void Flex_Expands_To_Grow_Shrink_And_Basis(
        string value, string grow, string shrink, string basis)
    {
        var box = Container(height: null);
        CssUtils.SetPropertyValue(box, "flex", value);

        Assert.Equal(grow, box.FlexGrow);
        Assert.Equal(shrink, box.FlexShrink);
        Assert.Equal(basis, box.FlexBasis);
    }

    private static (CssBox Root, CssBox Item) RowContainer()
    {
        var root = Container(ContainerHeight);
        return (root, Item(root, height: null));
    }

    private static CssBox Container(double? height)
    {
        var box = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(200, (float)(height ?? 0)),
            Display = "flex",
            FontSize = "16px",
            LayoutEnvironment = new PagedLayoutEnvironment(99999, pageWidth: 200),
        };

        if (height is { } h)
            box.Height = h.ToString(CultureInfo.InvariantCulture) + "px";

        return box;
    }

    private static CssBox Item(CssBox parent, double? height)
    {
        var box = new CssBox(parent, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF((float)ItemWidth, (float)(height ?? 0)),
            Display = CssConstants.Block,
            FontSize = "16px",
        };

        box.Width = ItemWidth.ToString(CultureInfo.InvariantCulture) + "px";
        if (height is { } h)
            box.Height = h.ToString(CultureInfo.InvariantCulture) + "px";

        return box;
    }
}
