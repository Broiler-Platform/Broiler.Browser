using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.HtmlBridge.Core.Diagnostics;
using Broiler.HtmlBridge.Logging;

namespace Broiler.Cli;

// Disambiguate the unqualified `DateTime` type: the Broiler.JS engine exposes a top-level
// `Broiler.DateTime` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.DateTime by simple-name lookup.
using DateTime = System.DateTime;

/// <summary>
/// Where a diagnostic run writes. Either half may be off: a bare
/// <c>--diagnostic-log</c> records the JavaScript failures and nothing else.
/// </summary>
internal sealed record DiagnosticOptions
{
    /// <summary>The bundle directory, or null when only a log was asked for.</summary>
    public string? Directory { get; init; }

    /// <summary>The JavaScript error log, defaulted inside <see cref="Directory"/> when one is set.</summary>
    public string? LogPath { get; init; }

    /// <summary>Whether anything at all was requested.</summary>
    public bool IsActive => Directory is not null || LogPath is not null;
}

/// <summary>
/// The <c>--diagnostic-dir</c> / <c>--diagnostic-log</c> recorder: for one capture, it writes every
/// JavaScript failure to a log as it happens and archives every page, script and sub-resource the
/// engine fetched or ran.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it is for.</b> A heavily-scripted page — a search-results page, above all — fails as a
/// cascade: one missing API throws, the bootstrap that would have built the page never finishes, and
/// what renders is a fragment with no clue in it about why. The rendered output cannot answer "which
/// script, which call, which resource"; this can, by keeping the evidence rather than the conclusion.
/// </para>
/// <para>
/// <b>The log is streamed, not summarised at the end.</b> The runs that most need diagnosing are the
/// ones that hang, blow the timeout, or die — exactly the runs where a buffer-and-write-at-exit
/// design produces an empty file. Each failure is written and flushed as it is logged, so whatever
/// the process does next, the log holds everything up to that moment.
/// </para>
/// <para>
/// <b>Identical bytes are stored once.</b> A script is archived both as fetched (under its URL) and
/// as executed (under the <c>inline-7</c> label the error log names it by). Storing the second copy
/// would double a multi-megabyte bundle for no information, so the manifest entry points at the file
/// already written — which incidentally <em>is</em> the label→URL mapping a reader wants.
/// </para>
/// <para>
/// <b>It cannot change the capture.</b> Every handler swallows its own I/O failures and
/// <see cref="ResourceTrace"/> swallows what a handler throws, so a full disk or an unwritable path
/// degrades the bundle and nothing else. Diagnostics observe a run; they do not participate in it.
/// </para>
/// </remarks>
internal sealed class DiagnosticSession : IDisposable
{
    /// <summary>
    /// Kept entries are bounded: a runaway script can log without limit, and a diagnostic aid that
    /// exhausts memory on the pages that need it most would be worse than none. The streamed log is
    /// unbounded — it is the one that must not lose anything.
    /// </summary>
    private const int MaxRetainedEntries = 50_000;

    /// <summary>
    /// The label <see cref="ScriptedPage.Serialize"/> archives the post-script DOM under. Shared so the
    /// summary can pair it with the document as fetched.
    /// </summary>
    internal const string AfterScriptsLabel = "document-after-scripts";

    private readonly DiagnosticOptions _options;
    private readonly Lock _sync = new();
    private readonly List<RenderLogEntry> _entries = [];
    private readonly List<ResourceRecord> _resources = [];
    private readonly Dictionary<string, string> _fileNamesByContentHash = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedFileNames = new(StringComparer.OrdinalIgnoreCase);

    private Action<RenderLogEntry>? _logHandler;
    private Action<ResourceTraceEntry>? _resourceHandler;
    private StreamWriter? _log;
    private StreamWriter? _consoleLog;
    private string? _resourceDirectory;
    private int _droppedEntries;
    private int _errorCount;

    private DiagnosticSession(DiagnosticOptions options) => _options = options;

    /// <summary>The directory the bundle was written to, or null when only a log was requested.</summary>
    public string? Directory => _options.Directory;

    /// <summary>Errors and warnings written to the log so far.</summary>
    public int ErrorCount { get { lock (_sync) return _errorCount; } }

    /// <summary>Resources archived so far.</summary>
    public int ResourceCount { get { lock (_sync) return _resources.Count; } }

    /// <summary>
    /// Creates the bundle and starts recording, or returns null when <paramref name="options"/> asked
    /// for nothing. Throws <see cref="IOException"/> if the destination cannot be created — that is a
    /// mistyped argument, and failing at the start is better than a silent no-op run.
    /// </summary>
    public static DiagnosticSession? Start(DiagnosticOptions options)
    {
        if (!options.IsActive)
            return null;

        var session = new DiagnosticSession(options);
        session.Open();
        return session;
    }

    private void Open()
    {
        if (_options.Directory is { } directory)
        {
            System.IO.Directory.CreateDirectory(directory);
            _resourceDirectory = Path.Combine(directory, "resources");
            System.IO.Directory.CreateDirectory(_resourceDirectory);

            _consoleLog = OpenWriter(Path.Combine(directory, "console.log"));
            _resourceHandler = OnResource;
            ResourceTrace.Recorded += _resourceHandler;
        }

        if (_options.LogPath is { } logPath)
        {
            var logDirectory = Path.GetDirectoryName(Path.GetFullPath(logPath));
            if (!string.IsNullOrEmpty(logDirectory))
                System.IO.Directory.CreateDirectory(logDirectory);

            _log = OpenWriter(logPath);
            _log.WriteLine(
                $"# Broiler JavaScript diagnostics — started {DateTime.UtcNow:o}");
            _log.Flush();
        }

        _logHandler = OnLogEntry;
        RenderLogger.EntryLogged += _logHandler;

        // A rejected promise nobody handled is a failure the host would otherwise never hear about:
        // it propagates through the promise machinery rather than out of an Eval, so no catch block
        // in the capture path sees it. The engine collects them only while this is on; the capture
        // reports what is left once its microtask checkpoint has drained.
        UnhandledRejections.Track(true);
    }

    private static StreamWriter OpenWriter(string path) =>
        // Truncating is deliberate: a bundle describes one run, and appending across runs would
        // silently interleave two pages' failures in a file whose whole value is being unambiguous.
        new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));

    // ── Recording ───────────────────────────────────────────────────────────────────────────────

    private void OnLogEntry(RenderLogEntry entry)
    {
        try
        {
            var isConsole = entry.Context.StartsWith("console.", StringComparison.Ordinal);
            var isFailure = entry.Category == LogCategory.JavaScript && entry.Level >= LogLevel.Warning;

            lock (_sync)
            {
                if (_entries.Count < MaxRetainedEntries)
                    _entries.Add(entry);
                else
                    _droppedEntries++;

                if (isConsole && _consoleLog is { } console)
                {
                    console.WriteLine(
                        $"{entry.Timestamp:o} {entry.Context,-14} {entry.Message}");
                    console.Flush();
                }

                if (!isFailure)
                    return;

                _errorCount++;
                if (_log is not { } log)
                    return;

                log.WriteLine(
                    $"{entry.Timestamp:o} {entry.Level,-7} [{entry.Category}/{entry.Context}] {entry.Message}");
                if (entry.Exception is { } exception)
                {
                    foreach (var line in exception.ToString().Split('\n'))
                        log.WriteLine("    " + line.TrimEnd('\r'));
                }

                log.Flush();
            }
        }
        catch (IOException)
        {
            // See the type remarks: a diagnostics sink never breaks the run it observes.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnResource(ResourceTraceEntry entry)
    {
        try
        {
            lock (_sync)
            {
                var ordinal = _resources.Count;
                string? savedAs = null;

                if (entry.Content is { } content && _resourceDirectory is { } directory)
                {
                    var hash = ContentHash(content);
                    if (_fileNamesByContentHash.TryGetValue(hash, out var existing))
                    {
                        savedAs = existing;
                    }
                    else
                    {
                        savedAs = UniqueFileName(ordinal, entry);
                        File.WriteAllText(Path.Combine(directory, savedAs), content);
                        _fileNamesByContentHash[hash] = savedAs;
                    }
                }

                _resources.Add(new ResourceRecord(
                    Ordinal: ordinal,
                    Url: entry.Url,
                    Kind: entry.Kind.ToString(),
                    Label: entry.Label,
                    SavedAs: savedAs,
                    Bytes: entry.Content is { } text ? Encoding.UTF8.GetByteCount(text) : 0,
                    StatusCode: entry.StatusCode,
                    ContentType: entry.ContentType,
                    Method: entry.Method,
                    ElapsedMs: Math.Round(entry.ElapsedMs, 2),
                    Error: entry.Error,
                    Timestamp: entry.Timestamp));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── Naming ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A name that sorts in fetch order, says what it is at a glance, and cannot collide. The
    /// ordinal supplies the ordering and the uniqueness; the readable stem is a convenience, and is
    /// truncated because a URL can be far longer than a filesystem allows.
    /// </summary>
    private string UniqueFileName(int ordinal, ResourceTraceEntry entry)
    {
        var stem = Sanitize(entry.Label ?? StemFromUrl(entry.Url));
        if (stem.Length > 60)
            stem = stem[..60];
        if (stem.Length == 0)
            stem = entry.Kind.ToString().ToLowerInvariant();

        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{ordinal:D4}-{stem}{ExtensionFor(entry)}");

        // Ordinals are unique by construction, so this only fires if a host ever records two entries
        // under one ordinal; keeping it means a collision can never silently overwrite evidence.
        var attempt = 1;
        while (!_usedFileNames.Add(name))
            name = string.Create(CultureInfo.InvariantCulture, $"{ordinal:D4}-{stem}-{attempt++}{ExtensionFor(entry)}");

        return name;
    }

    /// <summary>
    /// The readable part of an archived name. Only the last few path segments are kept: what
    /// distinguishes two URLs is almost always at the end of the path, and a deep one truncated from
    /// the front produces a directory of files that all begin with the same forty characters.
    /// </summary>
    private static string StemFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var leaf = segments.Length == 0
            ? "index"
            : string.Join('-', segments[^Math.Min(3, segments.Length)..]);

        // The extension is appended separately, so keeping the URL's would yield "app.js.js".
        var dot = leaf.LastIndexOf('.');
        if (dot > 0 && leaf.Length - dot <= 6)
            leaf = leaf[..dot];

        return uri.IsFile ? leaf : uri.Host + "-" + leaf;
    }

    /// <summary>
    /// The extension the archived file gets: the URL's own when it has a plausible one, otherwise the
    /// one implied by the content type or the kind. It exists so an editor opens the file with the
    /// right syntax — nothing reads it back.
    /// </summary>
    private static string ExtensionFor(ResourceTraceEntry entry)
    {
        // A labelled entry's URL is synthetic — the page plus "#inline-7" — so its extension
        // describes the page, not the script. Only an entry that was really fetched from that URL
        // may take its extension from it.
        if (entry.Label is null && Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (extension.Length is > 1 and <= 6 && extension.All(c => char.IsLetterOrDigit(c) || c == '.'))
                return extension;
        }

        if (entry.ContentType is { } contentType)
        {
            if (contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)) return ".js";
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase)) return ".json";
            if (contentType.Contains("css", StringComparison.OrdinalIgnoreCase)) return ".css";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase)) return ".html";
        }

        return entry.Kind switch
        {
            ResourceTraceKind.Script or ResourceTraceKind.ExecutedScript => ".js",
            ResourceTraceKind.Stylesheet => ".css",
            ResourceTraceKind.Document or ResourceTraceKind.SubDocument => ".html",
            _ => ".txt",
        };
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');

        return builder.ToString().Trim('-');
    }

    private static string ContentHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    // ── Reporting ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unsubscribes, then writes the manifest, the structured record and the human-readable summary.
    /// Safe to call twice.
    /// </summary>
    public void Dispose()
    {
        if (_logHandler is { } logHandler)
        {
            RenderLogger.EntryLogged -= logHandler;
            _logHandler = null;
        }

        if (_resourceHandler is { } resourceHandler)
        {
            ResourceTrace.Recorded -= resourceHandler;
            _resourceHandler = null;
        }

        // Discards anything still collected, so a later run in this process cannot inherit it.
        UnhandledRejections.Track(false);

        try
        {
            WriteReports();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        // Under the lock, because unsubscribing does not join: a handler already dispatched on a
        // prefetch worker can be inside its write when this runs, and closing the stream from under
        // it would raise an ObjectDisposedException in the middle of a render. Waiting for the lock
        // means no handler is mid-write, and any that arrives afterwards finds a null writer.
        lock (_sync)
        {
            _log?.Dispose();
            _log = null;
            _consoleLog?.Dispose();
            _consoleLog = null;
        }
    }

    private void WriteReports()
    {
        if (_options.Directory is not { } directory)
            return;

        RenderLogEntry[] entries;
        ResourceRecord[] resources;
        int dropped;
        lock (_sync)
        {
            entries = [.. _entries];
            resources = [.. _resources];
            dropped = _droppedEntries;
        }

        var failures = entries
            .Where(static e => e.Category == LogCategory.JavaScript && e.Level >= LogLevel.Warning)
            .ToArray();
        var messages = failures.Select(static e => e.Message).ToArray();
        var groups = JsErrorDigest.Group(messages, failures.Select(static e => e.Context).ToArray());
        var missing = JsErrorDigest.MissingApis(messages);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        if (_resourceDirectory is not null)
        {
            File.WriteAllText(
                Path.Combine(_resourceDirectory, "index.json"),
                JsonSerializer.Serialize(resources, jsonOptions));
        }

        File.WriteAllText(
            Path.Combine(directory, "diagnostics.json"),
            JsonSerializer.Serialize(
                new
                {
                    generatedAt = DateTime.UtcNow.ToString("o"),
                    totals = new
                    {
                        logEntries = entries.Length,
                        droppedLogEntries = dropped,
                        javascriptFailures = failures.Length,
                        distinctFailures = groups.Count,
                        resources = resources.Length,
                        failedResources = resources.Count(static r => r.Error is not null),
                    },
                    distinctErrors = groups.Select(static g => new { g.Count, g.Example, context = g.FirstContext }),
                    missingApis = missing,
                    entries = entries.Select(static e => new
                    {
                        timestamp = e.Timestamp.ToString("o"),
                        category = e.Category.ToString(),
                        level = e.Level.ToString(),
                        context = e.Context,
                        message = e.Message,
                        exception = e.Exception?.ToString(),
                    }),
                },
                jsonOptions));

        File.WriteAllText(Path.Combine(directory, "summary.md"), BuildSummary(groups, missing, resources, dropped));
    }

    private static string BuildSummary(
        IReadOnlyList<JsErrorGroup> groups,
        IReadOnlyList<MissingApi> missing,
        IReadOnlyList<ResourceRecord> resources,
        int dropped)
    {
        var summary = new StringBuilder();
        summary.AppendLine("# Broiler capture diagnostics").AppendLine();
        summary.Append("Generated ").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).AppendLine().AppendLine();

        summary.AppendLine("| Measure | Value |");
        summary.AppendLine("| --- | --- |");
        summary.Append("| JavaScript failures | ").Append(groups.Sum(static g => g.Count)).AppendLine(" |");
        summary.Append("| Distinct failures | ").Append(groups.Count).AppendLine(" |");
        summary.Append("| Resources recorded | ").Append(resources.Count).AppendLine(" |");
        summary.Append("| Resources that failed | ").Append(resources.Count(static r => r.Error is not null)).AppendLine(" |");
        if (dropped > 0)
            summary.Append("| Log entries dropped (retention cap) | ").Append(dropped).AppendLine(" |");
        summary.AppendLine();

        // The one number that answers "did the page's JavaScript build anything?" without opening a
        // file. A scripted page that renders as a fragment usually shows a document that scripts
        // barely changed — which points at the failures above rather than at layout or paint.
        var fetched = resources.FirstOrDefault(static r => r.Kind == nameof(ResourceTraceKind.Document) && r.Label is null);
        var afterScripts = resources.FirstOrDefault(static r => r.Label == AfterScriptsLabel);
        if (fetched is not null && afterScripts is not null)
        {
            var delta = afterScripts.Bytes - fetched.Bytes;
            summary.AppendLine("## What the scripts did to the document").AppendLine();
            summary.AppendLine("| Stage | Bytes |");
            summary.AppendLine("| --- | --- |");
            summary.Append("| As fetched | ").Append(fetched.Bytes.ToString("N0", CultureInfo.InvariantCulture)).AppendLine(" |");
            summary.Append("| After scripts | ").Append(afterScripts.Bytes.ToString("N0", CultureInfo.InvariantCulture)).AppendLine(" |");
            summary.Append("| Change | ").Append(delta.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture)).AppendLine(" |");
            summary.AppendLine();
            summary.Append("Both documents are in `resources/`: `").Append(fetched.SavedAs)
                .Append("` and `").Append(afterScripts.SavedAs).AppendLine("`. Diff them to see exactly what ran.");
            summary.AppendLine();
        }

        if (missing.Count > 0)
        {
            summary.AppendLine("## Platform features the page asked for and did not get").AppendLine();
            summary.AppendLine("Inferred from the failure messages; the count is how often each was blamed.").AppendLine();
            summary.AppendLine("| Name | Kind | Failures |");
            summary.AppendLine("| --- | --- | --- |");
            foreach (var api in missing.Take(40))
                summary.Append("| `").Append(api.Name).Append("` | ").Append(api.Kind).Append(" | ").Append(api.Count).AppendLine(" |");
            summary.AppendLine();
        }

        if (groups.Count > 0)
        {
            summary.AppendLine("## Distinct failures, most frequent first").AppendLine();
            foreach (var group in groups.Take(40))
            {
                summary.Append("- **×").Append(group.Count).Append("** ");
                if (group.FirstContext is { Length: > 0 } context)
                    summary.Append('`').Append(context).Append("` — ");
                summary.AppendLine(Escape(group.Example));
            }

            summary.AppendLine();
        }

        var failed = resources.Where(static r => r.Error is not null).ToArray();
        if (failed.Length > 0)
        {
            summary.AppendLine("## Resources that failed to load").AppendLine();
            foreach (var resource in failed.Take(40))
                summary.Append("- `").Append(resource.Url).Append("` — ").AppendLine(Escape(resource.Error ?? string.Empty));
            summary.AppendLine();
        }

        var slowest = resources
            .Where(static r => r.ElapsedMs > 0)
            .OrderByDescending(static r => r.ElapsedMs)
            .Take(10)
            .ToArray();
        if (slowest.Length > 0)
        {
            summary.AppendLine("## Slowest resources").AppendLine();
            summary.AppendLine("| ms | Kind | URL |");
            summary.AppendLine("| --- | --- | --- |");
            foreach (var resource in slowest)
            {
                summary.Append("| ").Append(resource.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture))
                    .Append(" | ").Append(resource.Kind)
                    .Append(" | `").Append(resource.Url).AppendLine("` |");
            }

            summary.AppendLine();
        }

        return summary.ToString();
    }

    /// <summary>Keeps a message with a newline or a pipe in it from breaking the Markdown around it.</summary>
    private static string Escape(string value) =>
        value.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>A one-line result for the console, so a run says where its evidence went.</summary>
    public string Describe()
    {
        lock (_sync)
        {
            var where = _options.Directory ?? _options.LogPath;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Diagnostics: {_errorCount} JavaScript failure(s), {_resources.Count} resource(s) → {where}");
        }
    }

    /// <summary>One archived resource, as it appears in <c>resources/index.json</c>.</summary>
    private sealed record ResourceRecord(
        int Ordinal,
        string Url,
        string Kind,
        string? Label,
        string? SavedAs,
        int Bytes,
        int? StatusCode,
        string? ContentType,
        string? Method,
        double ElapsedMs,
        string? Error,
        DateTime Timestamp);
}
