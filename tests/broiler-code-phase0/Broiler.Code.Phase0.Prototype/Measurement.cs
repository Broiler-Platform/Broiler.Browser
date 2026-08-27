using System.Diagnostics;

namespace Broiler.Code.Phase0.Prototype;

/// <summary>Summary statistics for one repeated operation.</summary>
public sealed record Measurement(
    int Samples,
    double MedianMilliseconds,
    double P95Milliseconds,
    double MaxMilliseconds,
    long MedianAllocatedBytes,
    long MaxAllocatedBytes);

public sealed class Sampler
{
    private readonly List<double> _elapsed = [];
    private readonly List<long> _allocated = [];
    private long _allocatedAtStart;
    private long _startedAt;

    public void Begin()
    {
        _allocatedAtStart = GC.GetTotalAllocatedBytes(precise: true);
        _startedAt = Stopwatch.GetTimestamp();
    }

    public void End()
    {
        _elapsed.Add(Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds);
        _allocated.Add(GC.GetTotalAllocatedBytes(precise: true) - _allocatedAtStart);
    }

    public Measurement Summarize()
    {
        if (_elapsed.Count == 0)
            return new Measurement(0, 0, 0, 0, 0, 0);

        double[] elapsed = [.. _elapsed];
        long[] allocated = [.. _allocated];
        Array.Sort(elapsed);
        Array.Sort(allocated);

        return new Measurement(
            elapsed.Length,
            Percentile(elapsed, 0.50),
            Percentile(elapsed, 0.95),
            elapsed[^1],
            (long)Percentile(allocated, 0.50),
            allocated[^1]);
    }

    /// <summary>Nearest-rank percentile. Exact and order-only, so it never
    /// interpolates a value no sample actually produced.</summary>
    private static double Percentile(double[] sorted, double fraction) =>
        sorted[Rank(sorted.Length, fraction)];

    private static double Percentile(long[] sorted, double fraction) =>
        sorted[Rank(sorted.Length, fraction)];

    private static int Rank(int count, double fraction)
    {
        int index = (int)Math.Ceiling(fraction * count) - 1;
        return Math.Clamp(index, 0, count - 1);
    }
}
