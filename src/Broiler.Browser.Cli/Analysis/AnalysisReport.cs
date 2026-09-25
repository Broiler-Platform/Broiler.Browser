using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Broiler.Cli.Analysis;

// Disambiguate the unqualified `DateTime` type: the Broiler.JS engine exposes a top-level
// `Broiler.DateTime` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.DateTime by simple-name lookup.
using DateTime = System.DateTime;

/// <summary>How much a finding matters.</summary>
internal enum FindingSeverity
{
    /// <summary>The page is visibly wrong or did not load.</summary>
    Error,

    /// <summary>Something that usually changes what renders.</summary>
    Warning,

    /// <summary>Worth knowing; often intentional.</summary>
    Info,
}

/// <summary>One thing the analysis thinks a reader should look at, and where to look.</summary>
/// <param name="Severity">How much it matters.</param>
/// <param name="Area"><c>HTML</c>, <c>CSS</c>, <c>JavaScript</c>, <c>Layout</c>, <c>Network</c>, <c>Render</c> or <c>Process</c>.</param>
/// <param name="Title">What it is, in one line.</param>
/// <param name="Detail">The evidence: counts, names, the first example.</param>
/// <param name="SeeAlso">The file that holds the rest.</param>
internal sealed record Finding(FindingSeverity Severity, string Area, string Title, string Detail, string? SeeAlso);

/// <summary>Where and on what the analysis ran.</summary>
internal sealed record AnalysisEnvironment(
    string Engine,
    string EngineVersion,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    string CliConfiguration,
    string CommandLine,
    IReadOnlyDictionary<string, string> Components)
{
    /// <summary>Reads the environment of this process.</summary>
    public static AnalysisEnvironment Capture()
    {
        var engine = typeof(Broiler.JavaScript.Engine.JSContext).Assembly;
        var components = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => a.GetName().Name is { } name && name.StartsWith("Broiler.", StringComparison.Ordinal))
            .GroupBy(static a => a.GetName().Name!, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .ToDictionary(static g => g.Key, static g => VersionOf(g.First()), StringComparer.Ordinal);

        return new AnalysisEnvironment(
            "Broiler.JS",
            VersionOf(engine),
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            typeof(AnalysisEnvironment).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "(unknown)",
            string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(Quote)),
            components);
    }

    private static string VersionOf(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "(unknown)";

    private static string Quote(string argument) =>
        argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;
}

/// <summary>The document request, as it went.</summary>
internal sealed record DocumentSummary(
    string RequestedUrl,
    string FinalUrl,
    int? Status,
    string? ContentType,
    int Bytes,
    IReadOnlyList<string> Redirects);

/// <summary>What the page's scripts did.</summary>
internal sealed record ScriptingSummary
{
    public string Engine { get; init; } = "Broiler.JS";
    public int ClassicScripts { get; init; }
    public int DeferredScripts { get; init; }
    public int ModuleRoots { get; init; }
    public IReadOnlyList<ScriptTiming> Timings { get; init; } = [];
    public int Failures { get; init; }
    public IReadOnlyList<JsErrorGroup> DistinctFailures { get; init; } = [];
    public IReadOnlyList<MissingApi> MissingApis { get; init; } = [];
    public int UnhandledRejections { get; init; }
    public IReadOnlyDictionary<string, int> Console { get; init; } = new Dictionary<string, int>();
    public long JavaScriptExceptions { get; init; }
    public bool SettleExhausted { get; init; }
    public bool PendingWorkAfterSettle { get; init; }
    public string? NavigationNotFollowed { get; init; }

    /// <summary>How many of the scripts' geometry questions made the page lay itself out.</summary>
    public int GeometryLayouts { get; init; }

    /// <summary>How many geometry questions reached the layout view, cached answers included.</summary>
    public int GeometryRequests { get; init; }

    /// <summary>The time those layouts took, which the scripts spent waiting.</summary>
    public double GeometryLayoutMs { get; init; }

    public double SlowestGeometryLayoutMs { get; init; }

    /// <summary>The script bridge's turn trace: turns that ran long, and long idle gaps between turns.</summary>
    public IReadOnlyList<string> LongTurns { get; init; } = [];
}

/// <summary>One script evaluation and how long it took.</summary>
internal sealed record ScriptTiming(string Label, double Ms, bool Succeeded);

/// <summary>The page's network, summarised.</summary>
internal sealed record NetworkSummary
{
    public int Requests { get; init; }
    public int Failed { get; init; }
    public long Bytes { get; init; }
    public IReadOnlyDictionary<string, int> ByDestination { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<NetworkEntry> Failures { get; init; } = [];
    public IReadOnlyList<NetworkEntry> Slowest { get; init; } = [];
    public int NeverRead { get; init; }
    public int StillPending { get; init; }
}

/// <summary>One render of the page, summarised.</summary>
internal sealed record RenderSummary
{
    public string? ViewportImage { get; init; }
    public string? FullPageImage { get; init; }
    public string? BoxesImage { get; init; }
    public string ContentSize { get; init; } = string.Empty;
    public int FullPageHeight { get; init; }
    public string? CanvasBackground { get; init; }
    public IReadOnlyDictionary<string, double> Timings { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<RenderEvent> Errors { get; init; } = [];
    public int StylesheetRequests { get; init; }
    public int ImageRequests { get; init; }
    public int EmbeddedDocuments { get; init; }
    public int DisplayListItems { get; init; }
    public IReadOnlyDictionary<string, int> DisplayListByKind { get; init; } = new Dictionary<string, int>();
    public string? GeometryError { get; init; }

    /// <summary>Summarises <paramref name="result"/>.</summary>
    public static RenderSummary From(RenderResult result) => new()
    {
        ViewportImage = result.ViewportImage,
        FullPageImage = result.FullPageImage,
        BoxesImage = result.BoxesImage,
        ContentSize = string.Create(
            CultureInfo.InvariantCulture, $"{result.ContentSize.Width:0.#}×{result.ContentSize.Height:0.#}"),
        FullPageHeight = result.FullPageHeight,
        CanvasBackground = result.CanvasBackground,
        Timings = result.Timings,
        Errors = result.Errors,
        StylesheetRequests = result.StylesheetRequests.Count,
        ImageRequests = result.ImageRequests.Count,
        EmbeddedDocuments = result.EmbeddedDocuments,
        DisplayListItems = result.DisplayList?.Items.Count ?? 0,
        DisplayListByKind = result.DisplayList is { } list
            ? list.Items
                .GroupBy(static item => item.GetType().Name)
                .OrderByDescending(static g => g.Count())
                .ToDictionary(static g => g.Key, static g => g.Count())
            : new Dictionary<string, int>(),
        GeometryError = result.GeometryError,
    };
}

/// <summary>The exception log, summarised.</summary>
/// <param name="Total">Every exception recorded, each counted once however often it was rethrown.</param>
/// <param name="ByKind">The total by kind: first-chance, unhandled, unobserved task.</param>
/// <param name="ByComponent">The total by the component that threw.</param>
/// <param name="Top">The most frequent signatures.</param>
/// <param name="Rethrows">The notifications that were a recorded exception thrown again, not counted in the total.</param>
/// <param name="WithoutStackRoom">The exceptions that arrived with too little stack left to record: a stack overflow was near.</param>
internal sealed record ExceptionSummary(
    long Total,
    IReadOnlyDictionary<string, int> ByKind,
    IReadOnlyDictionary<string, int> ByComponent,
    IReadOnlyList<ExceptionSignature> Top,
    long Rethrows = 0,
    long WithoutStackRoom = 0);

/// <summary>A file the analysis wrote, and what is in it.</summary>
internal sealed record AnalysisFile(string Path, string Description);

/// <summary>What the browser window itself showed of the page (<see cref="WindowProbe"/>).</summary>
/// <param name="Image">The window's page area, <c>screenshot-window.png</c>.</param>
/// <param name="DifferenceRatio">
/// The share of those pixels that differ from the analysis's own render, <c>screenshot.png</c>, or
/// null when that render failed.
/// </param>
/// <param name="Settled">Whether the window finished loading the page before its time ran out.</param>
/// <param name="Status">The window's status text when the image was taken.</param>
/// <param name="DurationMs">How long the window took to load, run and paint the page.</param>
internal sealed record WindowReport(string Image, double? DifferenceRatio, bool Settled, string Status, double DurationMs);

/// <summary>Everything one analysis found, as <c>report.json</c> records it.</summary>
internal sealed record AnalysisReport
{
    public required string Url { get; init; }
    public required DateTime StartedAt { get; init; }
    public double DurationMs { get; init; }
    public string Viewport { get; init; } = string.Empty;
    public bool Completed { get; init; }
    public string? WatchdogFired { get; init; }
    public required AnalysisEnvironment Environment { get; init; }
    public DocumentSummary? Document { get; init; }
    public IReadOnlyList<PhaseRecord> Phases { get; init; } = [];
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public ScriptingSummary? Scripting { get; init; }
    public NetworkSummary? Network { get; init; }
    public RenderSummary? Render { get; init; }
    public RenderSummary? RenderWithoutScripts { get; init; }

    /// <summary>The share of viewport pixels that differ between the page with and without its scripts.</summary>
    public double? ScriptVisualEffect { get; init; }

    /// <summary>What the browser window itself showed of the page (<see cref="WindowProbe"/>).</summary>
    public WindowReport? Window { get; init; }

    public LayoutReport? Layout { get; init; }
    public HtmlReport? Html { get; init; }
    public CssReport? Css { get; init; }
    public ExceptionSummary? Exceptions { get; init; }

    /// <summary>With <c>--sample-stacks</c>, the Broiler methods the slow phases were caught in most.</summary>
    public IReadOnlyList<HotSpot> HotSpots { get; init; } = [];

    public IReadOnlyList<AnalysisFile> Files { get; init; } = [];
}

/// <summary>
/// Turns the analysis's measurements into the short, ranked list a reader starts from: the things
/// that most often explain a page that renders or runs wrong, each with its evidence and the file
/// that holds the rest.
/// </summary>
/// <remarks>
/// Every rule here reads a measurement and states it; none of them guesses at a cause the
/// measurements do not show. Where a finding is often intentional — an element placed off the page,
/// a vendor-prefixed property — it is ranked as information, not as a problem.
/// </remarks>
internal static class Triage
{
    /// <summary>
    /// The share of the page area whose pixels may differ between the window and the analysis's render
    /// before it is a finding: form controls the window hosts natively and anti-aliasing stay below it.
    /// </summary>
    internal const double WindowDifferenceThreshold = 0.05;

    /// <summary>The parse errors whose repair changes what a page shows, and how a finding names them.</summary>
    private static readonly (string Code, FindingSeverity Severity, string Title)[] RenderingParseErrors =
    [
        ("eof-in-text", FindingSeverity.Error, "element(s) whose end tag never comes, so the rest of the document is their text"),
        ("non-void-html-element-start-tag-with-trailing-solidus", FindingSeverity.Warning, "HTML start tag(s) like <div/>, which Broiler closes at once and a browser leaves open"),
        ("eof-in-comment", FindingSeverity.Warning, "HTML comment(s) that never end, hiding everything after them"),
        ("eof-in-tag", FindingSeverity.Warning, "HTML tag(s) the document ends inside, which are dropped"),
        ("unexpected-end-tag", FindingSeverity.Info, "HTML end tag(s) that match no open element, repaired as a browser repairs them"),
        ("end-tag-closes-open-elements", FindingSeverity.Info, "HTML end tag(s) that also close elements still open inside them"),
        ("unclosed-element", FindingSeverity.Info, "HTML element(s) still open at the end of the document"),
    ];

    public static IReadOnlyList<Finding> Rank(AnalysisReport report)
    {
        var findings = new List<Finding>();

        if (report.WatchdogFired is { } watchdog)
            findings.Add(new(FindingSeverity.Error, "Process", "The analysis did not finish", watchdog, "watchdog.md"));

        foreach (var phase in report.Phases.Where(static p => p.Outcome == PhaseOutcome.Failed))
        {
            findings.Add(new(FindingSeverity.Error, "Process", $"The {phase.Name} phase failed",
                phase.Error ?? "(no message)", "exceptions.log"));
        }

        // A page a browser shows in a second or two has nothing that should take ten here.
        foreach (var phase in report.Phases.Where(static p => p.Outcome == PhaseOutcome.Succeeded && p.DurationMs >= 10_000))
        {
            var hot = report.HotSpots.FirstOrDefault(h => h.Phases.Contains(phase.Name, StringComparer.Ordinal));
            findings.Add(new(FindingSeverity.Warning, "Process",
                string.Create(CultureInfo.InvariantCulture, $"The {phase.Name} phase took {phase.DurationMs / 1000:0.#} s"),
                hot is not null
                    ? $"most sampled in {hot.Frame} ({hot.Samples} sample(s))"
                    : "run again with --sample-stacks to see where the time goes",
                hot is not null ? "slow-phase-stacks.txt" : "report.md#phases"));
        }

        if (report.Document is { } document)
        {
            if (document.Status is >= 400)
            {
                findings.Add(new(FindingSeverity.Error, "Network", $"The document was answered with HTTP {document.Status}",
                    $"{document.FinalUrl} — what rendered is the server's error page", "network.json"));
            }

            if (document.Redirects.Count > 1)
            {
                findings.Add(new(FindingSeverity.Info, "Network", $"The document was redirected {document.Redirects.Count - 1} time(s)",
                    string.Join(" → ", document.Redirects), "network.json"));
            }
        }

        if (report.Html is { } html)
        {
            if (html.QuirksMode)
            {
                findings.Add(new(FindingSeverity.Warning, "HTML", "The document renders in quirks mode",
                    html.Doctype is null
                        ? "It has no doctype. Quirks mode changes table sizing, line heights and unitless lengths; add <!DOCTYPE html>."
                        : $"Its doctype, {html.Doctype}, selects quirks mode, which changes table sizing, line heights and unitless lengths.",
                    "report.md#html"));
            }

            // The parse errors whose repair changes what renders get a finding each; the rest are
            // counted. A missing doctype is the quirks-mode finding's, above.
            foreach (var (code, severity, title) in RenderingParseErrors)
            {
                if (html.ParseErrorsByCode.FirstOrDefault(c => c.Tag == code) is not { } counted)
                    continue;

                var first = html.ParseErrors.FirstOrDefault(e => e.Code == code);
                findings.Add(new(severity, "HTML", $"{counted.Count} {title}",
                    first is null ? code : $"first at {At(first)}: {first.Message}", "report.md#html"));
            }

            var otherParseErrors = html.ParseErrorsByCode
                .Where(static c => c.Tag != "missing-doctype" && !RenderingParseErrors.Any(r => r.Code == c.Tag))
                .ToArray();
            if (otherParseErrors.Length > 0)
            {
                findings.Add(new(FindingSeverity.Info, "HTML", $"{otherParseErrors.Sum(static c => c.Count)} other HTML parse error(s)",
                    string.Join(", ", otherParseErrors.Take(8).Select(static c => $"{c.Tag} ×{c.Count}")), "report.md#html"));
            }

            if (html.DuplicateIds.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "HTML", $"{html.DuplicateIds.Count} id(s) are used more than once",
                    string.Join(", ", html.DuplicateIds.Take(8).Select(static d => $"#{d.Tag} ×{d.Count}")) +
                    " — getElementById and #id selectors match only one of them",
                    "report.md#html"));
            }

            if (html.ObsoleteElements.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "HTML", $"{html.ObsoleteElements.Sum(static e => e.Count)} element(s) HTML lists as obsolete",
                    string.Join(", ", html.ObsoleteElements.Take(8).Select(static e => $"<{e.Tag}> ×{e.Count}")) +
                    " — browsers still render most of them, but not all alike, and not all the same way Broiler does",
                    "report.md#html"));
            }

            if (html.CustomElements.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "HTML", $"{html.CustomElements.Sum(static e => e.Count)} custom element(s)",
                    string.Join(", ", html.CustomElements.Take(8).Select(static e => $"<{e.Tag}> ×{e.Count}")) +
                    " — until a script defines them they are unstyled inline elements, and they stay so if that script failed",
                    "report.md#html"));
            }

            if (html.UnknownElements.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "HTML", $"{html.UnknownElements.Sum(static e => e.Count)} element(s) with tags HTML does not define",
                    string.Join(", ", html.UnknownElements.Take(8).Select(static e => $"<{e.Tag}> ×{e.Count}")) +
                    " — they render as inline elements with no default style",
                    "report.md#html"));
            }

            var failedScripts = html.Scripts.Where(static s => s.Load is { } load && (load.StartsWith("failed", StringComparison.Ordinal) || load.Contains("HTTP error", StringComparison.Ordinal))).ToArray();
            if (failedScripts.Length > 0)
            {
                findings.Add(new(FindingSeverity.Error, "JavaScript", $"{failedScripts.Length} script(s) could not be loaded",
                    string.Join("; ", failedScripts.Take(5).Select(static s => $"{s.Source} ({s.Load})")), "network.json"));
            }

            var missing = html.Stylesheets.Concat(html.Images).Concat(html.Frames)
                .Where(static r => r.Load is { } load && (load.Contains("NOT FOUND", StringComparison.Ordinal)
                    || load.StartsWith("failed", StringComparison.Ordinal) || load.Contains("HTTP error", StringComparison.Ordinal)))
                .ToArray();
            if (missing.Length > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "HTML", $"{missing.Length} stylesheet, image or frame reference(s) could not be loaded",
                    string.Join("; ", missing.Take(5).Select(static r => $"{r.Element} {r.Url} ({r.Load})")), "report.md#html"));
            }

            var skipped = html.Scripts.Where(static s => s.Kind.StartsWith("not run", StringComparison.Ordinal)).ToArray();
            if (skipped.Length > 0)
            {
                findings.Add(new(FindingSeverity.Info, "JavaScript", $"{skipped.Length} <script> element(s) have a type that is not run",
                    string.Join(", ", skipped.Select(static s => s.Kind).Distinct().Take(5)) +
                    " — a page that expects a library to compile them sees nothing happen", "report.md#html"));
            }
        }

        if (report.Scripting is { } scripting)
        {
            if (scripting.Failures > 0)
            {
                var first = scripting.DistinctFailures.FirstOrDefault();
                findings.Add(new(FindingSeverity.Error, "JavaScript",
                    $"{scripting.Failures} JavaScript failure(s), {scripting.DistinctFailures.Count} distinct",
                    first is null ? string.Empty : $"Most frequent (×{first.Count}): {AnalysisConsole.OneLine(first.Example, 240)}",
                    "javascript-errors.log"));
            }

            if (scripting.MissingApis.Count > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "JavaScript", $"{scripting.MissingApis.Count} platform feature(s) the page asked for and did not get",
                    string.Join(", ", scripting.MissingApis.Take(12).Select(static m => $"{m.Name} ({m.Kind}, ×{m.Count})")),
                    "report.md#javascript"));
            }

            if (scripting.UnhandledRejections > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "JavaScript", $"{scripting.UnhandledRejections} promise rejection(s) nobody handled",
                    "an async operation failed and nothing reported it; see javascript-errors.log", "javascript-errors.log"));
            }

            if (scripting.JavaScriptExceptions > 0)
            {
                findings.Add(new(FindingSeverity.Info, "JavaScript", $"{scripting.JavaScriptExceptions} exception(s) were thrown inside JavaScript",
                    "including those the page caught itself — a caught exception in feature detection is how a page quietly takes its fallback path",
                    "exceptions.log"));
            }

            if (scripting.LongTurns.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "JavaScript", $"{scripting.LongTurns.Count} long turn(s) or idle gap(s) in the script bridge's turn trace",
                    $"first: {AnalysisConsole.OneLine(scripting.LongTurns[0], 200)}", "report.md#javascript"));
            }

            if (scripting.SettleExhausted)
            {
                findings.Add(new(FindingSeverity.Warning, "JavaScript", "The load window did not settle",
                    "timers kept scheduling work due at once until the iteration budget ran out; the page was captured mid-work",
                    "messages.log"));
            }

            if (scripting.GeometryLayoutMs >= 1000)
            {
                findings.Add(new(FindingSeverity.Warning, "Layout",
                    string.Create(CultureInfo.InvariantCulture, $"Scripts waited {scripting.GeometryLayoutMs / 1000:0.#} s for layout"),
                    string.Create(CultureInfo.InvariantCulture,
                        $"{scripting.GeometryLayouts} geometry question(s) — getBoundingClientRect, offsetWidth and the like — each laid the whole page out, the slowest in {scripting.SlowestGeometryLayoutMs / 1000:0.#} s; a slow page layout stalls every script that measures"),
                    "report.md#javascript"));
            }

            if (scripting.NavigationNotFollowed is { } navigation)
            {
                findings.Add(new(FindingSeverity.Warning, "JavaScript", "The page asked to navigate away, and was not followed",
                    $"{navigation} — what rendered is the page before it left", "report.md#javascript"));
            }
        }

        if (report.Network is { } network && network.Failed > 0)
        {
            findings.Add(new(FindingSeverity.Warning, "Network", $"{network.Failed} of {network.Requests} request(s) failed",
                string.Join("; ", network.Failures.Take(5).Select(static f => $"{f.Destination} {f.Url} → {f.Error ?? f.Status?.ToString(CultureInfo.InvariantCulture) ?? f.BodyError}")),
                "network.json"));
        }

        if (report.Render is { } render)
        {
            if (render.Errors.Count > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "Render", $"The renderer reported {render.Errors.Count} error(s)",
                    string.Join("; ", render.Errors.GroupBy(static e => e.Kind).Select(static g => $"{g.Key} ×{g.Count()}")) +
                    $" — first: {AnalysisConsole.OneLine(render.Errors[0].Detail, 240)}", "report.md#render"));
            }

            if (render.GeometryError is { } geometry)
            {
                findings.Add(new(FindingSeverity.Info, "Layout", "Element geometry could not be taken", geometry, "exceptions.log"));
            }
        }

        // The window runs the page again with its own code. A page it shows differently from the
        // analysis's render points at how the window loads, styles or lays out the page, which none of
        // the other findings look at; a page that changes from one load to the next differs too, which
        // is why this is a lead and a warning, not an error.
        if (report.Window is { } window)
        {
            if (window.DifferenceRatio is { } difference && difference >= WindowDifferenceThreshold)
            {
                findings.Add(new(FindingSeverity.Warning, "Render", "The browser window shows this page differently from the analysis",
                    string.Create(CultureInfo.InvariantCulture,
                        $"{difference:P0} of the page area differs; compare {window.Image} with screenshot.png"),
                    window.Image));
            }

            if (!window.Settled)
            {
                findings.Add(new(FindingSeverity.Info, "Render", "The browser window did not finish loading the page",
                    $"its status was \"{window.Status}\" when its image was taken", window.Image));
            }
        }

        // Only when not one pixel differs: a script that writes one line of text changes well under a
        // tenth of a percent of a viewport, so any threshold above zero would accuse working scripts.
        if (report.ScriptVisualEffect is 0 && report.Scripting is { Failures: > 0 })
        {
            findings.Add(new(FindingSeverity.Warning, "JavaScript", "The page looks the same with and without its scripts",
                "its scripts failed and changed no pixel of the viewport; compare screenshot.png and screenshot-without-scripts.png",
                "screenshot-without-scripts.png"));
        }

        if (report.Layout is { } layout)
        {
            if (layout.InvariantViolations.Count > 0)
            {
                findings.Add(new(FindingSeverity.Error, "Layout", $"{layout.InvariantViolations.Count} layout invariant violation(s)",
                    $"Broiler.Layout's own checker: {AnalysisConsole.OneLine(layout.InvariantViolations[0], 240)}",
                    "layout/fragment-tree.txt"));
            }

            // No real page is this tall: the longest articles run to a few tens of thousands of pixels.
            if (layout.ContentHeight > 100_000)
            {
                findings.Add(new(FindingSeverity.Error, "Layout",
                    string.Create(CultureInfo.InvariantCulture, $"The page is laid out {layout.ContentHeight:N0}px tall"),
                    layout.TallerThanContent.Count > 0
                        ? $"that is a runaway height; it starts at {layout.TallerThanContent[0].Element} in {layout.TallerThanContent[0].Path} ({layout.TallerThanContent[0].Detail})"
                        : "that is a runaway height; see layout/fragment-tree.txt for the tallest boxes",
                    "layout/fragment-tree.txt"));
            }

            if (layout.TallerThanContent.Count > 0)
            {
                var first = layout.TallerThanContent[0];
                findings.Add(new(FindingSeverity.Warning, "Layout", $"{layout.TallerThanContent.Count} box(es) are far taller than their content",
                    $"tallest: {first.Element} in {first.Path}, {first.Detail}", "report.md#layout"));
            }

            if (layout.DumpErrors.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "Process", $"{layout.DumpErrors.Count} layout file(s) could not be written",
                    layout.DumpErrors[0], "report.md#layout"));
            }

            if (layout.HorizontalOverflow.Count > 0)
            {
                var first = layout.HorizontalOverflow[0];
                findings.Add(new(FindingSeverity.Warning, "Layout", $"{layout.HorizontalOverflow.Count} element(s) reach past the right edge of the viewport",
                    $"widest: {first.Element} in {first.Path}, {first.Detail}", "screenshot-boxes.png"));
            }

            if (layout.CollapsedWithText.Count > 0)
            {
                var first = layout.CollapsedWithText[0];
                findings.Add(new(FindingSeverity.Warning, "Layout", $"{layout.CollapsedWithText.Count} element(s) with text were laid out with no width or height",
                    $"first: {first.Element} in {first.Path}, {first.Detail} — a visually hidden label is laid out this way on purpose; text a reader should see is not",
                    "report.md#layout"));
            }

            if (layout.OffPage.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "Layout", $"{layout.OffPage.Count} element(s) are placed entirely off the page",
                    $"often a deliberately hidden skip link; first: {layout.OffPage[0].Element} at {layout.OffPage[0].Box}", "report.md#layout"));
            }
        }

        if (report.Css is { } css)
        {
            if (css.ParseProblems.Count > 0)
            {
                var first = css.ParseProblems[0];
                findings.Add(new(FindingSeverity.Warning, "CSS", $"{css.ParseProblems.Count} CSS parse problem(s)",
                    $"first: {first.Source} {first.Line}:{first.Column} {first.Code} {first.Message} — `{first.Excerpt}`", "report.md#css"));
            }

            if (css.RejectedDuringCascade.Count > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "CSS", $"{css.RejectedDuringCascade.Sum(static r => r.Count)} declaration(s) dropped by the style engine while it cascaded",
                    string.Join(", ", css.RejectedDuringCascade.Take(8).Select(static r => $"{CssInspector.Describe(r)} ×{r.Count}")), "report.md#css"));
            }
            else if (css.RejectedValues.Count > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "CSS", $"{css.RejectedValues.Count} value(s) the style engine does not accept",
                    string.Join(", ", css.RejectedValues.Take(8).Select(static r => $"{CssInspector.Describe(r)} ×{r.Count}")), "report.md#css"));
            }

            if (css.UnknownProperties.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "CSS", $"{css.UnknownProperties.Count} property name(s) the CSS engine does not know",
                    string.Join(", ", css.UnknownProperties.Take(12).Select(static u => $"{u.Property} ×{u.Count}")), "report.md#css"));
            }

            SelectorGapFinding(findings, css, "guessed", FindingSeverity.Warning, "pseudo-classes Broiler.CSS guesses at",
                "each matches every element, so its declarations reach elements a browser leaves alone");
            SelectorGapFinding(findings, css, "not modeled", FindingSeverity.Info, "pseudo-classes Broiler.CSS never matches where a browser can",
                "their rules apply to nothing here");
            SelectorGapFinding(findings, css, "unstyled pseudo-element", FindingSeverity.Info, "pseudo-elements Broiler does not render",
                "their rules reach nothing");
            SelectorGapFinding(findings, css, "invalid", FindingSeverity.Info, "pseudo-classes no browser supports",
                "a browser drops each whole rule; Broiler drops only these selectors, so the rest of a selector list still applies");

            var notDrawn = css.NotAppliedByLayout.Where(static u => CssInspector.AffectsAStillImage(u.Property)).ToArray();
            if (notDrawn.Length > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "Layout", $"{notDrawn.Length} CSS propert(ies) Broiler's layout does not apply",
                    string.Join(", ", notDrawn.Take(10).Select(static u => $"{CssInspector.Describe(u)} ×{u.Count}")) +
                    " — the style engine accepted them and the layout engine ignored them", "report.md#css"));
            }

            if (css.LayoutFallbacks.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "Layout", $"Broiler's layout approximated {css.LayoutFallbacks.Count} feature(s)",
                    string.Join(", ", css.LayoutFallbacks.Take(6).Select(static u => $"{CssInspector.Describe(u)} ×{u.Count}")), "report.md#css"));
            }

            var fallbacks = css.Fonts.Where(static f => f.Unavailable.Count > 0).ToArray();
            if (fallbacks.Length > 0)
            {
                findings.Add(new(FindingSeverity.Warning, "Render", $"{fallbacks.Length} font-family list(s) start with a font that is not available",
                    string.Join("; ", fallbacks.Take(5).Select(static f =>
                        $"{string.Join(", ", f.Unavailable)} → {(f.ResolvedFamily ?? "no listed font")} ({f.TextRuns} text run(s))")) +
                    " — text in a different face wraps differently and moves everything after it", "report.md#fonts"));
            }

            if (css.UnknownAtRules.Count > 0)
            {
                findings.Add(new(FindingSeverity.Info, "CSS", $"{css.UnknownAtRules.Count} at-rule(s) CSS does not define",
                    string.Join(", ", css.UnknownAtRules.Select(static a => "@" + a)), "report.md#css"));
            }
        }

        if (report.Exceptions is { WithoutStackRoom: > 0 } starved)
        {
            findings.Add(new(FindingSeverity.Warning, "JavaScript", $"{starved.WithoutStackRoom} exception(s) were thrown with the stack nearly exhausted",
                "a script recursed until the stack ran out (\"Maximum call stack size exceeded\" in javascript-errors.log); they are counted, not recorded",
                "exceptions.json"));
        }

        if (report.Exceptions is { Total: > 0 } exceptions)
        {
            var hostSide = exceptions.Top.Where(static e => !e.Type.StartsWith("Broiler.JavaScript", StringComparison.Ordinal)).ToArray();
            if (hostSide.Length > 0)
            {
                var first = hostSide[0];
                findings.Add(new(FindingSeverity.Info, "Process", $"{exceptions.Total} exception(s) in the process, first-chance included",
                    $"most frequent outside JavaScript: {first.Type} ×{first.Count} in {first.Site} — {AnalysisConsole.OneLine(first.FirstMessage, 200)}",
                    "exceptions.log"));
            }
        }

        return [.. findings.OrderBy(static f => f.Severity)];
    }

    private static string At(HtmlParseProblem error) => error.Line is { } line
        ? string.Create(CultureInfo.InvariantCulture, $"line {line}, column {error.Column}")
        : "an unknown position";

    private static void SelectorGapFinding(
        List<Finding> findings,
        CssReport css,
        string kind,
        FindingSeverity severity,
        string what,
        string consequence)
    {
        var gaps = css.SelectorGaps.Where(g => g.Kind == kind).ToArray();
        if (gaps.Length == 0)
            return;

        findings.Add(new(severity, "CSS", $"{gaps.Sum(static g => g.Selectors)} selector(s) use {what}",
            string.Join(", ", gaps.Take(6).Select(static g => $"{g.Part} ×{g.Selectors} (`{AnalysisConsole.OneLine(g.Example, 60)}`, {g.FirstSource})")) +
            $" — {consequence}", "report.md#css"));
    }
}
