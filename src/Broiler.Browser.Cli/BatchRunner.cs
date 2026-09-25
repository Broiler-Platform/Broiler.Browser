using System.Diagnostics;
using System.Text;

namespace Broiler.Cli;

/// <summary>One input of a batch and the file its result is written to.</summary>
internal sealed record BatchItem(string Input, string OutputPath);

/// <summary>
/// What one batch item produced: its exit code and everything it wrote, held rather than
/// printed so the console transcript can be replayed in input order (see
/// <see cref="BatchRunner"/>).
/// </summary>
internal sealed record BatchItemOutcome(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// A whole batch: the process exit code it implies, and each item's outcome in input order so a
/// caller that knows more than "non-zero means failed" — the fuzz sharder, whose shards exit 1
/// to report violations found — can summarize it itself.
/// </summary>
internal sealed record BatchRunResult(int ExitCode, IReadOnlyList<BatchItemOutcome> Outcomes);

/// <summary>
/// Runs a batch of CLI inputs concurrently. Multithreading roadmap item #20.
/// </summary>
/// <remarks>
/// <para>
/// There are two execution modes, and which one an operation may use is decided by whether
/// it touches the render path, not by convenience:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="RunInProcess"/> — threads in this process. Correct only for work that reaches
/// no shared mutable engine state. Document conversion was that work, and it is Broiler.Documents'
/// own command line now (<c>broilerdoc convert</c>), so nothing here runs in process today. The
/// mode stays because it is the same scheduler the child-process mode runs on, and the one its
/// ordering and failure handling can be tested through without starting a process.
/// </description></item>
/// <item><description>
/// <see cref="RunInChildProcesses"/> — one child <c>Broiler.Cli</c> per item, bounded. This is
/// what rendering work has to use today: the render path still holds unsynchronised caches on
/// process-wide singletons (<c>FontsHandler</c>'s four dictionaries, <c>BImageRenderer._images</c>),
/// which roadmap items #9 and #8 exist to fix. Until they land, two renders in one process is a
/// data race, and a second process is the cheap correct answer — the same reasoning the WPT
/// runner's worker pool rests on.
/// </description></item>
/// </list>
/// <para>
/// Both modes buffer each item's output and replay the whole transcript in input order once the
/// batch finishes, so a batch produces the same bytes on stdout at any thread count. Progress is
/// reported as items complete, which is the one thing that legitimately varies.
/// </para>
/// </remarks>
internal static class BatchRunner
{
    /// <summary>
    /// Resolves the thread/process count for a batch: an explicit <c>--threads</c> wins,
    /// otherwise one per core, and never more than there are items to do.
    /// </summary>
    internal static int ResolveDegreeOfParallelism(int? requested, int itemCount, int processorCount)
    {
        if (itemCount <= 0)
            return 1;

        int degree = requested is { } explicitDegree
            ? Math.Max(1, explicitDegree)
            : Math.Max(1, processorCount);

        return Math.Min(degree, itemCount);
    }

    /// <summary>
    /// Derives <c>&lt;outputDir&gt;/&lt;input name&gt;.&lt;extension&gt;</c> for each input, or
    /// <c>&lt;outputDir&gt;/&lt;input name&gt;</c> — a directory name — for an empty extension.
    /// Two inputs from different directories can share a file name, so a collision appends
    /// <c>-2</c>, <c>-3</c>, … in input order — deterministic, and never silently overwriting
    /// one item's result with another's.
    /// </summary>
    internal static IReadOnlyList<BatchItem> DeriveOutputPaths(
        IReadOnlyList<string> inputs,
        string outputDir,
        string extension)
    {
        var normalizedExtension = extension.Length == 0 || extension.StartsWith('.') ? extension : "." + extension;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<BatchItem>(inputs.Count);

        foreach (var input in inputs)
        {
            var stem = DeriveStem(input);
            var candidate = stem + normalizedExtension;
            for (int suffix = 2; !used.Add(candidate); suffix++)
                candidate = $"{stem}-{suffix}{normalizedExtension}";

            items.Add(new BatchItem(input, Path.Combine(outputDir, candidate)));
        }

        return items;
    }

    /// <summary>
    /// The output file's base name for an input. A file path contributes its own name; a URL
    /// contributes its last non-empty path segment, falling back to the host, so
    /// <c>https://example.com/a/b.html</c> becomes <c>b</c> and <c>https://example.com/</c>
    /// becomes <c>example.com</c>.
    /// </summary>
    private static string DeriveStem(string input)
    {
        string candidate = input;

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                // A host is not a file name: "example.com" would lose its ".com" to
                // GetFileNameWithoutExtension and collide with every other example.* host.
                return SanitizeFileName(uri.Host);
            }

            candidate = segments[^1];
        }
        else if (uri is { IsFile: true })
        {
            candidate = uri.LocalPath;
        }

        var stem = Path.GetFileNameWithoutExtension(candidate);
        if (string.IsNullOrWhiteSpace(stem))
            stem = Path.GetFileName(candidate);
        if (string.IsNullOrWhiteSpace(stem))
            stem = "output";

        return SanitizeFileName(stem);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return builder.ToString();
    }

    /// <summary>
    /// Runs <paramref name="convert"/> over the batch on <paramref name="degreeOfParallelism"/>
    /// threads of this process. See the type remarks for when this is admissible.
    /// </summary>
    internal static BatchRunResult RunInProcess(
        IReadOnlyList<BatchItem> items,
        int degreeOfParallelism,
        Func<BatchItem, TextWriter, TextWriter, int> convert)
    {
        return Run(items, degreeOfParallelism, summarize: true, (item, _) =>
        {
            var standardOutput = new StringWriter();
            var standardError = new StringWriter();
            int exitCode;
            try
            {
                exitCode = convert(item, standardOutput, standardError);
            }
            catch (Exception ex)
            {
                standardError.WriteLine($"Unexpected error converting {item.Input}: {ex.Message}");
                exitCode = 1;
            }

            return new BatchItemOutcome(exitCode, standardOutput.ToString(), standardError.ToString());
        });
    }

    /// <summary>
    /// Runs each item as its own <c>Broiler.Cli</c> process, at most
    /// <paramref name="degreeOfParallelism"/> at a time. <paramref name="argumentsFor"/> builds
    /// the child's argument list, which is an ordinary single-input invocation — the child needs
    /// to know nothing about batching, so a batch item behaves exactly as it would alone.
    /// </summary>
    internal static BatchRunResult RunInChildProcesses(
        IReadOnlyList<BatchItem> items,
        int degreeOfParallelism,
        Func<BatchItem, int, IReadOnlyList<string>> argumentsFor,
        bool summarize = true,
        Func<string, string>? filterTranscript = null)
    {
        return Run(
            items,
            degreeOfParallelism,
            summarize,
            (item, index) => RunChild(argumentsFor(item, index)),
            filterTranscript);
    }

    private static BatchItemOutcome RunChild(IReadOnlyList<string> arguments)
    {
        var startInfo = CreateCurrentProgramStartInfo(arguments);
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start a Broiler.Cli worker process.");

            // Read both pipes before waiting: a child that fills one of them while the parent
            // waits on exit deadlocks, and a full-page capture writes enough to do it.
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return new BatchItemOutcome(
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            return new BatchItemOutcome(1, string.Empty, $"Worker process failed: {ex.Message}{Environment.NewLine}");
        }
    }

    private static BatchRunResult Run(
        IReadOnlyList<BatchItem> items,
        int degreeOfParallelism,
        bool summarize,
        Func<BatchItem, int, BatchItemOutcome> execute,
        Func<string, string>? filterTranscript = null)
    {
        if (items.Count == 0)
            return new BatchRunResult(0, []);

        var outcomes = new BatchItemOutcome[items.Count];
        int nextIndex = -1;
        int completed = 0;

        void RunSlot()
        {
            while (true)
            {
                int index = Interlocked.Increment(ref nextIndex);
                if (index >= items.Count)
                    return;

                outcomes[index] = execute(items[index], index);
                int done = Interlocked.Increment(ref completed);
                Console.WriteLine($"[{done}/{items.Count}] {items[index].Input} -> {items[index].OutputPath}");
            }
        }

        int slots = Math.Clamp(degreeOfParallelism, 1, items.Count);
        if (slots == 1)
        {
            RunSlot();
        }
        else
        {
            var tasks = new Task[slots];
            for (int slot = 0; slot < slots; slot++)
                tasks[slot] = Task.Run(RunSlot);
            Task.WaitAll(tasks);
        }

        return new BatchRunResult(Report(items, outcomes, summarize, filterTranscript), outcomes);
    }

    /// <summary>
    /// Replays every item's output in input order and returns 0 only if every item succeeded.
    /// With <paramref name="summarize"/> false the transcript is still replayed but the
    /// pass/fail tally is left to the caller, because a non-zero exit does not always mean the
    /// item failed.
    /// </summary>
    private static int Report(
        IReadOnlyList<BatchItem> items,
        IReadOnlyList<BatchItemOutcome> outcomes,
        bool summarize,
        Func<string, string>? filterTranscript)
    {
        int failed = 0;

        Console.WriteLine();
        for (int i = 0; i < items.Count; i++)
        {
            var outcome = outcomes[i];
            // The caller still sees the unfiltered text in the returned outcomes; the filter only
            // keeps machine-readable lines a child emits for its parent out of the transcript.
            var standardOutput = filterTranscript is null
                ? outcome.StandardOutput
                : filterTranscript(outcome.StandardOutput);
            if (!string.IsNullOrEmpty(standardOutput))
                Console.Out.Write(standardOutput);
            if (!string.IsNullOrEmpty(outcome.StandardError))
                Console.Error.Write(outcome.StandardError);
            if (outcome.ExitCode == 0)
                continue;

            failed++;
            if (summarize)
                Console.Error.WriteLine($"Failed ({outcome.ExitCode}): {items[i].Input}");
        }

        if (summarize)
        {
            Console.WriteLine();
            Console.WriteLine($"Batch complete: {items.Count} item(s), {items.Count - failed} succeeded, {failed} failed.");
        }

        return failed > 0 ? 1 : 0;
    }

    /// <summary>
    /// A start info that re-invokes this same program — the framework-dependent case runs
    /// through <c>dotnet &lt;assembly&gt;</c>, since <c>Environment.ProcessPath</c> is then the
    /// shared host rather than the app.
    /// </summary>
    internal static ProcessStartInfo CreateCurrentProgramStartInfo(IEnumerable<string> arguments)
    {
        var processPath = Environment.ProcessPath;
        var assemblyPath = typeof(BatchRunner).Assembly.Location;
        var startInfo = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(processPath) && !IsDotNetHost(processPath))
        {
            startInfo.FileName = processPath;
        }
        else
        {
            startInfo.FileName = string.IsNullOrWhiteSpace(processPath) ? "dotnet" : processPath;
            startInfo.ArgumentList.Add(assemblyPath);
        }

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static bool IsDotNetHost(string processPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(processPath);
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase);
    }
}
