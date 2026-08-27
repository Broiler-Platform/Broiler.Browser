using System.Drawing;
using Broiler.Graphics;
using Broiler.HTML.Graphics;
using Broiler.Layout.IR;
using HtmlContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The browser app does not paint the layout display list the way the CLI does; it translates the
/// list into a <see cref="BRenderList"/> and lets the platform's text stack draw the glyphs. That
/// hop is only correct while the font it describes to the backend is the font layout measured the
/// run with — and it was not: <c>DrawTextItem.FontSize</c> is a point count that went into
/// <see cref="BFontStyle.SizeInPixels"/>, and <c>DrawTextItem.FontFamily</c> is the whole CSS
/// family list, which matches no installed family. Every word was then drawn narrower than the
/// space layout had reserved for it, so the words in a line visibly drifted apart.
///
/// These assert the translated font against the measured one directly, because that is the
/// invariant — a pixel comparison would pass or fail on which faces the host happens to have.
/// </summary>
public class RenderListTextFidelityTests
{
    private const double PointsToPixels = 96.0 / 72.0;

    /// <summary>
    /// Every unit CSS can express a font size in, including the ones the old string-parsing path
    /// got differently wrong: <c>px</c> was right by accident, <c>rem</c> parsed to nonsense,
    /// absolute units collapsed to the 12pt default, and the keywords had no case at all.
    /// </summary>
    private const string FontSizeUnitsPage = """
        <!DOCTYPE html>
        <html><head><style>
          body { font-family: Verdana, Arial, Helvetica, sans-serif; font-size: 16px; }
          .pt { font-size: 9pt; }
          .pct { font-size: 80%; }
          .em { font-size: 1.25em; }
          .rem { font-size: 1.5rem; }
          .px { font-size: 21px; }
          .smaller { font-size: smaller; }
          .larger { font-size: larger; }
          .inch { font-size: 0.2in; }
          .medium { font-size: medium; }
        </style></head><body>
          <p>default size</p>
          <p class="pt">points</p>
          <p class="pct">percent</p>
          <p class="em">em relative</p>
          <p class="rem">rem relative</p>
          <p class="px">pixels</p>
          <p class="smaller">smaller keyword</p>
          <p class="larger">larger keyword</p>
          <p class="inch">absolute inches</p>
          <p class="medium">medium keyword</p>
        </body></html>
        """;

    [Fact]
    public void Every_Run_Is_Drawn_At_The_Size_Layout_Measured_It_At()
    {
        var (items, commands) = Translate(FontSizeUnitsPage);

        Assert.NotEmpty(items);

        foreach (DrawTextItem item in items)
        {
            ILayoutFont measured = Assert.IsAssignableFrom<ILayoutFont>(item.FontHandle);
            BRenderCommand.DrawText command = Find(commands, item);

            // RFont.Size is in points; BFontStyle.SizeInPixels is in CSS pixels.
            Assert.Equal(measured.Size * PointsToPixels, command.Text.Font.SizeInPixels, 3);
        }
    }

    [Fact]
    public void No_Run_Is_Described_To_The_Backend_By_A_Css_Family_List()
    {
        var (items, commands) = Translate(FontSizeUnitsPage);

        foreach (DrawTextItem item in items)
        {
            BRenderCommand.DrawText command = Find(commands, item);

            // "Verdana, Arial, Helvetica, sans-serif" is not a family name. Passing it whole is
            // what made DirectWrite substitute an unrelated default face for the entire page.
            Assert.DoesNotContain(',', command.Text.Font.FamilyName);
            Assert.NotEmpty(command.Text.Font.FamilyName);
        }
    }

    [Fact]
    public void The_Family_Handed_To_The_Backend_Is_The_One_Layout_Resolved()
    {
        var (items, commands) = Translate(FontSizeUnitsPage);

        foreach (DrawTextItem item in items)
        {
            if (item.FontHandle is not RFont measured || string.IsNullOrEmpty(measured.Family))
                continue;

            BRenderCommand.DrawText command = Find(commands, item);
            Assert.Equal(measured.Family, command.Text.Font.FamilyName);
        }
    }

    [Fact]
    public void Slant_And_Weight_Follow_The_Face_That_Was_Measured()
    {
        const string page = """
            <!DOCTYPE html>
            <html><body style="font-family: Arial, sans-serif">
              <p style="font-style: italic">slanted</p>
              <p style="font-weight: bold">heavy</p>
              <p style="font-weight: 600">semi</p>
              <p>upright</p>
            </body></html>
            """;

        var (items, commands) = Translate(page);
        Assert.NotEmpty(items);

        foreach (DrawTextItem item in items)
        {
            if (item.FontHandle is not RFont measured)
                continue;

            BRenderCommand.DrawText command = Find(commands, item);
            bool measuredItalic = (measured.Style & FontStyle.Italic) != 0;
            bool measuredBold = (measured.Style & FontStyle.Bold) != 0;

            Assert.Equal(measuredItalic, command.Text.Font.Slant is BFontSlant.Italic or BFontSlant.Oblique);

            // Layout collapses font-weight to a bold bit at >= 600, so the drawn face has to make
            // the same collapse or it is not the face the run was measured in.
            Assert.Equal(measuredBold, command.Text.Font.Weight >= BFontWeight.Bold);
        }
    }

    [Fact]
    public void Svg_Text_Is_Described_By_Its_Measured_Font_Too()
    {
        const string page = """
            <!DOCTYPE html>
            <html><body>
              <svg width="200" height="60" xmlns="http://www.w3.org/2000/svg">
                <text x="10" y="30" font-family="Arial" font-size="18">svg run</text>
              </svg>
            </body></html>
            """;

        var (_, commands) = Translate(page);

        BRenderCommand.DrawText? svgRun = commands.FirstOrDefault(c => c.Text.Text.Contains("svg", StringComparison.Ordinal));
        Assert.NotNull(svgRun);

        // SVG synthesises its item with a font size already in CSS pixels, and the font handle it
        // carries was built at that size in points — so the same rule that fixes HTML text must not
        // scale this one twice.
        Assert.Equal(18d, svgRun.Text.Font.SizeInPixels, 1);
    }

    [Fact]
    public void A_Plain_Page_Translates_With_Nothing_Dropped()
    {
        const string page = """
            <!DOCTYPE html>
            <html><body style="font-family: Arial, sans-serif">
              <div style="border: 2px dashed #333; background: linear-gradient(#fff, #000)">
                <p>text</p>
              </div>
              <svg width="80" height="40" xmlns="http://www.w3.org/2000/svg">
                <ellipse cx="40" cy="20" rx="30" ry="15" fill="#0a0"/>
                <line x1="0" y1="0" x2="80" y2="40" stroke="#a00" stroke-width="2"/>
              </svg>
            </body></html>
            """;

        using HtmlGraphicsRenderList list = TranslateToList(page, out _);

        // Gradients, dashed borders, SVG ellipses and diagonal lines all used to be dropped or
        // flattened here; none of them may report as unsupported now.
        Assert.Empty(list.UnsupportedItems);
    }

    private static BRenderCommand.DrawText Find(IReadOnlyList<BRenderCommand.DrawText> commands, DrawTextItem item)
    {
        // Matched on the layout-assigned origin as well as the text: a page repeats short words at
        // different sizes, and a text shadow emits a second command at an offset origin.
        BRenderCommand.DrawText? match = commands.FirstOrDefault(c =>
            string.Equals(c.Text.Text, item.Text, StringComparison.Ordinal)
            && Math.Abs(c.Origin.X - item.Origin.X) < 0.01
            && Math.Abs(c.Origin.Y - item.Origin.Y) < 0.01);

        Assert.True(match is not null, $"No DrawText command was emitted for \"{item.Text}\" at {item.Origin}.");
        return match!;
    }

    private static (List<DrawTextItem> Items, List<BRenderCommand.DrawText> Commands) Translate(string html)
    {
        using HtmlGraphicsRenderList list = TranslateToList(html, out DisplayList displayList);

        return (
            displayList.Items.OfType<DrawTextItem>().Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList(),
            list.RenderList.Commands.OfType<BRenderCommand.DrawText>().ToList());
    }

    /// <summary>
    /// Lays a page out exactly as <c>BrowserViewport</c> does and runs the same translation, so the
    /// assertions are about the app's real path rather than a reconstruction of it.
    /// </summary>
    private static HtmlGraphicsRenderList TranslateToList(string html, out DisplayList displayList)
    {
        const int width = 800;
        const int height = 600;

        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
        };

        container.SetHtmlWithStyleSet(html);
        container.Location = PointF.Empty;
        container.MaxSize = new SizeF(width, height);
        container.PerformLayout(new RectangleF(0, 0, width, height));

        displayList = container.CreateDisplayList();

        using var renderer = new BImageRenderer();
        return HtmlGraphicsRenderListBuilder.Build(renderer, displayList, new RectangleF(0, 0, width, height));
    }
}
