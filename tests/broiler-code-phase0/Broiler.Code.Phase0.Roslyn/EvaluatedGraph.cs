using System.Diagnostics;
using System.Text.Json;

namespace Broiler.Code.Phase0.Roslyn;

/// <summary>
/// The target-specific evaluated graph: the metadata references, defines, and
/// compiler options a semantic service needs before it can say anything true.
///
/// Roslyn alone is not a language service. A <c>CSharpCompilation</c> with no
/// metadata references reports every framework type as missing, so the honest
/// question for an Android or browser host is not "can Roslyn run there" but
/// "can the evaluated graph and its reference set get there". This type is how
/// Phase 0 measures the second half.
/// </summary>
public sealed record EvaluatedGraph(
    IReadOnlyList<string> CompilePaths,
    IReadOnlyList<string> MetadataReferencePaths,
    IReadOnlyList<string> PreprocessorSymbols,
    string? LanguageVersion,
    string? NullableContext)
{
    public long TotalReferenceBytes => MetadataReferencePaths
        .Where(File.Exists)
        .Sum(path => new FileInfo(path).Length);

    /// <summary>
    /// Obtains the graph from the SDK the way a trusted worker would: MSBuild's
    /// own structured <c>-getItem</c>/<c>-getProperty</c> JSON output. Nothing
    /// here reads the build log, and the result is identical under any UI
    /// language.
    /// </summary>
    public static EvaluatedGraph Evaluate(string projectPath, string configuration)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
        };
        foreach (string argument in new[]
        {
            "build", projectPath, "-c", configuration, "-t:ResolveReferences",
            // The source list is part of the graph, not something to guess by
            // scanning directories. Conditional items, linked files, and the
            // SDK's own exclusions all live here; a directory scan picks up
            // obj\ output and compiles files the real build never sees.
            "-getItem:Compile",
            "-getItem:ReferencePath",
            "-getProperty:DefineConstants",
            "-getProperty:LangVersion",
            "-getProperty:Nullable",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The dotnet CLI could not be started.");
        string json = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit(TimeSpan.FromMinutes(5));

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Evaluating '{projectPath}' failed with exit code {process.ExitCode}. " +
                $"Display-only output: {error}");
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        var compiles = new List<string>();
        var references = new List<string>();
        if (root.TryGetProperty("Items", out JsonElement items))
        {
            ReadFullPaths(items, "Compile", compiles);
            ReadFullPaths(items, "ReferencePath", references);
        }

        string? defines = null;
        string? languageVersion = null;
        string? nullable = null;
        if (root.TryGetProperty("Properties", out JsonElement properties))
        {
            defines = GetString(properties, "DefineConstants");
            languageVersion = GetString(properties, "LangVersion");
            nullable = GetString(properties, "Nullable");
        }

        return new EvaluatedGraph(
            compiles,
            references,
            defines?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [],
            languageVersion,
            nullable);
    }

    private static void ReadFullPaths(JsonElement items, string itemName, List<string> into)
    {
        if (!items.TryGetProperty(itemName, out JsonElement element))
            return;

        foreach (JsonElement entry in element.EnumerateArray())
        {
            if (entry.TryGetProperty("FullPath", out JsonElement path) &&
                path.GetString() is { Length: > 0 } value)
            {
                into.Add(value);
            }
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
}
