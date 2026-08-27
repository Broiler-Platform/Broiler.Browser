using System.Drawing;
using System.Linq;
using Broiler.Graphics;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Covers the element structure <see cref="SvgRenderer"/>'s per-shape regexes cannot see —
/// <see cref="SvgStructure"/> — and the two things that read it: presentation-attribute
/// inheritance and the paint servers a <c>fill="url(#id)"</c> resolves to.
/// </summary>
/// <remarks>
/// The family that drove it is <c>conformance-checkers/html-svg/pservers-*</c> and
/// <c>struct-symbol-01-b</c> (issue #1658). Each was a distinct failure of the same blind spot: a
/// regex over the whole markup draws a <c>&lt;rect&gt;</c> wherever it sits, so the ones inside
/// <c>&lt;defs&gt;</c>, <c>&lt;symbol&gt;</c> and <c>&lt;pattern&gt;</c> — which SVG renders only
/// through a reference — were painted where they were declared, and a <c>&lt;g&gt;</c>'s
/// <c>font-size</c> or <c>transform</c> reached nothing inside it.
/// </remarks>
public sealed class SvgStructureAndPaintTests
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

    private static List<DrawSvgRectItem> Rects(string body) =>
        [.. Render(body).OfType<DrawSvgRectItem>()];

    // ── Non-rendering containers ──────────────────────────────────────────

    [Theory]
    [InlineData("defs")]
    [InlineData("symbol")]
    [InlineData("pattern")]
    [InlineData("mask")]
    [InlineData("clipPath")]
    public void Shape_Inside_A_NonRendering_Container_Is_Not_Painted(string container)
    {
        var rects = Rects($"<{container} id='c'><rect width='10' height='10' fill='red'/></{container}>");
        Assert.Empty(rects);
    }

    [Fact(Timeout = 600000)]
    public void Shape_After_A_NonRendering_Container_Is_Still_Painted()
    {
        // The container's suppression has to end at its end tag. Tracking it by name alone made a
        // nested child increment the depth without a matching decrement, which suppressed the
        // whole rest of the document — every conformance-checkers page starts with a <defs>.
        var rects = Rects(
            "<defs><g><rect width='10' height='10' fill='red'/></g></defs>"
            + "<rect width='20' height='20' fill='blue'/>");

        var painted = Assert.Single(rects);
        Assert.Equal(BColor.Blue, painted.Fill);
    }

    [Fact(Timeout = 600000)]
    public void Commented_Out_Markup_Is_Not_Painted()
    {
        // Most of the SVG 1.1 suite carries a commented-out <g id="draft-watermark">.
        Assert.Empty(Rects("<!-- <rect width='10' height='10' fill='red'/> -->"));
    }

    [Fact(Timeout = 600000)]
    public void Display_None_Suppresses_The_Element_And_Its_Children()
    {
        Assert.Empty(Rects("<g display='none'><rect width='10' height='10' fill='red'/></g>"));
    }

    // ── Inherited presentation attributes ─────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Group_Fill_Reaches_A_Child_That_Declares_None()
    {
        var rect = Assert.Single(Rects("<g fill='blue'><rect width='10' height='10'/></g>"));
        Assert.Equal(BColor.Blue, rect.Fill);
    }

    [Fact(Timeout = 600000)]
    public void A_Child_Fill_Wins_Over_The_Group_It_Inherits_From()
    {
        var rect = Assert.Single(Rects("<g fill='blue'><rect width='10' height='10' fill='red'/></g>"));
        Assert.Equal(BColor.Red, rect.Fill);
    }

    // ── transform ─────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Group_Transform_Moves_Its_Children()
    {
        var rect = Assert.Single(Rects(
            "<g transform='translate(40 60)'><rect width='10' height='10' fill='red'/></g>"));

        Assert.Equal(40, rect.X, 3);
        Assert.Equal(60, rect.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Rotated_Transform_Turns_A_Rect_Into_A_Polygon()
    {
        // Not a TransformItem: the raster canvas takes only translations and uniform scales, and a
        // layer it cannot take routes the drawing to a compat backend that renders nothing.
        var items = Render("<g transform='rotate(45)'><rect width='10' height='10' fill='red'/></g>");

        Assert.Empty(items.OfType<DrawSvgRectItem>());
        var polygon = Assert.Single(items.OfType<DrawSvgPolygonItem>());
        Assert.Equal(4, polygon.Points.Count);
        Assert.Equal(BColor.Red, polygon.Fill);
    }

    // ── Paint servers ─────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void An_Unresolvable_Paint_Reference_Takes_Its_Fallback_Colour()
    {
        // It used to fall through the colour parser to the caller's default — solid black.
        var rect = Assert.Single(Rects("<rect width='10' height='10' fill='url(#missing) lime'/>"));
        Assert.Equal(BColor.FromName("lime"), rect.Fill);
    }

    [Fact(Timeout = 600000)]
    public void An_Unresolvable_Paint_Reference_With_No_Fallback_Paints_Nothing()
    {
        var rect = Assert.Single(Rects("<rect width='10' height='10' fill='url(#missing)'/>"));
        Assert.True(rect.Fill.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void A_Zero_Sized_Pattern_Falls_Back_Rather_Than_Painting()
    {
        var rect = Assert.Single(Rects(
            "<defs><pattern id='p'><rect width='100%' height='100%' fill='red'/></pattern></defs>"
            + "<rect width='10' height='10' fill='url(#p) lime'/>"));

        Assert.Equal(BColor.FromName("lime"), rect.Fill);
    }

    [Fact(Timeout = 600000)]
    public void A_UserSpaceOnUse_Pattern_Tiles_Across_The_Shape()
    {
        var items = Render(
            "<defs><pattern id='p' x='0' y='0' width='10' height='10' patternUnits='userSpaceOnUse'>"
            + "<rect width='5' height='5' fill='blue'/></pattern></defs>"
            + "<rect width='40' height='20' fill='url(#p)'/>");

        // 4 columns × 2 rows of the pattern's single child, clipped to the shape.
        var tiles = items.OfType<DrawSvgRectItem>().Where(r => r.Fill == BColor.Blue).ToList();
        Assert.Equal(8, tiles.Count);
        Assert.Single(items.OfType<ClipItem>());
        Assert.Single(items.OfType<RestoreItem>());

        // The referencing rect keeps no fill of its own — the tiles are the fill.
        var host = items.OfType<DrawSvgRectItem>().Single(r => r.Width == 40);
        Assert.True(host.Fill.IsEmpty);
    }

    [Fact(Timeout = 600000)]
    public void A_Pattern_Inherits_The_One_Its_Href_Points_At()
    {
        var items = Render(
            "<defs>"
            + "<pattern id='a' x='0' y='0' width='10' height='10' patternUnits='userSpaceOnUse'>"
            + "<rect width='5' height='5' fill='blue'/></pattern>"
            + "<pattern id='b' xlink:href='#a' width='10' height='10'/>"
            + "</defs>"
            + "<rect width='20' height='10' fill='url(#b)'/>");

        Assert.Equal(2, items.OfType<DrawSvgRectItem>().Count(r => r.Fill == BColor.Blue));
    }

    // ── use / symbol ──────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Use_Renders_The_Symbol_It_References_Into_Its_Own_Viewport()
    {
        var items = Render(
            "<symbol id='s' viewBox='0 0 10 10' preserveAspectRatio='none'>"
            + "<rect width='10' height='10' fill='red'/></symbol>"
            + "<use x='20' y='30' width='40' height='20' xlink:href='#s'/>");

        var rect = Assert.Single(items.OfType<DrawSvgRectItem>());
        Assert.Equal(BColor.Red, rect.Fill);
        // Coordinates are local to the item's Bounds — here the viewport the <use> established.
        Assert.Equal(20, rect.Bounds.X + rect.X, 3);
        Assert.Equal(30, rect.Bounds.Y + rect.Y, 3);
        // preserveAspectRatio="none" stretches each axis independently.
        Assert.Equal(40, rect.Width, 3);
        Assert.Equal(20, rect.Height, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Use_Pointing_At_Nothing_Renders_Nothing()
    {
        Assert.Empty(Render("<use x='10' y='10' width='10' height='10' xlink:href='#missing'/>"));
    }

    [Fact(Timeout = 600000)]
    public void A_Pattern_Fill_Moves_With_The_Transform_On_The_Shape()
    {
        // The tiles are separate display items from the shape they fill, so the shape's transform
        // has to reach them too — otherwise the fill paints where the shape would have been.
        var items = Render(
            "<defs><pattern id='p' x='0' y='0' width='10' height='10' patternUnits='userSpaceOnUse'>"
            + "<rect width='10' height='10' fill='blue'/></pattern></defs>"
            + "<g transform='translate(40 60)'><rect width='10' height='10' fill='url(#p)'/></g>");

        var tile = Assert.Single(items.OfType<DrawSvgRectItem>().Where(r => r.Fill == BColor.Blue));
        Assert.Equal(40, tile.Bounds.X + tile.X, 3);
        Assert.Equal(60, tile.Bounds.Y + tile.Y, 3);

        var clip = Assert.Single(items.OfType<ClipItem>());
        Assert.Equal(40, clip.ClipRect.X, 3);
        Assert.Equal(60, clip.ClipRect.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void A_Use_Clones_A_Non_Viewport_Target_At_Its_Own_Offset()
    {
        // Anything but a <symbol>/<svg> is a translated deep clone: width/height do not apply, and
        // a target that is itself rendered keeps painting where it stands as well.
        var items = Render(
            "<rect id='r' x='0' y='0' width='10' height='10' fill='red'/>"
            + "<use x='40' y='60' xlink:href='#r'/>");

        var painted = items.OfType<DrawSvgRectItem>().Where(r => r.Fill == BColor.Red).ToList();
        Assert.Equal(2, painted.Count);
        Assert.Contains(painted, r => Math.Abs(r.Bounds.X + r.X) < 0.001 && Math.Abs(r.Bounds.Y + r.Y) < 0.001);
        Assert.Contains(painted, r => Math.Abs(r.Bounds.X + r.X - 40) < 0.001 && Math.Abs(r.Bounds.Y + r.Y - 60) < 0.001);
    }
}
