using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using Broiler.Graphics;
using Broiler.HTML.Image;
using Broiler.Layout.Diagnostics;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// The same independence census as <see cref="LayoutComposition"/>, run over real documents instead
/// of the corpus — because on the corpus the answer is a property of the generator.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the corpus cannot answer this.</b> Item #13's second shape is bounded above by the share of
/// a tree that sits under an independent subtree, and on the corpus that share reads 94.6–99.6% on
/// three pages and 0.0% on the other two. Both halves are tautologies: <c>paint</c> <em>is</em> 1 400
/// <c>position:absolute</c> divs, <c>mixed</c> <em>is</em> a 70x10 table and <c>boxes</c> <em>is</em>
/// nested flex and grid, while <c>text</c> and <c>rules</c> contain none of those constructs by
/// construction. The corpus was built to load one render stage each (P0-a), not to be representative
/// of tree shape, and a ceiling read off it would be a ceiling for documents written to exercise the
/// thing being measured.
/// </para>
/// <para>
/// <b>Why WPT documents.</b> Phase 4 §4 established that the WPT corpus is the population this
/// repository actually renders at scale, and measured its documents at 1 018 bytes at the median
/// against a corpus of 20–212 KB. Whatever item #13 is worth on the path that dominates this
/// repository's wall clock is a question about <em>these</em> trees. They are also not chosen by
/// anyone with this measurement in mind, which is the property the corpus lacks.
/// </para>
/// <para>
/// <b>No clock at all.</b> Every number here is a count — boxes, roots, depth — so there is nothing
/// for this host's drift to move, and the run needs no interleaving, no warm-up and no median. Phase
/// 4 §3 arrived at that as a general rule after a timing harness in this directory nearly published a
/// false positive; this is the case where it applies completely.
/// </para>
/// <para>
/// <b>What it does not claim.</b> These are conformance tests, not web pages: they are small and
/// single-purpose by design, and a directory of <c>css-flexbox</c> tests would report a flex share
/// that says more about the directory than about documents. The distribution is therefore published
/// per directory as well as pooled, so a reader can see whether one directory carries a result.
/// </para>
/// </remarks>
internal static class LayoutIndependenceCensus
{
    private const int Width = Corpus.ViewportWidth;
    private const int Height = Corpus.ViewportHeight;

    private readonly record struct Doc(
        string Directory,
        string Name,
        int Bytes,
        long TreeBoxes,
        long Depth,
        long Independent,
        long AbsposRoots,
        long TableCells,
        long FlexGridItems);

    public static int Run(string wptDir, int limit)
    {
        if (!Directory.Exists(wptDir))
        {
            Console.WriteLine($"No such directory: {wptDir}");
            return 2;
        }

        var files = Directory
            .EnumerateFiles(wptDir, "*.html", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (limit > 0 && files.Count > limit)
            files = files.Take(limit).ToList();

        if (files.Count == 0)
        {
            Console.WriteLine($"No .html documents under {wptDir}.");
            return 2;
        }

        var docs = new List<Doc>(files.Count);
        var skipped = 0;
        foreach (var file in files)
        {
            try
            {
                docs.Add(Census(wptDir, file));
            }
            catch (Exception)
            {
                // A document this engine cannot render is not a census result. Counted and reported
                // rather than dropped silently — a run that quietly skipped half its input would
                // report a distribution of whatever was left.
                skipped++;
            }
        }

        WriteMarkdown(wptDir, docs, skipped);
        return 0;
    }

    private static Doc Census(string root, string file)
    {
        var html = File.ReadAllText(file);
        var clip = new RectangleF(0, 0, Width, Height);
        var bitmap = new BBitmap(Width, Height);
        using var container = new HtmlContainer();
        container.Location = new PointF(0, 0);
        container.AvoidAsyncImagesLoading = true;
        container.AvoidImagesLateLoading = true;
        container.MaxSize = new SizeF(Width, Height);

        container.SetHtmlWithStyleSet(html, null, null);
        bitmap.Clear(BColor.White);

        LayoutWorkTrace.Reset();
        LayoutWorkTrace.Enabled = true;
        try
        {
            container.PerformLayout(bitmap, clip);
        }
        finally
        {
            LayoutWorkTrace.Enabled = false;
        }

        var counts = LayoutWorkTrace.Counts();
        bitmap.Dispose();

        var relative = Path.GetRelativePath(root, file);
        var directory = Path.GetDirectoryName(relative);
        return new Doc(
            string.IsNullOrEmpty(directory) ? "." : directory.Replace('\\', '/'),
            Path.GetFileName(file),
            html.Length,
            Get(counts, LayoutWorkTrace.Counters.TreeBoxes),
            Get(counts, LayoutWorkTrace.Counters.TreeDepth),
            Get(counts, LayoutWorkTrace.Counters.IndependentBoxes),
            Get(counts, LayoutWorkTrace.Counters.AbsposRoots),
            Get(counts, LayoutWorkTrace.Counters.TableCellRoots),
            Get(counts, LayoutWorkTrace.Counters.FlexGridRoots));
    }

    private static long Get(IReadOnlyDictionary<string, long> counts, string key) =>
        counts.TryGetValue(key, out var value) ? value : 0;

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
            return 0;
        var index = (int)Math.Round(p * (sorted.Count - 1), MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static void WriteMarkdown(string wptDir, IReadOnlyList<Doc> docs, int skipped)
    {
        Console.WriteLine("# How much of a real document is an independent subtree");
        Console.WriteLine();
        Console.WriteLine($"- Source: `{wptDir}`, {docs.Count} documents rendered at {Width}x{Height}" +
                          (skipped > 0 ? $", {skipped} skipped (the engine threw)" : ""));
        Console.WriteLine("- Every figure is a count. No clock, so no warm-up, no median and nothing to drift.");
        Console.WriteLine();

        if (docs.Count == 0)
            return;

        var boxes = docs.Select(d => (double)d.TreeBoxes).OrderBy(v => v).ToList();
        var depth = docs.Select(d => (double)d.Depth).OrderBy(v => v).ToList();
        var bytes = docs.Select(d => (double)d.Bytes).OrderBy(v => v).ToList();
        var shares = docs
            .Select(d => d.TreeBoxes > 0 ? 100.0 * d.Independent / d.TreeBoxes : 0)
            .OrderBy(v => v)
            .ToList();

        Console.WriteLine("## The distribution");
        Console.WriteLine();
        Console.WriteLine("| quantity | min | median | p90 | max |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        Console.WriteLine($"| document bytes | {F(bytes[0])} | {F(Percentile(bytes, 0.5))} | {F(Percentile(bytes, 0.9))} | {F(bytes[^1])} |");
        Console.WriteLine($"| boxes in the tree | {F(boxes[0])} | {F(Percentile(boxes, 0.5))} | {F(Percentile(boxes, 0.9))} | {F(boxes[^1])} |");
        Console.WriteLine($"| tree depth | {F(depth[0])} | {F(Percentile(depth, 0.5))} | {F(Percentile(depth, 0.9))} | {F(depth[^1])} |");
        Console.WriteLine($"| independent share % | {F(shares[0])} | {F(Percentile(shares, 0.5))} | {F(Percentile(shares, 0.9))} | {F(shares[^1])} |");
        Console.WriteLine();

        var none = docs.Count(d => d.Independent == 0);
        var totalBoxes = docs.Sum(d => d.TreeBoxes);
        var totalIndependent = docs.Sum(d => d.Independent);
        Console.WriteLine($"**{none} of {docs.Count} documents ({F(100.0 * none / docs.Count)}%) contain no independent subtree at all**, ");
        Console.WriteLine($"and pooled over every box in every document the independent share is ");
        Console.WriteLine($"**{F(totalBoxes > 0 ? 100.0 * totalIndependent / totalBoxes : 0)}%** ({totalIndependent} of {totalBoxes}).");
        Console.WriteLine();

        Console.WriteLine("## Per directory");
        Console.WriteLine();
        Console.WriteLine("Published separately so a reader can see whether one directory carries the pooled");
        Console.WriteLine("figure — a directory of flexbox tests would report a flex share that says more about");
        Console.WriteLine("the directory than about documents.");
        Console.WriteLine();
        Console.WriteLine("| directory | docs | median boxes | median depth | independent share (pooled) | abspos | cells | flex/grid |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var group in docs.GroupBy(d => d.Directory).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var groupBoxes = group.Select(d => (double)d.TreeBoxes).OrderBy(v => v).ToList();
            var groupDepth = group.Select(d => (double)d.Depth).OrderBy(v => v).ToList();
            var sumBoxes = group.Sum(d => d.TreeBoxes);
            var sumIndependent = group.Sum(d => d.Independent);
            Console.WriteLine(
                $"| {group.Key} | {group.Count()} | {F(Percentile(groupBoxes, 0.5))} | {F(Percentile(groupDepth, 0.5))} | " +
                $"{F(sumBoxes > 0 ? 100.0 * sumIndependent / sumBoxes : 0)}% | " +
                $"{group.Sum(d => d.AbsposRoots)} | {group.Sum(d => d.TableCells)} | {group.Sum(d => d.FlexGridItems)} |");
        }
        Console.WriteLine();

        Console.WriteLine("## The ten largest trees");
        Console.WriteLine();
        Console.WriteLine("The tail is where a subtree split would have anything to divide, so it is published");
        Console.WriteLine("rather than summarised away.");
        Console.WriteLine();
        Console.WriteLine("| document | bytes | boxes | depth | independent | share |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");
        foreach (var d in docs.OrderByDescending(d => d.TreeBoxes).Take(10))
        {
            Console.WriteLine(
                $"| {d.Directory}/{d.Name} | {d.Bytes} | {d.TreeBoxes} | {d.Depth} | {d.Independent} | " +
                $"{F(d.TreeBoxes > 0 ? 100.0 * d.Independent / d.TreeBoxes : 0)}% |");
        }
        Console.WriteLine();
    }
}
