using System.Diagnostics;
using System.Text.Json;

namespace Broiler.Code.Phase0.Worker;

public readonly record struct ToolchainProbe(
    string? GlobalJsonPath,
    string? GlobalJsonVersion,
    string? GlobalJsonRollForward,
    string? ResolvedSdkVersion,
    NormalizedDiagnostic? Diagnostic);

/// <summary>
/// Resolves the SDK the way the workspace asked for it, before any expensive
/// work starts.
///
/// The probe runs <c>dotnet --version</c> with the job root as the working
/// directory, which is how the CLI applies the workspace's own
/// <c>global.json</c>. Reading global.json and then building with the IDE's own
/// SDK would produce diagnostics from a compiler the user never selected.
/// </summary>
public static class ToolchainPreflight
{
    public static ToolchainProbe Probe(string jobRoot)
    {
        string? globalJsonPath = FindGlobalJson(jobRoot);
        string? requestedVersion = null;
        string? rollForward = null;

        if (globalJsonPath is not null)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
                if (document.RootElement.TryGetProperty("sdk", out JsonElement sdk))
                {
                    requestedVersion = sdk.TryGetProperty("version", out JsonElement version)
                        ? version.GetString() : null;
                    rollForward = sdk.TryGetProperty("rollForward", out JsonElement roll)
                        ? roll.GetString() : null;
                }
            }
            catch (JsonException exception)
            {
                return new ToolchainProbe(globalJsonPath, null, null, null, Diagnostic(
                    "BRC0100",
                    $"'{Path.GetFileName(globalJsonPath)}' is not valid JSON and no SDK could be " +
                    $"selected: {exception.Message}"));
            }
        }

        // `dotnet --version` prints one version string. It is not localized
        // prose, and reading it is not the same as parsing a build log — but
        // its exit status is what actually reports an unsatisfiable global.json,
        // and the message it prints is display-only like any other.
        var startInfo = new ProcessStartInfo("dotnet", "--version")
        {
            WorkingDirectory = jobRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The dotnet CLI could not be started.");
        string output = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit(TimeSpan.FromSeconds(60));

        if (process.ExitCode != 0 || output.Length == 0)
        {
            return new ToolchainProbe(globalJsonPath, requestedVersion, rollForward, null, Diagnostic(
                "BRC0101",
                requestedVersion is null
                    ? "No .NET SDK could be resolved for this workspace."
                    : $"The workspace pins SDK '{requestedVersion}' " +
                      $"(rollForward '{rollForward ?? "default"}'), which is not installed on this " +
                      "worker. Install it or relax the pin; the worker does not substitute its own SDK." +
                      (error.Length == 0 ? string.Empty : $" CLI output, display only: {error}")));
        }

        return new ToolchainProbe(globalJsonPath, requestedVersion, rollForward, output, null);
    }

    private static string? FindGlobalJson(string jobRoot)
    {
        var directory = new DirectoryInfo(jobRoot);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(candidate))
                return candidate;

            // The search stops at the job root. Anything above it belongs to
            // the machine, not to the workspace, and must not steer the build.
            if (string.Equals(directory.FullName, jobRoot, StringComparison.OrdinalIgnoreCase))
                break;
            directory = directory.Parent;
        }

        return null;
    }

    private static NormalizedDiagnostic Diagnostic(string code, string message) => new()
    {
        Id = code,
        Code = code,
        Severity = DiagnosticSeverity.Error,
        Origin = DiagnosticOrigin.Toolchain,
        Message = message,
        BuildId = string.Empty,
    };
}
