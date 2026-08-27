using System.Net;
using System.Net.Http;
using Broiler.App.Rendering;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The browser's page loader borrows the browser's <see cref="HttpClient"/>; it does not own it.
/// The client is the connection pool, and the browser keeps one for the whole session — a loader
/// that disposed it at the end of every navigation would close pooled connections underneath the
/// pending zero-byte read-ahead the pool's scavenger arms them with, which surfaces as
/// <c>IOException: Unable to read data from the transport connection</c> wrapping
/// <see cref="System.Net.Sockets.SocketError.OperationAborted"/> (Windows: WSA 995,
/// "aborted because of either a thread exit or an application request").
/// See <c>docs/browser-connection-pool-aborts.md</c>.
/// </summary>
public class PageLoaderLifetimeTests
{
    /// <summary>Answers every request from memory, so the tests touch no socket.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>ok</body></html>"),
                RequestMessage = request,
            });
    }

    [Fact(Timeout = 600000)]
    public async Task Dispose_LeavesACallerOwnedClientUsable()
    {
        using HttpClient client = new(new StubHandler());

        var loader = new PageLoader(client);
        _ = await loader.FetchAsync("https://example.com/first");
        loader.Dispose();

        // The next navigation reuses the browser's client, so it must have survived the
        // previous loader — the regression this guards is disposing it here.
        var next = new PageLoader(client);
        var (_, html) = await next.FetchAsync("https://example.com/second");

        Assert.Contains("ok", html);
    }

    [Fact(Timeout = 600000)]
    public async Task Dispose_DisposesAClientItWasGivenOwnershipOf()
    {
        HttpClient client = new(new StubHandler());

        var loader = new PageLoader(client, ownsHttpClient: true);
        _ = await loader.FetchAsync("https://example.com/first");
        loader.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.GetStringAsync("https://example.com/second"));
    }

    [Fact(Timeout = 600000)]
    public void Constructor_RejectsAMissingClient() =>
        Assert.Throws<ArgumentNullException>(() => new PageLoader(null!));
}
