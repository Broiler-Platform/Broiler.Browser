using System.Drawing;
using System.Globalization;
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

    /// <summary>
    /// normalize.css's focus ring: a vendor-prefixed pseudo-class, which Broiler.CSS guesses matches
    /// every element, so every button gets the outline. Each kind is reported with the first rule's place.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Selector_The_Engine_Does_Not_Model_Is_Named_With_Its_Place()
    {
        const string Css = "p { color: red; }\n" +
            "button:-moz-focusring,\n[type=\"button\"]:-moz-focusring { outline: 1px dotted; }\n" +
            "@media screen { input::placeholder { color: gray; } }\n" +
            "a:hover, p:bogus { color: blue; }\n";

        var report = CssInspector.Inspect(
            [new StylesheetSource("site.css", Css, SavedAs: null)],
            styleAttributes: [],
            rejectedDuringCascade: [],
            requestedFonts: new Dictionary<string, int>());

        Assert.Equal(
            ["guessed :-moz-focusring ×2 site.css 2:1", "unstyled pseudo-element ::placeholder ×1 site.css 4:17",
             "invalid :bogus ×1 site.css 5:1", "interactive :hover ×1 site.css 5:1"],
            report.SelectorGaps.Select(static g => $"{g.Kind} {g.Part} ×{g.Selectors} {g.FirstSource}"));
        Assert.Equal("button:-moz-focusring", report.SelectorGaps[0].Example);
    }

    [Theory(Timeout = 600000)]
    [InlineData("cursor", false)]
    [InlineData("-webkit-user-select", false)]
    [InlineData("transition-duration", false)]
    [InlineData("scroll-padding-top", false)]
    [InlineData("backdrop-filter", true)]
    [InlineData("accent-color", true)]
    [InlineData("-webkit-box-reflect", true)]
    [InlineData("scrollbar-color", true)]
    public void A_Property_That_Changes_Nothing_In_A_Still_Image_Is_Told_Apart(string property, bool affects) =>
        Assert.Equal(affects, CssInspector.AffectsAStillImage(property));

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

    /// <summary>
    /// Only a srcset lists candidates: in a <c>src</c> or an <c>href</c>, commas and spaces belong to
    /// the URL. And references resolve against the document's base URL, <c>&lt;base href&gt;</c> first.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Reference_Is_Resolved_Whole_Against_The_Base_Url()
    {
        const string Html =
            "<!DOCTYPE html><html><head>" +
            "<base href='https://cdn.example.test/assets/'>" +
            "<link rel='stylesheet' href='https://fonts.example.test/css?family=Open+Sans:400,700'>" +
            "</head><body>" +
            "<img src='photos/c_fill,w_300/x.jpg'>" +
            "<img srcset='small.jpg 1x, large.jpg 2x'>" +
            "</body></html>";

        var report = HtmlInspector.Inspect(Html, afterScripts: null, network: [], "file:///C:/pages/page.html");

        Assert.Equal("https://fonts.example.test/css?family=Open+Sans:400,700", Assert.Single(report.Stylesheets).Url);
        Assert.Equal(
            ["https://cdn.example.test/assets/photos/c_fill,w_300/x.jpg", "https://cdn.example.test/assets/small.jpg"],
            report.Images.Select(static i => i.Url));
        Assert.All(report.Images, static i => Assert.Equal("not requested", i.Load));
    }

    [Theory(Timeout = 600000)]
    [InlineData("inline data: URL", false)]
    [InlineData("local file", false)]
    [InlineData("200, 1,024 bytes", false)]
    [InlineData("local file NOT FOUND", true)]
    [InlineData("not requested", true)]
    [InlineData("no response", true)]
    [InlineData("failed: NameResolutionError", true)]
    [InlineData("404 (HTTP error), 0 bytes", true)]
    public void An_Image_Did_Not_Load_Only_When_It_Did_Not_Arrive(string load, bool didNotLoad) =>
        Assert.Equal(didNotLoad, HtmlInspector.DidNotLoad(load));

    [Fact(Timeout = 600000)]
    public void The_Markdown_Report_Writes_The_Pages_Markup_As_Text_And_Its_Numbers_Invariantly()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var markdown = MarkdownReport.Write(EmptyReport() with
            {
                Html = new HtmlReport { Title = "<img src=https://tracker.example.test/p.gif>", ElementsAsFetched = 12_345 },
            });

            Assert.Contains("&lt;img src=https://tracker.example.test/p.gif&gt;", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("<img", markdown, StringComparison.Ordinal);
            Assert.Contains("12,345 as fetched", markdown, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    /// <summary>
    /// The document as fetched is parsed with its parse errors, each located by line and column and
    /// saying what Broiler's parser did: the two end tags that match no open element are repaired as a
    /// browser repairs them, and the <c>&lt;section/&gt;</c> is where it departs from one.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Parse_Error_Is_Located_And_Says_What_The_Parser_Did()
    {
        const string Html = "<!DOCTYPE html>\n<html><body>\n<div><span>x</p>y</span></span></div>\n<section/>z\n</body></html>";

        var report = HtmlInspector.Inspect(Html, afterScripts: null, network: [], "https://example.test/");

        Assert.Equal(report.ParseErrors.Count, report.ParseErrorCount);
        Assert.Equal(
            ["unexpected-end-tag@3:13", "unexpected-end-tag@3:25", "non-void-html-element-start-tag-with-trailing-solidus@4:1"],
            report.ParseErrors.Select(static e => $"{e.Code}@{e.Line}:{e.Column}"));
        Assert.Contains("stands for an empty one, as in a browser", report.ParseErrors[0].Message, StringComparison.Ordinal);
        Assert.Contains("matches no open element and is ignored", report.ParseErrors[1].Message, StringComparison.Ordinal);
        Assert.Contains("where this parser closes it at once", report.ParseErrors[2].Message, StringComparison.Ordinal);
        Assert.Equal(new TagCount("unexpected-end-tag", 2), report.ParseErrorsByCode[0]);
    }

    [Fact(Timeout = 600000)]
    public void The_Parse_Errors_That_Change_What_Renders_Are_Findings()
    {
        var report = new AnalysisReport
        {
            Url = "https://example.test/",
            StartedAt = System.DateTime.UtcNow,
            Environment = AnalysisEnvironment.Capture(),
            Html = new HtmlReport
            {
                ParseErrorCount = 3,
                ParseErrorsByCode = [new TagCount("eof-in-text", 1), new TagCount("duplicate-attribute", 2)],
                ParseErrors = [new HtmlParseProblem("eof-in-text", 7, 1, "<script> has no </script>")],
            },
        };

        var findings = Triage.Rank(report);

        var swallowed = Assert.Single(findings, static f => f.Title.Contains("end tag never comes", StringComparison.Ordinal));
        Assert.Equal(FindingSeverity.Error, swallowed.Severity);
        Assert.Contains("line 7, column 1", swallowed.Detail, StringComparison.Ordinal);
        Assert.Contains(findings, static f => f.Title == "2 other HTML parse error(s)" && f.Detail == "duplicate-attribute ×2");
    }

    /// <summary>
    /// An end tag that matches no open element is repaired as a browser repairs it, so it is
    /// information. A <c>&lt;div/&gt;</c>, which Broiler closes and a browser leaves open, is still a warning.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Stray_End_Tag_Is_Information_And_A_Self_Closed_Div_A_Warning()
    {
        var report = new AnalysisReport
        {
            Url = "https://example.test/",
            StartedAt = System.DateTime.UtcNow,
            Environment = AnalysisEnvironment.Capture(),
            Html = HtmlInspector.Inspect("<!DOCTYPE html>\n<div>x</span></div>\n<div/>y\n", afterScripts: null, network: [], "https://example.test/"),
        };

        var findings = Triage.Rank(report);

        var stray = Assert.Single(findings, static f => f.Title.Contains("end tag(s) that match no open element", StringComparison.Ordinal));
        Assert.Equal(FindingSeverity.Info, stray.Severity);
        Assert.Contains("line 2, column 7: </span> matches no open element and is ignored", stray.Detail, StringComparison.Ordinal);
        var selfClosed = Assert.Single(findings, static f => f.Title.Contains("like <div/>", StringComparison.Ordinal));
        Assert.Equal(FindingSeverity.Warning, selfClosed.Severity);
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

    [Theory(Timeout = 600000)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.00001, 0.0001)]
    [InlineData(0.25, 0.25)]
    public void A_Pixel_Change_Never_Rounds_To_No_Change(double ratio, double reported) =>
        Assert.Equal(reported, PageAnalyzer.ScriptEffect(ratio));

    [Fact(Timeout = 600000)]
    public void Obsolete_And_Custom_Elements_And_Long_Turns_Are_Findings()
    {
        var findings = Triage.Rank(EmptyReport() with
        {
            Html = new HtmlReport { ObsoleteElements = [new TagCount("center", 2)], CustomElements = [new TagCount("my-widget", 3)] },
            Scripting = new ScriptingSummary { LongTurns = ["turn 1250 ms in timer"] },
        });

        Assert.Contains(findings, static f => f.Title.Contains("obsolete", StringComparison.Ordinal) && f.Detail.Contains("<center> ×2", StringComparison.Ordinal));
        Assert.Contains(findings, static f => f.Title == "3 custom element(s)");
        Assert.Contains(findings, static f => f.Title.Contains("long turn", StringComparison.Ordinal) && f.Detail.Contains("1250 ms", StringComparison.Ordinal));
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
