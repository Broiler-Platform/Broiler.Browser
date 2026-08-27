using Broiler.Layout.IR;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Images 4 §5.4 <c>image-set()</c>: which of the offered images a 1x display takes, and the
/// difference between a function that is invalid and one that validly selects nothing.
/// </summary>
/// <remarks>
/// The selection is resolved into the property value before anything downstream reads it, because
/// the two readers downstream each recognise only part of what the function can hold: the
/// background loader looks for <c>url(</c>, the paint walker looks for <c>gradient(</c>. Every case
/// here is one a WPT test in <c>css/css-images/image-set</c> states.
/// </remarks>
public sealed class CssImageSetTests
{
    private static string Resolve(string value)
    {
        Assert.True(CssImageSet.TryResolveLayers(value, out var resolved), $"expected {value} to parse");
        return resolved;
    }

    [Theory]
    // Every spelling of 1x resolves to the same image — 96dpi and 96/2.54 dpcm are one device pixel.
    [InlineData("image-set(url(\"/images/green.png\") 1x)")]
    [InlineData("image-set(url(\"/images/green.png\") 1dppx)")]
    [InlineData("image-set(url(\"/images/green.png\") 96dpi)")]
    [InlineData("image-set(url(\"/images/green.png\") 37.7952755906dpcm)")]
    [InlineData("image-set(url(\"/images/green.png\") calc(0.5x * 2))")]
    [InlineData("image-set(url(\"/images/green.png\") calc(2x / 2))")]
    // An omitted resolution is 1x.
    [InlineData("image-set(url(\"/images/green.png\"))")]
    // type() naming something this engine decodes keeps the option.
    [InlineData("image-set(url(\"/images/green.png\") 1x type('image/png'))")]
    // The prefixed spelling is the same function.
    [InlineData("-webkit-image-set(url(\"/images/green.png\") 1x)")]
    public void A_Single_Usable_Option_Is_The_Answer(string value)
    {
        Assert.Equal("url(\"/images/green.png\")", Resolve(value));
    }

    // Ties go to the first option, which is the whole of image-set-first-match-rendering: it offers
    // green then red, both at 1x.
    [Fact(Timeout = 600000)]
    public void The_First_Of_Two_Equal_Options_Wins()
    {
        Assert.Equal(
            "url(\"/images/green.png\")",
            Resolve("image-set(url(\"/images/green.png\") 1x, url(\"/images/red.png\") 1x)"));
    }

    // …and the order they are written in does not decide it: a 2x written first still loses to the
    // 1x behind it on a 1x display (image-set-unordered-res-rendering).
    [Fact(Timeout = 600000)]
    public void The_Smallest_Resolution_That_Reaches_The_Device_Wins()
    {
        Assert.Equal(
            "url(\"/images/green.png\")",
            Resolve("image-set(url(\"/images/red.png\") 2x, url(\"/images/green.png\") 1x)"));
    }

    // When nothing on offer reaches the device, the largest is the least bad.
    [Fact(Timeout = 600000)]
    public void The_Largest_Below_The_Device_Wins_When_None_Reaches_It()
    {
        Assert.True(CssImageSet.TryResolve(
            "image-set(url(\"a.png\") 0.25x, url(\"b.png\") 0.5x)", 1.0, out var image));
        Assert.Equal("url(\"b.png\")", image);
    }

    [Theory]
    // An option this device cannot take is skipped, and the next one answers instead.
    [InlineData("image-set(url(\"/images/red.png\") 1x type('image/unsupported'), url(\"/images/green.png\") 1x)")]
    [InlineData("image-set(url(\"/images/green.png\") 1x, url(\"\") 2x)")]
    [InlineData("image-set(url(\"/images/red.png\") 0x, url(\"/images/green.png\") 1x)")]
    public void An_Unusable_Option_Is_Skipped(string value)
    {
        Assert.Equal("url(\"/images/green.png\")", Resolve(value));
    }

    // A bare <string> is an image in this function, the same as wrapping it in url().
    [Fact(Timeout = 600000)]
    public void A_Bare_String_Is_An_Image()
    {
        Assert.Equal("url(\"/images/green.png\")", Resolve("image-set(\"/images/green.png\" 1x)"));
    }

    // A gradient is an image too, and unwrapping it is what lets the paint walker recognise the
    // layer as one it draws rather than one it fetches.
    [Fact(Timeout = 600000)]
    public void A_Gradient_Option_Is_Unwrapped_Whole()
    {
        Assert.Equal(
            "linear-gradient(green, lightgreen)",
            Resolve("image-set(linear-gradient(green, lightgreen) 1x)"));
    }

    // Zero resolution parses but is not usable, so a function offering only that selects nothing —
    // and `none` is what every reader of this value already understands that to mean. The test that
    // states it (image-set-zero-resolution-rendering) puts a *red* url() in the declaration under
    // it and expects a blank page, so "selects nothing" must not fall back to that declaration.
    [Fact(Timeout = 600000)]
    public void A_Function_With_No_Usable_Option_Selects_Nothing()
    {
        Assert.Equal("none", Resolve("image-set(url(\"/images/red.png\") 0x)"));
    }

    [Theory]
    // A negative resolution is a parse error, not an unusable option: CSS Syntax 3 §9 drops the
    // declaration whole, so the one cascaded under it applies. image-set-negative-resolution-
    // rendering-2 puts a *green* url() there and expects green — the opposite of the zero case.
    [InlineData("image-set(url(\"/images/red.png\") -1x)")]
    [InlineData("image-set(url(\"/images/red.png\") -1x, url(\"/images/red.png\") 2x)")]
    // Two images, or two resolutions, in one option is a parse error too.
    [InlineData("image-set(url(\"a.png\") url(\"b.png\") 1x)")]
    [InlineData("image-set(url(\"a.png\") 1x 2x)")]
    [InlineData("image-set(1x)")]
    public void An_Invalid_Function_Is_Reported_As_A_Parse_Error(string value)
    {
        Assert.False(CssImageSet.TryResolveLayers(value, out _));
    }

    [Theory]
    // Anything without an image-set() in it comes back byte-identical, which is what keeps this off
    // the path of every other background in the corpus.
    [InlineData("none")]
    [InlineData("url(\"/images/green.png\")")]
    [InlineData("linear-gradient(red, blue), url(a.png)")]
    [InlineData("")]
    public void A_Value_Without_The_Function_Is_Untouched(string value)
    {
        Assert.Equal(value, Resolve(value));
    }

    // Only the image-set layers of a multi-layer value are rewritten; the others keep their text.
    [Fact(Timeout = 600000)]
    public void Only_The_Image_Set_Layers_Of_A_List_Are_Rewritten()
    {
        Assert.Equal(
            "url(\"a.png\"), url(\"/images/green.png\"), linear-gradient(red, blue)",
            Resolve("url(\"a.png\"), image-set(url(\"/images/green.png\") 1x, url(\"b.png\") 2x), linear-gradient(red, blue)"));
    }

    // A comma inside a nested function is not a layer separator, and neither is one inside a string.
    [Fact(Timeout = 600000)]
    public void Nested_Commas_Do_Not_Split_Layers()
    {
        Assert.Equal(
            "linear-gradient(green, lightgreen)",
            Resolve("image-set(linear-gradient(green, lightgreen) 1x, url(\"b.png\") 2x)"));
        Assert.Equal("url(\"a,b.png\")", Resolve("image-set(url(\"a,b.png\") 1x)"));
    }

    [Theory]
    // A token that is not a resolution at all makes the option's second image — a parse error —
    // rather than being silently ignored.
    [InlineData("image-set(url(\"a.png\") 1)")]
    [InlineData("image-set(url(\"a.png\") calc(1x * 2x))")]
    [InlineData("image-set(url(\"a.png\") calc(1x + 1))")]
    public void A_Token_That_Is_Not_A_Resolution_Is_Not_Treated_As_One(string value)
    {
        Assert.False(CssImageSet.TryResolveLayers(value, out _));
    }
}
