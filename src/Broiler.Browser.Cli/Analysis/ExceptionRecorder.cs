using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;

namespace Broiler.Cli.Analysis;

// Disambiguate the unqualified `DateTime` type: the Broiler.JS engine exposes a top-level
// `Broiler.DateTime` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.DateTime by simple-name lookup.
using DateTime = System.DateTime;

/// <summary>One exception as the log records it, handed to a listener as it happens.</summary>
/// <param name="Sequence">The exception's number in this run, from 1.</param>
/// <param name="Kind"><c>first-chance</c>, <c>unhandled</c> or <c>unobserved-task</c>.</param>
/// <param name="Phase">The analysis phase that was running.</param>
/// <param name="Type">The exception's type.</param>
/// <param name="Message">Its message, read without running page code (<see cref="ExceptionText"/>).</param>
/// <param name="Site">The method that threw.</param>
/// <param name="Component">The component the throwing method belongs to, e.g. <c>Broiler.CSS</c>.</param>
internal sealed record ExceptionEvent(
    long Sequence,
    string Kind,
    string Phase,
    string Type,
    string Message,
    string Site,
    string Component);

/// <summary>The last exception recorded on a thread, for tying a later report on that thread to it.</summary>
/// <param name="Sequence">Its number in the exception log.</param>
/// <param name="Type">Its type.</param>
/// <param name="Message">Its message.</param>
/// <param name="Timestamp">When it was recorded, as a <see cref="Stopwatch"/> timestamp.</param>
internal sealed record RecentException(long Sequence, string Type, string Message, long Timestamp)
{
    /// <summary>How long ago it was recorded.</summary>
    public TimeSpan Age => Stopwatch.GetElapsedTime(Timestamp);
}

/// <summary>
/// Every exception of one kind, type and throw site: how often it happened, when, in which phases,
/// and the stack of the first occurrences.
/// </summary>
internal sealed record ExceptionSignature(
    string Kind,
    string Type,
    string Site,
    string Component,
    int Count,
    DateTime FirstSeen,
    DateTime LastSeen,
    IReadOnlyList<string> Phases,
    string FirstMessage,
    string? FirstStack);

/// <summary>
/// Records every exception raised anywhere in the process while an analysis runs — including the
/// first-chance exceptions that something caught and recovered from — to <c>exceptions.log</c> as it
/// happens, and summarises them by signature into <c>exceptions.json</c> at the end.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why first-chance exceptions.</b> The failures that make a page render wrong are rarely the ones
/// that surface. A stylesheet value that does not parse, an image that does not decode, a layout pass
/// that throws and falls back, a font lookup that fails over to the next family, a script's
/// <c>try</c>/<c>catch</c> around a feature test that took the unsupported branch: each is an
/// exception somebody caught, and the only trace it leaves is the wrong output. The first-chance
/// notification is raised for every one of them, on the throwing thread, before any handler runs —
/// it is the one place all of them can be seen.
/// </para>
/// <para>
/// <b>Streamed, flushed per record.</b> The runs that most need this log are the ones that crash or
/// hang, so nothing is held back for the end: each record is on disk before the throw continues.
/// Unhandled exceptions are recorded the same way, so a run that dies says why in its own log.
/// </para>
/// <para>
/// <b>Bounded where it has to be.</b> A page can throw from a loop — a feature test in a retry timer,
/// a parser falling back on every declaration — and a log that grew without limit, or took a stack
/// with file information for every throw, would make the analysis the slowest thing on the page. So
/// the first <see cref="StacksPerSignature"/> occurrences of each signature carry the full stack at
/// the throw, and after <see cref="MaxWrittenRecords"/> records the log stops writing lines. Nothing
/// stops the counting: the summary accounts for every exception.
/// </para>
/// <para>
/// <b>It must never throw into the code it observes.</b> The handler runs inside somebody else's
/// throw. It catches everything, and a per-thread guard drops the notifications raised by its own
/// work — reading a PDB for a stack's line numbers throws and catches internally — so it can never
/// recurse into itself.
/// </para>
/// <para>
/// <b>Nor overflow the stack it runs on.</b> A script engine throws "Maximum call stack size
/// exceeded" exactly when the stack is nearly gone, and the handler runs on top of that throw; taking
/// a stack with file information there would turn a recoverable script error into a process that
/// dies without a report. With less stack left than
/// <see cref="RuntimeHelpers.TryEnsureSufficientExecutionStack"/> asks for, a notification is only
/// counted.
/// </para>
/// <para>
/// <b>An exception counts once.</b> .NET raises a first-chance notification for every throw of an
/// exception object — each <c>throw;</c> that rethrows it and each <c>await</c> that hands it on —
/// so a single failure passing through a few frames arrived as several. The first notification for
/// an object is recorded; later ones are counted as rethrows.
/// </para>
/// </remarks>
internal sealed class ExceptionRecorder : IDisposable
{
    /// <summary>How many occurrences of one signature keep their full stack.</summary>
    internal const int StacksPerSignature = 5;

    /// <summary>How many stacks the whole run keeps, across signatures.</summary>
    internal const int MaxStacks = 2_000;

    /// <summary>How many records the log writes before it only counts.</summary>
    internal const int MaxWrittenRecords = 100_000;

    [ThreadStatic]
    private static bool t_inHandler;

    [ThreadStatic]
    private static RecentException? t_last;

    private static readonly object Seen = new();

    private readonly Lock _sync = new();
    private readonly Func<string> _phase;
    private readonly string _jsonPath;
    private readonly Dictionary<string, MutableSignature> _signatures = new(StringComparer.Ordinal);
    private readonly List<MutableSignature> _order = [];

    // The exception objects already recorded, so a rethrow of one is not recorded again. Weak: the
    // exceptions a page throws and drops must not live as long as the run.
    private readonly ConditionalWeakTable<Exception, object> _recorded = [];
    private long _rethrows;
    private long _withoutStackRoom;
    private StreamWriter? _log;
    private long _sequence;
    private int _written;
    private int _stacks;
    private bool _capNoticeWritten;
    private bool _disposed;
    private int _disposeStarted;

    private ExceptionRecorder(string logPath, string jsonPath, Func<string> phase)
    {
        _phase = phase;
        _jsonPath = jsonPath;

        var directory = Path.GetDirectoryName(Path.GetFullPath(logPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _log = new StreamWriter(
            new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false));
        _log.WriteLine(
            $"# Broiler exception log — every exception raised in the process, first-chance ones " +
            $"included, as it happened. Started {DateTime.UtcNow:o}.");
        _log.WriteLine(
            $"# The first {StacksPerSignature} occurrences of each type and throw site carry the stack at " +
            "the throw; exceptions.json counts every occurrence.");
        _log.Flush();
    }

    /// <summary>Raised after each exception is recorded, outside the recorder's lock.</summary>
    public event Action<ExceptionEvent>? Recorded;

    /// <summary>
    /// The last exception any recorder saw on the calling thread, and when. A component that catches
    /// an exception and reports only that something failed — the renderer's <c>RenderError</c> carries
    /// no message — has usually just caught it on this thread, so this is the lead to its cause.
    /// </summary>
    public static RecentException? LastOnCurrentThread => t_last;

    /// <summary>How many exceptions have been recorded so far.</summary>
    public long Total
    {
        get
        {
            lock (_sync)
                return _sequence;
        }
    }

    /// <summary>How many first-chance notifications were a recorded exception thrown again.</summary>
    public long Rethrows => Interlocked.Read(ref _rethrows);

    /// <summary>How many exceptions arrived with too little stack left to record them.</summary>
    public long WithoutStackRoom => Interlocked.Read(ref _withoutStackRoom);

    /// <summary>
    /// Starts recording into <paramref name="logPath"/>, with the summary to follow in
    /// <paramref name="jsonPath"/>. <paramref name="phase"/> names what the analysis is doing, and is
    /// read on whatever thread threw.
    /// </summary>
    public static ExceptionRecorder Start(string logPath, string jsonPath, Func<string> phase)
    {
        var recorder = new ExceptionRecorder(logPath, jsonPath, phase);
        AppDomain.CurrentDomain.FirstChanceException += recorder.OnFirstChance;
        AppDomain.CurrentDomain.UnhandledException += recorder.OnUnhandled;
        TaskScheduler.UnobservedTaskException += recorder.OnUnobservedTask;
        return recorder;
    }

    /// <summary>The signatures so far, most frequent first, ties in first-seen order.</summary>
    public IReadOnlyList<ExceptionSignature> Signatures()
    {
        lock (_sync)
        {
            return [.. _order
                .OrderByDescending(static s => s.Count)
                .Select(static s => s.Freeze())];
        }
    }

    private void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Before anything that needs the stack — the rethrow check included.
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Interlocked.Increment(ref _withoutStackRoom);
            return;
        }

        if (t_inHandler)
            return;

        if (!_recorded.TryAdd(e.Exception, Seen))
        {
            Interlocked.Increment(ref _rethrows);
            return;
        }

        Record(e.Exception, "first-chance");
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            Record(exception, e.IsTerminating ? "unhandled (terminating)" : "unhandled");
    }

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e) =>
        Record(e.Exception, "unobserved-task");

    private void Record(Exception exception, string kind)
    {
        if (t_inHandler)
            return;

        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            Interlocked.Increment(ref _withoutStackRoom);
            return;
        }

        t_inHandler = true;
        try
        {
            RecordCore(exception, kind);
        }
        catch (Exception)
        {
            // See the type remarks: nothing escapes into the throw being observed.
        }
        finally
        {
            t_inHandler = false;
        }
    }

    private void RecordCore(Exception exception, string kind)
    {
        var type = exception.GetType().FullName ?? exception.GetType().Name;
        var site = ThrowSite(exception);
        var component = ComponentOf(exception);
        var phase = SafePhase();
        var message = ExceptionText.SafeMessage(exception);
        var now = DateTime.UtcNow;
        var key = string.Concat(kind, "|", type, "|", site);

        long sequence;
        bool wantsStack;
        bool writes;
        lock (_sync)
        {
            if (_disposed)
                return;

            sequence = ++_sequence;
            if (!_signatures.TryGetValue(key, out var signature))
            {
                signature = new MutableSignature(kind, type, site, component, now, phase, message);
                _signatures.Add(key, signature);
                _order.Add(signature);
            }

            signature.Count++;
            signature.LastSeen = now;
            signature.Phases.Add(phase);

            wantsStack = signature.Count <= StacksPerSignature && _stacks < MaxStacks;
            if (wantsStack)
                _stacks++;

            writes = _written < MaxWrittenRecords;
            if (writes)
                _written++;
        }

        t_last = new RecentException(sequence, type, message, Stopwatch.GetTimestamp());

        // Outside the lock: a stack with line numbers reads PDBs, and holding the lock across that
        // would stall every other thread that throws meanwhile.
        var stack = wantsStack ? CaptureStack() : null;
        if (stack is not null)
        {
            lock (_sync)
            {
                if (_signatures.TryGetValue(key, out var signature))
                    signature.FirstStack ??= stack;
            }
        }

        if (writes)
            Write(sequence, kind, phase, type, message, site, exception, stack, now);
        else
            WriteCapNotice();

        Recorded?.Invoke(new ExceptionEvent(sequence, kind, phase, type, message, site, component));
    }

    private void Write(
        long sequence,
        string kind,
        string phase,
        string type,
        string message,
        string site,
        Exception exception,
        string? stack,
        DateTime now)
    {
        var thread = Thread.CurrentThread;
        var threadLabel = thread.IsThreadPoolThread
            ? $"pool thread {thread.ManagedThreadId}"
            : thread.Name is { Length: > 0 } name
                ? $"thread {thread.ManagedThreadId} ({name})"
                : $"thread {thread.ManagedThreadId}";

        var record = new StringBuilder();
        record.Append(now.ToString("o", CultureInfo.InvariantCulture))
            .Append(" #").Append(sequence.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(kind)
            .Append(" [").Append(phase).Append("] ")
            .Append(threadLabel).Append(": ")
            .Append(type).Append(": ").AppendLine(message.ReplaceLineEndings(" "));
        record.Append("    thrown in ").AppendLine(site);

        // Inner exceptions are part of what happened; the outer message often only says "failed".
        if (exception.InnerException is not null || exception is AggregateException)
        {
            foreach (var line in ExceptionText.Describe(exception).Split('\n').Skip(1))
                record.Append("    inner: ").AppendLine(line.TrimEnd('\r').Trim());
        }

        if (stack is not null)
        {
            foreach (var line in stack.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length > 0)
                    record.Append("      ").AppendLine(trimmed.Trim());
            }
        }

        lock (_sync)
        {
            if (_log is not { } log)
                return;

            log.Write(record.ToString());
            log.Flush();
        }
    }

    private void WriteCapNotice()
    {
        lock (_sync)
        {
            if (_capNoticeWritten || _log is not { } log)
                return;

            _capNoticeWritten = true;
            log.WriteLine(
                $"# {MaxWrittenRecords} records written; later exceptions are counted in exceptions.json " +
                "but not written here.");
            log.Flush();
        }
    }

    private string SafePhase()
    {
        try
        {
            return _phase();
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    /// <summary>
    /// The stack as it stands at the throw, without this recorder's own frames. At a first-chance
    /// notification the throwing method and all its callers are still on the stack, which is more
    /// than the exception's own trace will ever say about a throw that is caught one frame up.
    /// </summary>
    private static string CaptureStack()
    {
        var frames = new StackTrace(1, fNeedFileInfo: true).GetFrames();
        var builder = new StringBuilder();
        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method?.DeclaringType is { } declaring && IsRecorderFrame(declaring))
                continue;

            builder.Append(new StackTrace(frame).ToString());
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsRecorderFrame(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current == typeof(ExceptionRecorder))
                return true;
        }

        return false;
    }

    private static string ThrowSite(Exception exception)
    {
        MethodBase? method;
        try
        {
            method = exception.TargetSite;
        }
        catch (Exception)
        {
            method = null;
        }

        if (method is null)
            return "(unknown site)";

        return method.DeclaringType is { } declaring
            ? $"{declaring.FullName}.{method.Name}"
            : method.Name;
    }

    /// <summary>
    /// The component an exception came from: the root of the throwing method's namespace — the
    /// part that says <c>Broiler.CSS</c> or <c>System.Net</c> — which is where a reader would go to
    /// fix it, rather than the exception type's, which is usually <c>System</c>.
    /// </summary>
    internal static string ComponentOf(Exception exception)
    {
        string? ns;
        try
        {
            ns = exception.TargetSite?.DeclaringType?.Namespace;
        }
        catch (Exception)
        {
            ns = null;
        }

        ns ??= exception.GetType().Namespace;
        if (string.IsNullOrEmpty(ns))
            return "(unknown)";

        var parts = ns.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts[0];
    }

    /// <summary>
    /// Stops recording, forces a collection so a task that faulted unobserved reports now rather than
    /// never, and writes the summary. Safe to call twice.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
            return;

        // A faulted task nobody awaited reports only when its finalizer runs. Collecting here, while
        // still subscribed, is what turns "the run ended" into "and here is the task that failed".
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        catch (Exception)
        {
        }

        AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTask;

        IReadOnlyList<ExceptionSignature> signatures;
        long total;
        lock (_sync)
        {
            _disposed = true;
            total = _sequence;
            signatures = [.. _order.OrderByDescending(static s => s.Count).Select(static s => s.Freeze())];
        }

        try
        {
            WriteSummary(signatures, total);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        lock (_sync)
        {
            _log?.Dispose();
            _log = null;
        }
    }

    private void WriteSummary(IReadOnlyList<ExceptionSignature> signatures, long total)
    {
        lock (_sync)
        {
            if (_log is { } log)
            {
                log.WriteLine();
                log.WriteLine($"# {total} exception(s) in {signatures.Count} signature(s), most frequent first:");
                if (Rethrows > 0)
                    log.WriteLine($"# (and {Rethrows} rethrow(s) of exceptions already recorded, not counted again)");
                if (WithoutStackRoom > 0)
                    log.WriteLine($"# ({WithoutStackRoom} exception(s) arrived with too little stack left to record them: a stack overflow was near)");
                foreach (var signature in signatures.Take(50))
                {
                    log.WriteLine(
                        $"#   ×{signature.Count,-6} {signature.Kind,-14} {signature.Type} in {signature.Site} " +
                        $"[{string.Join(", ", signature.Phases)}]");
                }

                log.Flush();
            }
        }

        var json = JsonSerializer.Serialize(
            new
            {
                generatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                total,
                rethrows = Rethrows,
                withoutStackRoom = WithoutStackRoom,
                stacksPerSignature = StacksPerSignature,
                byKind = signatures
                    .GroupBy(static s => s.Kind)
                    .ToDictionary(static g => g.Key, static g => g.Sum(static s => s.Count)),
                byComponent = signatures
                    .GroupBy(static s => s.Component)
                    .OrderByDescending(static g => g.Sum(static s => s.Count))
                    .ToDictionary(static g => g.Key, static g => g.Sum(static s => s.Count)),
                signatures = signatures.Select(static s => new
                {
                    kind = s.Kind,
                    type = s.Type,
                    site = s.Site,
                    component = s.Component,
                    count = s.Count,
                    firstSeen = s.FirstSeen.ToString("o", CultureInfo.InvariantCulture),
                    lastSeen = s.LastSeen.ToString("o", CultureInfo.InvariantCulture),
                    phases = s.Phases,
                    message = s.FirstMessage,
                    stack = s.FirstStack,
                }),
            },
            AnalysisJson.Options);
        File.WriteAllText(_jsonPath, json);
    }

    private sealed class MutableSignature
    {
        private readonly string _kind;
        private readonly string _type;
        private readonly string _site;
        private readonly string _component;
        private readonly DateTime _firstSeen;
        private readonly string _firstMessage;

        public MutableSignature(
            string kind,
            string type,
            string site,
            string component,
            DateTime firstSeen,
            string firstPhase,
            string firstMessage)
        {
            _kind = kind;
            _type = type;
            _site = site;
            _component = component;
            _firstSeen = firstSeen;
            _firstMessage = firstMessage;
            LastSeen = firstSeen;
            Phases = new OrderedPhases(firstPhase);
        }

        public int Count;
        public DateTime LastSeen;
        public string? FirstStack;

        // Insertion-ordered, so the phases read in the order the signature appeared in them.
        public readonly OrderedPhases Phases;

        public ExceptionSignature Freeze() =>
            new(_kind, _type, _site, _component, Count, _firstSeen, LastSeen, Phases.ToList(), _firstMessage, FirstStack);
    }

    /// <summary>A small insertion-ordered set: a signature appears in a handful of phases at most.</summary>
    private sealed class OrderedPhases
    {
        private readonly List<string> _items = [];

        public OrderedPhases(string first) => _items.Add(first);

        public void Add(string phase)
        {
            if (!_items.Contains(phase, StringComparer.Ordinal))
                _items.Add(phase);
        }

        public List<string> ToList() => [.. _items];
    }
}
