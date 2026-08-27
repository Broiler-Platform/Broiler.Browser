using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Image Animation 1 <c>image-animation</c>: which point on an animated image's timeline a
/// box's images decode at. <c>paused</c> and <c>stopped</c> freeze at the first frame; everything
/// else follows the document clock.
/// </summary>
/// <remarks>
/// <para>
/// The property is inherited, so setting it on the root governs the document — except for one
/// case, which is the reason this file is not two assertions: a <c>&lt;body&gt;</c> background
/// that propagates to the canvas is governed by the <em>root element's</em> value, not body's.
/// WPT asserts that rule from both sides
/// (<c>image-animation-body-background-{root-propagation,no-propagation}-paused</c>), and getting
/// it backwards passes one and fails the other, so both directions are pinned here.
/// </para>
/// <para>
/// The frozen time itself is <c>TimeSpan.Zero</c> rather than "wherever the animation had got to",
/// because a still render has no history for those to differ over. See
/// <c>CssBox.ImagePresentationTime</c>.
/// </para>
/// </remarks>
public sealed class ImageAnimationPolicyTests
{
    private static readonly Uri BaseUrl = new("file:///image-animation.html");

    [Theory]
    [InlineData("paused", true)]
    [InlineData("stopped", true)]
    [InlineData("PAUSED", true)]
    [InlineData("normal", false)]
    [InlineData("running", false)]
    public void Frozen_Only_For_Paused_And_Stopped(string value, bool frozen)
    {
        var (_, _, div) = Tree();
        div.ImageAnimation = value;

        Assert.Equal(frozen ? TimeSpan.Zero : null, div.ImagePresentationTime);
    }

    [Fact(Timeout = 600000)]
    public void The_Initial_Value_Follows_The_Document_Clock()
    {
        var (_, _, div) = Tree();

        Assert.Equal("normal", div.ImageAnimation);
        Assert.Null(div.ImagePresentationTime);
    }

    // Inherited: the box walk copies it down in InheritStyle, so a value on the root reaches a
    // descendant that never declared one.
    [Fact(Timeout = 600000)]
    public void The_Value_Inherits_To_Descendants()
    {
        var (html, body, div) = Tree();
        html.ImageAnimation = "paused";

        // The box walk inherits one level at a time, parent to child, on the way down.
        body.InheritStyle(html);
        div.InheritStyle(body);

        Assert.Equal("paused", div.ImageAnimation);
        Assert.Equal(TimeSpan.Zero, div.ImagePresentationTime);
    }

    // The root asks for the pause and body's background is the one that reaches the canvas, so the
    // canvas image is paused (WPT image-animation-body-background-root-propagation-paused).
    [Fact(Timeout = 600000)]
    public void A_Propagating_Body_Background_Takes_The_Roots_Value()
    {
        var (html, body, _) = Tree();
        html.ImageAnimation = "paused";
        body.ImageAnimation = "normal";   // an explicit override on body must not win here

        Assert.Equal(TimeSpan.Zero, body.ImagePresentationTime);
    }

    // The mirror image, and the one a naive "just read this box" implementation fails: propagation
    // carries body's background up to the canvas but not the property that governs it, so body
    // asking for a pause does not pause the canvas image (WPT
    // image-animation-body-background-no-propagation-paused).
    [Fact(Timeout = 600000)]
    public void A_Propagating_Body_Background_Ignores_Bodys_Own_Value()
    {
        var (_, body, _) = Tree();
        body.ImageAnimation = "paused";

        Assert.Null(body.ImagePresentationTime);
    }

    // Once the root has a background of its own, body's no longer propagates — it is body's
    // background again, and body's value governs it.
    [Theory]
    [InlineData("green", "none")]
    [InlineData("transparent", "url(a.gif)")]
    public void A_Body_Background_That_Does_Not_Propagate_Keeps_Bodys_Value(
        string rootBackgroundColor, string rootBackgroundImage)
    {
        var (html, body, _) = Tree();
        html.BackgroundColor = rootBackgroundColor;
        html.BackgroundImage = rootBackgroundImage;
        body.ImageAnimation = "paused";

        Assert.Equal(TimeSpan.Zero, body.ImagePresentationTime);
    }

    // The root's own background is the canvas background rather than something propagated to it,
    // so the root's value governs it whether or not body says anything.
    [Fact(Timeout = 600000)]
    public void The_Roots_Own_Background_Uses_The_Roots_Value()
    {
        var (html, body, _) = Tree();
        html.BackgroundImage = "url(anim.gif)";
        body.ImageAnimation = "paused";

        Assert.Null(html.ImagePresentationTime);
    }

    /// <summary>A root, an <c>&lt;html&gt;</c> box, a <c>&lt;body&gt;</c> box and a plain div.</summary>
    private static (CssBox Html, CssBox Body, CssBox Div) Tree()
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new StubLayoutEnvironment(),
        };

        var html = Tagged(root, "html");
        var body = Tagged(html, "body");
        var div = Tagged(body, "div");
        return (html, body, div);
    }

    private static CssBox Tagged(CssBox parent, string tagName) =>
        new(parent, new HtmlTag(tagName, isSingle: false, attributes: null), BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
            Display = CssConstants.Block,
            FontSize = "16px",
        };

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
