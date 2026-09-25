using System.Collections.Concurrent;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The window parses a page with the Broiler.Dom.Html version Broiler.Browser.Core names, and builds
/// the tree a browser builds where that version's tree builder was fixed.
/// </summary>
/// <remarks>
/// The window used to run the oldest Broiler.Dom.Html its packages accepted, 0.1.0-preview.8, while
/// the fixes after it were already published. There, an end tag that matched no open element closed
/// every open element, and an inline icon's SVG <c>&lt;title&gt;</c> was moved out of its
/// <c>&lt;svg&gt;</c> into the head. The page's script reads the tree the parser built and writes
/// what it found, so the painted text says which parser the window ran.
/// </remarks>
public class PageParsingTests
{
    /// <summary>
    /// A <c>&lt;/p&gt;</c> inside a span with no paragraph open, and an icon with an SVG title. A browser
    /// reads that <c>&lt;/p&gt;</c> as an empty paragraph and leaves the span open, so "y" is the span's
    /// text too. It keeps the icon's title in the svg, so the head has one title, and the document's
    /// title is the page's. What the script writes has no spaces because the renderer paints no run
    /// for a space.
    /// </summary>
    private const string Page = """
        <!DOCTYPE html>
        <html><head><title>Page</title></head><body>
        <div><span id="span">x</p>y</span></div>
        <p>An icon <svg width="10" height="10"><title>Icon</title><rect width="10" height="10"/></svg></p>
        <p id="out">not-run</p>
        <script>
        var svgTitles = document.getElementsByTagName('svg')[0].getElementsByTagName('title').length;
        var headTitles = document.head.getElementsByTagName('title').length;
        document.getElementById('out').textContent = 'span-' + document.getElementById('span').textContent +
            '-svg-titles-' + svgTitles + '-head-titles-' + headTitles + '-title-' + document.title;
        </script>
        </body></html>
        """;

    [Fact(Timeout = 600000)]
    public void The_Window_Builds_The_Tree_A_Browser_Builds()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-page-parsing-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, Page);

        try
        {
            string painted = RunLoadPainted(new Uri(path).AbsoluteUri);

            // Broiler.Dom.Html 0.1.0-preview.8 paints "span-x-svg-titles-0-head-titles-2-title-PageIcon".
            Assert.True(
                painted.Contains("span-xy-svg-titles-1-head-titles-1-title-Page", StringComparison.Ordinal),
                $"The window painted: {painted}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Loads <paramref name="url"/> in a whole browser and answers the text of the last frame it painted.</summary>
    private static string RunLoadPainted(string url)
    {
        var posted = new ConcurrentQueue<Action>();
        using BrowserUiHost host = new(
            static () => new BSize(800, 600),
            static () => 1.0,
            static () => { },
            static _ => { },
            action => { posted.Enqueue(action); return true; });

        using BImageRenderer renderer = new();
        using BrowserApp app = new(host, () => renderer, url, static _ => { });

        string painted = string.Empty;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            while (posted.TryDequeue(out Action? action))
                action();

            if (app.HasPendingWork)
                app.StepAnimation();

            if (host.IsInvalidated)
                painted = string.Concat(app.RenderFrame().Commands.OfType<BRenderCommand.DrawText>().Select(c => c.Text.Text));

            if (string.Equals(app.Status, "Done", StringComparison.Ordinal) && posted.IsEmpty && !host.IsInvalidated)
                break;

            Thread.Sleep(5);
        }

        Assert.Equal("Done", app.Status);
        return painted;
    }
}
