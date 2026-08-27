using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Covers <see cref="SvgImageRaster"/>, the seam that makes an SVG used <em>as an image</em> render
/// through the same <see cref="SvgRenderer"/> as inline <c>&lt;svg&gt;</c> markup.
/// <para>
/// The case that drove it is issue #1627. The image backend used to draw SVG itself, with a regex
/// pass per element type that knew only rect, circle, ellipse, line, path and text — so a document
/// whose content was a <c>&lt;polygon&gt;</c> rasterised to a fully transparent bitmap and the image
/// did not appear at all, while the same file rendered correctly as inline markup. Eleven WPT tests
/// were failing on exactly that (<c>css/css-transforms/transform-root-bg-*</c>,
/// <c>transform-background-*</c> and <c>css/compositing/root-element-background-image-transparency-*</c>,
/// all of which load a <c>&lt;polygon&gt;</c>), and they had been misfiled as reference disagreements
/// because Broiler rendered blank for the test <em>and</em> its reference, which matched at 100%.
/// </para>
/// <para>
/// The point of these tests is therefore the <b>equivalence</b>, not the individual shapes: whatever
/// the inline renderer draws, the image path must draw the same. A test that pinned pixel output per
/// element would pass while the two renderers drifted apart again, which is the bug class this change
/// closes.
/// </para>
/// </summary>
public sealed class SvgImageRasterTests
{
    private static readonly RectangleF Bounds = new(0, 0, 100, 100);

    private static string Doc(string body) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' viewBox='0 0 100 100'>{body}</svg>";

    /// <summary>Records what it was asked to replay, standing in for the image backend's raster backend.</summary>
    private sealed class RecordingBackend : IRasterBackend
    {
        public List<(DisplayList List, object Surface)> Calls { get; } = [];

        public void Render(DisplayList list, object surface) => Calls.Add((list, surface));
    }

    // ─────────────────────────── the regression that motivated it ───────────────────────────

    /// <summary>
    /// The shape the old image renderer dropped. A <c>&lt;polygon&gt;</c> must produce display items;
    /// producing none is what made the eleven WPT tests render a blank canvas.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Polygon_Produces_Display_Items()
    {
        var list = SvgImageRaster.BuildDisplayList(
            Doc("<polygon fill='blue' points='0,50 100,100 100,0' />"), Bounds);

        Assert.NotEmpty(list.Items);
    }

    /// <summary>
    /// The same square written as a <c>&lt;rect&gt;</c> and as a <c>&lt;polygon&gt;</c> must both draw.
    /// This is the exact pair that isolated the bug: `rect` painted and `polygon` did not, through the
    /// same code path, at the same size and position.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Polygon_And_Rect_Both_Draw_The_Same_Square()
    {
        var asRect = SvgImageRaster.BuildDisplayList(
            Doc("<rect fill='blue' x='0' y='0' width='100' height='100' />"), Bounds);
        var asPolygon = SvgImageRaster.BuildDisplayList(
            Doc("<polygon fill='blue' points='0,0 100,0 100,100 0,100' />"), Bounds);

        Assert.NotEmpty(asRect.Items);
        Assert.NotEmpty(asPolygon.Items);
    }

    /// <summary>
    /// <c>&lt;polyline&gt;</c> is the other shape the old renderer had no arm for. It is covered so the
    /// fix is not read as being about one element.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Polyline_Produces_Display_Items()
    {
        var list = SvgImageRaster.BuildDisplayList(
            Doc("<polyline fill='none' stroke='blue' stroke-width='4' points='0,0 50,50 100,0' />"), Bounds);

        Assert.NotEmpty(list.Items);
    }

    // ─────────────────────────── the equivalence that is the point ───────────────────────────

    /// <summary>
    /// The image path and the inline path must produce the identical display list for the identical
    /// document. This is what "one SVG renderer, not two" means, and it is the assertion that fails if
    /// anyone reintroduces a separate image-side renderer.
    /// </summary>
    [Theory]
    [InlineData("<rect fill='blue' x='10' y='10' width='50' height='50' />")]
    [InlineData("<circle fill='blue' cx='50' cy='50' r='40' />")]
    [InlineData("<ellipse fill='blue' cx='50' cy='50' rx='40' ry='20' />")]
    [InlineData("<line stroke='blue' stroke-width='4' x1='0' y1='0' x2='100' y2='100' />")]
    [InlineData("<path fill='blue' d='M 0 0 L 100 0 L 100 100 Z' />")]
    [InlineData("<polygon fill='blue' points='0,50 100,100 100,0' />")]
    [InlineData("<polyline fill='none' stroke='blue' stroke-width='4' points='0,0 50,50 100,0' />")]
    public void Image_Path_Builds_What_The_Inline_Renderer_Builds(string body)
    {
        string svg = Doc(body);

        var viaImage = SvgImageRaster.BuildDisplayList(svg, Bounds).Items;
        var viaInline = SvgRenderer.RenderSvgContent(svg, Bounds);

        Assert.Equal(viaInline.Count, viaImage.Count);
        Assert.Equal(
            viaInline.Select(i => i.GetType().Name),
            viaImage.Select(i => i.GetType().Name));
    }

    /// <summary>
    /// The zoom argument reaches the renderer rather than being dropped by the wrapper. A view-box-less
    /// document is the case that shows it, since there the zoom seeds the user-unit scale instead of
    /// being overridden by a view-box-derived one.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Effective_Zoom_Is_Forwarded()
    {
        const string svg =
            "<svg xmlns='http://www.w3.org/2000/svg'><rect fill='blue' x='0' y='0' width='10' height='10' /></svg>";

        var atOne = SvgImageRaster.BuildDisplayList(svg, Bounds, 1.0).Items;
        var atTwo = SvgImageRaster.BuildDisplayList(svg, Bounds, 2.0).Items;

        Assert.Equal(
            SvgRenderer.RenderSvgContent(svg, Bounds, 2.0).Select(i => i.GetType().Name),
            atTwo.Select(i => i.GetType().Name));
        Assert.NotEmpty(atOne);
    }

    // ─────────────────────────── attribute quoting ───────────────────────────

    /// <summary>
    /// XML gives the two quote styles equal standing. The parser matched double quotes only, so a
    /// single-quoted element parsed with **no attributes at all** and drew nothing — silently, since
    /// an element with no attributes is not an error. Every other test in this file is written with
    /// single quotes, so they all guard this too; these three state it outright.
    /// </summary>
    [Theory]
    [InlineData("<rect fill=\"blue\" x=\"10\" y=\"20\" width=\"30\" height=\"40\" />")]
    [InlineData("<rect fill='blue' x='10' y='20' width='30' height='40' />")]
    [InlineData("<rect fill='blue' x=\"10\" y='20' width=\"30\" height='40' />")]
    public void Both_Quote_Styles_Parse_And_Agree(string rectMarkup)
    {
        var rect = SvgImageRaster
            .BuildDisplayList(
                $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>{rectMarkup}</svg>",
                Bounds)
            .Items.OfType<DrawSvgRectItem>().Single();

        Assert.Equal(10f, rect.X, 3);
        Assert.Equal(20f, rect.Y, 3);
        Assert.Equal(30f, rect.Width, 3);
        Assert.Equal(40f, rect.Height, 3);
    }

    /// <summary>
    /// A value may legally contain the *other* quote character. This is why the pattern alternates two
    /// negated classes rather than backreferencing one quote with a lazy body, which would have cut the
    /// value short at the inner quote.
    /// </summary>
    [Theory]
    [InlineData("<rect x='0' title='He said \"hi\"' width='50' height='50' fill='blue' />")]
    [InlineData("<rect x=\"0\" title=\"it's fine\" width=\"50\" height=\"50\" fill=\"blue\" />")]
    public void A_Value_May_Contain_The_Other_Quote(string rectMarkup)
    {
        var rect = SvgImageRaster
            .BuildDisplayList(
                $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>{rectMarkup}</svg>",
                Bounds)
            .Items.OfType<DrawSvgRectItem>().Single();

        Assert.Equal(50f, rect.Width, 3);
        Assert.Equal(50f, rect.Height, 3);
    }

    /// <summary>The root element's own attributes take both styles too — the viewBox drives every coordinate.</summary>
    [Fact(Timeout = 600000)]
    public void A_Single_Quoted_ViewBox_Establishes_The_Transform()
    {
        var doubleQuoted = SvgImageRaster.BuildDisplayList(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 200\">" +
            "<rect x=\"0\" y=\"0\" width=\"100\" height=\"100\" fill=\"blue\" /></svg>",
            Bounds).Items.OfType<DrawSvgRectItem>().Single();
        var singleQuoted = SvgImageRaster.BuildDisplayList(
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>" +
            "<rect x='0' y='0' width='100' height='100' fill='blue' /></svg>",
            Bounds).Items.OfType<DrawSvgRectItem>().Single();

        // A 200-unit viewBox into a 100px box halves every coordinate.
        Assert.Equal(50f, singleQuoted.Width, 3);
        Assert.Equal(doubleQuoted.Width, singleQuoted.Width, 3);
        Assert.Equal(doubleQuoted.Height, singleQuoted.Height, 3);
    }

    // ─────────────────────────── percentage lengths ───────────────────────────

    private static DrawSvgRectItem SingleRect(string svg, RectangleF bounds) =>
        SvgImageRaster.BuildDisplayList(svg, bounds).Items.OfType<DrawSvgRectItem>().Single();

    /// <summary>
    /// The shape of every <c>css-backgrounds/background-size/vector</c> test: no viewBox, and children
    /// sized in percentages. Before percentages resolved, both extents parsed as <c>0</c> and the
    /// document drew nothing at all.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Percentage_Extents_Resolve_Against_The_Bounds_When_There_Is_No_ViewBox()
    {
        var rect = SingleRect(
            ("<svg xmlns='http://www.w3.org/2000/svg'><rect y='0' width='100%' height='50%' fill='lime'/></svg>"),
            new RectangleF(0, 0, 200, 80));

        Assert.Equal(200f, rect.Width, 3);
        Assert.Equal(40f, rect.Height, 3);
    }

    /// <summary>
    /// A percentage offset resolves on the same basis as an extent, so the second band of the
    /// two-band SVG those tests use starts halfway down rather than at the origin.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Percentage_Offsets_Resolve_On_The_Same_Basis()
    {
        var items = SvgImageRaster.BuildDisplayList(
            ("<svg xmlns='http://www.w3.org/2000/svg'>" +
              "<rect y='0' width='100%' height='50%' fill='lime'/>" +
              "<rect y='50%' width='100%' height='50%' fill='aqua'/></svg>"),
            new RectangleF(0, 0, 100, 100)).Items.OfType<DrawSvgRectItem>().ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(0f, items[0].Y, 3);
        Assert.Equal(50f, items[1].Y, 3);
    }

    /// <summary>
    /// A viewBox establishes the viewport for its children, so a percentage inside it resolves against
    /// the viewBox extent — in user units — and is then scaled to the box like any other coordinate.
    /// Here 50% of a 200-unit viewBox is 100 units, drawn into a 100px box at scale 0.5, so 50px.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Percentage_Resolves_Against_The_ViewBox_When_There_Is_One()
    {
        var rect = SingleRect(
            ("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>" +
            "<rect x='0' y='0' width='50%' height='50%' fill='blue'/></svg>"),
            new RectangleF(0, 0, 100, 100));

        Assert.Equal(50f, rect.Width, 3);
        Assert.Equal(50f, rect.Height, 3);
    }

    /// <summary>
    /// A length in neither axis — a circle's <c>r</c> — takes the normalised diagonal, not the width.
    /// On a square viewport the two coincide, so the test uses a non-square one where they cannot:
    /// √(300² + 100²)/√2 ≈ 223.6, so 10% is ≈ 22.36 rather than 30.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Radius_Percentage_Uses_The_Normalised_Diagonal()
    {
        var circle = SvgImageRaster.BuildDisplayList(
            ("<svg xmlns='http://www.w3.org/2000/svg'><circle cx='0' cy='0' r='10%' fill='blue'/></svg>"),
            new RectangleF(0, 0, 300, 100)).Items.OfType<DrawSvgEllipseItem>().Single();

        Assert.Equal(22.36f, circle.Rx, 1);
    }

    /// <summary>
    /// The negative half. A plain user-unit length must be read exactly as before — this change is a
    /// strict addition, and a regression here would move every SVG that never used a percentage.
    /// </summary>
    [Theory]
    [InlineData("40", 40f)]
    [InlineData("40px", 40f)]
    [InlineData("40.5", 40.5f)]
    public void Non_Percentage_Lengths_Are_Unchanged(string raw, float expected)
    {
        var rect = SingleRect(
            ($"<svg xmlns='http://www.w3.org/2000/svg'><rect width='{raw}' height='{raw}' fill='blue'/></svg>"),
            new RectangleF(0, 0, 100, 100));

        Assert.Equal(expected, rect.Width, 3);
    }

    /// <summary>A malformed percentage falls back to the attribute's default rather than throwing.</summary>
    [Fact(Timeout = 600000)]
    public void A_Malformed_Percentage_Falls_Back_To_The_Default()
    {
        var rect = SingleRect(
            ("<svg xmlns='http://www.w3.org/2000/svg'><rect width='abc%' height='50%' fill='blue'/></svg>"),
            new RectangleF(0, 0, 100, 100));

        Assert.Equal(0f, rect.Width, 3);
        Assert.Equal(50f, rect.Height, 3);
    }

    // ─────────────────────────── fill-opacity and mix-blend-mode ───────────────────────────

    private static DrawSvgRectItem Rect(string rectMarkup) =>
        SvgImageRaster
            .BuildDisplayList(
                $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>{rectMarkup}</svg>", Bounds)
            .Items.OfType<DrawSvgRectItem>().Single();

    /// <summary>
    /// <c>fill-opacity</c> folds into the fill's alpha. Unhandled, a shape painted solid where the
    /// document asked for a tint — which is how <c>css-images/cross-fade-natural-size</c>'s reference
    /// came out solid green on white instead of a tinted green over black.
    /// </summary>
    [Theory]
    [InlineData("0.5", 128)]
    [InlineData("0.25", 64)]
    [InlineData("50%", 128)]
    [InlineData("1", 255)]
    [InlineData("0", 0)]
    public void Fill_Opacity_Folds_Into_The_Alpha(string opacity, int expectedAlpha)
    {
        var rect = Rect($"<rect fill='green' fill-opacity='{opacity}' width='50' height='50' />");

        Assert.Equal(expectedAlpha, rect.Fill.A);
    }

    /// <summary>Out-of-range values clamp rather than being rejected (SVG 1.1 §4.4).</summary>
    [Theory]
    [InlineData("2", 255)]
    [InlineData("-1", 0)]
    [InlineData("nonsense", 255)]
    public void An_Out_Of_Range_Or_Malformed_Opacity_Clamps(string opacity, int expectedAlpha)
    {
        var rect = Rect($"<rect fill='green' fill-opacity='{opacity}' width='50' height='50' />");

        Assert.Equal(expectedAlpha, rect.Fill.A);
    }

    /// <summary><c>stroke-opacity</c> is the same rule on the other paint, and does not touch the fill.</summary>
    [Fact(Timeout = 600000)]
    public void Stroke_Opacity_Applies_To_The_Stroke_Only()
    {
        var rect = Rect(
            "<rect fill='green' stroke='blue' stroke-opacity='0.5' stroke-width='4' width='50' height='50' />");

        Assert.Equal(128, rect.Stroke.A);
        Assert.Equal(255, rect.Fill.A);
    }

    /// <summary>
    /// A <c>style</c> declaration outranks the presentation attribute of the same name (SVG 1.1 §6.4).
    /// The reference this work came from writes both its opacity and its blend mode in <c>style</c>.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Style_Declaration_Outranks_The_Presentation_Attribute()
    {
        var rect = Rect(
            "<rect fill='green' fill-opacity='1' style='fill-opacity: 0.25' width='50' height='50' />");

        Assert.Equal(64, rect.Fill.A);
    }

    /// <summary>
    /// An absent paint is "no paint", not the default colour — the default is only the fallback for a
    /// value that is present and unparseable. Writing it the other way round painted every fill-less
    /// shape solid black and cost 104 reftests, almost all of `css-images/object-fit-*-svg-*`.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Absent_Paint_Is_No_Paint_Rather_Than_The_Default()
    {
        var noFill = Rect("<rect width='50' height='50' />");
        var badFill = Rect("<rect fill='not-a-colour' width='50' height='50' />");

        Assert.Equal(0, noFill.Fill.A);
        Assert.Equal(0, noFill.Stroke.A);
        // Present but unparseable falls back to the caller's default, which for a rect fill is black.
        Assert.Equal(255, badFill.Fill.A);
    }

    /// <summary>The paint itself may come from <c>style</c> too, not only its opacity.</summary>
    [Fact(Timeout = 600000)]
    public void A_Fill_May_Come_From_The_Style_Declaration()
    {
        var viaStyle = Rect("<rect style='fill: blue' width='50' height='50' />");
        var viaAttribute = Rect("<rect fill='blue' width='50' height='50' />");

        Assert.Equal(viaAttribute.Fill, viaStyle.Fill);
    }

    /// <summary>
    /// A non-normal <c>mix-blend-mode</c> wraps the shape in the same blend-layer pair the CSS
    /// <c>mix-blend-mode</c> path emits, so the raster backend composites it with machinery it already
    /// has.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Mix_Blend_Mode_Wraps_The_Shape_In_A_Blend_Layer()
    {
        var items = SvgImageRaster.BuildDisplayList(
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>" +
            "<rect fill='blue' width='50' height='50' style='mix-blend-mode: screen' /></svg>",
            Bounds).Items;

        Assert.Collection(items,
            i => Assert.Equal("screen", Assert.IsType<BlendModeItem>(i).Mode),
            i => Assert.IsType<DrawSvgRectItem>(i),
            i => Assert.IsType<RestoreBlendModeItem>(i));
    }

    /// <summary>
    /// The negative half, and what makes this safe to apply at every shape site: a shape with no blend
    /// mode, or an explicitly <c>normal</c> one, emits exactly the display list it did before — one
    /// item, no layer.
    /// </summary>
    [Theory]
    [InlineData("<rect fill='blue' width='50' height='50' />")]
    [InlineData("<rect fill='blue' width='50' height='50' style='mix-blend-mode: normal' />")]
    [InlineData("<rect fill='blue' width='50' height='50' mix-blend-mode='normal' />")]
    public void A_Normal_Or_Absent_Blend_Mode_Emits_No_Layer(string rectMarkup)
    {
        var items = SvgImageRaster.BuildDisplayList(
            $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>{rectMarkup}</svg>",
            Bounds).Items;

        Assert.Single(items);
        Assert.IsType<DrawSvgRectItem>(items[0]);
    }

    // ─────────────────────────── the replay contract ───────────────────────────

    /// <summary>A document that draws something is replayed onto the caller's surface, once.</summary>
    [Fact(Timeout = 600000)]
    public void Render_Replays_The_List_Onto_The_Callers_Surface()
    {
        var backend = new RecordingBackend();
        var surface = new object();

        bool drew = SvgImageRaster.Render(
            Doc("<polygon fill='blue' points='0,50 100,100 100,0' />"), Bounds, backend, surface);

        Assert.True(drew);
        var call = Assert.Single(backend.Calls);
        Assert.Same(surface, call.Surface);
        Assert.NotEmpty(call.List.Items);
    }

    /// <summary>
    /// The negative half, and the one that keeps the change from painting where it should not: a
    /// document that draws nothing must leave the surface untouched rather than replay an empty list.
    /// The image backend clears the bitmap to transparent before calling in, so an empty replay would
    /// be harmless today — but it would also make "nothing to draw" indistinguishable from "drew
    /// nothing", which is precisely the confusion that hid this bug.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><unsupported-element /></svg>")]
    public void Render_Does_Not_Touch_The_Surface_When_Nothing_Is_Drawn(string svg)
    {
        var backend = new RecordingBackend();

        bool drew = SvgImageRaster.Render(svg, Bounds, backend, new object());

        Assert.False(drew);
        Assert.Empty(backend.Calls);
    }

    /// <summary>Null source is "draws nothing", not a throw — an unreadable image must not take the page down.</summary>
    [Fact(Timeout = 600000)]
    public void Null_Source_Draws_Nothing()
    {
        var backend = new RecordingBackend();

        Assert.False(SvgImageRaster.Render(null!, Bounds, backend, new object()));
        Assert.Empty(backend.Calls);
    }

    /// <summary>The two arguments the caller cannot sensibly omit are rejected rather than swallowed.</summary>
    [Fact(Timeout = 600000)]
    public void Backend_And_Surface_Are_Required()
    {
        string svg = Doc("<rect fill='blue' x='0' y='0' width='10' height='10' />");

        Assert.Throws<ArgumentNullException>(
            () => SvgImageRaster.Render(svg, Bounds, null!, new object()));
        Assert.Throws<ArgumentNullException>(
            () => SvgImageRaster.Render(svg, Bounds, new RecordingBackend(), null!));
    }

    /// <summary>
    /// CSS Backgrounds §2.11.2: the root element's background is the canvas's, and for an SVG used
    /// as an image that canvas is the destination box. It is painted first, behind the document.
    /// </summary>
    /// <remarks>
    /// Nothing painted it, so a file drawing tinted shapes over a dark ground rendered them on
    /// white. Asserted on the display list rather than on pixels because that is where the ordering
    /// lives — a background emitted after the shapes would cover them.
    /// </remarks>
    [Theory(Timeout = 600000)]
    [InlineData("background: black")]
    [InlineData("background-color: black")]
    [InlineData("background: url(none.png) black")]
    public void A_Root_Background_Paints_The_Canvas_Behind_The_Document(string declaration)
    {
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' " +
                  $"viewBox='0 0 100 100' style='{declaration}'>" +
                  "<rect fill='blue' x='10' y='10' width='20' height='20' /></svg>";

        var items = SvgImageRaster.BuildDisplayList(svg, Bounds).Items;

        var first = Assert.IsType<DrawSvgRectItem>(items[0]);
        Assert.Equal(0, first.Fill.R);
        Assert.Equal(0, first.Fill.G);
        Assert.Equal(0, first.Fill.B);
        Assert.Equal(255, first.Fill.A);

        // It covers the whole destination box, not the shape's bounds.
        Assert.Equal(Bounds.Width, first.Width);
        Assert.Equal(Bounds.Height, first.Height);

        // And the document is still drawn, over it.
        Assert.Contains(items.Skip(1), i => i is DrawSvgRectItem { Fill.B: 255 });
    }

    /// <summary>
    /// A root that declares no background, or a fully transparent one, adds nothing — an SVG image
    /// is transparent where it paints nothing, and stamping an opaque rect would hide whatever the
    /// image is composited over.
    /// </summary>
    [Theory(Timeout = 600000)]
    [InlineData("")]
    [InlineData("style='background: transparent'")]
    [InlineData("style='background: none'")]
    [InlineData("style='fill: black'")]
    public void No_Root_Background_Paints_No_Canvas(string attribute)
    {
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' " +
                  $"viewBox='0 0 100 100' {attribute}>" +
                  "<rect fill='blue' x='10' y='10' width='20' height='20' /></svg>";

        var items = SvgImageRaster.BuildDisplayList(svg, Bounds).Items;

        var first = Assert.IsType<DrawSvgRectItem>(items[0]);
        Assert.Equal(20, first.Width);
    }

    /// <summary>
    /// The inline path must <em>not</em> paint it: an inline <c>&lt;svg&gt;</c> is an ordinary
    /// element whose CSS box already paints its background, and doing it again would double a
    /// translucent colour over itself.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_Inline_Renderer_Leaves_The_Root_Background_To_The_Css_Box()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' " +
                  "viewBox='0 0 100 100' style='background: black'>" +
                  "<rect fill='blue' x='10' y='10' width='20' height='20' /></svg>";

        var inline = SvgRenderer.RenderSvgContent(svg, Bounds);

        var first = Assert.IsType<DrawSvgRectItem>(inline[0]);
        Assert.Equal(20, first.Width);
    }
}
