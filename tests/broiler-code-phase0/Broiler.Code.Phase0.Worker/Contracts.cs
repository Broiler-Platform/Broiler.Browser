using System.Text.Json.Serialization;

namespace Broiler.Code.Phase0.Worker;

/// <summary>
/// A build request. Serializable and platform-neutral on purpose: the same
/// contract has to reach a local out-of-process worker and a remote isolated
/// one, and neither may require the caller to know which it is talking to.
/// </summary>
public sealed record BuildRequest
{
    /// <summary>Roots the user has granted. Nothing outside them is materialized.</summary>
    public required IReadOnlyList<string> GrantedRoots { get; init; }

    /// <summary>Project to build, relative to the first granted root.</summary>
    public required string ProjectPath { get; init; }

    public string Configuration { get; init; } = "Release";

    public string Target { get; init; } = "Build";

    /// <summary>Extra MSBuild properties. Never used to smuggle in a RID or a
    /// publish setting that belongs in the project or a publish profile.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Unsaved editor content, keyed by workspace-relative path. Included in
    /// the materialized snapshot so a build can never compile something other
    /// than what the editor is showing.
    /// </summary>
    public IReadOnlyDictionary<string, string> DirtyFiles { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The workspace version this request was composed against.</summary>
    public int WorkspaceVersion { get; init; }

    public RestorePolicy Restore { get; init; } = RestorePolicy.Allowed;

    public int TimeoutSeconds { get; init; } = 600;
}

[JsonConverter(typeof(JsonStringEnumConverter<RestorePolicy>))]
public enum RestorePolicy
{
    /// <summary>Restore may run and may reach configured feeds.</summary>
    Allowed,

    /// <summary>Fail rather than restore. Used to prove an offline build path.</summary>
    Forbidden,
}

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticSeverity>))]
public enum DiagnosticSeverity
{
    Hidden,
    Information,
    Warning,
    Error,
}

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticOrigin>))]
public enum DiagnosticOrigin
{
    /// <summary>Produced by the compiler, with a source location.</summary>
    Compiler,

    /// <summary>Produced by the toolchain: a missing SDK, workload, or feed.</summary>
    Toolchain,

    /// <summary>Produced by the worker itself: a containment or policy refusal.</summary>
    Worker,
}

/// <summary>
/// The one normalized diagnostic the live language service and the build worker
/// both produce. Identity is <see cref="Code"/>, document, and span — never
/// <see cref="Message"/>, which is localized and exists for display only.
/// </summary>
public sealed record NormalizedDiagnostic
{
    /// <summary>Stable within a build; derived from code, document, and span.</summary>
    public required string Id { get; init; }

    public required string Code { get; init; }

    public required DiagnosticSeverity Severity { get; init; }

    public required DiagnosticOrigin Origin { get; init; }

    public string? HelpUri { get; init; }

    /// <summary>Display only. No behavior may depend on it.</summary>
    public required string Message { get; init; }

    public string? ProjectPath { get; init; }

    public string? TargetFramework { get; init; }

    public string? Configuration { get; init; }

    public required string BuildId { get; init; }

    public int WorkspaceVersion { get; init; }

    /// <summary>Workspace-relative path, or null for a project-wide diagnostic.</summary>
    public string? DocumentPath { get; init; }

    /// <summary>
    /// SHA-256 of the exact bytes compiled. The workspace applies the
    /// diagnostic only when this matches the snapshot it granted; without it,
    /// "line 18" is a guess about which version of the file was meant.
    /// </summary>
    public string? DocumentSha256 { get; init; }

    /// <summary>Zero-based, normalized. SARIF reports one-based.</summary>
    public int? StartLine { get; init; }

    public int? StartColumn { get; init; }

    public int? EndLine { get; init; }

    public int? EndColumn { get; init; }

    /// <summary>
    /// Zero-based character offsets, resolved against the compiled bytes. The
    /// workspace still re-derives its own offsets from its own snapshot; these
    /// are here so a mismatch is detectable rather than silent.
    /// </summary>
    public int? StartOffset { get; init; }

    public int? EndOffset { get; init; }

    /// <summary>Where the named symbol is declared, when the worker can tell.</summary>
    public string? RelatedDocumentPath { get; init; }
}

public sealed record BuildArtifact
{
    public required string RelativePath { get; init; }

    public required string Kind { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<InputOrigin>))]
public enum InputOrigin
{
    /// <summary>Present in the granted workspace before evaluation.</summary>
    Granted,

    /// <summary>Editor content that had not been saved.</summary>
    DirtyOverlay,

    /// <summary>Discovered during restore.</summary>
    Restore,

    /// <summary>Emitted by a source generator during the build.</summary>
    Generated,
}

/// <summary>One entry of the build-input hash manifest.</summary>
public sealed record InputManifestEntry
{
    public required string RelativePath { get; init; }

    public required InputOrigin Origin { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }
}

public sealed record ToolchainInfo
{
    public required string RequestedBy { get; init; }

    public string? GlobalJsonVersion { get; init; }

    public string? GlobalJsonRollForward { get; init; }

    /// <summary>What the CLI resolved inside the materialized root.</summary>
    public string? ResolvedSdkVersion { get; init; }

    /// <summary>
    /// The compiler that actually produced the diagnostics, read from the SARIF
    /// tool driver. Phase 3 requires this next to the SDK version so a
    /// mismatch with the language service is visible instead of mysterious.
    /// </summary>
    public string? CompilerVersion { get; init; }

    public string? CompilerUiLanguage { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<BuildOutcome>))]
public enum BuildOutcome
{
    Succeeded,
    Failed,
    ToolchainUnavailable,
    RefusedByPolicy,
    TimedOut,
}

public sealed record BuildReport
{
    public required int SchemaVersion { get; init; }

    public required string BuildId { get; init; }

    public required BuildOutcome Outcome { get; init; }

    public required int ExitCode { get; init; }

    public required double ElapsedSeconds { get; init; }

    public required string ProjectPath { get; init; }

    public required string Configuration { get; init; }

    public required int WorkspaceVersion { get; init; }

    /// <summary>SHA-256 over the sorted granted-input manifest. This is the
    /// snapshot identity the whole report is about.</summary>
    public required string GrantedSnapshotSha256 { get; init; }

    public required ToolchainInfo Toolchain { get; init; }

    public required IReadOnlyList<NormalizedDiagnostic> Diagnostics { get; init; }

    public required IReadOnlyList<BuildArtifact> Artifacts { get; init; }

    public required IReadOnlyList<InputManifestEntry> Inputs { get; init; }

    /// <summary>
    /// Counts by origin, so a reader can see at a glance that evaluation and
    /// restore contributed inputs the granted snapshot never contained.
    /// </summary>
    public required IReadOnlyDictionary<string, int> InputCountsByOrigin { get; init; }

    /// <summary>
    /// The build's console output, retained verbatim and never parsed. It is
    /// localized; every structured field above came from SARIF or from the
    /// worker's own bookkeeping.
    /// </summary>
    public required string OpaqueLogTail { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }
}
