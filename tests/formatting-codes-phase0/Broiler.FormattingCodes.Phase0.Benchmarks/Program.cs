using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Broiler.Documents.Model;

namespace Broiler.FormattingCodes.Phase0.Benchmarks;

internal static class Program
{
    private const int DefaultIterations = 3;

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
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "fixtures.json");
        List<FixtureSpec> fixtures = JsonSerializer.Deserialize<List<FixtureSpec>>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The fixture manifest is empty.");

        var results = new List<FixtureResult>(fixtures.Count);
        foreach (FixtureSpec fixture in fixtures)
            results.Add(RunFixture(fixture, iterations));

        var report = new BaselineReport(
            SchemaVersion: 1,
            Iterations: iterations,
            Framework: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            Fixtures: results);

        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
        return 0;
    }

    private static FixtureResult RunFixture(FixtureSpec fixture, int iterations)
    {
        Func<RichTextDocument> build = () => BuildFixture(fixture);
        Measurement buildMeasurement = Measure(() => GC.KeepAlive(build()), iterations);
        RichTextDocument document = build();

        int actualChars = document.PlainText.Length;
        int actualRuns = document.Paragraphs.Sum(paragraph => paragraph.Runs.Count);
        if (actualChars != fixture.TargetChars ||
            document.ParagraphCount != fixture.Paragraphs ||
            actualRuns != fixture.ExpectedRuns)
        {
            throw new InvalidOperationException(
                $"Fixture '{fixture.Id}' expected {fixture.TargetChars} chars, " +
                $"{fixture.Paragraphs} paragraphs, and {fixture.ExpectedRuns} runs; " +
                $"created {actualChars}, {document.ParagraphCount}, and {actualRuns}.");
        }

        Measurement plainTextMeasurement = Measure(
            () => GC.KeepAlive(document.PlainText), iterations);
        Measurement scanMeasurement = Measure(
            () => GC.KeepAlive(ScanModel(document)), iterations);

        return new FixtureResult(
            fixture.Id,
            fixture.Generator,
            actualChars,
            document.ParagraphCount,
            actualRuns,
            buildMeasurement,
            plainTextMeasurement,
            scanMeasurement);
    }

    private static RichTextDocument BuildFixture(FixtureSpec fixture) => fixture.Generator switch
    {
        "plain" => RichTextDocument.FromPlainText(BuildPatternText(fixture.TargetChars)),
        "high-run-density" => BuildHighRunDensity(fixture),
        "empty-paragraphs" => RichTextDocument.FromParagraphs(
            Enumerable.Repeat(RichTextParagraph.Empty, fixture.Paragraphs)),
        "unicode" => RichTextDocument.FromPlainText(BuildUnicodeText(fixture.TargetChars)),
        _ => throw new InvalidOperationException(
            $"Unknown fixture generator '{fixture.Generator}'."),
    };

    private static RichTextDocument BuildHighRunDensity(FixtureSpec fixture)
    {
        int contentChars = fixture.TargetChars - (fixture.Paragraphs - 1);
        int baseLength = contentChars / fixture.Paragraphs;
        int remainder = contentChars % fixture.Paragraphs;
        var paragraphs = new RichTextParagraph[fixture.Paragraphs];

        for (int i = 0; i < paragraphs.Length; i++)
        {
            int length = baseLength + (i < remainder ? 1 : 0);
            string text = BuildPatternText(length, i);
            paragraphs[i] = RichTextParagraph.Create(
                text,
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

    private static long ScanModel(RichTextDocument document)
    {
        long checksum = document.ParagraphCount;
        foreach (RichTextParagraph paragraph in document.Paragraphs)
        {
            checksum += paragraph.Text.Length;
            foreach (char character in paragraph.Text)
                checksum += character;
            checksum += paragraph.Style.GetHashCode() & 0xffff;
            foreach (StyleRun run in paragraph.Runs)
            {
                checksum += run.Length;
                checksum += run.Style.Bold ? 3 : 0;
                checksum += run.Style.Italic ? 5 : 0;
                checksum += run.Style.Underline ? 7 : 0;
                checksum += run.Style.Strikethrough ? 11 : 0;
                checksum += run.Style.FontFamily?.Length ?? 0;
                checksum += run.Style.LinkHref?.Length ?? 0;
            }
        }

        return checksum;
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
        string Generator,
        int Characters,
        int Paragraphs,
        int Runs,
        Measurement Build,
        Measurement PlainTextMaterialization,
        Measurement ModelScan);

    private sealed record BaselineReport(
        int SchemaVersion,
        int Iterations,
        string Framework,
        string OperatingSystem,
        string Architecture,
        int ProcessorCount,
        IReadOnlyList<FixtureResult> Fixtures);
}
