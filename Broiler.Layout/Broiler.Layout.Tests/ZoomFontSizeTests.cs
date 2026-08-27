using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the native CSS <c>zoom</c> font-size increment: the used font size (<c>ActualFont.Size</c>) is the
/// computed (unzoomed) size scaled by <see cref="CssBoxProperties.EffectiveZoom"/>, so <c>em</c>/<c>%</c>
/// and inheritance resolve against the computed size and compound the ancestor zoom exactly once. Uses a
/// font environment that echoes the requested point size (the shared <c>FakeFont</c> reports a constant),
/// and asserts ratios so the px→pt conversion is irrelevant. Gated by <see cref="NativeZoom"/>.
/// </summary>
public sealed class ZoomFontSizeTests
{
    private static readonly Uri BaseUrl = new("file:///zoom-font.html");

    private static CssBox Root()
    {
        var root = new CssBox(null, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(100, 100) };
        root.LayoutEnvironment = new EchoFontEnvironment();
        return root;
    }

    private static CssBox Child(CssBox parent, string fontSize, string zoom = "normal")
    {
        var b = new CssBox(parent, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(10, 10), Zoom = zoom };
        b.FontSize = fontSize;
        return b;
    }

    private static void WithNativeZoom(Action body)
    {
        var prev = NativeZoom.Enabled;
        NativeZoom.Enabled = true;
        try { body(); }
        finally { NativeZoom.Enabled = prev; }
    }

    [Fact(Timeout = 600000)]
    public void AbsoluteFontSize_Scales_By_EffectiveZoom()
    {
        var root = Root();
        root.FontSize = "16px";
        var plain = Child(root, "20px");
        var zoomed = Child(root, "20px", "2");
        var nested = Child(zoomed, "20px", "1.5"); // effectiveZoom 3

        WithNativeZoom(() =>
        {
            Assert.Equal(2.0, zoomed.ActualFont.Size / plain.ActualFont.Size, 4);
            Assert.Equal(3.0, nested.ActualFont.Size / plain.ActualFont.Size, 4);
        });
    }

    [Fact(Timeout = 600000)]
    public void EmFontSize_Compounds_Ancestor_Zoom_Once()
    {
        var root = Root();
        root.FontSize = "16px";
        var plainEm = Child(root, "2em");            // used = 2 × root computed
        var zoomedEm = Child(root, "2em", "2");      // used = (2 × root computed) × 2

        WithNativeZoom(() =>
        {
            // The zoomed 2em box is exactly twice the un-zoomed 2em box — the ancestor zoom is applied
            // once (via EffectiveZoom), not compounded through the em resolution.
            Assert.Equal(2.0, zoomedEm.ActualFont.Size / plainEm.ActualFont.Size, 4);
        });
    }

    [Fact(Timeout = 600000)]
    public void Disabled_LeavesFontSize_Unscaled()
    {
        var root = Root();
        root.FontSize = "16px";
        var plain = Child(root, "20px");
        var zoomed = Child(root, "20px", "2");

        // Flag off: zoom has no effect on the used font size.
        Assert.Equal(1.0, zoomed.ActualFont.Size / plain.ActualFont.Size, 4);
    }

    // A layout environment whose font echoes the requested point size (unlike the shared FakeFont, which
    // reports a constant), so the font-size zoom scaling is observable through ActualFont.Size.
    private sealed class EchoFontEnvironment : ILayoutEnvironment
    {
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new EchoFont(size);
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

    private sealed class EchoFont(double size) : Broiler.Graphics.ILayoutFont
    {
        public double Size { get; } = size;
        public double Height => size;
        public double UnderlineOffset => 0;
        public double LeftPadding => 0;
        public string? FontFeatures => null;
    }
}
