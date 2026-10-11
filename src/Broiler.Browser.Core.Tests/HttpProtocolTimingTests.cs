using System.Net;
using Broiler.App.Rendering;
using Broiler.HtmlBridge;

namespace Broiler.Browser.Core.Tests;

public class HttpProtocolTimingTests
{
    [Theory]
    [InlineData(true, "2.0", "h2")]
    [InlineData(true, "1.1", "http/1.1")]
    [InlineData(false, "2.0", "h2")]
    [InlineData(false, "1.1", "http/1.1")]
    public async Task PageLoadCarriesTheResponseProtocolIntoNavigationTiming(bool throughProfile, string version, string protocol)
    {
        using var handler = new ResponseHandler(Version.Parse(version));
        using var profile = BrowserProfile.CreateEphemeral(handler);
        using var client = new HttpClient(handler, disposeHandler: false);
        using var pipeline = new RenderingPipeline(
            throughProfile ? new PageLoader(profile.Network) : new PageLoader(client), new ScriptEngine());
        var page = await pipeline.LoadAsync(PageRequest.ForUrl("https://example.test/"));
        Assert.Equal(protocol, page.FetchTiming!.NextHopProtocol);
        using var session = pipeline.ExecuteScriptsInteractive(page.Content, page.InheritedPolicy, page);
        Assert.Contains($"<div id=\"out\">{protocol}</div>", session!.SettleLoadWindow());
    }

    [Fact]
    public async Task InlineDocumentHasNoNetworkProtocol()
    {
        using var profile = BrowserProfile.CreateEphemeral();
        using var pipeline = new RenderingPipeline(new PageLoader(profile.Network), new ScriptEngine());
        var page = await pipeline.LoadAsync(PageRequest.ForUrl("https://example.test/") with { InlineDocument = "<html></html>" });
        Assert.Equal(string.Empty, page.FetchTiming!.NextHopProtocol);
    }

    private sealed class ResponseHandler(Version version) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Version = version,
                Content = new StringContent("<html><body><div id=\"out\"></div><script>document.getElementById('out').textContent = performance.getEntriesByType('navigation')[0].nextHopProtocol;</script></body></html>", System.Text.Encoding.UTF8, "text/html"),
            });
    }
}
