using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Broiler.App;
using Broiler.App.Rendering;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;
using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Dom;
using Broiler.Net.Http;
using Reply = Broiler.Browser.Core.Tests.LoopbackHttpServer.Reply;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// What moving navigation onto the profile's transport must not have cost: a page's scripts under a
/// real <c>Content-Security-Policy</c> header, a load that fails instead of hanging, loop guards that
/// see redirects, a page that fetches each image once however many frames of it are painted, and
/// favorites saved before the address bar showed canonical URLs.
/// </summary>
public class NavigationRobustnessTests
{
    // A 1x1 PNG, so the renderer has a real image to decode.
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// A header policy that lists its script host the way real ones do — a bare host, no scheme — does
    /// not stop the page's scripts: the header is not enforced until host sources are matched.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task AHeaderPolicyListingABareHostDoesNotDropThePagesScripts()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/app.js", Reply.Text("window.appRan = true;", "text/javascript"));
        server.Map("/page", Reply.Text("<html><head><script src=\"/app.js\"></script></head><body></body></html>") with
        {
            Headers = [("Content-Security-Policy", $"script-src 127.0.0.1:{server.Port} *.localhost")],
        });
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();
        using var pipeline = new RenderingPipeline(new PageLoader(profile.Network), new ScriptEngine(), profile.Network);

        LoadedPage page = await pipeline.LoadAsync(PageRequest.ForUrl(server.Url("/page")));

        Assert.Equal($"script-src 127.0.0.1:{server.Port} *.localhost", page.Response.ContentSecurityPolicy);
        Assert.Contains("window.appRan = true;", page.Content.Scripts);
        Assert.Single(server.RequestsFor("/app.js"));
    }

    /// <summary>
    /// A server that sends the headers and part of the body, then nothing, fails the load once the
    /// navigation's budget runs out — the headers arriving is not the end of the budget.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task ADocumentWhoseBodyStallsFailsWithATimeout()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/stall", new Reply(Body: "<html"u8.ToArray(), DeclaredLength: 100));
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();
        using var loader = new PageLoader(profile.Network, TimeSpan.FromSeconds(1));

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => loader.LoadAsync(PageRequest.ForUrl(server.Url("/stall"))));

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), $"the load took {elapsed.Elapsed}");
    }

    /// <summary>A stop by the user is still a cancellation, not a timeout.</summary>
    [Fact(Timeout = 600000)]
    public async Task StoppingAStalledLoadIsACancellation()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/stall", new Reply(Body: "<html"u8.ToArray(), DeclaredLength: 100));
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();
        using var loader = new PageLoader(profile.Network);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loader.LoadAsync(PageRequest.ForUrl(server.Url("/stall")), stop.Token));
    }

    /// <summary>
    /// <c>/app</c> redirects to <c>/login</c>, whose script sends the page back to <c>/app</c>: that is
    /// not the document already loaded -- <c>/app</c> answered with a redirect -- so the first return is
    /// followed. The redirect's source counts towards the same-path budget, so once it has been reached
    /// <see cref="BrowserApp.SamePathLoadLimit"/> times another return is a loop and is refused. Asking
    /// for where the load landed is still the document already loaded.
    /// </summary>
    [Fact]
    public void AScriptMayReturnOnceToAUrlTheLoadWasRedirectedAwayFrom()
    {
        var response = new PageLoadResult
        {
            FinalUrl = "https://site.test/login",
            Html = string.Empty,
            RedirectChain = [new Uri("https://site.test/app"), new Uri("https://site.test/login")],
        };
        IReadOnlyList<string> hopUrls = BrowserApp.HopUrls(response, response.FinalUrl);
        Assert.Equal(["https://site.test/app", "https://site.test/login"], hopUrls);

        var loads = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in hopUrls.Select(BrowserApp.NavigationPathKey))
            loads[path] = 1;

        var back = new NavigationRequest("https://site.test/app", NavigationKind.Replace);
        Assert.True(BrowserApp.ShouldFollow(back, response.FinalUrl, 0, loads));

        var here = new NavigationRequest("https://site.test/login", NavigationKind.Replace);
        Assert.False(BrowserApp.ShouldFollow(here, response.FinalUrl, 0, loads));

        loads["https://site.test/app"] = BrowserApp.SamePathLoadLimit;
        Assert.False(BrowserApp.ShouldFollow(back, response.FinalUrl, 0, loads));
        var again = new NavigationRequest("https://site.test/app?round=2", NavigationKind.Replace);
        Assert.False(BrowserApp.ShouldFollow(again, response.FinalUrl, 0, loads));

        // Somewhere new is still followed.
        var onward = new NavigationRequest("https://site.test/home", NavigationKind.Replace);
        Assert.True(BrowserApp.ShouldFollow(onward, response.FinalUrl, 0, loads));
    }

    /// <summary>
    /// A JS cookie challenge reached by a redirect: <c>/page</c> redirects to <c>/challenge</c> until
    /// the <c>ok</c> cookie is present, and the challenge's script sets it and sends the page back. The
    /// browser follows the return once, with the cookie, and shows the real page. A challenge that
    /// never lets go is followed only as far as the same-path budget.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void AChallengeReachedByARedirectReturnsToTheUrlItWasRedirectedFrom()
    {
        using var server = new LoopbackHttpServer();
        server
            .Map("/page", r => r.Cookie.Contains("ok=1", StringComparison.Ordinal)
                ? Reply.Text("<html><body><p>REAL-CONTENT</p></body></html>")
                : new Reply(Status: 302, Location: "/challenge"))
            .Map("/challenge", Reply.Text(
                "<html><body><p>CHALLENGE</p><script>document.cookie = 'ok=1; path=/'; location.replace('/page');</script></body></html>"))
            .Map("/loop", new Reply(Status: 302, Location: "/bounce"))
            .Map("/bounce", Reply.Text("<html><body><p>BOUNCE</p><script>location.replace('/loop');</script></body></html>"));
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();

        string painted = RunLoadPainted(profile, server.Url("/page"));
        Assert.Contains("REAL-CONTENT", painted);
        Assert.Contains(server.RequestsFor("/page"), r => r.Cookie.Contains("ok=1", StringComparison.Ordinal));

        string bounced = RunLoadPainted(profile, server.Url("/loop"));
        Assert.Contains("BOUNCE", bounced);
        Assert.InRange(server.RequestsFor("/loop").Length, 2, BrowserApp.SamePathLoadLimit);
    }

    /// <summary>
    /// A web page cannot open a local file: its script's <c>location.replace</c>, a refresh meta and a
    /// link all stay on the network, and the page loader refuses a local file a web document asks for,
    /// and a file on a share that anything but the user asks for. The user may still open one.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task AWebPageCannotNavigateToALocalFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "broiler-browser-tests", Guid.NewGuid().ToString("N"), "secret.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "<html><body><p>LOCAL-HTML-CONTENT</p></body></html>");
        string fileUrl = new Uri(path).AbsoluteUri;

        using var server = new LoopbackHttpServer();
        server
            .Map("/script", Reply.Text($"<html><body><p>WEB-PAGE</p><script>location.replace('{fileUrl}');</script></body></html>"))
            .Map("/refresh", Reply.Text($"<html><head><meta http-equiv=\"refresh\" content=\"0;url={fileUrl}\"></head><body><p>WEB-PAGE</p></body></html>"));
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();

        foreach (string page in new[] { "/script", "/refresh" })
        {
            string painted = RunLoadPainted(profile, server.Url(page));
            Assert.Contains("WEB-PAGE", painted);
            Assert.DoesNotContain("LOCAL-HTML-CONTENT", painted);
        }

        var web = DocumentRequestContext.CreateTopLevel(new Uri(server.Url("/script")));
        var local = DocumentRequestContext.CreateTopLevel(new Uri(fileUrl));
        Assert.False(BrowserApp.MayNavigateTo(web, fileUrl));
        Assert.True(BrowserApp.MayNavigateTo(local, fileUrl));
        Assert.True(BrowserApp.MayNavigateTo(null, fileUrl));
        Assert.True(BrowserApp.MayNavigateTo(web, server.Url("/refresh")));
        Assert.Null(BrowserApp.ToPageRequest(new NavigationRequest(fileUrl, NavigationKind.Assign) { Initiator = web }, string.Empty, server.Url("/script")));

        using var loader = new PageLoader(profile.Network);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => loader.LoadAsync(PageRequest.ForUrl(fileUrl) with { Initiator = web, NavigationType = PageNavigationType.Link }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => loader.LoadAsync(PageRequest.ForUrl("file://share-host.invalid/share/x.html") with { Initiator = local, NavigationType = PageNavigationType.Link }));
        Assert.Contains("LOCAL-HTML-CONTENT", (await loader.LoadAsync(PageRequest.ForUrl(fileUrl))).Html);
    }

    /// <summary>
    /// A page that animates while it loads is painted frame by frame, each frame a container of its
    /// own; its image is fetched once for all of them and for the finished page.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void EveryPaintedFrameOfALoadSharesOneImageFetch()
    {
        using var server = new LoopbackHttpServer();
        server
            .Map("/page", Reply.Text("""
                <!DOCTYPE html>
                <html><body><p id="n">start</p><img src="image.png" width="4" height="4">
                <script>
                var n = 0;
                (function tick() {
                    if (++n > 8) return;
                    document.getElementById('n').textContent = 'count-' + n;
                    var until = Date.now() + 40;
                    while (Date.now() < until) { }
                    setTimeout(tick, 20);
                })();
                </script>
                </body></html>
                """))
            .Map("/image.png", new Reply(ContentType: "image/png", Body: Pixel));
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();

        int frames = RunLoad(profile, server.Url("/page"));

        Assert.True(frames > 1, $"only {frames} frame(s) were painted, so nothing was shared");
        Assert.Single(server.RequestsFor("/image.png"));
    }

    /// <summary>
    /// A favorite saved as it was requested (<c>https://Example.com</c>) is the page the address bar
    /// now shows canonically (<c>https://example.com/</c>): its star reads saved, saving again adds
    /// nothing, and un-saving removes the old entry.
    /// </summary>
    [Fact]
    public void FavoritesMatchWhateverSpellingTheyWereSavedIn()
    {
        var favorites = new FavoritesManager(filePath: null);
        Assert.True(favorites.Add("https://Example.com"));

        Assert.True(favorites.Contains("https://example.com/"));
        Assert.False(favorites.Add("https://example.com/"));
        Assert.Single(favorites.Favorites);

        Assert.True(favorites.Remove("https://example.com/"));
        Assert.Empty(favorites.Favorites);

        Assert.True(favorites.Add("https://example.com/a"));
        Assert.False(favorites.Contains("https://example.com/A"));
    }

    [Fact]
    public void TheLoopbackServerNeverSharesItsLocalhostPortWithAnotherListener()
    {
        if (!Socket.OSSupportsIPv6)
            return;

        // Someone else holds [::1] at a port that is free on 127.0.0.1.
        var foreign = new TcpListener(IPAddress.IPv6Loopback, 0);
        foreign.Start();
        try
        {
            var port = ((IPEndPoint)foreign.LocalEndpoint).Port;
            var listeners = new List<TcpListener>();

            bool bound;
            try
            {
                bound = LoopbackHttpServer.TryBindLoopbackPair(port, listeners, out _);
            }
            catch (SocketException)
            {
                // 127.0.0.1 at that port is taken as well; nothing to show on this machine.
                return;
            }

            Assert.False(bound);
            Assert.Empty(listeners);
        }
        finally
        {
            foreign.Stop();
        }
    }

    /// <summary>Loads <paramref name="url"/> in a whole browser and answers the text of the last frame it painted.</summary>
    private static string RunLoadPainted(BrowserProfile profile, string url)
    {
        var posted = new ConcurrentQueue<Action>();
        using BrowserUiHost host = new(
            static () => new BSize(800, 600),
            static () => 1.0,
            static () => { },
            static _ => { },
            action => { posted.Enqueue(action); return true; });

        using BImageRenderer renderer = new();
        using BrowserApp app = new(host, () => renderer, url, static _ => { }, profile);

        string painted = string.Empty;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            while (posted.TryDequeue(out Action? action))
                action();

            if (app.HasPendingWork)
                app.StepAnimation();

            if (host.IsInvalidated)
                painted = string.Concat(app.RenderFrame().Commands.OfType<BRenderCommand.DrawText>().Select(c => c.Text.Text));

            if (string.Equals(app.Status, "Done", StringComparison.Ordinal) && posted.IsEmpty && !host.IsInvalidated)
                break;

            Thread.Sleep(5);
        }

        Assert.Equal("Done", app.Status);
        return painted;
    }

    /// <summary>Loads <paramref name="url"/> in a whole browser, painting every frame it offers; answers how many were painted.</summary>
    private static int RunLoad(BrowserProfile profile, string url)
    {
        var posted = new ConcurrentQueue<Action>();
        using BrowserUiHost host = new(
            static () => new BSize(800, 600),
            static () => 1.0,
            static () => { },
            static _ => { },
            action => { posted.Enqueue(action); return true; });

        using BImageRenderer renderer = new();
        using BrowserApp app = new(host, () => renderer, url, static _ => { }, profile);

        int frames = 0;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            while (posted.TryDequeue(out Action? action))
                action();

            if (app.HasPendingWork)
                app.StepAnimation();

            if (host.IsInvalidated)
            {
                _ = app.RenderFrame();
                frames++;
            }

            if (string.Equals(app.Status, "Done", StringComparison.Ordinal) && posted.IsEmpty && !host.IsInvalidated)
                break;

            Thread.Sleep(5);
        }

        Assert.Equal("Done", app.Status);
        return frames;
    }
}
