using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Multithreading item #8: what issuing a document's image loads concurrently is worth, whether any
/// setting changes a pixel, and — when a page does not move — which of the reasons the walk declined
/// it for.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this harness needs a page of its own.</b> The five <see cref="Corpus"/> pages contain no
/// images at all, by construction: each is built to load one stage, and an <c>&lt;img&gt;</c> on the
/// text page would have folded decode into the text row. So there is nothing in the corpus this item
/// can be measured on, and adding an image to a corpus page would change every number the published
/// stage profile quotes. <see cref="DecodeScaling"/> set the precedent — items #6 and #7 are measured
/// on fixtures that harness builds — and this follows it, one level up: the fixture here is a
/// document plus the files it references, because the thing being measured is what a *document's*
/// worth of images costs.
/// </para>
/// <para>
/// <b>The files are real files on disk, and that is the point.</b> A <c>data:</c> URI would decode
/// through <c>SetFromInlineData</c>, which is a different call path from
/// <c>SetImageFromFile</c>/<c>LoadImageFromFile</c> — and the second is the one the roadmap's §6 says
/// is inline and serial on every headless entry point. Measuring the first would measure a path no
/// page with real images takes.
/// </para>
/// <para>
/// <b>The layout stage is timed, not just the render.</b> Image loads run from
/// <c>MeasureWordsSize</c>, which is inside <c>PerformLayout</c> — so that is the stage the item
/// moves, and it is reported next to the whole render. An end-to-end table alone would divide the
/// effect by however much of the render is raster before reporting it.
/// </para>
/// <para>
/// <b>Both budgets are swept, because unlike bands and tiles these two compound.</b> A tile view runs
/// its bands inline, so a render spends one or the other; a concurrent image load decodes with items
/// #6/#7's band parallelism inside it, so <em>N</em> loads at <em>N</em> bands is <em>N²</em>
/// runnable threads on <em>N</em> cores. The last column is the full prefetch budget with the
/// within-image budget divided by the loads in flight — the correction that oversubscription obviously
/// calls for, and which this harness measured at nothing. It is kept as a column rather than deleted
/// along with the seam it was meant to justify, because a null result is only worth anything while it
/// can be re-run; the speedup column deliberately excludes it, since it is a variant of the full
/// budget rather than more of it.
/// </para>
/// </remarks>
internal static class ImagePrefetchScaling
{
    /// <summary>Images the fixture document references. Enough to fill the budget several times over.</summary>
    private const int ImageCount = 12;

    /// <summary>
    /// Pixels per side of each fixture image. Large enough that one decode is tens of milliseconds —
    /// a page of thumbnails would measure the scheduler rather than the decoder.
    /// </summary>
    private const int ImageSize = 768;

    /// <summary>
    /// Builds the fixture, renders it at every prefetch setting, and reports medians, speedups, what
    /// the walk decided, and whether the image matched the sequential render. Non-zero exit code if
    /// any setting drew different pixels.
    /// </summary>
    public static int Run(int iterations, int warmup)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Need at least one iteration.");

        var fixture = Fixture.Create();
        try
        {
            return Run(fixture, iterations, warmup);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static int Run(Fixture fixture, int iterations, int warmup)
    {
        var settings = Settings();
        var configuredPrefetch = ImagePrefetch.MaxDegreeOfParallelism;
        var configuredDecode = ImageDecodeParallelism.MaxDegreeOfParallelism;
        var mismatches = new List<string>();

        Console.WriteLine("# Image prefetch scaling (multithreading item #8)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"- Viewport: {Corpus.ViewportWidth}x{Corpus.ViewportHeight}");
        Console.WriteLine($"- Fixture: {ImageCount} images at {ImageSize}x{ImageSize}, "
            + $"{fixture.EncodedBytes:N0} encoded bytes on disk, half PNG and half JPEG");
        Console.WriteLine($"- {iterations} measured iterations, {warmup} warm-up; figures are medians");
        Console.WriteLine($"- Columns: {string.Join(", ", Array.ConvertAll(settings, s => s.Label))}");
        Console.WriteLine($"- Within-image decode budget (items #6/#7): {Environment.ProcessorCount}, "
            + "except the divided column");
        Console.WriteLine($"- Minimum images to overlap: {ImagePrefetch.MinimumImagesToOverlap}");
        Console.WriteLine();

        try
        {
            var reference = RenderPixels(fixture, settings[0]);
            foreach (var setting in settings)
            {
                if (setting == settings[0])
                    continue;
                if (!SameBytes(reference, RenderPixels(fixture, setting)))
                    mismatches.Add(setting.Label);
            }

            var (layout, total) = Measure(fixture, settings, iterations, warmup);

            Report("Layout stage (`PerformLayout`, which is where an image load runs)", settings, layout);
            Report("End to end, the same renders", settings, total);

            Console.WriteLine("What the walk decided, at the full prefetch budget:");
            Console.WriteLine();
            var decisions = CountDecisions(fixture);
            Console.WriteLine("| documents walked | loads issued | skipped: below minimum | skipped: host loads async |");
            Console.WriteLine("|---:|---:|---:|---:|");
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {decisions.DocumentsWalked:N0} | {decisions.LoadsIssued:N0} | "
                + $"{decisions.DocumentsBelowMinimum:N0} | {decisions.DocumentsNotSynchronous:N0} |"));
            Console.WriteLine();
        }
        finally
        {
            ImagePrefetch.MaxDegreeOfParallelism = configuredPrefetch;
            ImageDecodeParallelism.MaxDegreeOfParallelism = configuredDecode;
        }

        if (mismatches.Count == 0)
        {
            Console.WriteLine("Every setting rendered pixel-identically to the sequential render.");
            return 0;
        }

        Console.WriteLine($"**{mismatches.Count} setting(s) rendered different pixels: {string.Join(", ", mismatches)}**");
        return 1;
    }

    private static void Report(string title, Setting[] settings, double[] medians)
    {
        Console.WriteLine($"{title}:");
        Console.WriteLine();
        Console.WriteLine("| " + string.Join(" | ", Array.ConvertAll(settings, s => s.Label + " ms")) + " | speedup |");
        Console.WriteLine("|" + string.Concat(Array.ConvertAll(settings, _ => "---:|")) + "---:|");

        var cells = new string[settings.Length];
        for (var i = 0; i < settings.Length; i++)
            cells[i] = medians[i].ToString("F1", CultureInfo.InvariantCulture);

        // Against the widest undivided column, not the last one: the divided column is a different
        // configuration of the same budget, so quoting it as the speedup would report the
        // pessimization as this item's result.
        var widest = Array.FindLastIndex(settings, s => !s.DivideDecodeBudget);
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| {string.Join(" | ", cells)} | {medians[0] / medians[widest]:F2}x |"));
        Console.WriteLine();
    }

    /// <summary>
    /// One column of the tables: how many loads run at once, and whether the within-image decode
    /// budget is divided across them.
    /// </summary>
    private sealed record Setting(string Label, int ConcurrentLoads, bool DivideDecodeBudget);

    /// <summary>
    /// 1, 2, 4 and the core count with the decode budget left alone, then the core count again with
    /// it divided.
    /// </summary>
    /// <remarks>
    /// <b>The divided configuration is a column, not a separate measurement, and that correction is
    /// the reason it is spelled this way.</b> Measured in its own block it came out 1.10x faster than
    /// the undivided one on the first run of this harness and 1.17x <em>slower</em> on the second —
    /// the sign of the effect changed, because a block of nine renders sits under a different stretch
    /// of this host's throughput drift than the interleaved sweep it was being compared against. The
    /// remedy is the one <see cref="DecodeScaling"/> already documents for the same host: put every
    /// configuration in one round-robin so they all sit under the same drift. A configuration
    /// measured outside the interleave is not comparable with the ones inside it, however carefully
    /// its own medians are taken.
    /// </remarks>
    private static Setting[] Settings()
    {
        var loads = new SortedSet<int> { 1, 2, 4, Environment.ProcessorCount };
        var settings = new List<Setting>();
        foreach (var count in loads)
            settings.Add(new Setting($"{count} load", count, DivideDecodeBudget: false));

        settings.Add(new Setting($"{Environment.ProcessorCount} load, decode divided",
            Environment.ProcessorCount, DivideDecodeBudget: true));
        return [.. settings];
    }

    /// <summary>
    /// Applies one column's two budgets.
    /// </summary>
    /// <remarks>
    /// <b>The within-image budget is set for the whole render rather than scoped to the prefetch, and
    /// on this fixture those are the same thing.</b> Every image the document names is claimed by the
    /// walk and joined before layout measures anything, and nothing else in a render decodes — so the
    /// only decodes that happen are the prefetch's, and a render-wide setting reaches exactly them.
    /// Scoping it properly would need a seam in <c>ImagePrefetch</c> for layout to narrow a budget it
    /// does not reference, which is machinery this column exists to say is not worth having.
    /// </remarks>
    private static void ApplySetting(Setting setting)
    {
        ImagePrefetch.MaxDegreeOfParallelism = setting.ConcurrentLoads;
        ImageDecodeParallelism.MaxDegreeOfParallelism = setting.DivideDecodeBudget
            ? Math.Max(1, Environment.ProcessorCount / Math.Max(1, setting.ConcurrentLoads))
            : Environment.ProcessorCount;
    }

    private static (double[] Layout, double[] Total) Measure(
        Fixture fixture, Setting[] settings, int iterations, int warmup)
    {
        foreach (var setting in settings)
        {
            for (var i = 0; i < warmup; i++)
                RenderOnce(fixture, setting);
        }

        var layout = new double[settings.Length][];
        var total = new double[settings.Length][];
        for (var i = 0; i < settings.Length; i++)
        {
            layout[i] = new double[iterations];
            total[i] = new double[iterations];
        }

        // Interleaved within each iteration, for the reason DecodeScaling gives: this host's
        // throughput drifts enough over a run to swamp the effect if a setting is measured in a block.
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var i = 0; i < settings.Length; i++)
            {
                var sample = RenderOnce(fixture, settings[i]);
                layout[i][iteration] = sample.Layout;
                total[i][iteration] = sample.Total;
            }
        }

        var layoutMedians = new double[settings.Length];
        var totalMedians = new double[settings.Length];
        for (var i = 0; i < settings.Length; i++)
        {
            layoutMedians[i] = Median(layout[i]);
            totalMedians[i] = Median(total[i]);
        }

        return (layoutMedians, totalMedians);
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static (long DocumentsWalked, long LoadsIssued, long DocumentsBelowMinimum, long DocumentsNotSynchronous)
        CountDecisions(Fixture fixture)
    {
        ImagePrefetch.ResetDiagnostics();
        ImagePrefetch.CollectDiagnostics = true;
        try
        {
            RenderOnce(fixture, new Setting("diagnostics", Environment.ProcessorCount, DivideDecodeBudget: false));
            return ImagePrefetch.Diagnostics;
        }
        finally
        {
            ImagePrefetch.CollectDiagnostics = false;
        }
    }

    /// <summary>
    /// One render, with the layout stage timed inside it. Mirrors <c>HtmlRender.RenderToImageCore</c>
    /// the way <see cref="TileScaling"/> does, so the stage boundary is the same public call the
    /// profile attributes rather than an instrumented copy.
    /// </summary>
    private static (double Total, double Layout) RenderOnce(Fixture fixture, Setting setting)
    {
        ApplySetting(setting);

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
        container.SetHtmlWithStyleSet(fixture.Html, null, fixture.BaseUrl);
        bitmap.Clear(BColor.White);

        var layout = Stopwatch.StartNew();
        container.PerformLayout(bitmap, clip);
        layout.Stop();

        container.PerformPaint(bitmap, clip);
        bitmap.Dispose();
        outer.Stop();

        return (outer.Elapsed.TotalMilliseconds, layout.Elapsed.TotalMilliseconds);
    }

    private static byte[] RenderPixels(Fixture fixture, Setting setting)
    {
        ApplySetting(setting);
        using var bitmap = HtmlRender.RenderToImageWithStyleSet(
            fixture.Html,
            Corpus.ViewportWidth,
            Corpus.ViewportHeight,
            backgroundColor: BColor.White,
            baseUrl: fixture.BaseUrl);
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

    /// <summary>
    /// A directory of encoded images and the document that references them. Deleted on dispose; the
    /// files are what make the render take the <c>LoadImageFromFile</c> path this item is about.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory;

        private Fixture(string directory, string html, string baseUrl, long encodedBytes)
        {
            _directory = directory;
            Html = html;
            BaseUrl = baseUrl;
            EncodedBytes = encodedBytes;
        }

        public string Html { get; }

        /// <summary>Base for the document's relative <c>src</c>s — the fixture's own directory.</summary>
        public string BaseUrl { get; }

        public long EncodedBytes { get; }

        public static Fixture Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "broiler-image-prefetch-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);

            long encoded = 0;
            var names = new string[ImageCount];
            for (var i = 0; i < ImageCount; i++)
            {
                // Half PNG, half JPEG: their decoders have different serial shares (item #6 measured
                // 2.08–2.61x on JPEG against 1.22–1.29x on PNG), so a fixture of one of them would
                // report a speedup that belongs to that format rather than to a page.
                var png = i % 2 == 0;
                names[i] = png ? $"i{i}.png" : $"i{i}.jpg";

                // Distinct content per image, so nothing upstream can collapse two loads into one
                // and report a speedup that is really a cache hit.
                using var source = BuildImage(ImageSize, i);
                var bytes = png
                    ? source.Encode(ImageEncodeFormat.Png, 100)
                    : source.Encode(ImageEncodeFormat.Jpeg, 85);
                File.WriteAllBytes(Path.Combine(directory, names[i]), bytes);
                encoded += bytes.Length;
            }

            var css = new StringBuilder()
                .Append("body{margin:0;font:13px/1.4 sans-serif}")
                .Append(".t{width:150px;height:120px;display:inline-block;margin:2px}")
                .Append(".bg{width:180px;height:100px;display:inline-block;margin:2px;")
                .Append("background-repeat:no-repeat;background-size:cover}")
                .ToString();

            var body = new StringBuilder();
            for (var i = 0; i < ImageCount; i++)
            {
                // Two thirds as replaced content and one third as a background layer, so both of the
                // walk's claim sites are exercised by the page it is measured on.
                if (i % 3 == 2)
                    body.Append("<div class=\"bg\" style=\"background-image:url(").Append(names[i]).Append(")\"></div>");
                else
                    body.Append("<img class=\"t\" src=\"").Append(names[i]).Append("\" alt=\"\">");
            }

            var html = "<!DOCTYPE html><html><head><style>" + css + "</style></head><body>"
                + body + "</body></html>";

            var baseUrl = new Uri(Path.Combine(directory, "page.html")).AbsoluteUri;
            return new Fixture(directory, html, baseUrl, encoded);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a failed measurement.
            }
        }

        /// <summary>
        /// A deterministic image whose content depends on <paramref name="seed"/>. Same shape as
        /// <see cref="DecodeScaling"/>'s gradient, offset per image so no two files are identical.
        /// </summary>
        private static BBitmap BuildImage(int size, int seed)
        {
            var bitmap = new BBitmap(size, size);
            var offset = seed * 37;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    bitmap.SetPixel(x, y, new BColor(
                        (byte)((x + offset) * 255 / size),
                        (byte)((y + offset) * 255 / size),
                        (byte)((x + y + offset) * 255 / (2 * size)),
                        255));
                }
            }

            return bitmap;
        }
    }
}
