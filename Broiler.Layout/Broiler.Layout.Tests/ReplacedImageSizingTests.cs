using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS 2.1 §10.4: how a replaced element's used width and height come out of what the author stated
/// and what the image itself is.
/// </summary>
/// <remarks>
/// The intrinsic ratio fills in a dimension left <c>auto</c>; it never overrules one the author
/// stated. A percentage width is a stated width like any other — it is only resolved later, against
/// the containing block — and treating it as if it were <c>auto</c> made
/// <c>&lt;img width="100%" height="50"&gt;</c> come out as tall as it was wide. That is the shape
/// CSS2's own reference documents use to draw a coloured band, so the cost landed on the
/// <em>reference</em> side of 85 reftests in <c>css/CSS2/backgrounds</c> alone, where the test was
/// right all along.
/// </remarks>
public sealed class ReplacedImageSizingTests
{
    private static readonly Uri BaseUrl = new("file:///image.html");

    /// <summary>A 10×10 image (ratio 1) in a 400px-wide containing block.</summary>
    private static CssRectImage Measure(string width, string height, double imageWidth = 10, double imageHeight = 10)
    {
        var environment = new FakeLayoutEnvironment(new ImageIntrinsics(imageWidth, imageHeight, true));

        var containingBlock = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(400, 400),
            LayoutEnvironment = environment,
        };

        var box = new CssBox(containingBlock, null, BaseUrl) { Display = "inline", Width = width, Height = height };
        var word = new CssRectImage(box) { Image = new object() };

        CssLayoutEngine.MeasureImageSize(environment, word);
        return word;
    }

    [Fact(Timeout = 600000)]
    public void A_Percentage_Width_And_A_Stated_Height_Are_Both_Used()
    {
        var word = Measure("100%", "50px");

        Assert.Equal(400, word.Width, 3);
        Assert.Equal(50, word.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Length_Width_And_A_Stated_Height_Are_Both_Used()
    {
        var word = Measure("200px", "20px");

        Assert.Equal(200, word.Width, 3);
        Assert.Equal(20, word.Height, 3);
    }

    // With the height left to the image, the ratio fills it in — from a percentage width as much as
    // from a length one.
    [Theory]
    [InlineData("100%", 400, 200)]
    [InlineData("50%", 200, 100)]
    [InlineData("120px", 120, 60)]
    public void An_Auto_Height_Comes_From_The_Ratio(string width, double expectedWidth, double expectedHeight)
    {
        var word = Measure(width, "auto", imageWidth: 20, imageHeight: 10);

        Assert.Equal(expectedWidth, word.Width, 3);
        Assert.Equal(expectedHeight, word.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void An_Auto_Width_Comes_From_The_Ratio()
    {
        var word = Measure("auto", "30px", imageWidth: 20, imageHeight: 10);

        Assert.Equal(60, word.Width, 3);
        Assert.Equal(30, word.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void Neither_Stated_Is_The_Intrinsic_Size()
    {
        var word = Measure("auto", "auto", imageWidth: 33, imageHeight: 17);

        Assert.Equal(33, word.Width, 3);
        Assert.Equal(17, word.Height, 3);
    }

    // ─────────── CSS2.1 §10.4/§10.7: min-/max-* on an inline replaced element ───────────
    //
    // These are WPT css-sizing/block-image-percentage-max-height-inside-inline and
    // css-sizing/image-percentage-max-height-in-anonymous-block, which both put a 1×1 image with
    // `height: 1000px; max-width: 100px; max-height: 100%` inside a 100px-tall <div> and expect a
    // 100px green square. Every expectation below was taken from Chromium rather than read off the
    // spec — which is how the third case got written down correctly.

    /// <summary>The image sits in <c>div{height:100px}</c> → (optionally an anonymous block) → img,
    /// which is the shape both WPT tests build.</summary>
    private static CssRectImage MeasureInDefiniteBlock(
        string width, string height, string maxWidth, string maxHeight,
        bool throughAnonymousBlock, double imageWidth = 1, double imageHeight = 1)
    {
        var environment = new FakeLayoutEnvironment(new ImageIntrinsics(imageWidth, imageHeight, true));

        var root = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(400, 400),
            LayoutEnvironment = environment,
        };
        var definite = new CssBox(root, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = "block",
            Width = "100px",
            Height = "100px",
            Size = new SizeF(100, 0),
            LayoutEnvironment = environment,
        };
        // An anonymous block has no HtmlTag and never an author height, so it must not be the
        // percentage basis — the walk has to climb past it to the <div>.
        var parent = throughAnonymousBlock
            ? new CssBox(definite, null, BaseUrl) { Display = "block", LayoutEnvironment = environment }
            : definite;

        var box = new CssBox(parent, new HtmlTag("img", true, null), BaseUrl)
        {
            Display = "inline",
            Width = width,
            Height = height,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            LayoutEnvironment = environment,
        };
        var word = new CssRectImage(box) { Image = new object() };

        CssLayoutEngine.MeasureImageSize(environment, word);
        return word;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Percentage_Max_Height_Resolves_Against_The_Nearest_Definite_Element(bool throughAnonymousBlock)
    {
        var word = MeasureInDefiniteBlock("auto", "1000px", "100px", "100%", throughAnonymousBlock);

        Assert.Equal(100, word.Width, 3);
        Assert.Equal(100, word.Height, 3);
    }

    // A stated height is the author's: max-width shortens the ratio-derived width and leaves it be.
    [Fact(Timeout = 600000)]
    public void Max_Width_Shortens_The_Derived_Width_Only()
    {
        var word = MeasureInDefiniteBlock("auto", "1000px", "100px", "none", throughAnonymousBlock: false);

        Assert.Equal(100, word.Width, 3);
        Assert.Equal(1000, word.Height, 3);
    }

    // Both axes auto and both maximums violated: §10.4's table, which keeps the ratio rather than
    // taking one bound per axis (that would be 120×100 here).
    [Fact(Timeout = 600000)]
    public void Both_Maximums_Violated_Keeps_The_Intrinsic_Ratio()
    {
        var word = MeasureInDefiniteBlock("auto", "auto", "120px", "100px", throughAnonymousBlock: false,
            imageWidth: 8000, imageHeight: 8000);

        Assert.Equal(100, word.Width, 3);
        Assert.Equal(100, word.Height, 3);
    }

    // CSS2.1 §10.7: with nothing definite above it, a percentage max-height is `none`.
    [Fact(Timeout = 600000)]
    public void A_Percentage_Max_Height_Against_An_Indefinite_Ancestor_Does_Not_Clamp()
    {
        var environment = new FakeLayoutEnvironment(new ImageIntrinsics(1, 1, true));
        var root = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(400, 400),
            LayoutEnvironment = environment,
        };
        var autoHeight = new CssBox(root, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = "block",
            LayoutEnvironment = environment,
        };
        var box = new CssBox(autoHeight, new HtmlTag("img", true, null), BaseUrl)
        {
            Display = "inline",
            Height = "1000px",
            MaxHeight = "10%",
            LayoutEnvironment = environment,
        };
        var word = new CssRectImage(box) { Image = new object() };

        CssLayoutEngine.MeasureImageSize(environment, word);

        Assert.Equal(1000, word.Height, 3);
    }

    // ParseLength resolves an unrecognised unit to 0px, so a keyword max-* must be rejected before
    // it reaches the parser rather than clamping the image out of existence.
    [Theory]
    [InlineData("fit-content")]
    [InlineData("max-content")]
    [InlineData("stretch")]
    public void A_Keyword_Max_Width_Leaves_The_Image_Alone(string keyword)
    {
        var word = MeasureInDefiniteBlock("auto", "auto", keyword, "none", throughAnonymousBlock: false,
            imageWidth: 40, imageHeight: 20);

        Assert.Equal(40, word.Width, 3);
        Assert.Equal(20, word.Height, 3);
    }

    // The other half of that guard: zero is min-*'s initial value but a real clamp for max-*, so it
    // must not be filtered out with the keywords (WPT CSS2/normal-flow/max-height-101).
    [Fact(Timeout = 600000)]
    public void A_Zero_Max_Height_Clamps_To_Nothing()
    {
        var word = MeasureInDefiniteBlock("auto", "auto", "none", "0", throughAnonymousBlock: false,
            imageWidth: 40, imageHeight: 20);

        Assert.Equal(0, word.Height, 3);
    }

    // CSS2.1 §10.3.8/§10.6.5: an absolutely positioned replaced element is sized by the *replaced*
    // rules — the insets decide where it goes, never how big it is, and `right`/`bottom` are what
    // give way when they over-constrain. Broiler used to solve the §10.3.7 inset equation for it
    // (stretching an `<img left:4em; right:0; top:4em; bottom:0>` across the whole inset box) or, if
    // `right` was absent, shrink-to-fit it against its non-existent children and get zero — which
    // is why an `<img position:absolute; left:4em; top:4em>` painted nothing at all.
    // WPT CSS2/positioning/abspos-025 and the 22 `absolute-replaced-*` tests pin the rendering; this
    // pins the sizing rule the call sites now share.
    [Theory]
    [InlineData("auto", "auto", 15d, 15d)]
    [InlineData("40px", "auto", 40d, 40d)]
    [InlineData("auto", "40px", 40d, 40d)]
    public void An_Abspos_Replaced_Box_Is_Sized_By_Its_Natural_Size_Not_By_Its_Insets(
        string width, string height, double expectedWidth, double expectedHeight)
    {
        var environment = new FakeLayoutEnvironment(new ImageIntrinsics(15, 15, true));
        var root = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(400, 400),
            LayoutEnvironment = environment,
        };
        var box = new CssBox(root, new HtmlTag("canvas", false, null), BaseUrl)
        {
            Display = "block",
            Position = "absolute",
            Left = "64px",
            Right = "0",
            Top = "64px",
            Bottom = "0",
            Width = width,
            Height = height,
            IntrinsicReplacedSize = new SizeF(15, 15),
            LayoutEnvironment = environment,
        };

        box.ResolveReplacedContentSize(box.IntrinsicReplacedSize.Value, 400,
            out double contentWidth, out double contentHeight);

        Assert.Equal(expectedWidth, contentWidth, 3);
        Assert.Equal(expectedHeight, contentHeight, 3);
    }

    // ─────────── the content box the replaced rules answer in, and the border box ───────────

    /// <summary>
    /// CSS2.1 §10.3.2/§10.6.2 answer in <em>content</em> sizes — that is the frame the natural size
    /// and the ratio are stated in — so the used border box is the content box plus the edges,
    /// whatever <c>box-sizing</c> says. Converting as though the number were an author-specified
    /// length made <c>box-sizing: border-box</c> a no-op in the wrong direction and dropped the
    /// padding and border entirely: a 16×16 image with <c>padding: 1px 2px 3px 4px</c> reported a
    /// 16×16 border box where its content alone is 16×16 (css-flexbox/image-as-flexitem-size-007),
    /// and one sized from the other axis through its ratio came out the content size in both.
    /// </summary>
    [Theory]
    [InlineData("content-box", 22, 20)]
    [InlineData("border-box", 22, 20)]
    public void A_Replaced_Border_Box_Is_Its_Content_Box_Plus_The_Edges(
        string boxSizing, double expectedWidth, double expectedHeight)
    {
        var environment = new FakeLayoutEnvironment(new ImageIntrinsics(16, 16, true));

        var root = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(400, 400),
            LayoutEnvironment = environment,
        };
        var box = new CssBox(root, new HtmlTag("canvas", false, null), BaseUrl)
        {
            Display = "block",
            BoxSizing = boxSizing,
            PaddingTop = "1px",
            PaddingRight = "2px",
            PaddingBottom = "3px",
            PaddingLeft = "4px",
            IntrinsicReplacedSize = new SizeF(16, 16),
            LayoutEnvironment = environment,
        };

        Assert.True(box.TryResolveReplacedBorderBoxSize(400, out double width, out double height));
        Assert.Equal(expectedWidth, width, 3);
        Assert.Equal(expectedHeight, height, 3);
    }

    // Minimal ILayoutEnvironment: a fixed font, and the one image's intrinsics.
    private sealed class FakeLayoutEnvironment(ImageIntrinsics intrinsics) : Broiler.Layout.ILayoutEnvironment
    {
        private static readonly Broiler.Graphics.ILayoutFont TheFont = new FakeFont();
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => TheFont;
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;
        public Broiler.Layout.ImageIntrinsics GetImageIntrinsics(object imageHandle) => intrinsics;
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
