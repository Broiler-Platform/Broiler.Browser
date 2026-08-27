using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Broiler.Code.Phase0.Worker;

/// <summary>
/// The trusted out-of-process build worker prototype.
///
/// MSBuild project evaluation and targets execute code, so even the local
/// worker runs in its own process: the boundary protects the IDE's reliability,
/// not the user's account from their own project. Every job materializes into a
/// disposable root outside the Broiler checkout, so no unrelated staging
/// ancestor can reach the evaluation.
/// </summary>
public sealed class BuildWorker
{
    private readonly string _jobsRoot;
    private readonly Action<string> _log;

    public BuildWorker(string? jobsRoot = null, Action<string>? log = null)
    {
        _jobsRoot = jobsRoot ?? Path.Combine(Path.GetTempPath(), "broiler-code-worker");
        _log = log ?? (_ => { });
    }

    public BuildReport Run(BuildRequest request, string? uiLanguage = null)
    {
        string buildId = Guid.NewGuid().ToString("n");
        string jobRoot = Path.Combine(_jobsRoot, buildId);
        long started = Stopwatch.GetTimestamp();
        var notes = new List<string>();

        // The job root is deliberately under the system temp directory rather
        // than inside the repository: an ancestor Directory.Build.props of the
        // checkout would otherwise take part in evaluating a user's project.
        using var snapshot = GrantedSnapshot.Materialize(request, jobRoot);
        foreach (string refusal in snapshot.Refusals)
            notes.Add(refusal);

        string projectPath = request.ProjectPath.Replace('\\', '/');
        string absoluteProject = Path.GetFullPath(Path.Combine(jobRoot, projectPath));
        if (!GrantedSnapshot.IsContained(absoluteProject, jobRoot) || !File.Exists(absoluteProject))
        {
            return Failure(
                request, buildId, snapshot, notes, started, BuildOutcome.RefusedByPolicy,
                Worker(buildId, request, "BRC0001",
                    $"The requested project '{request.ProjectPath}' is not inside the granted snapshot."));
        }

        ToolchainProbe probe = ToolchainPreflight.Probe(jobRoot);
        if (probe.Diagnostic is { } toolchainProblem)
        {
            // The report still carries what the workspace asked for. Dropping
            // it here would leave the user with "no SDK" and no way to see
            // which pin caused it.
            return Failure(
                request, buildId, snapshot, notes, started, BuildOutcome.ToolchainUnavailable,
                toolchainProblem with { BuildId = buildId, WorkspaceVersion = request.WorkspaceVersion },
                Describe(probe));
        }

        string sarifDirectory = Path.Combine(jobRoot, ".broiler-worker", "sarif");
        Directory.CreateDirectory(sarifDirectory);
        string injected = WriteErrorLogInjection(jobRoot, sarifDirectory);

        ProcessOutcome process = RunBuild(request, jobRoot, absoluteProject, injected, uiLanguage);
        _log($"build exited {process.ExitCode} in {process.Elapsed.TotalSeconds:F1}s");

        snapshot.RecordDiscoveredInputs();
        ToolchainInfo toolchain = Describe(probe);

        var diagnostics = new List<NormalizedDiagnostic>();
        string? compilerVersion = null;
        string? compilerLanguage = null;

        foreach (string sarif in Directory.GetFiles(sarifDirectory, "*.sarif")
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            var context = new SarifContext(
                jobRoot,
                projectPath,
                TargetFrameworkFromSarifName(sarif),
                request.Configuration,
                buildId,
                request.WorkspaceVersion);
            diagnostics.AddRange(SarifReader.Read(sarif, context, out string? version, out string? language));
            compilerVersion ??= version;
            compilerLanguage ??= language;
        }

        // A build can fail without producing a single compiler diagnostic — a
        // missing workload, a restore refusal, an unresolvable SDK. Reporting
        // nothing there would tell the user the build failed for no reason, so
        // the worker states plainly that the cause is outside its structured
        // channel instead of scraping the localized log for it.
        BuildOutcome outcome = process.TimedOut
            ? BuildOutcome.TimedOut
            : process.ExitCode == 0 ? BuildOutcome.Succeeded : BuildOutcome.Failed;

        if (outcome is BuildOutcome.Failed &&
            !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(Worker(buildId, request, "BRC0002",
                "The build failed without a compiler diagnostic. The cause is a toolchain, " +
                "restore, or targets failure; Phase 4's structured MSBuild logger is what " +
                "turns it into a typed diagnostic. The console log is attached verbatim and " +
                "is not parsed."));
        }

        if (outcome == BuildOutcome.TimedOut)
        {
            diagnostics.Add(Worker(buildId, request, "BRC0003",
                $"The build exceeded its {request.TimeoutSeconds}s bound and its process tree was terminated."));
        }

        List<BuildArtifact> artifacts = outcome == BuildOutcome.Succeeded
            ? ArtifactCollector.Collect(jobRoot, absoluteProject, request.Configuration)
            : [];

        return Compose(
            request, buildId, snapshot, notes, started, outcome, process, diagnostics, artifacts,
            toolchain with
            {
                CompilerVersion = compilerVersion,
                CompilerUiLanguage = compilerLanguage,
            });
    }

    private static ToolchainInfo Describe(ToolchainProbe probe) => new()
    {
        RequestedBy = probe.GlobalJsonPath is null ? "SDK default" : "global.json",
        GlobalJsonVersion = probe.GlobalJsonVersion,
        GlobalJsonRollForward = probe.GlobalJsonRollForward,
        ResolvedSdkVersion = probe.ResolvedSdkVersion,
    };

    /// <summary>
    /// Writes the props file that gives every project in the graph its own
    /// SARIF path. A command-line <c>-p:ErrorLog=...</c> cannot do this: global
    /// properties are literal strings, so <c>$(MSBuildProjectName)</c> would
    /// land on disk unexpanded and every inner build of a multi-targeting
    /// project would fight over one file.
    /// </summary>
    private static string WriteErrorLogInjection(string jobRoot, string sarifDirectory)
    {
        string path = Path.Combine(jobRoot, ".broiler-worker", "ErrorLog.props");
        string contents = $"""
            <Project>
              <PropertyGroup>
                <ErrorLog>{sarifDirectory}\$(MSBuildProjectName)__$(TargetFramework).sarif{SarifReader.VersionSuffix}</ErrorLog>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private ProcessOutcome RunBuild(
        BuildRequest request,
        string jobRoot,
        string absoluteProject,
        string injectedProps,
        string? uiLanguage)
    {
        var arguments = new List<string>
        {
            "build",
            absoluteProject,
            "-c", request.Configuration,
            "-t:" + request.Target,
            "--nologo",
            "-v:q",
            // CustomAfterMicrosoftCommonTargets is imported after the project
            // body, so the worker's ErrorLog wins over any the project set.
            $"-p:CustomAfterMicrosoftCommonTargets={injectedProps}",
        };

        if (request.Restore == RestorePolicy.Forbidden)
            arguments.Add("--no-restore");

        foreach ((string key, string value) in request.Properties)
            arguments.Add($"-p:{key}={value}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = jobRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // A deterministic environment: no inherited MSBuild state, and an
        // explicit UI language so the locale-independence proof can vary it.
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment.Remove("MSBuildLoadMicrosoftTargetsReadOnly");
        if (uiLanguage is not null)
        {
            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = uiLanguage;
            startInfo.Environment["VSLANG"] = LanguageId(uiLanguage).ToString();
        }

        var output = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };

        long started = Stopwatch.GetTimestamp();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool exited = process.WaitForExit(TimeSpan.FromSeconds(request.TimeoutSeconds));
        if (!exited)
        {
            // MSBuild starts worker nodes; killing only the launcher would
            // leave them holding the job directory open and defeat cleanup.
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(30));
        }

        string text;
        lock (output)
            text = output.ToString();

        return new ProcessOutcome(
            exited ? process.ExitCode : -1,
            !exited,
            Stopwatch.GetElapsedTime(started),
            text);
    }

    private static int LanguageId(string language) => language switch
    {
        "en" or "en-US" => 1033,
        "de" or "de-DE" => 1031,
        "ja" or "ja-JP" => 1041,
        "fr" or "fr-FR" => 1036,
        _ => 1033,
    };

    private static string? TargetFrameworkFromSarifName(string path)
    {
        // "<Project>__<TargetFramework>.sarif". A dot separator does not work:
        // both project names and target framework monikers contain dots, so
        // "SampleReports.App.net10.0" splits at the wrong place and yields "0".
        string name = Path.GetFileNameWithoutExtension(path);
        int separator = name.LastIndexOf("__", StringComparison.Ordinal);
        if (separator < 0)
            return null;
        string framework = name[(separator + 2)..];
        return framework.Length == 0 ? null : framework;
    }

    private static NormalizedDiagnostic Worker(
        string buildId, BuildRequest request, string code, string message) =>
        new()
        {
            Id = code + "-" + buildId[..8],
            Code = code,
            Severity = DiagnosticSeverity.Error,
            Origin = DiagnosticOrigin.Worker,
            Message = message,
            ProjectPath = request.ProjectPath,
            Configuration = request.Configuration,
            BuildId = buildId,
            WorkspaceVersion = request.WorkspaceVersion,
        };

    private static BuildReport Failure(
        BuildRequest request,
        string buildId,
        GrantedSnapshot snapshot,
        List<string> notes,
        long started,
        BuildOutcome outcome,
        NormalizedDiagnostic diagnostic,
        ToolchainInfo? toolchain = null) =>
        Compose(
            request, buildId, snapshot, notes, started, outcome,
            new ProcessOutcome(-1, false, TimeSpan.Zero, string.Empty),
            [diagnostic], [],
            toolchain ?? new ToolchainInfo { RequestedBy = "not probed" });

    private static BuildReport Compose(
        BuildRequest request,
        string buildId,
        GrantedSnapshot snapshot,
        List<string> notes,
        long started,
        BuildOutcome outcome,
        ProcessOutcome process,
        IReadOnlyList<NormalizedDiagnostic> diagnostics,
        IReadOnlyList<BuildArtifact> artifacts,
        ToolchainInfo toolchain)
    {
        var counts = snapshot.Inputs
            .GroupBy(entry => entry.Origin)
            .ToDictionary(group => group.Key.ToString(), group => group.Count(), StringComparer.Ordinal);

        return new BuildReport
        {
            SchemaVersion = 1,
            BuildId = buildId,
            Outcome = outcome,
            ExitCode = process.ExitCode,
            ElapsedSeconds = Math.Round(Stopwatch.GetElapsedTime(started).TotalSeconds, 3),
            ProjectPath = request.ProjectPath.Replace('\\', '/'),
            Configuration = request.Configuration,
            WorkspaceVersion = request.WorkspaceVersion,
            GrantedSnapshotSha256 = snapshot.ComputeIdentity(),
            Toolchain = toolchain,
            Diagnostics = [.. diagnostics
                .OrderByDescending(d => d.Severity)
                .ThenBy(d => d.DocumentPath, StringComparer.Ordinal)
                .ThenBy(d => d.StartLine ?? 0)],
            Artifacts = artifacts,
            Inputs = [.. snapshot.Inputs.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)],
            InputCountsByOrigin = counts,
            OpaqueLogTail = Tail(process.Output, 4_000),
            Notes = notes,
        };
    }

    private static string Tail(string text, int limit) =>
        text.Length <= limit ? text : text[^limit..];

    public static string Serialize(BuildReport report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

    private readonly record struct ProcessOutcome(
        int ExitCode, bool TimedOut, TimeSpan Elapsed, string Output);
}
