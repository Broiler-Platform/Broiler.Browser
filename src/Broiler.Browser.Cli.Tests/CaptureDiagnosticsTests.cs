using System.Text.Json;

namespace Broiler.Cli.Tests;

/// <summary>
/// What a capture puts into a diagnostics bundle: the document as fetched, every script that ran
/// under the label its failures are logged by, and the document the scripts left.
/// </summary>
/// <remarks>
/// <see cref="DiagnosticSessionTests"/> covers the bundle itself with entries it records by hand;
/// this is the capture's half, which only a capture can produce.
/// </remarks>
[Collection("DiagnosticSession")]
public sealed class CaptureDiagnosticsTests : IDisposable
{
    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    [Fact(Timeout = 600000)]
    public async Task A_Bundle_Holds_The_Document_Its_Scripts_And_What_They_Left()
    {
        var url = _pages.Write("page.html", """
            <!DOCTYPE html>
            <html><body>
            <p id="out">before</p>
            <script>missingGlobalFunction();</script>
            <script>document.getElementById('out').textContent = 'after';</script>
            <script defer src="data:text/javascript,void%200"></script>
            </body></html>
            """);
        var bundle = _pages.Output("bundle");

        using (DiagnosticSession.Start(Program.ResolveDiagnosticOptions(bundle, null)))
        {
            await new CaptureService().CaptureAsync(new CaptureOptions
            {
                Url = url,
                OutputPath = _pages.Output("out.html"),
            });
        }

        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(bundle, "resources", "index.json")));
        var resources = index.RootElement.EnumerateArray()
            .Select(r => (
                Kind: r.GetProperty("Kind").GetString(),
                Label: r.GetProperty("Label").GetString(),
                SavedAs: r.GetProperty("SavedAs").GetString()))
            .ToArray();

        Assert.Contains(resources, r => r is { Kind: "Document", Label: null });
        Assert.Contains(resources, r => r is { Kind: "ExecutedScript", Label: "inline-0" });
        Assert.Contains(resources, r => r is { Kind: "ExecutedScript", Label: "inline-1" });
        Assert.Contains(resources, r => r is { Kind: "ExecutedScript", Label: "deferred-0" });

        var after = Assert.Single(resources, r => r.Label == DiagnosticSession.AfterScriptsLabel);
        Assert.Equal(
            "after",
            CaptureServiceTests.TextOf(await File.ReadAllTextAsync(Path.Combine(bundle, "resources", after.SavedAs!)), "out"));

        // The failure names the script by the label its archived source is filed under.
        Assert.Contains("inline-0", await File.ReadAllTextAsync(Path.Combine(bundle, "javascript-errors.log")));
    }
}
