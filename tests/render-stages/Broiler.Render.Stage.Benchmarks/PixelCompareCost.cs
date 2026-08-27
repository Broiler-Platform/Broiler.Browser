using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Broiler.Graphics;
using Broiler.HTML.Core.IR;
using Broiler.HTML.Image;
using Broiler.Media.Image;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// What <see cref="PixelDiffRunner.Compare"/> costs, and which half of it is the cost — the
/// before/after gate for the WPT runner's second-largest phase.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>render-fixed-cost.md</c> measured the WPT phases and found
/// <c>PixelDiffRunner.Compare</c> at <b>16–21% of a run</b>, 327–501 ms per comparison of two
/// 1024x768 bitmaps. Reading the method suggests two candidate causes, and they call for opposite
/// fixes, so guessing between them is exactly the mistake this phase has already made twice:
/// </para>
/// <list type="number">
///   <item><description>
///     <c>NormalizeForComparison</c> runs <c>BBitmap.Decode(source.Encode(Png, 100))</c> on
///     <em>both</em> inputs — a full PNG compress and decompress of 3 MB each, per comparison.
///   </description></item>
///   <item><description>
///     The comparison loop reads both bitmaps through per-pixel <c>GetPixel</c> and writes a
///     <c>SetPixel</c> to a diff bitmap for <em>every</em> pixel, matching or not — 786 432
///     iterations — and allocates that diff bitmap unconditionally, discarding it on the match path.
///   </description></item>
/// </list>
/// <para>
/// Both are measured here separately, against the whole call, so the fix is aimed at whichever is
/// actually large rather than whichever reads worst.
/// </para>
/// <para>
/// <b>The match and mismatch cases are both measured, because they are different code paths.</b>
/// A passing test discards the diff bitmap it just built; a failing one keeps it and materializes a
/// mismatch list. The WPT suites this was traced on pass ~62% and ~87% of the time respectively, so
/// the match path is the common one and the one worth being fast.
/// </para>
/// </remarks>
internal static class PixelCompareCost
{
    private const int Width = 1024;
    private const int Height = 768;

    public static int Run(int iterations, int warmup)
    {
        var a = Scene(0);
        var identical = Scene(0);
        var differing = Scene(7);

        // Sanity: the two cases really are a match and a mismatch, so the timings below are of the
        // paths they claim to be. A "mismatch" case that quietly matched would report the match
        // path twice and look like a null result.
        using (var probeMatch = PixelDiffRunner.Compare(a, identical))
        using (var probeDiff = PixelDiffRunner.Compare(a, differing))
        {
            if (!probeMatch.IsMatch)
                throw new InvalidOperationException("The 'identical' pair did not compare as a match.");
            if (probeDiff.IsMatch)
                throw new InvalidOperationException("The 'differing' pair compared as a match; the scenes are too similar.");
        }

        var rows = new List<(string Label, double Ms)>();

        Action[] cases =
        [
            () => { using var r = PixelDiffRunner.Compare(a, identical); },
            () => { using var r = PixelDiffRunner.Compare(a, differing); },
            () => { using var normalized = BBitmap.Decode(a.Encode(ImageEncodeFormat.Png, 100)); },
            () => { var e = a.Encode(ImageEncodeFormat.Png, 100); },
            () => { using var c = a.Copy(); },
        ];
        string[] labels =
        [
            "Compare — match path",
            "Compare — mismatch path",
            "NormalizeForComparison (Encode+Decode), one bitmap",
            "  of which: Encode(Png,100), one bitmap",
            "Copy(), one bitmap (what a normalize could cost)",
        ];

        for (var i = 0; i < warmup; i++)
            foreach (var c in cases)
                c();

        var samples = labels.Select(_ => new List<double>(iterations)).ToArray();
        for (var i = 0; i < iterations; i++)
            for (var c = 0; c < cases.Length; c++)
            {
                var watch = Stopwatch.StartNew();
                cases[c]();
                watch.Stop();
                samples[c].Add(watch.Elapsed.TotalMilliseconds);
            }

        for (var c = 0; c < labels.Length; c++)
            rows.Add((labels[c], Median(samples[c])));

        Console.WriteLine("# Pixel comparison cost");
        Console.WriteLine();
        Console.WriteLine($"- Bitmaps: {Width}x{Height} ({Width * Height:N0} pixels, {Width * Height * 4 / 1024 / 1024} MB each)");
        Console.WriteLine($"- Iterations: {iterations} measured, {warmup} warm-up; medians, cases interleaved");
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine();
        Console.WriteLine("| case | ms |");
        Console.WriteLine("|---|---:|");
        foreach (var (label, ms) in rows)
            Console.WriteLine($"| {label} | {ms.ToString("F2", CultureInfo.InvariantCulture)} |");
        Console.WriteLine();

        VerifyRoundTripIsIdentity();

        var match = rows[0].Ms;
        var normalizeBoth = 2 * rows[2].Ms;

        // The normalize row is measured standalone, so it stays meaningful after the round trip is
        // removed from Compare — at which point it is the cost that *was* being paid, not a share
        // of the current call. Reporting it as a percentage either way would print nonsense once
        // the denominator drops below it, which is exactly what a stale summary line does.
        Console.WriteLine(match > normalizeBoth / 2
            ? $"`Compare` normalizes **both** inputs, so normalization is ~{normalizeBoth.ToString("F1", CultureInfo.InvariantCulture)} ms " +
              $"of the {match.ToString("F1", CultureInfo.InvariantCulture)} ms match path — " +
              $"**{(normalizeBoth / match * 100).ToString("F0", CultureInfo.InvariantCulture)}%**. " +
              "The remainder is the per-pixel loop and the diff bitmap."
            : $"The match path is **{match.ToString("F2", CultureInfo.InvariantCulture)} ms**, well below the " +
              $"~{normalizeBoth.ToString("F1", CultureInfo.InvariantCulture)} ms a two-input PNG round trip costs — " +
              "so `Compare` is no longer normalizing, and what is left is the per-pixel loop. " +
              "The normalize rows stay in the table as the cost that used to be paid.");
        return 0;
    }

    /// <summary>
    /// Is the PNG round trip an identity on pixel values? If it is, it can be replaced with
    /// something cheap; if it is not, it is load-bearing and the cost has to be paid.
    /// </summary>
    /// <remarks>
    /// Checked over the cases where a PNG round trip could legitimately change pixels — graded
    /// alpha and fully-transparent-but-coloured pixels, where a codec is entitled to collapse the
    /// colour type or premultiply — and over real reference PNGs off disk, which is what the
    /// golden-image path actually compares against. Reasoning about the codec instead would be the
    /// third inference this phase has had to retract.
    /// </remarks>
    private static void VerifyRoundTripIsIdentity()
    {
        Console.WriteLine("## Is the round trip an identity?");
        Console.WriteLine();
        Console.WriteLine("| case | result |");
        Console.WriteLine("|---|---|");

        var failures = 0;

        void Check(string label, BBitmap src)
        {
            using var rt = BBitmap.Decode(src.Encode(ImageEncodeFormat.Png, 100));
            string verdict;
            if (src.Width != rt.Width || src.Height != rt.Height)
            {
                verdict = $"**DIFFERS** — {src.Width}x{src.Height} became {rt.Width}x{rt.Height}";
                failures++;
            }
            else
            {
                var diff = 0;
                for (var y = 0; y < src.Height && diff == 0; y++)
                    for (var x = 0; x < src.Width; x++)
                    {
                        var p = src.GetPixel(x, y);
                        var q = rt.GetPixel(x, y);
                        if (p.R != q.R || p.G != q.G || p.B != q.B || p.A != q.A) { diff++; break; }
                    }
                verdict = diff == 0 ? "identical" : "**DIFFERS**";
                if (diff != 0) failures++;
            }
            Console.WriteLine($"| {label} | {verdict} |");
        }

        var opaque = new BBitmap(256, 256);
        for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
                opaque.SetPixel(x, y, new BColor((byte)x, (byte)y, (byte)((x * y) & 0xFF), 255));
        Check("synthetic, opaque", opaque);

        var graded = new BBitmap(256, 256);
        for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
                graded.SetPixel(x, y, new BColor((byte)x, (byte)y, (byte)(255 - x), (byte)y));
        Check("synthetic, graded alpha", graded);

        var clear = new BBitmap(64, 64);
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                clear.SetPixel(x, y, new BColor(200, 100, 50, 0));
        Check("synthetic, transparent with non-zero RGB", clear);

        var refDir = Path.Combine("tests", "wpt", "references");
        if (Directory.Exists(refDir))
        {
            var pngs = Directory.EnumerateFiles(refDir, "*.png", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).Take(25).ToList();
            var bad = 0;
            foreach (var p in pngs)
            {
                try
                {
                    using var decoded = BBitmap.Decode(File.ReadAllBytes(p));
                    using var rt = BBitmap.Decode(decoded.Encode(ImageEncodeFormat.Png, 100));
                    if (decoded.Width != rt.Width || decoded.Height != rt.Height) { bad++; continue; }
                    for (var y = 0; y < decoded.Height; y++)
                        for (var x = 0; x < decoded.Width; x++)
                        {
                            var a1 = decoded.GetPixel(x, y);
                            var b1 = rt.GetPixel(x, y);
                            if (a1.R != b1.R || a1.G != b1.G || a1.B != b1.B || a1.A != b1.A)
                            { bad++; y = decoded.Height; break; }
                        }
                }
                catch (Exception ex) { Console.WriteLine($"| (skipped {Path.GetFileName(p)}) | {ex.GetType().Name} |"); }
            }
            failures += bad;
            Console.WriteLine($"| {pngs.Count} real WPT reference PNG(s) | {(bad == 0 ? "all identical" : $"**{bad} DIFFER**")} |");
        }
        else
        {
            Console.WriteLine($"| real reference PNGs | (none at {refDir}) |");
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "**The round trip is an identity on every case checked**, so it is an expensive no-op."
            : $"**{failures} case(s) differ — the round trip is load-bearing** and cannot simply be dropped.");
        Console.WriteLine();
    }

    /// <summary>
    /// A deterministic 1024x768 scene with enough structure that a PNG encode of it is not a
    /// degenerate run-length case. <paramref name="shift"/> perturbs it so two scenes differ.
    /// </summary>
    private static BBitmap Scene(int shift)
    {
        var bmp = new BBitmap(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var v = (byte)((x * 7 + y * 13 + shift * 29) & 0xFF);
                bmp.SetPixel(x, y, new BColor(v, (byte)(255 - v), (byte)((x + shift) & 0xFF), 255));
            }
        }
        return bmp;
    }

    private static double Median(IEnumerable<double> values)
    {
        var copy = values.ToArray();
        Array.Sort(copy);
        var mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : (copy[mid - 1] + copy[mid]) / 2;
    }
}
