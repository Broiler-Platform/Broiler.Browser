using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Broiler.Graphics;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Multithreading item #3: what band-parallel scanline raster is worth in
/// <c>Broiler.Graphics.BCanvas</c> — the copy behind <see cref="BImageRenderer"/>, Broiler.UI and
/// the Writer — and whether any thread setting changes a pixel.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the benchmark §2 of the roadmap said did not exist.</b> Item #3 was credited with the
/// same 4–6× as item #4 on the strength of the stage profile, and the profile times the *other*
/// rasterizer. Nothing here can be inferred from those pages, so nothing here is: the scenes are
/// <see cref="GraphicsSceneCorpus"/>, built from the draw-call mix Broiler.UI actually issues.
/// </para>
/// <para>
/// <b>Whole scenes, not isolated fills.</b> A microbenchmark of one large fill would report the
/// band speedup at its theoretical best and say nothing about a window, because what a window is —
/// per the call-mix count on <see cref="GraphicsSceneCorpus"/> — is many small rectangles and text.
/// The partitioner diagnostics are printed alongside the timings for exactly the reason item #4
/// needed them: a flat row means either "the threads did not help" or "no fill was ever big enough
/// to split", and those call for opposite next steps.
/// </para>
/// <para>
/// <b>The pixels are compared, every setting, every scene.</b> The exit gate is that the
/// single-threaded setting reproduces the parallel output exactly; a scaling table without that
/// check would be a claim about speed detached from a claim about correctness.
/// </para>
/// <para>
/// <b><c>--only-threads N</c> times one setting per process, and it exists for the comparison the
/// interleaved table cannot make.</b> Deciding what the port is worth means timing this build
/// against one that does not have it, and two builds cannot be interleaved inside one process. It
/// was also used to check the interleaving itself, since <see cref="RasterScaling"/> and
/// <see cref="DecodeScaling"/> alternate settings on the assumption that the settings do not
/// disturb each other: on the <c>canvas</c> scene the one-thread column reads 365 ms interleaved
/// and 362–375 ms alone, so on this harness the assumption holds and the interleaved table can be
/// quoted.
/// </para>
/// <para>
/// <b>What does disturb a reading is how much rendering the process has already done</b>, which is
/// why the scenes always run in the order <see cref="GraphicsSceneCorpus.Scenes"/> lists them. A
/// process that replays <em>only</em> <c>canvas</c> measures it at 823 ms where the same build in
/// this harness measures 362 ms — the scene is 13 enormous fills, so it enters few enough calls to
/// stay on OSR-compiled code, and the 1 500 small fills of <c>chrome</c> ahead of it are what
/// promote the fill path first. A figure from this command is comparable to another figure from
/// this command and to nothing else.
/// </para>
/// </remarks>
internal static class GraphicsRasterScaling
{
    public static int Run(int iterations, int warmup, int? onlyThreads = null)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Need at least one iteration.");

        int[] settings = onlyThreads is { } single ? [single] : ThreadSettings();
        int configured = BRasterParallelism.MaxDegreeOfParallelism;
        var mismatches = new List<string>();
        var splits = new List<(string Scene, (long InlineFills, long SplitFills, long InlineArea, long SplitArea) Counts)>();

        Console.WriteLine("# Broiler.Graphics raster thread scaling (multithreading item #3)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"- Surface: {GraphicsSceneCorpus.SurfaceWidth}x{GraphicsSceneCorpus.SurfaceHeight}");
        Console.WriteLine($"- Text backend: {BImageRenderer.DescribeSystemTextFont()}");
        Console.WriteLine($"- {iterations} measured iterations, {warmup} warm-up; figures are medians of whole scene replays");
        Console.WriteLine($"- Thread settings: {string.Join(", ", settings)}");
        Console.WriteLine(onlyThreads is null
            ? "- Settings interleaved within each iteration; checked against `--only-threads`, which agrees."
            : "- One setting per process. Comparable to other runs of this command, and to nothing else.");
        Console.WriteLine();
        Console.WriteLine("| Scene | " + string.Join(" | ", Array.ConvertAll(settings, t => $"{t}T ms")) + (onlyThreads is null ? " | speedup | pixels |" : " | pixels |"));
        Console.WriteLine("|---|" + string.Concat(Array.ConvertAll(settings, _ => "---:|")) + (onlyThreads is null ? "---:|---|" : "---|"));

        try
        {
            foreach ((string name, Func<BRenderList> build) in GraphicsSceneCorpus.Scenes)
            {
                BRenderList list = build();

                BRasterParallelism.MaxDegreeOfParallelism = 1;
                byte[] reference = RenderPixels(list);

                bool identical = true;
                foreach (int threads in settings)
                {
                    BRasterParallelism.MaxDegreeOfParallelism = threads;
                    if (SameBytes(reference, RenderPixels(list)))
                        continue;

                    identical = false;
                    mismatches.Add($"{name} at {threads} threads");
                }

                double[] medians;
                using (var renderer = new BImageRenderer())
                using (var surface = new BImageSurface(Descriptor))
                    medians = Measure(renderer, surface, list, settings, iterations, warmup);

                var cells = new string[settings.Length];
                for (int i = 0; i < settings.Length; i++)
                    cells[i] = medians[i].ToString("F1", CultureInfo.InvariantCulture);

                string speedup = onlyThreads is null
                    ? string.Create(CultureInfo.InvariantCulture, $" {medians[0] / medians[^1]:F2}x |")
                    : string.Empty;
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {name} | {string.Join(" | ", cells)} |{speedup} {(identical ? "identical" : "**DIFFERENT**")} |"));

                splits.Add((name, CountFills(list)));
            }
        }
        finally
        {
            BRasterParallelism.MaxDegreeOfParallelism = configured;
        }

        Console.WriteLine();
        Console.WriteLine("How much of each scene's raster is in fills large enough to split:");
        Console.WriteLine();
        Console.WriteLine("| Scene | fills inline | fills split | area inline | area split | share of area split |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach ((string name, var counts) in splits)
        {
            long total = counts.InlineArea + counts.SplitArea;
            double share = total == 0 ? 0d : (double)counts.SplitArea / total;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {name} | {counts.InlineFills:N0} | {counts.SplitFills:N0} | {counts.InlineArea:N0} | {counts.SplitArea:N0} | {share:P1} |"));
        }

        Console.WriteLine();
        if (mismatches.Count == 0)
        {
            Console.WriteLine("Every setting rendered pixel-identically to the single-threaded rasterizer.");
            return 0;
        }

        Console.WriteLine($"**{mismatches.Count} setting(s) rendered different pixels: {string.Join(", ", mismatches)}**");
        return 1;
    }

    private static int[] ThreadSettings()
    {
        var settings = new SortedSet<int> { 1, 2, 4, Environment.ProcessorCount };
        return [.. settings];
    }

    /// <summary>
    /// Times one scene at each thread setting, replaying into a surface created once.
    /// </summary>
    /// <remarks>
    /// <b>The renderer and the surface are reused, and that is a correction rather than a
    /// convenience.</b> Creating them per replay puts a 5 MB buffer allocation inside every timed
    /// region, and with the settings interleaved the resulting GC lands on whichever setting
    /// happens to follow it — which showed up as the two-thread column being reproducibly *slower*
    /// than the one-thread column on all three scenes, an ordering artefact and not a property of
    /// two threads. A host that draws frames holds its surface, so reusing one is also the shape
    /// being measured.
    /// </remarks>
    private static double[] Measure(BImageRenderer renderer, BImageSurface surface, BRenderList list, int[] settings, int iterations, int warmup)
    {
        foreach (int threads in settings)
        {
            BRasterParallelism.MaxDegreeOfParallelism = threads;
            for (int i = 0; i < warmup; i++)
                RenderOnce(renderer, surface, list);
        }

        var samples = new double[settings.Length][];
        for (int i = 0; i < settings.Length; i++)
            samples[i] = new double[iterations];

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int i = 0; i < settings.Length; i++)
            {
                BRasterParallelism.MaxDegreeOfParallelism = settings[i];
                var watch = Stopwatch.StartNew();
                RenderOnce(renderer, surface, list);
                watch.Stop();
                samples[i][iteration] = watch.Elapsed.TotalMilliseconds;
            }
        }

        var medians = new double[settings.Length];
        for (int i = 0; i < settings.Length; i++)
        {
            Array.Sort(samples[i]);
            medians[i] = samples[i][iterations / 2];
        }

        return medians;
    }

    /// <summary>
    /// Replays once with the partitioner counting, at the full thread budget, and returns what it
    /// decided. This is the number that explains a scaling table rather than restating it.
    /// </summary>
    private static (long InlineFills, long SplitFills, long InlineArea, long SplitArea) CountFills(BRenderList list)
    {
        int configured = BRasterParallelism.MaxDegreeOfParallelism;
        BRasterParallelism.MaxDegreeOfParallelism = Environment.ProcessorCount;
        BRasterParallelism.ResetDiagnostics();
        BRasterParallelism.CollectDiagnostics = true;
        try
        {
            using var renderer = new BImageRenderer();
            using var surface = new BImageSurface(Descriptor);
            RenderOnce(renderer, surface, list);
            return BRasterParallelism.Diagnostics;
        }
        finally
        {
            BRasterParallelism.CollectDiagnostics = false;
            BRasterParallelism.MaxDegreeOfParallelism = configured;
        }
    }

    private static void RenderOnce(BImageRenderer renderer, BImageSurface surface, BRenderList list) =>
        renderer.Render(surface, list, new BFrameContext(BColor.White));

    /// <summary>Replays and returns the encoded image, which is the comparison the exit gate wants.</summary>
    private static byte[] RenderPixels(BRenderList list)
    {
        using var renderer = new BImageRenderer();
        using BBitmap bitmap = renderer.RenderToImage(list, Descriptor, new BFrameContext(BColor.White));
        return bitmap.CopyRgba();
    }

    private static BSurfaceDescriptor Descriptor =>
        BSurfaceDescriptor.Default(new BSize(GraphicsSceneCorpus.SurfaceWidth, GraphicsSceneCorpus.SurfaceHeight));

    private static bool SameBytes(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
                return false;
        }

        return true;
    }
}
