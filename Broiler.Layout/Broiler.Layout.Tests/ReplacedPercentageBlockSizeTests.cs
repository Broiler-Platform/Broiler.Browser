using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS2.1 §10.5 and CSS Sizing 3 §5.2.1 for a replaced element whose block size is a percentage:
/// the percentage resolves against the nearest ancestor that states a definite block size, and the
/// ratio carries it to the inline axis — where it is also what the box's own shrink-to-fit ancestors
/// have to measure.
/// </summary>
/// <remarks>
/// <para>
/// Neither half worked. The image measurement resolved a percentage <em>width</em> and rejected a
/// percentage <em>height</em>, so <c>img { height: 100% }</c> kept its bitmap's height and, through
/// the ratio, its bitmap's width. And a replaced box that carries no word at all — a
/// <c>&lt;canvas&gt;</c>, a <c>&lt;video&gt;</c>, an <c>&lt;svg&gt;</c> — contributed nothing to the
/// intrinsic-width walk, which measures a box by walking its contents and found none in it. So a
/// <c>float</c> around either came out the wrong width: the bitmap's in the first case and zero in
/// the second.
/// </para>
/// <para>
/// The numbers are Chromium's on the same markup; css-sizing/intrinsic-percent-replaced states the
/// rule and 40 of its 45 tests turned on it.
/// </para>
/// </remarks>
public sealed class ReplacedPercentageBlockSizeTests
{
    private static readonly Uri BaseUrl = new("file:///replaced.html");

    private const double NaturalWidth = 200;
    private const double NaturalHeight = 200;
    private const float PageWidth = 400;

    /// <summary>
    /// The shape css-sizing/intrinsic-percent-replaced-021 measures: the image resolves its
    /// <c>height: 100%</c> against the float's stated 100px and squares up to 100 wide.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Image_Resolves_A_Percentage_Height_Against_A_Definite_Ancestor()
    {
        var (root, container, image) = FloatWithAReplacedChild(
            containerHeight: "100px", useCanvas: false);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(100, image.Words[0].Height, 3);
        Assert.Equal(100, image.Words[0].Width, 3);
        Assert.Equal(100, container.Size.Width, 3);
    }

    /// <summary>
    /// The same with a <c>&lt;canvas&gt;</c>, which sizes itself correctly through the replaced
    /// rules but carries no word — so it was the shrink-to-fit float around it that collapsed, to
    /// nothing at all (css-sizing/intrinsic-percent-replaced-001).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Wordless_Replaced_Box_Contributes_Its_Intrinsic_Width()
    {
        var (root, container, _) = FloatWithAReplacedChild(containerHeight: "100px", useCanvas: true);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(100, container.Size.Width, 3);
    }

    /// <summary>Nothing to resolve against: the ancestor's own height is content-based, so the
    /// percentage computes to <c>auto</c> and the image keeps its natural size.</summary>
    [Fact(Timeout = 600000)]
    public void An_Indefinite_Ancestor_Leaves_The_Image_At_Its_Natural_Size()
    {
        var (root, _, image) = FloatWithAReplacedChild(containerHeight: null, useCanvas: false);

        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(NaturalHeight, image.Words[0].Height, 3);
        Assert.Equal(NaturalWidth, image.Words[0].Width, 3);
    }

    /// <summary>
    /// A grid item's block size comes from its track, and its descendants' percentages resolve
    /// against that grid area — which Broiler does not model as a containing block. Resolving past
    /// the grid to the next ancestor that states a height gives a different, larger number
    /// (css-grid/replaced-element-percentage-height-in-grid-nested-in-flex-001 asks for the row's
    /// 100 and would get the grid's 200), so the percentage is declined there and the image keeps
    /// its natural size.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Basis_Beyond_A_Grid_Area_Is_Declined()
    {
        var environment = new StubLayoutEnvironment();
        var root = Root(environment);
        var body = Block(Block(root));

        var outer = new CssBox(body, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = CssConstants.Block,
            FontSize = "16px",
            Height = "200px",
        };
        var grid = new CssBox(outer, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = "grid",
            FontSize = "16px",
            Height = "100%",
        };
        var image = Image(grid, height: "100%");

        root.PerformLayout(environment);

        Assert.Equal(NaturalHeight, image.Words[0].Height, 3);
    }

    // ───────────────────────────────── the shapes ─────────────────────────────────

    /// <summary>root → html → body → <c>float</c> → replaced child with <c>height: 100%</c>.</summary>
    private static (CssBox Root, CssBox Container, CssBox Replaced) FloatWithAReplacedChild(
        string? containerHeight, bool useCanvas)
    {
        var environment = new StubLayoutEnvironment();
        var root = Root(environment);
        var body = Block(Block(root));

        var container = new CssBox(body, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = CssConstants.Block,
            Float = CssConstants.Left,
            FontSize = "16px",
        };

        if (containerHeight != null)
            container.Height = containerHeight;

        CssBox replaced = useCanvas
            ? new CssBox(container, new HtmlTag("canvas", false, null), BaseUrl)
            {
                Display = CssConstants.Block,
                FontSize = "16px",
                Height = "100%",
                IntrinsicReplacedSize = new SizeF((float)NaturalWidth, (float)NaturalHeight),
            }
            : Image(container, height: "100%");

        return (root, container, replaced);
    }

    private static CssBoxImage Image(CssBox parent, string height) =>
        new(parent, new HtmlTag("img", true, new Dictionary<string, string> { ["src"] = "i.png" }), BaseUrl)
        {
            Display = CssConstants.Inline,
            FontSize = "16px",
            Height = height,
        };

    private static CssBox Root(StubLayoutEnvironment environment) => new(null, null, BaseUrl)
    {
        Location = PointF.Empty,
        Size = new SizeF(PageWidth, PageWidth),
        Display = CssConstants.Block,
        FontSize = "16px",
        LayoutEnvironment = environment,
    };

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
        public ImageIntrinsics GetImageIntrinsics(object imageHandle) => new(NaturalWidth, NaturalHeight, true);
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
