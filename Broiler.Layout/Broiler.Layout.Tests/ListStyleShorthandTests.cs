using System;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Lists 3 §5: <c>list-style</c> sets <c>list-style-type</c>,
/// <c>list-style-position</c> and <c>list-style-image</c> in any order, and resets the ones the
/// declaration omits to their initial values. The cascade→box mapping had no arm for the
/// shorthand at all — the three longhands worked and the shorthand was dropped — so a page that
/// writes `list-style: none` (which is most pages with a navigation menu) kept the UA's bullets.
/// </summary>
public sealed class ListStyleShorthandTests
{
    private static readonly Uri BaseUrl = new("file:///list.html");

    private static CssBox Apply(string value)
    {
        var box = new CssBox(null, null, BaseUrl);
        CssUtils.SetPropertyValue(box, "list-style", value);
        return box;
    }

    [Fact(Timeout = 600000)]
    public void A_Bare_Type_Sets_The_Type_And_Resets_The_Rest()
    {
        var box = Apply("square");

        Assert.Equal("square", box.ListStyleType);
        Assert.Equal("outside", box.ListStylePosition);
        Assert.Equal("none", box.ListStyleImage);
    }

    [Fact(Timeout = 600000)]
    public void A_Bare_Position_Sets_The_Position_And_Resets_The_Rest()
    {
        var box = Apply("inside");

        Assert.Equal("disc", box.ListStyleType);
        Assert.Equal("inside", box.ListStylePosition);
        Assert.Equal("none", box.ListStyleImage);
    }

    [Theory]
    [InlineData("square inside url(bullet.png)")]
    [InlineData("inside url(bullet.png) square")]
    [InlineData("url(bullet.png) square inside")]
    public void The_Components_May_Come_In_Any_Order(string value)
    {
        var box = Apply(value);

        Assert.Equal("square", box.ListStyleType);
        Assert.Equal("inside", box.ListStylePosition);
        Assert.Equal("url(bullet.png)", box.ListStyleImage);
    }

    /// <summary>
    /// The one ambiguity in the grammar: a single <c>none</c> is both a valid type and a valid
    /// image, and the spec sets *both* from it. `list-style: none` is how every skin turns a
    /// navigation menu's bullets off.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Single_None_Clears_Both_The_Type_And_The_Image()
    {
        var box = Apply("none");

        Assert.Equal("none", box.ListStyleType);
        Assert.Equal("none", box.ListStyleImage);
        Assert.Equal("outside", box.ListStylePosition);
    }

    /// <summary>With a real image beside it, the `none` is the type — the image is already
    /// spoken for.</summary>
    [Fact(Timeout = 600000)]
    public void None_Beside_An_Image_Is_The_Type()
    {
        var box = Apply("none url(bullet.png)");

        Assert.Equal("none", box.ListStyleType);
        Assert.Equal("url(bullet.png)", box.ListStyleImage);
    }

    /// <summary>A CSS-wide keyword applies to every longhand, and must not be mistaken for a
    /// marker type.</summary>
    [Theory]
    [InlineData("inherit")]
    [InlineData("initial")]
    [InlineData("unset")]
    public void A_Css_Wide_Keyword_Goes_To_Every_Longhand(string keyword)
    {
        var box = Apply(keyword);

        Assert.Equal(keyword, box.ListStyleType);
        Assert.Equal(keyword, box.ListStylePosition);
        Assert.Equal(keyword, box.ListStyleImage);
    }
}
