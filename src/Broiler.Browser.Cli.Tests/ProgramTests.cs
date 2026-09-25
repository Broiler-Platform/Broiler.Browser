namespace Broiler.Cli.Tests;

/// <summary>
/// The command line's own arguments: what it refuses and what it points elsewhere.
/// </summary>
[Collection(CaptureCollection.Name)]
public sealed class ProgramTests
{
    /// <summary>Runs <see cref="Program.Main"/> and returns its exit code and what it wrote to stderr.</summary>
    private static async Task<(int ExitCode, string Error)> RunAsync(params string[] args)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exitCode = await Program.Main(args);
            return (exitCode, error.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>
    /// A script that still passes <c>--convert-doc</c> is told where conversion went, rather than that
    /// its argument is unknown.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task Convert_Doc_Points_At_The_Documents_Command_Line()
    {
        var (exitCode, error) = await RunAsync("--convert-doc", "report.rtf", "--output", "report.txt");

        Assert.Equal(1, exitCode);
        Assert.Contains("broilerdoc convert", error);
    }

    [Fact(Timeout = 600000)]
    public async Task An_Expression_Without_A_Page_Is_Refused()
    {
        var (exitCode, error) = await RunAsync("--evaluate", "1 + 1", "--output", "report.json");

        Assert.Equal(1, exitCode);
        Assert.Contains("--evaluate-page", error);
    }

    [Fact(Timeout = 600000)]
    public async Task A_Url_That_Is_Neither_Web_Nor_File_Is_Refused()
    {
        var (exitCode, error) = await RunAsync("--url", "ftp://example.test/page.html", "--output", "out.html");

        Assert.Equal(1, exitCode);
        Assert.Contains("not a valid HTTP, HTTPS, or file URL", error);
    }

    [Fact(Timeout = 600000)]
    public async Task The_Engine_Smoke_Tests_Pass()
    {
        var (exitCode, _) = await RunAsync("--test-engines");

        Assert.Equal(0, exitCode);
    }

    [Fact(Timeout = 600000)]
    public async Task An_Analysis_Without_An_Output_Directory_Is_Refused()
    {
        var (exitCode, error) = await RunAsync("--analyze", "https://example.test/");

        Assert.Equal(1, exitCode);
        Assert.Contains("--output-dir", error);
    }

    [Fact(Timeout = 600000)]
    public async Task An_Analysis_Is_Not_Combined_With_A_Capture()
    {
        var (exitCode, error) = await RunAsync("--analyze", "https://example.test/", "--url", "https://example.test/", "--output-dir", "out");

        Assert.Equal(1, exitCode);
        Assert.Contains("cannot be combined", error);
    }

    [Fact(Timeout = 600000)]
    public async Task A_Negative_Analysis_Timeout_Is_Refused()
    {
        var (exitCode, error) = await RunAsync("--analyze", "https://example.test/", "--output-dir", "out", "--analysis-timeout", "-1");

        Assert.Equal(1, exitCode);
        Assert.Contains("--analysis-timeout", error);
    }

    [Fact(Timeout = 600000)]
    public void A_File_Path_Becomes_Its_Url_And_Keeps_Its_Fragment()
    {
        var path = Path.Combine(Path.GetTempPath(), "broiler-resolve-" + Guid.NewGuid().ToString("N") + ".html");
        File.WriteAllText(path, "<p>x</p>");
        try
        {
            Assert.True(Program.TryResolvePageUrl(path + "#top", out var url));
            Assert.Equal(new Uri(path).AbsoluteUri + "#top", url);
            Assert.False(Program.TryResolvePageUrl("ftp://example.test/x", out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
