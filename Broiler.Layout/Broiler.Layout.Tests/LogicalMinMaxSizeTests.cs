using System;
using System.Drawing;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Logical 1 §4: <c>min-block-size</c>, <c>max-block-size</c> and their inline counterparts
/// name the same used values as <c>min-height</c>/<c>max-height</c>/<c>min-width</c>/<c>max-width</c>,
/// with which physical property each maps to decided by the box's writing mode.
/// </summary>
/// <remarks>
/// They parsed and then went nowhere: layout stored no flow-relative bound at all, so every
/// declaration of one was dropped on the floor. <c>css-break/break-inside-avoid-min-block-size-1</c>
/// is written entirely on <c>min-block-size</c>, and
/// <c>css-sizing/aspect-ratio/fieldset-element-002</c> caps an aspect ratio's content-based minimum
/// with <c>max-block-size</c> — neither had anything to read.
/// </remarks>
public sealed class LogicalMinMaxSizeTests
{
    private static readonly Uri BaseUrl = new("file:///logical-bounds.html");

    private static CssBox Box(string writingMode = null)
    {
        var box = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(200, 200),
        };

        if (writingMode != null)
            box.WritingMode = writingMode;

        return box;
    }

    [Fact(Timeout = 600000)]
    public void Block_Bounds_Are_The_Height_Bounds_In_Horizontal_Writing_Mode()
    {
        var box = Box();
        box.MinBlockSize = "40px";
        box.MaxBlockSize = "20px";

        Assert.Equal("40px", box.MinHeight);
        Assert.Equal("20px", box.MaxHeight);
    }

    [Fact(Timeout = 600000)]
    public void Inline_Bounds_Are_The_Width_Bounds_In_Horizontal_Writing_Mode()
    {
        var box = Box();
        box.MinInlineSize = "40px";
        box.MaxInlineSize = "20px";

        Assert.Equal("40px", box.MinWidth);
        Assert.Equal("20px", box.MaxWidth);
    }

    // The block axis is horizontal in a vertical writing mode, so the two swap.
    [Fact(Timeout = 600000)]
    public void The_Axes_Swap_In_A_Vertical_Writing_Mode()
    {
        var box = Box("vertical-rl");
        box.MaxBlockSize = "20px";
        box.MaxInlineSize = "30px";

        Assert.Equal("20px", box.MaxWidth);
        Assert.Equal("30px", box.MaxHeight);
    }

    // The physical longhand is not overridden — it is the one the author wrote last in the cascade,
    // and by the time layout reads either the cascade has already picked between them.
    [Fact(Timeout = 600000)]
    public void A_Physical_Bound_Wins_Over_The_Flow_Relative_One()
    {
        var box = Box();
        box.MaxHeight = "50px";
        box.MaxBlockSize = "20px";

        Assert.Equal("50px", box.MaxHeight);
    }

    // And a box that declares neither still reports the initial values rather than an empty string.
    [Fact(Timeout = 600000)]
    public void A_Box_With_Neither_Keeps_The_Initial_Values()
    {
        var box = Box();

        Assert.Equal("none", box.MaxHeight);
        Assert.Equal("0", box.MinHeight);
        Assert.Equal("none", box.MaxWidth);
        Assert.Equal("0", box.MinWidth);
    }
}
