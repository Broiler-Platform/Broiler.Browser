using System.Drawing;
using System.Globalization;
using Broiler.Layout.IR;
using Broiler.HTML.Core.IR;
using Broiler.HTML.Image;

namespace Broiler.Cli;

/// <summary>
/// Runs layout fuzz testing from the CLI. Generates random HTML/CSS,
/// lays out each document, and checks Fragment tree invariants.
/// Failures are saved to the output directory.
/// </summary>
internal sealed class LayoutFuzzService
{
    /// <summary>
    /// Runs the layout fuzz and prints results to the console.
    /// Returns 0 if no violations were found, 1 if any violations occurred.
    /// </summary>
    /// <remarks>
    /// With <paramref name="threads"/> above 1 the seed range is split into contiguous shards
    /// run by child processes, not threads. Each case lays out a document, and layout reaches
    /// the unsynchronised font caches the static-state audit names, so two cases in one process
    /// would race; a shard per process costs one startup and is exactly as deterministic,
    /// because a case's behaviour depends only on its seed.
    /// </remarks>
    public int Run(
        int count,
        int? seed = null,
        string? outputDir = null,
        int? threads = null,
        bool emitTotals = false)
    {
        int baseSeed = seed ?? Environment.TickCount;
        string failDir = outputDir ?? Path.Combine(Directory.GetCurrentDirectory(), "fuzz-failures");
        int failureCount = 0;
        int crashCount = 0;

        int shardCount = LayoutFuzzShards.ResolveShardCount(threads, count, Environment.ProcessorCount);
        if (shardCount > 1)
            return RunSharded(count, baseSeed, failDir, shardCount);

        Console.WriteLine($"Layout fuzz: running {count} cases (base seed {baseSeed})…");

        for (int i = 0; i < count; i++)
        {
            int caseSeed = baseSeed + i;
            var gen = new HtmlCssGenerator(caseSeed);
            string html = gen.Generate();

            try
            {
                var fragment = BuildFragmentTree(html);
                if (fragment is null)
                {
                    crashCount++;
                    continue;
                }

                var violations = FragmentInvariantChecker.Check(fragment);
                if (violations.Count > 0)
                {
                    failureCount++;
                    string json = FragmentJsonDumper.ToJson(fragment);

                    string minimized = DeltaMinimizer.Minimize(html, candidate =>
                    {
                        var f = BuildFragmentTree(candidate);
                        if (f is null) return false;
                        return FragmentInvariantChecker.Check(f).Count > 0;
                    });

                    SaveFailure(caseSeed, html, minimized, json, violations, failDir);
                    Console.WriteLine($"  [FAIL] seed {caseSeed}: {violations.Count} violation(s)");
                }
            }
            catch (Exception)
            {
                crashCount++;
            }

            // Progress indicator every 100 cases
            if ((i + 1) % 100 == 0)
            {
                Console.WriteLine($"  … {i + 1}/{count} done ({failureCount} failures, {crashCount} crashes)");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Fuzz complete: {count} cases, {failureCount} failure(s), {crashCount} crash(es).");

        if (failureCount > 0)
        {
            Console.WriteLine($"Failure details saved to: {failDir}");
        }

        // A shard's totals have to reach the parent that spawned it, and parsing English prose
        // out of a transcript is how that breaks the first time the prose is reworded. Only a
        // shard emits this; a run somebody typed themselves has no parent to report to.
        if (emitTotals)
            Console.WriteLine(LayoutFuzzShards.FormatTotals(new LayoutFuzzTotals(count, failureCount, crashCount)));

        return failureCount > 0 ? 1 : 0;
    }

    /// <summary>
    /// Runs the same seed range as the sequential path, split across child processes. The union
    /// of the shards is the identical seed set — <see cref="LayoutFuzzShards.Partition"/> is
    /// what guarantees that, and is unit-tested for it — so a sharded run finds the same
    /// failures and writes the same per-seed files.
    /// </summary>
    private static int RunSharded(int count, int baseSeed, string failDir, int shardCount)
    {
        var shards = LayoutFuzzShards.Partition(count, baseSeed, shardCount);
        Console.WriteLine(
            $"Layout fuzz: running {count} cases (base seed {baseSeed}) across {shards.Count} worker process(es)…");

        var items = shards
            .Select(shard => new BatchItem($"seeds {shard.BaseSeed}..{shard.BaseSeed + shard.Count - 1}", failDir))
            .ToList();

        var batch = BatchRunner.RunInChildProcesses(
            items,
            shards.Count,
            (_, index) =>
            {
                var shard = shards[index];
                return
                [
                    "--fuzz-layout",
                    "--count",
                    shard.Count.ToString(CultureInfo.InvariantCulture),
                    "--seed",
                    shard.BaseSeed.ToString(CultureInfo.InvariantCulture),
                    "--threads",
                    "1",
                    "--output",
                    failDir,
                    "--emit-totals",
                ];
            },
            // A shard exits 1 when it finds violations, which is the fuzz doing its job — the
            // generic "N failed" tally would read that as N broken workers.
            summarize: false,
            filterTranscript: LayoutFuzzShards.StripTotals);

        var totals = LayoutFuzzShards.Aggregate(batch.Outcomes.Select(outcome => outcome.StandardOutput));

        Console.WriteLine();
        if (totals.Cases != count)
        {
            // A shard that died before printing its totals would otherwise be invisible: the
            // run would report fewer cases than it was asked for and still look complete.
            Console.Error.WriteLine(
                $"Warning: only {totals.Cases} of {count} case(s) reported totals — a worker process " +
                "exited without finishing. Re-run with --threads 1 to reproduce.");
        }

        Console.WriteLine(
            $"Fuzz complete: {totals.Cases} cases, {totals.Failures} failure(s), {totals.Crashes} crash(es).");
        if (totals.Failures > 0)
            Console.WriteLine($"Failure details saved to: {failDir}");

        return totals.Failures > 0 || totals.Cases != count ? 1 : 0;
    }

    private static Fragment? BuildFragmentTree(string html)
    {
        try
        {
            using var container = new HtmlContainer();
            container.AvoidAsyncImagesLoading = true;
            container.AvoidImagesLateLoading = true;
            container.SetHtmlWithStyleSet(html);

            var clip = new RectangleF(0, 0, 500, 500);
            container.PerformLayout(clip);

            return container.LatestFragmentTree;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveFailure(
        int seed,
        string html,
        string minimizedHtml,
        string json,
        IReadOnlyList<string> violations,
        string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string prefix = Path.Combine(dir, $"fuzz_seed_{seed}");

            File.WriteAllText($"{prefix}.html", html);
            File.WriteAllText($"{prefix}_minimized.html", minimizedHtml);
            File.WriteAllText($"{prefix}.json", json);
            File.WriteAllText($"{prefix}_violations.txt",
                string.Join(Environment.NewLine, violations));
        }
        catch
        {
            // Best-effort save.
        }
    }
}
