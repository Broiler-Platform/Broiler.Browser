using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// HTML §4.8.4.3 <b>select an image source</b>: which <c>srcset</c> candidate an <c>&lt;img&gt;</c>
/// loads, and the pixel density that divides the decoded bitmap to give the element's natural size.
/// </summary>
/// <remarks>
/// The expectations are WPT's. <c>the-img-element/current-pixel-density/basic</c> states the
/// resulting size for each descriptor shape directly (its <c>data-expect</c> attributes), and
/// <c>sizes/parse-a-sizes-attribute-*</c> states, for two hundred spellings of the <c>sizes</c>
/// attribute, which of two images the element ends up showing — the only observable a <c>sizes</c>
/// entry has, since a source size is not a size but a divisor.
/// </remarks>
public sealed class ResponsiveImageSourceSetTests
{
    private static readonly Uri BaseUrl = new("file:///page.html");

    /// <summary>The viewport <c>vw</c>/<c>vh</c> and the media conditions resolve against.</summary>
    private const float ViewportWidth = 1000;
    private const float ViewportHeight = 800;

    private static ResponsiveImageSelection? Select(params (string Name, string Value)[] attributes)
    {
        Broiler.CSS.CssLengthParser.SetViewportSize(ViewportWidth, ViewportHeight);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in attributes)
            map[name] = value;

        var environment = new FakeLayoutEnvironment();
        var root = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(ViewportWidth, ViewportHeight),
            LayoutEnvironment = environment,
        };
        var box = new CssBox(root, new HtmlTag("img", true, map), BaseUrl)
        {
            Display = "inline",
            LayoutEnvironment = environment,
        };

        return ResponsiveImageSourceSet.Select(box);
    }

    private static double DensityOf(string srcset, string? sizes = null) =>
        Select(("srcset", srcset), ("sizes", sizes ?? string.Empty))?.Density ?? double.NaN;

    private static string? UrlOf(string srcset, string? sizes = null) =>
        Select(("srcset", srcset), ("sizes", sizes ?? string.Empty))?.Url;

    // ── no srcset ────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void An_Element_Without_A_Srcset_Is_Not_Responsive()
    {
        // Null, not "the src at 1x": the caller keeps its existing `src`/`data` fallback chain,
        // which is what an <object data> depends on.
        Assert.Null(Select(("src", "plain.png")));
    }

    // ── density descriptors ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a.png 1x", 1)]
    [InlineData("a.png 1.6x", 1.6)]
    [InlineData("a.png 2x", 2)]
    [InlineData("a.png 10000x", 10000)]
    public void A_Density_Descriptor_Is_The_Density(string srcset, double expected)
    {
        Assert.Equal(expected, DensityOf(srcset), 6);
    }

    // HTML's "rules for parsing floating-point number values" have no upper bound; a magnitude past
    // double's range converges to an infinity, and the element then has a zero natural size rather
    // than a broken image. WPT current-pixel-density/basic asserts exactly this shape.
    [Fact(Timeout = 600000)]
    public void An_Unrepresentably_Large_Density_Is_Infinite_Rather_Than_A_Parse_Error()
    {
        Assert.Equal(double.PositiveInfinity, DensityOf("a.png 9e99999999999999999999999x"));
    }

    [Fact(Timeout = 600000)]
    public void The_Smallest_Candidate_That_Covers_The_Display_Density_Wins()
    {
        Assert.Equal("b.png", UrlOf("c.png 4x, b.png 1x, d.png 2x"));
    }

    [Fact(Timeout = 600000)]
    public void With_Every_Candidate_Below_The_Display_Density_The_Densest_Wins()
    {
        Assert.Equal("c.png", UrlOf("a.png 0.25x, c.png 0.75x, b.png 0.5x"));
    }

    // The `src` of an all-`x` srcset is a 1x candidate of its own, so a page that ships a plain
    // image plus a retina one still has something to show at 1x.
    [Fact(Timeout = 600000)]
    public void A_Src_Joins_An_All_Density_Srcset_As_A_1x_Candidate()
    {
        var selection = Select(("src", "plain.png"), ("srcset", "retina.png 2x"));

        Assert.Equal("plain.png", selection?.Url);
        Assert.Equal(1, selection!.Value.Density, 6);
    }

    // …but a srcset that states widths describes the whole set, and `src` is not part of it.
    [Fact(Timeout = 600000)]
    public void A_Src_Does_Not_Join_A_Width_Srcset()
    {
        Assert.Equal("wide.png", UrlOf2(("src", "plain.png"), ("srcset", "wide.png 500w"), ("sizes", "500px")));
    }

    private static string? UrlOf2(params (string Name, string Value)[] attributes) => Select(attributes)?.Url;

    // ── <picture> ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects for an <c>&lt;img&gt;</c> that is the last child of a <c>&lt;picture&gt;</c>, with
    /// the given <c>&lt;source&gt;</c> siblings before it (each an attribute map).
    /// </summary>
    private static ResponsiveImageSelection? SelectInPicture(
        (string Name, string Value)[] imgAttributes,
        params Dictionary<string, string>[] sources)
    {
        Broiler.CSS.CssLengthParser.SetViewportSize(ViewportWidth, ViewportHeight);

        var document = new Dom.DomDocument();
        var picture = document.CreateElement("picture");
        foreach (var source in sources)
        {
            var element = document.CreateElement("source");
            foreach (var (name, value) in source)
                element.SetAttribute(name, value);
            picture.AppendChild(element);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in imgAttributes)
            map[name] = value;

        var img = document.CreateElement("img");
        foreach (var (name, value) in map)
            img.SetAttribute(name, value);
        picture.AppendChild(img);

        var environment = new FakeLayoutEnvironment();
        var root = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(ViewportWidth, ViewportHeight),
            LayoutEnvironment = environment,
        };
        var box = new CssBox(root, new HtmlTag("img", true, map), BaseUrl)
        {
            Display = "inline",
            LayoutEnvironment = environment,
            SourceElement = img,
        };

        return ResponsiveImageSourceSet.Select(box);
    }

    private static Dictionary<string, string> Source(params (string Name, string Value)[] attributes)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in attributes)
            map[name] = value;
        return map;
    }

    [Fact(Timeout = 600000)]
    public void The_First_Matching_Source_Supplies_The_Candidate_List()
    {
        var selection = SelectInPicture(
            [("src", "fallback.png"), ("srcset", "own.png 1x")],
            Source(("srcset", "wide.png 1x"), ("media", "(min-width: 100px)")));

        Assert.Equal("wide.png", selection?.Url);
    }

    // A source whose media does not apply, whose type names a format that cannot be decoded, or
    // that states no srcset at all, is passed over — and the <img>'s own attributes are the last
    // entry in that list.
    [Theory]
    [InlineData("media", "(min-width: 99999px)")]
    [InlineData("type", "image/x-not-a-real-format")]
    public void A_Source_That_Does_Not_Apply_Is_Passed_Over(string attribute, string value)
    {
        var selection = SelectInPicture(
            [("srcset", "own.png 1x")],
            Source(("srcset", "wide.png 1x"), (attribute, value)));

        Assert.Equal("own.png", selection?.Url);
    }

    [Fact(Timeout = 600000)]
    public void A_Source_Without_A_Srcset_Is_Not_A_Source_Set()
    {
        var selection = SelectInPicture(
            [("srcset", "own.png 1x")],
            Source(("media", "(min-width: 1px)")));

        Assert.Equal("own.png", selection?.Url);
    }

    // `src` is the fallback for when no <source> matched; it is not a 1x candidate inside a set
    // that one of them supplied.
    [Fact(Timeout = 600000)]
    public void The_Imgs_Src_Does_Not_Join_A_Sources_Candidate_List()
    {
        var selection = SelectInPicture(
            [("src", "fallback.png")],
            Source(("srcset", "wide.png 2x")));

        Assert.Equal("wide.png", selection?.Url);
    }

    // ── width descriptors and the source size ────────────────────────────────────────────────

    // A `w` descriptor is not a size: divided by the CSS width of the slot it gives the density the
    // candidate would be shown at, which is what turns a 100-pixel bitmap into a 400px-wide image.
    [Theory]
    [InlineData("a.png 256w", "256px", 1)]
    [InlineData("a.png 512w", "256px", 2)]
    [InlineData("a.png 256w", "512px", 0.5)]
    [InlineData("a.png 100w", "400px", 0.25)]
    public void A_Width_Descriptor_Is_A_Density_Once_Divided_By_The_Source_Size(
        string srcset, string sizes, double expected)
    {
        Assert.Equal(expected, DensityOf(srcset, sizes), 6);
    }

    // A zero-width slot cannot show any of the candidates: the density is infinite and the natural
    // size zero, which is what the reference browser renders (`sizes="0px"` in
    // current-pixel-density/basic expects a 0×0 image, not a broken one).
    [Theory]
    [InlineData("0px")]
    [InlineData("0")]
    public void A_Zero_Source_Size_Gives_An_Infinite_Density(string sizes)
    {
        Assert.Equal(double.PositiveInfinity, DensityOf("a.png 256w", sizes));
    }

    // `sizes` exists to turn a width descriptor into a density; with no width descriptor there is
    // nothing for it to divide, so it is inert. (WPT sizes/implicit-sizes-ignores-width.)
    [Fact(Timeout = 600000)]
    public void Sizes_Is_Ignored_When_No_Candidate_States_A_Width()
    {
        Assert.Equal(2, DensityOf("a.png 2x", "400px"), 6);
    }

    // The default source size is the viewport, not the replaced element's default object size: an
    // <img> with a `w` srcset and no `sizes` is asking to be as wide as the page.
    [Fact(Timeout = 600000)]
    public void An_Absent_Sizes_Defaults_To_100vw()
    {
        Assert.Equal(500 / ViewportWidth, DensityOf("a.png 500w"), 6);
    }

    // ── parsing a sizes attribute ────────────────────────────────────────────────────────────

    /// <summary>The source size a <c>sizes</c> attribute resolves to, read back through the density
    /// it produces for a one-pixel-wide candidate.</summary>
    private static double SourceSizeOf(string sizes) => 1 / DensityOf("a.png 1w", sizes);

    [Theory]
    // A bare length ends the search wherever it sits, so the order of the two entries decides.
    [InlineData("1px, 100vw", 1)]
    [InlineData("100vw, 1px", ViewportWidth)]
    // A leading parse error is skipped and the next entry answers.
    [InlineData(", 1px", 1)]
    [InlineData("1px,", 1)]
    [InlineData("foo bar, 1px", 1)]
    // Comments are removed without leaving whitespace behind, so `1/* */px` is a number and an
    // identifier — not the length `1px` — while `/* */1px/* */` is the length.
    [InlineData("/* */1px/* */", 1)]
    [InlineData(" /**/ /**/ 1px /**/ /**/ ", 1)]
    // Math functions are the one function family a source size may be written as.
    [InlineData("calc(1px)", 1)]
    [InlineData("min(1px, 100px)", 1)]
    [InlineData("max(-100px, 1px)", 1)]
    // An unclosed block runs to the end of the input rather than being discarded.
    [InlineData("calc(1px", 1)]
    // Exponents, signs and unitless zero.
    [InlineData("0.2e1px", 2)]
    [InlineData(".4E1px", 4)]
    [InlineData("+1px", 1)]
    [InlineData("-0e-0px", 0)]
    public void A_Sizes_Attribute_Resolves_To_Its_First_Valid_Entry(string sizes, double expected)
    {
        Assert.Equal(expected, SourceSizeOf(sizes), 4);
    }

    [Theory]
    // Not a <length>: a percentage, a unitless non-zero number, a negative length, an unknown unit.
    [InlineData("0.1%")]
    [InlineData("1")]
    [InlineData("-1px")]
    [InlineData("0.1x")]
    [InlineData("0.1s")]
    // Not a math function.
    [InlineData("var(--foo)")]
    [InlineData("attr(data-foo, length, 1px)")]
    [InlineData("toggle(1px)")]
    [InlineData("inherit")]
    // A division by zero has no <length> value.
    [InlineData("calc(1px / 0)")]
    [InlineData("calc(0px / 0)")]
    // clamp() is a <source-size-value> per spec, but the CSS length parser does not evaluate it, so
    // the entry is a parse error here rather than the 1px it should be. Pinned as the known
    // deviation it is: teaching the parser clamp() would move this case to the theory above.
    [InlineData("clamp(1px, -50px, 100px)")]
    // The last component value is not the length.
    [InlineData("1px !important")]
    [InlineData("1/* */px")]
    [InlineData("foo bar")]
    [InlineData("(min-width:0) 1px foo bar")]
    // An unclosed block swallows the comma, so the whole attribute is one non-length component.
    [InlineData("((),1px")]
    // Empty, and a list of nothing.
    [InlineData("")]
    [InlineData(",")]
    public void An_Unparseable_Sizes_Attribute_Falls_Back_To_100vw(string sizes)
    {
        Assert.Equal(ViewportWidth, SourceSizeOf(sizes), 4);
    }

    // A <media-condition> is specifically not a full media query: a media type makes the entry a
    // parse error rather than a query that happens to match, so these fall through to the entry
    // after them.
    [Theory]
    [InlineData("(min-width:0) 1px", 1)]
    [InlineData("(min-width:0) 1px, 100vw", 1)]
    [InlineData("not (min-width:0) 100vw, 1px", 1)]
    [InlineData("(min-width:99999px) 100vw, 1px", 1)]
    [InlineData("all 100vw, 1px", 1)]
    [InlineData("print 100vw, 1px", 1)]
    [InlineData("not print 100vw, 1px", 1)]
    [InlineData("unknown-media-type 100vw, 1px", 1)]
    [InlineData("min-width:0 100vw, 1px", 1)]
    public void A_Media_Condition_Gates_Its_Entry(string sizes, double expected)
    {
        Assert.Equal(expected, SourceSizeOf(sizes), 4);
    }

    // ── malformed srcset ─────────────────────────────────────────────────────────────────────

    // A comma may separate candidates or be glued to the end of a URL; a candidate whose descriptor
    // list is an error is dropped and the rest of the list still applies.
    [Theory]
    [InlineData("a.png, b.png 2x", "a.png")]
    [InlineData("a.png,, b.png 2x", "a.png")]
    [InlineData("  a.png  1x  ,  b.png  2x  ", "a.png")]
    [InlineData("bad.png 0w, good.png 1x", "good.png")]
    [InlineData("bad.png 1w 2x, good.png 1x", "good.png")]
    [InlineData("bad.png 2q, good.png 1x", "good.png")]
    public void A_Malformed_Candidate_Is_Dropped_And_The_Rest_Still_Apply(string srcset, string expected)
    {
        Assert.Equal(expected, UrlOf(srcset));
    }

    [Fact(Timeout = 600000)]
    public void A_Srcset_Of_Nothing_But_Errors_Selects_Nothing()
    {
        Assert.Null(Select(("srcset", "a.png 0w, b.png -1x")));
    }

    // A candidate URL is a run of non-whitespace, so an unspaced comma is part of it and not a
    // separator — `srcset="a.png,b.png 2x"` names one candidate whose URL contains a comma. Only a
    // *trailing* comma ends a candidate, which is what makes `a.png,` a candidate with no
    // descriptor. Written down because it reads like a typo either way.
    [Fact(Timeout = 600000)]
    public void An_Unspaced_Comma_Is_Part_Of_The_Url_Rather_Than_A_Separator()
    {
        Assert.Equal("a.png,b.png", UrlOf("a.png,b.png 2x"));
    }

    // Author text with no grammar to rely on must never take the layout of the page down with it:
    // the failure mode is "this element has no candidate", the one it already has.
    [Theory]
    [InlineData("a.png 1w", "0")]
    [InlineData("a.png 1w", "(")]
    [InlineData("a.png 1w", "\\")]
    [InlineData("a.png 1w", "'")]
    [InlineData("a.png 1w", "/*")]
    [InlineData("a.png 1w", "1e")]
    [InlineData("a.png 1w", "+")]
    [InlineData("a.png", "")]
    public void A_Degenerate_Attribute_Does_Not_Throw(string srcset, string sizes)
    {
        var exception = Record.Exception(() => Select(("srcset", srcset), ("sizes", sizes)));

        Assert.Null(exception);
    }

    // ── the density reaches the natural size ─────────────────────────────────────────────────

    /// <summary>
    /// The whole point of carrying the density: a candidate's natural size is the decoded bitmap
    /// divided by it. Measured through the sizing path rather than asserted on the selection, so a
    /// density that never reached <see cref="CssLayoutEngine.MeasureImageSize"/> would fail here.
    /// </summary>
    [Theory]
    [InlineData(1, 256)]
    [InlineData(1.6, 160)]
    [InlineData(2, 128)]
    [InlineData(double.PositiveInfinity, 0)]
    public void The_Natural_Size_Is_The_Bitmap_Divided_By_The_Density(double density, double expected)
    {
        var environment = new FakeLayoutEnvironment(new ImageIntrinsics(256, 256, true, 1));
        var root = new CssBox(null, null, BaseUrl)
        {
            Display = "block",
            Size = new SizeF(400, 400),
            LayoutEnvironment = environment,
        };
        var box = new CssBox(root, new HtmlTag("img", true, null), BaseUrl)
        {
            Display = "inline",
            LayoutEnvironment = environment,
        };
        var word = new CssRectImage(box) { Image = new object(), PixelDensity = density };

        CssLayoutEngine.MeasureImageSize(environment, word);

        Assert.Equal(expected, word.Width, 3);
        Assert.Equal(expected, word.Height, 3);
    }

    // Minimal ILayoutEnvironment: a fixed 16px font, and one image's intrinsics.
    private sealed class FakeLayoutEnvironment(ImageIntrinsics intrinsics = default) : Broiler.Layout.ILayoutEnvironment
    {
        private static readonly Broiler.Graphics.ILayoutFont TheFont = new FakeFont();
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => TheFont;
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;
        public ImageIntrinsics GetImageIntrinsics(object imageHandle) => intrinsics;
        public Broiler.Graphics.BColor ParseColor(string value) => default;
        public void RequestRefresh(bool relayout) { }
        public SizeF ViewportSize => new(ViewportWidth, ViewportHeight);
        public PointF RootLocation => PointF.Empty;
        public SizeF ActualSize { get; set; }
        public bool AvoidGeometryAntialias => false;
        public SizeF PageSize => new(ViewportWidth, ViewportHeight);
        public int MarginTop => 0;
        public void ReportLayoutError(string message, Exception? exception = null) { }
        public bool AvoidAsyncImagesLoading => true;
        public bool AvoidImagesLateLoading => true;
        public ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete) => null!;
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
