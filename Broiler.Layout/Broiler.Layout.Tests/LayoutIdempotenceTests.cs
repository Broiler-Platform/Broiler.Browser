using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Laying the same box tree out twice must produce what laying it out once produced.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in this engine used to do that: every relayout disposed the render tree and rebuilt it,
/// so a box was laid out once and thrown away. Multithreading item #14 lets a rebuild be
/// <em>skipped</em>, which turns "lay the same tree out again" into a path the renderer takes — and
/// the first time it did, the corpus's flex/grid page came out two pixels shorter the second time.
/// </para>
/// <para>
/// The cause was pass-to-pass state, not flex and not grid: <c>CollapsedMarginTop</c> records what
/// the previous margin collapse decided, and a first in-flow child's top margin propagates by
/// shifting its parent down only when the child's margin exceeds what the parent has already
/// absorbed. Read from the previous pass, that comparison fails and the shift does not happen. The
/// fix is a reset at the top of each box's own layout (<c>CssBox.ResetCollapsedMarginState</c>);
/// these cases are what keeps it.
/// </para>
/// <para>
/// The whole-page version of this is <c>--relayout-parity</c>, which renders every corpus page after
/// every mutation with the elision on and off and compares the images. This file is the unit-level
/// half: it names the geometry that moved, so a regression says <em>what</em> broke rather than only
/// that some page differs.
/// </para>
/// </remarks>
public sealed class LayoutIdempotenceTests
{
    private static readonly Uri BaseUrl = new("file:///idempotence.html");

    [Fact(Timeout = 600000)]
    public void A_first_childs_top_margin_propagates_the_same_way_on_a_second_layout()
    {
        var (root, child) = TreeWithFirstChildMarginTop("8px");

        root.PerformLayout(root.LayoutEnvironment);
        var firstPass = (Child: child.Location.Y, Bottom: root.ActualBottom);

        root.PerformLayout(root.LayoutEnvironment);
        var secondPass = (Child: child.Location.Y, Bottom: root.ActualBottom);

        Assert.Equal(firstPass.Child, secondPass.Child, 3);
        Assert.Equal(firstPass.Bottom, secondPass.Bottom, 3);
    }

    [Fact(Timeout = 600000)]
    public void The_margin_is_still_applied_rather_than_dropped_from_both_passes()
    {
        // The failure this guards against is the tempting fix: clearing the state so both passes
        // agree on the *wrong* answer would satisfy the case above and lay every page out short.
        var (withMargin, marginedChild) = TreeWithFirstChildMarginTop("8px");
        var (without, plainChild) = TreeWithFirstChildMarginTop("0");

        withMargin.PerformLayout(withMargin.LayoutEnvironment);
        without.PerformLayout(without.LayoutEnvironment);

        Assert.True(
            marginedChild.Location.Y > plainChild.Location.Y,
            $"the 8px top margin should still move the child ({marginedChild.Location.Y} vs {plainChild.Location.Y})");
    }

    [Fact(Timeout = 600000)]
    public void Ten_layouts_do_not_drift()
    {
        // Two passes catch a value that is consumed once; a value that accumulates needs more. Both
        // are pass-to-pass state and only one of them is what was found, so both are asserted.
        var (root, child) = TreeWithFirstChildMarginTop("8px");

        root.PerformLayout(root.LayoutEnvironment);
        var expected = (Child: child.Location.Y, Bottom: root.ActualBottom);

        for (var i = 0; i < 10; i++)
        {
            root.PerformLayout(root.LayoutEnvironment);
            Assert.Equal(expected.Child, child.Location.Y, 3);
            Assert.Equal(expected.Bottom, root.ActualBottom, 3);
        }
    }

    /// <summary>
    /// A root, two wrappers and a block whose first in-flow child carries
    /// <paramref name="marginTop"/> — the shape that reaches the parent-shifting branch of
    /// <c>MarginTopCollapse</c>, which needs a grandparent above the collapsing pair.
    /// </summary>
    private static (CssBox Root, CssBox Child) TreeWithFirstChildMarginTop(string marginTop)
    {
        var environment = new StubLayoutEnvironment();

        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = environment,
        };

        var html = Block(root);
        var body = Block(html);
        var child = Block(body);
        child.MarginTop = marginTop;
        child.Height = "10px";

        return (root, child);
    }

    private static CssBox Block(CssBox parent)
    {
        var box = new CssBox(parent, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
            Display = CssConstants.Block,
            FontSize = "16px",
        };
        return box;
    }

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
