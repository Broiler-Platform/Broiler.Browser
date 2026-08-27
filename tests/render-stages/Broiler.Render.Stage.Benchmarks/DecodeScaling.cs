using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Broiler.Graphics;
using Broiler.Media.Image;
using Broiler.Media.Image.Managed;
using BBitmap = Broiler.HTML.Image.BBitmap;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// Multithreading items #6 and #7: what band-parallel decode is worth, and whether it changes a
/// single pixel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not BenchmarkDotNet.</b> <see cref="DecodeBenchmarks"/> already times one decode
/// precisely; the question here is a different one — how the same decode scales as the thread
/// budget changes — and answering it needs several settings compared inside one process, because
/// <see cref="ImageDecodeParallelism.MaxDegreeOfParallelism"/> is a process-wide setting that a
/// BenchmarkDotNet parameter cannot vary across its isolated child runs without also varying the
/// process. Medians of a warmed stopwatch loop are the right instrument for a ratio, and the
/// ratio is what items #6 and #7 claim.
/// </para>
/// <para>
/// <b>The scaling figure and the exit gate are produced together, deliberately.</b> Each setting's
/// decoded bytes are compared against the single-threaded decode of the same fixture, so a run
/// that reports a speedup has also just proved the output identical. A speedup measured without
/// that check is not evidence of anything: the fastest possible decoder returns the wrong pixels.
/// </para>
/// </remarks>
internal static class DecodeScaling
{
    private sealed record Fixture(string Name, byte[] Bytes, int Pixels);

    /// <summary>
    /// Decodes each fixture at each thread setting and reports medians, speedups, and whether the
    /// bytes matched the sequential decode. Returns a non-zero exit code if any of them did not.
    /// </summary>
    public static int Run(int iterations, int warmup)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "Need at least one iteration.");

        var fixtures = BuildFixtures();
        var settings = ThreadSettings();
        var configured = ImageDecodeParallelism.MaxDegreeOfParallelism;
        var mismatches = new List<string>();

        Console.WriteLine("# Image decode thread scaling (multithreading items #6, #7)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: {Environment.Version}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine($"- {iterations} measured iterations, {warmup} warm-up; figures are medians");
        Console.WriteLine($"- Thread settings: {string.Join(", ", settings)}");
        Console.WriteLine();

        try
        {
            foreach (var fixture in fixtures)
            {
                ImageDecodeParallelism.MaxDegreeOfParallelism = 1;
                var reference = DecodeBytes(fixture);

                foreach (var threads in settings)
                {
                    ImageDecodeParallelism.MaxDegreeOfParallelism = threads;
                    if (!SameBytes(reference, DecodeBytes(fixture)))
                        mismatches.Add($"{fixture.Name} at {threads} threads");
                }

                var medians = Measure(fixture, settings, iterations, warmup);
                var baseline = medians[0];

                Console.WriteLine($"## {fixture.Name}");
                Console.WriteLine();
                Console.WriteLine($"{fixture.Bytes.Length:N0} encoded bytes, {fixture.Pixels:N0} pixels.");
                Console.WriteLine();
                Console.WriteLine("| Threads | ms | speedup |");
                Console.WriteLine("|---|---:|---:|");

                for (var i = 0; i < settings.Length; i++)
                {
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"| {settings[i]} | {medians[i]:F2} | {baseline / medians[i]:F2}x |"));
                }

                Console.WriteLine();
            }
        }
        finally
        {
            ImageDecodeParallelism.MaxDegreeOfParallelism = configured;
        }

        if (mismatches.Count == 0)
        {
            Console.WriteLine("Every setting decoded byte-identically to the single-threaded decoder.");
            return 0;
        }

        Console.WriteLine($"**{mismatches.Count} setting(s) decoded different pixels: {string.Join(", ", mismatches)}**");
        return 1;
    }

    /// <summary>
    /// 1, 2, 4 and the core count — the last one deduplicated, so a 4-core box reports three rows
    /// rather than four with two of them measuring the same thing.
    /// </summary>
    private static int[] ThreadSettings()
    {
        var settings = new SortedSet<int> { 1, 2, 4, Environment.ProcessorCount };
        var result = new List<int>();
        foreach (var threads in settings)
            result.Add(threads);
        return [.. result];
    }

    /// <summary>
    /// Two content shapes at 1024², matching <see cref="DecodeBenchmarks"/>: a photographic ramp
    /// (poor PNG ratio, good JPEG ratio) and flat tiles (the reverse). The serial share of a decode
    /// is a function of how much entropy-coded data it walks, so the two shapes bracket the
    /// speedup rather than giving one point and implying it is the answer.
    /// </summary>
    private static List<Fixture> BuildFixtures()
    {
        const int size = 1024;
        var fixtures = new List<Fixture>();

        foreach (var content in new[] { "gradient", "blocks" })
        {
            using var source = BuildImage(size, content);
            fixtures.Add(new Fixture($"PNG {size}x{size} {content}", source.Encode(ImageEncodeFormat.Png, 100), size * size));
            fixtures.Add(new Fixture($"JPEG {size}x{size} {content}", source.Encode(ImageEncodeFormat.Jpeg, 85), size * size));
        }

        return fixtures;
    }

    /// <summary>Same deterministic source image as <see cref="DecodeBenchmarks"/>.</summary>
    private static BBitmap BuildImage(int size, string content)
    {
        var bitmap = new BBitmap(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                BColor color;
                if (string.Equals(content, "blocks", StringComparison.Ordinal))
                {
                    var block = ((x / 16) + (y / 16)) % 5;
                    color = new BColor((byte)(40 * block), (byte)(200 - 30 * block), (byte)(60 + 40 * block), 255);
                }
                else
                {
                    color = new BColor(
                        (byte)(x * 255 / size),
                        (byte)(y * 255 / size),
                        (byte)((x + y) * 255 / (2 * size)),
                        255);
                }

                bitmap.SetPixel(x, y, color);
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Times every thread setting, round-robin, and returns each one's median.
    /// </summary>
    /// <remarks>
    /// <b>Interleaved on purpose.</b> This container's throughput drifts by tens of percent over
    /// tens of seconds — enough that the same untouched decode measured in two consecutive
    /// processes differs by more than the effect being measured. Timing setting 1, then setting 2,
    /// then setting 4 <em>within</em> each iteration puts all of them under the same drift, so the
    /// ratio between their medians survives it even where the absolute milliseconds do not. Blocked
    /// measurement — all of setting 1, then all of setting 2 — cannot distinguish a speedup from
    /// the box getting faster, and a scaling harness that cannot do that is measuring the host.
    /// </remarks>
    private static double[] Measure(Fixture fixture, int[] settings, int iterations, int warmup)
    {
        foreach (var threads in settings)
        {
            ImageDecodeParallelism.MaxDegreeOfParallelism = threads;
            for (var i = 0; i < warmup; i++)
                DecodeOnce(fixture);
        }

        var samples = new double[settings.Length][];
        for (var i = 0; i < settings.Length; i++)
            samples[i] = new double[iterations];

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            for (var i = 0; i < settings.Length; i++)
            {
                ImageDecodeParallelism.MaxDegreeOfParallelism = settings[i];
                var watch = Stopwatch.StartNew();
                DecodeOnce(fixture);
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

    private static void DecodeOnce(Fixture fixture)
    {
        using var bitmap = BBitmap.Decode(fixture.Bytes);
        GC.KeepAlive(bitmap);
    }

    private static byte[] DecodeBytes(Fixture fixture)
    {
        using var bitmap = BBitmap.Decode(fixture.Bytes);
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        var index = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                pixels[index++] = color.R;
                pixels[index++] = color.G;
                pixels[index++] = color.B;
                pixels[index++] = color.A;
            }
        }

        return pixels;
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
