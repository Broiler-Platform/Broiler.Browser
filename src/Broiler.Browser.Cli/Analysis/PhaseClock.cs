using System.Diagnostics;
using System.Globalization;

namespace Broiler.Cli.Analysis;

/// <summary>How a phase of an analysis ended.</summary>
internal enum PhaseOutcome
{
    /// <summary>The phase ran to the end.</summary>
    Succeeded,

    /// <summary>The phase threw; <see cref="PhaseRecord.Error"/> says what.</summary>
    Failed,

    /// <summary>The phase did not run, because something it needs was not produced.</summary>
    Skipped,
}

/// <summary>One phase of an analysis: when it started, how long it ran and how it ended.</summary>
/// <param name="Name">The phase's name, as the exception log files exceptions under it.</param>
/// <param name="StartMs">Milliseconds from the start of the analysis to the start of the phase.</param>
/// <param name="DurationMs">How long the phase ran.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Detail">A one-line account of what it produced, when it produced something.</param>
/// <param name="Error">The exception that ended it, as type and message, when it failed.</param>
internal sealed record PhaseRecord(
    string Name,
    double StartMs,
    double DurationMs,
    PhaseOutcome Outcome,
    string? Detail,
    string? Error);

/// <summary>
/// The phases of one page analysis. It knows which phase is running — which is what the exception log
/// files each exception under — and keeps a record of how each one went.
/// </summary>
/// <remarks>
/// <para>
/// <b>A phase that throws does not end the analysis.</b> An analysis runs precisely on the pages
/// that break things, and the evidence from the phases that did work — the files, the logs, the
/// screenshot of the document without its scripts — is most of what a reader needs to understand
/// the one that did not. So <see cref="Run{T}"/> records the failure and returns
/// <see langword="default"/>, and the caller decides what can still run without that result.
/// </para>
/// <para>
/// <b>The current phase is process-wide, not per-thread.</b> An exception on a prefetch worker
/// while the page is loading belongs to the loading, whichever thread raised it; a per-thread or
/// async-local notion of "current" would file it under nothing.
/// </para>
/// </remarks>
internal sealed class PhaseClock
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Lock _sync = new();
    private readonly List<PhaseRecord> _phases = [];
    private readonly AnalysisConsole _console;
    private volatile string _current = "startup";
    private double _currentStartedMs;

    public PhaseClock(AnalysisConsole console) => _console = console;

    /// <summary>The phase that is running now.</summary>
    public string Current => _current;

    /// <summary>How long the current phase has been running, in milliseconds.</summary>
    public double CurrentElapsedMs => ElapsedMs - Volatile.Read(ref _currentStartedMs);

    /// <summary>Milliseconds since the analysis started.</summary>
    public double ElapsedMs => _clock.Elapsed.TotalMilliseconds;

    /// <summary>
    /// Runs <paramref name="body"/> as the phase <paramref name="name"/>, and returns what it produced,
    /// or <see langword="default"/> when it threw.
    /// </summary>
    /// <param name="name">The phase's name.</param>
    /// <param name="body">The work.</param>
    /// <param name="describe">A one-line account of the result for the console and the report.</param>
    public T? Run<T>(string name, Func<T> body, Func<T, string?>? describe = null)
    {
        var started = Enter(name);
        try
        {
            var result = body();
            Leave(name, started, PhaseOutcome.Succeeded, Describe(result, describe), error: null);
            return result;
        }
        catch (Exception ex)
        {
            Leave(name, started, PhaseOutcome.Failed, detail: null, Describe(ex));
            return default;
        }
    }

    /// <summary>As <see cref="Run{T}"/>, for work that does not produce a value.</summary>
    public bool Run(string name, Action body, Func<string?>? describe = null) =>
        Run(name, () =>
        {
            body();
            return true;
        }, _ => describe?.Invoke());

    /// <summary>As <see cref="Run{T}"/>, for asynchronous work.</summary>
    public async Task<T?> RunAsync<T>(string name, Func<Task<T>> body, Func<T, string?>? describe = null)
    {
        var started = Enter(name);
        try
        {
            var result = await body().ConfigureAwait(false);
            Leave(name, started, PhaseOutcome.Succeeded, Describe(result, describe), error: null);
            return result;
        }
        catch (Exception ex)
        {
            Leave(name, started, PhaseOutcome.Failed, detail: null, Describe(ex));
            return default;
        }
    }

    /// <summary>Records that <paramref name="name"/> did not run, and why.</summary>
    public void Skip(string name, string reason)
    {
        var now = ElapsedMs;
        lock (_sync)
            _phases.Add(new PhaseRecord(name, now, 0, PhaseOutcome.Skipped, reason, Error: null));

        _console.Line($"skipped   {name}: {reason}");
    }

    /// <summary>The phases so far, in the order they started.</summary>
    public IReadOnlyList<PhaseRecord> Snapshot()
    {
        lock (_sync)
            return [.. _phases];
    }

    private double Enter(string name)
    {
        var now = ElapsedMs;
        Volatile.Write(ref _currentStartedMs, now);
        _current = name;
        _console.Line($"start     {name}");
        return now;
    }

    private void Leave(string name, double started, PhaseOutcome outcome, string? detail, string? error)
    {
        var duration = ElapsedMs - started;
        lock (_sync)
            _phases.Add(new PhaseRecord(name, Math.Round(started, 1), Math.Round(duration, 1), outcome, detail, error));

        var ms = duration.ToString("0", CultureInfo.InvariantCulture);
        _console.Line(outcome == PhaseOutcome.Succeeded
            ? $"done      {name} ({ms} ms){(detail is null ? string.Empty : " — " + AnalysisConsole.OneLine(detail))}"
            : $"FAILED    {name} ({ms} ms): {AnalysisConsole.OneLine(error ?? string.Empty)}");

        // Between phases nothing is running on the analysis's behalf, and an exception raised on a
        // worker now belongs to no phase rather than to the one that just ended.
        _current = "between phases";
    }

    private static string? Describe<T>(T result, Func<T, string?>? describe)
    {
        if (describe is null)
            return null;

        try
        {
            return describe(result);
        }
        catch (Exception ex)
        {
            return $"(could not describe the result: {ex.GetType().Name})";
        }
    }

    private static string Describe(Exception ex) => $"{ex.GetType().FullName}: {ExceptionText.SafeMessage(ex)}";
}
