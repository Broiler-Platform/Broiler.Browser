using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Broiler.JavaScript.Engine;
using Broiler.JavaScript.Runtime;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Does JavaScript in two <see cref="JSContext"/> instances actually run at the same time on two
/// threads, or does the engine serialize them? The measurement multithreading item #18 (Web
/// Workers) turns on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is the first thing to measure about item #18.</b> The item is scoped at 30–40 days
/// and High risk, and its master-table row says isolation "is feasible". <c>JSContextIsolationTests</c>
/// answers the correctness half — four contexts on four threads, overlapping, produce correct and
/// non-interfering results. That is necessary and not sufficient: an engine that took one global
/// lock would pass every one of those assertions and still make a worker useless, because the whole
/// point of a worker is to run <em>while</em> the main context runs. Correct-but-serialized is the
/// outcome that would sink the item, and no correctness test can distinguish it.
/// </para>
/// <para>
/// <b>The workload is deliberately CPU-bound and allocation-light.</b> Anything that waits on I/O
/// would scale whether or not the engine serialized, and anything allocation-heavy would measure the
/// GC rather than the engine. Property access, arithmetic and calls in a loop is the shape that has
/// nowhere to hide.
/// </para>
/// <para>
/// <b>Both cache configurations are measured</b>, because Phase 4 §8/§9 put the bridge's and the
/// runner's constant sources through the process-shared <see cref="DictionaryCodeCache.Current"/>.
/// Today that is safe by construction — those hosts render on one thread per process — but under
/// item #18 two threads would reach it at once, and if the shared cache serialized them it would be
/// a worker blocker this phase introduced itself. Measuring both says whether that is so.
/// </para>
/// <para>
/// <b>Thread counts are interleaved</b>, for the reason <c>layout-passes.md</c> writes up: this host
/// drifts by tens of percent over tens of seconds, and a configuration measured in its own
/// contiguous block sits in its own slice of the drift.
/// </para>
/// </remarks>
internal static class JSContextScaling
{
    private static readonly int[] ThreadCounts = [1, 2, 4];

    /// <summary>
    /// CPU-bound, allocation-light, and self-checking: the loop's result is asserted so a run that
    /// silently computed nothing cannot be reported as a fast one.
    /// </summary>
    private const string Workload = @"
        (function () {
            var o = { a: 1, b: 2, c: 3 };
            function step(x) { return (x * 31 + o.a * 7 + o.b * 3 + o.c) % 1000003; }
            var acc = 1;
            for (var i = 0; i < 400000; i++) acc = step(acc);
            return acc;
        })()";

    private readonly record struct Row(int Threads, bool SharedCache, double WallMs, double PerThreadMs, double Speedup);

    public static int Run(int iterations, int warmup)
    {
        // One warm-up pass per configuration: the first context in a process pays for the built-in
        // registry's static initialization, and charging that to the 1-thread row would invent a
        // speedup out of startup cost.
        foreach (var shared in new[] { false, true })
            foreach (var n in ThreadCounts)
                for (var w = 0; w < warmup; w++)
                    MeasureOnce(n, shared);

        var samples = new Dictionary<(int, bool), List<double>>();
        foreach (var shared in new[] { false, true })
            foreach (var n in ThreadCounts)
                samples[(n, shared)] = new List<double>(iterations);

        for (var i = 0; i < iterations; i++)
            foreach (var shared in new[] { false, true })
                foreach (var n in ThreadCounts)
                    samples[(n, shared)].Add(MeasureOnce(n, shared));

        var rows = new List<Row>();
        foreach (var shared in new[] { false, true })
        {
            var baseline = Median(samples[(1, shared)]);
            foreach (var n in ThreadCounts)
            {
                var wall = Median(samples[(n, shared)]);
                rows.Add(new Row(n, shared, wall, wall / n, baseline * n / wall));
            }
        }

        WriteMarkdown(rows, iterations, warmup);
        return 0;
    }

    /// <summary>
    /// Runs the workload on <paramref name="threads"/> threads, each with its own context, and
    /// returns the wall time for the whole batch. Each thread does the same amount of work, so
    /// perfect scaling holds wall time flat as threads rise.
    /// </summary>
    private static double MeasureOnce(int threads, bool sharedCache)
    {
        var ready = new Barrier(threads);
        var workers = new Thread[threads];
        var failures = 0;

        for (var t = 0; t < threads; t++)
        {
            workers[t] = new Thread(() =>
            {
                using var context = new JSContext();
                if (sharedCache)
                    context.CodeCache = DictionaryCodeCache.Current;

                // Compile before the clock starts. The question is whether *execution* overlaps;
                // leaving the compile inside would let a serialized compiler masquerade as a
                // serialized engine, and Phase 4 already established compilation is cacheable.
                context.Eval(Workload);

                ready.SignalAndWait();
                var result = context.Eval(Workload);
                if (Math.Abs(result.DoubleValue) < 1)
                    Interlocked.Increment(ref failures);
            })
            { IsBackground = true };
        }

        var watch = Stopwatch.StartNew();
        foreach (var w in workers)
            w.Start();
        foreach (var w in workers)
            w.Join();
        watch.Stop();

        if (failures > 0)
            throw new InvalidOperationException($"{failures} thread(s) computed a degenerate result; the workload was optimised away.");

        return watch.Elapsed.TotalMilliseconds;
    }

    private static double Median(IEnumerable<double> values)
    {
        var copy = values.ToArray();
        Array.Sort(copy);
        var mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2;
    }

    private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    private static void WriteMarkdown(IReadOnlyList<Row> rows, int iterations, int warmup)
    {
        Console.WriteLine("# JSContext concurrency scaling");
        Console.WriteLine();
        Console.WriteLine($"- Iterations: {iterations} measured, {warmup} warm-up; medians, configurations interleaved");
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine("- One `JSContext` per thread, each running the same CPU-bound workload; compiled before the clock starts");
        Console.WriteLine("- Each thread does the *same* work, so perfect scaling holds wall time flat as threads rise");
        Console.WriteLine();
        Console.WriteLine("| code cache | threads | wall ms | ms per thread's work | speedup vs 1 thread |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            Console.WriteLine(
                $"| {(r.SharedCache ? "process-shared" : "per-context")} | {r.Threads} | {F(r.WallMs)} | " +
                $"{F(r.PerThreadMs)} | **{F(r.Speedup)}×** |");
        }
        Console.WriteLine();

        foreach (var shared in new[] { false, true })
        {
            var top = rows.Where(r => r.SharedCache == shared).OrderBy(r => r.Threads).Last();
            var label = shared ? "process-shared cache" : "per-context cache";
            Console.WriteLine(top.Speedup >= 1.5
                ? $"**{label}: {F(top.Speedup)}× at {top.Threads} threads — contexts do run concurrently.**"
                : $"**{label}: only {F(top.Speedup)}× at {top.Threads} threads — execution is largely serialized, "
                  + "and a worker would add threads without adding throughput.**");
        }
    }
}
