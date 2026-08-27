using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS 2.1 §11.1.2 <c>clip</c>, and the <c>clip-path: inset()</c> it is resolved into.
/// </summary>
/// <remarks>
/// The property is stated from two edges — <c>top</c>/<c>bottom</c> measured down from the border
/// box's top, <c>right</c>/<c>left</c> measured right from its left — so the numbers that look like
/// a full box are usually an empty clip. That is the shape most of <c>css/CSS2/visufx/clip-*</c>
/// state, each under a different unit, and each meaning "nothing should be visible".
/// </remarks>
public sealed class ClipRectProjectionTests
{
    private static readonly Uri BaseUrl = new("file:///clip.html");

    /// <summary>An absolutely positioned box of <paramref name="size"/> px, square by default.</summary>
    private static CssBox NewBox(string clip, float size = 96, string position = "absolute")
    {
        var box = Bare(position, size);
        CssUtils.SetPropertyValue(box, "clip", clip);
        return box;
    }

    private static CssBox Bare(string position = "absolute", float size = 96) =>
        new(null, null, BaseUrl)
        {
            Position = position,
            Size = new SizeF(size, size),
            Location = new PointF(0, 0),
            LayoutEnvironment = new FakeLayoutEnvironment(),
        };

    [Fact(Timeout = 600000)]
    public void Clip_RoundTrips_Through_The_Cascade_Mapping()
    {
        var box = NewBox("rect(0px, 1px, 1px, 0px)");
        Assert.Equal("rect(0px, 1px, 1px, 0px)", CssUtils.GetPropertyValue(box, "clip"));
    }

    [Fact(Timeout = 600000)]
    public void The_Initial_Value_Is_Auto_And_Projects_Nothing()
    {
        var box = Bare();
        Assert.Equal("auto", box.Clip);
        Assert.False(ClipRect.TryProjectAsInset(box, out _));
    }

    // rect(top, right, bottom, left) → inset(top, width - right, height - bottom, left).
    [Fact(Timeout = 600000)]
    public void A_Rect_Becomes_The_Inset_Of_The_Same_Rectangle()
    {
        Assert.True(ClipRect.TryProjectAsInset(NewBox("rect(10px, 25px, 50px, 10px)", size: 100), out var inset));
        Assert.Equal("inset(10px 75px 50px 10px)", inset);
    }

    // Four equal offsets on a box of the same size is an empty clip: the rectangle runs from the
    // 96px mark to the 96px mark on both axes. Every unit spelling of it means the same thing.
    [Theory]
    [InlineData("rect(96px, 96px, 96px, 96px)")]
    [InlineData("rect(1in, 1in, 1in, 1in)")]
    [InlineData("rect(72pt, 72pt, 72pt, 72pt)")]
    [InlineData("rect(6pc, 6pc, 6pc, 6pc)")]
    [InlineData("rect(2.54cm, 2.54cm, 2.54cm, 2.54cm)")]
    [InlineData("rect(25.4mm, 25.4mm, 25.4mm, 25.4mm)")]
    public void Four_Equal_Offsets_On_A_Square_Box_Are_An_Empty_Clip(string clip)
    {
        Assert.True(ClipRect.TryProjectAsInset(NewBox(clip), out var inset));
        Assert.Equal("inset(96px 0px 0px 96px)", inset);
    }

    // Zero on every side is the same empty clip at the box's origin — as is negative zero, which
    // clip-004…-015 spell out one unit at a time.
    [Theory]
    [InlineData("rect(0, 0, 0, 0)")]
    [InlineData("rect(0px, 0px, 0px, 0px)")]
    [InlineData("rect(-0px, -0px, -0px, -0px)")]
    [InlineData("rect(+0px, +0px, +0px, +0px)")]
    public void Zero_On_Every_Side_Is_An_Empty_Clip_At_The_Origin(string clip)
    {
        Assert.True(ClipRect.TryProjectAsInset(NewBox(clip), out var inset));
        Assert.Equal("inset(0px 96px 96px 0px)", inset);
    }

    // `auto` is the border edge on that side: no inset from top or left, and the full extent to
    // right and bottom.
    [Fact(Timeout = 600000)]
    public void Auto_Sides_Are_The_Border_Edges()
    {
        Assert.True(ClipRect.TryProjectAsInset(NewBox("rect(auto, auto, auto, auto)"), out var inset));
        Assert.Equal("inset(0px 0px 0px 0px)", inset);
    }

    [Fact(Timeout = 600000)]
    public void A_Clip_Larger_Than_The_Box_Insets_Negatively()
    {
        Assert.True(ClipRect.TryProjectAsInset(NewBox("rect(-50px, auto, auto, -50px)"), out var inset));
        Assert.Equal("inset(-50px 0px 0px -50px)", inset);
    }

    // CSS 2.1's grammar says commas; every engine has always taken spaces too, and clip-016…-021
    // exist to say so, one per mixture.
    [Theory]
    [InlineData("rect(0 0 0 0)")]
    [InlineData("rect(0,0,0 0)")]
    [InlineData("rect(0,0 0,0)")]
    [InlineData("rect(0 0,0 0)")]
    [InlineData("rect( 0 0 0 0 )")]
    public void Commas_And_Spaces_Separate_The_Sides_Alike(string clip) =>
        Assert.True(ClipRect.TryProjectAsInset(NewBox(clip), out _));

    // "Applies to: absolutely positioned elements."
    [Theory]
    [InlineData("static")]
    [InlineData("relative")]
    [InlineData("sticky")]
    public void Clip_Does_Not_Apply_To_A_Box_That_Is_Not_Absolutely_Positioned(string position) =>
        Assert.False(ClipRect.TryProjectAsInset(NewBox("rect(0, 0, 0, 0)", position: position), out _));

    [Fact(Timeout = 600000)]
    public void Clip_Applies_To_A_Fixed_Box_Too() =>
        Assert.True(ClipRect.TryProjectAsInset(NewBox("rect(0, 0, 0, 0)", position: "fixed"), out _));

    // An invalid value is dropped, leaving the element unclipped — the grammar takes lengths and
    // `auto`, so a percentage, a shape function, or the wrong number of sides is not a clip.
    [Theory]
    [InlineData("auto")]
    [InlineData("circle(10px, 25px)")]
    [InlineData("rect(0, 0, 0)")]
    [InlineData("rect(0, 0, 0, 0, 0)")]
    [InlineData("rect(0%, 0%, 0%, 0%)")]
    [InlineData("rect(0, 0, 0, red)")]
    public void An_Invalid_Value_Projects_Nothing(string clip) =>
        Assert.False(ClipRect.TryProjectAsInset(NewBox(clip), out _));

    // CSS Masking 1 §7: a real clip-path supersedes clip, so the box paints with its own.
    [Fact(Timeout = 600000)]
    public void A_Clip_Path_Wins_Over_Clip()
    {
        var box = NewBox("rect(0, 0, 0, 0)");
        box.ClipPath = "circle(50%)";
        Assert.Equal("circle(50%)", ClipRect.EffectiveClipPath(box));
    }

    [Fact(Timeout = 600000)]
    public void Clip_Is_Projected_When_Clip_Path_Says_None()
    {
        var box = NewBox("rect(0, 0, 0, 0)");
        Assert.Equal("inset(0px 96px 96px 0px)", ClipRect.EffectiveClipPath(box));
    }

    [Fact(Timeout = 600000)]
    public void A_Box_With_Neither_Keeps_Its_Own_Clip_Path()
    {
        var box = Bare();
        Assert.Equal("none", ClipRect.EffectiveClipPath(box));
    }

    // Minimal ILayoutEnvironment: supplies a fixed 16px font so a rect() side stated in `em` or
    // `ex` resolves. These boxes carry no text or replaced content, so nothing else is exercised.
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
