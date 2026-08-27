using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the native CSS <c>zoom</c> behaviour for generated <c>::before</c>/<c>::after</c> pseudo-elements
/// (increment 5, the last paint-residue item). Unlike the serialization bake — which must synthesise pseudo
/// <c>&lt;style&gt;</c> overrides because the DOM has no pseudo nodes — the engine materialises a pseudo box
/// as a real <see cref="CssBox"/> child of the originating element's box (see
/// <c>DomParser.CreatePseudoElementBox</c> → <see cref="CssBoxHelper.CreateBox(CssBox, Uri, HtmlTag, CssBox)"/>).
/// So it inherits the originating element's <see cref="CssBoxProperties.EffectiveZoom"/> through the box tree
/// and resolves its own lengths through the zoom-aware getters — no pseudo-specific zoom code is needed.
/// This test constructs a box the same way the pseudo materialiser does and pins that: the zoom is inherited
/// exactly once (not double-counted via <c>InheritStyle</c>), an own <c>zoom</c> still compounds, and the
/// generated box's lengths scale.
/// </summary>
public sealed class ZoomPseudoTests
{
    private static readonly Uri BaseUrl = new("file:///zoom-pseudo.html");

    private static CssBox Originating(string zoom)
    {
        var root = new CssBox(null, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(400, 400), Zoom = zoom };
        root.LayoutEnvironment = new EchoFontEnvironment();
        root.FontSize = "16px";
        return root;
    }

    // Materialise a pseudo box exactly as DomParser.CreatePseudoElementBox does (anonymous child box).
    private static CssBox Pseudo(CssBox originating)
    {
        var b = CssBoxHelper.CreateBox(originating, BaseUrl);
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
    public void PseudoBox_Inherits_OriginatingZoom_ExactlyOnce()
    {
        var originating = Originating("2");
        var pseudo = Pseudo(originating); // no own zoom → OwnZoom 1

        // Inherited via the box tree (GetParent().EffectiveZoom), NOT copied by InheritStyle — so it is the
        // originating element's factor exactly, not squared.
        WithNativeZoom(() => Assert.Equal(2.0, pseudo.EffectiveZoom, 6));
    }

    [Fact(Timeout = 600000)]
    public void PseudoBox_OwnZoom_Compounds_On_Top_Of_Originating()
    {
        var originating = Originating("2");
        var pseudo = Pseudo(originating);
        pseudo.Zoom = "1.5"; // e.g. ::before { zoom: 1.5 } → effective 3

        WithNativeZoom(() => Assert.Equal(3.0, pseudo.EffectiveZoom, 6));
    }

    [Fact(Timeout = 600000)]
    public void PseudoBox_Lengths_Scale_By_InheritedZoom()
    {
        var originating = Originating("2");
        var pseudo = Pseudo(originating);
        pseudo.PaddingLeft = "10px";
        pseudo.MarginTop = "8px";

        WithNativeZoom(() =>
        {
            Assert.Equal(20, pseudo.ActualPaddingLeft, 3);
            Assert.Equal(16, pseudo.ActualMarginTop, 3);
        });
    }

    [Fact(Timeout = 600000)]
    public void Disabled_LeavesPseudoBox_Unscaled()
    {
        var originating = Originating("2");
        var pseudo = Pseudo(originating);
        pseudo.PaddingLeft = "10px";

        Assert.Equal(1.0, pseudo.EffectiveZoom, 6);
        Assert.Equal(10, pseudo.ActualPaddingLeft, 3);
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
