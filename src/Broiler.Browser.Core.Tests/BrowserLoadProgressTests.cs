using System.Collections.Concurrent;
using Broiler.Graphics;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A page that animates while it loads must be painted while it loads.
/// </summary>
/// <remarks>
/// The load window is settled on the load worker so the page's callbacks are never run inside the
/// message pump (<c>docs/browser-load-window-pump.md</c>). Settling it <em>silently</em> was the
/// other half of that change, and it cost the browser every intermediate frame: Acid3 advances its
/// score one test per <c>setTimeout</c>, all of them inside the load window, so the browser showed
/// the final score and none of the count. The settle now reports each batch and
/// <c>BrowserApp.LoadProgress</c> paints what the UI thread can keep up with.
/// </remarks>
public class BrowserLoadProgressTests
{
    /// <summary>A page that counts to ten, one <c>setTimeout</c> at a time, inside the load window.</summary>
    private const string CountingPage = """
        <html><body><p id="n">start</p>
        <script>
        var n = 0;
        (function tick() {
            if (++n > 10) return;
            document.getElementById('n').textContent = 'count-' + n;
            // Each step costs about as much as a real page's does, so the load window spans long
            // enough for a host to paint between batches rather than settling inside one frame.
            var until = Date.now() + 40;
            while (Date.now() < until) { }
            setTimeout(tick, 20);
        })();
        </script>
        </body></html>
        """;

    [Fact(Timeout = 600000)]
    public void ALoadWindowThatAnimatesIsPaintedWhileItRuns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-load-progress-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, CountingPage);

        try
        {
            var painted = new List<string>();
            _ = RunLoad(new Uri(path).AbsoluteUri, frame =>
            {
                string text = TextOf(frame);
                if (painted.Count == 0 || painted[^1] != text)
                    painted.Add(text);
            });

            // The counting reached the screen, not just the total. Which of the ten steps get their
            // own frame depends on how fast the UI thread lays the document out — the pump publishes
            // one frame at a time — so the assertion is that the count was visibly under way, not
            // that every step was shown.
            List<string> counted = painted.Where(text => text.Contains("count-", StringComparison.Ordinal)).ToList();
            Assert.True(counted.Count > 1, $"the load window was painted {counted.Count} time(s): {string.Join(" | ", painted)}");
            Assert.Contains(counted, text => !text.Contains("count-10", StringComparison.Ordinal));
            Assert.Contains("count-10", painted[^1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The frames are produced on the load worker, so the page still finishes loading and the pump
    /// still keeps its cadence.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void PaintingTheLoadWindowStillEndsWithASettledPage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-load-progress-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, CountingPage);

        try
        {
            LoadOutcome outcome = RunLoad(new Uri(path).AbsoluteUri, static _ => { });

            Assert.Equal("Done", outcome.Status);
            Assert.False(outcome.IsBusy);
            Assert.False(outcome.HasPendingWork);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Drives the loop a windowed host runs — drain posted work, step, paint — until the page
    /// reports itself done, handing every painted frame to <paramref name="onFrame"/>.
    /// </summary>
    private static LoadOutcome RunLoad(string url, Action<BRenderList> onFrame)
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

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            while (posted.TryDequeue(out Action? action))
                action();

            if (app.HasPendingWork)
                app.StepAnimation();

            if (host.IsInvalidated)
                onFrame(app.RenderFrame());

            if (string.Equals(app.Status, "Done", StringComparison.Ordinal) && posted.IsEmpty && !host.IsInvalidated)
                break;

            Thread.Sleep(5);
        }

        return new LoadOutcome(app.Status, app.IsBusy, app.HasPendingWork);
    }

    private readonly record struct LoadOutcome(string Status, bool IsBusy, bool HasPendingWork);

    private static string TextOf(BRenderList frame) =>
        // Joined without a separator: the renderer splits a run wherever the style changes, so
        // "count-10" arrives as "count-" and "10".
        string.Concat(frame.Commands.OfType<BRenderCommand.DrawText>().Select(command => command.Text.Text));
}
