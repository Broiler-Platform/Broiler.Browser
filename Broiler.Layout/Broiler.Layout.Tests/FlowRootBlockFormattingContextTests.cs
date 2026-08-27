using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Display 3 §2.5: <c>display: flow-root</c> is "block box that establishes a new block
/// formatting context" — block-level outside, BFC-rooting inside, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The engine modelled neither half. <c>flow-root</c> reached layout as an unrecognised display
/// string: <see cref="CssBox.IsBlock"/> did not name it, so it was neither block nor inline and
/// laid out and painted <em>nothing at all</em> — a <c>flow-root</c> element reserved its
/// explicit height, if it had one, and its background and its whole subtree simply never
/// appeared. And four near-copies of "does this box establish a BFC?" had drifted apart across
/// the engine (one of them had never gained the flex/grid displays), so even a recognised
/// <c>flow-root</c> would have rooted a BFC in some float calculations and not others.
/// </para>
/// <para>
/// Both halves are asserted here: that the box lays out and sizes like a block, and that it
/// contains its floats the way the other BFC roots do. The consolidation is what makes the
/// second one hold everywhere rather than in whichever copy was edited —
/// <see cref="CssBoxHelper.EstablishesBfc"/> is now the only answer to the question, so the
/// parity case below covers every caller by construction.
/// </para>
/// <para>
/// Found through WPT reftests: <c>css/css-page/monolithic-overflow-014-print</c> and 31 others
/// across <c>css-break</c>, <c>css-box/margin-trim</c>, <c>CSS2/floats</c> and <c>css-rhythm</c>
/// whose <em>reference</em> file states the expected rendering with a <c>flow-root</c> wrapper.
/// </para>
/// </remarks>
public sealed class FlowRootBlockFormattingContextTests
{
    private static readonly Uri BaseUrl = new("file:///flow-root.html");

    [Fact(Timeout = 600000)]
    public void FlowRoot_Is_Block_Level()
    {
        Assert.True(Leaf(display: "flow-root").IsBlock);
        Assert.False(Leaf(display: "flow-root").IsInline);
    }

    [Fact(Timeout = 600000)]
    public void FlowRoot_Establishes_A_Block_Formatting_Context() =>
        Assert.True(CssBoxHelper.EstablishesBfc(Leaf(display: "flow-root")));

    // The displays that already rooted a BFC must keep doing so, and a plain in-flow block
    // must still not — the consolidated predicate is only correct if it did not widen.
    [Theory]
    [InlineData("inline-block", true)]
    [InlineData("table-cell", true)]
    [InlineData("flex", true)]
    [InlineData("inline-flex", true)]
    [InlineData("grid", true)]
    [InlineData("inline-grid", true)]
    [InlineData("flow-root", true)]
    [InlineData("block", false)]
    [InlineData("inline", false)]
    [InlineData("list-item", false)]
    public void EstablishesBfc_Answers_For_Each_Display(string display, bool expected) =>
        Assert.Equal(expected, CssBoxHelper.EstablishesBfc(Leaf(display)));

    [Theory]
    [InlineData("overflow", CssConstants.Hidden, true)]
    [InlineData("overflow", CssConstants.Visible, false)]
    [InlineData("float", CssConstants.Left, true)]
    [InlineData("position", CssConstants.Absolute, true)]
    [InlineData("position", CssConstants.Fixed, true)]
    public void EstablishesBfc_Answers_For_The_Non_Display_Triggers(
        string property, string value, bool expected)
    {
        var box = Leaf(CssConstants.Block);
        switch (property)
        {
            case "overflow": box.Overflow = value; break;
            case "float": box.Float = value; break;
            case "position": box.Position = value; break;
        }

        Assert.Equal(expected, CssBoxHelper.EstablishesBfc(box));
    }

    // CSS2.1 §10.6.7: a BFC root's auto height includes its descendant floats. This is the
    // behaviour the whole keyword exists for, and the one the reference files above rely on.
    [Fact(Timeout = 600000)]
    public void FlowRoot_Auto_Height_Contains_A_Descendant_Float()
    {
        var (root, container) = TreeWithFloatedChild(display: "flow-root");
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(200, container.ActualBottom - container.Location.Y, 3);
    }

    // The control: a plain block does not root a BFC, so the same float escapes its height.
    // Without this, a change that made every block contain its floats would pass the case above.
    [Fact(Timeout = 600000)]
    public void A_Plain_Block_Does_Not_Contain_The_Same_Float()
    {
        var (root, container) = TreeWithFloatedChild(CssConstants.Block);
        root.PerformLayout(root.LayoutEnvironment);

        Assert.True(container.ActualBottom - container.Location.Y < 200);
    }

    // `overflow: hidden` is the other way to root a BFC and was the one that already worked;
    // flow-root has to reach the same height, not merely a non-zero one.
    [Fact(Timeout = 600000)]
    public void FlowRoot_And_Overflow_Hidden_Reach_The_Same_Height()
    {
        var (flowRootTree, flowRoot) = TreeWithFloatedChild(display: "flow-root");
        flowRootTree.PerformLayout(flowRootTree.LayoutEnvironment);

        var (overflowTree, overflow) = TreeWithFloatedChild(CssConstants.Block);
        overflow.Overflow = CssConstants.Hidden;
        overflowTree.PerformLayout(overflowTree.LayoutEnvironment);

        Assert.Equal(
            overflow.ActualBottom - overflow.Location.Y,
            flowRoot.ActualBottom - flowRoot.Location.Y,
            3);
    }

    /// <summary>A root, and a container of <paramref name="display"/> holding one 200px float.</summary>
    private static (CssBox Root, CssBox Container) TreeWithFloatedChild(string display)
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new StubLayoutEnvironment(),
        };

        var container = new CssBox(root, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
            Display = display,
            FontSize = "16px",
        };

        var floated = new CssBox(container, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(50, 200),
            Display = CssConstants.Block,
            FontSize = "16px",
            Float = CssConstants.Left,
        };
        floated.Width = "50px";
        floated.Height = "200px";

        return (root, container);
    }

    private static CssBox Leaf(string display) =>
        new(null, null, BaseUrl) { Display = display, LayoutEnvironment = new StubLayoutEnvironment() };

    private sealed class StubLayoutEnvironment : ILayoutEnvironment
    {
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new StubFont(size);
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;
        public ImageIntrinsics GetImageIntrinsics(object imageHandle) => default;
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
        public ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete) => null!;
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
