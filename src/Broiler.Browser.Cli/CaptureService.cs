using System.Globalization;
using System.Text;
using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HTML.Image;
using Broiler.Media.Image;

namespace Broiler.Cli;

// Disambiguate the unqualified `Regex` type: the Broiler.JS engine exposes a top-level
// `Broiler.Regex` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.Text.RegularExpressions.Regex by simple-name lookup (CS0118). The alias must sit
// inside the Broiler.Cli namespace scope so it is resolved before the enclosing `Broiler`
// namespace's `Regex` member. This file uses only the .NET Regex.
using Regex = System.Text.RegularExpressions.Regex;

/// <summary>
/// Supported output formats for captured content.
/// </summary>
public enum OutputFormat
{
    /// <summary>HTML output.</summary>
    Html,

    /// <summary>Plain-text output.</summary>
    Text,
}

/// <summary>
/// Supported image formats for image capture.
/// </summary>
public enum ImageFormat
{
    /// <summary>PNG image format.</summary>
    Png,

    /// <summary>JPEG image format.</summary>
    Jpeg,
}

/// <summary>
/// Options for configuring a website image capture operation.
/// </summary>
public class ImageCaptureOptions
{
    /// <summary>
    /// The URL of the website to capture as an image.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The output file path for the captured image.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// The width of the rendered image in pixels. Defaults to 1024.
    /// </summary>
    public int Width { get; init; } = 1024;

    /// <summary>
    /// The height of the rendered image in pixels. Defaults to 768.
    /// </summary>
    public int Height { get; init; } = 768;

    /// <summary>
    /// When <c>true</c>, the renderer automatically sizes the image to
    /// fit the full HTML content instead of clipping to
    /// <see cref="Width"/>×<see cref="Height"/>.
    /// </summary>
    public bool FullPage { get; init; }

    /// <summary>
    /// Navigation timeout in seconds. Defaults to 30.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// When <c>true</c>, the renderer extracts the first link from the
    /// initial HTML page and navigates to it before rendering. This
    /// emulates the Chromium/Playwright behavior for test landing pages
    /// (e.g. Acid2) that require a click to start.
    /// </summary>
    public bool FollowFirstLink { get; init; }

    /// <summary>
    /// Determines the image format from the output file extension.
    /// Returns <see cref="ImageFormat.Jpeg"/> for .jpg/.jpeg files,
    /// otherwise <see cref="ImageFormat.Png"/>.
    /// </summary>
    public ImageFormat ImageFormat
    {
        get
        {
            var ext = Path.GetExtension(OutputPath);
            return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg
                : ImageFormat.Png;
        }
    }
}

/// <summary>
/// Options for configuring a website capture operation.
/// </summary>
public class CaptureOptions
{
    /// <summary>
    /// The URL of the website to capture.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The output file path for the captured content.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Whether to capture the full page content or only a summary.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool FullPage { get; init; }

    /// <summary>
    /// Navigation timeout in seconds. Defaults to 30.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// When <c>true</c>, the renderer extracts the first link from the
    /// initial HTML page and navigates to it before capturing. This
    /// emulates the Chromium/Playwright behavior for test landing pages
    /// (e.g. Acid2) that require a click to start.
    /// </summary>
    public bool FollowFirstLink { get; init; }

    /// <summary>
    /// Determines the output format from the output file extension.
    /// Returns <see cref="OutputFormat.Text"/> for .txt files,
    /// otherwise <see cref="OutputFormat.Html"/>.
    /// </summary>
    public OutputFormat OutputFormat
    {
        get
        {
            var ext = Path.GetExtension(OutputPath);
            return ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? OutputFormat.Text
                : OutputFormat.Html;
        }
    }
}

/// <summary>
/// Options for evaluating JavaScript expressions against a page once its own scripts have run.
/// </summary>
/// <remarks>
/// This is the machine-readable counterpart to a capture: instead of an image or a serialized
/// DOM, it answers "what did the page's JavaScript compute?". A test page that publishes its
/// outcome in a global — the DuckDuckGo privacy test pages publish <c>results</c> — is otherwise
/// only readable by scraping whatever markup it happened to render its outcome into.
/// </remarks>
public class PageEvaluationOptions
{
    /// <summary>
    /// The URL of the page whose scripts are executed before the expressions are evaluated.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// The output file path for the JSON evaluation report.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// The expressions to evaluate, in order, after the page's own scripts and its load event.
    /// Pending asynchronous work is drained between them, so an expression that starts the page's
    /// test run has settled before the next expression reads its outcome.
    /// </summary>
    public required IReadOnlyList<string> Expressions { get; init; }

    /// <summary>
    /// Optional path for the post-script DOM, written as HTML. Off unless asked for.
    /// </summary>
    public string? HtmlOutputPath { get; init; }

    /// <summary>
    /// Document fetch timeout in seconds. Defaults to 30.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// One expression evaluated against a page, and what it produced.
/// </summary>
/// <param name="Index">Position in the requested order; the caller identifies its expressions by it.</param>
/// <param name="Expression">The source that was evaluated.</param>
/// <param name="Type">The JavaScript <c>typeof</c> of the value, or <c>null</c> when the evaluation threw.</param>
/// <param name="Value">The value as a string, <c>null</c> for <c>null</c>/<c>undefined</c> or when it threw.</param>
/// <param name="Error">The failure message when the evaluation threw, otherwise <c>null</c>.</param>
public sealed record PageEvaluation(int Index, string Expression, string? Type, string? Value, string? Error);

/// <summary>
/// Captures a page the way the browser window loads it — its document, its scripts and its load
/// window run through the window's own pipeline (<see cref="HeadlessBrowser"/>) — and writes the
/// result out as a document, an image, or a report of what its JavaScript computed.
/// </summary>
public class CaptureService
{
    /// <summary>
    /// Loads the page, runs its scripts, and saves the document they left as HTML, or as plain text
    /// for a <c>.txt</c> output.
    /// </summary>
    /// <remarks>
    /// The Broiler repository's command line saved the markup as fetched here, and ran the page's
    /// inline scripts only in a context with no document, for the errors they logged. On the
    /// window's pipeline the scripts run against the page, so the document they leave is what this
    /// saves; the markup as fetched is in a diagnostics bundle, beside it.
    /// </remarks>
    /// <param name="options">Capture configuration options.</param>
    /// <returns>A task that completes when the capture is finished.</returns>
    /// <exception cref="HttpRequestException">Thrown when the URL cannot be fetched.</exception>
    /// <exception cref="TimeoutException">Thrown when the document does not arrive within the timeout.</exception>
    /// <exception cref="IOException">Thrown when the output file cannot be written.</exception>
    public async Task CaptureAsync(CaptureOptions options)
    {
        EnsureOutputDirectory(options.OutputPath);

        using var browser = new HeadlessBrowser(TimeSpan.FromSeconds(options.TimeoutSeconds));
        LoadedPage page = await browser.LoadAsync(options.Url, options.FollowFirstLink);
        using var scripted = browser.Run(page);
        var html = scripted.Serialize();

        if (options.OutputFormat == OutputFormat.Text)
            html = Regex.Replace(html, @"<[^>]+>", string.Empty);

        await File.WriteAllTextAsync(options.OutputPath, html);
    }

    /// <summary>
    /// Loads a page, runs its scripts, evaluates the requested expressions in the same realm, and
    /// writes a JSON report to <see cref="PageEvaluationOptions.OutputPath"/>.
    /// </summary>
    /// <remarks>
    /// The expressions run on the page's own global, after its scripts and its load event, so an
    /// identifier a script declared — including a top-level <c>const</c>, which is a lexical
    /// binding rather than a property of <c>window</c> — resolves exactly as it would in a later
    /// script on the page.
    /// </remarks>
    public async Task EvaluatePageAsync(PageEvaluationOptions options)
    {
        EnsureOutputDirectory(options.OutputPath);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var browser = new HeadlessBrowser(TimeSpan.FromSeconds(options.TimeoutSeconds));
        LoadedPage page = await browser.LoadAsync(options.Url, followFirstLink: false);
        using var scripted = browser.Run(page, needsRealm: true);

        var evaluations = new List<PageEvaluation>(options.Expressions.Count);
        for (var i = 0; i < options.Expressions.Count; i++)
            evaluations.Add(scripted.Evaluate(i, options.Expressions[i]));

        var serialized = scripted.Serialize();
        stopwatch.Stop();

        if (options.HtmlOutputPath is { Length: > 0 } htmlPath)
        {
            EnsureOutputDirectory(htmlPath);
            await File.WriteAllTextAsync(htmlPath, serialized);
        }

        var report = new
        {
            url = options.Url,
            startedAt = startedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            durationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
            documentBytes = Encoding.UTF8.GetByteCount(page.Content.Html),
            evaluations = evaluations.Select(e => new
            {
                index = e.Index,
                expression = e.Expression,
                type = e.Type,
                value = e.Value,
                error = e.Error,
            }).ToArray(),
        };

        await File.WriteAllTextAsync(
            options.OutputPath,
            System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            }));
    }

    /// <summary>
    /// Loads the page, runs its scripts, and renders the document they left as an image.
    /// </summary>
    /// <remarks>
    /// The render is the one the Broiler repository's command line made — Broiler.HTML.Image's
    /// <see cref="HtmlRender"/>, over the same render preparation the window applies
    /// (<c>HtmlPostProcessor.ProcessForBrowsing</c>) — with the document's own URL as the base, which
    /// after a redirect or a followed link is where its relative references point. The window renders
    /// through a container that fetches images and stylesheets on the profile's network; this one
    /// fetches them itself, as it always has.
    /// </remarks>
    /// <param name="options">Image capture configuration options.</param>
    /// <returns>A task that completes when the image capture is finished.</returns>
    /// <exception cref="HttpRequestException">Thrown when the URL cannot be fetched.</exception>
    /// <exception cref="TimeoutException">Thrown when the document does not arrive within the timeout.</exception>
    /// <exception cref="IOException">Thrown when the output file cannot be written.</exception>
    public async Task CaptureImageAsync(ImageCaptureOptions options)
    {
        EnsureOutputDirectory(options.OutputPath);

        using var browser = new HeadlessBrowser(TimeSpan.FromSeconds(options.TimeoutSeconds));
        LoadedPage page = await browser.LoadAsync(options.Url, options.FollowFirstLink);
        string html;
        using (var scripted = browser.Run(page))
            html = scripted.Serialize();

        // The production render preparation — strip the already-executed scripts, drop noscript
        // fallback, empty iframe fallback — so that HtmlRender produces clean output. This is the
        // browsing profile: it deliberately does NOT apply the Acid/WPT test-harness artifact cleanup.
        html = HtmlPostProcessor.ProcessForBrowsing(html);

        var format = options.ImageFormat == ImageFormat.Jpeg
            ? ImageEncodeFormat.Jpeg
            : ImageEncodeFormat.Png;

        // A fragment identifier (e.g. "#top") renders the page scrolled to that anchor. It is the one
        // the caller asked for, as it always was, including when a link was followed.
        var fragment = new Uri(options.Url).Fragment;
        if (!string.IsNullOrEmpty(fragment) && fragment.StartsWith('#') && fragment.Length > 1)
        {
            RenderAtAnchor(html, fragment[1..], page.FinalUrl, options, format);
        }
        else if (options.FullPage)
        {
            HtmlRender.RenderToFileAutoSizedWithStyleSet(html, options.OutputPath,
                maxWidth: options.Width, maxHeight: options.Height,
                format: format, quality: 90, baseUrl: page.FinalUrl);
        }
        else
        {
            HtmlRender.RenderToFileWithStyleSet(
                html, options.Width, options.Height, options.OutputPath, format, baseUrl: page.FinalUrl);
        }
    }

    /// <summary>
    /// Renders the HTML scrolled to the element with the given <paramref name="elementId"/>,
    /// producing a viewport-sized image of the content visible at that anchor.
    /// This mirrors the approach used by the Acid2 differential test suite:
    /// layout with a tall viewport, locate the anchor, then render the viewport-sized
    /// region starting at the anchor's Y position.
    /// </summary>
    private static void RenderAtAnchor(string html, string elementId, string baseUrl, ImageCaptureOptions options,
        ImageEncodeFormat format)
    {
        using var bitmap = HtmlRender.RenderToImageAtAnchorWithStyleSet(
            html,
            elementId,
            options.Width,
            options.Height,
            baseUrl: baseUrl);

        if (bitmap is null)
        {
            HtmlRender.RenderToFileWithStyleSet(
                html,
                options.Width,
                options.Height,
                options.OutputPath,
                format,
                baseUrl: baseUrl);
            return;
        }

        bitmap.Save(options.OutputPath, format, 90);
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        try
        {
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (outputDir != null && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException)
        {
            throw new IOException($"Cannot create output directory: {ex.Message}", ex);
        }
    }
}
