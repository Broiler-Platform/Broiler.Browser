using Broiler.Cli.Analysis;
using Broiler.Graphics.Color;
using AnalysisBitmap = Broiler.HTML.Image.BBitmap;
using WindowBitmap = Broiler.Graphics.Imaging.BBitmap;

namespace Broiler.Cli.Tests;

/// <summary>
/// How the analysis compares the browser window's image with its own render: in averaged squares, so
/// that the two painting the same text a few pixels apart is no difference, and not at all for a URL
/// the window scrolls into.
/// </summary>
public sealed class WindowComparisonTests
{
    private const int Width = 320;
    private const int Height = 240;

    private static readonly BColor White = BColor.FromRgba(255, 255, 255, 255);
    private static readonly BColor Black = BColor.FromRgba(0, 0, 0, 255);
    private static readonly BColor Blue = BColor.FromRgba(0, 0, 255, 255);

    /// <summary>
    /// Lines of 5×9 glyphs 8px apart, and the same lines 3px right and 2px down: over a third of the
    /// pixels change, and no square's average moves as far as the tolerance. The window and the
    /// analysis paint the same text that far apart; 7-zip.org, laid out the same in both, differed in
    /// 8.9% of its pixels.
    /// </summary>
    [Theory(Timeout = 600000)]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    public void Text_A_Few_Pixels_Apart_Differs_In_No_Square(int dx, int dy)
    {
        using var analysis = Analysis((x, y) => IsGlyph(x, y) ? Black : White);
        using var window = Window((x, y) => IsGlyph(x - dx, y - dy) ? Black : White);

        Assert.Equal(0, WindowProbe.Difference(analysis, window));
    }

    /// <summary>
    /// Half the page filled in one image and blank in the other is half the squares, as Acid1's body
    /// filling the window to the bottom was 22.2% of them.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Content_In_One_Image_Only_Counts_Its_Squares()
    {
        using var analysis = Analysis((_, _) => White);
        using var window = Window((x, _) => x < Width / 2 ? Blue : White);

        Assert.Equal(0.5, WindowProbe.Difference(analysis, window), 3);
    }

    /// <summary>
    /// The window scrolls to a URL's fragment and the analysis's render shows the top of the page, so
    /// the two are not compared: for the URL the window opened, or the one the analysis's load
    /// was redirected to.
    /// </summary>
    [Theory(Timeout = 600000)]
    [InlineData("http://acid2.acidtests.org/#top", "http://acid2.acidtests.org/#top", "#top")]
    [InlineData("https://example.test/page", "https://example.test/landing#section", "#section")]
    public void A_Url_With_A_Fragment_Is_Not_Compared(string opened, string loaded, string fragment)
    {
        var why = WindowProbe.WhyNotCompared(opened, loaded, analysisRendered: true);

        Assert.NotNull(why);
        Assert.Contains(fragment, why, StringComparison.Ordinal);
    }

    [Theory(Timeout = 600000)]
    [InlineData("https://example.test/page")]
    [InlineData("https://example.test/page#")]
    public void A_Url_Without_One_Is_Compared(string url) =>
        Assert.Null(WindowProbe.WhyNotCompared(url, url, analysisRendered: true));

    [Fact(Timeout = 600000)]
    public void Nothing_Is_Compared_Without_The_Analysis_Render() =>
        Assert.Contains(
            "render failed",
            WindowProbe.WhyNotCompared("https://example.test/page", "https://example.test/page", analysisRendered: false),
            StringComparison.Ordinal);

    /// <summary>Lines of 5×9 glyphs at an 8px pitch, 18px apart, inside an 8px margin.</summary>
    private static bool IsGlyph(int x, int y) =>
        x >= 8 && y >= 8 && x < Width - 16 && y < Height - 16 && (x - 8) % 8 < 5 && (y - 8) % 18 < 9;

    private static AnalysisBitmap Analysis(Func<int, int, BColor> paint)
    {
        var bitmap = new AnalysisBitmap(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
                bitmap.SetPixel(x, y, paint(x, y));
        }

        return bitmap;
    }

    private static WindowBitmap Window(Func<int, int, BColor> paint)
    {
        var pixels = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var color = paint(x, y);
                var at = (y * Width + x) * 4;
                pixels[at] = color.R;
                pixels[at + 1] = color.G;
                pixels[at + 2] = color.B;
                pixels[at + 3] = color.A;
            }
        }

        return new WindowBitmap(Width, Height, pixels, takeOwnership: true);
    }
}
