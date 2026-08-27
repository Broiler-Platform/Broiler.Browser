using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The vertical-flow rotation decomposes a multi-glyph run into per-glyph cells stacked along the
/// inline axis, and a word lives in two lists: <c>CssLineBox.Words</c> (what paint walks) and
/// <c>CssBox.Words</c> (what <c>OffsetTop</c>/<c>OffsetLeft</c> walk). Replacing only the line's copy
/// leaves the two describing different runs, and every later pass that translates a subtree then
/// moves the run nobody paints while the painted glyphs stay put.
/// </summary>
/// <remarks>
/// That is not hypothetical: <c>PerformLayout</c> runs the rotation and <em>then</em>
/// <c>RunScrollSimulation</c>, which is exactly such a translation. A scrolled
/// vertical-writing-mode page therefore painted its backgrounds at the scrolled offset and its text
/// at the unscrolled one — the residual on WPT
/// <c>css/css-view-transitions/massive-element-left-of-viewport-partially-onscreen</c>, whose
/// reference scrolls a vertical-lr document to its far end: the green and blue bands moved and the
/// Ahem glyphs stayed at the document origin.
/// </remarks>
public sealed class VerticalFlowWordListSyncTests
{
    private static readonly Uri BaseUrl = new("file:///vertical.html");

    private const string Text = "ABCDEFGH";

    /// <summary>A vertical-lr rotation root (no parent, so the rotation runs) holding one inline run.</summary>
    private static CssBox VerticalRootWithRun()
    {
        var root = new CssBox(null, new HtmlTag("div", false, null), BaseUrl)
        {
            Display = "block",
            Location = new PointF(0, 0),
            Size = new SizeF(400, 200),
            WritingMode = "vertical-lr",
            LayoutEnvironment = new MeasuringLayoutEnvironment(),
        };

        var span = new CssBox(root, new HtmlTag("span", false, null), BaseUrl) { Display = "inline" };
        span.Words.Add(new CssRectWord(span, Text, false, false));

        return root;
    }

    private static List<CssBox> Tree(CssBox box)
    {
        var all = new List<CssBox> { box };
        foreach (var child in box.Boxes)
            all.AddRange(Tree(child));
        return all;
    }

    /// <summary>Every word the line boxes would paint, in order.</summary>
    private static List<CssRect> PaintedWords(CssBox root) =>
        [.. Tree(root).SelectMany(b => b.LineBoxes).SelectMany(l => l.Words)];

    [Fact(Timeout = 600000)]
    public void EveryPaintedGlyphIsRegisteredOnItsOwnerBox()
    {
        var root = VerticalRootWithRun();
        root.PerformLayout(root.LayoutEnvironment);

        var painted = PaintedWords(root);
        Assert.NotEmpty(painted);

        // The run was decomposed, so this is genuinely exercising the substitution rather than
        // passing because nothing was replaced.
        Assert.True(painted.Count > 1, $"expected the {Text.Length}-glyph run to be decomposed, got {painted.Count} word(s)");

        foreach (var word in painted)
        {
            Assert.NotNull(word.OwnerBox);
            Assert.Contains(word, word.OwnerBox.Words);
        }
    }

    [Fact(Timeout = 600000)]
    public void APostRotationOffsetMovesThePaintedGlyphs()
    {
        var root = VerticalRootWithRun();
        root.PerformLayout(root.LayoutEnvironment);

        var painted = PaintedWords(root);
        var before = painted.Select(w => (w.Left, w.Top)).ToList();

        const double dx = 50;
        const double dy = 7;
        root.OffsetLeft(dx);
        root.OffsetTop(dy);

        // The same CssRect instances the line boxes hold must have moved — if the box kept a
        // separate pre-rotation copy, these are untouched and the page paints text at the old place.
        Assert.Equal(before.Count, painted.Count);
        for (int i = 0; i < painted.Count; i++)
        {
            Assert.Equal(before[i].Left + dx, painted[i].Left, 3);
            Assert.Equal(before[i].Top + dy, painted[i].Top, 3);
        }
    }

    /// <summary>Measures a fixed 10px advance per character so a run has a width to divide into
    /// per-glyph cells; the rotation skips a zero-width run's decomposition entirely.</summary>
    private sealed class MeasuringLayoutEnvironment : Broiler.Layout.ILayoutEnvironment
    {
        private const double CharWidth = 10;
        private static readonly Broiler.Graphics.ILayoutFont TheFont = new FakeFont();

        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => TheFont;
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => new((float)(text.Length * CharWidth), 16);

        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth)
        {
            charFit = text.Length;
            charFitWidth = text.Length * CharWidth;
        }

        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => CharWidth;
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
