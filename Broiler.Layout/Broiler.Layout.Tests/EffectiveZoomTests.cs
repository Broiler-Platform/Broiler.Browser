using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins the engine <c>zoom</c> foundation (HtmlBridge complexity-reduction roadmap Phase 5, the CSS
/// <c>zoom</c> endgame): the per-box <see cref="CssBoxProperties.Zoom"/> property and the compounding
/// <see cref="CssBoxProperties.EffectiveZoom"/> it implies. The factor compounds multiplicatively down
/// the box tree and is gated by <see cref="NativeZoom"/> — <c>1.0</c> everywhere while off, so the engine
/// is zoom-neutral by default and this foundation is inert until later increments consume it.
/// </summary>
public sealed class EffectiveZoomTests
{
    private static readonly Uri BaseUrl = new("file:///zoom.html");

    private static CssBox Box(CssBox parent, string zoom = "normal")
    {
        var b = new CssBox(parent, null, BaseUrl) { Location = new PointF(0, 0), Size = new SizeF(10, 10), Zoom = zoom };
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
    public void EffectiveZoom_Compounds_Down_The_Tree_When_Enabled()
    {
        var root = Box(null, "normal");
        var child = Box(root, "2");
        var grandchild = Box(child, "1.5");

        WithNativeZoom(() =>
        {
            Assert.Equal(1.0, root.EffectiveZoom, 6);
            Assert.Equal(2.0, child.EffectiveZoom, 6);        // 1 × 2
            Assert.Equal(3.0, grandchild.EffectiveZoom, 6);   // 1 × 2 × 1.5
        });
    }

    [Fact(Timeout = 600000)]
    public void EffectiveZoom_Is_One_Everywhere_When_Disabled()
    {
        var root = Box(null, "3");
        var child = Box(root, "2");

        // Flag off (default): the engine is zoom-neutral regardless of the specified factor.
        Assert.Equal(1.0, root.EffectiveZoom, 6);
        Assert.Equal(1.0, child.EffectiveZoom, 6);
    }

    [Theory]
    [InlineData("2", 2.0)]
    [InlineData("1.5", 1.5)]
    [InlineData("150%", 1.5)]
    [InlineData("normal", 1.0)]
    [InlineData("inherit", 1.0)]
    [InlineData("", 1.0)]
    [InlineData("0", 1.0)]        // non-positive is ignored
    [InlineData("-1", 1.0)]
    [InlineData("junk", 1.0)]
    public void OwnZoom_Parses_Number_And_Percentage(string zoom, double expected)
    {
        var b = Box(null, zoom);
        WithNativeZoom(() => Assert.Equal(expected, b.EffectiveZoom, 6));
    }
}
