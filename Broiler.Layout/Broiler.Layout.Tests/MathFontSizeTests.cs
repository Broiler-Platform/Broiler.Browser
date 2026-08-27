using Broiler.Layout.Engine;

namespace Broiler.Layout.Tests;

/// <summary>
/// <c>font-size: math</c> (issue #1538 problem 30 — WPT
/// <c>css/css-fonts/math-script-level-and-math-style/font-size-math-001.tentative</c>).
///
/// The keyword computes to the inherited size scaled by the math scaling factor, and that factor is
/// driven entirely by a change in <c>math-depth</c>; with no change it is 1, so the keyword is
/// exactly <c>1em</c>. It used to fall through to the length parser, which reads an unrecognised
/// token as <c>0</c> — and the zero clamp turned that into a 0.001pt font, so the keyword did not
/// merely fail to scale, it collapsed the element and everything under it.
/// </summary>
public sealed class MathFontSizeTests
{
    private static readonly Uri BaseUrl = new("file:///math.html");

    private static CssBox Box(CssBox? parent, string fontSize) =>
        new(parent!, null, BaseUrl) { FontSize = fontSize };

    [Fact(Timeout = 600000)]
    public void MathResolvesToTheInheritedSize()
    {
        var parent = Box(null, "40px");
        var child = Box(parent, "math");

        Assert.Equal(parent.ComputedFontSizePoints, child.ComputedFontSizePoints, 3);
    }

    [Fact(Timeout = 600000)]
    public void MathDoesNotCollapseTheSubtree()
    {
        // The failure this guards is not that `math` fails to scale — it is that the length parser
        // read it as 0, the zero clamp made it 0.001pt, and every relative size under it resolved
        // against *that*. A `larger` child is the cheapest probe: it is parent + 2, so it reports
        // what the subtree actually inherited.
        // (Equivalence to `1em` itself is what the reftest asserts, in pixels — its reference is
        // the same document with `math` written as `1em`.)
        var root = Box(null, "40px");
        var math = Box(root, "math");
        var nested = Box(math, "larger");

        Assert.Equal(root.ComputedFontSizePoints, math.ComputedFontSizePoints, 3);
        Assert.Equal(root.ComputedFontSizePoints + 2, nested.ComputedFontSizePoints, 3);
    }

    [Fact(Timeout = 600000)]
    public void MathIsMatchedCaseInsensitively()
    {
        var parent = Box(null, "40px");

        Assert.Equal(parent.ComputedFontSizePoints, Box(parent, "MATH").ComputedFontSizePoints, 3);
    }

    [Fact(Timeout = 600000)]
    public void AnUnrelatedKeywordIsStillNotTreatedAsMath()
    {
        // The arm must not swallow other tokens: `larger` keeps its own resolution rather than
        // becoming the inherited size.
        var parent = Box(null, "40px");
        var larger = Box(parent, "larger");

        Assert.NotEqual(parent.ComputedFontSizePoints, larger.ComputedFontSizePoints);
    }
}
