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
}
