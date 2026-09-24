namespace Broiler.Cli.Tests;

/// <summary>
/// The tests that run a capture in this process, one at a time.
/// </summary>
/// <remarks>
/// A capture reaches state that is process-wide: the render path's unsynchronised caches, which is
/// why a batch runs its captures in child processes (<see cref="BatchRunner"/>), and
/// <c>ResourceTrace</c> and <c>RenderLogger</c>, whose listeners would record a concurrent capture
/// into another test's diagnostics bundle.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CaptureCollection
{
    public const string Name = "Capture";
}

/// <summary>
/// A directory of pages written for one test, removed with it.
/// </summary>
internal sealed class TestPages : IDisposable
{
    public TestPages() => Directory.CreateDirectory(Root);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "broiler-cli-" + Guid.NewGuid().ToString("N"));

    /// <summary>Writes <paramref name="html"/> to <paramref name="name"/> and returns its <c>file:</c> URL.</summary>
    public string Write(string name, string html)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, html);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>A path in the directory for a test's output.</summary>
    public string Output(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
