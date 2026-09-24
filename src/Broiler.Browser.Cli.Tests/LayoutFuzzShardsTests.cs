
namespace Broiler.Cli.Tests;

/// <summary>
/// Covers the fuzz seed partition and the shard totals protocol. The partition carries the
/// whole correctness argument for a sharded fuzz run: a case depends only on its seed, so a
/// partition that covers the sequential seed set exactly, once each, examines the same
/// documents and produces the same failures.
/// </summary>
public sealed class LayoutFuzzShardsTests
{
    [Theory]
    [InlineData(200, 4)]
    [InlineData(1000, 7)]
    [InlineData(101, 10)]
    [InlineData(5, 5)]
    [InlineData(1, 4)]
    public void The_Shards_Cover_The_Sequential_Seed_Set_Exactly_Once(int count, int shardCount)
    {
        const int baseSeed = 4242;
        var shards = LayoutFuzzShards.Partition(count, baseSeed, shardCount);

        var shardSeeds = shards
            .SelectMany(shard => Enumerable.Range(shard.BaseSeed, shard.Count))
            .ToList();

        Assert.Equal(Enumerable.Range(baseSeed, count), shardSeeds);
    }

    [Fact(Timeout = 600000)]
    public void Shards_Are_Contiguous_And_Balanced_To_Within_One_Case()
    {
        var shards = LayoutFuzzShards.Partition(count: 101, baseSeed: 0, shardCount: 4);

        Assert.Equal(4, shards.Count);
        Assert.Equal(1, shards.Max(s => s.Count) - shards.Min(s => s.Count));
        for (int i = 1; i < shards.Count; i++)
            Assert.Equal(shards[i - 1].BaseSeed + shards[i - 1].Count, shards[i].BaseSeed);
    }

    [Fact(Timeout = 600000)]
    public void A_Zero_Case_Run_Has_No_Shards()
    {
        Assert.Empty(LayoutFuzzShards.Partition(count: 0, baseSeed: 1, shardCount: 4));
    }

    [Fact(Timeout = 600000)]
    public void A_Small_Run_Is_Not_Split_Across_Processes()
    {
        // Below the per-shard minimum the process startup costs more than the cases do.
        Assert.Equal(1, LayoutFuzzShards.ResolveShardCount(requested: 8, caseCount: 20, processorCount: 8));
    }

    [Fact(Timeout = 600000)]
    public void A_Large_Run_Uses_The_Requested_Shard_Count()
    {
        Assert.Equal(8, LayoutFuzzShards.ResolveShardCount(requested: 8, caseCount: 1000, processorCount: 4));
    }

    [Fact(Timeout = 600000)]
    public void Shard_Count_Defaults_To_One_Per_Core()
    {
        Assert.Equal(4, LayoutFuzzShards.ResolveShardCount(requested: null, caseCount: 1000, processorCount: 4));
    }

    [Fact(Timeout = 600000)]
    public void Shard_Count_Is_Capped_So_No_Shard_Is_Trivially_Small()
    {
        // 60 cases over 16 cores would be 3 cases per process; the cap keeps it at 2 shards.
        Assert.Equal(2, LayoutFuzzShards.ResolveShardCount(requested: 16, caseCount: 60, processorCount: 16));
    }

    [Fact(Timeout = 600000)]
    public void Totals_Round_Trip_Through_The_Marker_Line()
    {
        var totals = new LayoutFuzzTotals(Cases: 250, Failures: 7, Crashes: 2);

        Assert.True(LayoutFuzzShards.TryParseTotals(LayoutFuzzShards.FormatTotals(totals), out var parsed));
        Assert.Equal(totals, parsed);
    }

    [Fact(Timeout = 600000)]
    public void Ordinary_Transcript_Lines_Are_Not_Mistaken_For_Totals()
    {
        Assert.False(LayoutFuzzShards.TryParseTotals("Fuzz complete: 200 cases, 56 failure(s).", out _));
        Assert.False(LayoutFuzzShards.TryParseTotals("  [FAIL] seed 4242: 3 violation(s)", out _));
    }

    [Fact(Timeout = 600000)]
    public void Shard_Totals_Sum_To_The_Whole_Run()
    {
        var transcripts = new[]
        {
            $"  [FAIL] seed 1: 2 violation(s)\n{LayoutFuzzShards.FormatTotals(new LayoutFuzzTotals(50, 18, 0))}\n",
            LayoutFuzzShards.FormatTotals(new LayoutFuzzTotals(50, 12, 1)) + "\n",
            LayoutFuzzShards.FormatTotals(new LayoutFuzzTotals(50, 14, 0)) + "\n",
            LayoutFuzzShards.FormatTotals(new LayoutFuzzTotals(50, 12, 3)) + "\n",
        };

        Assert.Equal(new LayoutFuzzTotals(200, 56, 4), LayoutFuzzShards.Aggregate(transcripts));
    }

    [Fact(Timeout = 600000)]
    public void A_Shard_That_Died_Before_Reporting_Shows_Up_As_A_Case_Shortfall()
    {
        // Silence must not read as "nothing went wrong": the missing cases are what tells the
        // parent a worker exited early, and what makes it warn rather than report a short run.
        var transcripts = new[]
        {
            LayoutFuzzShards.FormatTotals(new LayoutFuzzTotals(50, 1, 0)) + "\n",
            "Unhandled exception. System.OutOfMemoryException\n",
        };

        Assert.Equal(new LayoutFuzzTotals(50, 1, 0), LayoutFuzzShards.Aggregate(transcripts));
    }

    [Fact(Timeout = 600000)]
    public void Stripping_Totals_Leaves_The_Human_Transcript_Intact()
    {
        var transcript =
            "Layout fuzz: running 50 cases…\n" +
            "  [FAIL] seed 4242: 3 violation(s)\n" +
            LayoutFuzzShards.FormatTotals(new LayoutFuzzTotals(50, 1, 0)) + "\n";

        var stripped = LayoutFuzzShards.StripTotals(transcript);

        Assert.Contains("Layout fuzz: running 50 cases…", stripped);
        Assert.Contains("[FAIL] seed 4242", stripped);
        Assert.DoesNotContain("##fuzz-totals", stripped);
    }
}
