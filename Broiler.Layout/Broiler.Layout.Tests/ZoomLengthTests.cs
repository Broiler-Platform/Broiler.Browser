using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the native CSS <c>zoom</c> length increment: absolute (<c>px</c>/…) and <c>rem</c> used lengths
/// scale by <see cref="CssBoxProperties.EffectiveZoom"/> (padding, margin, border width, box size);
/// font-relative (<c>em</c>) lengths scale through the zoomed font metrics (once, not twice); percentages
/// resolve against their zoomed basis and are not re-scaled here. Gated by <see cref="NativeZoom"/>.
/// </summary>
public sealed class ZoomLengthTests
{
    private static readonly Uri BaseUrl = new("file:///zoom-len.html");

    private static CssBox Root()
    {
        var root = new CssBox(null, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(400, 400) };
        root.LayoutEnvironment = new EchoFontEnvironment();
        root.FontSize = "16px";
        return root;
    }

    private static CssBox Box(CssBox parent, string zoom = "normal")
    {
        var b = new CssBox(parent, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(200, 200), Zoom = zoom };
        b.FontSize = "16px";
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
    public void AbsolutePadding_Margin_Border_Scale_By_EffectiveZoom()
    {
        var box = Box(Root(), "2");
        box.PaddingLeft = "10px";
        box.MarginTop = "8px";
        box.BorderLeftWidth = "3px";
        box.BorderLeftStyle = "solid";

        WithNativeZoom(() =>
        {
            Assert.Equal(20, box.ActualPaddingLeft, 3);
            Assert.Equal(16, box.ActualMarginTop, 3);
            Assert.Equal(6, box.ActualBorderLeftWidth, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void NestedZoom_Compounds_On_Lengths()
    {
        var outer = Box(Root(), "2");
        var inner = Box(outer, "1.5"); // effectiveZoom 3
        inner.PaddingLeft = "10px";

        WithNativeZoom(() => Assert.Equal(30, inner.ActualPaddingLeft, 3));
    }

    [Fact(Timeout = 600000)]
    public void EmLength_Scales_Once_Through_The_Zoomed_Font()
    {
        var plain = Box(Root());
        plain.PaddingLeft = "2em";                 // 2 × 16px font
        var zoomed = Box(Root(), "2");
        zoomed.PaddingLeft = "2em";                // 2 × (16px × 2) font — scaled once, via the font

        WithNativeZoom(() =>
            Assert.Equal(2.0, zoomed.ActualPaddingLeft / plain.ActualPaddingLeft, 3));
    }

    [Fact(Timeout = 600000)]
    public void OutlineWidth_And_Offset_Scale_By_EffectiveZoom()
    {
        // Paint-only lengths (increment 5): outline does not affect layout, but its used width/offset
        // still scale by the element's zoom so the painted outline tracks the zoomed box.
        var box = Box(Root(), "2");
        box.OutlineStyle = "solid";
        box.OutlineWidth = "3px";
        box.OutlineOffset = "5px";

        WithNativeZoom(() =>
        {
            Assert.Equal(6, box.ActualOutlineWidth, 3);
            Assert.Equal(10, box.ActualOutlineOffset, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void OutlineWidth_Keyword_Scales_By_EffectiveZoom()
    {
        var box = Box(Root(), "2");
        box.OutlineStyle = "solid";
        box.OutlineWidth = "thick"; // medium/thick keyword resolves to px, then scales like an absolute width

        WithNativeZoom(() => Assert.Equal(10, box.ActualOutlineWidth, 3)); // thick = 5px × 2
    }

    [Fact(Timeout = 600000)]
    public void ComputedStyleIr_Carries_EffectiveZoom_For_PaintOnly_Lengths()
    {
        // The paint layer resolves a few paint-only lengths (e.g. text-shadow offsets) from raw strings
        // rather than the zoomed box geometry, so the ComputedStyle IR carries EffectiveZoom as the hook
        // it scales them by. 1.0 while off; the compounded factor when a zoomed box is built.
        var zoomed = Box(Root(), "2");
        Assert.Equal(1.0, Broiler.Layout.IR.ComputedStyleBuilder.FromBox(zoomed).EffectiveZoom, 6); // flag off
        WithNativeZoom(() =>
            Assert.Equal(2.0, Broiler.Layout.IR.ComputedStyleBuilder.FromBox(zoomed).EffectiveZoom, 6));
    }

    [Fact(Timeout = 600000)]
    public void BorderRadius_Absolute_Scales_By_EffectiveZoom()
    {
        // Paint-only used length (increment 5): an absolute corner radius scales with the box's zoom.
        var box = Box(Root(), "2");
        box.CornerNwRadius = "8px";
        box.CornerSeRadius = "4px";

        WithNativeZoom(() =>
        {
            Assert.Equal(16, box.ActualCornerNw, 3);
            Assert.Equal(8, box.ActualCornerSe, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void BorderRadius_Percent_ResolvesAgainstZoomedBox_NotReScaled()
    {
        // A `%` radius resolves against the box's own (already zoom-scaled) border box, so it carries the
        // full factor and is not re-scaled: 25% of the 200px box width = 50, unchanged by the zoom flag.
        var box = Box(Root(), "2");
        box.CornerNwRadius = "25%"; // box Size.Width is 200

        WithNativeZoom(() => Assert.Equal(50, box.ActualCornerNw, 3));
    }

    [Fact(Timeout = 600000)]
    public void BorderRadius_Disabled_Unscaled()
    {
        var box = Box(Root(), "2");
        box.CornerNwRadius = "8px";
        Assert.Equal(8, box.ActualCornerNw, 3);
    }

    [Fact(Timeout = 600000)]
    public void Outline_Disabled_Unscaled()
    {
        var box = Box(Root(), "2");
        box.OutlineStyle = "solid";
        box.OutlineWidth = "3px";
        box.OutlineOffset = "5px";

        Assert.Equal(3, box.ActualOutlineWidth, 3);
        Assert.Equal(5, box.ActualOutlineOffset, 3);
    }

    [Fact(Timeout = 600000)]
    public void Disabled_LeavesLengths_Unscaled()
    {
        var box = Box(Root(), "2");
        box.PaddingLeft = "10px";
        box.BorderLeftWidth = "3px";
        box.BorderLeftStyle = "solid";

        Assert.Equal(10, box.ActualPaddingLeft, 3);
        Assert.Equal(3, box.ActualBorderLeftWidth, 3);
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
