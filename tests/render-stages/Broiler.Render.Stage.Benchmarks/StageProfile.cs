using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.Diagnostics;
using Broiler.Layout.IR;
using BBitmap = Broiler.HTML.Image.BBitmap;
// Both rasterizers now carry a partitioner of this name — item #4's copy here and item
// #3's in Broiler.Graphics — so the reference has to say which. This file measures the
// HTML one. The ambiguity goes away when the two rasterizers are unified.
using BRasterParallelism = Broiler.HTML.Image.BRasterParallelism;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Attributes headless-render wall time to named stages, which is the whole of P0-a's exit gate:
/// <i>a published profile that attributes ≥90% of headless-render wall time to named stages,
/// reproducible from one command.</i>
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a stopwatch, not BenchmarkDotNet, and the difference is the point.</b> BenchmarkDotNet
/// answers "how long does this operation take, precisely" — which is what the benchmark classes in
/// this project are for. The exit gate asks a different question: "what fraction of one real render
/// is each stage", and that has to be measured *inside a single render*, on stages that are nested
/// and shared state. Running each stage as an isolated BenchmarkDotNet case would measure five
/// operations that never see each other's caches and then add up numbers that no run ever produced.
/// </para>
/// <para>
/// <b>The stage boundaries are the public pipeline, not instrumentation added to it.</b>
/// <c>HtmlRender.RenderToImageCore</c> — the entry every headless consumer reaches, from the WPT
/// runner to the CLI — is four public calls in a row, and this walks the same four with a timer
/// around each. No engine source is touched, so nothing here can drift from the real path by being
/// edited independently of it.
/// </para>
/// <para>
/// <b>Where the raster split comes from, and why it is out of band.</b> Four of the five stages are
/// disjoint calls. The fifth is not: <c>PerformPaint</c> builds the display list and then replays it,
/// with no public seam between. Calling <c>CreateDisplayList</c> first would make the split visible
/// but would also add a whole paint walk to the render being timed, which is exactly the
/// measurement error the profile exists to avoid. So the paint walk is timed *after* the outer
/// window closes, on the fragment tree the run already produced, and raster is
/// <c>PerformPaint − CreateDisplayList</c>. That subtraction is sound because
/// <c>CreateDisplayList</c> is a pure function of the fragment tree: it re-walks a tree it does not
/// mutate, and <c>PerformPaint</c> calls it with the same viewport. It is reported as a derived
/// figure rather than a measured one, and <see cref="StageRow.Derived"/> says which rows are which.
/// </para>
/// <para>
/// <b>The residual is reported rather than hidden.</b> Attributed time is the sum of the measured
/// stages; the outer timer covers the whole of what <c>RenderToImageCore</c> does, including the
/// bitmap allocation, the canvas-background resolve, and container construction and disposal.
/// Whatever is left over is a row named <c>(unattributed)</c>. A profile that reached 100% by
/// defining the total as the sum of its parts would pass its own exit gate by construction.
/// </para>
/// <para>
/// <b>The parse+cascade breakdown is the one place this profile times inside the engine.</b> Phase 2
/// §9 of the multithreading roadmap made "which half of <c>parse+cascade</c> dominates" the largest
/// unattributed question in the document, and it is unanswerable from the outside: the HTML parse,
/// the stylesheet parse, the cascade and the box-fixup passes are four private calls in a row and
/// none of them is a pure function of the source, so the out-of-band trick that produces the raster
/// split cannot be reused. <see cref="RenderStageTrace"/> carries the timers instead, on the real
/// path and off by default; the reason that is not the drift risk P0-a rejects, and what a reader
/// has to check in its place, are written up there. The sub-rows are printed against the measured
/// <c>parse+cascade</c> figure with their own residual, so an untimed call inside the stage shows up
/// as a gap rather than being divided among its neighbours.
/// </para>
/// </remarks>
internal static class StageProfile
{
    /// <summary>Stages, in pipeline order. The strings are the profile's vocabulary.</summary>
    internal static class Stages
    {
        public const string ParseCascade = "parse+cascade";
        public const string Layout = "layout";
        public const string PaintWalk = "paint (display list)";
        public const string Raster = "raster";
        public const string Unattributed = "(unattributed)";
    }

    /// <summary>One stage's contribution on one page.</summary>
    /// <param name="Stage">Stage name from <see cref="Stages"/>.</param>
    /// <param name="Milliseconds">Median wall time across the measured iterations.</param>
    /// <param name="Share">Fraction of the page's end-to-end median, in [0, 1].</param>
    /// <param name="Derived">
    /// True when the figure is a difference of two measurements rather than a timer of its own —
    /// see the class remarks on the raster split.
    /// </param>
    internal sealed record StageRow(string Stage, double Milliseconds, double Share, bool Derived);

    /// <summary>
    /// One sub-stage of <c>parse+cascade</c>. <paramref name="ShareOfStage"/> is taken against the
    /// stage rather than the render, because the question the breakdown answers — which half of the
    /// stage dominates — is a question about the stage.
    /// </summary>
    internal sealed record SubStageRow(
        string SubStage, double Milliseconds, double ShareOfStage, double ShareOfRender);

    /// <summary>Every stage of one page, plus the end-to-end total the shares are taken against.</summary>
    internal sealed record PageProfile(
        string Page,
        string Loads,
        int SourceLength,
        double TotalMilliseconds,
        double AttributedShare,
        IReadOnlyList<StageRow> Rows,
        IReadOnlyList<SubStageRow> ParseCascadeBreakdown);

    /// <summary>
    /// Runs the profile over the whole corpus and writes it to stdout, and to
    /// <paramref name="jsonPath"/> when one is given.
    /// </summary>
    /// <param name="iterations">Measured iterations per page. The reported figure is the median.</param>
    /// <param name="warmup">
    /// Unmeasured iterations before the first measured one. Non-optional in practice: the first
    /// render in a process pays JIT, static-constructor, and font-cache costs that belong to none of
    /// the stages and would land almost entirely in <c>parse+cascade</c> simply for going first.
    /// </param>
    /// <param name="jsonPath">Optional path for the machine-readable copy.</param>
    public static int Run(int iterations, int warmup, string? jsonPath)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Need at least one measured iteration.");

        var profiles = new List<PageProfile>();
        foreach (var page in Corpus.Pages)
            profiles.Add(ProfilePage(page, iterations, warmup));

        WriteMarkdown(profiles, iterations, warmup);

        if (!string.IsNullOrEmpty(jsonPath))
        {
            WriteJson(profiles, iterations, warmup, jsonPath);
            Console.WriteLine();
            Console.WriteLine($"Wrote {jsonPath}");
        }

        // The exit gate is a property of the profile, so the command that produces the profile is
        // what checks it. Exit code 1 on a miss makes it usable from CI without a second parser.
        var worst = double.MaxValue;
        var worstPage = "";
        foreach (var profile in profiles)
        {
            if (profile.AttributedShare < worst)
            {
                worst = profile.AttributedShare;
                worstPage = profile.Page;
            }
        }

        Console.WriteLine();
        if (worst >= 0.90)
        {
            Console.WriteLine($"P0-a exit gate MET: every page attributes >=90% of wall time to named "
                + $"stages (worst: {worstPage} at {worst * 100:F1}%).");
            return 0;
        }

        Console.WriteLine($"P0-a exit gate MISSED: {worstPage} attributes only {worst * 100:F1}% "
            + "of wall time to named stages (>=90% required).");
        return 1;
    }

    private static PageProfile ProfilePage(Corpus.Page page, int iterations, int warmup)
    {
        // On for the warm-up too: the sub-stage timers are four Stopwatch.GetTimestamp pairs per
        // render, but profiling the measured iterations under a different code path from the ones
        // that warmed it is a method error whatever the size of the difference.
        RenderStageTrace.Enabled = true;
        try
        {
            for (var i = 0; i < warmup; i++)
                MeasureOnce(page);

            var totals = new double[iterations];
            var parse = new double[iterations];
            var layout = new double[iterations];
            var paintTotal = new double[iterations];
            var paintWalk = new double[iterations];
            var subStages = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var name in SubStageOrder)
                subStages[name] = new double[iterations];

            for (var i = 0; i < iterations; i++)
            {
                var sample = MeasureOnce(page);
                totals[i] = sample.Total;
                parse[i] = sample.ParseCascade;
                layout[i] = sample.Layout;
                paintTotal[i] = sample.PaintTotal;
                paintWalk[i] = sample.PaintWalk;
                foreach (var name in SubStageOrder)
                    subStages[name][i] = sample.SubStages.TryGetValue(name, out var ms) ? ms : 0;
            }

            return Summarize(page, totals, parse, layout, paintTotal, paintWalk, subStages);
        }
        finally
        {
            RenderStageTrace.Enabled = false;
        }
    }

    /// <summary>Sub-stages in pipeline order; the profile prints them in this order.</summary>
    private static readonly string[] SubStageOrder =
    [
        RenderStageTrace.SubStages.HtmlParse,
        RenderStageTrace.SubStages.CssParse,
        RenderStageTrace.SubStages.CascadeResolve,
        RenderStageTrace.SubStages.CascadeProject,
        RenderStageTrace.SubStages.BoxFixups,
    ];

    private static PageProfile Summarize(
        Corpus.Page page,
        double[] totals,
        double[] parse,
        double[] layout,
        double[] paintTotal,
        double[] paintWalk,
        Dictionary<string, double[]> subStages)
    {
        var total = Median(totals);
        var parseMs = Median(parse);
        var layoutMs = Median(layout);
        var paintTotalMs = Median(paintTotal);
        var paintWalkMs = Median(paintWalk);

        // The paint walk is measured on a second call, so on a page where it is a rounding error it
        // can land marginally above the PerformPaint median and drive raster negative. Clamping to
        // the interval it must lie in keeps the arithmetic consistent without inventing a number:
        // the pair still sums to the measured PerformPaint total either way.
        paintWalkMs = Math.Clamp(paintWalkMs, 0, paintTotalMs);
        var rasterMs = paintTotalMs - paintWalkMs;

        var attributed = parseMs + layoutMs + paintTotalMs;
        var unattributed = Math.Max(0, total - attributed);

        var rows = new List<StageRow>
        {
            new(Stages.ParseCascade, parseMs, parseMs / total, Derived: false),
            new(Stages.Layout, layoutMs, layoutMs / total, Derived: false),
            new(Stages.PaintWalk, paintWalkMs, paintWalkMs / total, Derived: false),
            new(Stages.Raster, rasterMs, rasterMs / total, Derived: true),
            new(Stages.Unattributed, unattributed, unattributed / total, Derived: true),
        };

        var breakdown = new List<SubStageRow>();
        var subTotal = 0.0;
        foreach (var name in SubStageOrder)
        {
            var ms = Median(subStages[name]);
            subTotal += ms;
            breakdown.Add(new SubStageRow(name, ms, ms / parseMs, ms / total));
        }

        // Same rule as the top-level table: what the timers did not cover is a row, not a rounding
        // adjustment spread over the ones that did. A future call added inside the stage and left
        // untimed shows up here instead of silently inflating the cascade.
        var subResidual = Math.Max(0, parseMs - subTotal);
        breakdown.Add(new SubStageRow("(untimed)", subResidual, subResidual / parseMs, subResidual / total));

        return new PageProfile(
            page.Name, page.Loads, page.SourceLength, total, attributed / total, rows, breakdown);
    }

    private readonly record struct Sample(
        double Total,
        double ParseCascade,
        double Layout,
        double PaintTotal,
        double PaintWalk,
        IReadOnlyDictionary<string, double> SubStages);

    /// <summary>
    /// One render, timed stage by stage. Mirrors <c>HtmlRender.RenderToImageCore</c> — the entry
    /// <c>RenderToImageWithStyleSet</c> resolves to, and the one the WPT runner and the CLI use.
    /// </summary>
    private static Sample MeasureOnce(Corpus.Page page)
    {
        const int width = Corpus.ViewportWidth;
        const int height = Corpus.ViewportHeight;
        var clip = new RectangleF(0, 0, width, height);

        RenderStageTrace.Reset();
        var outer = Stopwatch.StartNew();

        var bitmap = new BBitmap(width, height);
        using var container = new HtmlContainer();
        container.Location = new PointF(0, 0);
        container.MaxSize = new SizeF(width, height);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;

        var parseWatch = Stopwatch.StartNew();
        container.SetHtmlWithStyleSet(page.Html, null, null);
        parseWatch.Stop();

        bitmap.Clear(BColor.White);

        var layoutWatch = Stopwatch.StartNew();
        container.PerformLayout(bitmap, clip);
        layoutWatch.Stop();

        var paintWatch = Stopwatch.StartNew();
        container.PerformPaint(bitmap, clip);
        paintWatch.Stop();

        bitmap.Dispose();
        outer.Stop();

        // Out of band, on the fragment tree the run above produced — see the class remarks.
        var walkWatch = Stopwatch.StartNew();
        container.CreateDisplayList();
        walkWatch.Stop();

        return new Sample(
            Ms(outer), Ms(parseWatch), Ms(layoutWatch), Ms(paintWatch), Ms(walkWatch),
            RenderStageTrace.Totals());
    }

    private static double Ms(Stopwatch watch) => watch.Elapsed.TotalMilliseconds;

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        var mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2;
    }

    private static void WriteMarkdown(IReadOnlyList<PageProfile> profiles, int iterations, int warmup)
    {
        Console.WriteLine("# Headless render stage profile");
        Console.WriteLine();
        Console.WriteLine($"- Viewport: {Corpus.ViewportWidth}x{Corpus.ViewportHeight}");
        Console.WriteLine($"- Iterations: {iterations} measured, {warmup} warm-up; figures are medians");
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        // The raster share is now a function of a setting, so the setting has to appear next to it:
        // the same corpus profiled at one raster thread and at four reports different raster
        // figures, and a reader comparing two publications cannot tell which they are holding.
        Console.WriteLine(
            $"- Raster threads: {BRasterParallelism.MaxDegreeOfParallelism} " +
            $"(item #4; `BROILER_RASTER_THREADS`, default one per core)");
        Console.WriteLine(
            $"- Raster tiles: {TileParallelReplay.MaxDegreeOfParallelism} " +
            $"(item #5; `BROILER_RASTER_TILES`, default one per core)");
        Console.WriteLine($"- GC: {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}, "
            + $"{(System.Runtime.GCSettings.LatencyMode)} latency mode");
        Console.WriteLine();

        foreach (var profile in profiles)
        {
            Console.WriteLine($"## {profile.Page} — {profile.Loads}");
            Console.WriteLine();
            Console.WriteLine($"{profile.SourceLength:N0} chars of source; "
                + $"{profile.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms end to end.");
            Console.WriteLine();
            Console.WriteLine("| Stage | ms | share |");
            Console.WriteLine("|---|---:|---:|");
            foreach (var row in profile.Rows)
            {
                var name = row.Derived ? row.Stage + " *" : row.Stage;
                Console.WriteLine($"| {name} | {row.Milliseconds.ToString("F2", CultureInfo.InvariantCulture)} "
                    + $"| {(row.Share * 100).ToString("F1", CultureInfo.InvariantCulture)}% |");
            }

            Console.WriteLine();
            Console.WriteLine($"Attributed to named stages: "
                + $"**{(profile.AttributedShare * 100).ToString("F1", CultureInfo.InvariantCulture)}%**");
            Console.WriteLine();
            Console.WriteLine("Inside `parse+cascade`:");
            Console.WriteLine();
            Console.WriteLine("| Sub-stage | ms | of stage | of render |");
            Console.WriteLine("|---|---:|---:|---:|");
            foreach (var row in profile.ParseCascadeBreakdown)
            {
                Console.WriteLine($"| {row.SubStage} "
                    + $"| {row.Milliseconds.ToString("F2", CultureInfo.InvariantCulture)} "
                    + $"| {(row.ShareOfStage * 100).ToString("F1", CultureInfo.InvariantCulture)}% "
                    + $"| {(row.ShareOfRender * 100).ToString("F1", CultureInfo.InvariantCulture)}% |");
            }

            Console.WriteLine();
        }

        Console.WriteLine("`*` derived by subtraction rather than timed directly; see `StageProfile`.");
    }

    private static void WriteJson(
        IReadOnlyList<PageProfile> profiles, int iterations, int warmup, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var document = new
        {
            viewport = new { width = Corpus.ViewportWidth, height = Corpus.ViewportHeight },
            iterations,
            warmup,
            runtime = Environment.Version.ToString(),
            processorCount = Environment.ProcessorCount,
            serverGc = System.Runtime.GCSettings.IsServerGC,
            pages = profiles,
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
