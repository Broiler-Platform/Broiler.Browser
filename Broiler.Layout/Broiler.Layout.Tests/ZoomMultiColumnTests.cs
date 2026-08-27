using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the native CSS <c>zoom</c> multi-column increment (increment 4, column edge cases): the
/// <c>column-gap</c> used length scales like every other length — an explicit gap by
/// <see cref="CssBoxProperties.EffectiveZoom"/>, while the <c>normal</c> default (≈ 1em) already rides the
/// zoomed font. <c>column-width</c> and the multicol fragmentation <c>height</c>/<c>max-height</c> route
/// through the same <c>ParseUsedLength</c> helper (pinned by <see cref="ZoomInsetTests"/>). Observed through
/// <see cref="CssBox.ResolveColumnGap"/>. Gated by <see cref="NativeZoom"/> — flag-off the gap is unscaled.
/// </summary>
public sealed class ZoomMultiColumnTests
{
    private static readonly Uri BaseUrl = new("file:///zoom-multicol.html");

    private static CssBox Root()
    {
        var root = new CssBox(null, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(800, 800) };
        root.LayoutEnvironment = new EchoFontEnvironment();
        root.FontSize = "16px";
        return root;
    }

    private static CssBox Column(CssBox parent, string columnGap, string zoom)
    {
        var b = new CssBox(parent, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(400, 400), Zoom = zoom };
        b.FontSize = "16px";
        b.ColumnGap = columnGap;
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
    public void ExplicitColumnGap_ScalesBy_EffectiveZoom()
    {
        var b = Column(Root(), "40px", "2");
        WithNativeZoom(() => Assert.Equal(80, b.ResolveColumnGap(), 3));
    }

    [Fact(Timeout = 600000)]
    public void NestedZoom_Compounds_On_ColumnGap()
    {
        var outer = Column(Root(), "normal", "2");
        var inner = Column(outer, "10px", "1.5"); // effectiveZoom 3
        WithNativeZoom(() => Assert.Equal(30, inner.ResolveColumnGap(), 3));
    }

    [Fact(Timeout = 600000)]
    public void Disabled_LeavesColumnGap_Unscaled()
    {
        var b = Column(Root(), "40px", "2");
        Assert.Equal(40, b.ResolveColumnGap(), 3);
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
