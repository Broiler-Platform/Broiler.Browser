using System.Text.Json;
using Broiler.HtmlBridge.Core.Diagnostics;
using Broiler.HtmlBridge.Logging;

namespace Broiler.Cli.Tests;

/// <summary>
/// Covers the <c>--diagnostic-dir</c>/<c>--diagnostic-log</c> bundle: that a run's JavaScript
/// failures reach the log as they happen, that every page and script the engine touched is archived
/// with a manifest, and — the property the whole feature rests on — that none of it runs unless it
/// was asked for.
/// </summary>
/// <remarks>
/// <see cref="ResourceTrace"/> and <see cref="RenderLogger"/> are process-wide, so these tests share
/// them with anything else running concurrently. Each assertion is therefore written to tolerate
/// entries it did not produce: it looks for its own resources by URL rather than asserting on totals.
/// The one exception is the off-by-default test, which is guarded by starting no session at all.
/// </remarks>
[Collection("DiagnosticSession")]
public sealed class DiagnosticSessionTests
{
    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "broiler-diag-" + Guid.NewGuid().ToString("N"));

    [Fact(Timeout = 600000)]
    public void No_Arguments_Starts_Nothing()
    {
        var options = Program.ResolveDiagnosticOptions(directory: null, logPath: null);

        Assert.False(options.IsActive);
        Assert.Null(DiagnosticSession.Start(options));
    }

    [Fact(Timeout = 600000)]
    public void A_Directory_Puts_The_Log_Inside_It()
    {
        var options = Program.ResolveDiagnosticOptions("/tmp/bundle", logPath: null);

        Assert.True(options.IsActive);
        Assert.Equal(Path.Combine("/tmp/bundle", "javascript-errors.log"), options.LogPath);
    }

    [Fact(Timeout = 600000)]
    public void An_Explicit_Log_Path_Wins_Over_The_Default()
    {
        var options = Program.ResolveDiagnosticOptions("/tmp/bundle", "/var/log/js.log");

        Assert.Equal("/var/log/js.log", options.LogPath);
    }

    [Fact(Timeout = 600000)]
    public void A_Log_Alone_Archives_Nothing()
    {
        var options = Program.ResolveDiagnosticOptions(directory: null, logPath: "/tmp/js.log");

        Assert.True(options.IsActive);
        Assert.Null(options.Directory);
    }

    [Fact(Timeout = 600000)]
    public void Nothing_Listens_When_No_Session_Is_Running()
    {
        // The guarantee every traced fetch site depends on: with no host asking for a trace, the
        // call sites see an inactive sink and do no work at all.
        Assert.False(ResourceTrace.IsActive);
    }

    [Fact(Timeout = 600000)]
    public void Failures_Reach_The_Log_Before_The_Session_Ends()
    {
        var directory = NewDirectory();
        try
        {
            using (var session = DiagnosticSession.Start(Program.ResolveDiagnosticOptions(directory, null)))
            {
                Assert.NotNull(session);
                RenderLogger.LogError(
                    LogCategory.JavaScript,
                    "DiagnosticSessionTests",
                    "Script inline-0 failed: someMarkerApi is not defined",
                    new InvalidOperationException("someMarkerApi is not defined"));

                // Read while the session is still open: a run that hangs or is killed must still
                // leave its failures on disk, which a write-at-exit design would not.
                var streamed = ReadShared(Path.Combine(directory, "javascript-errors.log"));
                Assert.Contains("someMarkerApi is not defined", streamed, StringComparison.Ordinal);
                Assert.Contains("InvalidOperationException", streamed, StringComparison.Ordinal);
            }
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact(Timeout = 600000)]
    public void Console_Output_Is_Kept_Apart_From_Failures()
    {
        var directory = NewDirectory();
        try
        {
            using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(directory, null)))
            {
                RenderLogger.LogDebug(LogCategory.JavaScript, "console.log", "console-marker-log");
                RenderLogger.Log(LogCategory.JavaScript, LogLevel.Error, "console.error", "console-marker-error");
            }

            var console = File.ReadAllText(Path.Combine(directory, "console.log"));
            Assert.Contains("console-marker-log", console, StringComparison.Ordinal);
            Assert.Contains("console-marker-error", console, StringComparison.Ordinal);

            // console.log is not a failure and must not pad the error log; console.error is one and
            // belongs in both.
            var failures = File.ReadAllText(Path.Combine(directory, "javascript-errors.log"));
            Assert.DoesNotContain("console-marker-log", failures, StringComparison.Ordinal);
            Assert.Contains("console-marker-error", failures, StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact(Timeout = 600000)]
    public void Every_Resource_Is_Archived_With_A_Manifest_Row()
    {
        var directory = NewDirectory();
        try
        {
            using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(directory, null)))
            {
                ResourceTrace.RecordBody(
                    ResourceTraceKind.Document, "https://example.test/page", "<html>marker-page</html>");
                ResourceTrace.RecordBody(
                    ResourceTraceKind.ExecutedScript, "https://example.test/page#inline-0", "var marker = 1;", "inline-0");
            }

            var rows = Manifest(directory);

            var page = Assert.Single(rows, r => Url(r) == "https://example.test/page");
            Assert.Equal(
                "<html>marker-page</html>",
                File.ReadAllText(Path.Combine(directory, "resources", SavedAs(page)!)));

            var script = Assert.Single(rows, r => Url(r) == "https://example.test/page#inline-0");
            Assert.Equal("inline-0", script.GetProperty("Label").GetString());
            // A script's archive is named .js even though the synthetic URL ends in the page's path.
            Assert.EndsWith(".js", SavedAs(script), StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact(Timeout = 600000)]
    public void Identical_Bytes_Are_Stored_Once_And_Map_The_Label_To_Its_Url()
    {
        var directory = NewDirectory();
        try
        {
            const string source = "var deduplicationMarker = 1;";
            using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(directory, null)))
            {
                ResourceTrace.RecordBody(ResourceTraceKind.Script, "https://example.test/app.js", source);
                ResourceTrace.RecordBody(
                    ResourceTraceKind.ExecutedScript, "https://example.test/page#inline-4", source, "inline-4");
            }

            var rows = Manifest(directory);
            var fetched = Assert.Single(rows, r => Url(r) == "https://example.test/app.js");
            var executed = Assert.Single(rows, r => Url(r) == "https://example.test/page#inline-4");

            // Both rows point at one file — which is exactly the mapping from the label the error log
            // prints to the URL the bytes came from.
            Assert.Equal(SavedAs(fetched), SavedAs(executed));
            Assert.Single(
                Directory.GetFiles(Path.Combine(directory, "resources"), "*.js"));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact(Timeout = 600000)]
    public void A_Failed_Fetch_Is_Recorded_As_A_Failure()
    {
        var directory = NewDirectory();
        try
        {
            using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(directory, null)))
            {
                ResourceTrace.Begin(ResourceTraceKind.Script, "https://example.test/missing.js")
                    .Failed(new HttpRequestException("404 marker"));
            }

            var row = Assert.Single(Manifest(directory), r => Url(r) == "https://example.test/missing.js");
            Assert.Equal("404 marker", row.GetProperty("Error").GetString());
            // Nothing was received, so nothing is archived — but the attempt is still on the record.
            Assert.Null(SavedAs(row));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact(Timeout = 600000)]
    public void The_Summary_Ranks_Failures_And_Names_The_Missing_Features()
    {
        var directory = NewDirectory();
        try
        {
            using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(directory, null)))
            {
                for (var i = 0; i < 3; i++)
                {
                    RenderLogger.Log(
                        LogCategory.JavaScript, LogLevel.Error, "DiagnosticSessionTests",
                        $"Script inline-{i} failed: requestIdleCallbackMarker is not defined");
                }

                RenderLogger.Log(
                    LogCategory.JavaScript, LogLevel.Error, "DiagnosticSessionTests",
                    "Cannot get property scheduleMarker of undefined");
            }

            var summary = File.ReadAllText(Path.Combine(directory, "summary.md"));

            // The three per-script spellings are one defect, so the digest counts them together.
            Assert.Contains("**×3**", summary, StringComparison.Ordinal);
            Assert.Contains("requestIdleCallbackMarker", summary, StringComparison.Ordinal);
            Assert.Contains("scheduleMarker", summary, StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact(Timeout = 600000)]
    public void Disposing_Unsubscribes_So_A_Later_Run_Is_Not_Recorded()
    {
        var directory = NewDirectory();
        try
        {
            using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(directory, null)))
            {
            }

            Assert.False(ResourceTrace.IsActive);
            RenderLogger.Log(
                LogCategory.JavaScript, LogLevel.Error, "DiagnosticSessionTests", "after-dispose-marker");

            Assert.DoesNotContain(
                "after-dispose-marker",
                File.ReadAllText(Path.Combine(directory, "javascript-errors.log")),
                StringComparison.Ordinal);
        }
        finally
        {
            Delete(directory);
        }
    }

    private static JsonElement[] Manifest(string directory) =>
        [.. JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "resources", "index.json")))
            .RootElement.EnumerateArray()];

    private static string? Url(JsonElement row) => row.GetProperty("Url").GetString();

    private static string? SavedAs(JsonElement row) =>
        row.GetProperty("SavedAs") is { ValueKind: not JsonValueKind.Null } value ? value.GetString() : null;

    /// <summary>Reads a file the session still has open for writing.</summary>
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// The diagnostics sinks are process-wide statics, so these suites must not overlap: two sessions
/// listening at once would each record the other's entries.
/// </summary>
[CollectionDefinition("DiagnosticSession", DisableParallelization = true)]
public sealed class DiagnosticSessionCollection;
