using System.Net;
using Broiler.App.Rendering;
using Broiler.Net.Cookies;
using Broiler.Net.Http;
using Reply = Broiler.Browser.Core.Tests.LoopbackHttpServer.Reply;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// Page navigations on the profile's network: where a redirected navigation lands, which cookies it
/// sends and stores, and how the navigation's origin decides its same-site status.
/// </summary>
/// <remarks>
/// <para>
/// Every test runs a real <see cref="PageLoader"/> over a real <see cref="BrowserProfile"/> against a
/// loopback server, so the cookies are the ones the server saw arrive, not the ones a stub was told
/// about.
/// </para>
/// <para>
/// <b>The jar that was replaced.</b> The browser used to navigate through a process-wide
/// <see cref="HttpClient"/> whose handler kept an automatic cookie container. Consent and login flows
/// worked because of it: it stored the cookie a redirect hop set, and the cookie an error response
/// set. The profile's session has to do both, or those flows break the moment the old jar goes.
/// </para>
/// </remarks>
public class PageLoaderNavigationTests
{
    private static Task<PageLoadResult> NavigateAsync(BrowserProfile profile, PageRequest request)
    {
        PageLoader loader = new(profile.Network);
        return loader.LoadAsync(request);
    }

    private static Task<PageLoadResult> NavigateAsync(BrowserProfile profile, string url) =>
        NavigateAsync(profile, PageRequest.ForUrl(url));

    private static bool Stored(BrowserProfile profile, string name) =>
        profile.Cookies.Snapshot().Any(cookie => cookie.Name == name);

    [Fact(Timeout = 600000)]
    public async Task ARedirectedGetIsAtItsFinalUrl()
    {
        using var server = new LoopbackHttpServer();
        server
            .Map("/start", new Reply(302, Location: "/landing"))
            .Map("/landing", Reply.Text("<html><body>landed</body></html>"));
        using var profile = BrowserProfile.CreateEphemeral();

        PageLoadResult result = await NavigateAsync(profile, server.Url("/start"));

        // The regression: a GET used to report the URL it asked for, so a redirected page was
        // treated as the page it had been redirected away from.
        Assert.Equal(server.Url("/landing"), result.FinalUrl);
        Assert.True(result.Redirected);
        Assert.Equal([server.Url("/start"), server.Url("/landing")], result.RedirectChain.Select(url => url.AbsoluteUri).ToArray());
        Assert.Equal(PageRequest.Get, result.Method);
        Assert.Contains("landed", result.Html);

        // The tuple API the pipeline used to read reports the same URL.
        using PageLoader loader = new(profile.Network);
        var (normalisedUrl, _) = await loader.FetchAsync(server.Url("/start"));
        Assert.Equal(server.Url("/landing"), normalisedUrl);
    }

    [Fact(Timeout = 600000)]
    public async Task CookiesFromARedirectHopAndAnErrorResponseAreStoredAndSent()
    {
        using var server = new LoopbackHttpServer();
        server
            .Map("/start", new Reply(302, Location: "/landing", SetCookies: ["hop=1; Path=/"]))
            .Map("/landing", Reply.Text("<p>landed</p>", setCookies: ["land=1; Path=/"]))
            .Map("/fail", Reply.Text("<p>no</p>", setCookies: ["err=1; Path=/"]) with { Status = 500 })
            .Map("/next", Reply.Text("<p>next</p>"));
        using var profile = BrowserProfile.CreateEphemeral();

        _ = await NavigateAsync(profile, server.Url("/start"));

        // The landing request is the same navigation's next hop: it already carries the hop's cookie.
        Assert.Equal("hop=1", Assert.Single(server.RequestsFor("/landing")).Cookie);

        // An error status is still the error path...
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => NavigateAsync(profile, server.Url("/fail")));
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);

        // ...but only after the response's cookies were stored.
        Assert.True(Stored(profile, "hop"));
        Assert.True(Stored(profile, "land"));
        Assert.True(Stored(profile, "err"));

        _ = await NavigateAsync(profile, server.Url("/next"));
        string sent = Assert.Single(server.RequestsFor("/next")).Cookie;
        Assert.Contains("hop=1", sent);
        Assert.Contains("land=1", sent);
        Assert.Contains("err=1", sent);
    }

    [Fact(Timeout = 600000)]
    public async Task TheResultCarriesTheHeadersTheHostMayReadAndNeverSetCookie()
    {
        using var server = new LoopbackHttpServer()
            .Map("/page", Reply.Text("<p>x</p>", setCookies: ["sid=1; Path=/"]) with
            {
                Headers = [("Content-Security-Policy", "script-src 'self'"), ("X-Probe", "yes")],
            });
        using var profile = BrowserProfile.CreateEphemeral();

        PageLoadResult result = await NavigateAsync(profile, server.Url("/page"));

        Assert.Equal(200, result.StatusCode);
        Assert.Equal("script-src 'self'", result.ContentSecurityPolicy);
        Assert.Equal(["yes"], result.GetHeaderValues("X-Probe").ToArray());
        Assert.Empty(result.GetHeaderValues("Set-Cookie"));
        Assert.True(Stored(profile, "sid"));
        Assert.Equal(SameSiteStatus.SameSite, result.SameSite);
    }

    [Fact(Timeout = 600000)]
    public async Task APostAnsweredWithSeeOtherLeavesAGetInHistory()
    {
        using var server = new LoopbackHttpServer();
        server
            .Map("/submit", new Reply(303, Location: "/done"))
            .Map("/done", Reply.Text("<p>done</p>"));
        using var profile = BrowserProfile.CreateEphemeral();
        var initiator = DocumentRequestContext.CreateTopLevel(new Uri(server.Url("/form")));
        PageRequest submit = new PageRequest(server.Url("/submit"), PageRequest.Post, PageRequest.FormUrlEncoded, "q=1")
        {
            Initiator = initiator,
            NavigationType = PageNavigationType.FormSubmission,
        };

        PageLoadResult result = await NavigateAsync(profile, submit);
        PageRequest entry = submit.ForLoadedDocument(result);

        Assert.Equal("POST", Assert.Single(server.RequestsFor("/submit")).Method);
        Assert.Equal("GET", Assert.Single(server.RequestsFor("/done")).Method);
        Assert.Equal(PageRequest.Get, result.Method);

        // Reloading the result must not submit the form again: the entry is a GET of where it landed,
        // still the form document's navigation, with the status a UI reload replays.
        Assert.Equal(server.Url("/done"), entry.Url);
        Assert.True(entry.IsRepeatable);
        Assert.False(entry.HasBody);
        Assert.Same(initiator, entry.Initiator);
        Assert.Equal(SameSiteStatus.SameSite, entry.RecordedSameSite);
    }

    [Fact(Timeout = 600000)]
    public async Task APostAnsweredInPlaceKeepsItsBodyInHistory()
    {
        using var server = new LoopbackHttpServer()
            .Map("/submit", Reply.Text("<p>thanks</p>"));
        using var profile = BrowserProfile.CreateEphemeral();
        PageRequest submit = new(server.Url("/submit"), PageRequest.Post, PageRequest.FormUrlEncoded, "q=1");

        PageRequest entry = submit.ForLoadedDocument(await NavigateAsync(profile, submit));

        Assert.Equal(PageRequest.Post, entry.Method);
        Assert.Equal("q=1", entry.Body);
        Assert.False(entry.IsRepeatable);
    }

    /// <summary>
    /// The initiator is what the transport decides SameSite from: a navigation a cross-site page
    /// started does not carry the target's <c>Strict</c> cookies, and a cross-site <c>POST</c> not its
    /// <c>Lax</c> ones either. The same request from the address bar carries both.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task ANavigationStartedByACrossSitePageIsCrossSite()
    {
        using var server = new LoopbackHttpServer()
            .Map("/login", Reply.Text("<p>in</p>", setCookies:
            [
                "strict=1; SameSite=Strict; Path=/",
                "lax=1; SameSite=Lax; Path=/",
            ]))
            .Map("/target", Reply.Text("<p>target</p>"));
        using var profile = BrowserProfile.CreateEphemeral();
        _ = await NavigateAsync(profile, server.Url("/login"));
        var crossSite = DocumentRequestContext.CreateTopLevel(new Uri(server.LocalhostUrl("/page")));
        var sameSite = DocumentRequestContext.CreateTopLevel(new Uri(server.Url("/page")));

        _ = await NavigateAsync(profile, PageRequest.ForUrl(server.Url("/target")) with
        {
            Initiator = crossSite,
            NavigationType = PageNavigationType.Link,
        });
        _ = await NavigateAsync(profile, new PageRequest(server.Url("/target"), PageRequest.Post, PageRequest.FormUrlEncoded, "a=1")
        {
            Initiator = crossSite,
            NavigationType = PageNavigationType.FormSubmission,
        });
        _ = await NavigateAsync(profile, PageRequest.ForUrl(server.Url("/target")) with
        {
            Initiator = sameSite,
            NavigationType = PageNavigationType.Link,
        });
        _ = await NavigateAsync(profile, server.Url("/target"));

        var requests = server.RequestsFor("/target");
        Assert.Equal(4, requests.Length);
        Assert.Equal("lax=1", requests[0].Cookie);
        Assert.Equal(string.Empty, requests[1].Cookie);
        Assert.Contains("strict=1", requests[2].Cookie);
        Assert.Contains("strict=1", requests[3].Cookie);
    }

    /// <summary>
    /// A reload from the browser's UI has no initiator; it replays the same-site status recorded for
    /// the document it reloads, so reloading a page a cross-site link opened stays cross-site.
    /// </summary>
    [Theory(Timeout = 600000)]
    [InlineData(SameSiteStatus.CrossSite, false)]
    [InlineData(SameSiteStatus.SameSite, true)]
    public async Task AUiReloadReplaysTheRecordedSameSiteStatus(SameSiteStatus recorded, bool sendsStrict)
    {
        using var server = new LoopbackHttpServer()
            .Map("/login", Reply.Text("<p>in</p>", setCookies: ["strict=1; SameSite=Strict; Path=/"]))
            .Map("/page", Reply.Text("<p>page</p>"));
        using var profile = BrowserProfile.CreateEphemeral();
        _ = await NavigateAsync(profile, server.Url("/login"));

        PageRequest reload = PageRequest.ForUrl(server.Url("/page")) with
        {
            NavigationType = PageNavigationType.Reload,
            RecordedSameSite = recorded,
        };
        PageLoadResult result = await NavigateAsync(profile, reload);

        Assert.Equal(sendsStrict, Assert.Single(server.RequestsFor("/page")).Cookie.Contains("strict=1", StringComparison.Ordinal));
        Assert.Equal(recorded, result.SameSite);
    }

    [Fact]
    public void TheNavigationContextNamesTheInitiatorAndOnlyAUiReloadReplays()
    {
        var page = DocumentRequestContext.CreateTopLevel(new Uri("https://a.example/"));

        RequestContext typed = PageLoader.NavigationContext(PageRequest.ForUrl("https://b.example/"));
        Assert.True(typed.IsTopLevelNavigation);
        Assert.Null(typed.Client);
        Assert.False(typed.IsUserReload);

        RequestContext link = PageLoader.NavigationContext(PageRequest.ForUrl("https://b.example/") with
        {
            Initiator = page,
            NavigationType = PageNavigationType.Link,
        });
        Assert.Same(page, link.Client);

        // A reload whose entry never finished loading has nothing to replay, so it is the entry's own
        // navigation again, initiator included.
        RequestContext unrecorded = PageLoader.NavigationContext(PageRequest.ForUrl("https://b.example/") with
        {
            Initiator = page,
            NavigationType = PageNavigationType.Reload,
        });
        Assert.False(unrecorded.IsUserReload);
        Assert.Same(page, unrecorded.Client);

        RequestContext reload = PageLoader.NavigationContext(PageRequest.ForUrl("https://b.example/") with
        {
            Initiator = page,
            NavigationType = PageNavigationType.Reload,
            RecordedSameSite = SameSiteStatus.SameSite,
        });
        Assert.True(reload.IsUserReload);
        Assert.True(reload.ReloadWasSameSite);
        Assert.Null(reload.Client);
    }

    [Fact(Timeout = 600000)]
    public async Task ALocalFileLoadsWithoutTheNetwork()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broiler-page-loader-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, "<p>local</p>");
        try
        {
            using var profile = BrowserProfile.CreateEphemeral();
            string url = new Uri(path).AbsoluteUri;

            PageLoadResult result = await NavigateAsync(profile, url);

            Assert.Equal(url, result.FinalUrl);
            Assert.Contains("local", result.Html);
            Assert.Null(result.SameSite);

            // Its document is cookie-averse: document.cookie reads nothing and writes nothing there.
            Assert.True(RenderingPipeline.CreateDocumentContext(result.FinalUrl).IsCookieAverse);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
