using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.Layout.Engine;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Covers the Phase 5 native dialog/backdrop top-layer projection: the bridge stamps a
/// <c>data-broiler-top-layer</c> order marker on top-layer boxes (open modal dialogs, open
/// popovers, synthesized <c>::backdrop</c>s), and <see cref="FragmentTreeBuilder"/> projects
/// it onto <see cref="Fragment.TopLayerOrder"/> so the renderer's native top-layer paint pass
/// (<c>PaintWalker.PaintTopLayer</c>) can lift those boxes above every ordinary stacking
/// context. An unmarked box stays <c>null</c> (ordinary stacking); a blank/garbage marker is
/// ignored, so a stray attribute can never accidentally promote a box.
/// </summary>
public sealed class TopLayerFragmentProjectionTests
{
    private static readonly Uri BaseUrl = new("file:///top-layer.html");

    private static CssBox Box(CssBox? parent, PointF location, SizeF size,
        IReadOnlyDictionary<string, string>? attributes = null, string tagName = "div")
    {
        var tag = new HtmlTag(tagName, false, attributes);
        var b = new CssBox(parent!, tag, BaseUrl) { Location = location, Size = size, Display = "block" };
        if (parent == null)
            b.LayoutEnvironment = new FakeLayoutEnvironment();
        return b;
    }

    private static Fragment FirstChild(Fragment root) => root.Children[0];

    [Fact(Timeout = 600000)]
    public void UnmarkedBox_HasNoTopLayerOrder()
    {
        var root = Box(null, new PointF(0, 0), new SizeF(300, 300));
        _ = Box(root, new PointF(10, 10), new SizeF(50, 50));

        var frag = FragmentTreeBuilder.Build(root);

        Assert.Null(frag.TopLayerOrder);
        Assert.Null(FirstChild(frag).TopLayerOrder);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("-2", -2)]
    [InlineData("2000000000", 2_000_000_000)]
    public void MarkedBox_ProjectsOrderToFragment(string marker, int expected)
    {
        var root = Box(null, new PointF(0, 0), new SizeF(300, 300));
        _ = Box(root, new PointF(10, 10), new SizeF(50, 50),
            new Dictionary<string, string> { ["data-broiler-top-layer"] = marker }, tagName: "dialog");

        var frag = FragmentTreeBuilder.Build(root);

        Assert.Equal(expected, FirstChild(frag).TopLayerOrder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    public void BlankOrGarbageMarker_IsIgnored(string marker)
    {
        var root = Box(null, new PointF(0, 0), new SizeF(300, 300));
        _ = Box(root, new PointF(10, 10), new SizeF(50, 50),
            new Dictionary<string, string> { ["data-broiler-top-layer"] = marker }, tagName: "dialog");

        var frag = FragmentTreeBuilder.Build(root);

        Assert.Null(FirstChild(frag).TopLayerOrder);
    }

    [Fact(Timeout = 600000)]
    public void OrderPersistsIndependentOfStackLevel()
    {
        // A marked box with no z-index still carries its top-layer order; the two channels are
        // independent (StackLevel stays the z-index default, TopLayerOrder drives the top layer).
        var root = Box(null, new PointF(0, 0), new SizeF(300, 300));
        _ = Box(root, new PointF(10, 10), new SizeF(50, 50),
            new Dictionary<string, string> { ["data-broiler-top-layer"] = "5" }, tagName: "dialog");

        var child = FirstChild(FragmentTreeBuilder.Build(root));

        Assert.Equal(5, child.TopLayerOrder);
        Assert.Equal(0, child.StackLevel);
    }

    // Minimal ILayoutEnvironment: supplies a fixed font so ComputedStyleBuilder.FromBox can
    // resolve em-based used values, and benign defaults elsewhere. These synthetic boxes carry
    // no text or replaced content, so the measurement/image/colour members are never exercised.
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
