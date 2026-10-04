using System.Net;
using Broiler.App.Rendering;
using Reply = Broiler.Browser.Core.Tests.LoopbackHttpServer.Reply;

namespace Broiler.Browser.Core.Tests;

public class PageLoaderErrorResponseTests
{
    [Theory(Timeout = 600000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARedirected429FailsWithoutRetryingAndStoresItsCookies(bool throughProfile)
    {
        const string body = "<html><body><h1>Too many requests</h1><p>Please wait before trying again.</p></body></html>";
        using var server = new LoopbackHttpServer()
            .Map("/start", new Reply(302, Location: "/blocked?q=test"))
            .Map("/blocked", Reply.Text(body, setCookies: ["challenge=1; Path=/; HttpOnly"]) with
            {
                Status = 429,
                Headers = [("Retry-After", "120"), ("Content-Security-Policy", "script-src 'none'")],
            });
        using var profile = throughProfile ? BrowserProfile.CreateEphemeral() : null;
        using var client = throughProfile ? null : new HttpClient(new HttpClientHandler { UseProxy = false });
        using var loader = profile is not null ? new PageLoader(profile.Network) : new PageLoader(client!);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => loader.LoadAsync(PageRequest.ForUrl(server.Url("/start"))));

        Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
        Assert.Single(server.RequestsFor("/start"));
        Assert.Single(server.RequestsFor("/blocked"));
        Assert.Equal(2, server.Requests.Count);

        if (profile is not null)
        {
            Assert.Contains(profile.Cookies.Snapshot(), cookie => cookie.Name == "challenge" && cookie.HttpOnly);
        }
    }

    [Theory(Timeout = 600000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnErrorBodyIsReadBeforeItsStatusFailsTheNavigation(bool throughProfile)
    {
        using var content = new ReadTrackingContent();
        using var handler = new ErrorResponseHandler(content);
        using var profile = throughProfile ? BrowserProfile.CreateEphemeral(handler) : null;
        using var client = throughProfile ? null : new HttpClient(handler, disposeHandler: false);
        using var loader = profile is not null ? new PageLoader(profile.Network) : new PageLoader(client!);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(
            () => loader.LoadAsync(PageRequest.ForUrl("https://example.test/blocked")));

        Assert.Equal(HttpStatusCode.TooManyRequests, failure.StatusCode);
        Assert.True(content.Read, "The diagnostic transport must see the body before the status becomes an exception.");
    }

    [Theory(Timeout = 600000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANetworkFailureStillThrowsWithoutInventingAnHttpDocument(bool throughProfile)
    {
        var failure = new HttpRequestException(HttpRequestError.ConnectionError, "Connection failed.");
        using var handler = new FailedConnectionHandler(failure);
        using var profile = throughProfile ? BrowserProfile.CreateEphemeral(handler) : null;
        using var client = throughProfile ? null : new HttpClient(handler, disposeHandler: false);
        using var loader = profile is not null ? new PageLoader(profile.Network) : new PageLoader(client!);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => loader.LoadAsync(PageRequest.ForUrl("https://example.test/page")));

        Assert.Equal(HttpRequestError.ConnectionError, thrown.HttpRequestError);
        Assert.Null(thrown.StatusCode);
        Assert.Equal(1, handler.Requests);
    }

    private sealed class FailedConnectionHandler(HttpRequestException failure) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromException<HttpResponseMessage>(failure);
        }
    }

    private sealed class ErrorResponseHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = content,
                RequestMessage = request,
            });
    }

    private sealed class ReadTrackingContent : HttpContent
    {
        public bool Read { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync("<p>server refusal explanation</p>"u8.ToArray());
            Read = true;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
