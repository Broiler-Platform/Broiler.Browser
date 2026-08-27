using System;
using System.Drawing;
using System.Linq;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS 2.1 §9.4.3: <c>position: relative</c> on an <em>inline-level</em> box.
/// </summary>
/// <remarks>
/// The offset is visual — the box keeps its place in the flow and its content is drawn somewhere
/// else — so it has to reach the words, which is what <c>OffsetLeft</c>/<c>OffsetTop</c> do.
/// <c>PerformLayout</c> applies it for every box it lays out, but an inline-level box is laid out by
/// <c>CreateLineBoxes</c> and never goes through <c>PerformLayout</c>, so nothing applied it at all:
/// an inline <c>&lt;span&gt;</c> or <c>&lt;img&gt;</c> asking to be moved rendered at its static
/// position. It is the shape CSS2's own reference documents use to place a swatch, so the cost
/// landed on the reference side — 68 of <c>css/css-writing-modes</c>' reftests, whose references are
/// ordinary horizontal documents.
/// </remarks>
public sealed class InlineRelativePositionTests
{
    private static readonly Uri BaseUrl = new("file:///relative.html");

    private static CssBox Container(string writingMode = "horizontal-tb")
    {
        var root = new CssBox(null, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = "block",
            Location = new PointF(0, 0),
            Size = new SizeF(400, 200),
            WritingMode = writingMode,
            LayoutEnvironment = new FakeLayoutEnvironment(),
        };
        return root;
    }

    private static CssBox Inline(CssBox parent, string? left = null, string? top = null,
        string? right = null, string? bottom = null, string position = "relative")
    {
        var box = new CssBox(parent, new HtmlTag("span", false, null), BaseUrl) { Display = "inline" };
        box.Position = position;
        if (left != null) box.Left = left;
        if (top != null) box.Top = top;
        if (right != null) box.Right = right;
        if (bottom != null) box.Bottom = bottom;
        box.Words.Add(new CssRectWord(box, "X", false, false));
        return box;
    }

    private static (double Left, double Top) WordOf(CssBox box) =>
        (box.Words.Single().Left, box.Words.Single().Top);

    /// <summary>
    /// Where the word lands, less where the same word lands with no offset at all. Line layout
    /// assigns word positions from scratch on every pass — which is also why a re-layout cannot
    /// compound these offsets — so the shift is only readable against a static control.
    /// </summary>
    private static (double X, double Y) Shift(
        string writingMode = "horizontal-tb",
        string? left = null, string? top = null, string? right = null, string? bottom = null)
    {
        var moved = Container(writingMode);
        var movedSpan = Inline(moved, left, top, right, bottom);
        moved.PerformLayout(moved.LayoutEnvironment);

        var still = Container(writingMode);
        var stillSpan = Inline(still, position: "static");
        still.PerformLayout(still.LayoutEnvironment);

        var (mx, my) = WordOf(movedSpan);
        var (sx, sy) = WordOf(stillSpan);
        return (mx - sx, my - sy);
    }

    [Fact(Timeout = 600000)]
    public void An_Inline_Box_Offsets_Its_Words() =>
        Assert.Equal((60, 30), Shift(left: "60px", top: "30px"));

    // CSS2.1 §9.4.3: with `left` auto and `right` set, the box moves the other way.
    [Fact(Timeout = 600000)]
    public void Right_And_Bottom_Offset_The_Other_Way() =>
        Assert.Equal((-15, -5), Shift(right: "15px", bottom: "5px"));

    [Fact(Timeout = 600000)]
    public void A_Static_Inline_Box_Is_Left_Where_It_Was() =>
        Assert.Equal((0, 0), Shift());

    // Offsets compound: a relative box inside a relative box moves by both.
    [Fact(Timeout = 600000)]
    public void Nested_Relative_Inline_Boxes_Compound()
    {
        var moved = Container();
        var outer = Inline(moved, left: "60px", top: "30px");
        var inner = Inline(outer, left: "5px", top: "7px");
        moved.PerformLayout(moved.LayoutEnvironment);

        var still = Container();
        var stillOuter = Inline(still, position: "static");
        var stillInner = Inline(stillOuter, position: "static");
        still.PerformLayout(still.LayoutEnvironment);

        var (ix, iy) = WordOf(inner);
        var (sx, sy) = WordOf(stillInner);
        Assert.Equal((65, 37), (ix - sx, iy - sy));
    }

    // An inline-block is inline-*level* but is laid out as a block, so its own PerformLayout
    // applies its offset — the walk must leave it alone, or it moves by double.
    // CSS2/margin-padding-clear/margin-collapse-001's reference stacks two of them and is the pair
    // that caught it; this pins the half the walk is responsible for, since the harness below does
    // not run an inline-block's own layout. A shift of zero here is the walk declining to touch it.
    [Theory]
    [InlineData("inline-block")]
    [InlineData("inline-flex")]
    [InlineData("inline-grid")]
    public void The_Walk_Leaves_A_Box_That_Lays_Itself_Out_Alone(string display)
    {
        var moved = Container();
        var box = Inline(moved, left: "60px", top: "30px");
        box.Display = display;
        moved.PerformLayout(moved.LayoutEnvironment);

        var still = Container();
        var stillBox = Inline(still, position: "static");
        stillBox.Display = display;
        still.PerformLayout(still.LayoutEnvironment);

        var (mx, my) = WordOf(box);
        var (sx, sy) = WordOf(stillBox);
        Assert.Equal((0, 0), (mx - sx, my - sy));
    }

    // `left`/`top` are physical, but a vertical container's words sit in the engine's rotated
    // space, so the offset would arrive turned a quarter turn. Until that mapping exists, a
    // vertical container is left alone rather than moved wrongly — which is what
    // css-writing-modes/vrl-inline-paint-invalidation asks for.
    [Theory]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    [InlineData("sideways-rl")]
    [InlineData("sideways-lr")]
    public void A_Vertical_Container_Leaves_Its_Inline_Boxes_Alone(string writingMode) =>
        Assert.Equal((0, 0), Shift(writingMode, left: "60px", top: "30px"));

    // Minimal ILayoutEnvironment: a fixed font, and nothing else these boxes reach.
    private sealed class FakeLayoutEnvironment : Broiler.Layout.ILayoutEnvironment
    {
        private static readonly Broiler.Graphics.ILayoutFont TheFont = new FakeFont();
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => TheFont;
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => new(8, 16);
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = text.Length; charFitWidth = 8; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 4;
        public Broiler.Layout.ImageIntrinsics GetImageIntrinsics(object imageHandle) => default;
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
        public Broiler.Layout.ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete) => null!;
        public string FormatListMarker(int number, string style) => string.Empty;
    }

    private sealed class FakeFont : Broiler.Graphics.ILayoutFont
    {
        public double Size => 16;
        public double Height => 16;
        public double UnderlineOffset => 0;
        public double LeftPadding => 0;
        public string? FontFeatures => null;
    }
}
