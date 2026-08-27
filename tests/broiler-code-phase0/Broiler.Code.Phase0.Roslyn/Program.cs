using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Broiler.Code.Phase0.Roslyn;

/// <summary>
/// Measures what an optional local Roslyn semantic service costs, and whether
/// the evaluated graph it depends on can actually be delivered.
///
/// The measurement that decides the Android and browser composition is not
/// "does Roslyn run" — it does — but the sum of the Roslyn payload, the
/// metadata reference set, and the memory a live compilation retains. This
/// harness records each of those separately so a host can be judged against the
/// ones it actually has to carry.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        int iterations = ParseIterations(args);
        string fixtureRoot = Path.GetFullPath(GetOption(args, "--fixture")
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixture"));

        EvaluatedGraph graph = EvaluatedGraph.Evaluate(
            Path.Combine(fixtureRoot, "src", "SampleReports.App", "SampleReports.App.csproj"),
            "Release");

        var report = new RoslynReport(
            SchemaVersion: 1,
            Iterations: iterations,
            Framework: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            LanguageServiceCompiler: RoslynIdentity(),
            Payload: MeasurePayload(),
            Graph: DescribeGraph(graph),
            Syntax: MeasureSyntax(iterations),
            FixtureAgreement: MeasureFixtureAgreement(fixtureRoot, graph),
            Solution: MeasureSolution(graph, fileCount: 1_000, linesPerFile: 100),
            Unmeasured: UnmeasuredModes());

        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        }));

        return report.FixtureAgreement.Agrees ? 0 : 1;
    }

    private static string RoslynIdentity()
    {
        Assembly assembly = typeof(CSharpSyntaxTree).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational ?? assembly.GetName().Version?.ToString() ?? "unknown";
    }

    /// <summary>
    /// What composing the semantic service adds to a host's payload: the Roslyn
    /// assemblies themselves, before trimming.
    /// </summary>
    private static PayloadReport MeasurePayload()
    {
        var files = new List<AssemblySize>();
        foreach (string path in Directory
            .GetFiles(AppContext.BaseDirectory, "Microsoft.CodeAnalysis*.dll")
            .Concat(Directory.GetFiles(AppContext.BaseDirectory, "System.Collections.Immutable.dll"))
            .Concat(Directory.GetFiles(AppContext.BaseDirectory, "System.Reflection.Metadata.dll"))
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            files.Add(new AssemblySize(Path.GetFileName(path), new FileInfo(path).Length));
        }

        return new PayloadReport(files.Sum(file => file.Bytes), files);
    }

    private static GraphReport DescribeGraph(EvaluatedGraph graph) => new(
        graph.CompilePaths.Count,
        graph.MetadataReferencePaths.Count,
        graph.TotalReferenceBytes,
        graph.PreprocessorSymbols.Count,
        graph.LanguageVersion,
        graph.NullableContext,
        "Obtained from MSBuild's structured -getItem/-getProperty JSON. A host without a " +
        "trusted evaluation cannot construct this, and a compilation without it reports every " +
        "framework type as missing — which is why local semantic diagnostics are gated on graph " +
        "delivery and not on the Roslyn payload alone.");

    private static SyntaxReport MeasureSyntax(int iterations)
    {
        string source = LargeSource(100_000);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        // Warm up before sampling, so the first measurement does not also pay
        // for JIT.
        GC.KeepAlive(CSharpSyntaxTree.ParseText(SourceText.From(source), parseOptions));

        var sampler = new Sampler();
        SyntaxTree? tree = null;
        for (int i = 0; i < iterations; i++)
        {
            Collect();
            sampler.Begin();
            tree = CSharpSyntaxTree.ParseText(SourceText.From(source), parseOptions);
            GC.KeepAlive(tree.GetRoot());
            sampler.End();
        }

        Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        SyntaxTree held = CSharpSyntaxTree.ParseText(SourceText.From(source), parseOptions);
        SyntaxNode root = held.GetRoot();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        int nodes = root.DescendantNodesAndSelf().Count();
        GC.KeepAlive(held);

        return new SyntaxReport(
            100_000,
            Encoding.UTF8.GetByteCount(source),
            tree!.GetRoot().DescendantTokens().Count(),
            nodes,
            sampler.Summarize(),
            after - before);
    }

    /// <summary>
    /// The proof that a local semantic service fed the evaluated graph agrees
    /// with the authoritative build. Without it, "we have Roslyn on the client"
    /// is an assertion about a library rather than about diagnostics a user can
    /// trust.
    /// </summary>
    private static AgreementReport MeasureFixtureAgreement(string fixtureRoot, EvaluatedGraph graph)
    {
        // Sources come from the evaluated Compile items, not from a directory
        // scan. The scan looks equivalent and is not: it misses that
        // ReportRunner.Corrected.cs is conditioned out, and it picks up
        // obj\ assembly-info files from both projects, which collide on
        // duplicate assembly attributes and manufacture sixteen errors the
        // authoritative build never reports.
        var sources = new List<(string Path, string Text)>();
        foreach (string path in graph.CompilePaths
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            sources.Add((Path.GetRelativePath(fixtureRoot, path).Replace('\\', '/'),
                File.ReadAllText(path)));
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest)
            .WithPreprocessorSymbols(graph.PreprocessorSymbols);
        var trees = sources
            .Select(source => CSharpSyntaxTree.ParseText(
                SourceText.From(source.Text), parseOptions, source.Path))
            .ToList();

        var references = graph.MetadataReferencePaths
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        var sampler = new Sampler();
        Collect();
        sampler.Begin();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "SampleReports.App",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable));
        ImmutableArrayDiagnostics diagnostics = Collect(compilation);
        sampler.End();

        var error = diagnostics.Items.FirstOrDefault(d =>
            d.Id == "CS7036" && d.Severity == DiagnosticSeverity.Error);
        var warning = diagnostics.Items.FirstOrDefault(d => d.Id == "CS0618");

        // The worker recorded CS7036 at zero-based line 17, column 31 of
        // ReportRunner.Broken.cs. The language service has to land on the same
        // document and the same span, or the two channels are describing
        // different things.
        (string? Document, int Line, int Column) located = Locate(error);
        bool agrees =
            located.Document == "src/SampleReports.App/Reporting/ReportRunner.Broken.cs" &&
            located.Line == 17 &&
            located.Column == 31 &&
            warning is not null &&
            diagnostics.Items.Count(d => d.Severity == DiagnosticSeverity.Error) == 1;

        return new AgreementReport(
            agrees,
            sources.Count,
            references.Count,
            located.Document,
            located.Line,
            located.Column,
            error?.Id,
            warning?.Id,
            diagnostics.Items.Count(d => d.Severity == DiagnosticSeverity.Error),
            sampler.Summarize());
    }

    private static SolutionReport MeasureSolution(
        EvaluatedGraph graph, int fileCount, int linesPerFile)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var sources = new string[fileCount];
        for (int i = 0; i < fileCount; i++)
            sources[i] = LargeSource(linesPerFile, i);

        var references = graph.MetadataReferencePaths
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        Collect();
        var parseSampler = new Sampler();
        parseSampler.Begin();
        var trees = new SyntaxTree[fileCount];
        for (int i = 0; i < fileCount; i++)
        {
            trees[i] = CSharpSyntaxTree.ParseText(
                SourceText.From(sources[i]), parseOptions, $"Generated{i}.cs");
        }

        parseSampler.End();

        Collect();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        var compileSampler = new Sampler();
        compileSampler.Begin();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratedSolution", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        ImmutableArrayDiagnostics diagnostics = Collect(compilation);
        compileSampler.End();
        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(compilation);

        return new SolutionReport(
            fileCount,
            linesPerFile,
            fileCount * linesPerFile,
            diagnostics.Items.Length,
            parseSampler.Summarize(),
            compileSampler.Summarize(),
            after - before);
    }

    private static IReadOnlyList<UnmeasuredMode> UnmeasuredModes() =>
    [
        new("android-jit-untrimmed-cold-start",
            "Cold start and peak resident memory of the semantic service on an Android device.",
            "Needs a physical device or emulator running the composed host. Not available on the " +
            "recording machine, and a desktop figure is not a substitute — the JIT, the memory " +
            "ceiling, and the storage are all different."),
        new("android-trimmed-payload-delta",
            "Installed size delta of an APK that embeds the Roslyn assemblies, trimmed and untrimmed.",
            "Measurable here in principle; recorded as owed because trimming Roslyn requires a " +
            "reflection-safety review that Phase 0 has not done, and an untrimmed number quoted " +
            "as a trimmed one would be misleading."),
        new("webassembly-interpreted-cold-start",
            "Browser cold start and peak heap for the trimmed interpreted profile with Roslyn composed.",
            "Needs a browser harness driving the published profile. The published payload size is " +
            "measurable without one and is recorded separately by the payload probe script."),
        new("webassembly-aot",
            "AOT profile measurements with Roslyn composed.",
            "Deliberately not measured. Phase 0's instruction is to measure AOT only if that IDE " +
            "host mode will be claimed, and no such claim is being made."),
    ];

    private static ImmutableArrayDiagnostics Collect(CSharpCompilation compilation) =>
        new([.. compilation.GetDiagnostics()
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)]);

    private static (string?, int, int) Locate(Diagnostic? diagnostic)
    {
        if (diagnostic is null)
            return (null, -1, -1);
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        return (span.Path, span.StartLinePosition.Line, span.StartLinePosition.Character);
    }

    private static string LargeSource(int lineCount, int seed = 0)
    {
        var builder = new StringBuilder(lineCount * 48);
        builder.Append("using System;\n");
        builder.Append($"namespace Generated.Space{seed};\n");
        int line = 2;
        int id = 0;
        while (line < lineCount)
        {
            builder.Append($"public sealed class Widget{seed}_{id}\n");
            builder.Append("{\n");
            builder.Append($"    public int Limit => 0x{id:X4};\n");
            builder.Append("    public int Measure(int value)\n");
            builder.Append("    {\n");
            builder.Append("        if (value > Limit)\n");
            builder.Append("            return value * 3 - 1;\n");
            builder.Append("        return checked(value / 2);\n");
            builder.Append("    }\n");
            builder.Append("}\n");
            line += 9;
            id++;
        }

        return builder.ToString();
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static int ParseIterations(string[] args)
    {
        string? value = GetOption(args, "--iterations");
        return value is not null && int.TryParse(value, out int iterations) && iterations > 0
            ? iterations
            : 3;
    }

    private static string? GetOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private readonly record struct ImmutableArrayDiagnostics(Diagnostic[] Items);

    private sealed record AssemblySize(string Name, long Bytes);

    private sealed record PayloadReport(long TotalBytes, IReadOnlyList<AssemblySize> Assemblies);

    private sealed record GraphReport(
        int CompileItemCount,
        int MetadataReferenceCount,
        long MetadataReferenceBytes,
        int PreprocessorSymbolCount,
        string? LanguageVersion,
        string? NullableContext,
        string Note);

    private sealed record SyntaxReport(
        int Lines,
        int SourceBytes,
        int Tokens,
        int Nodes,
        Measurement Parse,
        long RetainedBytes);

    private sealed record AgreementReport(
        bool Agrees,
        int SourceFiles,
        int MetadataReferences,
        string? ErrorDocument,
        int ErrorLine,
        int ErrorColumn,
        string? ErrorId,
        string? WarningId,
        int ErrorCount,
        Measurement CompileAndDiagnose);

    private sealed record SolutionReport(
        int FileCount,
        int LinesPerFile,
        int TotalLines,
        int DiagnosticCount,
        Measurement Parse,
        Measurement CompileAndDiagnose,
        long RetainedBytes);

    private sealed record UnmeasuredMode(string Id, string What, string Why);

    private sealed record RoslynReport(
        int SchemaVersion,
        int Iterations,
        string Framework,
        string OperatingSystem,
        string Architecture,
        int ProcessorCount,
        string LanguageServiceCompiler,
        PayloadReport Payload,
        GraphReport Graph,
        SyntaxReport Syntax,
        AgreementReport FixtureAgreement,
        SolutionReport Solution,
        IReadOnlyList<UnmeasuredMode> Unmeasured);
}
