using System;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Flexbox §4 / CSS Grid §6: "each contiguous sequence of child text runs is wrapped in an
/// anonymous block container flex item… However, if the entire sequence of child text runs contains
/// only white space it is instead not rendered."
/// </summary>
/// <remarks>
/// A text run carries no <c>display</c>, so blockification had nothing to blockify and the
/// inline/block correction never reaches a flex or grid container's children. Text written straight
/// inside such a container therefore laid out as nothing — which on duckduckgo.com's start page is
/// the "Search" and "Ask AI" labels beside the search box's mode icons.
/// </remarks>
public sealed class FlexAnonymousTextItemTests
{
    private static readonly Uri BaseUrl = new("file:///flex-text.html");

    [Theory]
    [InlineData("flex")]
    [InlineData("inline-flex")]
    [InlineData("grid")]
    [InlineData("inline-grid")]
    public void A_Text_Run_Becomes_An_Anonymous_Item(string display)
    {
        var root = Container(display);
        var run = TextRun(root, "Search");

        FlexGridItemBlockification.Generate(root);

        var wrapper = Assert.Single(root.Boxes);
        Assert.NotSame(run, wrapper);
        Assert.Null(wrapper.HtmlTag);
        Assert.Equal(CssConstants.Block, wrapper.Display);
        Assert.Equal([run], wrapper.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void The_Run_Keeps_Its_Place_Among_The_Element_Items()
    {
        var root = Container("flex");
        var icon = Element(root, "button");
        var run = TextRun(root, "Search");
        var trailing = Element(root, "span");

        FlexGridItemBlockification.Generate(root);

        Assert.Equal(3, root.Boxes.Count);
        Assert.Same(icon, root.Boxes[0]);
        Assert.Equal([run], root.Boxes[1].Boxes);
        Assert.Same(trailing, root.Boxes[2]);
    }

    [Fact(Timeout = 600000)]
    public void Consecutive_Runs_Share_One_Anonymous_Item()
    {
        var root = Container("flex");
        var first = TextRun(root, "Search");
        var space = TextRun(root, " ");
        var second = TextRun(root, "privately");
        var element = Element(root, "span");

        FlexGridItemBlockification.Generate(root);

        Assert.Equal(2, root.Boxes.Count);
        Assert.Equal([first, space, second], root.Boxes[0].Boxes);
        Assert.Same(element, root.Boxes[1]);
    }

    // White space between two items is not a flex item — it is not rendered at all.
    [Fact(Timeout = 600000)]
    public void A_White_Space_Only_Sequence_Is_Not_Wrapped()
    {
        var root = Container("flex");
        var first = Element(root, "span");
        var space = TextRun(root, "\n  ");
        var second = Element(root, "span");

        FlexGridItemBlockification.Generate(root);

        Assert.Equal([first, space, second], root.Boxes);
    }

    // …unless white-space makes it meaningful, in which case it is a run like any other.
    [Fact(Timeout = 600000)]
    public void Preserved_White_Space_Is_A_Run_Of_Its_Own()
    {
        var root = Container("flex");
        var space = TextRun(root, "   ");
        space.WhiteSpace = CssConstants.Pre;

        FlexGridItemBlockification.Generate(root);

        var wrapper = Assert.Single(root.Boxes);
        Assert.Equal([space], wrapper.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void A_Block_Container_Is_Left_To_The_Inline_Block_Correction()
    {
        var root = Container(CssConstants.Block);
        var run = TextRun(root, "Search");

        FlexGridItemBlockification.Generate(root);

        Assert.Equal([run], root.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void Running_The_Pass_Twice_Does_Not_Nest_A_Second_Wrapper()
    {
        var root = Container("flex");
        var run = TextRun(root, "Search");

        FlexGridItemBlockification.Generate(root);
        FlexGridItemBlockification.Generate(root);

        var wrapper = Assert.Single(root.Boxes);
        Assert.Equal([run], wrapper.Boxes);
    }

    // A `display: contents` wrapper is spliced first, so the label inside it is a run of the flex
    // container and gets its own anonymous item — the duckduckgo.com button-label shape end to end.
    [Fact(Timeout = 600000)]
    public void A_Label_Behind_A_Contents_Wrapper_Reaches_The_Container()
    {
        var root = Container("flex");
        var icon = Element(root, "span");
        var label = Element(root, "span");
        label.Display = "contents";
        var run = TextRun(label, "Set As Default Search");

        FlexGridItemBlockification.Generate(root);

        Assert.Equal(2, root.Boxes.Count);
        Assert.Same(icon, root.Boxes[0]);
        Assert.Equal([run], root.Boxes[1].Boxes);
    }

    private static CssBox Container(string display) =>
        new(null, new HtmlTag("div", isSingle: false), BaseUrl) { Display = display };

    private static CssBox Element(CssBox parent, string tag) =>
        new(parent, new HtmlTag(tag, isSingle: false), BaseUrl) { Display = CssConstants.Inline };

    private static CssBox TextRun(CssBox parent, string text) =>
        new(parent, null, BaseUrl) { Text = text.AsMemory() };
}
