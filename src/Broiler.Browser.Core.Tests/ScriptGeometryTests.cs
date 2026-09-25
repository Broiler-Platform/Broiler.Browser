using System.Collections.Concurrent;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A page's script that asks the window how big an element is gets the size the page is laid out at.
/// </summary>
/// <remarks>
/// The window's bridge had no layout view until it passed <see cref="HeadlessLayoutView"/> through
/// <see cref="BrowserApp.BridgeOptions"/>, and the bridge's null view answers 0 to every geometry
/// question — so a page that sized, positioned or laid itself out from script saw an empty page.
/// </remarks>
public class ScriptGeometryTests
{
    /// <summary>
    /// A box 120px wide, and a script that writes what <c>getBoundingClientRect</c> says its width is.
    /// The text it writes is assembled at run time, so it cannot be painted unless the script ran and
    /// was told that width. It has no spaces because the renderer paints each word as a run of its own
    /// and paints no run for a space.
    /// </summary>
    private const string MeasuringPage = """
        <html><body>
        <div id="box" style="width: 120px; height: 40px; background: #3a7"></div>
        <p id="out">not-measured</p>
        <script>
        var rect = document.getElementById('box').getBoundingClientRect();
        document.getElementById('out').textContent = 'box-width-' + rect.width;
        </script>
        </body></html>
        """;

    [Fact(Timeout = 600000)]
    public void AScriptMeasuresAnElementAtTheWidthItIsLaidOutAt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-script-geometry-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, MeasuringPage);

        try
        {
            string painted = RunLoadPainted(new Uri(path).AbsoluteUri);

            // The null view answers 0, which paints "box-width-0".
            Assert.True(painted.Contains("box-width-120", StringComparison.Ordinal), $"The window painted: {painted}");
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
