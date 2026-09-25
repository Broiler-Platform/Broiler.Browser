using System.Collections.Concurrent;
using System.Diagnostics;
using Broiler.Browser;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;

namespace Broiler.Cli.Analysis;

/// <summary>What the browser window showed of a page: <see cref="WindowProbe.Render"/>'s result.</summary>
/// <param name="Image">The window's page area, as a file of the analysis.</param>
/// <param name="Page">The same image, for comparing with the analysis's own render.</param>
/// <param name="Settled">Whether the window finished loading the page before its time ran out.</param>
/// <param name="Status">The window's status text when the image was taken.</param>
/// <param name="DurationMs">How long the window took to load, run and paint the page.</param>
internal sealed record WindowRender(string Image, BBitmap Page, bool Settled, string Status, double DurationMs);

/// <summary>
/// Shows the page in the browser window itself — a whole <see cref="BrowserApp"/> over a headless
/// host, on a profile of its own — and keeps what the window painted in its page area.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the analysis needs the window as well as its own render.</b> <see cref="RenderProbe"/>
/// builds a container configured the way the window configures its own, but it is not the window: it
/// parses the document and lays it out on one thread, and hands the container its viewport before
/// the parse. The window did neither. It parsed a page on its load worker and laid it out on its UI
/// thread, in the document mode that thread had published last, and it resolved the page's media
/// queries against 99999px. So Acid1's body filled the window to the bottom, and MediaWiki's skin
/// was laid out for a screen nobody has, while the analysis's own screenshot of both was right. A
/// page that renders wrong in the browser and right in the analysis is exactly the case the
/// analysis could not see.
/// </para>
/// <para>
/// <b>It loads the page a second time.</b> The window runs its own navigation: the page's requests
/// are sent again, on the window's own ephemeral profile, and are not in <c>network.json</c>. Content
/// that changes from one load to the next differs between the two images for that reason alone.
/// </para>
/// </remarks>
internal static class WindowProbe
{
    internal const string ImageName = "screenshot-window.png";

    /// <summary>
    /// Opens <paramref name="url"/> in a window whose page area is
    /// <paramref name="width"/>×<paramref name="height"/>, runs it until the window reports it done or
    /// <paramref name="budget"/> runs out, and writes the page area to <see cref="ImageName"/>.
    /// </summary>
    public static WindowRender Render(string url, int width, int height, string outputDirectory, TimeSpan budget)
    {
        var clock = Stopwatch.StartNew();
        var posted = new ConcurrentQueue<Action>();
        BSize window = BrowserApp.WindowSizeFor(width, height);

        // The host posts to this thread, which is the window's UI thread for the length of the run:
        // everything the window would do on its message loop happens in the loop below.
        using BrowserUiHost host = new(
            () => window,
            static () => 1.0,
            static () => { },
            static _ => { },
            action =>
            {
                posted.Enqueue(action);
                return true;
            });
        using BImageRenderer renderer = new();
        using BrowserApp app = new(host, () => renderer, url, static _ => { });

        var settled = false;
        while (clock.Elapsed < budget)
        {
            while (posted.TryDequeue(out Action? action))
                action();

            if (app.HasPendingWork)
                app.StepAnimation();

            if (host.IsInvalidated)
                _ = app.RenderFrame();

            if (string.Equals(app.Status, "Done", StringComparison.Ordinal) && posted.IsEmpty && !host.IsInvalidated)
            {
                settled = true;
                break;
            }

            Thread.Sleep(5);
        }

        using BBitmap frame = renderer.RenderToImage(
            app.RenderFrame(),
            new BSurfaceDescriptor(window, 1.0),
            new BFrameContext(BColor.White));
        var page = Crop(frame, app.PageArea);
        page.Save(Path.Combine(outputDirectory, ImageName));
        return new WindowRender(ImageName, page, settled, app.Status, Math.Round(clock.Elapsed.TotalMilliseconds, 1));
    }

    /// <summary>
    /// The share of pixels that differ between the analysis's render and the window's page area, over
    /// the part of the two they both cover, with <see cref="RenderProbe.DifferenceRatio"/>'s tolerance
    /// for anti-aliasing. The two are different bitmap types — the renderer's and the window's — so
    /// they are compared pixel by pixel rather than converted.
    /// </summary>
    public static double Difference(Broiler.HTML.Image.BBitmap analysis, BBitmap window, int tolerance = 8)
    {
        var width = Math.Min(analysis.Width, window.Width);
        var height = Math.Min(analysis.Height, window.Height);
        if (width <= 0 || height <= 0)
            return 1.0;

        long differing = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = analysis.GetPixel(x, y);
                var q = window.GetPixel(x, y);
                if (Math.Abs(p.R - q.R) > tolerance || Math.Abs(p.G - q.G) > tolerance
                    || Math.Abs(p.B - q.B) > tolerance || Math.Abs(p.A - q.A) > tolerance)
                {
                    differing++;
                }
            }
        }

        return (double)differing / ((long)width * height);
    }

    private static BBitmap Crop(BBitmap source, BRect area)
    {
        var x = Math.Clamp((int)Math.Round(area.X), 0, source.Width);
        var y = Math.Clamp((int)Math.Round(area.Y), 0, source.Height);
        var width = Math.Clamp((int)Math.Round(area.Width), 0, source.Width - x);
        var height = Math.Clamp((int)Math.Round(area.Height), 0, source.Height - y);

        const int Bytes = BPixelBuffer.BytesPerPixel;
        var pixels = source.CopyRgba();
        var cropped = new byte[width * height * Bytes];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(pixels, ((y + row) * source.Width + x) * Bytes, cropped, row * width * Bytes, width * Bytes);

        return new BBitmap(width, height, cropped, takeOwnership: true);
    }
}
