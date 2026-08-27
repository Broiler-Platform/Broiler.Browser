using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.Diagnostics;
using Broiler.Layout.Engine;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Multithreading item #12: what parallel style recalc is worth per corpus page, and whether any
/// thread setting changes a pixel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three columns, because the item has two halves and they do not scale together.</b> The warm
/// pass resolves every element's cascade on N threads; the box walk then consumes the results in
/// document order on one thread and cannot be threaded (see <c>CssStyleRecalc</c>). So the table
/// reports the two sub-stages next to the render they add up to: a speedup on the pair says what
/// the item bought, and the residual <c>project</c> column says what is left to buy — which is the
/// Amdahl ceiling, stated rather than inferred.
/// </para>
/// <para>
/// <b>The baseline is the warm pass switched off, not one thread through it.</b> At a budget of one
/// <c>CssStyleRecalc.Warm</c> returns immediately and the box walk computes each element's cascade
/// as it reaches it — which is the code that shipped before this item, so it is the right zero. A
/// "one thread through the warm pass" column would measure the pass's own overhead against itself.
/// </para>
/// <para>
/// <b>Settings are interleaved within an iteration</b>, for the reason <c>DecodeScaling</c> gives:
/// this host's throughput drifts over a run by more than the effect being measured, so a setting
/// measured as a block reads as whatever the machine was doing at the time.
/// </para>
/// </remarks>
internal static class StyleScaling
{
    /// <summary>
    /// Renders every corpus page at every style-thread setting, reporting sub-stage medians,
    /// speedups and whether the image matched the sequential render. Non-zero exit code if any
    /// setting drew different pixels.
    /// </summary>
    public static int Run(int iterations, int warmup)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Need at least one iteration.");

        var settings = ThreadSettings();
        var configured = CssStyleRecalc.MaxDegreeOfParallelism;
        var mismatches = new List<string>();

        Console.WriteLine("# Parallel style recalc scaling (multithreading item #12)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"- Viewport: {Corpus.ViewportWidth}x{Corpus.ViewportHeight}");
        Console.WriteLine($"- {iterations} measured iterations, {warmup} warm-up; figures are medians");
        Console.WriteLine($"- Style threads: {string.Join(", ", settings)} (1 = warm pass off)");
        Console.WriteLine($"- Minimum elements to run the warm pass: {CssStyleRecalc.MinimumParallelElements}");
        Console.WriteLine($"- GC: {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}");
        Console.WriteLine();

        var rows = new List<(string Page, double[] Cascade, double[] Project, double[] Total, bool Identical)>();

        try
        {
            foreach (var page in Corpus.Pages)
            {
                var reference = RenderPixels(page.Html, threads: 1);
                var identical = true;
                foreach (var threads in settings)
                {
                    if (threads == 1)
                        continue;
                    if (SameBytes(reference, RenderPixels(page.Html, threads)))
                        continue;

                    identical = false;
                    mismatches.Add($"{page.Name} at {threads} style threads");
                }

                var (cascade, project, total) = Measure(page.Html, settings, iterations, warmup);
                rows.Add((page.Name, cascade, project, total, identical));
            }
        }
        finally
        {
            CssStyleRecalc.MaxDegreeOfParallelism = configured;
        }

        Console.WriteLine("Cascade sub-stages (`cascade (resolve)` + `cascade (project)`):");
        Console.WriteLine();
        Console.WriteLine(
            "| Page | " + string.Join(" | ", Array.ConvertAll(settings, t => $"{t}T ms")) +
            " | speedup | project residue |");
        Console.WriteLine("|---|" + string.Concat(Array.ConvertAll(settings, _ => "---:|")) + "---:|---:|");
        foreach (var row in rows)
        {
            var cells = new string[settings.Length];
            for (var i = 0; i < settings.Length; i++)
                cells[i] = (row.Cascade[i] + row.Project[i]).ToString("F1", CultureInfo.InvariantCulture);

            var baseline = row.Cascade[0] + row.Project[0];
            var best = row.Cascade[^1] + row.Project[^1];
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {row.Page} | {string.Join(" | ", cells)} | {baseline / best:F2}x | {row.Project[^1] / best * 100:F0}% |"));
        }

        Console.WriteLine();
        Console.WriteLine("End to end, the same renders:");
        Console.WriteLine();
        Console.WriteLine(
            "| Page | " + string.Join(" | ", Array.ConvertAll(settings, t => $"{t}T ms")) + " | speedup | pixels |");
        Console.WriteLine("|---|" + string.Concat(Array.ConvertAll(settings, _ => "---:|")) + "---:|---|");
        foreach (var row in rows)
        {
            var cells = new string[settings.Length];
            for (var i = 0; i < settings.Length; i++)
                cells[i] = row.Total[i].ToString("F1", CultureInfo.InvariantCulture);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {row.Page} | {string.Join(" | ", cells)} | {row.Total[0] / row.Total[^1]:F2}x "
                + $"| {(row.Identical ? "identical" : "**DIFFERENT**")} |"));
        }

        Console.WriteLine();
        if (mismatches.Count == 0)
        {
            Console.WriteLine("Every thread setting rendered pixel-identically to the sequential cascade.");
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

    private static (double[] Cascade, double[] Project, double[] Total) Measure(
        string html, int[] settings, int iterations, int warmup)
    {
        foreach (var threads in settings)
        {
            for (var i = 0; i < warmup; i++)
                RenderOnce(html, threads);
        }

        var cascade = new double[settings.Length][];
        var project = new double[settings.Length][];
        var total = new double[settings.Length][];
        for (var i = 0; i < settings.Length; i++)
        {
            cascade[i] = new double[iterations];
            project[i] = new double[iterations];
            total[i] = new double[iterations];
        }

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var i = 0; i < settings.Length; i++)
            {
                var sample = RenderOnce(html, settings[i]);
                cascade[i][iteration] = sample.Resolve;
                project[i][iteration] = sample.Project;
                total[i][iteration] = sample.Total;
            }
        }

        var cascadeMedians = new double[settings.Length];
        var projectMedians = new double[settings.Length];
        var totalMedians = new double[settings.Length];
        for (var i = 0; i < settings.Length; i++)
        {
            cascadeMedians[i] = Median(cascade[i]);
            projectMedians[i] = Median(project[i]);
            totalMedians[i] = Median(total[i]);
        }

        return (cascadeMedians, projectMedians, totalMedians);
    }

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    /// <summary>One render with the cascade sub-stages traced, at the given style-thread budget.</summary>
    private static (double Total, double Resolve, double Project) RenderOnce(string html, int threads)
    {
        CssStyleRecalc.MaxDegreeOfParallelism = threads;

        const int width = Corpus.ViewportWidth;
        const int height = Corpus.ViewportHeight;
        var clip = new RectangleF(0, 0, width, height);

        RenderStageTrace.Enabled = true;
        RenderStageTrace.Reset();
        try
        {
            var outer = Stopwatch.StartNew();
            var bitmap = new BBitmap(width, height);
            using (var container = new HtmlContainer())
            {
                container.Location = new PointF(0, 0);
                container.MaxSize = new SizeF(width, height);
                container.AvoidAsyncImagesLoading = true;
                container.AvoidImagesLateLoading = true;
                container.SetHtmlWithStyleSet(html, null, null);
                bitmap.Clear(BColor.White);
                container.PerformLayout(bitmap, clip);
                container.PerformPaint(bitmap, clip);
            }

            bitmap.Dispose();
            outer.Stop();

            var totals = RenderStageTrace.Totals();
            return (
                outer.Elapsed.TotalMilliseconds,
                totals.TryGetValue(RenderStageTrace.SubStages.CascadeResolve, out var resolve) ? resolve : 0,
                totals.TryGetValue(RenderStageTrace.SubStages.CascadeProject, out var project) ? project : 0);
        }
        finally
        {
            RenderStageTrace.Enabled = false;
        }
    }

    private static byte[] RenderPixels(string html, int threads)
    {
        CssStyleRecalc.MaxDegreeOfParallelism = threads;
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
