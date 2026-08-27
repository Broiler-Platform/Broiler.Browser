using System.Security.Cryptography;

namespace Broiler.Code.Phase0.Worker;

/// <summary>
/// Collects and verifies build outputs. Existence, kind, size, hash, and
/// containment are checked before the worker reports success: a build that
/// "succeeded" without producing the artifact it claimed is a failure the user
/// finds out about later, at the worst moment.
/// </summary>
public static class ArtifactCollector
{
    public static List<BuildArtifact> Collect(string jobRoot, string projectPath, string configuration)
    {
        var artifacts = new List<BuildArtifact>();
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string binaries = Path.Combine(projectDirectory, "bin", configuration);
        if (!Directory.Exists(binaries))
            return artifacts;

        foreach (string path in Directory.EnumerateFiles(binaries, "*", SearchOption.AllDirectories))
        {
            string kind = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".dll" => "assembly",
                ".exe" => "executable",
                ".pdb" => "symbols",
                ".apk" => "android-package",
                ".aab" => "android-bundle",
                ".wasm" => "webassembly-module",
                ".json" => "configuration",
                _ => "content",
            };

            // Containment is re-checked at collection, not only at
            // materialization: a target is free to write wherever it likes, and
            // a link planted during the build would otherwise carry a file out
            // of the job root under the guise of an artifact.
            var info = new FileInfo(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                !GrantedSnapshot.IsContained(path, jobRoot))
            {
                continue;
            }

            using FileStream stream = File.OpenRead(path);
            artifacts.Add(new BuildArtifact
            {
                RelativePath = Path.GetRelativePath(jobRoot, path).Replace('\\', '/'),
                Kind = kind,
                SizeBytes = info.Length,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)),
            });
        }

        artifacts.Sort((left, right) =>
            string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return artifacts;
    }
}
