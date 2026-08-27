using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The layout defects that <c>www.mediawiki.org</c>'s Vector 2022 skin exposed, each reduced to the
/// smallest shape that still shows it. They are unrelated to one another except in where they were
/// found, so each fact states its own rule and its own consequence.
/// </summary>
/// <remarks>
/// <para>
/// The page is a useful witness because it stacks ordinary constructs the reference browser handles
/// without comment: a flex header whose children are floated, a thumbnail wrapped in a
/// <c>display: table</c> figure whose image carries a 3px margin, and an article body that opens
/// with an empty <c>&lt;p&gt;</c> holding nothing but a <c>&lt;style&gt;</c>. Each of those was
/// laid out wrongly, and the errors partly cancelled — which is why they had to be isolated here
/// rather than measured on the page.
/// </para>
/// </remarks>
public sealed class VectorSkinLayoutTests
{
    private static readonly Uri BaseUrl = new("file:///vector.html");

    // ─────────── CSS Flexbox §3: float and clear have no effect on a flex item ───────────

    /// <summary>
    /// Vector's logo is <c>display: flex</c> and both of its children — the sun icon and the
    /// wordmark — are <c>float: left</c>. Left floated, they are taken out of flow, the flex
    /// container sizes as though it held nothing, and the logo collapses.
    /// </summary>
    [Theory]
    [InlineData("flex")]
    [InlineData("inline-flex")]
    [InlineData("grid")]
    [InlineData("inline-grid")]
    public void A_Floated_Child_Of_A_Flex_Or_Grid_Container_Stops_Being_A_Float(string containerDisplay)
    {
        var (root, container) = ContainerWithChild(containerDisplay, CssConstants.Left, "both");

        FlexGridItemBlockification.Generate(root);

        var item = container.Boxes[0];
        Assert.Equal(CssConstants.None, item.Float);
        Assert.Equal(CssConstants.None, item.Clear);
    }

    /// <summary>And it becomes an item like any other, so it is blockified too.</summary>
    [Fact(Timeout = 600000)]
    public void A_Floated_Flex_Item_Is_Blockified()
    {
        var (root, container) = ContainerWithChild("flex", CssConstants.Left, CssConstants.None);

        FlexGridItemBlockification.Generate(root);

        Assert.Equal(CssConstants.Block, container.Boxes[0].Display);
    }

    /// <summary>The control: a float in an ordinary block container is still a float.</summary>
    [Fact(Timeout = 600000)]
    public void A_Floated_Child_Of_A_Block_Container_Is_Left_Alone()
    {
        var (root, container) = ContainerWithChild(CssConstants.Block, CssConstants.Left, "both");

        FlexGridItemBlockification.Generate(root);

        var child = container.Boxes[0];
        Assert.Equal(CssConstants.Left, child.Float);
        Assert.Equal("both", child.Clear);
    }

    /// <summary>An absolutely positioned child is not an item, so its float is not the item rule's
    /// to clear (its own out-of-flow rules already say <c>float</c> computes to <c>none</c>).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Abspos_Child_Of_A_Flex_Container_Is_Not_An_Item()
    {
        var (root, container) = ContainerWithChild("flex", CssConstants.Left, CssConstants.None);
        container.Boxes[0].Position = CssConstants.Absolute;

        FlexGridItemBlockification.Generate(root);

        Assert.Equal(CssConstants.Left, container.Boxes[0].Float);
    }

    // ─────────── CSS2.1 §8.3.1: the collapsed set is spent once ───────────

    /// <summary>
    /// A first in-flow child whose top margin the parent absorbs must record how much was spent, so
    /// that a later sibling collapsing through it subtracts that much. MediaWiki's article body
    /// opens with an empty <c>&lt;p&gt;</c> (a <c>&lt;style&gt;</c> and two abspos spans, nothing
    /// with a box), and without the record its collapse-through landed on top of the margin already
    /// applied and pushed the whole article down.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Empty_First_Child_Does_Not_Spend_Its_Collapsed_Margin_Twice()
    {
        var (root, container) = PageWithContainer(containerMarginTop: "16px");

        var empty = Block(container);
        empty.MarginTop = "8px";
        empty.MarginBottom = "16px";

        var following = Block(container);
        following.Height = "40px";

        root.PerformLayout(root.LayoutEnvironment);

        // The set is {16px container, 8px + 16px collapsed through the empty box} = 16px, applied
        // once above the container's content. The following block sits at the content top.
        Assert.Equal(container.ClientTop, following.Location.Y, 3);
    }

    /// <summary>
    /// The same record is what makes the collapse transitive: a margin two levels down is adjoining
    /// the grandparent's, so a wrapper <c>&lt;div&gt;</c> between them must not turn it into a gap.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Margin_Under_A_Wrapper_Still_Collapses_With_The_Grandparent()
    {
        var (root, container) = PageWithContainer(containerMarginTop: "24px");

        var wrapper = Block(container);
        var inner = Block(wrapper);
        inner.MarginTop = "10px";
        inner.Height = "50px";

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(container.ClientTop, inner.Location.Y, 3);
    }

    /// <summary>The control: a top border on the wrapper separates the margins, and the 10px is a
    /// real gap inside it.</summary>
    [Fact(Timeout = 600000)]
    public void A_Border_On_The_Wrapper_Stops_The_Collapse()
    {
        var (root, container) = PageWithContainer(containerMarginTop: "24px");

        var wrapper = Block(container);
        wrapper.BorderTopStyle = CssConstants.Solid;
        wrapper.BorderTopWidth = "1px";
        var inner = Block(wrapper);
        inner.MarginTop = "10px";
        inner.Height = "50px";

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(container.ClientTop + 1 + 10, inner.Location.Y, 3);
    }

    /// <summary>
    /// CSS2.1 §9.2.4: a <c>display: none</c> element generates no box, so it has no margins to
    /// collapse through and none to hand on. They were collected anyway, and a hidden element's
    /// margin then separated the two visible siblings around it. MediaWiki's empty
    /// <c>.vector-column-start</c> holds two <c>display: none</c> pinned containers with
    /// <c>margin-bottom: 32px</c>, and that 32px was pushing the whole article down.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Hidden_Childs_Margin_Does_Not_Collapse_Through_Its_Empty_Parent()
    {
        var (root, container) = PageWithContainer(containerMarginTop: null);

        var first = Block(container);
        first.Height = "20px";

        // An empty box whose only child is hidden: nothing of it reaches the flow.
        var empty = Block(container);
        var hidden = Block(empty);
        hidden.Display = CssConstants.None;
        hidden.MarginBottom = "32px";

        var following = Block(container);
        following.Height = "40px";

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(first.Location.Y + 20, following.Location.Y, 3);
    }

    /// <summary>The control: the same margin on a <em>visible</em> empty box does collapse
    /// through it and does separate the siblings.</summary>
    [Fact(Timeout = 600000)]
    public void A_Visible_Empty_Boxs_Margin_Still_Collapses_Through()
    {
        var (root, container) = PageWithContainer(containerMarginTop: null);

        var first = Block(container);
        first.Height = "20px";

        var empty = Block(container);
        var inner = Block(empty);
        inner.MarginBottom = "32px";

        var following = Block(container);
        following.Height = "40px";

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(first.Location.Y + 20 + 32, following.Location.Y, 3);
    }

    /// <summary>
    /// A collapse-through hands the next sibling the margin standing above the empty box, less
    /// what was already spent placing it. "Less" must not go below zero: margin that is not there
    /// cannot be cancelled, and subtracting it unclamped yields a *negative* margin that lifts the
    /// box above its own parent's content edge. MediaWiki's site notice was drawn 18px above the
    /// box that owns it for exactly this reason.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Collapse_Through_Never_Yields_A_Negative_Margin()
    {
        var (root, container) = PageWithContainer(containerMarginTop: "24px");

        // First in-flow child, empty, carrying a margin much smaller than the 24px already
        // standing above it.
        var empty = Block(container);
        var inner = Block(empty);
        inner.MarginTop = "6px";

        var following = Block(container);
        following.Height = "40px";

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(container.ClientTop, following.Location.Y, 3);
    }

    // ─────────── CSS Sizing 3 §5: an inline child's margins are on the line ───────────

    /// <summary>
    /// Vector wraps every thumbnail in a <c>display: table</c> figure whose image carries
    /// <c>margin: 3px</c>. Leaving an inline child's horizontal margins out of the max-content sum
    /// made the figure 6px narrower than the image needs, and <c>max-width</c> then scaled the photo
    /// down to fit a box that should have fitted it exactly.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Inline_Childs_Horizontal_Margins_Count_Toward_Max_Content()
    {
        double withMargins = MaxContentOfBlockHoldingInline(marginPx: 3);
        double withoutMargins = MaxContentOfBlockHoldingInline(marginPx: 0);

        Assert.Equal(withoutMargins + 6, withMargins, 3);
    }

    // ─────────── CSS2.1 §10.8.1: an inline replaced box aligns by its margin box ───────────

    /// <summary>
    /// The vertical half of the same margin: the image's own top margin has to move it down inside
    /// the line, and its bottom margin has to keep the line open below it. Neither happened, so a
    /// thumbnail sat flush with the top of a wrapper 6px too short for it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Inline_Images_Vertical_Margins_Are_Part_Of_Its_Line()
    {
        var (plainRoot, plainImage, plainWrapper) = PageWithAnInlineImage(margin: null);
        var (marginRoot, marginImage, marginWrapper) = PageWithAnInlineImage(margin: "3px");

        plainRoot.PerformLayout(plainRoot.LayoutEnvironment);
        marginRoot.PerformLayout(marginRoot.LayoutEnvironment);

        Assert.Equal(plainImage.Words[0].Top + 3, marginImage.Words[0].Top, 3);
        Assert.Equal(
            plainWrapper.ActualBottom - plainWrapper.Location.Y + 6,
            marginWrapper.ActualBottom - marginWrapper.Location.Y,
            3);
    }

    // ───────────────────────────────── the shapes ─────────────────────────────────

    private const float PageWidth = 400;
    private const double ImageWidth = 100;
    private const double ImageHeight = 50;

    private static (CssBox Root, CssBox Container) ContainerWithChild(string containerDisplay, string float_, string clear)
    {
        var (root, container) = PageWithContainer(containerMarginTop: null);
        container.Display = containerDisplay;

        var child = new CssBox(container, new HtmlTag("span", false, null), BaseUrl)
        {
            Display = CssConstants.Inline,
            Float = float_,
            Clear = clear,
            FontSize = "16px",
        };
        _ = child;

        return (root, container);
    }

    private static (CssBox Root, CssBox Container) PageWithContainer(string? containerMarginTop)
    {
        var environment = new StubLayoutEnvironment();
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = PointF.Empty,
            Size = new SizeF(PageWidth, PageWidth),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = environment,
        };
        var html = Block(root);
        var body = Block(html);
        var container = Block(body);
        if (containerMarginTop != null)
            container.MarginTop = containerMarginTop;

        return (root, container);
    }

    /// <summary>The max-content width of a block whose only child is an inline box carrying
    /// <paramref name="marginPx"/> of horizontal margin on each side.</summary>
    private static double MaxContentOfBlockHoldingInline(int marginPx)
    {
        var (root, container) = PageWithContainer(containerMarginTop: null);

        var inline = new CssBox(container, new HtmlTag("span", false, null), BaseUrl)
        {
            Display = CssConstants.Inline,
            FontSize = "16px",
            Width = "40px",
            MarginLeft = $"{marginPx}px",
            MarginRight = $"{marginPx}px",
        };
        _ = inline;

        root.PerformLayout(root.LayoutEnvironment);
        container.GetMinMaxWidth(out _, out double max);
        return max;
    }

    private static (CssBox Root, CssBoxImage Image, CssBox Wrapper) PageWithAnInlineImage(string? margin)
    {
        var (root, wrapper) = PageWithContainer(containerMarginTop: null);

        var image = new CssBoxImage(wrapper, new HtmlTag("img", true, new Dictionary<string, string> { ["src"] = "i.png" }), BaseUrl)
        {
            Display = CssConstants.Inline,
            FontSize = "16px",
            Width = "50px",
            Height = "50px",
        };
        if (margin != null)
        {
            image.MarginTop = margin;
            image.MarginBottom = margin;
        }

        return (root, image, wrapper);
    }

    private static CssBox Block(CssBox parent) => new(parent, null, BaseUrl)
    {
        Location = PointF.Empty,
        Size = new SizeF(PageWidth, 0),
        Display = CssConstants.Block,
        FontSize = "16px",
    };

    private sealed class StubImageLoader(Action<object?, RectangleF, bool> onComplete) : ILayoutImageLoader
    {
        private static readonly object TheImage = new();

        public object? Image { get; private set; }
        public RectangleF Rectangle => RectangleF.Empty;

        public void LoadImage(string src, IReadOnlyDictionary<string, string>? attributes, Uri baseUrl)
        {
            Image = TheImage;
            onComplete(TheImage, RectangleF.Empty, false);
        }

        public void Dispose() { }
    }

    private sealed class StubLayoutEnvironment : ILayoutEnvironment
    {
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new StubFont(size);
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;
        public ImageIntrinsics GetImageIntrinsics(object imageHandle) => new(ImageWidth, ImageHeight, true);
        public Broiler.Graphics.BColor ParseColor(string value) => default;
        public void RequestRefresh(bool relayout) { }
        public SizeF ViewportSize => new(1000, 1000);
        public PointF RootLocation => PointF.Empty;
        public SizeF ActualSize { get; set; }
        public bool AvoidGeometryAntialias => false;
        public SizeF PageSize => new(1000, 1000);
        public int MarginTop => 0;
        public void ReportLayoutError(string message, Exception? exception = null) { }
        public bool AvoidAsyncImagesLoading => true;
        public bool AvoidImagesLateLoading => true;
        public ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete) => new StubImageLoader(onComplete);
        public string FormatListMarker(int number, string style) => string.Empty;
    }

    private sealed class StubFont(double size) : Broiler.Graphics.ILayoutFont
    {
        public double Size { get; } = size;
        public double Height => size;
        public double UnderlineOffset => 0;
        public double LeftPadding => 0;
        public string? FontFeatures => null;
    }
}
