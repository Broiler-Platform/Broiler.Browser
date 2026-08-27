using System;
using System.Drawing;
using System.Globalization;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Paged Media 3 §3.4 named pages — the <c>page</c> property, and the page break that a change
/// of page name forces between two boxes.
/// </summary>
/// <remarks>
/// <para>
/// A page name selects which <c>@page</c> rule applies to the pages a box lands on, so two boxes
/// whose used names differ cannot share a page. The break is a consequence of the naming rather
/// than a second declaration, which is why WPT's <c>css-page/page-name-*</c> references state the
/// same layouts with <c>break-before: page</c> and no names at all.
/// </para>
/// <para>
/// <b>What this was worth.</b> Missing named pages was the single largest cause of paged renders
/// disagreeing with their references: of 118 print reftests whose paged render came out with the
/// wrong number of pages, 31 were <c>page-name</c> tests. Implementing them moved the paged print
/// run 173 → 213 of 400. (The run is behind <c>BROILER_WPT_PAGED_PRINT=1</c>; unpaginated, where
/// no page name can matter, the same 400 score 252 — see <c>WptTestRunner.PagedPrint</c>.)
/// </para>
/// </remarks>
public sealed class NamedPageTests
{
    private static readonly Uri BaseUrl = new("file:///named-page.html");

    private const double PageHeight = 200;
    private const double ChildHeight = 50;

    // The property reaches the box through the ordinary cascade entry point, which is the only way
    // an author's `page: a` ever arrives.
    [Fact(Timeout = 600000)]
    public void The_Page_Property_Reaches_The_Box()
    {
        var root = Root();
        var box = Block(root, ChildHeight);
        CssUtils.SetPropertyValue(box, "page", "chapter");

        Assert.Equal("chapter", box.Page);
        Assert.Equal("chapter", CssUtils.GetPropertyValue(box, "page"));
    }

    [Fact(Timeout = 600000)]
    public void A_Change_Of_Page_Name_Breaks_Between_Two_Siblings()
    {
        var root = Root();
        Block(root, ChildHeight, page: "a");
        var second = Block(root, ChildHeight, page: "b");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(PageHeight, second.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void The_Same_Page_Name_Breaks_Nothing()
    {
        var root = Root();
        Block(root, ChildHeight, page: "a");
        var second = Block(root, ChildHeight, page: "a");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    // `page` is not inherited, but `auto` resolves to the nearest ancestor's used value — so a
    // child that declares nothing is on its parent's page, not on the unnamed one.
    [Fact(Timeout = 600000)]
    public void An_Unnamed_Box_Is_On_Its_Ancestors_Page()
    {
        var root = Root();
        CssUtils.SetPropertyValue(root, "page", "a");
        Block(root, ChildHeight, page: "a");
        var second = Block(root, ChildHeight);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void An_Explicit_Auto_Resolves_The_Same_Way_As_No_Declaration()
    {
        var root = Root();
        CssUtils.SetPropertyValue(root, "page", "a");
        Block(root, ChildHeight, page: "a");
        var second = Block(root, ChildHeight, page: CssConstants.Auto);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    // Propagation, from css-page/page-name-propagated-001: the page a wrapper starts is the one its
    // innermost first descendant names, not the one the wrapper itself does. Reading the outermost
    // name here would break between the two siblings; the test's reference has no break at all.
    [Fact(Timeout = 600000)]
    public void A_Wrappers_Start_Takes_The_Name_Of_Its_First_Descendant()
    {
        var root = Root();
        Block(root, ChildHeight, page: "a");

        var wrapper = Block(root, height: null, page: "b");
        var middle = Block(wrapper, height: null, page: "c");
        Block(middle, ChildHeight, page: "a");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, wrapper.Location.Y, 3);
    }

    // ...and symmetrically its end, which is the name the *next* sibling is compared against.
    [Fact(Timeout = 600000)]
    public void A_Wrappers_End_Takes_The_Name_Of_Its_Last_Descendant()
    {
        var root = Root();

        var wrapper = Block(root, height: null, page: "b");
        Block(wrapper, ChildHeight, page: "a");

        var after = Block(root, ChildHeight, page: "a");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, after.Location.Y, 3);
    }

    // A box that puts nothing on a page carries a used page name all the same — its ancestor's.
    // Reading it would break wherever the markup is indented, since the collapsed whitespace
    // between two elements is such a box (css-page/page-name-img-003).
    [Fact(Timeout = 600000)]
    public void A_Box_That_Generates_Nothing_Is_Not_What_The_Name_Is_Compared_Against()
    {
        var root = Root();
        CssUtils.SetPropertyValue(root, "page", "a");
        Block(root, ChildHeight, page: "b");
        Block(root, height: null);                       // no height, no content: generates nothing
        var third = Block(root, ChildHeight, page: "b");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, third.Location.Y, 3);
    }

    // `page` names the page a block-level box is laid out on. An inline-level box shares its
    // parent's line, and so its parent's page, so a name on it names nothing (page-name-img-001).
    [Fact(Timeout = 600000)]
    public void An_Inline_Level_Boxs_Own_Page_Name_Is_Ignored()
    {
        var root = Root();

        var first = Block(root, ChildHeight, page: "x");
        var inlineChild = Block(first, height: 20, page: "y");
        inlineChild.Display = CssConstants.InlineBlock;

        var second = Block(root, ChildHeight, page: "x");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    // A flex container places its items rather than stacking them in the page's flow, so an item's
    // page name has no boundary to fall at (css-page/page-name-flex-001).
    [Theory]
    [InlineData("flex")]
    [InlineData("grid")]
    public void A_Page_Name_On_An_Item_Of_A_Flex_Or_Grid_Container_Breaks_Nothing(string display)
    {
        AssertNamesChangeNothing((container, first, second) =>
        {
            container.Display = display;
        });
    }

    // ...but a name on the container itself still breaks: the container is in its parent's flow
    // however it lays its own children out (css-page/page-name-flex-003).
    [Fact(Timeout = 600000)]
    public void A_Page_Name_On_A_Flex_Container_Still_Breaks()
    {
        var root = Root();
        Block(root, ChildHeight, page: "a");

        var container = Block(root, ChildHeight, page: "b");
        container.Display = "flex";

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(PageHeight, container.Location.Y, 3);
    }

    // Being out of flow is not one of these. An out-of-flow box does not carry its *own* name into
    // its parent's flow — that is a separate rule, pinned below — but its children are stacked in
    // its own block flow, and a name change between two of them is still a page break
    // (css-page/page-name-003, citing the Chromium bug that established it).
    // css-page/page-name-abspos-002 states the opposite on near-identical markup and is the odd one
    // out: Chromium fails it against its own reference too. See docs/wpt-rendering-gaps-wont-fix.md.
    [Theory]
    [InlineData(CssConstants.Absolute)]
    [InlineData(CssConstants.Fixed)]
    public void A_Page_Name_Change_Inside_An_Out_Of_Flow_Subtree_Still_Breaks(string position)
    {
        var root = Root();
        var container = Block(root, height: null);
        container.Position = position;
        Block(container, ChildHeight, page: "a");
        var second = Block(container, ChildHeight, page: "b");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(PageHeight, second.Location.Y, 3);
    }

    // ...and the other half of that pair: the out-of-flow box's own name never reaches the flow it
    // was taken out of, so the two in-flow siblings around it share a page and nothing breaks
    // (css-page/page-name-abspos-001).
    [Theory]
    [InlineData(CssConstants.Absolute)]
    [InlineData(CssConstants.Fixed)]
    public void An_Out_Of_Flow_Boxs_Own_Page_Name_Stays_Out_Of_The_Flow(string position)
    {
        var root = Root();
        Block(root, ChildHeight, page: "a");

        var outOfFlow = Block(root, ChildHeight, page: "b");
        outOfFlow.Position = position;

        var third = Block(root, ChildHeight, page: "a");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, third.Location.Y, 3);
    }

    // A box whose block axis is not the page's stacks its children sideways, where a page boundary
    // does not fall (css-page/page-name-orthogonal-writing-003).
    [Fact(Timeout = 600000)]
    public void A_Page_Name_In_An_Orthogonal_Writing_Mode_Breaks_Nothing()
    {
        AssertNamesChangeNothing((container, first, second) =>
        {
            container.WritingMode = "vertical-rl";
        });
    }

    // Unpaginated is the default, and the reason all of this can be applied unconditionally: with
    // no page size there is no boundary, so a page name is inert rather than wrong.
    [Fact(Timeout = 600000)]
    public void Without_A_Page_Size_A_Change_Of_Page_Name_Moves_Nothing()
    {
        var root = Root(pageHeight: 99999);
        Block(root, ChildHeight, page: "a");
        var second = Block(root, ChildHeight, page: "b");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    /// <summary>
    /// Lays out two children of a container twice — once with differing page names on them, once
    /// with none — and asserts the two agree.
    /// </summary>
    /// <remarks>
    /// The comparison is against the same tree rather than against a stated coordinate because
    /// these are the cases where the container places its children itself: a flex or grid
    /// container, a box writing sideways. Where those put their children is the container's
    /// business, and asserting a number here would be asserting that as well. What the rule claims
    /// is only that the names change nothing, which is exactly what this measures.
    /// </remarks>
    private static void AssertNamesChangeNothing(Action<CssBox, CssBox, CssBox> configure)
    {
        var withNames = LayOut(named: true);
        var withoutNames = LayOut(named: false);

        Assert.Equal(withoutNames.First, withNames.First, 3);
        Assert.Equal(withoutNames.Second, withNames.Second, 3);

        (double First, double Second) LayOut(bool named)
        {
            var root = Root();
            var container = Block(root, height: null);
            var first = Block(container, ChildHeight, page: named ? "a" : null);
            var second = Block(container, ChildHeight, page: named ? "b" : null);
            configure(container, first, second);

            root.PerformLayout(root.LayoutEnvironment);
            return (first.Location.Y, second.Location.Y);
        }
    }

    private static CssBox Root(double pageHeight = PageHeight) =>
        new(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 1000),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new PagedLayoutEnvironment(pageHeight),
        };

    /// <summary>
    /// A block child of <paramref name="parent"/>. A <c>null</c> height leaves it to size itself
    /// around its children, which is what a wrapper in a page-name test is.
    /// </summary>
    private static CssBox Block(CssBox parent, double? height, string? page = null)
    {
        var box = new CssBox(parent, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, (float)(height ?? 0)),
            Display = CssConstants.Block,
            FontSize = "16px",
        };

        if (height is { } h)
            box.Height = h.ToString(CultureInfo.InvariantCulture) + "px";
        if (page is not null)
            CssUtils.SetPropertyValue(box, "page", page);

        return box;
    }
}
