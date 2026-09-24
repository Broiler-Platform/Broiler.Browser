
namespace Broiler.Cli.Tests;

/// <summary>
/// Covers the CLI's batch machinery (multithreading roadmap item #20): how a batch is sized,
/// how output paths are derived, and the property the exit gate rests on — that a batch's
/// transcript and results do not depend on how many threads ran it.
/// </summary>
public sealed class BatchRunnerTests
{
    private static IReadOnlyList<BatchItem> Items(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new BatchItem($"input-{i:D2}", $"/out/input-{i:D2}.md"))
            .ToList();

    [Fact(Timeout = 600000)]
    public void Every_Item_Runs_Exactly_Once_Across_The_Batch()
    {
        var items = Items(40);
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();

        var result = BatchRunner.RunInProcess(items, degreeOfParallelism: 8, (item, _, _) =>
        {
            seen.Add(item.Input);
            return 0;
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            items.Select(i => i.Input).OrderBy(s => s, StringComparer.Ordinal),
            seen.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact(Timeout = 600000)]
    public void Outcomes_Are_In_Input_Order_Even_When_Items_Finish_Out_Of_Order()
    {
        // The reason a batch can be parallel at all: the transcript is replayed from an
        // index-keyed array, so a slow first item does not reorder the report.
        var items = Items(16);

        var result = BatchRunner.RunInProcess(items, degreeOfParallelism: 8, (item, standardOutput, _) =>
        {
            int ordinal = int.Parse(item.Input["input-".Length..]);
            Thread.Sleep((16 - ordinal) * 2);
            standardOutput.Write(item.Input);
            return 0;
        });

        Assert.Equal(
            items.Select(i => i.Input).ToList(),
            result.Outcomes.Select(o => o.StandardOutput).ToList());
    }

    [Fact(Timeout = 600000)]
    public void A_Failed_Item_Fails_The_Batch_But_Does_Not_Stop_It()
    {
        var items = Items(6);

        var result = BatchRunner.RunInProcess(items, degreeOfParallelism: 4, (item, _, standardError) =>
        {
            if (item.Input.EndsWith("03", StringComparison.Ordinal))
            {
                standardError.Write("boom");
                return 1;
            }

            return 0;
        });

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(6, result.Outcomes.Count);
        Assert.Equal(1, result.Outcomes.Count(o => o.ExitCode != 0));
    }

    [Fact(Timeout = 600000)]
    public void A_Throwing_Item_Is_Reported_Rather_Than_Taking_The_Batch_Down()
    {
        var items = Items(4);

        var result = BatchRunner.RunInProcess(items, degreeOfParallelism: 4, (item, _, _) =>
            item.Input.EndsWith("02", StringComparison.Ordinal)
                ? throw new InvalidOperationException("codec exploded")
                : 0);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("codec exploded", result.Outcomes[2].StandardError);
        Assert.All(new[] { 0, 1, 3 }, i => Assert.Equal(0, result.Outcomes[i].ExitCode));
    }

    [Fact(Timeout = 600000)]
    public void An_Empty_Batch_Succeeds_Without_Running_Anything()
    {
        var result = BatchRunner.RunInProcess([], degreeOfParallelism: 8, (_, _, _) =>
            throw new InvalidOperationException("should not run"));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Outcomes);
    }

    [Fact(Timeout = 600000)]
    public void Degree_Never_Exceeds_The_Number_Of_Items()
    {
        Assert.Equal(3, BatchRunner.ResolveDegreeOfParallelism(requested: 16, itemCount: 3, processorCount: 8));
    }

    [Fact(Timeout = 600000)]
    public void Degree_Defaults_To_One_Per_Core()
    {
        Assert.Equal(8, BatchRunner.ResolveDegreeOfParallelism(requested: null, itemCount: 40, processorCount: 8));
    }

    [Fact(Timeout = 600000)]
    public void Degree_Is_At_Least_One()
    {
        Assert.Equal(1, BatchRunner.ResolveDegreeOfParallelism(requested: 0, itemCount: 40, processorCount: 0));
    }

    [Fact(Timeout = 600000)]
    public void Output_Paths_Are_Derived_From_The_Input_Name()
    {
        var items = BatchRunner.DeriveOutputPaths(
            ["/docs/report.rtf", "/docs/notes.docx"],
            outputDir: "/out",
            extension: "md");

        Assert.Equal(Path.Combine("/out", "report.md"), items[0].OutputPath);
        Assert.Equal(Path.Combine("/out", "notes.md"), items[1].OutputPath);
    }

    [Fact(Timeout = 600000)]
    public void Colliding_Names_Are_Disambiguated_In_Input_Order()
    {
        // Same file name from two directories must not have one result overwrite the other.
        var items = BatchRunner.DeriveOutputPaths(
            ["/a/report.rtf", "/b/report.docx", "/c/report.html"],
            outputDir: "/out",
            extension: "md");

        Assert.Equal(Path.Combine("/out", "report.md"), items[0].OutputPath);
        Assert.Equal(Path.Combine("/out", "report-2.md"), items[1].OutputPath);
        Assert.Equal(Path.Combine("/out", "report-3.md"), items[2].OutputPath);
    }

    [Theory]
    [InlineData("https://example.com/a/page.html", "page.png")]
    [InlineData("https://example.com/", "example.com.png")]
    [InlineData("https://example.com", "example.com.png")]
    public void Url_Inputs_Derive_A_Name_From_The_Last_Path_Segment(string url, string expected)
    {
        var items = BatchRunner.DeriveOutputPaths([url], outputDir: "/out", extension: "png");

        Assert.Equal(Path.Combine("/out", expected), items[0].OutputPath);
    }

    [Fact(Timeout = 600000)]
    public void A_Leading_Dot_On_The_Extension_Is_Accepted()
    {
        var items = BatchRunner.DeriveOutputPaths(["/docs/report.rtf"], outputDir: "/out", extension: ".md");

        Assert.Equal(Path.Combine("/out", "report.md"), items[0].OutputPath);
    }
}
