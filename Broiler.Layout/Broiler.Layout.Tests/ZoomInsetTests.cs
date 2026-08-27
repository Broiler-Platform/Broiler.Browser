using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the native CSS <c>zoom</c> inset increment (increment-3 follow-up): the <c>top</c>/<c>right</c>/
/// <c>bottom</c>/<c>left</c> insets scale like every other used length — absolute insets by
/// <see cref="CssBoxProperties.EffectiveZoom"/>, percentages (resolved against the ancestor-zoomed
/// containing block) by this box's own zoom. Observed through
/// <see cref="CssBox.ResolveOverconstrainedAutoMargins"/>, whose §10.3.7 auto-margin split subtracts the
/// resolved insets, so a larger (zoomed) inset leaves less free space and a smaller centring margin.
/// Gated by <see cref="NativeZoom"/> — flag-off the insets are unscaled.
/// </summary>
public sealed class ZoomInsetTests
{
    private static readonly Uri BaseUrl = new("file:///zoom-inset.html");

    private static CssBox Box(CssBox parent, SizeF size, string position, string zoom = "normal")
    {
        var b = new CssBox(parent, null, BaseUrl) { Location = new PointF(0, 0), Size = size, Position = position, Zoom = zoom };
        if (parent == null)
            b.LayoutEnvironment = new FakeLayoutEnvironment();
        return b;
    }

    // A 100×100 fixed box, right/bottom pinned to 0, left/top an inset, both margins auto, definite size.
    private static CssBox InsetBox(string leftInset, string zoom)
    {
        var root = Box(null, new SizeF(1000, 1000), "static");
        var b = Box(root, new SizeF(100, 100), "fixed", zoom);
        b.Width = "100px";
        b.Height = "100px";
        b.Left = leftInset; b.Right = "0"; b.Top = leftInset; b.Bottom = "0";
        b.MarginLeft = "auto"; b.MarginRight = "auto";
        b.MarginTop = "auto"; b.MarginBottom = "auto";
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
    public void AbsoluteInset_ScalesBy_EffectiveZoom()
    {
        var b = InsetBox("40px", "2");
        WithNativeZoom(() =>
        {
            b.ResolveOverconstrainedAutoMargins(300, 300);
            // left inset 40px × 2 = 80; margin = (300 - 80 - 0 - 100) / 2 = 60.
            Assert.Equal(60, b.ActualMarginLeft, 3);
            Assert.Equal(60, b.ActualMarginTop, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void PercentInset_ScalesBy_OwnZoom_AgainstContainingBlock()
    {
        var b = InsetBox("10%", "2");
        WithNativeZoom(() =>
        {
            b.ResolveOverconstrainedAutoMargins(300, 300);
            // 10% of the (ancestor-zoomed) 300 CB = 30, × ownZoom 2 = 60; margin = (300 - 60 - 100) / 2 = 70.
            Assert.Equal(70, b.ActualMarginLeft, 3);
            Assert.Equal(70, b.ActualMarginTop, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void AbsoluteHeightFallback_ScalesBy_EffectiveZoom()
    {
        // With Size.Height still 0, §10.6.4 block-axis centring derives the box height from the explicit
        // CSS `height` (the ParseUsedLength(Height, 0) fallback in IsDefiniteBorderBoxHeight); under zoom
        // that absolute height scales by EffectiveZoom, leaving less free space and a smaller margin.
        var root = Box(null, new SizeF(1000, 1000), "static");
        var b = Box(root, new SizeF(100, 0), "fixed", "2");
        b.Height = "100px";
        b.Top = "0"; b.Bottom = "0";
        b.MarginTop = "auto"; b.MarginBottom = "auto";
        WithNativeZoom(() =>
        {
            b.ResolveOverconstrainedAutoMargins(300, 300);
            // height 100px × 2 = 200; margin = (300 - 200) / 2 = 50.
            Assert.Equal(50, b.ActualMarginTop, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void AbsoluteHeightFallback_Disabled_IsUnscaled()
    {
        var root = Box(null, new SizeF(1000, 1000), "static");
        var b = Box(root, new SizeF(100, 0), "fixed", "2");
        b.Height = "100px";
        b.Top = "0"; b.Bottom = "0";
        b.MarginTop = "auto"; b.MarginBottom = "auto";
        // Flag off: height stays 100; margin = (300 - 100) / 2 = 100.
        b.ResolveOverconstrainedAutoMargins(300, 300);
        Assert.Equal(100, b.ActualMarginTop, 3);
    }

    [Fact(Timeout = 600000)]
    public void Disabled_LeavesInsets_Unscaled()
    {
        var b = InsetBox("40px", "2");
        // Flag off: inset stays 40; margin = (300 - 40 - 100) / 2 = 80.
        b.ResolveOverconstrainedAutoMargins(300, 300);
        Assert.Equal(80, b.ActualMarginLeft, 3);
        Assert.Equal(80, b.ActualMarginTop, 3);
    }

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
