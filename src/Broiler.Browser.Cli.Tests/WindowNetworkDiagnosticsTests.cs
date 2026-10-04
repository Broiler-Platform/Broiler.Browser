using Broiler.Cli.Analysis;

namespace Broiler.Cli.Tests;

/// <summary>The independent window run must leave evidence of every navigation and its terminal result.</summary>
[Collection(CaptureCollection.Name)]
public sealed class WindowNetworkDiagnosticsTests
{
    [Fact(Timeout = 600000)]
    public void A_Load_Error_Is_Terminal_And_Does_Not_Consume_The_Whole_Window_Budget()
    {
        using var pages = new TestPages();
        var output = pages.Output("window-error");
        Directory.CreateDirectory(output);
        var missing = new Uri(Path.Combine(output, "missing.html")).AbsoluteUri;

        var result = WindowProbe.Render(missing, 320, 240, output, TimeSpan.FromSeconds(10));
        using var image = result.Page;

        Assert.True(result.Settled);
        Assert.Equal("Error loading page", result.Status);
    }
}
