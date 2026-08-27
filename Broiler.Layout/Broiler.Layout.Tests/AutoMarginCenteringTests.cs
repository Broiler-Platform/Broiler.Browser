using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins CSS2.1 §10.3.7 (inline axis) / §10.6.4 (block axis) auto-margin centring for
/// absolutely-positioned and fixed boxes — <see cref="CssBox.ResolveOverconstrainedAutoMargins"/>.
/// When both opposing insets and a definite size are specified and <em>both</em> margins on that
/// axis are <c>auto</c>, the auto margins take equal shares of the leftover space, centring the box
/// (the engine equivalent of <c>inset:0; margin:auto</c>). Only that exact over-constrained +
/// both-auto case is resolved; a one-inset box, or one with a non-auto margin, is left to the
/// existing rules — so ordinary abspos layout is unchanged.
/// </summary>
public sealed class AutoMarginCenteringTests
{
    private static readonly Uri BaseUrl = new("file:///auto-margin.html");

    private static CssBox Box(CssBox parent, SizeF size, string position)
    {
        var b = new CssBox(parent, null, BaseUrl) { Location = new PointF(0, 0), Size = size, Position = position };
        if (parent == null)
            b.LayoutEnvironment = new FakeLayoutEnvironment();
        return b;
    }

    // A 100×100 box, insets all 0, margins all auto, definite size → both margins resolve to the
    // equal half-excess so the box centres in the 300×300 containing block.
    private static CssBox CenteredBox(string position = "fixed")
    {
        var root = Box(null, new SizeF(1000, 1000), "static");
        var b = Box(root, new SizeF(100, 100), position);
        b.Width = "100px";
        b.Height = "100px";
        b.Left = "0"; b.Right = "0"; b.Top = "0"; b.Bottom = "0";
        b.MarginLeft = "auto"; b.MarginRight = "auto";
        b.MarginTop = "auto"; b.MarginBottom = "auto";
        return b;
    }

    [Theory]
    [InlineData("fixed")]
    [InlineData("absolute")]
    public void BothInsets_BothMarginsAuto_DefiniteSize_Centres(string position)
    {
        var b = CenteredBox(position);
        b.ResolveOverconstrainedAutoMargins(300, 300);
        // excess = 300 - 0 - 0 - 100 = 200 → each margin 100.
        Assert.Equal(100, b.ActualMarginLeft, 3);
        Assert.Equal(100, b.ActualMarginRight, 3);
        Assert.Equal(100, b.ActualMarginTop, 3);
        Assert.Equal(100, b.ActualMarginBottom, 3);
    }

    [Fact(Timeout = 600000)]
    public void SurvivesAfterMarginAlreadyReadAsAuto()
    {
        // Reading ActualMargin* first rewrites the specified `auto` string to "0" (used value);
        // the centring must still recognise the margin was specified auto (latched flag).
        var b = CenteredBox();
        _ = b.ActualMarginLeft; _ = b.ActualMarginRight;
        _ = b.ActualMarginTop; _ = b.ActualMarginBottom;
        b.ResolveOverconstrainedAutoMargins(300, 300);
        Assert.Equal(100, b.ActualMarginLeft, 3);
        Assert.Equal(100, b.ActualMarginTop, 3);
    }

    [Fact(Timeout = 600000)]
    public void OnlyOneInset_NotTouched()
    {
        var b = CenteredBox();
        b.Right = "auto"; // only left is set → not over-constrained
        b.Bottom = "auto";
        b.ResolveOverconstrainedAutoMargins(300, 300);
        Assert.Equal(0, b.ActualMarginLeft, 3);
        Assert.Equal(0, b.ActualMarginTop, 3);
    }

    [Fact(Timeout = 600000)]
    public void NonAutoMargin_NotTouched()
    {
        var b = CenteredBox();
        b.MarginLeft = "5px"; // one margin non-auto → §10.3.7 ignores right, no centring
        b.MarginTop = "5px";
        b.ResolveOverconstrainedAutoMargins(300, 300);
        Assert.Equal(5, b.ActualMarginLeft, 3);
        Assert.Equal(5, b.ActualMarginTop, 3);
    }

    [Fact(Timeout = 600000)]
    public void AutoWidth_NotCentredHorizontally()
    {
        var b = CenteredBox();
        b.Width = "auto"; // width auto → §10.3.7 solves for width, not margins
        b.ResolveOverconstrainedAutoMargins(300, 300);
        Assert.Equal(0, b.ActualMarginLeft, 3);
        // vertical still centres (height is definite)
        Assert.Equal(100, b.ActualMarginTop, 3);
    }

    [Fact(Timeout = 600000)]
    public void IntrinsicWidth_ShrinkWrapped_IsCentred()
    {
        // A `width: fit-content` box shrink-wraps (ResolveBlockUsedWidth resolves Size.Width before
        // positioning); the auto margins then centre that resolved size, just like an explicit width.
        var root = Box(null, new SizeF(1000, 1000), "static");
        var b = Box(root, new SizeF(100, 100), "fixed");
        b.Width = "fit-content";              // intrinsic keyword, size already resolved to 100 above
        b.Height = "100px";
        b.Left = "0"; b.Right = "0"; b.Top = "0"; b.Bottom = "0";
        b.MarginLeft = "auto"; b.MarginRight = "auto";
        b.MarginTop = "auto"; b.MarginBottom = "auto";
        b.ResolveOverconstrainedAutoMargins(300, 300);
        // excess = 300 - 0 - 0 - 100 = 200 → each margin 100.
        Assert.Equal(100, b.ActualMarginLeft, 3);
        Assert.Equal(100, b.ActualMarginRight, 3);
    }

    [Fact(Timeout = 600000)]
    public void ExcessNegative_MarginsZero()
    {
        var root = Box(null, new SizeF(1000, 1000), "static");
        var b = Box(root, new SizeF(400, 100), "fixed");
        b.Width = "400px"; b.Height = "100px";
        b.Left = "0"; b.Right = "0"; b.Top = "0"; b.Bottom = "0";
        b.MarginLeft = "auto"; b.MarginRight = "auto";
        b.MarginTop = "auto"; b.MarginBottom = "auto";
        b.ResolveOverconstrainedAutoMargins(300, 300);
        // 400 wider than the 300 CB → negative excess clamps to 0.
        Assert.Equal(0, b.ActualMarginLeft, 3);
    }

    private sealed class FakeLayoutEnvironment : Broiler.Layout.ILayoutEnvironment
    {
        private static readonly Broiler.Graphics.ILayoutFont TheFont = new FakeFont();
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => TheFont;
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;
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
