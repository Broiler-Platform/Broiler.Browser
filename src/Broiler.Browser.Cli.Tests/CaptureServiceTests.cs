using System.Text.Json;

namespace Broiler.Cli.Tests;

/// <summary>
/// A capture runs the page the way the browser window does — its scripts, their timers and promise
/// reactions, the load window settled — and writes out what that left.
/// </summary>
[Collection(CaptureCollection.Name)]
public sealed class CaptureServiceTests : IDisposable
{
    /// <summary>
    /// One line written by each kind of work a page does while it loads, and one that only a laid-out
    /// document can answer.
    /// </summary>
    private const string ScriptedPage = """
        <!DOCTYPE html>
        <html>
        <head>
        <title>Scripted page</title>
        <style>#box { width: 120px; height: 40px; background: #3a7; }</style>
        </head>
        <body>
        <div id="box"></div>
        <p id="sync"></p>
        <p id="timer"></p>
        <p id="promise"></p>
        <p id="geometry"></p>
        <script>
          document.getElementById('sync').textContent = 'written by an inline script';
          const declaredConst = 41 + 1;
          var results = { ran: false };
          setTimeout(function () {
            document.getElementById('timer').textContent = 'written by a timer';
          }, 50);
          Promise.resolve().then(function () {
            document.getElementById('promise').textContent = 'written by a promise reaction';
          });
          document.getElementById('geometry').textContent =
            'box width ' + document.getElementById('box').getBoundingClientRect().width;
          function runTests() {
            return new Promise(function (resolve) {
              setTimeout(function () { results = { ran: true, score: 7 }; resolve(); }, 10);
            });
          }
        </script>
        </body>
        </html>
        """;

    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    /// <summary>
    /// The text of the element <paramref name="id"/> in a serialized document, or <c>null</c> when it
    /// has none.
    /// </summary>
    /// <remarks>
    /// Never search the whole document for a script's output: the serialized document still holds
    /// each <c>&lt;script&gt;</c> element's source, so the string a script writes is in it whether or
    /// not the script ran. A negative control found exactly that in the first version of these tests.
    /// </remarks>
    internal static string? TextOf(string html, string id)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, $"id=\"{id}\">([^<]*)<");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<string> CaptureDocumentAsync(string url, string outputName = "out.html", bool followFirstLink = false)
    {
        var output = _pages.Output(outputName);
        await new CaptureService().CaptureAsync(new CaptureOptions
        {
            Url = url,
            OutputPath = output,
            FollowFirstLink = followFirstLink,
        });

        return await File.ReadAllTextAsync(output);
    }

    [Fact(Timeout = 600000)]
    public async Task A_Capture_Saves_The_Document_Its_Scripts_Left()
    {
        var html = await CaptureDocumentAsync(_pages.Write("page.html", ScriptedPage));

        Assert.Equal("written by an inline script", TextOf(html, "sync"));
        Assert.Equal("written by a promise reaction", TextOf(html, "promise"));
    }

    /// <summary>
    /// A timer the page set while loading has run by the time the document is saved: the load window
    /// was settled, not just the synchronous scripts run.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task The_Load_Window_Is_Settled_Before_The_Document_Is_Saved()
    {
        var html = await CaptureDocumentAsync(_pages.Write("page.html", ScriptedPage));

        Assert.Equal("written by a timer", TextOf(html, "timer"));
    }

    /// <summary>
    /// A script asking for an element's size is answered by a real layout. The bridge's null view,
    /// which is what it gets without one, answers 0.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task A_Scripts_Geometry_Comes_From_A_Real_Layout()
    {
        var html = await CaptureDocumentAsync(_pages.Write("page.html", ScriptedPage));

        Assert.Equal("box width 120", TextOf(html, "geometry"));
    }

    [Fact(Timeout = 600000)]
    public async Task A_Text_Capture_Keeps_The_Text_And_Drops_The_Markup()
    {
        var text = await CaptureDocumentAsync(_pages.Write("page.html", ScriptedPage), "out.txt");

        // "box width 120" is assembled when the script runs, so unlike the script's string literals it
        // cannot come from the script's own source text, which a text capture keeps too.
        Assert.Contains("box width 120", text);
        Assert.DoesNotContain("<p", text);
    }

    [Fact(Timeout = 600000)]
    public async Task An_Image_Capture_Is_Written_At_The_Requested_Size()
    {
        var output = _pages.Output("out.png");

        var exitCode = await Program.Main([
            "--capture-image", _pages.Write("page.html", ScriptedPage),
            "--output", output,
            "--width", "200",
            "--height", "120",
        ]);

        Assert.Equal(0, exitCode);
        using var image = Broiler.Graphics.Imaging.BBitmap.Decode(output);
        Assert.Equal(200, image.Width);
        Assert.Equal(120, image.Height);
    }

    [Fact(Timeout = 600000)]
    public async Task Following_The_First_Link_Captures_The_Page_It_Links_To()
    {
        _pages.Write("page.html", ScriptedPage);
        var landing = _pages.Write("landing.html", "<html><body><a href=\"page.html\">Start the test</a></body></html>");

        var html = await CaptureDocumentAsync(landing, followFirstLink: true);

        Assert.Equal("written by a timer", TextOf(html, "timer"));
        Assert.DoesNotContain("Start the test", html);
    }

    /// <summary>
    /// A local landing page may send the capture to a page beside it, not to a file elsewhere on the
    /// disk.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task A_Link_Out_Of_The_Landing_Pages_Directory_Is_Not_Followed()
    {
        _pages.Write("outside.html", "<html><body>outside the landing directory</body></html>");
        var landing = _pages.Write(
            Path.Combine("tests", "landing.html"),
            "<html><body><a href=\"../outside.html\">Start the test</a></body></html>");

        var html = await CaptureDocumentAsync(landing, followFirstLink: true);

        Assert.Contains("Start the test", html);
        Assert.DoesNotContain("outside the landing directory", html);
    }

    private async Task<JsonElement[]> EvaluateAsync(string url, params string[] expressions)
    {
        var output = _pages.Output("report.json");
        await new CaptureService().EvaluatePageAsync(new PageEvaluationOptions
        {
            Url = url,
            OutputPath = output,
            Expressions = expressions,
            HtmlOutputPath = _pages.Output("after.html"),
        });

        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(output));
        return [.. report.RootElement.GetProperty("evaluations").EnumerateArray().Select(e => e.Clone())];
    }

    private static (string? Type, string? Value, string? Error) Read(JsonElement evaluation) =>
        (evaluation.GetProperty("type").GetString(),
         evaluation.GetProperty("value").GetString(),
         evaluation.GetProperty("error").GetString());

    /// <summary>
    /// An expression runs on the page's own global, so a top-level <c>const</c> — a lexical binding,
    /// not a property of <c>window</c> — resolves as it would in a later script on the page.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task An_Expression_Sees_The_Pages_Own_Declarations()
    {
        var evaluations = await EvaluateAsync(_pages.Write("page.html", ScriptedPage), "declaredConst");

        Assert.Equal(("number", "42", (string?)null), Read(Assert.Single(evaluations)));
    }

    /// <summary>
    /// The page's work settles between expressions: the first starts a test run on a timer, and the
    /// second reads the result that timer wrote.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task Work_An_Expression_Starts_Has_Settled_Before_The_Next_One_Runs()
    {
        var evaluations = await EvaluateAsync(
            _pages.Write("page.html", ScriptedPage),
            "runTests()",
            "JSON.stringify(results)");

        Assert.Equal(("string", "{\"ran\":true,\"score\":7}", (string?)null), Read(evaluations[1]));
    }

    [Fact(Timeout = 600000)]
    public async Task Null_Undefined_And_A_Throw_Are_Reported_Apart()
    {
        var evaluations = await EvaluateAsync(
            _pages.Write("page.html", ScriptedPage),
            "null",
            "void 0",
            "notDeclaredAnywhere");

        Assert.Equal(("null", (string?)null, (string?)null), Read(evaluations[0]));
        Assert.Equal(("undefined", (string?)null, (string?)null), Read(evaluations[1]));

        var (type, value, error) = Read(evaluations[2]);
        Assert.Null(type);
        Assert.Null(value);
        Assert.Contains("notDeclaredAnywhere", error);
    }

    /// <summary>
    /// "The page had no scripts" is an answer an evaluation has to be able to give, so a page with
    /// none still gets a realm to evaluate in.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task A_Page_Without_Scripts_Can_Still_Be_Evaluated()
    {
        var evaluations = await EvaluateAsync(
            _pages.Write("static.html", "<html><head><title>No scripts here</title></head><body></body></html>"),
            "document.title");

        Assert.Equal(("string", "No scripts here", (string?)null), Read(Assert.Single(evaluations)));
    }

    [Fact(Timeout = 600000)]
    public async Task The_Html_Output_Is_The_Document_After_The_Expressions()
    {
        await EvaluateAsync(
            _pages.Write("page.html", ScriptedPage),
            "document.getElementById('sync').textContent = 'rewritten by an expression'");

        var html = await File.ReadAllTextAsync(_pages.Output("after.html"));
        Assert.Equal("rewritten by an expression", TextOf(html, "sync"));
        Assert.Equal("written by a timer", TextOf(html, "timer"));
    }
}
