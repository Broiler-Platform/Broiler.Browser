using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Covers the P5.8d.2b native <c>position: sticky</c> post-pass (flag-gated, default off):
/// pinning a sticky box to its scroll container's scrollport edges (physical <c>top</c>/
/// <c>bottom</c>/<c>left</c>/<c>right</c> insets), clamped so it never leaves its containing
/// block. The pass reads the box's post-scroll <see cref="CssBox.Bounds"/> (the scroll
/// post-pass has already shifted the container content by then), so these synthetic trees
/// place the sticky box at its post-scroll position directly and assert the resulting offset —
/// exactly the geometry <c>ComputeStickyShift</c> sees at runtime.
/// </summary>
public sealed class NativeStickyPlacementTests
{
    private static readonly Uri BaseUrl = new("file:///sticky-pass.html");

    private static CssBox Box(CssBox? parent, PointF location, SizeF size, string display = "block")
    {
        var b = new CssBox(parent!, null, BaseUrl) { Location = location, Size = size, Display = display };
        if (parent == null)
            b.LayoutEnvironment = new FakeLayoutEnvironment();
        return b;
    }

    private static void Run(CssBox root)
    {
        try
        {
            NativeAnchorPlacement.Enabled = true;
            CssBox.RunStickyPositioning(root);
        }
        finally
        {
            NativeAnchorPlacement.Enabled = false;
        }
    }

    // A scroll container (overflow:hidden) holding a tall containing block that holds the
    // sticky box. The CB is tall by default so the containing-block clamp is non-binding.
    private static CssBox Fixture(out CssBox sticky, PointF stickyLoc, SizeF stickySize,
        PointF cbLoc, SizeF cbSize, PointF scLoc = default, SizeF scSize = default)
    {
        if (scSize == default) scSize = new SizeF(200, 200);
        var root = Box(null, new PointF(0, 0), new SizeF(1000, 1000));
        var sc = Box(root, scLoc, scSize);
        sc.Overflow = "hidden";
        var cb = Box(sc, cbLoc, cbSize);
        sticky = Box(cb, stickyLoc, stickySize);
        sticky.Position = "sticky";
        return root;
    }

    [Fact(Timeout = 600000)]
    public void TopInset_PinsToScrollportTop_WhenScrolledPast()
    {
        // Scrollport 0..200. Tall CB scrolled up to (0,-350). Sticky (50×30) at (0,-50) —
        // scrolled 40px above the scrollport top; top:10 pins it to 10px below the top.
        var root = Fixture(out var sticky, new PointF(0, -50), new SizeF(50, 30),
            new PointF(0, -350), new SizeF(200, 1000));
        sticky.Top = "10px";

        Run(root);

        Assert.Equal(10, sticky.Location.Y, 3); // pinned at scrollport top + 10
        Assert.Equal(0, sticky.Location.X, 3);  // no horizontal inset → unchanged
    }

    [Fact(Timeout = 600000)]
    public void TopInset_NoPin_WhenStillBelowInsetLine()
    {
        // Sticky at (0,100) — 100px below the scrollport top, still past top:10 → no shift.
        var root = Fixture(out var sticky, new PointF(0, 100), new SizeF(50, 30),
            new PointF(0, 0), new SizeF(200, 1000));
        sticky.Top = "10px";

        Run(root);

        Assert.Equal(100, sticky.Location.Y, 3);
        Assert.Equal(0, sticky.Location.X, 3);
    }

    [Fact(Timeout = 600000)]
    public void BottomInset_PinsToScrollportBottom_WhenScrolledPast()
    {
        // Sticky (50×30) at (0,300) is below the visible scrollport; bottom:10 pins its
        // bottom edge to 10px above the scrollport bottom (200 − 10 = 190 → top 160).
        var root = Fixture(out var sticky, new PointF(0, 300), new SizeF(50, 30),
            new PointF(0, 0), new SizeF(200, 1000));
        sticky.Bottom = "10px";

        Run(root);

        Assert.Equal(160, sticky.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void LeftInset_PinsHorizontally()
    {
        // Wide CB scrolled left. Sticky (30×50) at (-50,0); left:10 pins to x=10.
        var root = Fixture(out var sticky, new PointF(-50, 0), new SizeF(30, 50),
            new PointF(0, 0), new SizeF(1000, 200));
        sticky.Left = "10px";

        Run(root);

        Assert.Equal(10, sticky.Location.X, 3);
        Assert.Equal(0, sticky.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void PercentInset_ResolvesAgainstScrollportSize()
    {
        // top:5% of the 200px scrollport = 10px → same pin as the 10px case.
        var root = Fixture(out var sticky, new PointF(0, -50), new SizeF(50, 30),
            new PointF(0, -350), new SizeF(200, 1000));
        sticky.Top = "5%";

        Run(root);

        Assert.Equal(10, sticky.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void Clamp_KeepsBoxWithinContainingBlock()
    {
        // Short CB: content box (0,-50)..(0,10), only 60 tall. Sticky at the CB top (0,-50),
        // size 50×30. The ideal top:10 pin wants +60 (to y=10), but the CB only allows the
        // box to move down until its bottom hits the CB bottom (y=10): max shift = 60−30 = 30
        // → final y = −50 + 30 = −20 (box bottom = 10 = CB bottom).
        var root = Fixture(out var sticky, new PointF(0, -50), new SizeF(50, 30),
            new PointF(0, -50), new SizeF(200, 60));
        sticky.Top = "10px";

        Run(root);

        Assert.Equal(-20, sticky.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void NoClipContainer_PinsAgainstViewport_WhenScrolledAbove()
    {
        // No clipping ancestor → the box's scroll container is the viewport (document scroll).
        // Placed at post-page-scroll y=-50 (above the viewport top); top:10 pins it to y=10.
        // The FakeLayoutEnvironment viewport is 1000×1000 at the origin.
        var root = Box(null, new PointF(0, 0), new SizeF(1000, 1000));
        var cb = Box(root, new PointF(0, -50), new SizeF(200, 1000));
        var sticky = Box(cb, new PointF(0, -50), new SizeF(50, 30));
        sticky.Position = "sticky";
        sticky.Top = "10px";

        Run(root);

        Assert.Equal(10, sticky.Location.Y, 3); // pinned 10px below the viewport top
    }

    [Fact(Timeout = 600000)]
    public void ViewportSticky_NoPin_WhenWithinViewportPastInset()
    {
        // Viewport-anchored sticky whose (post-scroll) position is comfortably inside the
        // viewport and past top:10 → no pin, stays put.
        var root = Box(null, new PointF(0, 0), new SizeF(1000, 1000));
        var cb = Box(root, new PointF(0, 0), new SizeF(200, 1000));
        var sticky = Box(cb, new PointF(0, 200), new SizeF(50, 30));
        sticky.Position = "sticky";
        sticky.Top = "10px";

        Run(root);

        Assert.Equal(200, sticky.Location.Y, 3);
    }

    [Fact(Timeout = 600000)]
    public void NonStickyBox_IsIgnored()
    {
        var root = Fixture(out var sticky, new PointF(0, -50), new SizeF(50, 30),
            new PointF(0, -350), new SizeF(200, 1000));
        sticky.Position = "relative"; // not sticky
        sticky.Top = "10px";

        Run(root);

        Assert.Equal(-50, sticky.Location.Y, 3);
    }

    [Theory]
    [InlineData("10px", 100.0, 10.0)]
    [InlineData("25%", 200.0, 50.0)]
    [InlineData("auto", 100.0, 0.0)]
    [InlineData("", 100.0, 0.0)]
    [InlineData("1em", 100.0, 0.0)] // non-px/percent → 0 (first-increment px/percent scope)
    public void ParseStickyInset_PxOrPercentAgainstScrollport(string value, double basis, double expected)
    {
        Assert.Equal(expected, CssBox.ParseStickyInset(value, basis), 3);
    }

    // Minimal layout environment for the synthetic box trees (resolves a fixed-size font so
    // Actual border/padding widths compute); the boxes carry no text/replaced content.
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
