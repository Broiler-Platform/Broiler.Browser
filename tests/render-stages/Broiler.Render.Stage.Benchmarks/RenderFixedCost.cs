using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.Diagnostics;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// What a render costs before the document costs anything — the intercept of render time against
/// document size, measured through the entry point the WPT runner uses.
/// </summary>
/// <remarks>
/// <para>
/// <b>The question, and why the WPT A/B could not answer it.</b>
/// <c>wpt-sequential-wins.md</c> established that Phases 2 and 3 are worth nothing on a WPT run,
/// that the run is CPU-bound, and that a reftest costs ~2.2–2.5 CPU-s for two renders of a document
/// whose median size is 1 018 bytes. From that it inferred a per-render <em>fixed</em> cost of about
/// a second "inside the engine". **That last step was an inference, not a measurement**, and it has
/// two candidate explanations the runner's own timings cannot separate: the fixed cost could be in
/// the engine (constructing a container, building the UA cascade, erasing and rasterising a
/// 1024x768 surface for a nearly blank page) or in the harness around it (reading files, the
/// worker's JSON protocol, screenshot encoding, the pixel comparison). This harness separates them
/// by measuring the engine side alone.
/// </para>
/// <para>
/// <b>Measured through <c>HtmlRender.RenderToImageWithStyleSet</c>, deliberately.</b> That is the
/// call <c>WptTestRunner</c> makes, at the same 1024x768 the runner uses, and it is a one-shot: it
/// constructs an <c>HtmlContainer</c>, sets the HTML, lays out, paints, and disposes, every time.
/// Every one of those steps is a fixed-cost candidate, so a harness that hoisted the container out
/// of the loop — as the stage profile reasonably does — would measure away the thing being looked
/// for. The stage split below <em>does</em> use a container, and its total is reported against the
/// one-shot's so the two can be checked against each other rather than assumed equal.
/// </para>
/// <para>
/// <b>The sweep is over element count, and the fit is what is published.</b> Rendering one size
/// says nothing about what is fixed; rendering a range and fitting <c>ms = a + b*n</c> puts a number
/// on each. The intercept <c>a</c> is the per-render fixed cost this file exists to measure, and the
/// slope <c>b</c> is what a document actually costs per element — the quantity every sequential win
/// in Phases 2–3 was aimed at.
/// </para>
/// <para>
/// <b>Sizes are interleaved</b>, iteration by iteration rather than size by size, for the reason
/// <c>layout-passes.md</c> writes up at length: this host's throughput drifts by tens of percent over
/// tens of seconds, and a size measured in its own contiguous block sits in its own slice of the
/// drift. A regression fitted through points taken under different drift is a fit through noise.
/// </para>
/// </remarks>
internal static class RenderFixedCost
{
    // The viewport WptTestRunner constructs by default — this harness is about that path.
    private const int Width = 1024;
    private const int Height = 768;

    /// <summary>
    /// Element counts to sweep. Starts at zero — an empty <c>&lt;body&gt;</c> is the only point that
    /// measures the fixed cost with no document cost mixed into it at all — and reaches past the
    /// ~1 KB median of a WPT reftest into corpus territory, so the fit is not an extrapolation from
    /// four points at the bottom of the range.
    /// </summary>
    private static readonly int[] Counts = [0, 1, 2, 5, 10, 25, 50, 100, 250, 500];

    private readonly record struct Sample(double TotalMs, int Bytes);

    private readonly record struct Row(
        int Count, int Bytes, double OneShotMs,
        double ContainerCtorMs, double SetHtmlMs, double LayoutMs, double PaintMs, double BitmapMs,
        double DisposeMs);

    public static int Run(int iterations, int warmup)
    {
        var docs = Counts.ToDictionary(n => n, Document);

        // --- Pass 1: the one-shot entry, swept and interleaved -----------------------------
        foreach (var n in Counts)
            for (var i = 0; i < warmup; i++)
                RenderOneShot(docs[n]);

        var oneShot = Counts.ToDictionary(n => n, _ => new List<double>(iterations));
        for (var i = 0; i < iterations; i++)
            foreach (var n in Counts)
                oneShot[n].Add(RenderOneShot(docs[n]));

        // --- Pass 2: the same work, staged ------------------------------------------------
        // Replicates RenderToImageCore step by step so the one-shot's total can be attributed.
        // Its total is printed against the one-shot's; a gap between them is reported, not hidden.
        foreach (var n in Counts)
            for (var i = 0; i < warmup; i++)
                RenderStaged(docs[n]);

        var staged = Counts.ToDictionary(n => n, _ => new List<(double ctor, double set, double lay, double paint, double bmp, double dispose)>(iterations));
        for (var i = 0; i < iterations; i++)
            foreach (var n in Counts)
                staged[n].Add(RenderStaged(docs[n]));

        var rows = Counts.Select(n => new Row(
            n,
            Encoding.UTF8.GetByteCount(docs[n]),
            Median(oneShot[n]),
            Median(staged[n].Select(s => s.ctor)),
            Median(staged[n].Select(s => s.set)),
            Median(staged[n].Select(s => s.lay)),
            Median(staged[n].Select(s => s.paint)),
            Median(staged[n].Select(s => s.bmp)),
            Median(staged[n].Select(s => s.dispose)))).ToList();

        WriteMarkdown(rows, iterations, warmup);
        return 0;
    }

    /// <summary>
    /// A document of <paramref name="n"/> simple styled boxes. Shaped like a WPT reftest rather than
    /// like a corpus page: a small inline stylesheet and plain block boxes with a background and a
    /// little text, because the question is what a *reftest-shaped* render costs.
    /// </summary>
    private static string Document(int n)
    {
        var sb = new StringBuilder(64 + n * 48);
        sb.Append("<!DOCTYPE html><html><head><style>");
        sb.Append(".b{width:80px;height:20px;background:#3a7;margin:2px;border:1px solid #145;}");
        sb.Append("</style></head><body>");
        for (var i = 0; i < n; i++)
            sb.Append("<div class=\"b\">box ").Append(i).Append("</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>One call to the entry the WPT runner uses, timed end to end.</summary>
    private static double RenderOneShot(string html)
    {
        var watch = Stopwatch.StartNew();
        using var bitmap = HtmlRender.RenderToImageWithStyleSet(html, Width, Height, backgroundColor: BColor.White);
        watch.Stop();
        return watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// The same sequence <c>RenderToImageCore</c> performs, with a stopwatch around each step.
    /// </summary>
    /// <remarks>
    /// Disposal is timed, and that is not a detail. <c>RenderToImageCore</c> holds both the
    /// container and the bitmap in <c>using</c> blocks, so both are torn down inside the one-shot's
    /// timed region. A first version of this harness disposed after stopping the clocks and reported
    /// a staged total that fell further behind the one-shot the larger the document got — the
    /// missing work was the render tree being released, which scales with the tree.
    /// </remarks>
    private static (double ctor, double set, double lay, double paint, double bmp, double dispose) RenderStaged(string html)
    {
        var clip = new RectangleF(0, 0, Width, Height);

        var bmpWatch = Stopwatch.StartNew();
        var bitmap = new BBitmap(Width, Height);
        bitmap.Erase(BColor.White);
        bmpWatch.Stop();

        var ctorWatch = Stopwatch.StartNew();
        var container = new HtmlContainer();
        container.Location = new PointF(0, 0);
        container.MaxSize = new SizeF(Width, Height);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;
        ctorWatch.Stop();

        var setWatch = Stopwatch.StartNew();
        container.SetHtmlWithStyleSet(html, null, null);
        setWatch.Stop();

        var layWatch = Stopwatch.StartNew();
        container.PerformLayout(bitmap, clip);
        layWatch.Stop();

        var paintWatch = Stopwatch.StartNew();
        container.PerformPaint(bitmap, clip);
        paintWatch.Stop();

        var disposeWatch = Stopwatch.StartNew();
        container.Dispose();
        bitmap.Dispose();
        disposeWatch.Stop();

        return (Ms(ctorWatch), Ms(setWatch), Ms(layWatch), Ms(paintWatch), Ms(bmpWatch), Ms(disposeWatch));
    }

    private static double Ms(Stopwatch w) => w.Elapsed.TotalMilliseconds;

    private static double Median(IEnumerable<double> values)
    {
        var copy = values.ToArray();
        Array.Sort(copy);
        var mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2;
    }

    /// <summary>Ordinary least squares of <c>y = a + b*x</c>, returning (intercept, slope).</summary>
    private static (double a, double b) Fit(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        var n = xs.Count;
        double mx = xs.Average(), my = ys.Average();
        double num = 0, den = 0;
        for (var i = 0; i < n; i++)
        {
            num += (xs[i] - mx) * (ys[i] - my);
            den += (xs[i] - mx) * (xs[i] - mx);
        }
        var b = den == 0 ? 0 : num / den;
        return (my - b * mx, b);
    }

    private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    private static void WriteMarkdown(IReadOnlyList<Row> rows, int iterations, int warmup)
    {
        Console.WriteLine("# Per-render fixed cost");
        Console.WriteLine();
        Console.WriteLine($"- Entry: `HtmlRender.RenderToImageWithStyleSet` at {Width}x{Height} — the call `WptTestRunner` makes");
        Console.WriteLine($"- Iterations: {iterations} measured, {warmup} warm-up; medians, sizes interleaved");
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine();

        Console.WriteLine("| boxes | bytes | one-shot ms | ctor | set html | layout | paint | bitmap | dispose | staged total |");
        Console.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            var stagedTotal = r.ContainerCtorMs + r.SetHtmlMs + r.LayoutMs + r.PaintMs + r.BitmapMs + r.DisposeMs;
            Console.WriteLine(
                $"| {r.Count} | {r.Bytes} | **{F(r.OneShotMs)}** | {F(r.ContainerCtorMs)} | {F(r.SetHtmlMs)} | " +
                $"{F(r.LayoutMs)} | {F(r.PaintMs)} | {F(r.BitmapMs)} | {F(r.DisposeMs)} | {F(stagedTotal)} |");
        }
        Console.WriteLine();

        var xs = rows.Select(r => (double)r.Count).ToList();
        var (a, b) = Fit(xs, rows.Select(r => r.OneShotMs).ToList());
        Console.WriteLine("## Fit");
        Console.WriteLine();
        Console.WriteLine($"`one-shot ms = {F(a)} + {F(b)} * boxes`");
        Console.WriteLine();
        Console.WriteLine($"- **Per-render fixed cost (intercept): {F(a)} ms**");
        Console.WriteLine($"- Per-box cost (slope): {F(b)} ms");
        var empty = rows.First(r => r.Count == 0);
        Console.WriteLine($"- Measured directly at zero boxes: **{F(empty.OneShotMs)} ms** ({empty.Bytes} bytes)");
        Console.WriteLine();

        var emptyStaged = empty.ContainerCtorMs + empty.SetHtmlMs + empty.LayoutMs + empty.PaintMs + empty.BitmapMs + empty.DisposeMs;
        Console.WriteLine("## Where the empty render's time goes");
        Console.WriteLine();
        Console.WriteLine("| step | ms | share of staged total |");
        Console.WriteLine("|---|---:|---:|");
        foreach (var (name, ms) in new[]
                 {
                     ("container ctor", empty.ContainerCtorMs),
                     ("SetHtmlWithStyleSet", empty.SetHtmlMs),
                     ("PerformLayout", empty.LayoutMs),
                     ("PerformPaint", empty.PaintMs),
                     ("bitmap alloc + erase", empty.BitmapMs),
                     ("dispose (container + bitmap)", empty.DisposeMs),
                 })
        {
            var share = emptyStaged > 0 ? ms / emptyStaged * 100 : 0;
            Console.WriteLine($"| {name} | {F(ms)} | {share.ToString("F1", CultureInfo.InvariantCulture)}% |");
        }
        Console.WriteLine($"| **staged total** | **{F(emptyStaged)}** | |");
        Console.WriteLine($"| one-shot, same document | {F(empty.OneShotMs)} | |");
        Console.WriteLine();
        var gap = empty.OneShotMs - emptyStaged;
        Console.WriteLine($"Unattributed between the two: **{F(gap)} ms**. The staged pass replicates");
        Console.WriteLine("`RenderToImageCore` rather than calling it, so a gap is the replication's error and");
        Console.WriteLine("bounds how much of the split below can be trusted.");
    }
}
