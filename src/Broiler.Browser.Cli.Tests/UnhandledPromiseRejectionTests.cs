using Broiler.HtmlBridge.Logging;

namespace Broiler.Cli.Tests;

/// <summary>
/// Covers the failures a capture used to lose entirely: a promise rejected with nothing waiting on
/// it. They reach no <c>catch</c> in the capture path — they propagate through the promise
/// machinery instead — so before the engine tracked them a page could fail throughout and the
/// diagnostics bundle would report zero failures.
/// </summary>
/// <remarks>
/// <para>
/// Each script runs as a page through a real capture, inside a diagnostics session, because the
/// engine only tracks rejections while one is running.
/// </para>
/// <para>
/// The half that matters most here is <see cref="Handled_Rejections_Are_Not_Reported"/>. Reporting
/// an unhandled rejection is easy; not reporting a handled one is the whole difficulty, because a
/// handler is routinely attached after the promise has already settled.
/// </para>
/// </remarks>
[Collection("DiagnosticSession")]
public sealed class UnhandledPromiseRejectionTests
{
    /// <summary>Runs <paramref name="script"/> through a capture and returns what was logged.</summary>
    private static async Task<string[]> CaptureFailuresAsync(string script)
    {
        var messages = new List<string>();
        void Collect(RenderLogEntry entry)
        {
            if (entry.Category == LogCategory.JavaScript && entry.Level >= LogLevel.Warning)
                lock (messages) messages.Add(entry.Message);
        }

        using var pages = new TestPages();
        var url = pages.Write("page.html", $"<!DOCTYPE html><html><body><script>{script}</script></body></html>");

        RenderLogger.EntryLogged += Collect;
        try
        {
            using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(pages.Output("bundle"), null)))
            {
                await new CaptureService().CaptureAsync(new CaptureOptions
                {
                    Url = url,
                    OutputPath = pages.Output("out.html"),
                });
            }
        }
        finally
        {
            RenderLogger.EntryLogged -= Collect;
        }

        lock (messages) return [.. messages];
    }

    [Theory]
    // The three ways a page produces one, which are three different code paths in the engine: a
    // throw inside a reaction rejects the derived promise, while the other two mint a promise that
    // is already rejected without ever passing through Reject.
    [InlineData("Promise.resolve().then(function () { throw new Error('rejectionMarker'); });")]
    [InlineData("Promise.reject(new Error('rejectionMarker'));")]
    [InlineData("(async function () { throw new Error('rejectionMarker'); })();")]
    public async Task An_Unclaimed_Rejection_Is_Reported(string script)
    {
        var failures = await CaptureFailuresAsync(script);

        Assert.Contains(failures, m => m.Contains("Unhandled promise rejection", StringComparison.Ordinal));
        Assert.Contains(failures, m => m.Contains("rejectionMarker", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Promise.reject(new Error('handledMarker')).catch(function (e) {});")]
    [InlineData("Promise.resolve().then(function () { throw new Error('handledMarker'); }).catch(function (e) {});")]
    [InlineData("Promise.reject(new Error('handledMarker')).then(null, function (e) {});")]
    [InlineData("(async function () { try { await Promise.reject(new Error('handledMarker')); } catch (e) {} })();")]
    [InlineData("Promise.all([Promise.reject(new Error('handledMarker'))]).catch(function (e) {});")]
    // The case that forces the report to be deferred rather than raised at the rejection: the
    // promise is already rejected when the handler is attached, a microtask later.
    [InlineData("var p = Promise.reject(new Error('handledMarker')); Promise.resolve().then(function () { p.catch(function (e) {}); });")]
    // The same across a timer: the handler arrives in a callback the load window runs, so a report
    // taken before the window settled would call it unhandled.
    [InlineData("var p = Promise.reject(new Error('handledMarker')); setTimeout(function () { p.catch(function (e) {}); }, 0);")]
    public async Task Handled_Rejections_Are_Not_Reported(string script)
    {
        Assert.DoesNotContain(
            await CaptureFailuresAsync(script),
            m => m.Contains("Unhandled promise rejection", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public async Task A_Rejection_Is_Reported_Once_Per_Promise()
    {
        var failures = await CaptureFailuresAsync(
            "Promise.reject(new Error('firstMarker')); Promise.reject(new Error('secondMarker'));");

        Assert.Equal(
            2,
            failures.Count(m => m.Contains("Unhandled promise rejection", StringComparison.Ordinal)));
    }

    [Fact(Timeout = 600000)]
    public async Task The_Report_Names_The_Error()
    {
        var failures = await CaptureFailuresAsync("Promise.reject(new TypeError('describedMarker'));");

        // The reason is described as a console would describe it — name and message — rather than
        // as "[object Object]", which would say nothing about what failed.
        Assert.Contains(failures, m => m.Contains("TypeError: describedMarker", StringComparison.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public async Task A_Non_Error_Reason_Is_Still_Described()
    {
        var failures = await CaptureFailuresAsync("Promise.reject('plainStringMarker');");

        Assert.Contains(failures, m => m.Contains("plainStringMarker", StringComparison.Ordinal));
    }
}
