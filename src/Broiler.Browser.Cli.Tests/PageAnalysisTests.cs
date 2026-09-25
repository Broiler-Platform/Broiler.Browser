using System.Text.Json;
using Broiler.Cli.Analysis;
using Broiler.HtmlBridge;

namespace Broiler.Cli.Tests;

/// <summary>
/// <c>--analyze</c> end to end, on pages written for each test: what it writes, what it finds, and
/// that it runs a page on Broiler.JS whatever the build configuration.
/// </summary>
[Collection(CaptureCollection.Name)]
public sealed class PageAnalysisTests : IDisposable
{
    /// <summary>A page with one of each kind of problem the analysis looks for.</summary>
    private const string ProblemPage = """
        <html>
        <head>
        <title>Problem page</title>
        <link rel="stylesheet" href="site.css">
        <style>
          body { font-family: 'Definitely Missing Font', serif; }
          #wide { width: 3000px; height: 10px; }
          .broken { colr: red; }
        </style>
        </head>
        <body>
        <p id="twice">one</p><p id="twice">two</p>
        <div id="wide"></div>
        <p id="out"></p>
        <script src="app.js"></script>
        <script>
          console.log('console-marker');
          try { throw new Error('caught-js-marker'); } catch (e) { }
          document.getElementById('out').textContent = 'measured ' + document.getElementById('wide').getBoundingClientRect().width;
          missingPlatformFeatureMarker();
        </script>
        </body>
        </html>
        """;

    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    private async Task<(int ExitCode, string Directory, JsonElement Report, string Console)> AnalyzeAsync(
        string url,
        TimeSpan? watchdog = null,
        Action<int>? exit = null)
    {
        var directory = _pages.Output("analysis-" + Guid.NewGuid().ToString("N")[..8]);
        var console = new StringWriter();
        var exitCode = await new PageAnalyzer(
            new PageAnalysisOptions
            {
                Url = url,
                OutputDirectory = directory,
                Width = 800,
                Height = 600,
                Watchdog = watchdog,
            },
            console,
            exit).RunAsync();

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "report.json")));
        return (exitCode, directory, report.RootElement.Clone(), console.ToString());
    }

    private string WriteProblemPage()
    {
        _pages.Write("site.css", "h1 { color: #123; }\n.x { color: ; }\n");
        _pages.Write("app.js", "window.fromExternalScript = 'external-script-marker';");
        return _pages.Write("page.html", ProblemPage);
    }

    [Fact(Timeout = 600000)]
    public async Task An_Analysis_Writes_Its_Report_Screenshots_Logs_And_The_Pages_Files()
    {
        var (exitCode, directory, _, _) = await AnalyzeAsync(WriteProblemPage());

        Assert.Equal(PageAnalyzer.Completed, exitCode);
        foreach (var file in (string[])["report.html", "report.md", "report.json", "screenshot.png", "screenshot-without-scripts.png",
                     "screenshot-boxes.png", "exceptions.log", "exceptions.json", "javascript-errors.log", "console.log",
                     "messages.log", "network.json", "network.har", "document-as-fetched.html", "document-after-scripts.html",
                     "document-as-rendered.html", "layout/fragment-tree.txt", "resources/index.json"])
        {
            Assert.True(File.Exists(Path.Combine(directory, file)), $"{file} was not written");
        }

        // The page's own files: the external script and stylesheet as fetched, the inline script as run.
        var archived = string.Concat(Directory.GetFiles(Path.Combine(directory, "resources")).Select(File.ReadAllText));
        Assert.Contains("external-script-marker", archived, StringComparison.Ordinal);
        Assert.Contains("h1 { color: #123; }", archived, StringComparison.Ordinal);
        Assert.Contains("console-marker", archived, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public async Task An_Exception_The_Page_Caught_Itself_Is_In_The_Exception_Log()
    {
        var (_, directory, report, _) = await AnalyzeAsync(WriteProblemPage());

        var log = await File.ReadAllTextAsync(Path.Combine(directory, "exceptions.log"));
        Assert.Contains("caught-js-marker", log, StringComparison.Ordinal);
        Assert.Contains("first-chance [scripts]", log, StringComparison.Ordinal);
        Assert.True(report.GetProperty("scripting").GetProperty("javaScriptExceptions").GetInt64() >= 1);
    }

    [Fact(Timeout = 600000)]
    public async Task The_Findings_Name_The_Problems_The_Page_Was_Written_With()
    {
        var (_, _, report, _) = await AnalyzeAsync(WriteProblemPage());

        var titles = report.GetProperty("findings").EnumerateArray()
            .Select(static f => f.GetProperty("title").GetString() + " | " + f.GetProperty("detail").GetString())
            .ToArray();

        Assert.Contains(titles, static t => t.Contains("quirks mode", StringComparison.Ordinal));
        Assert.Contains(titles, static t => t.Contains("missingPlatformFeatureMarker", StringComparison.Ordinal));
        Assert.Contains(titles, static t => t.Contains("used more than once", StringComparison.Ordinal));
        Assert.Contains(titles, static t => t.Contains("div#wide", StringComparison.Ordinal));
        Assert.Contains(titles, static t => t.Contains("CSS parse problem", StringComparison.Ordinal));
        Assert.Contains(titles, static t => t.Contains("colr", StringComparison.Ordinal));
        Assert.Contains(titles, static t => t.Contains("Definitely Missing Font", StringComparison.Ordinal));
    }

    /// <summary>
    /// A script's geometry question is answered from a real layout, and the recorder that times those
    /// layouts hands back the same answer the bridge would have had without it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task Geometry_Questions_Are_Timed_Without_Changing_Their_Answers()
    {
        var (_, directory, report, _) = await AnalyzeAsync(WriteProblemPage());

        var html = await File.ReadAllTextAsync(Path.Combine(directory, "document-after-scripts.html"));
        Assert.Equal("measured 3000", CaptureServiceTests.TextOf(html, "out"));
        Assert.True(report.GetProperty("scripting").GetProperty("geometryRequests").GetInt32() >= 1);
    }

    [Fact(Timeout = 600000)]
    public async Task The_Analysis_Names_Broiler_JS_As_Its_Engine()
    {
        var (_, _, report, _) = await AnalyzeAsync(WriteProblemPage());

        Assert.Equal("Broiler.JS", report.GetProperty("environment").GetProperty("engine").GetString());
    }

    /// <summary>
    /// Under <c>Debug-VM</c>/<c>Release-VM</c> the window's factory puts the VM profile in front of
    /// Broiler.JS; the analysis's composition does not ask the factory, so its engine is Broiler.JS in
    /// every configuration this test is built in.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_Analysis_Composes_Broiler_JS_Directly()
    {
        using var browser = new HeadlessBrowser(TimeSpan.FromSeconds(5), new HeadlessBrowserOptions { BroilerJsOnly = true });

        Assert.IsType<ScriptEngine>(browser.Engine);
    }

    [Fact(Timeout = 600000)]
    public async Task A_Document_That_Cannot_Be_Loaded_Fails_The_Run_And_Still_Reports()
    {
        var missing = new Uri(Path.Combine(_pages.Root, "does-not-exist.html")).AbsoluteUri;

        var (exitCode, _, report, _) = await AnalyzeAsync(missing);

        Assert.Equal(PageAnalyzer.Failed, exitCode);
        var load = report.GetProperty("phases").EnumerateArray().First(static p => p.GetProperty("name").GetString() == "load");
        Assert.Equal("failed", load.GetProperty("outcome").GetString());
    }

    /// <summary>
    /// A page that keeps the analysis busy past its limit still leaves its evidence: the watchdog writes
    /// what it knows and ends the process — here, a test's stand-in for the exit.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task The_Watchdog_Writes_What_It_Knows_And_Ends_The_Run()
    {
        var url = _pages.Write("busy.html", """
            <!DOCTYPE html><html><body><p>busy</p>
            <script>var until = Date.now() + 4000; while (Date.now() < until) { }</script>
            </body></html>
            """);
        var exitCodes = new List<int>();

        var (_, directory, _, console) = await AnalyzeAsync(url, TimeSpan.FromMilliseconds(1500), code =>
        {
            lock (exitCodes)
                exitCodes.Add(code);
        });

        Assert.Equal([PageAnalyzer.WatchdogExit], exitCodes);
        Assert.Contains("WATCHDOG", console, StringComparison.Ordinal);

        // Which phase it stops in depends on how fast the machine loads the page; that it names one does not.
        var watchdog = await File.ReadAllTextAsync(Path.Combine(directory, "watchdog.md"));
        Assert.Contains("stopped by the watchdog", watchdog, StringComparison.Ordinal);
        Assert.Matches("in the [a-z-]+ phase", watchdog);
    }
}
