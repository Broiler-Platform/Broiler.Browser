using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins two native CSS <c>zoom</c> used-value gaps closed as part of the increment-6 cutover readiness
/// work:
/// <list type="bullet">
/// <item>An explicit <em>absolute</em>-length <c>line-height</c> scales by
/// <see cref="CssBoxProperties.EffectiveZoom"/> (matching the retired serialization bake, which multiplied
/// <c>line-height</c> by the used zoom); a unitless/<c>%</c>/font-relative line-height is <b>not</b>
/// re-scaled here — it already rides the zoomed font basis, so scaling again would double-count.</item>
/// <item>A <c>calc()</c> length resolves under the parser's element-zoom scope
/// (<see cref="Broiler.CSS.CssLengthParser.SetElementZoom"/>), so its absolute terms pick up
/// <c>EffectiveZoom</c> — the parent side of the increment-3 calc patch, re-added once
/// <c>Broiler.CSS</c> exposed <c>SetElementZoom</c>.</item>
/// </list>
/// Both are gated by <see cref="NativeZoom"/> — flag-off they are byte-identical.
/// </summary>
public sealed class ZoomLineHeightAndCalcTests
{
    private static readonly Uri BaseUrl = new("file:///zoom-lineheight-calc.html");

    private static CssBox Box(CssBox parent, SizeF size, string position, string zoom = "normal")
    {
        var b = new CssBox(parent, null, BaseUrl) { Location = new PointF(0, 0), Size = size, Position = position, Zoom = zoom };
        if (parent == null)
            b.LayoutEnvironment = new FakeLayoutEnvironment();
        return b;
    }

    private static void WithNativeZoom(Action body)
    {
        var prev = NativeZoom.Enabled;
        NativeZoom.Enabled = true;
        try { body(); }
        finally { NativeZoom.Enabled = prev; }
    }

    // ---- line-height -------------------------------------------------------

    private static CssBox LineHeightBox(string lineHeight, string zoom)
    {
        var root = Box(null, new SizeF(1000, 1000), "static");
        var b = Box(root, new SizeF(100, 100), "static", zoom);
        b.LineHeight = lineHeight;
        return b;
    }

    [Fact(Timeout = 600000)]
    public void AbsoluteLineHeight_ScalesBy_EffectiveZoom()
    {
        var b = LineHeightBox("20px", "2");
        WithNativeZoom(() => Assert.Equal(40, b.ActualLineHeight, 3)); // 20px × 2
    }

    [Fact(Timeout = 600000)]
    public void AbsoluteLineHeight_NestedZoom_Compounds()
    {
        var root = Box(null, new SizeF(1000, 1000), "static");
        var mid = Box(root, new SizeF(500, 500), "static", "2");
        var b = Box(mid, new SizeF(100, 100), "static", "2");
        b.LineHeight = "20px";
        WithNativeZoom(() => Assert.Equal(80, b.ActualLineHeight, 3)); // 20px × (2 × 2)
    }

    [Fact(Timeout = 600000)]
    public void AbsoluteLineHeight_Disabled_IsUnscaled()
    {
        var b = LineHeightBox("20px", "2");
        Assert.Equal(20, b.ActualLineHeight, 3); // flag off — no zoom
    }

    [Fact(Timeout = 600000)]
    public void UnitlessLineHeight_IsNotReScaled_ByZoomHelper()
    {
        // A unitless line-height rides the font size (16 in the fake environment, which does not itself
        // scale the font), so ApplyZoomToLineHeight must leave it alone: flag-on == flag-off. If the helper
        // wrongly treated unitless as an absolute length it would multiply by EffectiveZoom (double-count).
        var on = LineHeightBox("1.5", "2");
        double flagOnValue = 0;
        WithNativeZoom(() => flagOnValue = on.ActualLineHeight);
        var off = LineHeightBox("1.5", "2");
        Assert.Equal(off.ActualLineHeight, flagOnValue, 3);
    }

    // ---- calc() inset (ParseUsedLength calc wiring) ------------------------

    private static CssBox CalcInsetBox(string leftInset, string zoom)
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

    [Fact(Timeout = 600000)]
    public void CalcAbsoluteInset_ScalesBy_EffectiveZoom_ViaParserScope()
    {
        // calc(20px + 20px) = 40 absolute; under the calc-zoom scope the parser scales the absolute terms
        // by EffectiveZoom 2 → 80. Observed through the §10.3.7 auto-margin split:
        // margin = (300 - 80 - 0 - 100) / 2 = 60. (Same result as a plain 40px inset — proving the calc
        // wiring passes EffectiveZoom to CssLengthParser.)
        var b = CalcInsetBox("calc(20px + 20px)", "2");
        WithNativeZoom(() =>
        {
            b.ResolveOverconstrainedAutoMargins(300, 300);
            Assert.Equal(60, b.ActualMarginLeft, 3);
            Assert.Equal(60, b.ActualMarginTop, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void CalcInset_Disabled_IsUnscaled()
    {
        // Flag off: calc(20px + 20px) = 40, unscaled; margin = (300 - 40 - 100) / 2 = 80.
        var b = CalcInsetBox("calc(20px + 20px)", "2");
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
