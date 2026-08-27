using System.Security.Cryptography;
using System.Text;

namespace Broiler.Code.Phase0.Worker;

/// <summary>
/// Materializes the granted workspace into a disposable per-job root outside
/// the Broiler checkout, applies unsaved editor content, and records what it
/// copied by hash.
/// </summary>
public sealed class GrantedSnapshot : IDisposable
{
    private static readonly string[] ExcludedDirectories =
        ["bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts"];

    private readonly List<InputManifestEntry> _inputs = [];
    private readonly List<string> _refusals = [];
    private bool _disposed;

    private GrantedSnapshot(string root, string sourceRoot)
    {
        Root = root;
        SourceRoot = sourceRoot;
    }

    /// <summary>The disposable per-job directory the build runs in.</summary>
    public string Root { get; }

    /// <summary>The first granted root, used to resolve relative request paths.</summary>
    public string SourceRoot { get; }

    public IReadOnlyList<InputManifestEntry> Inputs => _inputs;

    /// <summary>Paths refused for containment reasons, as actionable text.</summary>
    public IReadOnlyList<string> Refusals => _refusals;

    /// <summary>
    /// SHA-256 over the ordered (path, hash) pairs of the granted inputs. Two
    /// jobs with the same value compiled the same bytes; a diagnostic is only
    /// applicable to the workspace state that produced this value.
    /// </summary>
    public string ComputeIdentity()
    {
        var builder = new StringBuilder();
        foreach (InputManifestEntry entry in _inputs
            .Where(entry => entry.Origin is InputOrigin.Granted or InputOrigin.DirtyOverlay)
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal))
        {
            builder.Append(entry.RelativePath).Append('\0').Append(entry.Sha256).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    public static GrantedSnapshot Materialize(BuildRequest request, string jobRoot)
    {
        string sourceRoot = Path.GetFullPath(request.GrantedRoots[0]);
        Directory.CreateDirectory(jobRoot);
        var snapshot = new GrantedSnapshot(jobRoot, sourceRoot);

        foreach (string granted in request.GrantedRoots)
            snapshot.CopyTree(Path.GetFullPath(granted), sourceRoot);

        snapshot.ApplyDirtyOverlays(request.DirtyFiles);
        return snapshot;
    }

    /// <summary>
    /// Records what evaluation and restore produced that the granted snapshot
    /// never contained: <c>obj</c> assets, generated compile inputs, and NuGet
    /// import files. These can only be found after the fact, inside the worker,
    /// which is exactly why the manifest is completed here rather than assumed
    /// up front.
    /// </summary>
    public void RecordDiscoveredInputs()
    {
        foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Root, path).Replace('\\', '/');
            if (!relative.Contains("/obj/", StringComparison.Ordinal) &&
                !relative.StartsWith("obj/", StringComparison.Ordinal))
            {
                continue;
            }

            InputOrigin origin = relative.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? InputOrigin.Generated
                : InputOrigin.Restore;

            // Only inputs, not build outputs: a copied assembly under obj is a
            // product of the build, not something the build consumed.
            if (relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                relative.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddEntry(path, relative, origin);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        TryDelete(Root);
    }

    private void CopyTree(string source, string sourceRoot)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
            .Prepend(source))
        {
            var info = new DirectoryInfo(directory);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                _refusals.Add($"Refused reparse point '{Relative(directory, sourceRoot)}'.");
                continue;
            }

            if (IsExcluded(directory, sourceRoot))
                continue;

            string relativeDirectory = Relative(directory, sourceRoot);
            Directory.CreateDirectory(Path.Combine(Root, relativeDirectory));

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    _refusals.Add($"Refused reparse point '{Relative(file, sourceRoot)}'.");
                    continue;
                }

                string relative = Relative(file, sourceRoot);
                string destination = Path.GetFullPath(Path.Combine(Root, relative));
                if (!IsContained(destination, Root))
                {
                    _refusals.Add($"Refused '{relative}': it resolves outside the job root.");
                    continue;
                }

                File.Copy(file, destination, overwrite: true);
                AddEntry(destination, relative, InputOrigin.Granted);
            }
        }
    }

    private void ApplyDirtyOverlays(IReadOnlyDictionary<string, string> dirtyFiles)
    {
        foreach ((string relative, string content) in dirtyFiles)
        {
            string normalized = relative.Replace('\\', '/');
            string destination = Path.GetFullPath(Path.Combine(Root, normalized));
            if (!IsContained(destination, Root))
            {
                _refusals.Add($"Refused dirty overlay '{relative}': it resolves outside the job root.");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _inputs.RemoveAll(entry => string.Equals(entry.RelativePath, normalized, StringComparison.Ordinal));
            AddEntry(destination, normalized, InputOrigin.DirtyOverlay);
        }
    }

    private void AddEntry(string absolutePath, string relativePath, InputOrigin origin)
    {
        var info = new FileInfo(absolutePath);
        using FileStream stream = File.OpenRead(absolutePath);
        _inputs.Add(new InputManifestEntry
        {
            RelativePath = relativePath.Replace('\\', '/'),
            Origin = origin,
            SizeBytes = info.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)),
        });
    }

    private static bool IsExcluded(string directory, string sourceRoot)
    {
        string relative = Relative(directory, sourceRoot);
        foreach (string segment in relative.Split(
            ['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (ExcludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string Relative(string path, string root) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// True when <paramref name="candidate"/> is inside <paramref name="root"/>.
    /// The separator suffix matters: without it, "C:\jobs\1-evil" passes a
    /// prefix test against "C:\jobs\1".
    /// </summary>
    public static bool IsContained(string candidate, string root)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
