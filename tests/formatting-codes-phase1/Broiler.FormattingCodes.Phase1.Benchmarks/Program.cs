using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Broiler.Documents.FormatCodes;
using Broiler.Documents.Model;

namespace Broiler.FormattingCodes.Phase1.Benchmarks;

internal static class Program
{
    private const int DefaultIterations = 5;

    private static readonly InlineStyle[] DenseStyles =
    [
        new InlineStyle { Bold = true },
        new InlineStyle { Italic = true },
        new InlineStyle { Underline = true },
        new InlineStyle { Strikethrough = true },
        new InlineStyle { FontSize = 15.5f },
        new InlineStyle { FontFamily = "Broiler Sans" },
        new InlineStyle { LinkHref = "https://example.invalid/format-code" },
    ];

    public static int Main(string[] args)
    {
        int iterations = ParseIterations(args);
        List<FixtureSpec> fixtures = JsonSerializer.Deserialize<List<FixtureSpec>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Fixture manifest is empty.");
        var projector = new FormatCodeProjector();
        var results = new List<FixtureResult>(fixtures.Count);

        foreach (FixtureSpec fixture in fixtures)
        {
            RichTextDocument document = BuildFixture(fixture);
            FormatCodeProjection projection = projector.Project(document);
            Measurement measurement = Measure(
                () => GC.KeepAlive(projector.Project(document)),
                iterations);
            results.Add(new FixtureResult(
                fixture.Id,
                document.PlainText.Length,
                document.ParagraphCount,
                document.Paragraphs.Sum(paragraph => paragraph.Runs.Count),
                projection.Text.Length,
                projection.Tokens.Count,
                projection.Diagnostics.Count,
                FormatCodeProjectionPolicy.RecommendBackgroundProjection(document),
                measurement));
        }

        var report = new BenchmarkReport(
            SchemaVersion: 1,
            GrammarVersion: 1,
            Iterations: iterations,
            Framework: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            SynchronousCharacterThreshold: FormatCodeProjectionPolicy.MaxSynchronousSourceCharacters,
            SynchronousStructuralUnitThreshold: FormatCodeProjectionPolicy.MaxSynchronousStructuralUnits,
            Fixtures: results);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
        return 0;
    }

    private static Measurement Measure(Action action, int iterations)
    {
        action();
        var elapsed = new double[iterations];
        var allocated = new long[iterations];
        for (int i = 0; i < iterations; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            action();
            elapsed[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Array.Sort(elapsed);
        Array.Sort(allocated);
        return new Measurement(elapsed[iterations / 2], allocated[iterations / 2]);
    }

    private static RichTextDocument BuildFixture(FixtureSpec fixture) => fixture.Generator switch
    {
        "plain" => RichTextDocument.FromPlainText(BuildPatternText(fixture.TargetChars)),
        "high-run-density" => BuildHighRunDensity(fixture),
        "empty-paragraphs" => RichTextDocument.FromParagraphs(
            Enumerable.Repeat(RichTextParagraph.Empty, fixture.Paragraphs)),
        "unicode" => RichTextDocument.FromPlainText(BuildUnicodeText(fixture.TargetChars)),
        _ => throw new InvalidOperationException($"Unknown generator '{fixture.Generator}'."),
    };

    private static RichTextDocument BuildHighRunDensity(FixtureSpec fixture)
    {
        int contentCharacters = fixture.TargetChars - (fixture.Paragraphs - 1);
        int baseLength = contentCharacters / fixture.Paragraphs;
        int remainder = contentCharacters % fixture.Paragraphs;
        var paragraphs = new RichTextParagraph[fixture.Paragraphs];
        for (int i = 0; i < paragraphs.Length; i++)
        {
            paragraphs[i] = RichTextParagraph.Create(
                BuildPatternText(baseLength + (i < remainder ? 1 : 0), i),
                DenseStyles[i % DenseStyles.Length]);
        }

        return RichTextDocument.FromParagraphs(paragraphs);
    }

    private static string BuildPatternText(int length, int phase = 0)
    {
        const string pattern = "The quick brown fox 0123456789. ";
        return string.Create(length, phase, static (span, start) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = pattern[(i + start) % pattern.Length];
        });
    }

    private static string BuildUnicodeText(int length)
    {
        const string pattern = "A\u0308 café Ελληνικά עברית العربية 中文 😀 [x] \\ \t \u0001 ";
        var builder = new StringBuilder(length);
        while (builder.Length + pattern.Length <= length)
            builder.Append(pattern);
        builder.Append('界', length - builder.Length);
        return builder.ToString();
    }

    private static int ParseIterations(string[] args)
    {
        if (args.Length == 0)
            return DefaultIterations;
        if (args.Length == 2 && args[0] == "--iterations" &&
            int.TryParse(args[1], out int iterations) && iterations > 0)
        {
            return iterations;
        }

        throw new ArgumentException("Usage: --iterations <positive integer>");
    }

    private sealed record FixtureSpec(
        string Id,
        string Generator,
        int TargetChars,
        int Paragraphs,
        int ExpectedRuns,
        string Purpose);

    private sealed record Measurement(double MedianMilliseconds, long MedianAllocatedBytes);

    private sealed record FixtureResult(
        string Id,
        int SourceCharacters,
        int Paragraphs,
        int Runs,
        int ProjectedCharacters,
        int Tokens,
        int Diagnostics,
        bool RecommendBackgroundProjection,
        Measurement Projection);

    private sealed record BenchmarkReport(
        int SchemaVersion,
        int GrammarVersion,
        int Iterations,
        string Framework,
        string OperatingSystem,
        string Architecture,
        int ProcessorCount,
        int SynchronousCharacterThreshold,
        int SynchronousStructuralUnitThreshold,
        IReadOnlyList<FixtureResult> Fixtures);
}
