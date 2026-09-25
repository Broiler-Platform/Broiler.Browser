using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.CSS.Dom;
using Broiler.HtmlBridge.Logging;
using Broiler.HtmlBridge.Scripting;
using Broiler.Layout.IR;
using Broiler.HTML.Core.IR;

namespace Broiler.Cli.Analysis;

// Disambiguate the unqualified `DateTime` type: the Broiler.JS engine exposes a top-level
// `Broiler.DateTime` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.DateTime by simple-name lookup.
using DateTime = System.DateTime;

/// <summary>
/// <c>--analyze</c>: loads one page the way a capture does, runs it on Broiler.JS, renders it, and
/// writes everything the run produced and everything the analysis concluded into one directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> A page that renders wrong in Broiler can be wrong for a reason in any of five
/// places — its markup, its stylesheets, its scripts, the layout, or the network — and the symptom, a
/// wrong picture, looks the same whichever it is. The analysis keeps the evidence from all five in
/// one run and ranks what it found, so the question "where do I start" has an answer on the first
/// page of the report.
/// </para>
/// <para>
/// <b>Broiler.JS, always.</b> The capture commands run a page on the engine the build configuration
/// picks, which under <c>Debug-VM</c>/<c>Release-VM</c> puts the Broiler.VM JavaScript profile in front
/// of Broiler.JS. The analysis composes Broiler.JS directly (<see cref="HeadlessBrowserOptions.BroilerJsOnly"/>)
/// in every configuration, so its findings are about one engine, and the report names it.
/// </para>
/// <para>
/// <b>Every phase can fail on its own.</b> The pages worth analysing are the ones that break things,
/// so a phase that throws is recorded and the phases that do not need its result still run: a page
/// whose scripts crash still gets its screenshot without scripts, its markup and stylesheet analysis,
/// and its logs. Only a document that could not be fetched at all ends the analysis early.
/// </para>
/// <para>
/// <b>A watchdog bounds the whole run.</b> Nothing below the command line bounds a script that loops or
/// a layout that does not terminate. When the watchdog fires it writes what is known — the phase that
/// was running, the requests still open, the last exceptions — disposes the bundle so every log and
/// manifest is complete up to that moment, and ends the process with exit code 3.
/// </para>
/// </remarks>
internal sealed class PageAnalyzer
{
    /// <summary>Exit code: the analysis ran to the end. Whatever the page did is in the report.</summary>
    public const int Completed = 0;

    /// <summary>Exit code: the analysis could not start, or the document could not be fetched.</summary>
    public const int Failed = 1;

    /// <summary>Exit code: the watchdog ended the analysis.</summary>
    public const int WatchdogExit = 3;

    // Who writes the reports: the run when it gets to them, or the watchdog when it fires first.
    private const int RunReports = 1;
    private const int WatchdogReports = 2;

    /// <summary>How many first-chance exceptions the console prints before it only counts them.</summary>
    private const int MaxConsoleExceptions = 300;

    /// <summary>How many rejected declarations are kept; the style engine can report one per element.</summary>
    private const int MaxRejectedDeclarations = 100_000;

    /// <summary>
    /// A geometry request slower than this was a layout of the document rather than an answer from
    /// the layout view's snapshot.
    /// </summary>
    private const double LayoutQueryThresholdMs = 10;

    private readonly PageAnalysisOptions _options;
    private readonly AnalysisConsole _console;
    private readonly PhaseClock _phases;
    private readonly Action<int> _exit;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly List<AnalysisFile> _files = [];

    // A field rather than a local so the watchdog can report what the phases had produced when it fired.
    private readonly RunState _state = new();
    private int _consoleExceptions;

    // 0 until the run or the watchdog claims the reports; the other then stands aside.
    private int _reporter;
    private readonly ManualResetEventSlim _watchdogDone = new();

    /// <param name="options">What to analyse, and how.</param>
    /// <param name="output">Where the console lines go; standard output when null.</param>
    /// <param name="exit">
    /// What the watchdog calls to end the process — <see cref="Environment.Exit"/>, unless a test
    /// needs to watch it fire without losing its own process.
    /// </param>
    public PageAnalyzer(PageAnalysisOptions options, TextWriter? output = null, Action<int>? exit = null)
    {
        _options = options;
        _console = new AnalysisConsole(options.Verbose, output);
        _phases = new PhaseClock(_console);
        _exit = exit ?? Environment.Exit;
    }

    /// <summary>Runs the analysis and returns the process exit code.</summary>
    public async Task<int> RunAsync()
    {
        var output = Path.GetFullPath(_options.OutputDirectory);
        Directory.CreateDirectory(output);
        RemovePreviousAnalysis(output);
        var previousTurnTrace = EnableJavaScriptTurnTrace();
        _console.Line($"analyzing {_options.Url}");
        _console.Line($"output    {output}");
        _console.Line($"engine    Broiler.JS, viewport {_options.Width}×{_options.Height}");

        var bundle = DiagnosticSession.Start(new DiagnosticOptions
        {
            Directory = output,
            LogPath = Path.Combine(output, "javascript-errors.log"),
            Phase = () => _phases.Current,
            WriteSummary = false,
        })!;

        var rejected = new ConcurrentQueue<(string Property, string Value)>();
        var previousRejectionHook = CssEngineDiagnostics.DeclarationRejected;
        CssEngineDiagnostics.DeclarationRejected = (property, value) =>
        {
            if (rejected.Count < MaxRejectedDeclarations)
                rejected.Enqueue((property, value));
            previousRejectionHook?.Invoke(property, value);
        };

        var network = new NetworkRecorder((entry, bytes) => bundle.ArchiveBytes(
            entry.FinalUrl ?? entry.Url,
            ResourceKindFor(entry.Destination),
            bytes,
            entry.ContentType,
            entry.Status,
            entry.Method,
            entry.HeadersMs));

        var layouts = new LayoutQueryRecorder(() => _phases.ElapsedMs, () => _phases.Current);

        Action<RenderLogEntry> onLog = OnLogEntry;
        RenderLogger.EntryLogged += onLog;
        network.Responded += OnResponse;
        layouts.Completed += OnLayoutQuery;
        if (bundle.Exceptions is { } exceptions)
            exceptions.Recorded += OnException;

        using var watchdog = StartWatchdog(output, bundle, network);
        var sampler = _options.SampleStacks
            ? StackSampler.Start(Path.Combine(output, "slow-phase-stacks.txt"), _phases, _console, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4))
            : null;
        var state = _state;
        try
        {
            await RunPhasesAsync(output, bundle, network, layouts, rejected, state).ConfigureAwait(false);
        }
        finally
        {
            RenderLogger.EntryLogged -= onLog;
            CssEngineDiagnostics.DeclarationRejected = previousRejectionHook;
            sampler?.Dispose();

            // The trace read the variable when it started; leaving it set would hand it to every
            // process this one starts later.
            Environment.SetEnvironmentVariable(TurnTraceVariable, previousTurnTrace);
        }

        // The watchdog may have fired while the phases were ending, and be writing the reports now.
        // Two writers would share every file and the exit would cut one of them off mid-write, so
        // the first to claim the reports writes them. Having lost, this thread writes nothing and
        // waits for the watchdog to end the process — or, under a test's stand-in exit, to finish.
        if (Interlocked.CompareExchange(ref _reporter, RunReports, 0) != 0)
        {
            _watchdogDone.Wait();
            return WatchdogExit;
        }

        if (sampler is not null)
        {
            state.HotSpots = sampler.HotSpots();
            AddFile("slow-phase-stacks.txt", $"{sampler.Samples} stack sample(s) taken while a phase ran long, and the frames they caught most");
        }

        // The bundle is finished before the report, so that the report counts every exception and
        // every archived resource — the last of them arrive while the page is torn down.
        bundle.Dispose();

        var report = _phases.Run("report", () => WriteReports(output, bundle, network, state, watchdogFired: null));
        watchdog?.Dispose();

        _console.Line(bundle.Describe());
        if (report is not null)
            PrintSummary(report, output);

        return state.Page is null ? Failed : Completed;
    }

    /// <summary>What the phases produced, for the report.</summary>
    private sealed class RunState
    {
        public LoadedPage? Page;
        public ScriptingSummary? Scripting;
        public RenderResult? Render;
        public RenderResult? RenderWithoutScripts;
        public double? ScriptVisualEffect;
        public LayoutReport? Layout;
        public HtmlReport? Html;
        public CssReport? Css;
        public IReadOnlyList<HotSpot> HotSpots = [];
    }

    private async Task RunPhasesAsync(
        string output,
        DiagnosticSession bundle,
        NetworkRecorder network,
        LayoutQueryRecorder layouts,
        ConcurrentQueue<(string Property, string Value)> rejected,
        RunState state)
    {
        var profiler = new ScriptProfilingHook();
        using var browser = new HeadlessBrowser(
            TimeSpan.FromSeconds(_options.TimeoutSeconds),
            new HeadlessBrowserOptions
            {
                WrapNetwork = network.Wrap,
                BroilerJsOnly = true,
                Profiler = profiler,
                WrapLayoutView = layouts.Wrap,
            });

        var page = await _phases.RunAsync(
            "load",
            () => browser.LoadAsync(_options.Url, _options.FollowFirstLink),
            static p => string.Create(
                CultureInfo.InvariantCulture,
                $"{p.Response.StatusCode} {p.Response.GetHeaderValues("Content-Type").FirstOrDefault() ?? "(no content type)"}, " +
                $"{Encoding.UTF8.GetByteCount(p.Content.Html):N0} bytes from {p.FinalUrl}; " +
                $"{p.Content.Scripts.Count} classic, {p.Content.DeferredScripts.Count} deferred, {p.Content.ModuleRoots.Count} module script(s)")).ConfigureAwait(false);
        state.Page = page;
        if (page is null)
        {
            foreach (var phase in (string[])["scripts", "settle", "render", "render-without-scripts", "layout", "html", "css"])
                _phases.Skip(phase, "the document could not be loaded");
            return;
        }

        WriteText(output, "document-as-fetched.html", page.Content.Html, "the document as the server sent it");

        using var scripted = _phases.Run(
            "scripts",
            () => browser.Start(page),
            static s => s.RanScripts ? "the page's scripts ran and its load event fired" : "the page has no scripts");

        string? afterScripts = null;
        if (scripted is not null)
        {
            _phases.Run(
                "settle",
                () => scripted.Settle(),
                () => scripted.AsyncDrainLimitExhausted
                    ? "stopped at the iteration budget with work still due"
                    : scripted.HasPendingWork ? "settled; timers remain queued beyond the load window" : "settled");

            state.Scripting = Scripting(page, scripted, profiler, bundle, layouts);
            afterScripts = _phases.Run("serialize", scripted.Serialize, static h => string.Create(CultureInfo.InvariantCulture, $"{Encoding.UTF8.GetByteCount(h):N0} bytes after scripts"));
        }

        afterScripts ??= page.Content.Html;
        WriteText(output, "document-after-scripts.html", afterScripts, "the document as its scripts left it");

        var rendered = HtmlPostProcessor.ProcessForBrowsing(afterScripts);
        WriteText(output, "document-as-rendered.html", rendered, "what the renderer was given: scripts, noscript and iframe fallback removed");

        state.Render = _phases.Run(
            "render",
            () => RenderProbe.Render(
                rendered,
                page.FinalUrl,
                browser.Network,
                page.Document,
                _options.Width,
                _options.Height,
                output,
                "screenshot",
                fullPage: true,
                outlineBoxes: true,
                elementGeometry: true,
                _options.MaxFullPageHeight),
            static r => string.Create(
                CultureInfo.InvariantCulture,
                $"content {r.ContentSize.Width:0}×{r.ContentSize.Height:0}, {r.Errors.Count} render error(s), " +
                $"{r.StylesheetRequests.Count} stylesheet and {r.ImageRequests.Count} image request(s)"));
        if (state.Render is { } render)
        {
            AddFile(render.ViewportImage, $"the viewport, {_options.Width}×{_options.Height}, after scripts");
            AddFile(render.FullPageImage, "the whole page, after scripts");
            AddFile(render.BoxesImage, "the viewport with every layout box outlined; red boxes reach past the right edge");
        }

        state.RenderWithoutScripts = _phases.Run(
            "render-without-scripts",
            () => RenderProbe.Render(
                HtmlPostProcessor.ProcessForBrowsing(page.Content.Html),
                page.FinalUrl,
                browser.Network,
                page.Document,
                _options.Width,
                _options.Height,
                output,
                "screenshot-without-scripts",
                fullPage: false,
                outlineBoxes: false,
                elementGeometry: false,
                _options.MaxFullPageHeight),
            static r => $"{r.Errors.Count} render error(s)");
        if (state.RenderWithoutScripts is { } bare)
        {
            AddFile(bare.ViewportImage, "the viewport rendered from the document as fetched, before any script ran");
            if (state.Render?.Viewport is { } withScripts && bare.Viewport is { } withoutScripts)
                state.ScriptVisualEffect = ScriptEffect(RenderProbe.DifferenceRatio(withScripts, withoutScripts));
        }

        // The document the scripts left, parsed back from its serialization. The session can hand out
        // its render document instead, but that is a deep copy with the bridge's own bookkeeping, and
        // on a large page it cost more than the whole HTML analysis; the markup carries what is read here.
        var afterScriptsDocument = _phases.Run("parse", () => HtmlInspector.Parse(afterScripts));
        state.Layout = _phases.Run(
            "layout",
            () => Layout(output, state.Render),
            static l => string.Create(CultureInfo.InvariantCulture, $"{l.Boxes:N0} boxes, {l.HorizontalOverflow.Count} overflowing, {l.CollapsedWithText.Count} collapsed with text"));

        state.Html = _phases.Run(
            "html",
            () => HtmlInspector.Inspect(page.Content.Html, afterScriptsDocument, network.Snapshot(), page.FinalUrl),
            static h => string.Create(CultureInfo.InvariantCulture, $"{h.ElementsAsFetched:N0} elements as fetched, {h.ElementsAfterScripts:N0} after scripts{(h.QuirksMode ? ", QUIRKS MODE" : string.Empty)}"));

        state.Css = _phases.Run(
            "css",
            () => Css(output, bundle, network, afterScriptsDocument ?? HtmlInspector.Parse(page.Content.Html), rejected, state.Layout),
            static c => $"{c.Sheets.Count} stylesheet source(s), {c.ParseProblems.Count} parse problem(s), {c.UnknownProperties.Count} unknown propert(ies)");

        // Held until the pending navigation has been read, which disposing the session would lose.
        state.Scripting = state.Scripting is { } scripting
            ? scripting with { NavigationNotFollowed = Describe(scripted?.TakePendingNavigation()) }
            : null;
    }

    // ── Phases ──────────────────────────────────────────────────────────────────────────────────

    private ScriptingSummary Scripting(
        LoadedPage page,
        ScriptedPage scripted,
        ScriptProfilingHook profiler,
        DiagnosticSession bundle,
        LayoutQueryRecorder layouts)
    {
        var queries = layouts.Snapshot();
        var entries = bundle.Entries();
        var failures = entries
            .Where(static e => e.Category == LogCategory.JavaScript && e.Level >= LogLevel.Warning && !e.Context.StartsWith("console.", StringComparison.Ordinal))
            .ToArray();
        var messages = failures.Select(static e => e.Message).ToArray();

        return new ScriptingSummary
        {
            ClassicScripts = page.Content.Scripts.Count,
            DeferredScripts = page.Content.DeferredScripts.Count,
            ModuleRoots = page.Content.ModuleRoots.Count,
            Timings = [.. profiler.Entries.Select(static t => new ScriptTiming(t.Label, Math.Round(t.Elapsed.TotalMilliseconds, 2), t.Succeeded))],
            Failures = failures.Length,
            DistinctFailures = JsErrorDigest.Group(messages, [.. failures.Select(static e => e.Context)]),
            MissingApis = JsErrorDigest.MissingApis(messages),
            UnhandledRejections = failures.Count(static e => e.Context == "CaptureService.UnhandledRejection"),
            Console = entries
                .Where(static e => e.Context.StartsWith("console.", StringComparison.Ordinal))
                .GroupBy(static e => e.Context)
                .ToDictionary(static g => g.Key, static g => g.Count()),
            JavaScriptExceptions = bundle.Exceptions?.Signatures()
                .Where(static s => s.Type.StartsWith("Broiler.JavaScript", StringComparison.Ordinal))
                .Sum(static s => (long)s.Count) ?? 0,
            SettleExhausted = scripted.AsyncDrainLimitExhausted,
            PendingWorkAfterSettle = scripted.HasPendingWork,
            GeometryLayouts = queries.Count(static q => q.Ms >= LayoutQueryThresholdMs),
            GeometryRequests = queries.Count,
            GeometryLayoutMs = Math.Round(queries.Sum(static q => q.Ms), 1),
            SlowestGeometryLayoutMs = queries.Count == 0 ? 0 : queries.Max(static q => q.Ms),
            LongTurns = [.. entries
                .Where(static e => e.Context == "JsEntryTrace" && !e.Message.StartsWith("enabled", StringComparison.Ordinal))
                .Select(static e => e.Message.Trim())
                .Take(40)],
        };
    }

    private LayoutReport Layout(string output, RenderResult? render)
    {
        if (render?.Fragments is not { } fragments)
            return new LayoutReport();

        var directory = Path.Combine(output, "layout");
        Directory.CreateDirectory(directory);
        var dumpErrors = new List<string>();

        // Each file on its own: a dumper that cannot handle this page — the JSON dumpers stop at the
        // serializer's default depth, which a real page's box tree exceeds — must cost only its file.
        void Dump(string name, string description, Action<string> write)
        {
            try
            {
                write(Path.Combine(directory, name));
                AddFile("layout/" + name, description);
            }
            catch (Exception ex)
            {
                dumpErrors.Add($"layout/{name}: {ex.GetType().FullName}: {ExceptionText.SafeMessage(ex)}");
            }
        }

        Dump("fragment-tree.txt", "the layout as indented text: one box per line with its tag, display, geometry and first text",
            path => LayoutInspector.WriteFragmentTree(fragments, path));
        Dump("fragments.json", "the fragment tree as Broiler.Layout dumps it",
            path => File.WriteAllText(path, FragmentJsonDumper.ToJson(fragments)));
        Dump("computed-styles.json", "every box's computed style, as the renderer resolved it",
            path => File.WriteAllText(path, ComputedStyleJsonDumper.ToJson(fragments)));
        if (render.DisplayList is { } displayList)
        {
            Dump("display-list.json", "the paint commands the viewport image was drawn from",
                path => File.WriteAllText(path, DisplayListJsonDumper.ToJson(displayList)));
        }

        if (render.InvariantViolations.Count > 0)
        {
            Dump("invariant-violations.txt", "what Broiler.Layout's invariant checker found wrong with the fragment tree",
                path => File.WriteAllLines(path, render.InvariantViolations));
        }

        return LayoutInspector.Inspect(
            fragments,
            render.InvariantViolations,
            render.ContentSize,
            render.ElementGeometry,
            render.GeometryDocument,
            render.Width,
            render.Height) with { DumpErrors = dumpErrors };
    }

    private CssReport Css(
        string output,
        DiagnosticSession bundle,
        NetworkRecorder network,
        Broiler.Dom.DomDocument document,
        ConcurrentQueue<(string Property, string Value)> rejected,
        LayoutReport? layout)
    {
        var resources = Path.Combine(output, "resources");
        var sheets = new List<StylesheetSource>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in network.Snapshot())
        {
            if (entry.Destination != "style" || entry.SavedAs is not { } savedAs || !seen.Add(entry.FinalUrl ?? entry.Url))
                continue;

            sheets.Add(new StylesheetSource(entry.FinalUrl ?? entry.Url, File.ReadAllText(Path.Combine(resources, savedAs)), savedAs));
        }

        // Sheets the script bridge fetched itself — for the CSSOM, or inserted by a script — which the
        // renderer may never have asked for.
        foreach (var resource in bundle.Resources())
        {
            if (resource.Kind != "Stylesheet" || resource.SavedAs is not { } savedAs || !seen.Add(resource.Url))
                continue;

            sheets.Add(new StylesheetSource(resource.Url, File.ReadAllText(Path.Combine(resources, savedAs)), savedAs));
        }

        var inline = HtmlInspector.StyleElements(document);
        for (var i = 0; i < inline.Count; i++)
            sheets.Add(new StylesheetSource($"<style> #{i + 1}", inline[i], null));

        return CssInspector.Inspect(
            sheets,
            HtmlInspector.StyleAttributes(document),
            [.. rejected],
            layout?.FontFamilies ?? new Dictionary<string, int>());
    }

    // ── Reports ─────────────────────────────────────────────────────────────────────────────────

    private AnalysisReport WriteReports(
        string output,
        DiagnosticSession bundle,
        NetworkRecorder network,
        RunState state,
        string? watchdogFired)
    {
        var requests = network.Snapshot();
        AnalysisJson.Write(Path.Combine(output, "network.json"), requests);
        AnalysisJson.Write(
            Path.Combine(output, "network.har"),
            NetworkRecorder.ToHar(requests, _options.Url, _startedAt, typeof(PageAnalyzer).Assembly.GetName().Version?.ToString() ?? "0"));

        var report = BuildReport(bundle, requests, state, watchdogFired);
        AnalysisJson.Write(Path.Combine(output, "report.json"), report);
        File.WriteAllText(Path.Combine(output, "report.md"), MarkdownReport.Write(report));
        File.WriteAllText(Path.Combine(output, "report.html"), HtmlReportPage.Write(report));
        return report;
    }

    private AnalysisReport BuildReport(
        DiagnosticSession bundle,
        IReadOnlyList<NetworkEntry> requests,
        RunState state,
        string? watchdogFired)
    {
        var page = state.Page;
        var exceptions = bundle.Exceptions?.Signatures() ?? [];
        var document = page is null
            ? null
            : new DocumentSummary(
                _options.Url,
                page.FinalUrl,
                page.Response.StatusCode,
                page.Response.GetHeaderValues("Content-Type").FirstOrDefault(),
                Encoding.UTF8.GetByteCount(page.Content.Html),
                requests.FirstOrDefault(static r => r.Destination == "document")?.Redirects ?? []);

        var report = new AnalysisReport
        {
            Url = _options.Url,
            StartedAt = _startedAt,
            DurationMs = Math.Round(_phases.ElapsedMs, 1),
            Viewport = string.Create(CultureInfo.InvariantCulture, $"{_options.Width}×{_options.Height}"),
            Completed = watchdogFired is null && page is not null,
            WatchdogFired = watchdogFired,
            Environment = AnalysisEnvironment.Capture(),
            Document = document,
            Phases = _phases.Snapshot(),
            Scripting = state.Scripting,
            Network = Network(requests),
            Render = state.Render is { } render ? RenderSummary.From(render) : null,
            RenderWithoutScripts = state.RenderWithoutScripts is { } bare ? RenderSummary.From(bare) : null,
            ScriptVisualEffect = state.ScriptVisualEffect,
            Layout = state.Layout,
            Html = state.Html,
            Css = state.Css,
            HotSpots = state.HotSpots,
            Exceptions = new ExceptionSummary(
                exceptions.Sum(static e => (long)e.Count),
                exceptions.GroupBy(static e => e.Kind).ToDictionary(static g => g.Key, static g => g.Sum(static e => e.Count)),
                exceptions.GroupBy(static e => e.Component)
                    .OrderByDescending(static g => g.Sum(static e => e.Count))
                    .ToDictionary(static g => g.Key, static g => g.Sum(static e => e.Count)),
                [.. exceptions.Take(40)],
                bundle.Exceptions?.Rethrows ?? 0,
                bundle.Exceptions?.WithoutStackRoom ?? 0),
            Files = Files(),
        };

        return report with { Findings = Triage.Rank(report) };
    }

    private static NetworkSummary Network(IReadOnlyList<NetworkEntry> requests) => new()
    {
        Requests = requests.Count,
        Failed = requests.Count(static r => r.IsFailure),
        Bytes = requests.Sum(static r => r.BodyBytes),
        ByDestination = requests
            .GroupBy(static r => r.Destination)
            .OrderByDescending(static g => g.Count())
            .ToDictionary(static g => g.Key, static g => g.Count()),
        Failures = [.. requests.Where(static r => r.IsFailure).Take(40)],
        Slowest = [.. requests.OrderByDescending(static r => r.CompleteMs ?? r.HeadersMs).Take(10)],
        NeverRead = requests.Count(static r => r.Status is not null && r.Body == BodyState.NotRead),
        StillPending = requests.Count(static r => r.Status is null && r.Error is null),
    };

    private IReadOnlyList<AnalysisFile> Files()
    {
        var files = new List<AnalysisFile>
        {
            new("report.html", "this report as a page, with the screenshots"),
            new("report.md", "this report as Markdown"),
            new("report.json", "everything in this report, machine-readable"),
            new("exceptions.log", "every exception raised during the run, first-chance ones included, with stacks"),
            new("exceptions.json", "the exceptions grouped by type and throw site, with counts and phases"),
            new("javascript-errors.log", "every JavaScript failure, as it happened, with its stack"),
            new("console.log", "the page's console output"),
            new("messages.log", "every message the pipeline logged, at every level"),
            new("network.json", "every request: destination, status, timing, redirects, headers, body file"),
            new("network.har", "the same requests as an HTTP Archive, which browser developer tools open"),
            new("resources/", "every document, script, stylesheet, image, font and fetch response, with resources/index.json"),
            new("diagnostics.json", "every log entry, and the JavaScript failures grouped"),
        };

        lock (_files)
            files.AddRange(_files);

        return files;
    }

    private void PrintSummary(AnalysisReport report, string output)
    {
        _console.Line(string.Empty);
        _console.Line($"{report.Findings.Count} finding(s):");
        foreach (var finding in report.Findings.Take(15))
            _console.Line($"  {finding.Severity.ToString().ToUpperInvariant(),-7} {finding.Area,-10} {finding.Title}");

        if (report.Findings.Count > 15)
            _console.Line($"  … and {report.Findings.Count - 15} more in the report");

        _console.Line($"report    {Path.Combine(output, "report.html")}");
    }

    // ── Watchdog ────────────────────────────────────────────────────────────────────────────────

    private Timer? StartWatchdog(string output, DiagnosticSession bundle, NetworkRecorder network)
    {
        if (_options.Watchdog is not { } limit)
            return null;

        return new Timer(_ => FireWatchdog(output, bundle, network, limit), null, limit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Writes what is known about a run that did not finish, completes the bundle, and ends the process.
    /// Runs on a timer thread while the analysis is stuck somewhere, so it reads only what is safe to
    /// read concurrently — the phase clock, the network snapshot, the exception signatures — and
    /// touches nothing the stuck thread holds.
    /// </summary>
    private void FireWatchdog(string output, DiagnosticSession bundle, NetworkRecorder network, TimeSpan limit)
    {
        // The run got to its reports first; it writes them and ends on its own.
        if (Interlocked.CompareExchange(ref _reporter, WatchdogReports, 0) != 0)
            return;

        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"stopped by the watchdog after {limit.TotalSeconds:0} s, in the {_phases.Current} phase");
        try
        {
            _console.Line($"WATCHDOG  {message}; writing what there is and exiting");

            var open = network.Snapshot().Where(static r => r.Status is null && r.Error is null).ToArray();
            var text = new StringBuilder()
                .AppendLine("# The analysis did not finish").AppendLine()
                .AppendLine($"It was {message}. Nothing below the command line bounds a script that loops or a layout that does not end, so the watchdog did.").AppendLine()
                .AppendLine("## Phases so far").AppendLine();
            foreach (var phase in _phases.Snapshot())
                text.AppendLine($"- {phase.Name}: {phase.Outcome} after {phase.DurationMs:0} ms{(phase.Error is null ? string.Empty : " — " + phase.Error)}");
            text.AppendLine($"- {_phases.Current}: still running").AppendLine();

            text.AppendLine($"## Requests still open ({open.Length})").AppendLine();
            foreach (var request in open.Take(50))
                text.AppendLine($"- {request.Destination} {request.Url}");

            text.AppendLine().AppendLine("## Most frequent exceptions so far").AppendLine();
            foreach (var signature in bundle.Exceptions?.Signatures().Take(15) ?? [])
                text.AppendLine($"- ×{signature.Count} {signature.Type} in {signature.Site}: {AnalysisConsole.OneLine(signature.FirstMessage, 200)}");

            File.WriteAllText(Path.Combine(output, "watchdog.md"), text.ToString());
            bundle.Dispose();
            WriteReports(output, bundle, network, _state, message);
        }
        catch (Exception)
        {
            // Whatever could be written was; the exit below is the point.
        }
        finally
        {
            _exit(WatchdogExit);

            // Reached only when the exit is a test's stand-in: the run, which lost the reports to
            // this thread, may go on.
            _watchdogDone.Set();
        }
    }

    // ── Console events ──────────────────────────────────────────────────────────────────────────

    private void OnLogEntry(RenderLogEntry entry)
    {
        if (!_console.Verbose)
            return;

        if (entry.Context.StartsWith("console.", StringComparison.Ordinal))
            _console.Event("console", $"{entry.Context[8..]}: {entry.Message}");
        else if (entry.Level >= LogLevel.Warning)
            _console.Event(entry.Category == LogCategory.JavaScript ? "js" : "engine", $"{entry.Level} {entry.Context}: {entry.Message}");
    }

    private void OnResponse(NetworkEntry entry)
    {
        if (!_console.Verbose)
            return;

        var outcome = entry.Error is { } error
            ? $"FAILED ({entry.ErrorKind}) {error}"
            : string.Create(CultureInfo.InvariantCulture, $"{entry.Status} {entry.ContentType ?? string.Empty}");
        _console.Event("network", string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Method} {entry.Destination,-8} {outcome} {entry.HeadersMs:0} ms {entry.Url}"));
    }

    private void OnLayoutQuery(LayoutQuery query)
    {
        if (!_console.Verbose || query.Ms < LayoutQueryThresholdMs)
            return;

        _console.Event("layout", string.Create(
            CultureInfo.InvariantCulture,
            $"a script's geometry question laid the page out: {query.Elements:N0} element(s) in {query.Ms:0} ms [{query.Phase}]{(query.Error is null ? string.Empty : " FAILED " + query.Error)}"));
    }

    private const string TurnTraceVariable = "BROILER_TRACE_JS_ENTRY";

    /// <summary>
    /// Switches on the script bridge's turn trace (<c>BROILER_TRACE_JS_ENTRY</c>), which reports every
    /// turn JavaScript runs for longer than a threshold and every idle gap between turns — the one
    /// measurement that tells a script that is busy from a page that is waiting. It is read once, when
    /// the bridge first runs a script, so it is set before the page loads; a value the caller set
    /// is kept. Returns the value to restore.
    /// </summary>
    private static string? EnableJavaScriptTurnTrace()
    {
        var previous = Environment.GetEnvironmentVariable(TurnTraceVariable);
        if (string.IsNullOrEmpty(previous))
            Environment.SetEnvironmentVariable(TurnTraceVariable, "turn=100,gap=1000");
        return previous;
    }

    private void OnException(ExceptionEvent e)
    {
        if (!_console.Verbose)
            return;

        var shown = Interlocked.Increment(ref _consoleExceptions);
        if (shown <= MaxConsoleExceptions)
            _console.Event("exception", $"#{e.Sequence} {e.Kind} [{e.Phase}] {e.Type}: {e.Message} (in {e.Site})");
        else if (shown == MaxConsoleExceptions + 1)
            _console.Event("exception", "further exceptions are written to exceptions.log only");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The share of pixels the scripts changed, rounded for the report — but never to zero when a
    /// pixel changed: "no pixel changed" is a finding, and forty changed pixels of a viewport round
    /// to 0.0000.
    /// </summary>
    internal static double ScriptEffect(double ratio) =>
        ratio == 0 ? 0 : Math.Max(Math.Round(ratio, 4), 0.0001);

    /// <summary>Every file an analysis writes into its directory, apart from those in <c>resources/</c>.</summary>
    private static readonly string[] AnalysisFiles =
    [
        "report.html", "report.md", "report.json", "screenshot.png", "screenshot-full.png",
        "screenshot-without-scripts.png", "screenshot-boxes.png", "document-as-fetched.html",
        "document-after-scripts.html", "document-as-rendered.html", "exceptions.log", "exceptions.json",
        "javascript-errors.log", "console.log", "messages.log", "diagnostics.json", "network.json",
        "network.har", "watchdog.md", "slow-phase-stacks.txt", "summary.md",
        "layout/fragment-tree.txt", "layout/fragments.json", "layout/computed-styles.json",
        "layout/display-list.json", "layout/invariant-violations.txt",
    ];

    /// <summary>
    /// Clears what an earlier analysis left in <paramref name="output"/>, so that no file of that run —
    /// a <c>watchdog.md</c> it ended with, a resource it fetched — sits beside this run's as if this
    /// run had written it.
    /// </summary>
    /// <remarks>
    /// Only a directory that holds an analysis (its <c>report.json</c> or <c>watchdog.md</c>) is
    /// touched, and only the files an analysis writes: the fixed names, and the resources its own
    /// <c>resources/index.json</c> lists. Anything else a reader put there stays.
    /// </remarks>
    private void RemovePreviousAnalysis(string output)
    {
        if (!File.Exists(Path.Combine(output, "report.json")) && !File.Exists(Path.Combine(output, "watchdog.md")))
            return;

        var resources = Path.Combine(output, "resources");
        var index = Path.Combine(resources, "index.json");
        var names = new List<string>(AnalysisFiles);
        try
        {
            if (File.Exists(index))
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(index));
                foreach (var entry in json.RootElement.EnumerateArray())
                {
                    if (entry.TryGetProperty("SavedAs", out var saved) && saved.GetString() is { Length: > 0 } file
                        && Path.GetFileName(file) == file)
                    {
                        names.Add("resources/" + file);
                    }
                }

                names.Add("resources/index.json");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            _console.Line($"could not read the earlier run's resources/index.json: {ex.Message}");
        }

        var removed = 0;
        foreach (var name in names)
        {
            var path = Path.Combine(output, name);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _console.Line($"could not remove the earlier run's {name}: {ex.Message}");
            }
        }

        _console.Line($"cleared   {removed} file(s) of an earlier analysis in this directory");
    }

    private void WriteText(string output, string name, string content, string description)
    {
        try
        {
            File.WriteAllText(Path.Combine(output, name), content);
            AddFile(name, description);
        }
        catch (IOException ex)
        {
            _console.Line($"could not write {name}: {ex.Message}");
        }
    }

    private void AddFile(string? path, string description)
    {
        if (path is null)
            return;

        lock (_files)
            _files.Add(new AnalysisFile(path, description));
    }

    private static string? Describe(Broiler.HtmlBridge.Dom.NavigationRequest? navigation) =>
        navigation is null
            ? null
            : navigation.Delay > TimeSpan.Zero
                ? string.Create(CultureInfo.InvariantCulture, $"{navigation.Kind} to {navigation.Url} after {navigation.Delay.TotalSeconds:0.#} s")
                : $"{navigation.Kind} to {navigation.Url}";

    /// <summary>The archive kind a network body is filed under, by what asked for it.</summary>
    private static string ResourceKindFor(string destination) => destination switch
    {
        "document" => "Document",
        "script" => "Script",
        "style" => "Stylesheet",
        "image" => "Image",
        "font" => "Font",
        "iframe" or "frame" or "object" or "embed" => "SubDocument",
        _ => "Fetch",
    };
}
