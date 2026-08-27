using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Text 3 §4.1: a forced line break at the <em>end</em> of a block generates
/// no line box of its own.
/// </summary>
/// <remarks>
/// <para>
/// The engine already applied this to a trailing <c>&lt;br&gt;</c>. It did not
/// apply it to the other spelling of the same thing — a preserved newline under
/// <c>white-space: pre</c> — because that newline is flowed as a zero-width word
/// that opens the next line and is then reported onto it, leaving a final line
/// box whose only content is the break. Every block of preserved text ending in
/// a newline was therefore laid out one whole line taller than any browser
/// renders it.
/// </para>
/// <para>
/// Found while triaging the WPT <c>css/css-overflow/line-clamp</c> reftests,
/// where it was hiding in the <em>reference</em> files rather than the tests:
/// the four <c>line-clamp-with-abspos</c>/<c>-fixed-pos</c> references state the
/// clamped result as preserved text ending in a newline, so they rendered a
/// line too tall and the tests could only pass while the clamp was equally
/// wrong.
/// </para>
/// </remarks>
public sealed class TrailingForcedBreakLineTests
{
    private static readonly Uri BaseUrl = new("file:///pre.html");

    // "Line 1\n" is one line, not two — the case every browser agrees on and
    // the engine got wrong.
    [Fact(Timeout = 600000)]
    public void A_Trailing_Newline_Adds_No_Line()
    {
        Assert.Equal(LineCount("Line 1"), LineCount("Line 1\n"));
    }

    // ...and it is still only the *trailing* one that goes.
    [Theory]
    [InlineData("Line 1", 1)]
    [InlineData("Line 1\n", 1)]
    [InlineData("Line 1\nLine 2", 2)]
    [InlineData("Line 1\nLine 2\n", 2)]
    [InlineData("Line 1\n\n", 2)]        // the interior break is content
    [InlineData("Line 1\n\nLine 3", 3)]
    public void Preserved_Newlines_Produce_The_Browser_Line_Count(string text, int expected)
    {
        Assert.Equal(expected, LineCount(text));
    }

    // A block whose entire content is one preserved newline still occupies
    // height: the break has no line to be "at the end of", so the line carrying
    // it survives and takes its inline's line-height.
    // quirks/line-height-preserved-segment-break makes a span holding nothing
    // but a newline fill a 100px box, which is only possible if it does.
    // Asserted as "has height" rather than as an exact line count or height:
    // the engine opens an empty line before the break as well, so a newline-only
    // block is two line boxes tall here where a browser makes it one. That is a
    // separate pre-existing difference, not this rule — what this pins is that
    // the trailing-break removal does not reach a block with nothing in front of
    // the break and collapse it to nothing.
    [Fact(Timeout = 600000)]
    public void A_Block_Holding_Only_A_Newline_Keeps_Its_Height()
    {
        Assert.True(Height("\n") > 0);
    }

    /// <summary>Line boxes produced for <paramref name="text"/> under <c>white-space: pre</c>.</summary>
    private static int LineCount(string text) => Layout(text).LineBoxes.Count;

    /// <summary>Laid-out content height of a <c>white-space: pre</c> block holding <paramref name="text"/>.</summary>
    private static double Height(string text)
    {
        var block = Layout(text);
        return block.ActualBottom - block.Location.Y;
    }

    private static CssBox Layout(string text)
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            WhiteSpace = CssConstants.Pre,
            LayoutEnvironment = new StubLayoutEnvironment(),
        };

        var block = new CssBox(root, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
            Display = CssConstants.Block,
            FontSize = "16px",
            WhiteSpace = CssConstants.Pre,
        };

        var content = new CssBox(block, null, BaseUrl)
        {
            Display = CssConstants.Inline,
            FontSize = "16px",
            WhiteSpace = CssConstants.Pre,
            Text = text.AsMemory(),
        };
        content.ParseToWords();

        root.PerformLayout(root.LayoutEnvironment);
        return block;
    }

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
