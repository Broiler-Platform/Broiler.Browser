using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the native CSS <c>zoom</c> behaviour of <c>word-spacing</c> (increment 5's bounded text-measurement
/// follow-up). <c>word-spacing</c> is a used length that feeds the per-word-gap advance
/// (<see cref="CssBoxProperties.ActualWordSpacing"/>, built from <c>CssUtils.WhiteSpace</c> +
/// <c>MeasureWordSpacing</c>), so it scales by the box's <see cref="CssBoxProperties.EffectiveZoom"/> — the
/// whitespace-glyph part already rides the zoomed font. Both contributing terms route through
/// <c>ApplyZoomToLength</c>, so the whole value scales proportionally, and is byte-identical while
/// <see cref="NativeZoom"/> is off. (<c>letter-spacing</c> is not laid out by the engine at all, so there is
/// nothing to scale for it.)
/// </summary>
public sealed class ZoomWordSpacingTests
{
    private static readonly Uri BaseUrl = new("file:///zoom-wordspacing.html");

    private static CssBox Box(string zoom)
    {
        var b = new CssBox(null, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(400, 400), Zoom = zoom };
        b.LayoutEnvironment = new EchoFontEnvironment();
        b.FontSize = "16px";
        return b;
    }

    private static readonly EchoFontEnvironment Env = new();

    private static double Measure(string zoom, string wordSpacing)
    {
        var b = Box(zoom);
        b.WordSpacing = wordSpacing;
        b.MeasureWordSpacing(Env);
        return b.ActualWordSpacing;
    }

    private static void WithNativeZoom(Action body)
    {
        var prev = NativeZoom.Enabled;
        NativeZoom.Enabled = true;
        try { body(); }
        finally { NativeZoom.Enabled = prev; }
    }

    [Fact(Timeout = 600000)]
    public void WordSpacing_Advance_Scales_By_EffectiveZoom()
    {
        // The EchoFont reports a zero whitespace-glyph width, so ActualWordSpacing is purely the
        // (zoom-scaled) word-spacing contribution; the zoomed value is exactly EffectiveZoom × the plain one.
        double plain = Measure("1", "10px");
        WithNativeZoom(() =>
        {
            double zoomed = Measure("2", "10px");
            Assert.Equal(2.0, zoomed / plain, 4);
        });
    }

    [Fact(Timeout = 600000)]
    public void EmWordSpacing_Rides_The_Zoomed_Font_Once()
    {
        // An em word-spacing scales through the zoomed font (GetEmHeight), not a second time: the zoomed
        // 0.5em value is exactly twice the un-zoomed one under zoom:2.
        double plain = Measure("1", "0.5em");
        WithNativeZoom(() =>
        {
            double zoomed = Measure("2", "0.5em");
            Assert.Equal(2.0, zoomed / plain, 4);
        });
    }

    [Fact(Timeout = 600000)]
    public void Disabled_LeavesWordSpacing_Unscaled()
    {
        // Flag off: a zoomed box measures the same word-spacing as an un-zoomed one — byte-identical.
        Assert.Equal(Measure("1", "10px"), Measure("2", "10px"), 6);
    }

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
