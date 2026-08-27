using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broiler.Code.Phase0.Prototype.Classification;
using Broiler.Code.Phase0.Prototype.Fixtures;
using Broiler.Code.Phase0.Prototype.Text;
using Broiler.Code.Phase0.Prototype.Traces;

namespace Broiler.Code.Phase0.Prototype;

/// <summary>
/// The Phase 0 edit-to-classification prototype. It proves the three things the
/// exit gate names — immutable snapshots, incremental classification, and
/// rejection of stale work — and records the numbers the budgets are frozen
/// against. Correctness runs first and a failure fails the process, because a
/// timing figure from a wrong classifier is worse than no figure.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions TraceOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static int Main(string[] args)
    {
        int iterations = ParseIterations(args);

        List<CheckResult> checks = CorrectnessChecks.RunAll();
        var failed = checks.Where(check => !check.Passed).ToList();
        if (failed.Count > 0)
        {
            foreach (CheckResult check in failed)
                Console.Error.WriteLine($"FAILED {check.Name}: {check.Detail}");
            return 1;
        }

        FixtureReport largeFile = MeasureLargeFile("csharp-100k-lines", 0, iterations);
        FixtureReport wideFile = MeasureLargeFile("csharp-100k-lines-wide", 104, iterations);
        SolutionReport solution = MeasureSolution(fileCount: 1_000, linesPerFile: 100);
        List<TraceResult> traces = RunTraces();

        var report = new PrototypeReport(
            SchemaVersion: 1,
            Iterations: iterations,
            Framework: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            Checks: [.. checks.Select(check => new CheckSummary(check.Name, check.Passed, check.Detail))],
            LargeFile: largeFile,
            WideFile: wideFile,
            Solution: solution,
            Traces: traces);

        Console.WriteLine(JsonSerializer.Serialize(report, ReportOptions));

        // Every trace must end agreeing with a full classification, and no trace
        // may ever have painted a snapshot the buffer had already replaced.
        bool tracesHealthy = traces.Count > 0 &&
            traces.All(trace => trace.IncrementalMatchedFull && trace.OnlyCurrentSnapshotPainted);
        return tracesHealthy ? 0 : 1;
    }

    private static FixtureReport MeasureLargeFile(string id, int minimumLineWidth, int iterations)
    {
        const int lines = 100_000;
        string source = SourceFixtures.BuildCSharpFile(lines, seed: 0, minimumLineWidth);
        var classifier = new PortableCSharpClassifier();

        // Warm up first. Without this the fixture measured first also pays for
        // JIT and first-touch costs, and reads as slower than a larger fixture
        // measured afterwards.
        TextSnapshot warmup = TextSnapshot.Create(source);
        GC.KeepAlive(classifier.ClassifyAll(warmup, CancellationToken.None));

        var build = new Sampler();
        for (int i = 0; i < iterations; i++)
        {
            Collect();
            build.Begin();
            GC.KeepAlive(TextSnapshot.Create(source));
            build.End();
        }

        TextSnapshot snapshot = TextSnapshot.Create(source);
        var full = new Sampler();
        ClassificationResult? result = null;
        for (int i = 0; i < iterations; i++)
        {
            Collect();
            full.Begin();
            result = classifier.ClassifyAll(snapshot, CancellationToken.None);
            full.End();
        }

        long retained = MeasureRetainedBytes(() =>
            classifier.ClassifyAll(snapshot, CancellationToken.None));

        return new FixtureReport(
            id,
            lines,
            Encoding.UTF8.GetByteCount(source),
            snapshot.LineCount,
            result!.TotalSpans,
            build.Summarize(),
            full.Summarize(),
            retained);
    }

    private static SolutionReport MeasureSolution(int fileCount, int linesPerFile)
    {
        Collect();
        var buildSampler = new Sampler();
        buildSampler.Begin();
        string[] files = SourceFixtures.BuildSolution(fileCount, linesPerFile);
        buildSampler.End();

        var classifier = new PortableCSharpClassifier();
        var snapshots = new TextSnapshot[fileCount];
        for (int i = 0; i < fileCount; i++)
            snapshots[i] = TextSnapshot.Create(files[i]);

        Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        var classifySampler = new Sampler();
        classifySampler.Begin();
        var results = new ClassificationResult[fileCount];
        for (int i = 0; i < fileCount; i++)
            results[i] = classifier.ClassifyAll(snapshots[i], CancellationToken.None);
        classifySampler.End();
        long after = GC.GetTotalMemory(forceFullCollection: true);

        int totalLines = results.Sum(r => r.Lines.Length);
        int totalSpans = results.Sum(r => r.TotalSpans);
        long sourceBytes = files.Sum(f => (long)Encoding.UTF8.GetByteCount(f));
        GC.KeepAlive(results);

        return new SolutionReport(
            fileCount,
            linesPerFile,
            totalLines,
            totalSpans,
            sourceBytes,
            buildSampler.Summarize(),
            classifySampler.Summarize(),
            after - before);
    }

    private static List<TraceResult> RunTraces()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "traces");
        var results = new List<TraceResult>();
        if (!Directory.Exists(directory))
            return results;

        foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            EditTrace trace = JsonSerializer.Deserialize<EditTrace>(File.ReadAllText(path), TraceOptions)
                ?? throw new InvalidOperationException($"Trace '{path}' is empty.");
            results.Add(TraceRunner.Run(trace, TraceRunner.ResolveFixture(trace)));
        }

        return results;
    }

    /// <summary>
    /// Steady-state cost of holding one classification result, which is the
    /// figure the memory budget is stated against — peak allocation during the
    /// run says nothing about what stays resident while the file is open.
    /// </summary>
    private static long MeasureRetainedBytes(Func<ClassificationResult> produce)
    {
        Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        ClassificationResult held = produce();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(held);
        return after - before;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static int ParseIterations(string[] args)
    {
        if (args.Length == 0)
            return 3;
        if (args.Length == 2 && args[0] == "--iterations" &&
            int.TryParse(args[1], out int iterations) && iterations > 0)
        {
            return iterations;
        }

        throw new ArgumentException("Usage: --iterations <positive integer>");
    }

    private sealed record CheckSummary(string Name, bool Passed, string Detail);

    private sealed record FixtureReport(
        string Id,
        int RequestedLines,
        int SourceBytes,
        int IndexedLines,
        int TotalSpans,
        Measurement SnapshotCreation,
        Measurement FullClassification,
        long RetainedBytes);

    private sealed record SolutionReport(
        int FileCount,
        int LinesPerFile,
        int TotalLines,
        int TotalSpans,
        long SourceBytes,
        Measurement Generation,
        Measurement ClassifyAllFiles,
        long RetainedBytes);

    private sealed record PrototypeReport(
        int SchemaVersion,
        int Iterations,
        string Framework,
        string OperatingSystem,
        string Architecture,
        int ProcessorCount,
        IReadOnlyList<CheckSummary> Checks,
        FixtureReport LargeFile,
        FixtureReport WideFile,
        SolutionReport Solution,
        IReadOnlyList<TraceResult> Traces);
}
