using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Broiler.Graphics;
using Broiler.HTML.Image;
// Both rasterizers now carry a partitioner of this name — item #4's copy here and item
// #3's in Broiler.Graphics — so the reference has to say which. This file measures the
// HTML one. The ambiguity goes away when the two rasterizers are unified.
using BRasterParallelism = Broiler.HTML.Image.BRasterParallelism;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Multithreading item #4: what band-parallel scanline raster is worth per corpus page, and
/// whether any thread setting changes a pixel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whole renders, not isolated fills.</b> A microbenchmark of one large <c>FillRect</c> would
/// report the band speedup at its theoretical best and say nothing about a page, because what a
/// page raster actually is — per the stage profile — is a display list of many primitives of very
/// different sizes, most of them below the threshold at which a fill is split at all. The number
/// that matters is what the parallelism does to a render, so a render is what this times.
/// </para>
/// <para>
/// <b>The pixels are compared, every setting, every page.</b> Item #4's exit gate is that the
/// single-threaded setting reproduces the parallel output exactly; a scaling table without that
/// check would be a claim about speed detached from a claim about correctness. Settings are
/// interleaved within each iteration for the reason given on <see cref="DecodeScaling"/> — this
/// host's throughput drifts enough over a run to swamp the effect if the measurement is blocked.
/// </para>
/// </remarks>
internal static class RasterScaling
{
    /// <summary>
    /// Renders every corpus page at every thread setting and reports medians, speedups and
    /// whether the image matched the single-threaded render. Non-zero exit code if any did not.
    /// </summary>
    public static int Run(int iterations, int warmup)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Need at least one iteration.");

        var settings = ThreadSettings();
        var configured = BRasterParallelism.MaxDegreeOfParallelism;
        var mismatches = new List<string>();
        var splits = new List<(string Page, (long InlineFills, long SplitFills, long InlineArea, long SplitArea) Counts)>();

        Console.WriteLine("# Raster thread scaling (multithreading item #4)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"- Viewport: {Corpus.ViewportWidth}x{Corpus.ViewportHeight}");
        Console.WriteLine($"- {iterations} measured iterations, {warmup} warm-up; figures are medians of whole renders");
        Console.WriteLine($"- Thread settings: {string.Join(", ", settings)}");
        Console.WriteLine();
        Console.WriteLine("| Page | " + string.Join(" | ", Array.ConvertAll(settings, t => $"{t}T ms")) + " | speedup | pixels |");
        Console.WriteLine("|---|" + string.Concat(Array.ConvertAll(settings, _ => "---:|")) + "---:|---|");

        try
        {
            foreach (var page in Corpus.Pages)
            {
                BRasterParallelism.MaxDegreeOfParallelism = 1;
                var reference = RenderPixels(page.Html);

                var identical = true;
                foreach (var threads in settings)
                {
                    BRasterParallelism.MaxDegreeOfParallelism = threads;
                    if (SameBytes(reference, RenderPixels(page.Html)))
                        continue;

                    identical = false;
                    mismatches.Add($"{page.Name} at {threads} threads");
                }

                var medians = Measure(page.Html, settings, iterations, warmup);
                var cells = new string[settings.Length];
                for (var i = 0; i < settings.Length; i++)
                    cells[i] = medians[i].ToString("F1", CultureInfo.InvariantCulture);

                var speedup = medians[0] / medians[^1];
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {page.Name} | {string.Join(" | ", cells)} | {speedup:F2}x | {(identical ? "identical" : "**DIFFERENT**")} |"));

                splits.Add((page.Name, CountFills(page.Html)));
            }
        }
        finally
        {
            BRasterParallelism.MaxDegreeOfParallelism = configured;
        }

        Console.WriteLine();
        Console.WriteLine("How much of each page's raster is in fills large enough to split:");
        Console.WriteLine();
        Console.WriteLine("| Page | fills inline | fills split | area inline | area split | share of area split |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach (var (name, counts) in splits)
        {
            var total = counts.InlineArea + counts.SplitArea;
            var share = total == 0 ? 0d : (double)counts.SplitArea / total;
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
        var result = new List<int>(settings);
        return [.. result];
    }

    private static double[] Measure(string html, int[] settings, int iterations, int warmup)
    {
        foreach (var threads in settings)
        {
            BRasterParallelism.MaxDegreeOfParallelism = threads;
            for (var i = 0; i < warmup; i++)
                RenderOnce(html);
        }

        var samples = new double[settings.Length][];
        for (var i = 0; i < settings.Length; i++)
            samples[i] = new double[iterations];

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var i = 0; i < settings.Length; i++)
            {
                BRasterParallelism.MaxDegreeOfParallelism = settings[i];
                var watch = Stopwatch.StartNew();
                RenderOnce(html);
                watch.Stop();
                samples[i][iteration] = watch.Elapsed.TotalMilliseconds;
            }
        }

        var medians = new double[settings.Length];
        for (var i = 0; i < settings.Length; i++)
        {
            Array.Sort(samples[i]);
            medians[i] = samples[i][iterations / 2];
        }

        return medians;
    }

    /// <summary>
    /// Renders once with the partitioner counting, at the full thread budget, and returns what it
    /// decided. This is the number that explains a scaling table rather than restating it.
    /// </summary>
    private static (long InlineFills, long SplitFills, long InlineArea, long SplitArea) CountFills(string html)
    {
        var configured = BRasterParallelism.MaxDegreeOfParallelism;
        BRasterParallelism.MaxDegreeOfParallelism = Environment.ProcessorCount;
        BRasterParallelism.ResetDiagnostics();
        BRasterParallelism.CollectDiagnostics = true;
        try
        {
            RenderOnce(html);
            return BRasterParallelism.Diagnostics;
        }
        finally
        {
            BRasterParallelism.CollectDiagnostics = false;
            BRasterParallelism.MaxDegreeOfParallelism = configured;
        }
    }

    private static void RenderOnce(string html)
    {
        using var bitmap = HtmlRender.RenderToImageWithStyleSet(
            html, Corpus.ViewportWidth, Corpus.ViewportHeight, backgroundColor: BColor.White);
        GC.KeepAlive(bitmap);
    }

    /// <summary>Renders and returns the encoded image, which is the comparison the exit gate wants.</summary>
    private static byte[] RenderPixels(string html)
    {
        using var bitmap = HtmlRender.RenderToImageWithStyleSet(
            html, Corpus.ViewportWidth, Corpus.ViewportHeight, backgroundColor: BColor.White);
        return bitmap.Encode();
    }

    private static bool SameBytes(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length)
            return false;
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i])
                return false;
        }

        return true;
    }
}
