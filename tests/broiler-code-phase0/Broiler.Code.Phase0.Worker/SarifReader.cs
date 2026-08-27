using System.Security.Cryptography;
using System.Text.Json;

namespace Broiler.Code.Phase0.Worker;

/// <summary>
/// Reads the compiler's SARIF 2.1 error log into normalized diagnostics.
///
/// This is the whole reason the worker never parses console output. The console
/// text is localized — the machine this baseline was recorded on emits German —
/// but SARIF carries the rule ID, severity, file URI, and one-based region as
/// structured fields, and only the message string is translated. Reading the
/// structure and treating the message as display-only is what makes the
/// diagnostic contract locale-independent.
/// </summary>
public static class SarifReader
{
    /// <summary>
    /// The comma in the <c>ErrorLog</c> property value must be MSBuild-escaped
    /// as <c>%2C</c>. Passed literally, the version selector is silently
    /// dropped and the compiler writes SARIF 1.0.0 instead, whose results carry
    /// <c>resultFile</c> rather than <c>physicalLocation</c> — a schema change
    /// that arrives with no warning and no build failure.
    /// </summary>
    public const string VersionSuffix = "%2Cversion=2.1";

    public static IReadOnlyList<NormalizedDiagnostic> Read(
        string sarifPath,
        SarifContext context,
        out string? compilerVersion,
        out string? compilerLanguage)
    {
        compilerVersion = null;
        compilerLanguage = null;

        using FileStream stream = File.OpenRead(sarifPath);
        using JsonDocument document = JsonDocument.Parse(stream);

        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("version", out JsonElement version) ||
            !version.GetString()!.StartsWith("2.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{sarifPath}' is SARIF {version.GetString()}, not 2.1. " +
                "The ErrorLog version selector was dropped; escape its comma as %2C.");
        }

        var diagnostics = new List<NormalizedDiagnostic>();
        if (!root.TryGetProperty("runs", out JsonElement runs))
            return diagnostics;

        foreach (JsonElement run in runs.EnumerateArray())
        {
            if (run.TryGetProperty("tool", out JsonElement tool) &&
                tool.TryGetProperty("driver", out JsonElement driver))
            {
                compilerVersion ??= GetString(driver, "version");
                compilerLanguage ??= GetString(driver, "language");
            }

            Dictionary<string, string> helpUris = ReadHelpUris(run);
            if (!run.TryGetProperty("results", out JsonElement results))
                continue;

            foreach (JsonElement result in results.EnumerateArray())
                AddResult(diagnostics, result, helpUris, context);
        }

        return diagnostics;
    }

    private static void AddResult(
        List<NormalizedDiagnostic> diagnostics,
        JsonElement result,
        Dictionary<string, string> helpUris,
        SarifContext context)
    {
        string code = GetString(result, "ruleId") ?? "UNKNOWN";
        string message = result.TryGetProperty("message", out JsonElement messageElement)
            ? GetString(messageElement, "text") ?? string.Empty
            : string.Empty;
        DiagnosticSeverity severity = (GetString(result, "level") ?? "warning") switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "note" => DiagnosticSeverity.Information,
            _ => DiagnosticSeverity.Hidden,
        };

        string? documentPath = null;
        string? documentSha = null;
        int? startLine = null, startColumn = null, endLine = null, endColumn = null;
        int? startOffset = null, endOffset = null;

        if (result.TryGetProperty("locations", out JsonElement locations) &&
            locations.GetArrayLength() > 0 &&
            locations[0].TryGetProperty("physicalLocation", out JsonElement physical))
        {
            string? uri = physical.TryGetProperty("artifactLocation", out JsonElement artifact)
                ? GetString(artifact, "uri")
                : null;
            documentPath = context.ToWorkspaceRelative(uri);

            if (physical.TryGetProperty("region", out JsonElement region))
            {
                // SARIF is one-based; the normalized model is zero-based, and
                // converting once here keeps every consumer from guessing.
                startLine = GetInt(region, "startLine") - 1;
                startColumn = GetInt(region, "startColumn") - 1;
                endLine = GetInt(region, "endLine") - 1;
                endColumn = GetInt(region, "endColumn") - 1;
            }

            if (documentPath is not null)
            {
                SourceFile? file = context.Resolve(documentPath);
                if (file is not null)
                {
                    documentSha = file.Sha256;
                    startOffset = file.ToOffset(startLine, startColumn);
                    endOffset = file.ToOffset(endLine, endColumn);
                }
            }
        }

        string id = Identity(code, documentPath, startLine, startColumn, endLine, endColumn);
        diagnostics.Add(new NormalizedDiagnostic
        {
            Id = id,
            Code = code,
            Severity = severity,
            Origin = DiagnosticOrigin.Compiler,
            HelpUri = helpUris.GetValueOrDefault(code),
            Message = message,
            ProjectPath = context.ProjectPath,
            TargetFramework = context.TargetFramework,
            Configuration = context.Configuration,
            BuildId = context.BuildId,
            WorkspaceVersion = context.WorkspaceVersion,
            DocumentPath = documentPath,
            DocumentSha256 = documentSha,
            StartLine = startLine,
            StartColumn = startColumn,
            EndLine = endLine,
            EndColumn = endColumn,
            StartOffset = startOffset,
            EndOffset = endOffset,
        });
    }

    private static Dictionary<string, string> ReadHelpUris(JsonElement run)
    {
        var uris = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!run.TryGetProperty("tool", out JsonElement tool) ||
            !tool.TryGetProperty("driver", out JsonElement driver) ||
            !driver.TryGetProperty("rules", out JsonElement rules))
        {
            return uris;
        }

        foreach (JsonElement rule in rules.EnumerateArray())
        {
            string? id = GetString(rule, "id");
            string? uri = GetString(rule, "helpUri");
            if (id is not null && uri is not null)
                uris[id] = uri;
        }

        return uris;
    }

    private static string Identity(
        string code, string? document, int? startLine, int? startColumn, int? endLine, int? endColumn)
    {
        string material = $"{code}\0{document}\0{startLine}\0{startColumn}\0{endLine}\0{endColumn}";
        return Convert.ToHexStringLower(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material)))[..16];
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 1;
}

/// <summary>Everything the reader needs to normalize one project's SARIF run.</summary>
public sealed class SarifContext
{
    private readonly Dictionary<string, SourceFile> _files = new(StringComparer.OrdinalIgnoreCase);

    public SarifContext(string jobRoot, string projectPath, string? targetFramework,
        string configuration, string buildId, int workspaceVersion)
    {
        JobRoot = Path.GetFullPath(jobRoot);
        ProjectPath = projectPath;
        TargetFramework = targetFramework;
        Configuration = configuration;
        BuildId = buildId;
        WorkspaceVersion = workspaceVersion;
    }

    public string JobRoot { get; }

    public string ProjectPath { get; }

    public string? TargetFramework { get; }

    public string Configuration { get; }

    public string BuildId { get; }

    public int WorkspaceVersion { get; }

    /// <summary>
    /// Maps a SARIF file URI back to a workspace-relative path. A location
    /// outside the job root is dropped rather than reported: it cannot belong
    /// to a document the workspace granted, and reporting it would let a build
    /// point the editor at a file the user never opened.
    /// </summary>
    public string? ToWorkspaceRelative(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !parsed.IsFile)
            return null;

        string absolute = Path.GetFullPath(parsed.LocalPath);
        if (!GrantedSnapshot.IsContained(absolute, JobRoot))
            return null;

        return Path.GetRelativePath(JobRoot, absolute).Replace('\\', '/');
    }

    public SourceFile? Resolve(string relativePath)
    {
        if (_files.TryGetValue(relativePath, out SourceFile? cached))
            return cached;

        string absolute = Path.Combine(JobRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolute))
            return null;

        var file = SourceFile.Load(absolute);
        _files[relativePath] = file;
        return file;
    }
}

/// <summary>
/// The exact bytes the compiler read, with a line index over them. The worker
/// resolves offsets here so a mismatch against the workspace's own snapshot is
/// detectable; the workspace still owns its offsets.
/// </summary>
public sealed class SourceFile
{
    private readonly int[] _lineStarts;

    private SourceFile(string text, int[] lineStarts, string sha256)
    {
        Text = text;
        _lineStarts = lineStarts;
        Sha256 = sha256;
    }

    public string Text { get; }

    public string Sha256 { get; }

    public static SourceFile Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        string text = File.ReadAllText(path);

        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
            else if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))
                starts.Add(i + 1);
        }

        return new SourceFile(text, [.. starts], sha);
    }

    public int? ToOffset(int? zeroBasedLine, int? zeroBasedColumn)
    {
        if (zeroBasedLine is not { } line || zeroBasedColumn is not { } column)
            return null;
        if (line < 0 || line >= _lineStarts.Length)
            return null;
        return Math.Clamp(_lineStarts[line] + column, 0, Text.Length);
    }
}
