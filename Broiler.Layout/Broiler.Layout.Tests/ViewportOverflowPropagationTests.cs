using System;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Overflow 3 §3.3: a non-<c>visible</c> <c>overflow</c> on the <c>&lt;body&gt;</c> is applied
/// to the <b>viewport</b>, not to the body's own box, whenever the root element leaves its own
/// <c>overflow</c> at <c>visible</c>.
/// </summary>
/// <remarks>
/// Broiler had no propagation at all, so <c>body { overflow: hidden }</c> clipped the body's own
/// box — WPT <c>css-overflow/overflow-body-propagation-009</c> renders 0.3 % of the canvas where the
/// reference renders 82 %. The rule that needs pinning is not the common case but the
/// disqualifications: only the <em>first</em> <c>&lt;body&gt;</c> propagates, only if it generates a
/// box, and never over a root element that set its own value
/// (<c>overflow-body-propagation-016</c> is a document with two of them).
/// </remarks>
public sealed class ViewportOverflowPropagationTests
{
    private static readonly Uri BaseUrl = new("file:///page.html");

    /// <summary>Builds <c>html &gt; body…</c> and runs the propagation on the body at
    /// <paramref name="bodyIndex"/>, returning that body's used overflow.</summary>
    private static (string Body, string Root) Propagate(
        string rootOverflow, string[] bodyOverflows, string[] bodyDisplays, int bodyIndex)
    {
        var root = new CssBox(null, new HtmlTag("html", false, null), BaseUrl)
        {
            Display = "block",
            Overflow = rootOverflow,
        };

        var bodies = new CssBox[bodyOverflows.Length];
        for (int i = 0; i < bodyOverflows.Length; i++)
        {
            bodies[i] = new CssBox(root, new HtmlTag("body", false, null), BaseUrl)
            {
                Display = bodyDisplays[i],
                Overflow = bodyOverflows[i],
            };
        }

        bodies[bodyIndex].ApplyViewportOverflowPropagation();
        return (bodies[bodyIndex].Overflow, root.Overflow);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("clip")]
    [InlineData("scroll")]
    [InlineData("auto")]
    public void The_Bodys_Overflow_Goes_To_The_Viewport(string overflow)
    {
        var (body, root) = Propagate("visible", [overflow], ["block"], 0);

        Assert.Equal("visible", body);
        // The value lands on the viewport, which Broiler clips at the canvas edge — not on the root
        // element's box, whose border box is a different rectangle once the body has margins.
        Assert.Equal("visible", root);
    }

    [Fact(Timeout = 600000)]
    public void A_Root_That_Set_Its_Own_Overflow_Keeps_It_And_The_Body_Keeps_Its_Own()
    {
        var (body, root) = Propagate("scroll", ["hidden"], ["block"], 0);

        Assert.Equal("hidden", body);
        Assert.Equal("scroll", root);
    }

    [Fact(Timeout = 600000)]
    public void A_Visible_Body_Has_Nothing_To_Propagate()
    {
        Assert.Equal("visible", Propagate("visible", ["visible"], ["block"], 0).Body);
    }

    // Two <body> elements: only the first propagates, so the second keeps its own clip.
    [Fact(Timeout = 600000)]
    public void A_Second_Body_Does_Not_Propagate()
    {
        var (body, _) = Propagate("visible", ["scroll", "hidden"], ["block", "block"], 1);

        Assert.Equal("hidden", body);
    }

    // …and when the first generates no box, nothing propagates at all — the turn does not pass to
    // the second (overflow-body-propagation-016).
    [Fact(Timeout = 600000)]
    public void A_Display_None_First_Body_Does_Not_Hand_The_Turn_To_The_Second()
    {
        var (body, _) = Propagate("visible", ["scroll", "hidden"], ["none", "block"], 1);

        Assert.Equal("hidden", body);
    }

    [Fact(Timeout = 600000)]
    public void A_Display_None_First_Body_Propagates_Nothing_Itself()
    {
        var (body, _) = Propagate("visible", ["scroll", "hidden"], ["none", "block"], 0);

        Assert.Equal("scroll", body);
    }
}
