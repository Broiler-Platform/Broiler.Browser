using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using Broiler.Graphics;
using Broiler.HTML.Image;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Multithreading item #19: what the GC configuration is worth on a headless batch render.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a measurement, not a setting.</b> The roadmap row for item #19 says "Unknown until
/// measured", and the honest way to close it is to report the numbers and let the configuration
/// follow. The emitter reports whichever mode the process was launched in, along with allocation and
/// collection counts, so the two modes are compared by running it twice rather than by asserting a
/// preference in a props file.
/// </para>
/// <para>
/// <b>Server GC cannot be switched at runtime</b> — <see cref="GCSettings.IsServerGC"/> is read-only
/// and the mode is fixed when the runtime starts. So a single process cannot compare the two, and
/// the comparison is two runs of this command with <c>DOTNET_gcServer</c> set differently. The
/// runner script in the README does exactly that; this emitter's job is to make one run's numbers
/// machine-readable and self-describing.
/// </para>
/// <para>
/// <b>Concurrent GC is reported but also not switched here.</b> It is settable through
/// <c>DOTNET_gcConcurrent</c> at startup for the same reason, and mutating
/// <see cref="GCSettings.LatencyMode"/> mid-process would measure a mode transition rather than a
/// mode.
/// </para>
/// </remarks>
internal static class GcConfigurationMetrics
{
    /// <summary>
    /// Renders the whole corpus <paramref name="rounds"/> times and reports throughput together with
    /// the GC counters, in the process's current GC configuration.
    /// </summary>
    public static void Run(int rounds)
    {
        if (rounds < 1)
            throw new ArgumentOutOfRangeException(nameof(rounds), rounds, "Need at least one round.");

        // One unmeasured round: the first render in a process pays JIT and static-constructor costs,
        // and gen-2 counts from a cold start would otherwise be attributed to the GC mode.
        RenderCorpusOnce();

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);

        var watch = Stopwatch.StartNew();
        for (var round = 0; round < rounds; round++)
            RenderCorpusOnce();
        watch.Stop();

        var gen0 = GC.CollectionCount(0) - gen0Before;
        var gen1 = GC.CollectionCount(1) - gen1Before;
        var gen2 = GC.CollectionCount(2) - gen2Before;
        var allocated = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;

        var renders = rounds * Corpus.Pages.Count;
        var ms = watch.Elapsed.TotalMilliseconds;

        Console.WriteLine("# GC configuration probe (multithreading item #19)");
        Console.WriteLine();
        Console.WriteLine($"- Mode: **{(GCSettings.IsServerGC ? "Server" : "Workstation")} GC**, "
            + $"latency mode {GCSettings.LatencyMode}");
        Console.WriteLine($"- Cores: {Environment.ProcessorCount} logical");
        Console.WriteLine($"- Rounds: {rounds} over {Corpus.Pages.Count} pages = {renders} renders "
            + $"at {Corpus.ViewportWidth}x{Corpus.ViewportHeight}");
        Console.WriteLine();
        Console.WriteLine("| Metric | Value |");
        Console.WriteLine("|---|---:|");
        Console.WriteLine($"| wall time | {ms.ToString("F1", CultureInfo.InvariantCulture)} ms |");
        Console.WriteLine($"| per render | {(ms / renders).ToString("F2", CultureInfo.InvariantCulture)} ms |");
        Console.WriteLine($"| allocated | {(allocated / 1024.0 / 1024.0).ToString("F1", CultureInfo.InvariantCulture)} MiB |");
        Console.WriteLine($"| allocated per render | {(allocated / renders / 1024.0 / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} MiB |");
        Console.WriteLine($"| gen 0 collections | {gen0} |");
        Console.WriteLine($"| gen 1 collections | {gen1} |");
        Console.WriteLine($"| gen 2 collections | {gen2} |");
        Console.WriteLine();
        Console.WriteLine("Compare two modes by running this command twice with `DOTNET_gcServer=0` and");
        Console.WriteLine("`DOTNET_gcServer=1`; the mode is fixed at runtime start and cannot be switched here.");
    }

    private static void RenderCorpusOnce()
    {
        foreach (var page in Corpus.Pages)
        {
            using var bitmap = HtmlRender.RenderToImageWithStyleSet(
                page.Html, Corpus.ViewportWidth, Corpus.ViewportHeight, backgroundColor: BColor.White);
            GC.KeepAlive(bitmap);
        }
    }
}
