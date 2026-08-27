using System;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Pins that the CSS Anchor Positioning longhands surfaced on
/// <see cref="CssBoxProperties"/> in P5.8b are projected onto the box by the
/// cascade→box mapping (<see cref="CssUtils.SetPropertyValue"/>) and round-trip
/// through <see cref="CssUtils.GetPropertyValue"/>. This is the wiring the P5.8c
/// engine anchor-placement post-pass will read; before P5.8b the switch had no
/// arm for these names, so the declared values (which the cascade already emits)
/// were silently dropped.
/// </summary>
public sealed class AnchorPropertyProjectionTests
{
    private static readonly Uri BaseUrl = new("file:///anchor.html");

    private static CssBox NewBox() => new(null, null, BaseUrl);

    [Fact(Timeout = 600000)]
    public void InitialValues_AreTheAnchorDefaults()
    {
        var box = NewBox();
        Assert.Equal("none", box.AnchorName);
        Assert.Equal("auto", box.PositionAnchor);
        Assert.Equal("none", box.PositionArea);
        Assert.Equal("normal", box.PositionTry);
        Assert.Equal("none", box.PositionTryFallbacks);
    }

    [Theory]
    [InlineData("anchor-name", "--foo")]
    [InlineData("position-anchor", "--foo")]
    [InlineData("position-area", "top left")]
    [InlineData("position-try", "flip-block")]
    [InlineData("position-try-fallbacks", "--f1, --f2, --f3")]
    public void SetPropertyValue_ProjectsOntoBox_AndRoundTrips(string property, string value)
    {
        var box = NewBox();
        CssUtils.SetPropertyValue(box, property, value);
        Assert.Equal(value, CssUtils.GetPropertyValue(box, property));
    }

    [Fact(Timeout = 600000)]
    public void SetPropertyValue_PopulatesEachNamedField()
    {
        var box = NewBox();
        CssUtils.SetPropertyValue(box, "anchor-name", "--a");
        CssUtils.SetPropertyValue(box, "position-anchor", "--a");
        CssUtils.SetPropertyValue(box, "position-area", "bottom right");
        CssUtils.SetPropertyValue(box, "position-try", "--fallback-1");
        CssUtils.SetPropertyValue(box, "position-try-fallbacks", "--f1, --f2");

        Assert.Equal("--a", box.AnchorName);
        Assert.Equal("--a", box.PositionAnchor);
        Assert.Equal("bottom right", box.PositionArea);
        Assert.Equal("--fallback-1", box.PositionTry);
        Assert.Equal("--f1, --f2", box.PositionTryFallbacks);
    }
}
