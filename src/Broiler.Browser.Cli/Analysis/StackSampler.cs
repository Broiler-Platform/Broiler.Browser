using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Broiler.Cli.Analysis;

// Disambiguate the unqualified `DateTime` type: the Broiler.JS engine exposes a top-level
// `Broiler.DateTime` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.DateTime by simple-name lookup.
using DateTime = System.DateTime;

/// <summary>A frame the samples caught the analysis in, and how often.</summary>
/// <param name="Frame">The innermost Broiler method on a sampled stack, without its parameter list.</param>
/// <param name="Samples">How many sampled stacks had it innermost.</param>
/// <param name="Phases">The phases it was sampled in.</param>
internal sealed record HotSpot(string Frame, int Samples, IReadOnlyList<string> Phases);

/// <summary>
/// <c>--sample-stacks</c>: while a phase runs longer than it should, takes the managed stacks of this
/// process with <c>dotnet-stack</c> every few seconds, keeps the stacks that are in Broiler code, and
/// ranks the methods they were in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an external tool.</b> A .NET process cannot read another of its threads' stacks — the stack
/// of the thread that is stuck laying out a page is exactly the one the analysis cannot see from inside.
/// <c>dotnet-stack</c> asks the runtime for all of them over the diagnostics port, which is the
/// supported way to do it, and costs the target a brief pause per sample.
/// </para>
/// <para>
/// <b>Off unless asked for, and optional when asked.</b> It starts a process against this one, which a
/// run should not do unannounced. When the tool is not installed the analysis says how to install it
/// and runs without samples.
/// </para>
/// </remarks>
internal sealed class StackSampler : IDisposable
{
    private readonly string _tool;
    private readonly string _outputPath;
    private readonly PhaseClock _phases;
    private readonly AnalysisConsole _console;
    private readonly TimeSpan _after;
    private readonly TimeSpan _every;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, (int Samples, HashSet<string> Phases)> _hotSpots = new(StringComparer.Ordinal);
    private int _samples;

    private StackSampler(string tool, string outputPath, PhaseClock phases, AnalysisConsole console, TimeSpan after, TimeSpan every)
    {
        _tool = tool;
        _outputPath = outputPath;
        _phases = phases;
        _console = console;
        _after = after;
        _every = every;
        File.WriteAllText(
            outputPath,
            $"# Managed stacks of the analysis, taken with dotnet-stack every {every.TotalSeconds:0} s while a phase ran longer than {after.TotalSeconds:0} s.{System.Environment.NewLine}" +
            $"# Only the threads that were in Broiler code are kept.{System.Environment.NewLine}");
        _thread = new Thread(Run) { IsBackground = true, Name = "Broiler.Cli stack sampler" };
        _thread.Start();
    }

    /// <summary>How many samples were taken.</summary>
    public int Samples
    {
        get
        {
            lock (_sync)
                return _samples;
        }
    }

    /// <summary>
    /// Starts sampling, or returns null — and says why on the console — when <c>dotnet-stack</c> cannot
    /// be found.
    /// </summary>
    public static StackSampler? Start(string outputPath, PhaseClock phases, AnalysisConsole console, TimeSpan after, TimeSpan every)
    {
        if (FindTool() is not { } tool)
        {
            console.Line("stacks    dotnet-stack was not found, so no stacks are sampled; install it with: dotnet tool install -g dotnet-stack");
            return null;
        }

        console.Line($"stacks    sampling with {tool} while a phase runs longer than {after.TotalSeconds:0} s");
        return new StackSampler(tool, outputPath, phases, console, after, every);
    }

    /// <summary>The innermost Broiler frames the samples caught, most frequent first.</summary>
    public IReadOnlyList<HotSpot> HotSpots()
    {
        lock (_sync)
        {
            return [.. _hotSpots
                .OrderByDescending(static h => h.Value.Samples)
                .Take(30)
                .Select(static h => new HotSpot(h.Key, h.Value.Samples, [.. h.Value.Phases]))];
        }
    }

    private void Run()
    {
        while (!_stop.Token.WaitHandle.WaitOne(_every))
        {
            if (_phases.CurrentElapsedMs < _after.TotalMilliseconds)
                continue;

            try
            {
                Sample(_phases.Current, _phases.CurrentElapsedMs);
            }
            catch (Exception ex)
            {
                _console.Line($"stacks    a sample failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private void Sample(string phase, double phaseMs)
    {
        var start = new ProcessStartInfo
        {
            FileName = _tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("report");
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        using var process = Process.Start(start);
        if (process is null)
            return;

        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            return;
        }

        // A thread that is waiting is not where the time goes: the main thread blocked on the analysis
        // task, an idle compiler worker, a pool thread with nothing to do. Only the threads that were
        // running — in Broiler code somewhere on their stack — are kept.
        var threads = ParseThreads(output.GetAwaiter().GetResult())
            .Where(static t => t.Frames.Count > 0 && !IsWaitFrame(t.Frames[0]) && t.Frames.Any(IsBroilerFrame))
            .ToArray();

        var text = new StringBuilder()
            .AppendLine()
            .Append("## ").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
            .Append(" — ").Append(phase).Append(", ")
            .Append((phaseMs / 1000).ToString("0.0", CultureInfo.InvariantCulture)).AppendLine(" s in");
        foreach (var (header, frames) in threads)
        {
            text.AppendLine(header);
            foreach (var frame in frames)
                text.Append("  ").AppendLine(frame);
        }

        lock (_sync)
        {
            _samples++;
            foreach (var (_, frames) in threads)
            {
                if (frames.FirstOrDefault(IsBroilerFrame) is not { } innermost)
                    continue;

                var key = WithoutParameters(innermost);
                var entry = _hotSpots.TryGetValue(key, out var existing) ? existing : (0, new HashSet<string>(StringComparer.Ordinal));
                entry.Item2.Add(phase);
                _hotSpots[key] = (entry.Item1 + 1, entry.Item2);
            }

            File.AppendAllText(_outputPath, text.ToString());
        }
    }

    /// <summary>Splits <c>dotnet-stack report</c> output into threads, innermost frame first.</summary>
    internal static IReadOnlyList<(string Header, IReadOnlyList<string> Frames)> ParseThreads(string report)
    {
        var threads = new List<(string, IReadOnlyList<string>)>();
        string? header = null;
        var frames = new List<string>();
        foreach (var raw in report.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Thread (", StringComparison.Ordinal))
            {
                if (header is not null)
                    threads.Add((header, frames));
                header = line;
                frames = [];
            }
            else if (header is not null && line.Trim() is { Length: > 0 } frame && frame != "[Native Frames]")
            {
                frames.Add(frame);
            }
        }

        if (header is not null)
            threads.Add((header, frames));

        return threads;
    }

    /// <summary>
    /// A frame in a Broiler component, as <c>dotnet-stack</c> prints it: <c>Module!Namespace.Type.Method(…)</c>.
    /// The command line's own frames do not count: it is what waits for the components, not what they
    /// spend their time in.
    /// </summary>
    internal static bool IsBroilerFrame(string frame) =>
        frame.Contains("!Broiler.", StringComparison.Ordinal) && !frame.Contains("!Broiler.Cli.", StringComparison.Ordinal);

    private static readonly string[] WaitFrames =
    [
        "System.Threading.Monitor.Wait", "WaitHandle.Wait", "WaitOneNoCheck", "WaitAnyMultiple",
        "ManualResetEventSlim.Wait", "SemaphoreSlim.Wait", "System.Threading.Thread.Sleep", "LowLevelLifoSemaphore",
        "GetQueuedCompletionStatus", "SpinThenBlockingWait", "Task.InternalWait", "IOCompletionPoller.Poll",
        "WaitForSingleObject", "WaitForMultipleObjects", "Monitor.ObjWait",
    ];

    /// <summary>Whether a thread's innermost frame is a wait rather than work.</summary>
    internal static bool IsWaitFrame(string frame) => WaitFrames.Any(wait => frame.Contains(wait, StringComparison.Ordinal));

    internal static string WithoutParameters(string frame)
    {
        var bang = frame.IndexOf('!');
        var start = bang >= 0 ? bang + 1 : 0;
        var paren = frame.IndexOf('(', start);
        return paren > start ? frame[start..paren] : frame[start..];
    }

    private static string? FindTool()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet-stack.exe" : "dotnet-stack";
        var candidates = new List<string>();
        foreach (var directory in (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(directory, name));

        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
            candidates.Add(Path.Combine(home, ".dotnet", "tools", name));

        return candidates.FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(40));
        _stop.Dispose();

        try
        {
            var hotSpots = HotSpots();
            if (hotSpots.Count == 0)
                return;

            var summary = new StringBuilder().AppendLine().AppendLine("## Innermost Broiler frames, by samples").AppendLine();
            foreach (var hotSpot in hotSpots)
                summary.Append(hotSpot.Samples.ToString(CultureInfo.InvariantCulture).PadLeft(5)).Append("  ").Append(hotSpot.Frame)
                    .Append("  [").Append(string.Join(", ", hotSpot.Phases)).AppendLine("]");
            File.AppendAllText(_outputPath, summary.ToString());
        }
        catch (IOException)
        {
        }
    }
}
