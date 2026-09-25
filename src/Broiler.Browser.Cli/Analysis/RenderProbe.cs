using System.Diagnostics;
using System.Drawing;
using Broiler.Dom.Html;
using Broiler.Graphics.Color;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Image;
using Broiler.Layout;
using Broiler.Layout.IR;
using Broiler.Media.Image;
using Broiler.Net.Http;
using BDom = Broiler.Dom;

namespace Broiler.Cli.Analysis;

/// <summary>Something the renderer reported while it rendered a document.</summary>
/// <param name="AtMs">Milliseconds into the render.</param>
/// <param name="Kind">The renderer's own classification, e.g. <c>Image</c> or <c>CssParsing</c>.</param>
/// <param name="Detail">What the report said, or the source it was about.</param>
internal sealed record RenderEvent(double AtMs, string Kind, string Detail);

/// <summary>What asking for one document to be rendered produced.</summary>
internal sealed class RenderResult
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>The document's laid-out size at the viewport width.</summary>
    public SizeF ContentSize { get; set; }

    /// <summary>The height of the full-page image, which is capped.</summary>
    public int FullPageHeight { get; set; }

    /// <summary>The viewport image, relative to the output directory.</summary>
    public string? ViewportImage { get; set; }

    /// <summary>The full-page image, when the document is taller than the viewport.</summary>
    public string? FullPageImage { get; set; }

    /// <summary>The viewport image with every layout box outlined.</summary>
    public string? BoxesImage { get; set; }

    /// <summary>The canvas colour the image was cleared to, as <c>#rrggbbaa</c>.</summary>
    public string? CanvasBackground { get; set; }

    /// <summary>The fragment tree of the layout at the viewport size.</summary>
    public Fragment? Fragments { get; set; }

    /// <summary>The display list painted into the viewport image.</summary>
    public DisplayList? DisplayList { get; set; }

    /// <summary>What Broiler.Layout's own invariant checker says about <see cref="Fragments"/>.</summary>
    public IReadOnlyList<string> InvariantViolations { get; set; } = [];

    /// <summary>The renderer's <c>RenderError</c> reports.</summary>
    public List<RenderEvent> Errors { get; } = [];

    /// <summary>Each linked stylesheet the renderer asked for, before loading it.</summary>
    public List<RenderEvent> StylesheetRequests { get; } = [];

    /// <summary>Each image the renderer asked for, before loading it.</summary>
    public List<RenderEvent> ImageRequests { get; } = [];

    /// <summary>Embedded documents (iframes, objects) composited into the image.</summary>
    public int EmbeddedDocuments { get; set; }

    /// <summary>How long each step took, in milliseconds.</summary>
    public Dictionary<string, double> Timings { get; } = new(StringComparer.Ordinal);

    /// <summary>The viewport image itself, kept for comparison with another render.</summary>
    public BBitmap? Viewport { get; set; }

    /// <summary>The document the element geometry was taken from: the rendered markup, parsed.</summary>
    public BDom.DomDocument? GeometryDocument { get; set; }

    /// <summary>Each element's boxes at the viewport size, when the geometry pass ran.</summary>
    public IReadOnlyDictionary<BDom.DomElement, BoxGeometry>? ElementGeometry { get; set; }

    /// <summary>Why the geometry pass produced nothing, when it failed.</summary>
    public string? GeometryError { get; set; }
}

/// <summary>
/// Renders a document the way the browser window's content container does, and keeps everything the
/// renderer says and builds along the way: its error reports, its stylesheet and image requests, the
/// layout's fragment tree and the display list.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <see cref="HtmlRender"/>.</b> The capture commands rasterise through
/// <see cref="HtmlRender"/>'s static entry points, which build their own container and give a caller
/// none of it: no render errors, no fragment tree, and no way to load the page's images, stylesheets
/// and fonts on the profile's network, so those loads bypass the recorder and carry no cookies. This
/// builds the container itself, configured as the window configures its own
/// (<c>BrowserViewport.CreateContentContainer</c>: synchronous image loading, the document's URL as
/// base, the profile's network as transport, the document's request context), and rasterises it the
/// way <see cref="HtmlRender"/> does — the canvas colour resolved from the root, layout and paint
/// into a <see cref="BBitmap"/>, and embedded documents composited over their boxes.
/// </para>
/// <para>
/// <b>One layout is the reference.</b> The fragment tree, the display list and the box outlines all
/// come from the layout at the viewport size — the one the screenshot shows. The full-page image lays
/// the document out again at its own height, as a full-page screenshot in any browser does, so a
/// <c>vh</c> length can differ between the two images; that is what "full page" means everywhere.
/// </para>
/// </remarks>
internal static class RenderProbe
{
    /// <summary>A full-page image taller than this is cut here, so a pathological page cannot exhaust memory.</summary>
    internal const int DefaultMaxFullPageHeight = 16_384;

    /// <summary>Renders <paramref name="html"/> and writes its images under <paramref name="outputDirectory"/>.</summary>
    /// <param name="html">The document, prepared for rendering (<c>HtmlPostProcessor.ProcessForBrowsing</c>).</param>
    /// <param name="baseUrl">The document's URL.</param>
    /// <param name="network">The transport its sub-resources are loaded on, or null for the renderer's own.</param>
    /// <param name="document">The document's request context, the client of those loads.</param>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    /// <param name="outputDirectory">Where the images go.</param>
    /// <param name="imagePrefix">The images' file-name prefix, e.g. <c>screenshot</c>.</param>
    /// <param name="fullPage">Whether to render the full page as well as the viewport.</param>
    /// <param name="outlineBoxes">Whether to write the viewport image with its layout boxes outlined.</param>
    /// <param name="elementGeometry">Whether to lay the document out again by element, to attribute findings.</param>
    /// <param name="maxFullPageHeight">The full-page image's height limit.</param>
    public static RenderResult Render(
        string html,
        string baseUrl,
        IBrowserRequestTransport? network,
        DocumentRequestContext? document,
        int width,
        int height,
        string outputDirectory,
        string imagePrefix,
        bool fullPage,
        bool outlineBoxes,
        bool elementGeometry,
        int maxFullPageHeight = DefaultMaxFullPageHeight)
    {
        var result = new RenderResult { Width = width, Height = height };
        var clock = Stopwatch.StartNew();
        double Mark(string step, double since)
        {
            var now = clock.Elapsed.TotalMilliseconds;
            result.Timings[step] = Math.Round(now - since, 1);
            return now;
        }

        using var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            BaseUrl = baseUrl,
            RequestTransport = network,
            DocumentContext = document,
            Location = new PointF(0, 0),
            MaxSize = new SizeF(width, height),
        };

        // Subscribed before the parse: the parse is what loads the linked stylesheets and the fonts.
        container.RenderError += (_, e) =>
            Add(result.Errors, new RenderEvent(clock.Elapsed.TotalMilliseconds, e.Type.ToString(), Describe(e)));
        container.StylesheetLoad += (_, e) =>
            Add(result.StylesheetRequests, new RenderEvent(clock.Elapsed.TotalMilliseconds, "stylesheet", e.Src));
        container.ImageLoad += (_, e) =>
            Add(result.ImageRequests, new RenderEvent(clock.Elapsed.TotalMilliseconds, "image", e.Src));

        var since = 0.0;
        container.SetHtmlWithStyleSet(html, baseStyleSet: null, baseUrl);
        since = Mark("parse", since);

        // A top-level document always paints its canvas backdrop (CSS Color Adjust §2.4 makes only an
        // embedded one transparent), so the image is cleared to the resolved canvas colour.
        var backdrop = CanvasBackground(container);
        result.CanvasBackground = $"#{backdrop.R:x2}{backdrop.G:x2}{backdrop.B:x2}{backdrop.A:x2}";

        var viewport = new BBitmap(width, height);
        var clip = new RectangleF(0, 0, width, height);
        viewport.Clear(backdrop);
        container.PerformLayout(viewport, clip);
        since = Mark("layout", since);

        container.PerformPaint(viewport, clip);
        result.Fragments = container.LatestFragmentTree;
        result.EmbeddedDocuments = CompositeEmbeddedDocuments(result.Fragments, viewport);
        since = Mark("paint", since);

        result.ContentSize = container.ActualSize;
        result.DisplayList = container.CreateDisplayList();
        result.InvariantViolations = result.Fragments is { } tree ? FragmentInvariantChecker.Check(tree) : [];
        since = Mark("inspect", since);

        if (elementGeometry)
        {
            TakeElementGeometry(result, container, html, baseUrl, network, document, width, height);
            since = Mark("element-geometry", since);
        }

        result.ViewportImage = imagePrefix + ".png";
        viewport.Save(Path.Combine(outputDirectory, result.ViewportImage), ImageEncodeFormat.Png);
        result.Viewport = viewport;
        since = Mark("encode", since);

        if (outlineBoxes && result.Fragments is { } fragments)
        {
            using var outlined = viewport.Copy();
            BoxOutlines.Draw(outlined, fragments, width);
            result.BoxesImage = imagePrefix + "-boxes.png";
            outlined.Save(Path.Combine(outputDirectory, result.BoxesImage), ImageEncodeFormat.Png);
            since = Mark("outline", since);
        }

        var contentHeight = (int)Math.Ceiling(result.ContentSize.Height);
        if (fullPage && contentHeight > height)
        {
            var fullHeight = Math.Min(contentHeight, maxFullPageHeight);
            result.FullPageHeight = fullHeight;

            using var full = new BBitmap(width, fullHeight);
            var fullClip = new RectangleF(0, 0, width, fullHeight);
            container.MaxSize = new SizeF(width, fullHeight);
            full.Clear(backdrop);
            container.PerformLayout(full, fullClip);
            container.PerformPaint(full, fullClip);
            CompositeEmbeddedDocuments(container.LatestFragmentTree, full);

            result.FullPageImage = imagePrefix + "-full.png";
            full.Save(Path.Combine(outputDirectory, result.FullPageImage), ImageEncodeFormat.Png);
            Mark("full-page", since);
        }

        return result;
    }

    /// <summary>
    /// The share of pixels that differ between two renders of the same size, with a small per-channel
    /// tolerance so anti-aliasing noise is not counted as a difference.
    /// </summary>
    public static double DifferenceRatio(BBitmap a, BBitmap b, int tolerance = 8)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return 1.0;

        long differing = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                if (Math.Abs(p.R - q.R) > tolerance || Math.Abs(p.G - q.G) > tolerance
                    || Math.Abs(p.B - q.B) > tolerance || Math.Abs(p.A - q.A) > tolerance)
                {
                    differing++;
                }
            }
        }

        return (double)differing / ((long)a.Width * a.Height);
    }

    /// <summary>
    /// Lays the same markup out again by DOM element — the path the script bridge's layout view takes
    /// (<see cref="HtmlContainer.SetDocumentWithStyleSet"/>) — so each box can be named by the element
    /// it belongs to. It shares the first container's sub-resource cache, so it loads nothing twice.
    /// </summary>
    /// <remarks>
    /// The string path publishes the document's quirks mode itself; the document path leaves that to
    /// its caller, as the bridge does when it parses. So it is published here, from the same markup, or
    /// a quirks-mode page would be measured in standards mode.
    /// </remarks>
    private static void TakeElementGeometry(
        RenderResult result,
        HtmlContainer rendered,
        string html,
        string baseUrl,
        IBrowserRequestTransport? network,
        DocumentRequestContext? document,
        int width,
        int height)
    {
        try
        {
            var parsed = HtmlDocumentParser.ParseDocument(html).Document;
            DocumentModeContext.CurrentQuirksMode = DocumentModeContext.IsQuirksHtml(html);

            using var probe = new HtmlContainer
            {
                AvoidAsyncImagesLoading = true,
                AvoidImagesLateLoading = true,
                BaseUrl = baseUrl,
                RequestTransport = network,
                DocumentContext = document,
            };
            probe.ShareSubresourceCacheWith(rendered);
            probe.SetDocumentWithStyleSet(parsed, baseStyleSet: null, baseUrl);
            result.ElementGeometry = probe.GetLayoutGeometry(new SizeF(width, height));
            result.GeometryDocument = parsed;
        }
        catch (Exception ex)
        {
            result.GeometryError = $"{ex.GetType().FullName}: {ExceptionText.SafeMessage(ex)}";
        }
    }

    private static void Add(List<RenderEvent> list, RenderEvent item)
    {
        lock (list)
            list.Add(item);
    }

    /// <summary>How recent an exception on the reporting thread must be to be named as the likely cause.</summary>
    private static readonly TimeSpan CauseWindow = TimeSpan.FromMilliseconds(250);

    private static string Describe(HtmlRenderErrorEventArgs e)
    {
        // Broiler.HTML hands the event its type and nothing else: the message and the exception its
        // reporters pass are dropped inside the container. When the report follows an exception —
        // most do, from a catch block — that exception was recorded on this thread a moment ago,
        // which is the best lead there is until the event carries its own message.
        if (ExceptionRecorder.LastOnCurrentThread is { } last && last.Age <= CauseWindow)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"the renderer gives no message; the last exception on this thread, {last.Age.TotalMilliseconds:0} ms earlier, was #{last.Sequence} {last.Type}: {last.Message}");
        }

        return "the renderer gives no message, and no exception preceded it on this thread";
    }

    /// <summary>
    /// The colour the canvas is cleared to: the root element's background when it has one, white
    /// otherwise — what <see cref="HtmlRender"/> resolves for a document with no colour of its own.
    /// </summary>
    private static BColor CanvasBackground(HtmlContainer container)
    {
        var root = container.GetRootBackgroundColor();
        return !root.IsEmpty && root.A > 0 ? new BColor(root.R, root.G, root.B, root.A) : BColor.FromArgb(255, 255, 255, 255);
    }

    /// <summary>
    /// A canvas colour with no alpha that is not <see langword="default"/>. <see cref="HtmlRender"/>
    /// reads a default colour as "resolve the document's own", so a truly zero colour cannot ask it
    /// for a transparent canvas; this one can.
    /// </summary>
    private static readonly BColor TransparentCanvas = BColor.FromArgb(0, 0, 0, 1);

    /// <summary>
    /// Composites each embedded document (<c>&lt;iframe&gt;</c>, <c>&lt;object type="text/html"&gt;</c>)
    /// over its content box, rendered by <see cref="HtmlRender"/> at that size, as
    /// <see cref="HtmlRender"/>'s own image entry points do. Returns how many were composited.
    /// </summary>
    /// <remarks>
    /// One difference remains, and it is Broiler.Layout's to remove: <see cref="HtmlRender"/> pins the
    /// embedding element's colour scheme while it renders a frame, so a frame whose scheme differs from
    /// its element's gets an opaque canvas (CSS Color Adjust §2.4). The pin is internal to
    /// Broiler.Layout, so here every frame gets the transparent canvas a matching scheme gets — which
    /// is every frame that does not set <c>color-scheme</c> itself.
    /// </remarks>
    private static int CompositeEmbeddedDocuments(Fragment? fragment, BBitmap target)
    {
        if (fragment is null)
            return 0;

        var count = 0;
        if (!string.IsNullOrEmpty(fragment.EmbeddedDocumentHtml))
        {
            var border = fragment.Border;
            var padding = fragment.Padding;
            var dx = (int)Math.Round(fragment.Location.X + (float)(border.Left + padding.Left));
            var dy = (int)Math.Round(fragment.Location.Y + (float)(border.Top + padding.Top));
            var dw = (int)Math.Round(fragment.Size.Width - (float)(border.Left + border.Right + padding.Left + padding.Right));
            var dh = (int)Math.Round(fragment.Size.Height - (float)(border.Top + border.Bottom + padding.Top + padding.Bottom));

            if (dw > 0 && dh > 0 && (long)dw * dh * 4 <= int.MaxValue)
            {
                using var sub = HtmlRender.RenderToImageWithStyleSet(
                    fragment.EmbeddedDocumentHtml,
                    dw,
                    dh,
                    backgroundColor: TransparentCanvas,
                    baseUrl: fragment.EmbeddedDocumentBaseUrl);
                BlitOver(target, sub, dx, dy);
                count++;
            }
        }

        foreach (var child in fragment.Children)
            count += CompositeEmbeddedDocuments(child, target);

        return count;
    }

    private static void BlitOver(BBitmap target, BBitmap source, int destX, int destY)
    {
        for (var y = 0; y < source.Height; y++)
        {
            var ty = destY + y;
            if ((uint)ty >= (uint)target.Height)
                continue;

            for (var x = 0; x < source.Width; x++)
            {
                var tx = destX + x;
                if ((uint)tx >= (uint)target.Width)
                    continue;

                target.SetPixel(tx, ty, BoxOutlines.Over(source.GetPixel(x, y), target.GetPixel(tx, ty)));
            }
        }
    }
}
