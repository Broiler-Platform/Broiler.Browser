using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Cli.Analysis;
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

    /// <summary>
    /// Names what the run is doing when an exception is recorded, for the exception log. A capture
    /// has one phase; <c>--analyze</c> passes its phase clock.
    /// </summary>
    public Func<string>? Phase { get; init; }

    /// <summary>
    /// Whether the bundle writes its own <c>summary.md</c>. <c>--analyze</c> writes a report that
    /// covers it and turns this off.
    /// </summary>
    public bool WriteSummary { get; init; } = true;
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
/// <para>
/// <b>A bundle also keeps every exception and every message.</b> <c>exceptions.log</c> records each
/// exception raised in the process while the bundle is open, first-chance ones included — the
/// stylesheet value that did not parse, the image that did not decode, the layout pass that fell
/// back, each caught and recovered from and invisible in the output otherwise (see
/// <see cref="ExceptionRecorder"/>). <c>messages.log</c> is every entry the pipeline logged, at every
/// level and in every category, where the JavaScript log keeps only the failures.
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
    private StreamWriter? _messages;
    private ExceptionRecorder? _exceptions;
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

    /// <summary>The bundle's exception log, or null when only a JavaScript log was requested.</summary>
    public ExceptionRecorder? Exceptions => _exceptions;

    /// <summary>The log entries kept so far, in the order they were logged.</summary>
    public IReadOnlyList<RenderLogEntry> Entries()
    {
        lock (_sync)
            return [.. _entries];
    }

    /// <summary>The archive's manifest so far, as <c>resources/index.json</c> will record it.</summary>
    public IReadOnlyList<ResourceRecord> Resources()
    {
        lock (_sync)
            return [.. _resources];
    }

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

            // First, so that an exception raised while the rest of the bundle opens is on its record.
            _exceptions = ExceptionRecorder.Start(
                Path.Combine(directory, "exceptions.log"),
                Path.Combine(directory, "exceptions.json"),
                _options.Phase ?? (static () => "capture"));

            _consoleLog = OpenWriter(Path.Combine(directory, "console.log"));
            _messages = OpenWriter(Path.Combine(directory, "messages.log"));
            _messages.WriteLine(
                $"# Every message the pipeline logged, every level and category — started {DateTime.UtcNow:o}");
            _messages.Flush();
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

                if (_messages is { } messages)
                {
                    messages.WriteLine(
                        $"{entry.Timestamp:o} {entry.Level,-7} [{entry.Category}/{entry.Context}] {entry.Message}");
                    if (entry.Exception is { } logged)
                    {
                        // Read without running page code: this log records every level, and a
                        // debug entry is no reason to call a page's getter (see ExceptionText).
                        foreach (var line in ExceptionText.Describe(logged).Split('\n'))
                            messages.WriteLine("    " + line.TrimEnd('\r'));
                    }

                    messages.Flush();
                }

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
                    var bytes = Encoding.UTF8.GetBytes(content);
                    savedAs = Store(directory, ordinal, bytes, entry.Url, entry.Label, entry.Kind.ToString(), entry.ContentType);
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
                    Timestamp: entry.Timestamp,
                    RecordedBy: ResourceRecord.Engine));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Archives a response body the network recorder kept — an image, a font, anything the engine's
    /// own trace does not see because it is not text or not the engine's to fetch — beside the
    /// resources the engine recorded, and returns the file it is in. Identical bytes share a file with
    /// whatever was archived first, so a script fetched over the network and traced by the engine is
    /// still one file. Returns null when the bundle keeps no resources.
    /// </summary>
    public string? ArchiveBytes(
        string url,
        string kind,
        byte[] content,
        string? contentType,
        int? statusCode,
        string? method,
        double elapsedMs)
    {
        try
        {
            lock (_sync)
            {
                if (_resourceDirectory is not { } directory)
                    return null;

                var ordinal = _resources.Count;
                var savedAs = Store(directory, ordinal, content, url, label: null, kind, contentType);
                _resources.Add(new ResourceRecord(
                    Ordinal: ordinal,
                    Url: url,
                    Kind: kind,
                    Label: null,
                    SavedAs: savedAs,
                    Bytes: content.Length,
                    StatusCode: statusCode,
                    ContentType: contentType,
                    Method: string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ? null : method,
                    ElapsedMs: Math.Round(elapsedMs, 2),
                    Error: null,
                    Timestamp: DateTime.UtcNow,
                    RecordedBy: ResourceRecord.Network));
                return savedAs;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="bytes"/> once per distinct content, and returns its file name.</summary>
    private string Store(
        string directory,
        int ordinal,
        byte[] bytes,
        string url,
        string? label,
        string kind,
        string? contentType)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (_fileNamesByContentHash.TryGetValue(hash, out var existing))
            return existing;

        var savedAs = UniqueFileName(ordinal, url, label, kind, contentType);
        File.WriteAllBytes(Path.Combine(directory, savedAs), bytes);
        _fileNamesByContentHash[hash] = savedAs;
        return savedAs;
    }

    // ── Naming ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A name that sorts in fetch order, says what it is at a glance, and cannot collide. The
    /// ordinal supplies the ordering and the uniqueness; the readable stem is a convenience, and is
    /// truncated because a URL can be far longer than a filesystem allows.
    /// </summary>
    private string UniqueFileName(int ordinal, string url, string? label, string kind, string? contentType)
    {
        var stem = Sanitize(label ?? StemFromUrl(url));
        if (stem.Length > 60)
            stem = stem[..60];
        if (stem.Length == 0)
            stem = kind.ToLowerInvariant();

        var extension = ExtensionFor(url, label, kind, contentType);
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{ordinal:D4}-{stem}{extension}");

        // Ordinals are unique by construction, so this only fires if a host ever records two entries
        // under one ordinal; keeping it means a collision can never silently overwrite evidence.
        var attempt = 1;
        while (!_usedFileNames.Add(name))
            name = string.Create(CultureInfo.InvariantCulture, $"{ordinal:D4}-{stem}-{attempt++}{extension}");

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

        // A data: URL has no path worth naming a file after, only its payload.
        if (uri.Scheme == "data")
            return "data-url";

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
    /// one implied by the content type or the kind. It exists so an editor or an image viewer opens
    /// the file as what it is — nothing reads it back.
    /// </summary>
    private static string ExtensionFor(string url, string? label, string kind, string? contentType)
    {
        // A labelled entry's URL is synthetic — the page plus "#inline-7" — so its extension
        // describes the page, not the script. Only an entry that was really fetched from that URL
        // may take its extension from it.
        if (label is null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme != "data")
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (extension.Length is > 1 and <= 6 && extension.All(c => char.IsLetterOrDigit(c) || c == '.'))
                return extension;
        }

        if (contentType is not null && ExtensionForContentType(contentType) is { } fromType)
            return fromType;

        return kind switch
        {
            nameof(ResourceTraceKind.Script) or nameof(ResourceTraceKind.ExecutedScript) => ".js",
            nameof(ResourceTraceKind.Stylesheet) => ".css",
            nameof(ResourceTraceKind.Document) or nameof(ResourceTraceKind.SubDocument) => ".html",
            "Image" => ".img",
            "Font" => ".font",
            _ => ".txt",
        };
    }

    private static string? ExtensionForContentType(string contentType)
    {
        var mediaType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/avif" => ".avif",
            "image/svg+xml" => ".svg",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            "image/bmp" => ".bmp",
            "font/woff2" or "application/font-woff2" => ".woff2",
            "font/woff" or "application/font-woff" or "application/x-font-woff" => ".woff",
            "font/ttf" or "application/x-font-ttf" or "font/sfnt" => ".ttf",
            "font/otf" or "application/x-font-otf" or "font/opentype" => ".otf",
            "application/vnd.ms-fontobject" => ".eot",
            "application/xml" or "text/xml" => ".xml",
            "text/plain" => ".txt",
            _ when mediaType.Contains("javascript", StringComparison.Ordinal) || mediaType.EndsWith("ecmascript", StringComparison.Ordinal) => ".js",
            _ when mediaType.Contains("json", StringComparison.Ordinal) => ".json",
            _ when mediaType.Contains("css", StringComparison.Ordinal) => ".css",
            _ when mediaType.Contains("html", StringComparison.Ordinal) => ".html",
            _ => null,
        };
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-');

        return builder.ToString().Trim('-');
    }

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

        // Before the reports, so that exceptions.json is complete by the time diagnostics.json counts it.
        _exceptions?.Dispose();

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
            _messages?.Dispose();
            _messages = null;
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
                        exceptions = _exceptions?.Total ?? 0,
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

        if (_options.WriteSummary)
        {
            File.WriteAllText(
                Path.Combine(directory, "summary.md"),
                BuildSummary(groups, missing, resources, dropped, _exceptions?.Signatures() ?? []));
        }
    }

    private static string BuildSummary(
        IReadOnlyList<JsErrorGroup> groups,
        IReadOnlyList<MissingApi> missing,
        IReadOnlyList<ResourceRecord> resources,
        int dropped,
        IReadOnlyList<ExceptionSignature> exceptions)
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
        summary.Append("| Exceptions (first-chance included) | ").Append(exceptions.Sum(static e => e.Count)).AppendLine(" |");
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

        if (exceptions.Count > 0)
        {
            summary.AppendLine("## Exceptions, most frequent first").AppendLine();
            summary.AppendLine("First-chance ones included — most were caught by the code that raised them, and are");
            summary.AppendLine("listed because a caught exception is how a fallback hides. `exceptions.log` has each");
            summary.AppendLine("one with its stack.").AppendLine();
            summary.AppendLine("| Count | Kind | Type | Thrown in | Message |");
            summary.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var exception in exceptions.Take(25))
            {
                summary.Append("| ").Append(exception.Count)
                    .Append(" | ").Append(exception.Kind)
                    .Append(" | `").Append(exception.Type)
                    .Append("` | `").Append(Escape(exception.Site))
                    .Append("` | ").Append(Escape(AnalysisConsole.OneLine(exception.FirstMessage, 200))).AppendLine(" |");
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
            var exceptions = _exceptions is { } recorder
                ? string.Create(CultureInfo.InvariantCulture, $", {recorder.Total} exception(s)")
                : string.Empty;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Diagnostics: {_errorCount} JavaScript failure(s), {_resources.Count} resource(s){exceptions} → {where}");
        }
    }
}

/// <summary>One archived resource, as it appears in <c>resources/index.json</c>.</summary>
/// <param name="RecordedBy">
/// <see cref="Engine"/> for what the engine traced — a page, a script as fetched or as run, a
/// stylesheet, a fetch response, a sub-document — or <see cref="Network"/> for a body the network
/// recorder kept, which is how images and fonts get into the archive.
/// </param>
internal sealed record ResourceRecord(
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
    DateTime Timestamp,
    string RecordedBy)
{
    /// <summary>Recorded through the engine's resource trace.</summary>
    public const string Engine = "engine";

    /// <summary>Recorded by <c>--analyze</c>'s network recorder.</summary>
    public const string Network = "network";
}
