using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS2.1 §9.7: a float has its computed <c>display</c> blockified, the same way an out-of-flow box
/// does. Only the out-of-flow half was implemented, and a floated <em>replaced</em> element paid for
/// the other half by disappearing outright.
/// </summary>
/// <remarks>
/// <para>
/// A replaced box that stays inline-level takes the inline branch of <c>PerformLayoutImp</c>, which
/// sizes a box from its words. A replaced box has no words to be sized from, so it came out 0×0 at
/// (0, 0): no image, no border, no background, and no layout space either. Measured on the shape
/// below before the fix, an <c>&lt;img&gt;</c> that paints 4 096 pixels unfloated painted zero
/// anywhere on the canvas with <c>float: left</c>.
/// </para>
/// <para>
/// Found on WPT <c>css-images/object-fit-contain-svg-001i</c> and the 79 other
/// <c>object-fit-*-svg-*</c> tests, which float every image they lay out and so rendered a blank
/// page — matching their equally blank reference at 100 % and reporting a pass. Routing SVG images
/// through the one renderer is what made the blankness visible; the float bug is older than that and
/// hit raster images just as hard.
/// </para>
/// <para>
/// Both halves are asserted: which boxes the rule now claims (and, as much to the point, which it
/// still does not), and that a claimed one actually lays out at a size after a real layout pass.
/// </para>
/// </remarks>
public sealed class FloatedReplacedBlockificationTests
{
    private static readonly Uri BaseUrl = new("file:///float.html");

    // ───────────────────────── which boxes the rule claims ─────────────────────────

    [Theory]
    [InlineData(CssConstants.Left)]
    [InlineData(CssConstants.Right)]
    public void A_Floated_Image_Is_Blockified(string side) =>
        Assert.True(ImageBox(float_: side).IsBlockifiedFloatedReplaced);

    [Fact(Timeout = 600000)]
    public void An_Unfloated_Image_Is_Not() =>
        Assert.False(ImageBox(float_: CssConstants.None).IsBlockifiedFloatedReplaced);

    /// <summary>A <c>&lt;canvas&gt;</c> or <c>&lt;video&gt;</c> is replaced without carrying an image
    /// word — its intrinsic size is what says so, and it fails the same way.</summary>
    [Fact(Timeout = 600000)]
    public void A_Floated_Box_With_An_Intrinsic_Replaced_Size_Is_Blockified()
    {
        var box = Leaf();
        box.Float = CssConstants.Left;
        box.IntrinsicReplacedSize = new SizeF(300, 150);

        Assert.True(box.IsBlockifiedFloatedReplaced);
    }

    /// <summary>The control that keeps the rule narrow: a floated <c>&lt;span&gt;</c> is sized from
    /// its words today and does render, so it must keep taking the inline path. Blockifying every
    /// float is what §9.7 actually asks for, but it is a separate change with its own evidence.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Floated_Non_Replaced_Inline_Is_Left_On_The_Inline_Path()
    {
        var box = Leaf();
        box.Float = CssConstants.Left;

        Assert.False(box.IsBlockifiedFloatedReplaced);
    }

    /// <summary>A zero intrinsic size is what an undecoded or broken replaced element reports, and it
    /// is not evidence of anything — the rule must not claim a box on it.</summary>
    [Fact(Timeout = 600000)]
    public void A_Zero_Intrinsic_Replaced_Size_Does_Not_Count()
    {
        var box = Leaf();
        box.Float = CssConstants.Left;
        box.IntrinsicReplacedSize = SizeF.Empty;

        Assert.False(box.IsBlockifiedFloatedReplaced);
    }

    // ───────────────────────── and that it lays out at a size ─────────────────────────

    /// <summary>The defect itself: 0×0 at (0, 0) is what a floated image used to come out as.</summary>
    [Theory]
    [InlineData(CssConstants.Left)]
    [InlineData(CssConstants.Right)]
    public void A_Floated_Image_Lays_Out_At_Its_Stated_Size(string side)
    {
        var (root, image) = PageWithAnImage(float_: side, width: "64px", height: "64px");

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(64, image.Size.Width, 3);
        Assert.Equal(64, image.Size.Height, 3);
    }

    /// <summary>Floating an image must not change how big the image itself is — only where it goes.
    /// The image word is what paints on both paths, so it is what the two are compared through.
    /// </summary>
    [Theory]
    [InlineData("64px", "64px")]
    [InlineData(null, null)]
    public void Floating_An_Image_Does_Not_Change_How_Big_It_Is(string? width, string? height)
    {
        var (floatedRoot, floated) = PageWithAnImage(CssConstants.Left, width, height);
        var (plainRoot, plain) = PageWithAnImage(CssConstants.None, width, height);

        floatedRoot.PerformLayout(floatedRoot.LayoutEnvironment);
        plainRoot.PerformLayout(plainRoot.LayoutEnvironment);

        Assert.Equal(plain.Words[0].Width, floated.Words[0].Width, 3);
        Assert.Equal(plain.Words[0].Height, floated.Words[0].Height, 3);
    }

    /// <summary>
    /// CSS2.1 §10.3.6: a floating <em>replaced</em> box with <c>width: auto</c> takes its natural
    /// width. §10.3.5's shrink-to-fit is for non-replaced boxes and measures children, of which an
    /// image has none — so blockifying the float alone left it 0px wide, reserving no space for text
    /// to flow around and drawing no border or background, even though the word inside still carried
    /// the natural size. The shape is the common one: an <c>&lt;img&gt;</c> floated by CSS that
    /// states no dimensions at all.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Floated_Image_With_No_Stated_Size_Takes_Its_Intrinsic_One()
    {
        var (root, image) = PageWithAnImage(CssConstants.Left, width: null, height: null);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(ImageWidth, image.Size.Width, 3);
        Assert.Equal(ImageHeight, image.Size.Height, 3);
    }

    /// <summary>The control for that guard: a floating <em>non</em>-replaced box must still
    /// shrink-to-fit rather than fill its containing block, which is the one thing exempting
    /// replaced boxes from §10.3.5 could plausibly have broken.</summary>
    [Fact(Timeout = 600000)]
    public void A_Floated_Non_Replaced_Box_Still_Shrinks_To_Fit()
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
        var body = Block(Block(root));

        var floated = Block(body);
        floated.Float = CssConstants.Left;
        var child = Block(floated);
        child.Width = "30px";
        child.Height = "10px";

        root.PerformLayout(environment);

        Assert.Equal(30, floated.Size.Width, 3);
    }

    /// <summary>A float is taken out of the normal flow but still shortens the line beside it, so the
    /// image has to end up left of centre for <c>float: left</c> and right of it for
    /// <c>float: right</c> — a box merely sized but never positioned would sit at the origin for both.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Right_Float_Ends_Up_On_The_Right()
    {
        var (leftRoot, leftImage) = PageWithAnImage(float_: CssConstants.Left, width: "64px", height: "64px");
        var (rightRoot, rightImage) = PageWithAnImage(float_: CssConstants.Right, width: "64px", height: "64px");

        leftRoot.PerformLayout(leftRoot.LayoutEnvironment);
        rightRoot.PerformLayout(rightRoot.LayoutEnvironment);

        Assert.True(
            rightImage.Location.X > leftImage.Location.X,
            $"a right float should sit right of a left one ({rightImage.Location.X} vs {leftImage.Location.X})");
    }

    // ───────────────────────────────── the shapes ─────────────────────────────────

    private const double ImageWidth = 100;
    private const double ImageHeight = 50;
    private const float PageWidth = 400;

    /// <summary>A bare inline box with no parent chain — enough for the predicate, which reads only
    /// this box.</summary>
    private static CssBox Leaf()
    {
        var environment = new StubLayoutEnvironment();
        var root = new CssBox(null, null, BaseUrl)
        {
            Display = CssConstants.Block,
            Size = new SizeF(PageWidth, PageWidth),
            LayoutEnvironment = environment,
        };
        return new CssBox(root, new HtmlTag("span", false, null), BaseUrl)
        {
            Display = CssConstants.Inline,
            FontSize = "16px",
        };
    }

    private static CssBoxImage ImageBox(string float_)
    {
        var (_, image) = PageWithAnImage(float_, "64px", "64px");
        return image;
    }

    /// <summary>root → html → body → <c>&lt;img&gt;</c>, which is the shape a real page reaches
    /// layout as.</summary>
    private static (CssBox Root, CssBoxImage Image) PageWithAnImage(string float_, string? width, string? height)
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

        var image = new CssBoxImage(body, new HtmlTag("img", true, new Dictionary<string, string> { ["src"] = "i.png" }), BaseUrl)
        {
            Display = CssConstants.Inline,
            Float = float_,
            FontSize = "16px",
        };
        if (width != null)
            image.Width = width;
        if (height != null)
            image.Height = height;

        return (root, image);
    }

    private static CssBox Block(CssBox parent) => new(parent, null, BaseUrl)
    {
        Location = PointF.Empty,
        Size = new SizeF(PageWidth, 0),
        Display = CssConstants.Block,
        FontSize = "16px",
    };

    /// <summary>Loads one <see cref="ImageWidth"/>×<see cref="ImageHeight"/> image, synchronously.
    /// </summary>
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
