using Broiler.App.Rendering;
using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Scripting;
using Reply = Broiler.Browser.Core.Tests.LoopbackHttpServer.Reply;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A page's performance timeline as the window hands it to the script engine: measured from the
/// navigation's start, with the scripts the window fetched for the page before running it.
/// </summary>
/// <remarks>
/// The window fetches a page's parser-inserted scripts itself, before the engine has the page, so the
/// engine knows of those fetches only from the records the window passes on. Without them a page that
/// looks its own scripts up in <c>performance.getEntriesByType('resource')</c> -- reCAPTCHA does --
/// found nothing, and its navigation entry's fetch read 0.
/// </remarks>
public class DocumentTimingTests
{
    /// <summary>
    /// The page's timeline has the script the window fetched: a <c>script</c> in the head that held up
    /// rendering, after the navigation's start. The navigation entry's response ended after it too.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task ThePagesTimelineHasTheScriptsTheWindowFetched()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/app.js", Reply.Text("window.appRan = true;", "text/javascript"));
        server.Map("/page", Reply.Text(
            "<html><head><script src=\"/app.js\"></script></head><body><div id=\"out\"></div><script>" +
            "var app = performance.getEntriesByType('resource').filter(function (e) { return /\\/app\\.js$/.test(e.name); })[0];" +
            "document.getElementById('out').textContent = [app.initiatorType, app.renderBlockingStatus, app.startTime > 0," +
            "  app.responseEnd >= app.startTime, performance.getEntriesByType('navigation')[0].responseEnd > 0, window.appRan].join();" +
            "</script></body></html>"));
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();
        using var pipeline = new RenderingPipeline(new PageLoader(profile.Network), new ScriptEngine(), profile.Network);

        LoadedPage page = await pipeline.LoadAsync(PageRequest.ForUrl(server.Url("/page")));
        using InteractiveSession? session = pipeline.ExecuteScriptsInteractive(page.Content, page.InheritedPolicy, page);

        Assert.NotNull(session);
        Assert.Contains("<div id=\"out\">script,blocking,true,true,true,true</div>", session.SettleLoadWindow());
    }
}
