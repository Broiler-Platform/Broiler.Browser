using System.Drawing;
using System.Linq;
using Broiler.Graphics;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The four gaps behind the <c>conformance-checkers/html-svg</c> entries of issue #1661:
/// <c>&lt;path&gt;</c> was never drawn at all, the shape passes painted out of document order, a
/// nested <c>&lt;svg&gt;</c> established no viewport, and a CSS system colour reached the SVG paint
/// parser as an unknown name and fell through to black.
/// </summary>
public sealed class SvgPathAndViewportTests
{
    private static readonly RectangleF Bounds = new(0, 0, 100, 100);

    private static string Doc(string body) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'>{body}</svg>";

    private static List<DisplayItem> Render(string body)
    {
        SvgFilterTable.Reset();
        SvgClipPathTable.Reset();
        return SvgRenderer.RenderSvgContent(Doc(body), Bounds);
    }

    // ── <path> ────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Path_Is_Painted()
    {
        // Nothing drew <path> before: the only pass over it harvested start points for <textPath>.
        var polygon = Assert.Single(
            Render("<path d='M10,10 L90,10 L90,90 Z' fill='red'/>").OfType<DrawSvgPolygonItem>());

        Assert.Equal(BColor.Red, polygon.Fill);
        Assert.Contains(polygon.Points, p => p.X == 10 && p.Y == 10);
        Assert.Contains(polygon.Points, p => p.X == 90 && p.Y == 90);
    }

    [Theory]
    [InlineData("M10,10 H90 V90 Z")]          // absolute shorthand
    [InlineData("m10,10 h80 v80 z")]          // relative shorthand
    [InlineData("M10 10L90 10L90 90Z")]       // no separators before a command
    [InlineData("M10,10L90,10 90,90Z")]       // an implicit repeat of the last command
    public void Equivalent_Path_Spellings_Reach_The_Same_Corners(string d)
    {
        var polygon = Assert.Single(
            Render($"<path d='{d}' fill='red'/>").OfType<DrawSvgPolygonItem>());

        Assert.Contains(polygon.Points, p => p.X == 10 && p.Y == 10);
        Assert.Contains(polygon.Points, p => p.X == 90 && p.Y == 10);
        Assert.Contains(polygon.Points, p => p.X == 90 && p.Y == 90);
    }

    [Fact(Timeout = 600000)]
    public void A_Curve_Is_Flattened_Into_Chords_That_Stay_Inside_Its_Hull()
    {
        var polygon = Assert.Single(
            Render("<path d='M0,0 C0,100 100,100 100,0' fill='red'/>").OfType<DrawSvgPolygonItem>());

        Assert.True(polygon.Points.Count > 8, "a cubic must be subdivided, not drawn as one chord");
        Assert.All(polygon.Points, p =>
        {
            Assert.InRange(p.X, 0, 100);
            Assert.InRange(p.Y, 0, 75);
        });
    }

    [Fact(Timeout = 600000)]
    public void An_Arc_Command_Reaches_Its_Endpoint()
    {
        var polygon = Assert.Single(
            Render("<path d='M10,50 A40,40 0 0 1 90,50' fill='red'/>").OfType<DrawSvgPolygonItem>());

        var last = polygon.Points[^1];
        Assert.Equal(90, last.X, 1);
        Assert.Equal(50, last.Y, 1);
    }

    [Fact(Timeout = 600000)]
    public void An_Unclosed_Subpath_Strokes_As_A_Polyline_And_A_Closed_One_As_A_Polygon()
    {
        // The difference is whether the stroke runs all the way round: only Z asks for that.
        var open = Render("<path d='M10,10 L90,10 L90,90' fill='none' stroke='red'/>");
        Assert.Single(open.OfType<DrawSvgPolylineItem>());
        Assert.Empty(open.OfType<DrawSvgPolygonItem>());

        var closed = Render("<path d='M10,10 L90,10 L90,90 Z' fill='none' stroke='red'/>");
        Assert.Single(closed.OfType<DrawSvgPolygonItem>());
    }

    [Fact(Timeout = 600000)]
    public void A_Paths_SubPaths_Fill_As_One_Shape_So_An_Inner_Ring_Can_Be_A_Hole()
    {
        // Filling each subpath as its own opaque polygon painted over the hole; the rasterizer's
        // fill is an even-odd crossing test, so the two rings must reach it as one ring.
        var fills = Render(
                "<path d='M0,0 H100 V100 H0 Z M25,25 H75 V75 H25 Z' fill='red'/>")
            .OfType<DrawSvgPolygonItem>()
            .Where(p => !p.Fill.IsEmpty)
            .ToList();

        Assert.Single(fills);
        Assert.Contains(fills[0].Points, p => p.X == 25 && p.Y == 25);
        Assert.Contains(fills[0].Points, p => p.X == 100 && p.Y == 100);
    }

    [Fact(Timeout = 600000)]
    public void An_Unparseable_Path_Draws_Nothing()
    {
        Assert.Empty(Render("<path d='nonsense' fill='red'/>").OfType<DrawSvgPolygonItem>());
        Assert.Empty(Render("<path fill='red'/>").OfType<DrawSvgPolygonItem>());
    }

    // ── Document order ────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void Shapes_Paint_In_Document_Order_Across_Element_Types()
    {
        // One regex per shape type, each sweeping the whole markup, emitted every rect before every
        // circle before every text — so a later sibling could not cover an earlier one.
        var items = Render(
            "<circle cx='10' cy='10' r='5' fill='red'/>"
            + "<rect width='10' height='10' fill='lime'/>"
            + "<circle cx='20' cy='20' r='5' fill='blue'/>");

        Assert.Collection(
            items,
            item => Assert.Equal(BColor.Red, Assert.IsType<DrawSvgEllipseItem>(item).Fill),
            item => Assert.Equal(BColor.FromName("lime"), Assert.IsType<DrawSvgRectItem>(item).Fill),
            item => Assert.Equal(BColor.Blue, Assert.IsType<DrawSvgEllipseItem>(item).Fill));
    }

    // ── Nested <svg> viewports ────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Nested_Svg_Places_Its_Content_In_Its_Own_Viewport()
    {
        // The child is 100×100 in a viewport of its own, which sits at 50,50 and is 50 wide: it
        // must land there at half scale, not at the root's origin and scale.
        var rect = Assert.Single(Render(
                "<svg x='50' y='50' width='50' height='50' viewBox='0 0 100 100'>"
                + "<rect width='100' height='100' fill='red'/></svg>")
            .OfType<DrawSvgRectItem>());

        // A display item's X/Y are relative to the Bounds it carries, which for the nested content
        // is the viewport rather than the root box.
        Assert.Equal(50, rect.Bounds.X + rect.X, 3);
        Assert.Equal(50, rect.Bounds.Y + rect.Y, 3);
        Assert.Equal(50, rect.Width, 3);
        Assert.Equal(50, rect.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Nested_Svgs_Content_Is_Not_Also_Painted_By_The_Outer_Pass()
    {
        var rects = Render(
                "<svg x='0' y='0' width='50' height='50'><rect width='10' height='10' fill='red'/></svg>")
            .OfType<DrawSvgRectItem>()
            .ToList();

        Assert.Single(rects);
    }

    [Fact(Timeout = 600000)]
    public void A_Nested_Svg_Keeps_Inheriting_Across_The_Viewport_Boundary()
    {
        var rect = Assert.Single(Render(
                "<g fill='lime'><svg x='0' y='0' width='100' height='100'>"
                + "<rect width='10' height='10'/></svg></g>")
            .OfType<DrawSvgRectItem>());

        Assert.Equal(BColor.FromName("lime"), rect.Fill);
    }

    // ── clip-path on a shape ──────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Shapes_ClipPath_Reference_Narrows_What_It_Paints()
    {
        var items = Render(
            "<clipPath id='c'><rect x='0' y='0' width='50' height='50'/></clipPath>"
            + "<rect width='100' height='100' fill='red' clip-path='url(#c)'/>");

        var clip = Assert.Single(items.OfType<ClipItem>());
        Assert.Equal(new RectangleF(0, 0, 50, 50), clip.ClipRect);
        Assert.Single(items.OfType<RestoreItem>());
    }

    // ── vector-effect ─────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_NonScaling_Stroke_Does_Not_Take_The_View_Box_Scale()
    {
        // The view box maps 10 user units onto 100 px, so an ordinary 2-unit stroke is 20 px wide
        // and a non-scaling one stays 2.
        string doc = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 10 10'>"
            + "<rect width='5' height='5' fill='none' stroke='red' stroke-width='2'{0}/></svg>";

        var scaling = SvgRenderer
            .RenderSvgContent(string.Format(doc, string.Empty), Bounds)
            .OfType<DrawSvgRectItem>()
            .Single();
        var nonScaling = SvgRenderer
            .RenderSvgContent(string.Format(doc, " vector-effect='non-scaling-stroke'"), Bounds)
            .OfType<DrawSvgRectItem>()
            .Single();

        Assert.Equal(20, scaling.StrokeWidth, 3);
        Assert.Equal(2, nonScaling.StrokeWidth, 3);
    }

    // ── System colours ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Canvas", 255, 255, 255)]
    [InlineData("CanvasText", 0, 0, 0)]
    [InlineData("ButtonFace", 239, 239, 239)]
    public void A_System_Colour_Is_A_Valid_Svg_Paint(string name, int r, int g, int b)
    {
        // It matched no named colour and fell through to the caller's default — black — so a page
        // painted entirely in system colours came out as one black rectangle.
        var rect = Assert.Single(
            Render($"<rect width='10' height='10' fill='{name}'/>").OfType<DrawSvgRectItem>());

        Assert.Equal(BColor.FromArgb(255, r, g, b), rect.Fill);
    }

    [Fact(Timeout = 600000)]
    public void A_Named_Colour_Still_Wins_Over_The_System_Table()
    {
        var rect = Assert.Single(
            Render("<rect width='10' height='10' fill='red'/>").OfType<DrawSvgRectItem>());

        Assert.Equal(BColor.Red, rect.Fill);
    }
}
