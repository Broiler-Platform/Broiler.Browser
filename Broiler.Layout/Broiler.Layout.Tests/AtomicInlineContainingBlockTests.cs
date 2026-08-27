using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS2.1 §10.1: a box's containing block is established by its nearest ancestor
/// <em>block container</em>. An atomic inline-level box — <c>inline-block</c>,
/// <c>inline-table</c>, <c>inline-flex</c>, <c>inline-grid</c> — is one: it is inline-level
/// on the outside but establishes an independent formatting context on the inside
/// (§9.4.1; CSS Display 3 §2.5 for the inline-* flex/grid/table forms).
/// </summary>
/// <remarks>
/// <para>
/// Only <c>inline-block</c> was named in the walk, so the other three were transparent to
/// it and a descendant's containing block came back as whatever block ancestor lay beyond
/// them. Everything resolved against a containing block was then resolved against the wrong
/// box — a percentage height most visibly, since the ancestor's height is usually
/// <c>auto</c> and the percentage therefore computes to <c>auto</c> rather than to a size.
/// </para>
/// <para>
/// Found on WPT <c>css-grid/alignment/grid-item-aspect-ratio-justify-self-001</c>: a
/// <c>height: 100%</c> item in a 24×32 <c>inline-grid</c> rendered <b>1008×2016</b>, the
/// page's width taken through the item's <c>aspect-ratio</c>.
/// </para>
/// <para>These assertions read <see cref="CssBox.ContainingBlock"/> directly, which is the
/// rule itself rather than one of its consequences; the end-to-end sizing it fixes is
/// pinned by <c>Broiler.Cli.Tests.AtomicInlineContainingBlockTests</c>.</para>
/// </remarks>
public sealed class AtomicInlineContainingBlockTests
{
    private static readonly Uri BaseUrl = new("file:///containing-block.html");

    /// <summary>The four atomic inline-level displays. Each establishes a containing block
    /// for its contents exactly as its block-level counterpart does.</summary>
    [Theory]
    [InlineData(CssConstants.InlineBlock)]
    [InlineData("inline-table")]
    [InlineData("inline-flex")]
    [InlineData("inline-grid")]
    public void AnAtomicInlineLevelBoxIsTheContainingBlockForItsContents(string display)
    {
        var (wrapper, child) = PageWithAWrapper(display);

        Assert.Same(wrapper, child.ContainingBlock);
    }

    /// <summary>The block-level counterparts, which were already right — kept beside the
    /// theory above so the pairing is the assertion rather than a comment about one.</summary>
    [Theory]
    [InlineData(CssConstants.Block)]
    [InlineData(CssConstants.Table)]
    [InlineData("flex")]
    [InlineData("grid")]
    public void SoIsItsBlockLevelCounterpart(string display)
    {
        var (wrapper, child) = PageWithAWrapper(display);

        Assert.Same(wrapper, child.ContainingBlock);
    }

    /// <summary>The control that keeps the rule to <em>atomic</em> inline-level boxes: a
    /// non-replaced <c>display: inline</c> box is not a block container and establishes no
    /// containing block, so the walk still passes straight through it to the block above.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void APlainInlineIsStillTransparentToTheWalk()
    {
        var (wrapper, child) = PageWithAWrapper(CssConstants.Inline);

        Assert.NotSame(wrapper, child.ContainingBlock);
        Assert.Same(wrapper.ParentBox, child.ContainingBlock);
    }

    // ───────────────────────────────── the shape ─────────────────────────────────

    private const float PageWidth = 400;

    /// <summary>root → body → wrapper → child, the chain the walk climbs.</summary>
    private static (CssBox Wrapper, CssBox Child) PageWithAWrapper(string wrapperDisplay)
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = PointF.Empty,
            Size = new SizeF(PageWidth, PageWidth),
            Display = CssConstants.Block,
            FontSize = "16px",
        };

        var body = new CssBox(root, new HtmlTag("body", false, null), BaseUrl)
        {
            Display = CssConstants.Block,
            FontSize = "16px",
        };

        var wrapper = new CssBox(body, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = wrapperDisplay,
            Height = "32px",
            FontSize = "16px",
        };

        var child = new CssBox(wrapper, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = CssConstants.Block,
            Height = "100%",
            FontSize = "16px",
        };

        return (wrapper, child);
    }
}
