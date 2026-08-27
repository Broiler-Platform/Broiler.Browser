using System.Diagnostics;

namespace Broiler.Code.Phase0.Roslyn;

/// <summary>Summary statistics for one repeated operation.</summary>
public sealed record Measurement(
    int Samples,
    double MedianMilliseconds,
    double P95Milliseconds,
    double MaxMilliseconds,
    long MedianAllocatedBytes,
    long MaxAllocatedBytes);

/// <summary>
/// A copy of the prototype's sampler. The two harnesses are deliberately not
/// coupled: this one references Roslyn and the other must never be able to.
/// </summary>
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
            elapsed[Rank(elapsed.Length, 0.50)],
            elapsed[Rank(elapsed.Length, 0.95)],
            elapsed[^1],
            allocated[Rank(allocated.Length, 0.50)],
            allocated[^1]);
    }

    private static int Rank(int count, double fraction) =>
        Math.Clamp((int)Math.Ceiling(fraction * count) - 1, 0, count - 1);
}
