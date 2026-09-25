using System.Drawing;
using Broiler.Cli.Analysis;
using Broiler.Layout.IR;

namespace Broiler.Cli.Tests;

/// <summary>
/// The rules the analysis reads a page by: what counts as a CSS problem, an HTML problem, a layout
/// problem, and how they are ranked — each on a document small enough to know the answer for.
/// </summary>
public sealed class AnalysisInspectorTests
{
    // ── CSS ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Css_Parse_Problem_Is_Located_In_Its_Own_Sheet()
    {
        var report = CssInspector.Inspect(
            [new StylesheetSource("site.css", "a { color: red; }\n.x { color: ; }\n", SavedAs: null)],
            styleAttributes: [],
            rejectedDuringCascade: [],
            requestedFonts: new Dictionary<string, int>());

        var problem = Assert.Single(report.ParseProblems);
        Assert.Equal("site.css", problem.Source);
        Assert.Equal(2, problem.Line);
        Assert.StartsWith("CSS", problem.Code, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void An_Unknown_Property_Is_Reported_And_A_Known_One_Is_Not()
    {
        var report = CssInspector.Inspect(
            [new StylesheetSource("<style> #1", "p { colr: red; color: blue; --custom: 1; -webkit-box-orient: vertical; }", null)],
            [],
            [],
            new Dictionary<string, int>());

        var unknown = Assert.Single(report.UnknownProperties);
        Assert.Equal("colr", unknown.Property);
        Assert.Equal(1, report.CustomProperties);
        Assert.Equal(1, report.VendorPrefixedDeclarations);
    }

    [Fact(Timeout = 600000)]
    public void A_Value_The_Engine_Rejects_Is_Reported()
    {
        var report = CssInspector.Inspect([new StylesheetSource("s", "div { display: flexy; }", null)], [], [], new Dictionary<string, int>());

        var rejected = Assert.Single(report.RejectedValues);
        Assert.Equal("display", rejected.Property);
        Assert.Equal("flexy", rejected.Value);
    }

    [Fact(Timeout = 600000)]
    public void A_Style_Attribute_Problem_Is_Located_In_The_Attribute()
    {
        var report = CssInspector.Inspect([], ["color: red; width: ;"], [], new Dictionary<string, int>());

        var problem = Assert.Single(report.ParseProblems);
        Assert.Equal("style attribute #1", problem.Source);
        Assert.Equal(1, problem.Line);
        Assert.True(problem.Column > 10, $"column {problem.Column} should point at the empty value, not at the wrapper the attribute was parsed in");
    }

    [Fact(Timeout = 600000)]
    public void An_Unknown_At_Rule_Is_Reported()
    {
        var report = CssInspector.Inspect([new StylesheetSource("s", "@frobnicate x { a { color: red } } @media print { a { color: red } }", null)], [], [], new Dictionary<string, int>());

        Assert.Equal(["frobnicate"], report.UnknownAtRules);
        Assert.True(report.AtRules.ContainsKey("media"));
    }

    [Fact(Timeout = 600000)]
    public void A_Font_List_That_Starts_With_A_Missing_Font_Is_A_Fallback()
    {
        var fonts = CssInspector.ResolveFonts(
            new Dictionary<string, int> { ["'Definitely Missing Font', 'Web Face', serif"] = 3 },
            [new FontFaceRule("Web Face", "s", "url(face.woff2)")]);

        var font = Assert.Single(fonts);
        Assert.Equal(["Definitely Missing Font"], font.Unavailable);
        Assert.Equal("Web Face", font.ResolvedFamily);
        Assert.Equal("web font", font.Resolution);
    }

    // ── HTML ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A protocol-relative reference takes the page's scheme. <c>Uri.TryCreate</c> with an absolute kind
    /// reads <c>//host/path</c> as a UNC file path on Windows, and a first version of the analysis probed
    /// the network drive for every image on the page — twenty-two seconds on one article.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Protocol_Relative_Reference_Resolves_Against_The_Page()
    {
        var page = new Uri("https://en.example.org/wiki/Page");

        Assert.Equal("https://upload.example.org/a.png", HtmlInspector.Resolve("//upload.example.org/a.png", page));
        Assert.Equal("https://en.example.org/static/b.css", HtmlInspector.Resolve("/static/b.css", page));
        Assert.Equal("https://en.example.org/wiki/c.js", HtmlInspector.Resolve("c.js", page));
        Assert.Equal("data:text/plain,x", HtmlInspector.Resolve("data:text/plain,x", page));
    }

    [Fact(Timeout = 600000)]
    public void The_Markup_Report_Names_Quirks_Duplicates_And_Unknown_Elements()
    {
        const string Html = """
            <html><head><title>t</title></head><body>
            <p id="a">1</p><p id="a">2</p>
            <frobnicate>x</frobnicate><my-widget>y</my-widget><center>z</center>
            <svg><path d="M0 0"/></svg>
            <script>var x = 1;</script>
            <script type="text/x-template"><b>t</b></script>
            <script type="module">import './m.js';</script>
            </body></html>
            """;

        var report = HtmlInspector.Inspect(Html, afterScripts: null, network: [], "https://example.test/page");

        Assert.True(report.QuirksMode);
        Assert.Null(report.Doctype);
        Assert.Equal("a", Assert.Single(report.DuplicateIds).Tag);
        Assert.Equal("frobnicate", Assert.Single(report.UnknownElements).Tag);
        Assert.Equal("my-widget", Assert.Single(report.CustomElements).Tag);
        Assert.Equal("center", Assert.Single(report.ObsoleteElements).Tag);
        Assert.Equal(["classic", "not run (type=\"text/x-template\")", "module"], report.Scripts.Select(static s => s.Kind));
    }

    [Fact(Timeout = 600000)]
    public void A_Standards_Mode_Document_Is_Not_Reported_As_Quirks()
    {
        var report = HtmlInspector.Inspect("<!DOCTYPE html><html><body><p>x</p></body></html>", null, [], "https://example.test/");

        Assert.False(report.QuirksMode);
        Assert.Equal("<!DOCTYPE html>", report.Doctype);
    }

    [Fact(Timeout = 600000)]
    public void A_Missing_Local_File_Is_Reported_As_Not_Found()
    {
        var missing = new Uri(Path.Combine(Path.GetTempPath(), "broiler-missing-" + Guid.NewGuid().ToString("N") + ".png")).AbsoluteUri;

        Assert.Equal("local file NOT FOUND", HtmlInspector.LoadOutcome(missing, []));
    }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Box_Far_Taller_Than_Its_Content_Is_Found_And_Its_Container_Is_Not()
    {
        var runaway = Box("li", 100_000, Box("span", 18));
        var container = Box("ol", 100_020, runaway, Box("li", 20));

        Assert.NotNull(LayoutInspector.TallerThanContent(runaway, threshold: 4000));
        Assert.Null(LayoutInspector.TallerThanContent(container, threshold: 4000));
    }

    [Fact(Timeout = 600000)]
    public void A_Tall_Box_Whose_Children_Fill_It_Is_Not_Found()
    {
        var article = Box("article", 30_000, [.. Enumerable.Range(0, 300).Select(static _ => Box("p", 100))]);

        Assert.Null(LayoutInspector.TallerThanContent(article, threshold: 4000));
    }

    // ── Triage ──────────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void Findings_Are_Ranked_Errors_First()
    {
        var report = EmptyReport() with
        {
            Html = new HtmlReport { QuirksMode = true, DuplicateIds = [new TagCount("x", 2)] },
            Phases = [new PhaseRecord("render", 0, 10, PhaseOutcome.Failed, null, "System.Exception: boom")],
        };

        var findings = Triage.Rank(report);

        Assert.Equal(FindingSeverity.Error, findings[0].Severity);
        Assert.Contains(findings, static f => f.Title.Contains("quirks mode", StringComparison.Ordinal));
        Assert.Equal(findings.OrderBy(static f => f.Severity).Select(static f => f.Title), findings.Select(static f => f.Title));
    }

    [Fact(Timeout = 600000)]
    public void A_Page_Its_Scripts_Did_Not_Change_Is_Reported_Only_When_No_Pixel_Changed()
    {
        var failing = new ScriptingSummary { Failures = 2 };

        Assert.Contains(Triage.Rank(EmptyReport() with { Scripting = failing, ScriptVisualEffect = 0 }),
            static f => f.Title.Contains("same with and without", StringComparison.Ordinal));
        Assert.DoesNotContain(Triage.Rank(EmptyReport() with { Scripting = failing, ScriptVisualEffect = 0.0008 }),
            static f => f.Title.Contains("same with and without", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void A_Runaway_Page_Height_Is_An_Error()
    {
        var findings = Triage.Rank(EmptyReport() with { Layout = new LayoutReport { ContentHeight = 608_093 } });

        Assert.Contains(findings, static f => f.Severity == FindingSeverity.Error && f.Title.Contains("608,093", StringComparison.Ordinal));
    }

    // ── Stack samples ───────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void A_Stack_Report_Is_Split_Into_Threads_Innermost_Frame_First()
    {
        const string Report = """
            Thread (0x1):
              [Native Frames]
              Broiler.Layout!Broiler.Layout.Engine.CssBox.PerformLayout(class Broiler.Layout.Engine.CssBox)
              Broiler.HTML.Orchestration!Broiler.HTML.Orchestration.HtmlContainerInt.PerformLayout()
            Thread (0x2):
              System.Private.CoreLib.il!System.Threading.Monitor.Wait(class System.Object,int32)
            """;

        var threads = StackSampler.ParseThreads(Report);

        Assert.Equal(2, threads.Count);
        Assert.Equal(2, threads[0].Frames.Count);
        Assert.True(StackSampler.IsBroilerFrame(threads[0].Frames[0]));
        Assert.False(StackSampler.IsBroilerFrame(threads[1].Frames[0]));
        Assert.False(StackSampler.IsBroilerFrame("Broiler.Cli!Broiler.Cli.Program.<Main>(class System.String[])"));
        Assert.True(StackSampler.IsWaitFrame(threads[1].Frames[0]));
        Assert.False(StackSampler.IsWaitFrame(threads[0].Frames[0]));
        Assert.Equal("Broiler.Layout.Engine.CssBox.PerformLayout", StackSampler.WithoutParameters(threads[0].Frames[0]));
    }

    // ── Batch naming ────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = 600000)]
    public void An_Empty_Extension_Derives_Directory_Names()
    {
        var items = BatchRunner.DeriveOutputPaths(["https://a.example/one", "https://b.example/one"], "out", string.Empty);

        Assert.Equal([Path.Combine("out", "one"), Path.Combine("out", "one-2")], items.Select(static i => i.OutputPath));
    }

    private static Fragment Box(string tag, float height, params Fragment[] children) => new()
    {
        Size = new SizeF(400, height),
        Children = children,
        Style = new ComputedStyle { TagName = tag, Display = "block" },
    };

    private static AnalysisReport EmptyReport() => new()
    {
        Url = "https://example.test/",
        StartedAt = System.DateTime.UtcNow,
        Environment = new AnalysisEnvironment("Broiler.JS", "0", "rt", "os", "x64", "Release", "--analyze x", new Dictionary<string, string>()),
    };
}
