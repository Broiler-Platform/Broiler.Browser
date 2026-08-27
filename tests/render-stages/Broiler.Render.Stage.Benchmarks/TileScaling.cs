using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.IR;
using BBitmap = Broiler.HTML.Image.BBitmap;
// Both rasterizers now carry a partitioner of this name — item #4's copy here and item
// #3's in Broiler.Graphics — so the reference has to say which. This file measures the
// HTML one. The ambiguity goes away when the two rasterizers are unified.
using BRasterParallelism = Broiler.HTML.Image.BRasterParallelism;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Multithreading item #5: what tile-parallel display-list replay is worth per corpus page, whether
/// any tile setting changes a pixel, and — when a page does not move — which of the two reasons it
/// was not tiled for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written against item #4's failure mode.</b> Band parallelism shipped with a threshold nobody
/// had measured, and at that threshold four of the five corpus pages split no fill at all: the
/// feature was inert and the scaling table alone could not say so. So this harness reports the
/// partitioner's decisions next to the times, and separates "the surface refused" from "the display
/// list refused" — a flat row means something different in each case.
/// </para>
/// <para>
/// <b>The baseline is the pure sequential rasterizer.</b> A tile view runs its scanline bands
/// inline, so a table of tile settings against today's default (band-parallel) render would compare
/// two kinds of parallelism rather than measure one. The first column is therefore one tile and one
/// band — no threads anywhere — and the band-parallel column is printed beside it, so the two
/// answers the roadmap wants are both visible: what tiling is worth against no parallelism, and
/// what it is worth against the parallelism the repository already has.
/// </para>
/// <para>
/// <b>The stage is timed, and the render is timed beside it.</b> Tiling changes one stage, and on a
/// page the stage profile puts at 11.9% raster an end-to-end table would divide the effect by eight
/// before reporting it — which is how a real speedup gets mistaken for noise and a real regression
/// gets missed. So the first table is <c>PerformPaint</c>, which is the display-list replay plus the
/// walk that builds it (0.3–0.7% of a render, per the profile), and the second is the whole render,
/// which is what a user waits for.
/// </para>
/// </remarks>
internal static class TileScaling
{
    /// <summary>
    /// Renders every corpus page at every tile setting and reports medians, speedups, the tiling
    /// decisions and whether the image matched the sequential render. Non-zero exit code if any
    /// setting drew different pixels, or if any tile view reached the compat backend.
    /// </summary>
    public static int Run(int iterations, int warmup)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Need at least one iteration.");

        var settings = TileSettings();
        var configuredTiles = TileParallelReplay.MaxDegreeOfParallelism;
        var configuredBands = BRasterParallelism.MaxDegreeOfParallelism;
        var mismatches = new List<string>();
        var decisions = new List<(string Page, (long SurfaceRefusals, long ContentRefusals, long TiledReplays, long Tiles) Counts)>();

        Console.WriteLine("# Raster tile scaling (multithreading item #5)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"- Viewport: {Corpus.ViewportWidth}x{Corpus.ViewportHeight}");
        Console.WriteLine($"- {iterations} measured iterations, {warmup} warm-up; figures are medians of whole renders");
        Console.WriteLine($"- Tile settings: {string.Join(", ", settings)}; bands held at 1 throughout except the last column");
        Console.WriteLine($"- Minimum rows per tile: {TileParallelReplay.MinimumTileRows}");
        Console.WriteLine();
        Console.WriteLine("Raster stage (`PerformPaint`: display-list replay plus the walk that builds it):");
        Console.WriteLine();
        Console.WriteLine(
            "| Page | " + string.Join(" | ", Array.ConvertAll(settings, t => $"{t} tile ms")) +
            $" | speedup | {Environment.ProcessorCount} band ms |");
        Console.WriteLine(
            "|---|" + string.Concat(Array.ConvertAll(settings, _ => "---:|")) + "---:|---:|");

        var endToEnd = new List<(string Page, double[] Medians, bool Identical)>();

        try
        {
            TileParallelReplay.ResetDiagnostics();

            foreach (var page in Corpus.Pages)
            {
                var reference = RenderPixels(page.Html, tiles: 1, bands: 1);
                var identical = true;

                // Both budgets are swept, not just the tile one: the exit gate is that the pixels do
                // not depend on how the work was divided, and bands and tiles divide it differently.
                foreach (var tiles in settings)
                {
                    foreach (var bands in new[] { 1, Environment.ProcessorCount })
                    {
                        if (tiles == 1 && bands == 1)
                            continue;
                        if (SameBytes(reference, RenderPixels(page.Html, tiles, bands)))
                            continue;

                        identical = false;
                        mismatches.Add($"{page.Name} at {tiles} tiles / {bands} bands");
                    }
                }

                var (paintMedians, totalMedians) = Measure(page.Html, settings, iterations, warmup);
                var (bandPaint, bandTotal) = MeasureOne(
                    page.Html, tiles: 1, bands: Environment.ProcessorCount, iterations, warmup);

                var cells = new string[settings.Length];
                for (var i = 0; i < settings.Length; i++)
                    cells[i] = paintMedians[i].ToString("F1", CultureInfo.InvariantCulture);

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"| {page.Name} | {string.Join(" | ", cells)} | {paintMedians[0] / paintMedians[^1]:F2}x | {bandPaint:F1} |"));

                endToEnd.Add((page.Name, [.. totalMedians, bandTotal], identical));
                decisions.Add((page.Name, CountDecisions(page.Html)));
            }
        }
        finally
        {
            TileParallelReplay.MaxDegreeOfParallelism = configuredTiles;
            BRasterParallelism.MaxDegreeOfParallelism = configuredBands;
        }

        Console.WriteLine();
        Console.WriteLine("End to end, the same renders:");
        Console.WriteLine();
        Console.WriteLine(
            "| Page | " + string.Join(" | ", Array.ConvertAll(settings, t => $"{t} tile ms")) +
            $" | speedup | {Environment.ProcessorCount} band ms | pixels |");
        Console.WriteLine(
            "|---|" + string.Concat(Array.ConvertAll(settings, _ => "---:|")) + "---:|---:|---|");
        foreach (var (name, medians, identical) in endToEnd)
        {
            var cells = new string[settings.Length];
            for (var i = 0; i < settings.Length; i++)
                cells[i] = medians[i].ToString("F1", CultureInfo.InvariantCulture);

            var speedup = medians[0] / medians[settings.Length - 1];
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {name} | {string.Join(" | ", cells)} | {speedup:F2}x | {medians[^1]:F1} | {(identical ? "identical" : "**DIFFERENT**")} |"));
        }

        Console.WriteLine();
        Console.WriteLine("What the driver decided, at the full tile budget:");
        Console.WriteLine();
        Console.WriteLine("| Page | replays tiled | tiles created | refused by surface | refused by content |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        foreach (var (name, counts) in decisions)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {name} | {counts.TiledReplays:N0} | {counts.Tiles:N0} | {counts.SurfaceRefusals:N0} | {counts.ContentRefusals:N0} |"));
        }

        Console.WriteLine();
        var fallbacks = TileParallelReplay.CompatFallbacks;
        Console.WriteLine($"Tile views that reached the compat backend: **{fallbacks:N0}** (expected 0).");

        Console.WriteLine();
        if (mismatches.Count == 0 && fallbacks == 0)
        {
            Console.WriteLine("Every setting rendered pixel-identically to the sequential rasterizer.");
            return 0;
        }

        if (mismatches.Count > 0)
            Console.WriteLine($"**{mismatches.Count} setting(s) rendered different pixels: {string.Join(", ", mismatches)}**");
        if (fallbacks > 0)
            Console.WriteLine("**A tile view fell back to the compat backend; the eligibility test has a hole.**");
        return 1;
    }

    private static int[] TileSettings()
    {
        var settings = new SortedSet<int> { 1, 2, 4, Environment.ProcessorCount };
        return [.. settings];
    }

    private static (double[] Paint, double[] Total) Measure(string html, int[] settings, int iterations, int warmup)
    {
        foreach (var tiles in settings)
        {
            for (var i = 0; i < warmup; i++)
                RenderOnce(html, tiles, bands: 1);
        }

        var paint = new double[settings.Length][];
        var total = new double[settings.Length][];
        for (var i = 0; i < settings.Length; i++)
        {
            paint[i] = new double[iterations];
            total[i] = new double[iterations];
        }

        // Interleaved within each iteration, for the reason given on DecodeScaling: this host's
        // throughput drifts enough over a run to swamp the effect if a setting is measured in a block.
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var i = 0; i < settings.Length; i++)
            {
                var sample = RenderOnce(html, settings[i], bands: 1);
                paint[i][iteration] = sample.Paint;
                total[i][iteration] = sample.Total;
            }
        }

        var paintMedians = new double[settings.Length];
        var totalMedians = new double[settings.Length];
        for (var i = 0; i < settings.Length; i++)
        {
            paintMedians[i] = Median(paint[i]);
            totalMedians[i] = Median(total[i]);
        }

        return (paintMedians, totalMedians);
    }

    private static (double Paint, double Total) MeasureOne(string html, int tiles, int bands, int iterations, int warmup)
    {
        for (var i = 0; i < warmup; i++)
            RenderOnce(html, tiles, bands);

        var paint = new double[iterations];
        var total = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var sample = RenderOnce(html, tiles, bands);
            paint[i] = sample.Paint;
            total[i] = sample.Total;
        }

        return (Median(paint), Median(total));
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    /// <summary>
    /// Renders once with the driver counting, at the full tile budget, and returns what it decided.
    /// This is the number that explains a scaling table rather than restating it.
    /// </summary>
    private static (long SurfaceRefusals, long ContentRefusals, long TiledReplays, long Tiles) CountDecisions(string html)
    {
        var fallbacks = TileParallelReplay.CompatFallbacks;
        TileParallelReplay.ResetDiagnostics();
        TileParallelReplay.CollectDiagnostics = true;
        try
        {
            RenderOnce(html, Environment.ProcessorCount, bands: 1);
            return TileParallelReplay.Diagnostics;
        }
        finally
        {
            TileParallelReplay.CollectDiagnostics = false;

            // ResetDiagnostics zeroes the process-wide fallback count too; carry the running total
            // across so the figure printed at the end covers every render this harness drove.
            for (var i = 0L; i < fallbacks; i++)
                TileParallelReplay.NoteCompatFallback();
        }
    }

    /// <summary>
    /// One render, with the paint stage timed inside it. Mirrors <c>HtmlRender.RenderToImageCore</c>
    /// the way <see cref="StageProfile"/> does, so the stage boundary here is the same public call
    /// the profile attributes 46.9–80.8% of a render to rather than an instrumented copy of it.
    /// </summary>
    private static (double Total, double Paint) RenderOnce(string html, int tiles, int bands)
    {
        TileParallelReplay.MaxDegreeOfParallelism = tiles;
        BRasterParallelism.MaxDegreeOfParallelism = bands;

        const int width = Corpus.ViewportWidth;
        const int height = Corpus.ViewportHeight;
        var clip = new RectangleF(0, 0, width, height);

        var outer = Stopwatch.StartNew();
        var bitmap = new BBitmap(width, height);
        using var container = new HtmlContainer();
        container.Location = new PointF(0, 0);
        container.MaxSize = new SizeF(width, height);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;
        container.SetHtmlWithStyleSet(html, null, null);
        bitmap.Clear(BColor.White);
        container.PerformLayout(bitmap, clip);

        var paint = Stopwatch.StartNew();
        container.PerformPaint(bitmap, clip);
        paint.Stop();

        bitmap.Dispose();
        outer.Stop();

        return (outer.Elapsed.TotalMilliseconds, paint.Elapsed.TotalMilliseconds);
    }

    /// <summary>Renders and returns the encoded image, which is the comparison the exit gate wants.</summary>
    private static byte[] RenderPixels(string html, int tiles, int bands)
    {
        TileParallelReplay.MaxDegreeOfParallelism = tiles;
        BRasterParallelism.MaxDegreeOfParallelism = bands;
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
