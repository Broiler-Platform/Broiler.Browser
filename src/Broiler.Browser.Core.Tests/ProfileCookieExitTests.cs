using System.Collections.Concurrent;
using System.Text;
using Broiler.App.Rendering;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;
using Reply = Broiler.Browser.Core.Tests.LoopbackHttpServer.Reply;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The cookie a navigation receives reaches every sub-resource of the page through the loader that
/// really fetches it, as the browser window runs them — and nothing more than the rules allow.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole browser, not its parts.</b> The page is loaded by a <see cref="BrowserApp"/> on an
/// ephemeral <see cref="BrowserProfile"/>, driven the way a windowed host drives it: the page loader
/// navigates, the pipeline's extractor fetches the classic script and the module, the script engine's
/// bridge fetches the module's import, the linked stylesheet and the frame, and the renderer's
/// container fetches the stylesheet, the <c>@font-face</c> font and the images while it parses and
/// lays the page out. Each of those loaders once kept a cookie jar of its own, or none; every one of
/// them now sends through the profile's one session.
/// </para>
/// <para>
/// <b>The navigation is redirected</b>, and the page's references are relative, so a loader that
/// resolved them against the URL first asked for rather than the one the document came from would ask
/// for the wrong paths. The page's response sets an <c>HttpOnly</c> cookie and an ordinary one.
/// </para>
/// </remarks>
public class ProfileCookieExitTests
{
    private const string PageCookies = "sid=s3cret; visible=yes";

    // A 1x1 PNG, so the renderer has a real image to decode.
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact(Timeout = 600000)]
    public async Task ANavigationCookieReachesEverySubresourceOfThePageAndNoFurther()
    {
        using var server = new LoopbackHttpServer();
        MapSite(server);
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();

        // The second site's cookies, set by visiting it from the address bar first: one SameSite=Lax,
        // one SameSite=None. The page on the first site then embeds that site's resources.
        _ = await new PageLoader(profile.Network).LoadAsync(PageRequest.ForUrl(server.LocalhostUrl("/other/seed")));

        string painted = RunLoad(profile, server.Url("/enter"));

        // Every request the page made to its own site carried both of the navigation's cookies —
        // the HttpOnly one included, because these are HTTP requests. The only exceptions are the
        // two requests of the navigation itself, which came before the cookies existed.
        LoopbackHttpServer.Request[] firstParty = server.Requests
            .Where(request => request.Host.StartsWith("127.0.0.1:", StringComparison.Ordinal))
            .Where(request => request.Path is not ("/enter" or "/site/page"))
            .ToArray();
        foreach (LoopbackHttpServer.Request request in firstParty)
            Assert.True(request.Cookie == PageCookies, $"{request.Target} was sent Cookie=[{request.Cookie}]{Environment.NewLine}{server.Log()}");

        // ...and the page's every loader asked, each at the path the final URL resolves to.
        foreach (string path in new[]
        {
            "/site/classic.js",   // the extractor, classic script
            "/site/module.js",    // the extractor, module root
            "/site/dep.js",       // the bridge's module loader, static import
            "/site/style.css",    // the renderer's and the bridge's stylesheet loaders
            "/site/face.woff",    // the renderer's @font-face loader
            "/site/image.png",    // the renderer's image loader
            "/site/frame.html",   // the bridge's frame loader, a nested navigation
        })
        {
            Assert.True(server.RequestsFor(path).Length > 0, $"{path} was never requested:{Environment.NewLine}{server.Log()}");
        }

        // The second site's image and script are cross-site requests of this page: its
        // SameSite=None cookie goes with them, its SameSite=Lax one does not.
        foreach (string path in new[] { "/other/image.png", "/other/script.js" })
        {
            LoopbackHttpServer.Request[] requests = server.RequestsFor(path);
            Assert.True(requests.Length > 0, $"{path} was never requested:{Environment.NewLine}{server.Log()}");
            Assert.All(requests, request =>
            {
                Assert.StartsWith("localhost:", request.Host, StringComparison.Ordinal);
                Assert.Equal("other_none=1", request.Cookie);
            });
        }

        // document.cookie is the non-HTTP API: it sees the ordinary cookie and never the HttpOnly one.
        Assert.Contains("jar[visible=yes]", painted, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", painted, StringComparison.Ordinal);

        // One store: what the navigation set is in the profile.
        Assert.Contains(profile.Cookies.Snapshot(), cookie => cookie.Name == "sid" && cookie.HttpOnly);
    }

    private static void MapSite(LoopbackHttpServer server)
    {
        string otherSite = server.LocalhostUrl("/other");

        server
            .Map("/enter", new Reply(302, Location: "/site/page"))
            .Map("/site/page", Reply.Text(
                $$"""
                <!DOCTYPE html>
                <html><head>
                <link rel="stylesheet" href="style.css">
                <style>@font-face { font-family: ExitFace; src: url(face.woff); } p { font-family: ExitFace, sans-serif; }</style>
                <script src="classic.js"></script>
                <script type="module" src="module.js"></script>
                <script src="{{otherSite}}/script.js"></script>
                </head><body>
                <p id="jar">jar-pending</p>
                <img src="image.png" width="4" height="4">
                <img src="{{otherSite}}/image.png" width="4" height="4">
                <iframe id="frame" src="frame.html"></iframe>
                <script>
                document.getElementById('jar').textContent = 'jar[' + document.cookie + ']';
                // Touching the frame's document is what makes the bridge load it.
                var frameDocument = document.getElementById('frame').contentDocument;
                </script>
                </body></html>
                """,
                setCookies: ["sid=s3cret; HttpOnly; SameSite=Lax; Path=/", "visible=yes; SameSite=Lax; Path=/"]))
            .Map("/site/style.css", Reply.Text("p { color: rgb(1, 2, 3); }", "text/css"))
            .Map("/site/classic.js", Reply.Text("window.classicRan = true;", "text/javascript"))
            .Map("/site/module.js", Reply.Text("import { dep } from './dep.js'; window.moduleRan = dep;", "text/javascript"))
            .Map("/site/dep.js", Reply.Text("export const dep = 1;", "text/javascript"))
            .Map("/site/face.woff", new Reply(ContentType: "font/woff", Body: Encoding.ASCII.GetBytes("not really a font")))
            .Map("/site/image.png", new Reply(ContentType: "image/png", Body: Pixel))
            .Map("/site/frame.html", Reply.Text("<html><body><p>frame</p></body></html>"))
            .Map("/other/seed", Reply.Text("<p>other</p>", setCookies:
            [
                "other_lax=1; SameSite=Lax; Path=/",
                "other_none=1; SameSite=None; Secure; Path=/",
            ]))
            .Map("/other/image.png", new Reply(ContentType: "image/png", Body: Pixel))
            .Map("/other/script.js", Reply.Text("window.otherRan = true;", "text/javascript"));
    }

    /// <summary>
    /// Runs <paramref name="url"/> in a browser window on <paramref name="profile"/>, the way a windowed
    /// host does — drain posted work, step, paint — until the page reports itself done, and returns
    /// the text of the last frame painted.
    /// </summary>
    private static string RunLoad(BrowserProfile profile, string url)
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
                painted = TextOf(app.RenderFrame());

            if (string.Equals(app.Status, "Done", StringComparison.Ordinal) && posted.IsEmpty && !host.IsInvalidated)
                break;

            Thread.Sleep(5);
        }

        Assert.Equal("Done", app.Status);
        return painted;
    }

    private static string TextOf(BRenderList frame) =>
        string.Concat(frame.Commands.OfType<BRenderCommand.DrawText>().Select(command => command.Text.Text));
}
