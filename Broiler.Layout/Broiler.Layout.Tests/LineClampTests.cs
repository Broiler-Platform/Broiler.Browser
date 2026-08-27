using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Overflow 4 §5 <c>line-clamp</c> / <c>max-lines</c> / <c>block-ellipsis</c> and
/// the legacy <c>-webkit-line-clamp</c> of CSS Overflow 3 §6.
/// </summary>
/// <remarks>
/// <para>
/// The weight here is on <see cref="CssBox.ResolveLineClamp"/> rather than on the
/// cutting, because that is where the rules are and where they are easy to get
/// wrong: the two spellings are <em>not</em> aliases, they disagree about which
/// boxes they apply to, and the engine's blanket
/// <c>-webkit-*</c>-to-unprefixed cascade aliasing means a lone
/// <c>-webkit-line-clamp</c> also arrives spelled <c>line-clamp</c>. A resolver
/// that simply preferred the unprefixed property would clamp a plain block that
/// WPT says must not be clamped, and would do it for a document that never used
/// the unprefixed property at all.
/// </para>
/// <para>
/// Each case names the WPT reftest that pins it, so a future change that trades
/// one of these away can see what it is trading.
/// </para>
/// </remarks>
public sealed class LineClampTests
{
    private static readonly Uri BaseUrl = new("file:///line-clamp.html");

    // The plain shorthand: any block container, no opt-in needed
    // (css/css-overflow/line-clamp/line-clamp-001).
    [Fact(Timeout = 600000)]
    public void LineClamp_Clamps_A_Plain_Block()
    {
        var box = Block();
        box.LineClamp = "4";

        Assert.Equal((4, "…"), box.ResolveLineClamp());
    }

    // `max-lines` alone clamps but implies no ellipsis — only the `line-clamp`
    // shorthand sets `block-ellipsis: auto`.
    [Fact(Timeout = 600000)]
    public void MaxLines_Alone_Clamps_Without_An_Ellipsis()
    {
        var box = Block();
        box.MaxLines = "3";

        Assert.Equal((3, string.Empty), box.ResolveLineClamp());
    }

    [Theory]
    [InlineData(CssConstants.None, "…")]        // initial value; the shorthand's `auto`
    [InlineData(CssConstants.Auto, "…")]
    [InlineData("\"...\"", "...")]              // a <string> keeps its own characters
    [InlineData("'\\2026'", "…")]               // ...including as an escape
    public void BlockEllipsis_Selects_What_Is_Placed(string declared, string expected)
    {
        var box = Block();
        box.LineClamp = "2";
        box.BlockEllipsis = declared;

        Assert.Equal((2, expected), box.ResolveLineClamp());
    }

    // -------- the legacy spelling's opt-in --------

    // -webkit-line-clamp on something that is not a legacy box does nothing
    // (webkit-line-clamp-001: a plain block, expecting all five of its lines).
    [Fact(Timeout = 600000)]
    public void WebkitLineClamp_Does_Not_Clamp_A_Plain_Block()
    {
        var box = Block();
        box.WebkitLineClamp = "3";

        Assert.Null(box.ResolveLineClamp());
    }

    // ...nor on a legacy box laid out along the inline axis (webkit-line-clamp-015).
    [Fact(Timeout = 600000)]
    public void WebkitLineClamp_Does_Not_Clamp_A_Horizontal_Legacy_Box()
    {
        var box = Block();
        box.LegacyWebkitBoxDisplay = "-webkit-box";
        box.WebkitBoxOrient = "horizontal";
        box.WebkitLineClamp = "3";

        Assert.Null(box.ResolveLineClamp());
    }

    [Theory]
    [InlineData("vertical")]
    [InlineData("block-axis")]
    public void WebkitLineClamp_Clamps_A_Vertical_Legacy_Box(string orient)
    {
        var box = Block();
        box.LegacyWebkitBoxDisplay = "-webkit-box";
        box.WebkitBoxOrient = orient;
        box.WebkitLineClamp = "3";

        Assert.Equal((3, "…"), box.ResolveLineClamp());
    }

    // -------- the two spellings together --------

    // The case the cascade's vendor aliasing creates on its own: a document that
    // declared only `-webkit-line-clamp` is handed both properties with the same
    // value. The legacy rules must still be the ones that decide, or every such
    // document clamps a box that must not be clamped.
    [Fact(Timeout = 600000)]
    public void An_Aliased_WebkitLineClamp_Does_Not_Become_A_Plain_Clamp()
    {
        var box = Block();
        box.WebkitLineClamp = "3";
        box.LineClamp = "3";        // what the cascade's -webkit-* alias adds

        Assert.Null(box.ResolveLineClamp());
    }

    // line-clamp-034: both spellings, value 4, on a plain block — expects no
    // clamping at all, because -webkit-line-clamp's `continue: -webkit-discard`
    // is `auto` outside a legacy box and overrides the shorthand's `discard`.
    [Fact(Timeout = 600000)]
    public void WebkitLineClamp_Suppresses_LineClamp_On_A_Plain_Block()
    {
        var box = Block();
        box.LineClamp = "4";
        box.WebkitLineClamp = "4";

        Assert.Null(box.ResolveLineClamp());
    }

    // line-clamp-019: `line-clamp: 2` then `-webkit-line-clamp: 4` on a vertical
    // legacy box — expects four lines, the legacy property's count.
    [Fact(Timeout = 600000)]
    public void WebkitLineClamp_Wins_On_A_Legacy_Box()
    {
        var box = Block();
        box.LegacyWebkitBoxDisplay = "-webkit-box";
        box.WebkitBoxOrient = "vertical";
        box.LineClamp = "2";
        box.WebkitLineClamp = "4";

        Assert.Equal((4, "…"), box.ResolveLineClamp());
    }

    // -------- what is not a clamp at all --------

    [Theory]
    [InlineData(CssConstants.None)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("auto")]
    [InlineData("")]
    public void Only_A_Positive_Integer_Clamps(string value)
    {
        var box = Block();
        box.LineClamp = value;

        Assert.Null(box.ResolveLineClamp());
    }

    // CSS Overflow 4 §5: max-lines applies to block containers. A flex or grid
    // container has no inline formatting context of its own, so a clamp declared
    // on one does nothing (line-clamp-029 sets `line-clamp: 4` on a horizontal
    // `-webkit-box` — a legacy *flex* container — and expects five lines).
    [Theory]
    [InlineData("flex")]
    [InlineData("inline-flex")]
    [InlineData("grid")]
    [InlineData("inline-grid")]
    [InlineData(CssConstants.Table)]
    public void A_Clamp_On_A_Non_Block_Container_Does_Nothing(string display)
    {
        var box = Block();
        box.Display = display;
        box.LineClamp = "2";

        Assert.Null(box.ResolveLineClamp());
    }

    [Theory]
    [InlineData(CssConstants.Block)]
    [InlineData(CssConstants.InlineBlock)]
    [InlineData("flow-root")]
    [InlineData(CssConstants.ListItem)]
    [InlineData(CssConstants.TableCell)]
    public void A_Clamp_On_A_Block_Container_Applies(string display)
    {
        var box = Block();
        box.Display = display;
        box.LineClamp = "2";

        Assert.NotNull(box.ResolveLineClamp());
    }

    // -------- the legacy display mapping --------

    // The keyword cannot simply be dropped the way `grid-lanes` is: an unmapped
    // display is neither IsBlock nor IsInline, and such a box lays out and paints
    // nothing at all. Vertical is the orientation `-webkit-line-clamp` needs, and
    // a vertical legacy box stacks its children exactly as a block container does
    // — which is also how the WPT references draw the clamped result.
    [Theory]
    [InlineData("-webkit-box", "vertical", CssConstants.Block)]
    [InlineData("-webkit-inline-box", "vertical", CssConstants.InlineBlock)]
    [InlineData("-webkit-box", "horizontal", "flex")]
    [InlineData("-webkit-inline-box", "horizontal", "inline-flex")]
    public void LegacyWebkitBoxDisplay_Maps_By_Orientation(string display, string orient, string expected)
    {
        var box = Block();
        box.WebkitBoxOrient = orient;

        Assert.Equal(expected, CssUtils.NormalizeDisplayValue(display, box));
    }

    // The orientation usually arrives after `display` in author order, so the
    // mapping has to be redone when it does — otherwise every legacy box is
    // mapped as horizontal, which is the one orientation that does not clamp.
    [Fact(Timeout = 600000)]
    public void Setting_The_Orientation_Remaps_An_Already_Applied_Legacy_Display()
    {
        var box = Block();
        CssUtils.SetPropertyValue(box, "display", "-webkit-box");
        Assert.Equal("flex", box.Display);      // no orientation yet: the initial, horizontal

        CssUtils.SetPropertyValue(box, "-webkit-box-orient", "vertical");
        Assert.Equal(CssConstants.Block, box.Display);
        Assert.Equal("-webkit-box", box.LegacyWebkitBoxDisplay);
    }

    // A later non-legacy `display` has to clear the record, or the box stays
    // eligible for a legacy clamp it no longer opts into.
    [Fact(Timeout = 600000)]
    public void A_Later_Plain_Display_Clears_The_Legacy_Record()
    {
        var box = Block();
        CssUtils.SetPropertyValue(box, "display", "-webkit-box");
        CssUtils.SetPropertyValue(box, "display", CssConstants.Block);

        Assert.Null(box.LegacyWebkitBoxDisplay);
    }

    // -------- the cut itself --------

    // Four lines of one-word text in a container clamped to two: the last two
    // line boxes are discarded and the container is sized to the two it kept.
    [Fact(Timeout = 600000)]
    public void Clamping_Discards_The_Lines_Past_The_Limit_And_Shrinks_The_Container()
    {
        var (root, clamp) = TreeWithLines(lines: 4);
        clamp.LineClamp = "2";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(2, TotalLineBoxes(clamp));

        // The four lines are identical, so keeping two must halve the container --
        // stated against the unclamped layout rather than against a line height,
        // so the case says "sized to what it kept" and nothing about metrics.
        Assert.Equal(UnclampedHeight(lines: 4) / 2, clamp.ActualBottom - clamp.Location.Y, 3);
    }

    // The control: the same tree with no clamp keeps all four. Without it, a
    // change that dropped every line but the first would pass the case above.
    [Fact(Timeout = 600000)]
    public void An_Unclamped_Container_Keeps_Every_Line()
    {
        var (root, clamp) = TreeWithLines(lines: 4);
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(4, TotalLineBoxes(clamp));
        Assert.Equal(2 * UnclampedHeight(lines: 2), clamp.ActualBottom - clamp.Location.Y, 3);
    }

    /// <summary>The laid-out height of the same container with no clamp on it.</summary>
    private static double UnclampedHeight(int lines)
    {
        var (root, clamp) = TreeWithLines(lines);
        root.PerformLayout(root.LayoutEnvironment);
        return clamp.ActualBottom - clamp.Location.Y;
    }

    // A limit at or above the line count changes nothing — in particular it must
    // not place an ellipsis, because nothing was discarded.
    [Fact(Timeout = 600000)]
    public void A_Limit_The_Content_Does_Not_Reach_Changes_Nothing()
    {
        var (root, clamp) = TreeWithLines(lines: 3);
        clamp.LineClamp = "3";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Equal(3, TotalLineBoxes(clamp));
        Assert.DoesNotContain("…", AllWords(clamp), StringComparison.Ordinal);
    }

    // CSS Overflow 4 §4: the ellipsis is an anonymous inline in the *clamped
    // container's* root inline box, so it is owned by that container and takes
    // its font and colour — block-ellipsis-002 clamps a teal bold italic
    // container and expects a teal bold italic ellipsis.
    [Fact(Timeout = 600000)]
    public void The_Ellipsis_Is_Owned_By_The_Clamped_Container()
    {
        var (root, clamp) = TreeWithLines(lines: 4);
        clamp.LineClamp = "2";
        root.PerformLayout(root.LayoutEnvironment);

        var ellipsis = Assert.Single(EllipsisWords(clamp));
        Assert.Same(clamp, ellipsis.OwnerBox);
    }

    // `continue: discard` drops the box, not just its text: a child left with no
    // line of its own must not paint its background or borders either.
    [Fact(Timeout = 600000)]
    public void A_Child_Left_With_Nothing_Is_Flagged_Away_From_The_Fragment_Tree()
    {
        var (root, clamp) = TreeWithLines(lines: 4);
        clamp.LineClamp = "2";
        root.PerformLayout(root.LayoutEnvironment);

        Assert.Collection(clamp.Boxes,
            first => Assert.False(first.ClampedAway),
            second => Assert.False(second.ClampedAway),
            third => Assert.True(third.ClampedAway),
            fourth => Assert.True(fourth.ClampedAway));
    }

    // -------- helpers --------

    /// <summary>
    /// A root holding a clamp container whose <paramref name="lines"/> children are
    /// one-line blocks — the shape a <c>&lt;br&gt;</c>-separated container takes,
    /// where the lines being counted belong to several boxes rather than one.
    /// </summary>
    private static (CssBox Root, CssBox Clamp) TreeWithLines(int lines)
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new StubLayoutEnvironment(),
        };

        var clamp = new CssBox(root, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
            Display = CssConstants.Block,
            FontSize = "16px",
        };

        for (int i = 0; i < lines; i++)
        {
            var lineBlock = new CssBox(clamp, null, BaseUrl)
            {
                Location = new PointF(0, 0),
                Size = new SizeF(400, 0),
                Display = CssConstants.Block,
                FontSize = "16px",
                };

            var text = new CssBox(lineBlock, null, BaseUrl)
            {
                Display = CssConstants.Inline,
                FontSize = "16px",
                    Text = $"Line{i + 1}".AsMemory(),
            };
            text.ParseToWords();
        }

        return (root, clamp);
    }

    private static int TotalLineBoxes(CssBox box)
    {
        int count = box.LineBoxes.Count;

        foreach (var child in box.Boxes)
        {
            if (!child.ClampedAway)
                count += TotalLineBoxes(child);
        }

        return count;
    }

    private static string AllWords(CssBox box)
    {
        var text = string.Empty;

        foreach (var line in box.LineBoxes)
            foreach (var word in line.Words)
                text += word.Text;

        foreach (var child in box.Boxes)
        {
            if (!child.ClampedAway)
                text += AllWords(child);
        }

        return text;
    }

    private static List<CssRect> EllipsisWords(CssBox box)
    {
        List<CssRect> found = [];

        foreach (var line in box.LineBoxes)
            foreach (var word in line.Words)
                if (word.Text == "…")
                    found.Add(word);

        foreach (var child in box.Boxes)
        {
            if (!child.ClampedAway)
                found.AddRange(EllipsisWords(child));
        }

        return found;
    }

    private static CssBox Block() =>
        new(null, null, BaseUrl)
        {
            Display = CssConstants.Block,
            LayoutEnvironment = new StubLayoutEnvironment(),
        };

    private sealed class StubLayoutEnvironment : ILayoutEnvironment
    {
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new StubFont(size);
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => new((float)(text.Length * 8), (float)font.Height);
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = text.Length; charFitWidth = text.Length * 8; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 8;
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
