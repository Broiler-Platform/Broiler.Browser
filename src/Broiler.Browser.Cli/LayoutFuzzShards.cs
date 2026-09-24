using System.Globalization;

namespace Broiler.Cli;

/// <summary>A contiguous slice of the fuzz seed range, run by one worker process.</summary>
internal readonly record struct LayoutFuzzShard(int BaseSeed, int Count);

/// <summary>What a fuzz run (or one shard of one) examined and found.</summary>
internal readonly record struct LayoutFuzzTotals(int Cases, int Failures, int Crashes)
{
    internal LayoutFuzzTotals Add(LayoutFuzzTotals other) =>
        new(Cases + other.Cases, Failures + other.Failures, Crashes + other.Crashes);
}

/// <summary>
/// Splits a fuzz run's seed range across worker processes. Multithreading roadmap item #20.
/// </summary>
/// <remarks>
/// The partition is the whole correctness argument for a parallel fuzz run: a case's behaviour
/// depends only on its seed, so as long as the shards' seeds are exactly the sequential run's
/// seeds — each once — the sharded run examines the same documents and writes the same
/// per-seed failure files. That property is what <c>Partition</c> guarantees and what its tests
/// assert; nothing else about a fuzz run has to be synchronized.
/// </remarks>
internal static class LayoutFuzzShards
{
    /// <summary>
    /// Fewest cases worth handing to a separate process. Below this the process startup costs
    /// more than the cases do, so a small run stays in-process no matter what was asked for.
    /// </summary>
    internal const int MinimumCasesPerShard = 25;

    internal static int ResolveShardCount(int? requested, int caseCount, int processorCount)
    {
        if (caseCount <= 0)
            return 1;

        int shardCount = requested is { } explicitShards
            ? Math.Max(1, explicitShards)
            : Math.Max(1, processorCount);

        return Math.Clamp(shardCount, 1, Math.Max(1, caseCount / MinimumCasesPerShard));
    }

    /// <summary>
    /// Splits <paramref name="count"/> cases starting at <paramref name="baseSeed"/> into
    /// <paramref name="shardCount"/> contiguous shards. The remainder is spread over the first
    /// shards rather than dumped on the last one, so the slowest shard is never a whole extra
    /// chunk behind the others.
    /// </summary>
    internal static IReadOnlyList<LayoutFuzzShard> Partition(int count, int baseSeed, int shardCount)
    {
        if (count <= 0)
            return [];

        int shards = Math.Clamp(shardCount, 1, count);
        int chunk = count / shards;
        int remainder = count % shards;

        var partition = new List<LayoutFuzzShard>(shards);
        int seed = baseSeed;
        for (int i = 0; i < shards; i++)
        {
            int shardSize = chunk + (i < remainder ? 1 : 0);
            partition.Add(new LayoutFuzzShard(seed, shardSize));
            seed += shardSize;
        }

        return partition;
    }

    /// <summary>
    /// Prefix of the machine-readable totals line a fuzz run prints last. The parent sums these
    /// rather than reading the human summary, so rewording the transcript cannot silently break
    /// a sharded run's arithmetic.
    /// </summary>
    private const string TotalsMarker = "##fuzz-totals";

    /// <summary>
    /// Drops the machine-readable totals lines from a shard's transcript. The parent parses them
    /// from the raw text; a reader of the console should not have to see them.
    /// </summary>
    internal static string StripTotals(string transcript) =>
        string.Join(
            '\n',
            transcript
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith(TotalsMarker, StringComparison.Ordinal)));

    internal static string FormatTotals(LayoutFuzzTotals totals) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{TotalsMarker} cases={totals.Cases} failures={totals.Failures} crashes={totals.Crashes}");

    internal static bool TryParseTotals(string line, out LayoutFuzzTotals totals)
    {
        totals = default;
        var trimmed = line.Trim();
        if (!trimmed.StartsWith(TotalsMarker, StringComparison.Ordinal))
            return false;

        int cases = 0, failures = 0, crashes = 0;
        foreach (var field in trimmed[TotalsMarker.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = field.IndexOf('=');
            if (separator < 0)
                return false;

            if (!int.TryParse(field[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return false;

            switch (field[..separator])
            {
                case "cases": cases = value; break;
                case "failures": failures = value; break;
                case "crashes": crashes = value; break;
                default: return false;
            }
        }

        totals = new LayoutFuzzTotals(cases, failures, crashes);
        return true;
    }

    /// <summary>
    /// Sums the totals reported by each shard's transcript. A shard that printed no totals
    /// contributes nothing, which is what makes a died-early shard visible as a case shortfall
    /// rather than as a silently smaller run.
    /// </summary>
    internal static LayoutFuzzTotals Aggregate(IEnumerable<string> transcripts)
    {
        var total = new LayoutFuzzTotals(0, 0, 0);

        foreach (var transcript in transcripts)
        {
            foreach (var line in transcript.Split('\n'))
            {
                if (TryParseTotals(line, out var shardTotals))
                    total = total.Add(shardTotals);
            }
        }

        return total;
    }
}
