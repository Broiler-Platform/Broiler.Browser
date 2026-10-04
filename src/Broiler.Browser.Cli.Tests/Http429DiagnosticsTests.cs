using Broiler.Cli.Analysis;

namespace Broiler.Cli.Tests;

/// <summary>HTTP rejection evidence stays distinct from page errors and from guesses about server policy.</summary>
public sealed class Http429DiagnosticsTests
{
    [Theory(Timeout = 600000)]
    [InlineData("document", "analysis", true)]
    [InlineData("script", "window", false)]
    public void A_Received_429_Reports_Its_Scope_Retry_Header_And_Archived_Response(
        string destination, string scope, bool document)
    {
        var response = Response() with
        {
            Destination = destination,
            Scope = scope,
            FinalUrl = "https://example.test/rejected",
            ResponseHeaders = [new("rEtRy-AfTeR", "120")],
            SavedAs = "0001-error page.html",
        };

        var finding = Assert.Single(Triage.Rank(Report(response)), Is429Finding);

        Assert.Equal(document ? FindingSeverity.Error : FindingSeverity.Warning, finding.Severity);
        Assert.Contains($"{scope} ({destination})", finding.Title, StringComparison.Ordinal);
        Assert.Contains("https://example.test/rejected", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("received HTTP response status", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("Retry-After: 120", finding.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("No Retry-After", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("does not identify", finding.Detail, StringComparison.Ordinal);
        Assert.Equal("resources/0001-error%20page.html", finding.SeeAlso);
    }

    [Fact(Timeout = 600000)]
    public void A_429_With_No_Retry_Header_Or_Saved_Body_Still_Reports_The_Received_Status()
    {
        var response = Response() with { Body = BodyState.NotRead, BodyBytes = 0 };

        var finding = Assert.Single(Triage.Rank(Report(response)), Is429Finding);

        Assert.Contains("No Retry-After header was received", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("does not rule out rate limiting", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("Body: NotRead, 0 byte(s)", finding.Detail, StringComparison.Ordinal);
        Assert.Equal("network.json", finding.SeeAlso);
    }

    [Fact(Timeout = 600000)]
    public void Error_Text_Mentioning_429_Does_Not_Count_As_A_Received_429()
    {
        var response = Response() with { Status = null, Error = "HTTP 429 was mentioned in a transport error" };
        var report = Report() with
        {
            Network = new NetworkSummary { Requests = 1, Failed = 1, Failures = [response] },
            Html = new HtmlReport { Title = "Error 429 - too many requests" },
        };

        Assert.DoesNotContain(Triage.Rank(report), Is429Finding);
        Assert.DoesNotContain("### HTTP 429 responses", MarkdownReport.Write(report), StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void A_429_Beyond_The_Capped_Failure_List_Is_Still_Reported()
    {
        var priorFailures = Enumerable.Range(1, 40)
            .Select(id => Response() with { Id = id, Status = 404 })
            .ToArray();
        var rejected = Response() with { Id = 41 };
        var report = Report(rejected) with
        {
            Network = new NetworkSummary
            {
                Requests = 41,
                Failed = 41,
                Failures = priorFailures,
                Http429Responses = [rejected],
            },
        };

        var finding = Assert.Single(Triage.Rank(report), Is429Finding);
        Assert.Contains("Request #41", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("### HTTP 429 responses", MarkdownReport.Write(report), StringComparison.Ordinal);
    }

    [Theory(Timeout = 600000)]
    [InlineData("120")]
    [InlineData("Wed, 21 Oct 2026 07:28:00 GMT")]
    public void Markdown_Preserves_Retry_After_And_Does_Not_Claim_The_Header_Is_Missing(string retryAfter)
    {
        var response = Response() with
        {
            ResponseHeaders = [new("Retry-After", retryAfter)],
            SavedAs = "0001-response.html",
        };
        var markdown = MarkdownReport.Write(Report(response));

        Assert.Contains(retryAfter, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("No Retry-After header was received", markdown, StringComparison.Ordinal);
        Assert.Contains("[0001-response.html](resources/0001-response.html)", markdown, StringComparison.Ordinal);
        Assert.Contains("Complete, 128 B", markdown, StringComparison.Ordinal);
        Assert.Contains("analysis ×1", markdown, StringComparison.Ordinal);
        Assert.Contains("before session processing", markdown, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public void Markdown_Escapes_Response_Supplied_Text_In_The_429_Table()
    {
        var response = Response() with { ResponseHeaders = [new("Retry-After", "<script>alert(1)</script>|120")] };

        var markdown = MarkdownReport.Write(Report(response));

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;\\|120", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", markdown, StringComparison.Ordinal);
    }

    private static bool Is429Finding(Finding finding) =>
        finding.Title.StartsWith("HTTP 429 Too Many Requests received", StringComparison.Ordinal);

    private static NetworkEntry Response() => new()
    {
        Id = 1,
        Started = System.DateTime.UtcNow,
        Method = "GET",
        Url = "https://example.test/search?q=test",
        Destination = "document",
        Mode = "Navigate",
        Status = 429,
        Body = BodyState.Complete,
        BodyBytes = 128,
    };

    private static AnalysisReport Report(params NetworkEntry[] rejected) => new()
    {
        Url = "https://example.test/search?q=test",
        StartedAt = System.DateTime.UtcNow,
        Environment = new AnalysisEnvironment("Broiler.JS", "0", "rt", "os", "x64", "Release", "--analyze x", new Dictionary<string, string>()),
        Network = new NetworkSummary
        {
            Requests = rejected.Length,
            Failed = rejected.Length,
            Failures = rejected,
            Http429Responses = rejected,
            RequestsByScope = rejected.GroupBy(static response => response.Scope)
                .ToDictionary(static group => group.Key, static group => group.Count()),
        },
    };
}
