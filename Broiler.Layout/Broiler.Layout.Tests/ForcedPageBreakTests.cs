using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Fragmentation 3 §3 forced page breaks — <c>break-before</c>/<c>break-after</c> and their
/// legacy <c>page-break-*</c> spellings — placing a box at the top of the next page.
/// </summary>
/// <remarks>
/// <para>
/// The mechanism is exercised directly here rather than through a paged render, because it is
/// governed by the layout environment's page size and nothing else: a page size is in force, so a
/// break boundary exists and a forced break moves the box to it. That is the whole contract, and
/// it is what a paged host — a printer, a PDF export, the WPT print reftests — builds on.
/// </para>
/// <para>
/// <b>Reachable from the WPT runner behind a lever</b> — <c>BROILER_WPT_PAGED_PRINT=1</c>, which
/// renders a <c>-print</c> reftest as pages of its own <c>@page</c> box instead of one viewport.
/// Off by default: the paged run scores 213 of the 409 print reftests against 252 unpaginated,
/// because where the flow is not paginated a test and its reference are unpaginated together and
/// agree. Each piece of paged media that is still missing breaks that agreement for the pairs
/// resting on it, so the lever is what makes the remaining work measurable at all.
/// </para>
/// </remarks>
public sealed class ForcedPageBreakTests
{
    private static readonly Uri BaseUrl = new("file:///page-break.html");

    private const double PageHeight = 200;
    private const double ChildHeight = 50;

    // The property and its legacy alias both reach the box, in both directions.
    [Theory]
    [InlineData("break-before", "page")]
    [InlineData("page-break-before", "page")]
    [InlineData("break-before", "always")]
    [InlineData("break-before", "left")]
    [InlineData("break-before", "right")]
    [InlineData("break-before", "recto")]
    [InlineData("break-before", "verso")]
    public void A_Forced_BreakBefore_Moves_The_Box_To_The_Next_Page(string property, string value)
    {
        var (root, second) = TreeWithTwoChildren();
        CssUtils.SetPropertyValue(second, property, value);
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(PageHeight, second.Location.Y, 3);
    }

    // break-after on the *previous* sibling forces the same break.
    [Theory]
    [InlineData("break-after")]
    [InlineData("page-break-after")]
    public void A_Forced_BreakAfter_Moves_The_Following_Box(string property)
    {
        var (root, second) = TreeWithTwoChildren();
        CssUtils.SetPropertyValue(root.Boxes[0], property, "page");
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(PageHeight, second.Location.Y, 3);
    }

    // The control: with no break declared the second child follows the first, so a change that
    // simply pushed every box to a page boundary would not pass this.
    [Fact(Timeout = 600000)]
    public void Without_A_Forced_Break_The_Box_Follows_Its_Sibling()
    {
        var (root, second) = TreeWithTwoChildren();
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    [Theory]
    [InlineData(CssConstants.Auto)]
    [InlineData("avoid")]
    [InlineData("column")]
    public void A_Non_Forcing_Break_Value_Moves_Nothing(string value)
    {
        var (root, second) = TreeWithTwoChildren();
        second.BreakBefore = value;
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    // A box already at the top of a page satisfies the break where it stands. Moving it would
    // insert an entirely blank page, which is the one way this can be wrong and still look right
    // on a test whose content happens to start at a boundary.
    [Fact(Timeout = 600000)]
    public void A_Box_Already_At_A_Page_Boundary_Does_Not_Move()
    {
        var (root, second) = TreeWithTwoChildren(firstHeight: PageHeight);
        second.BreakBefore = "page";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(PageHeight, second.Location.Y, 3);
    }

    // Unpaginated is the default, and the whole reason this can be applied unconditionally: with
    // no page size there is no boundary, so a forced break is inert rather than wrong.
    [Fact(Timeout = 600000)]
    public void Without_A_Page_Size_A_Forced_Break_Is_Inert()
    {
        var (root, second) = TreeWithTwoChildren(pageHeight: 99999);
        second.BreakBefore = "page";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ChildHeight, second.Location.Y, 3);
    }

    // The moved box takes its subtree with it — a break that moved the box and left its content
    // behind would show as text on the wrong page.
    [Fact(Timeout = 600000)]
    public void The_Moved_Box_Carries_Its_Descendants()
    {
        var (root, second) = TreeWithTwoChildren();
        var grandchild = new CssBox(second, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(100, 20),
            Display = CssConstants.Block,
            FontSize = "16px",
        };
        grandchild.Height = "20px";

        second.BreakBefore = "page";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(second.Location.Y, grandchild.Location.Y, 3);
    }

    // -------- monolithic content (CSS Fragmentation 3 §4.1) --------

    // A page boundary falling inside a box that cannot be split moves the whole box past it. This
    // is the only thing automatic fragmentation has to *decide* in this model: breakable content
    // needs no work, because the surface is continuous and already resumes at the top of the next
    // page.
    //
    // Block-level boxes only. An atomic inline is monolithic too, but it is placed by the inline
    // formatting context rather than by the block child loop this hook sits in, and so are grid and
    // flex items — those paths are not covered yet.
    [Theory]
    [InlineData("break-inside", "avoid")]
    [InlineData("break-inside", "avoid-page")]
    [InlineData("page-break-inside", "avoid")]
    [InlineData("overflow", "hidden")]
    [InlineData("overflow", "scroll")]
    [InlineData("contain", "size")]
    [InlineData("contain", "strict")]
    [InlineData("contain", "content")]
    public void An_Unbreakable_Box_Straddling_A_Boundary_Moves_To_The_Next_Page(string property, string value)
    {
        // First child 150 tall, so the second (50 tall) would span 150..200 — across the boundary
        // at 200 only if it is taller than the 50 remaining. Make it 100 so it straddles.
        var (root, second) = TreeWithTwoChildren(firstHeight: 150, secondHeight: 100);
        CssUtils.SetPropertyValue(second, property, value);
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(PageHeight, second.Location.Y, 3);
    }

    // The control: breakable content is left exactly where the flow put it, straddling the
    // boundary, because the next band renders its remainder.
    [Fact(Timeout = 600000)]
    public void A_Breakable_Box_Is_Left_Straddling_The_Boundary()
    {
        var (root, second) = TreeWithTwoChildren(firstHeight: 150, secondHeight: 100);
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(150, second.Location.Y, 3);
    }

    // A box that fits in what is left of the page is not moved — moving it would waste the page.
    [Fact(Timeout = 600000)]
    public void An_Unbreakable_Box_That_Still_Fits_Does_Not_Move()
    {
        var (root, second) = TreeWithTwoChildren(firstHeight: 100, secondHeight: 50);
        second.BreakInside = "avoid";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(100, second.Location.Y, 3);
    }

    // A box taller than the page has to be cut wherever it starts, so it stays put: pushing it
    // would leave a blank page and then overflow the next one anyway.
    [Fact(Timeout = 600000)]
    public void An_Unbreakable_Box_Taller_Than_A_Page_Stays_Put()
    {
        var (root, second) = TreeWithTwoChildren(firstHeight: 150, secondHeight: PageHeight + 50);
        second.BreakInside = "avoid";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(150, second.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void Without_A_Page_Size_An_Unbreakable_Box_Is_Not_Moved()
    {
        var (root, second) = TreeWithTwoChildren(firstHeight: 150, secondHeight: 100, pageHeight: 99999);
        second.BreakInside = "avoid";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(150, second.Location.Y, 3);
    }

    // The page-count regression test, from css/CSS2/pagination/block-page-break-inside-avoid-print-ref:
    // a page area 192 tall, a 96-tall block with `page-break-after: always`, then two more 96-tall
    // blocks. The forced break puts the second at the top of page 2 and the third directly below
    // it, so the content ends at exactly two pages.
    //
    // The third block is what this is really for. ActualBottom is derived from the box's origin
    // (Location.Y + Size.Height) and its setter *resizes* rather than moves, so advancing it after
    // OffsetTop -- which the break already did -- stretched every pushed box by the distance it was
    // pushed. Each following sibling then started lower than it should, and the document reported
    // more content than it had: this two-page reference paginated as five.
    [Fact(Timeout = 600000)]
    public void A_Forced_Break_Moves_The_Box_Without_Stretching_It()
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(384, 1536),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new PagedLayoutEnvironment(192, pageWidth: 384),
        };

        CssBox Block(double h)
        {
            var b = new CssBox(root, null, BaseUrl)
            {
                Location = new PointF(0, 0),
                Size = new SizeF(384, (float)h),
                Display = CssConstants.Block,
                FontSize = "16px",
            };
            b.Height = h.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
            return b;
        }

        var first = Block(96);
        first.BreakAfter = "always";
        var second = Block(96);
        var third = Block(96);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(0, first.Location.Y, 1);
        Assert.Equal(192, second.Location.Y, 1);
        Assert.Equal(288, third.Location.Y, 1);
        Assert.Equal(384, third.ActualBottom, 1);
    }

    /// <summary>A root of two stacked block children; the second is the one under test.</summary>
    private static (CssBox Root, CssBox Second) TreeWithTwoChildren(
        double firstHeight = ChildHeight, double secondHeight = ChildHeight, double pageHeight = PageHeight)
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 1000),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new PagedLayoutEnvironment(pageHeight),
        };

        CssBox Child(double height)
        {
            var box = new CssBox(root, null, BaseUrl)
            {
                Location = new PointF(0, 0),
                Size = new SizeF(400, (float)height),
                Display = CssConstants.Block,
                FontSize = "16px",
            };
            box.Height = height.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
            return box;
        }

        Child(firstHeight);
        return (root, Child(secondHeight));
    }
}
