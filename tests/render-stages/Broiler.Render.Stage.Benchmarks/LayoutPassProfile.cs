using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.Diagnostics;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// How many times one <c>PerformLayout</c> lays the whole box tree out, and what the extra passes
/// cost — the precondition for the <c>Broiler.Layout</c> roadmap's step 1, built before the step is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this has to run before step 1 is written.</b> The roadmap states step 1 — "stop laying out
/// the whole tree twice" — as the highest-value sequential item in Phase 4, "worth more than steps 3
/// and 4 combined", and it names the site exactly: <c>HtmlContainerInt.PerformLayout</c> runs
/// <c>Root.PerformLayout</c> a second time whenever <c>MaxSize.Width &lt;= 0.1</c>, so that the first
/// pass can discover the shrink-to-fit width the second lays out against. That reading of the source
/// is correct. What no measurement in this repository had ever established is the question that
/// decides the item's size: <em>which hosts reach the branch.</em> Phase 3 §7's lesson, and the five
/// items before it whose stated blocker was not the operative one, is that the cheapest hour in an
/// item is the one that checks its premise.
/// </para>
/// <para>
/// <b>Three host shapes, because "a render" is not one thing.</b> The branch is on
/// <c>MaxSize.Width</c>, which is a property of the <em>caller</em>, not of the page — so a profile
/// that renders every page the one way the stage profile does could not see the branch at all, in
/// either direction. The three shapes here are the three that exist in this repository:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>viewport</b> — <c>MaxSize = (1280, 1024)</c>. What <c>HtmlRender.RenderToImage</c>, and
///     through it the WPT runner, the CLI capture, <c>Broiler.Browser.Core</c> and every benchmark in
///     this project, do. A fixed viewport width.
///   </description></item>
///   <item><description>
///     <b>autosize</b> — <c>MaxSize = (0, 0)</c>. What <c>HtmlRendererUtils.Layout</c> sets when a
///     host asks the document to size itself, which is the embedding-control path.
///   </description></item>
///   <item><description>
///     <b>measure</b> — <c>HtmlRendererUtils.MeasureHtmlByRestrictions</c>'s shape: lay out
///     unrestricted to learn the width, then lay out again against it. It calls <c>PerformLayout</c>
///     up to three times, and the first of those calls is itself on the shrink-to-fit path, so the
///     passes multiply rather than add.
///   </description></item>
/// </list>
/// <para>
/// <b>The count comes from the engine, not from arithmetic.</b> <see cref="LayoutPassCounter"/> is
/// incremented immediately before each <c>Root.PerformLayout</c>, so a pass is counted where it
/// happens. Inferring the count from <c>MaxSize</c> here would just restate this file's own
/// assumption about the branch and would be worthless as a check on it.
/// </para>
/// <para>
/// <b>Timing is reported next to the count but the count is the result.</b> A layout stage of 50 ms
/// is one pass of 50 or two of 25, and no timing can separate those; that is the whole reason this
/// harness counts. The milliseconds are here so that a shape whose pass count doubles can be checked
/// against a stage time that also roughly doubles — if it does not, the second pass is cheaper than
/// the first and the roadmap's "worth more than steps 3 and 4 combined" needs re-reading.
/// </para>
/// <para>
/// <b>Against a <c>Broiler.HTML</c> without the counter call the pass column reads zero</b>, the same
/// way <see cref="RenderStageTrace"/>'s sub-stage rows do against a tree that predates its scopes.
/// The run says so rather than reporting zeros as measurements.
/// </para>
/// </remarks>
internal static class LayoutPassProfile
{
    private const int Width = Corpus.ViewportWidth;
    private const int Height = Corpus.ViewportHeight;

    /// <summary>The three caller shapes, in the order the markdown reports them.</summary>
    private enum Shape
    {
        /// <summary>Fixed viewport — every headless path in this repository.</summary>
        Viewport,

        /// <summary>Auto-size — <c>HtmlRendererUtils.Layout(autoSize: true)</c>.</summary>
        AutoSize,

        /// <summary>Measure-by-restrictions — the sizing helper's up-to-three calls.</summary>
        Measure,
    }

    private readonly record struct Sample(int Calls, int Passes, double LayoutMs, double TotalMs);

    private readonly record struct Row(string Page, Shape Shape, int Calls, int Passes, double LayoutMs, double TotalMs);

    public static int Run(int iterations, int warmup)
    {
        var pages = Corpus.Pages;
        var shapes = new[] { Shape.Viewport, Shape.AutoSize, Shape.Measure };
        var rows = new List<Row>();

        foreach (var page in pages)
        {
            // Warm every shape before measuring any, then measure the three *interleaved* —
            // iteration by iteration rather than shape by shape.
            //
            // This is not a style preference. Phase 2 §7 of the multithreading roadmap records a
            // null result that was nearly a false positive for exactly this reason: this host's
            // throughput drifts by tens of percent over tens of seconds, so a configuration
            // measured in its own contiguous block sits in its own slice of the drift and its
            // median is not comparable with another block's. Measured shape-by-shape, this page
            // set reported `text` viewport at 136.8 ms in one run and 83.8 ms in the next with no
            // code change between them — a 1.6x swing that would have been read as a finding.
            // Interleaving puts every shape under the same drift, which is the only condition
            // under which their medians can be subtracted.
            foreach (var shape in shapes)
                for (var i = 0; i < warmup; i++)
                    MeasureOnce(page, shape);

            var samples = shapes.ToDictionary(s => s, _ => new List<Sample>(iterations));
            for (var i = 0; i < iterations; i++)
                foreach (var shape in shapes)
                    samples[shape].Add(MeasureOnce(page, shape));

            foreach (var shape in shapes)
            {
                var taken = samples[shape];

                // Counts are structural — identical every iteration — so the first sample's count is
                // the count. Asserting that rather than assuming it: a count that varies between
                // iterations of the same shape would mean the branch depends on state carried
                // between renders, which is a finding in itself and not something to median away.
                var calls = taken[0].Calls;
                var passes = taken[0].Passes;
                foreach (var s in taken)
                {
                    if (s.Calls != calls || s.Passes != passes)
                        throw new InvalidOperationException(
                            $"{page.Name}/{shape}: pass count varies between iterations " +
                            $"({calls}/{passes} then {s.Calls}/{s.Passes}) — the branch is stateful.");
                }

                rows.Add(new Row(
                    page.Name, shape, calls, passes,
                    Median(taken.Select(s => s.LayoutMs).ToArray()),
                    Median(taken.Select(s => s.TotalMs).ToArray())));
            }
        }

        rows = pages
            .SelectMany(p => shapes.Select(s => rows.First(r => r.Page == p.Name && r.Shape == s)))
            .ToList();

        WriteMarkdown(rows, iterations, warmup);
        return 0;
    }

    /// <summary>
    /// One render under one caller shape, with the pass counter armed for the layout call(s) only.
    /// </summary>
    private static Sample MeasureOnce(Corpus.Page page, Shape shape)
    {
        var clip = new RectangleF(0, 0, Width, Height);
        var bitmap = new BBitmap(Width, Height);
        using var container = new HtmlContainer();
        container.Location = new PointF(0, 0);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;

        // The sizing mode is a property of the host, known before it parses anything — every real
        // caller sets MaxSize on the container and then gives it a document. Setting it afterwards
        // instead measures a different first layout and is not comparable with the stage profile.
        container.MaxSize = shape == Shape.Viewport ? new SizeF(Width, Height) : new SizeF(0, 0);

        var outer = Stopwatch.StartNew();
        container.SetHtmlWithStyleSet(page.Html, null, null);
        bitmap.Clear(BColor.White);

        LayoutPassCounter.Reset();
        LayoutPassCounter.Enabled = true;
        var layoutWatch = Stopwatch.StartNew();
        try
        {
            switch (shape)
            {
                case Shape.Viewport:
                    container.PerformLayout(bitmap, clip);
                    break;

                case Shape.AutoSize:
                    // HtmlRendererUtils.Layout(autoSize: true) with no min/max: one call, and the
                    // engine's own shrink-to-fit branch inside it.
                    container.PerformLayout(bitmap, clip);
                    break;

                case Shape.Measure:
                    // HtmlRendererUtils.MeasureHtmlByRestrictions, reproduced against the public
                    // surface: lay out unrestricted, then re-lay against the width that produced.
                    // Bounding by the viewport width is what a real caller passes as maxSize.
                    container.PerformLayout(bitmap, clip);
                    if (container.ActualSize.Width > Width)
                    {
                        container.MaxSize = new SizeF(Width, 0);
                        container.PerformLayout(bitmap, clip);
                    }
                    var finalWidth = Math.Max(Math.Min(Width, (int)container.ActualSize.Width), 0);
                    if (finalWidth > container.ActualSize.Width)
                    {
                        container.MaxSize = new SizeF(finalWidth, 0);
                        container.PerformLayout(bitmap, clip);
                    }
                    break;
            }
        }
        finally
        {
            layoutWatch.Stop();
            LayoutPassCounter.Enabled = false;
        }

        outer.Stop();
        var sample = new Sample(
            LayoutPassCounter.Calls, LayoutPassCounter.Passes,
            layoutWatch.Elapsed.TotalMilliseconds, outer.Elapsed.TotalMilliseconds);

        bitmap.Dispose();
        return sample;
    }

    private static double Median(double[] values)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        var mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2;
    }

    private static string Name(Shape shape) => shape switch
    {
        Shape.Viewport => "viewport",
        Shape.AutoSize => "autosize",
        Shape.Measure => "measure",
        _ => shape.ToString(),
    };

    private static void WriteMarkdown(IReadOnlyList<Row> rows, int iterations, int warmup)
    {
        var instrumented = rows.Any(r => r.Passes > 0);

        Console.WriteLine("# Layout passes per render");
        Console.WriteLine();
        Console.WriteLine($"- Viewport: {Width}x{Height}");
        Console.WriteLine($"- Iterations: {iterations} measured, {warmup} warm-up; times are medians, counts are structural");
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine();

        if (!instrumented)
        {
            Console.WriteLine("> **The pass counter read zero on every row, so this run measured nothing.**");
            Console.WriteLine("> `LayoutPassCounter.Record` is called from `HtmlContainerInt.PerformLayout` in the");
            Console.WriteLine("> `Broiler.HTML` submodule; a tree that predates that call leaves the counter silent.");
            Console.WriteLine("> Apply the patch and re-run rather than reading the times below as a comparison.");
            Console.WriteLine();
        }

        Console.WriteLine("`calls` counts `PerformLayout` invocations; `passes` counts full-tree");
        Console.WriteLine("`Root.PerformLayout` walks. They differ when the engine's shrink-to-fit branch fires.");
        Console.WriteLine();
        Console.WriteLine("| page | shape | calls | passes | layout ms | render ms |");
        Console.WriteLine("|---|---|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            Console.WriteLine(
                $"| {r.Page} | {Name(r.Shape)} | {r.Calls} | {r.Passes} | " +
                $"{r.LayoutMs.ToString("F2", CultureInfo.InvariantCulture)} | " +
                $"{r.TotalMs.ToString("F2", CultureInfo.InvariantCulture)} |");
        }
        Console.WriteLine();

        var viewport = rows.Where(r => r.Shape == Shape.Viewport).ToList();
        var autosize = rows.Where(r => r.Shape == Shape.AutoSize).ToList();
        if (viewport.Count > 0 && viewport.All(r => r.Passes == 1))
        {
            Console.WriteLine("**Every fixed-viewport row lays the tree out once.** The shrink-to-fit branch is not");
            Console.WriteLine("reached by any path this repository measures — the WPT runner, the CLI capture, the");
            Console.WriteLine("browser and every benchmark here set a viewport width.");
            Console.WriteLine();
        }

        if (autosize.Count > 0 && autosize.All(r => r.Passes == 2))
        {
            Console.WriteLine("**Every auto-size row lays it out twice**, from a single `PerformLayout` call — which is");
            Console.WriteLine("the branch the roadmap's step 1 names, and the hosts that reach it are the embedding");
            Console.WriteLine("controls rather than the headless renderers.");
            Console.WriteLine();
        }
    }
}
