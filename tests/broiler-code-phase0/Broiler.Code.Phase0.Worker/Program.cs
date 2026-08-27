using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broiler.Code.Phase0.Worker;

/// <summary>
/// Drives the worker over the Phase 0 fixture and records the proofs the exit
/// gate asks for. Each scenario asserts one property and says plainly whether
/// it held, so the output is evidence rather than a demonstration.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Main(string[] args)
    {
        string workspace = Path.GetFullPath(GetOption(args, "--workspace")
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixture"));
        if (!Directory.Exists(workspace))
        {
            Console.Error.WriteLine($"The fixture workspace '{workspace}' does not exist.");
            return 2;
        }

        var worker = new BuildWorker(log: message => Console.Error.WriteLine($"[worker] {message}"));
        var scenarios = new List<ScenarioResult>
        {
            BrokenFixtureMapsBackToItsSnapshot(worker, workspace),
            CorrectedFixtureProducesVerifiedArtifacts(worker, workspace),
            DirtyOverlayIsWhatGetsCompiled(worker, workspace),
            DiagnosticsAreLocaleIndependent(worker, workspace),
            UnsatisfiableGlobalJsonFailsBeforeBuilding(worker, workspace),
            ProjectOutsideTheGrantIsRefused(worker, workspace),
        };

        Console.WriteLine(JsonSerializer.Serialize(
            new WorkerProofReport(1, workspace, scenarios), Options));
        return scenarios.All(scenario => scenario.Passed) ? 0 : 1;
    }

    /// <summary>
    /// The Phase 0 exit gate: one out-of-process build maps a cross-file error
    /// back to the exact document snapshot.
    /// </summary>
    private static ScenarioResult BrokenFixtureMapsBackToItsSnapshot(
        BuildWorker worker, string workspace)
    {
        BuildReport report = worker.Run(Request(workspace));
        NormalizedDiagnostic? error = report.Diagnostics
            .FirstOrDefault(d => d.Code == "CS7036" && d.Severity == DiagnosticSeverity.Error);
        NormalizedDiagnostic? warning = report.Diagnostics
            .FirstOrDefault(d => d.Code == "CS0618" && d.Severity == DiagnosticSeverity.Warning);

        string expectedDocument = "src/SampleReports.App/Reporting/ReportRunner.Broken.cs";
        string? onDiskHash = HashOf(Path.Combine(workspace, expectedDocument));

        bool passed =
            report.Outcome == BuildOutcome.Failed &&
            error is not null &&
            error.DocumentPath == expectedDocument &&
            error.StartLine == 17 &&
            error.DocumentSha256 == onDiskHash &&
            error.StartOffset is > 0 &&
            warning is not null &&
            warning.DocumentPath == "src/SampleReports.App/Reporting/LegacySummary.cs";

        return new ScenarioResult(
            "broken-fixture-maps-back-to-its-snapshot",
            "A cross-project error is reported against a workspace-relative document, a " +
            "zero-based span, and the SHA-256 of the exact bytes compiled — so the editor can " +
            "prove the diagnostic belongs to the snapshot it granted.",
            passed,
            new
            {
                report.Outcome,
                report.GrantedSnapshotSha256,
                report.Toolchain,
                DocumentHashMatchesWorkspaceCopy = error?.DocumentSha256 == onDiskHash,
                Error = error,
                Warning = warning,
                report.InputCountsByOrigin,
                DiagnosticCount = report.Diagnostics.Count,
            });
    }

    private static ScenarioResult CorrectedFixtureProducesVerifiedArtifacts(
        BuildWorker worker, string workspace)
    {
        BuildRequest request = Request(workspace) with
        {
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FixtureCorrected"] = "true",
            },
        };

        BuildReport report = worker.Run(request);
        BuildArtifact? application = report.Artifacts
            .FirstOrDefault(a => a.RelativePath.EndsWith("SampleReports.App.dll", StringComparison.Ordinal));
        BuildArtifact? library = report.Artifacts
            .FirstOrDefault(a => a.RelativePath.EndsWith("SampleReports.Core.dll", StringComparison.Ordinal));

        // The warning survives correction. "Build succeeded" must not be
        // allowed to mean "Problems pane empty".
        bool warningSurvives = report.Diagnostics.Any(d => d.Code == "CS0618");

        bool passed = report.Outcome == BuildOutcome.Succeeded &&
            application is { SizeBytes: > 0 } &&
            library is { SizeBytes: > 0 } &&
            warningSurvives &&
            !report.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        return new ScenarioResult(
            "corrected-fixture-produces-verified-artifacts",
            "With FixtureCorrected=true the same snapshot builds, and every artifact is " +
            "reported with its kind, size, and SHA-256 after a containment check.",
            passed,
            new
            {
                report.Outcome,
                report.GrantedSnapshotSha256,
                WarningSurvivesCorrection = warningSurvives,
                Application = application,
                Library = library,
                ArtifactCount = report.Artifacts.Count,
                report.InputCountsByOrigin,
                GeneratedOrRestoreDiscovered = report.Inputs
                    .Count(i => i.Origin is InputOrigin.Generated or InputOrigin.Restore),
            });
    }

    private static ScenarioResult DirtyOverlayIsWhatGetsCompiled(BuildWorker worker, string workspace)
    {
        // The on-disk file is the broken one. The overlay carries the editor's
        // unsaved fix; if the worker ignored it, the build would still fail.
        const string overlayPath = "src/SampleReports.App/Reporting/ReportRunner.Broken.cs";
        const string overlay = """
            using SampleReports.Core.Reporting;

            namespace SampleReports.App;

            internal static class ReportRunner
            {
                public static string Render(Report report)
                {
                    return ReportFormatter.Format(report, FormatOptions.Invariant);
                }
            }
            """;

        BuildRequest request = Request(workspace) with
        {
            DirtyFiles = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [overlayPath] = overlay,
            },
        };

        BuildReport report = worker.Run(request);
        InputManifestEntry? entry = report.Inputs
            .FirstOrDefault(i => i.RelativePath == overlayPath);

        bool passed = report.Outcome == BuildOutcome.Succeeded &&
            entry is { Origin: InputOrigin.DirtyOverlay } &&
            !report.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        return new ScenarioResult(
            "dirty-overlay-is-what-gets-compiled",
            "Unsaved editor content reaches the build and is recorded in the input manifest as " +
            "a dirty overlay, so a build can never compile something other than what the user " +
            "is looking at.",
            passed,
            new { report.Outcome, report.GrantedSnapshotSha256, OverlayInput = entry });
    }

    /// <summary>
    /// The evidence for "no UI parses localized console text": the same build,
    /// run under two compiler UI languages, must produce identical structured
    /// diagnostics and different message strings.
    /// </summary>
    private static ScenarioResult DiagnosticsAreLocaleIndependent(BuildWorker worker, string workspace)
    {
        BuildReport english = worker.Run(Request(workspace), uiLanguage: "en");
        BuildReport japanese = worker.Run(Request(workspace), uiLanguage: "ja");

        static string Structure(BuildReport report) => string.Join('\n', report.Diagnostics
            .Where(d => d.Origin == DiagnosticOrigin.Compiler)
            .OrderBy(d => d.Code, StringComparer.Ordinal)
            .Select(d => $"{d.Code}|{d.Severity}|{d.DocumentPath}|{d.StartLine}|{d.StartColumn}|" +
                $"{d.EndLine}|{d.EndColumn}|{d.DocumentSha256}"));

        string left = Structure(english);
        string right = Structure(japanese);

        string? englishMessage = english.Diagnostics.FirstOrDefault(d => d.Code == "CS7036")?.Message;
        string? japaneseMessage = japanese.Diagnostics.FirstOrDefault(d => d.Code == "CS7036")?.Message;

        bool localeActuallyChanged =
            englishMessage is not null && japaneseMessage is not null &&
            !string.Equals(englishMessage, japaneseMessage, StringComparison.Ordinal);
        bool passed = left.Length > 0 && string.Equals(left, right, StringComparison.Ordinal) &&
            localeActuallyChanged;

        return new ScenarioResult(
            "diagnostics-are-locale-independent",
            "Structured diagnostic identity is byte-identical across compiler UI languages while " +
            "the message text is not. Anything that keyed off the message would be a " +
            "locale-dependent bug that this repository's own German-language toolchain would hit " +
            "first.",
            passed,
            new
            {
                StructureIdentical = string.Equals(left, right, StringComparison.Ordinal),
                MessagesDiffer = localeActuallyChanged,
                EnglishMessage = englishMessage,
                JapaneseMessage = japaneseMessage,
                english.Toolchain.CompilerVersion,
            });
    }

    private static ScenarioResult UnsatisfiableGlobalJsonFailsBeforeBuilding(
        BuildWorker worker, string workspace)
    {
        BuildRequest request = Request(workspace) with
        {
            DirtyFiles = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["global.json"] = """
                    {
                      "sdk": {
                        "version": "99.0.100",
                        "rollForward": "disable"
                      }
                    }
                    """,
            },
        };

        BuildReport report = worker.Run(request);
        NormalizedDiagnostic? diagnostic = report.Diagnostics
            .FirstOrDefault(d => d.Origin == DiagnosticOrigin.Toolchain);

        bool passed = report.Outcome == BuildOutcome.ToolchainUnavailable &&
            diagnostic is { Code: "BRC0101" } &&
            report.Toolchain.GlobalJsonVersion == "99.0.100" &&
            report.Artifacts.Count == 0;

        return new ScenarioResult(
            "unsatisfiable-global-json-fails-before-building",
            "A workspace pin the worker cannot satisfy is a toolchain diagnostic raised in " +
            "preflight, not a source error and not a silent substitution of the worker's own SDK.",
            passed,
            new { report.Outcome, report.Toolchain, Diagnostic = diagnostic });
    }

    private static ScenarioResult ProjectOutsideTheGrantIsRefused(BuildWorker worker, string workspace)
    {
        BuildRequest request = Request(workspace) with
        {
            ProjectPath = "../../../../elsewhere/Escape.csproj",
        };

        BuildReport report = worker.Run(request);
        bool passed = report.Outcome == BuildOutcome.RefusedByPolicy &&
            report.Diagnostics.Any(d => d.Code == "BRC0001");

        return new ScenarioResult(
            "project-outside-the-grant-is-refused",
            "A traversal out of the granted roots is refused with an actionable capability " +
            "diagnostic before anything is evaluated.",
            passed,
            new { report.Outcome, Diagnostics = report.Diagnostics });
    }

    private static BuildRequest Request(string workspace) => new()
    {
        GrantedRoots = [workspace],
        ProjectPath = "src/SampleReports.App/SampleReports.App.csproj",
        Configuration = "Release",
        WorkspaceVersion = 1,
    };

    private static string? HashOf(string path)
    {
        if (!File.Exists(path))
            return null;
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    private static string? GetOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed record ScenarioResult(
        string Id, string Asserts, bool Passed, object Evidence);

    private sealed record WorkerProofReport(
        int SchemaVersion, string Workspace, IReadOnlyList<ScenarioResult> Scenarios);
}
