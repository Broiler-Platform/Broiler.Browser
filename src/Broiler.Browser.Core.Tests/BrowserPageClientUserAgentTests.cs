using Broiler.App.Rendering;
using Broiler.Net.Http;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The browser window's own navigations are identified, not just the CLI's captures.
/// </summary>
/// <remarks>
/// Both entry points had the same defect and only one of them is exercised by a capture test: a
/// <c>User-Agent</c> added to <c>CaptureService</c> alone would leave <c>Broiler.App</c> answering
/// <c>403 Forbidden</c> on <c>https://www.mediawiki.org/wiki/MediaWiki</c> exactly as reported. The
/// browser sends every navigation through its profile's network session, so asserting on what that
/// session puts on the wire is asserting on every page the window will ever load.
/// </remarks>
public class BrowserPageClientUserAgentTests
{
    [Fact(Timeout = 600000)]
    public async Task The_Profile_Network_Sends_A_User_Agent()
    {
        using var server = new LoopbackHttpServer()
            .Map("/page", LoopbackHttpServer.Reply.Text("<html><body>ok</body></html>"));
        using BrowserProfile profile = BrowserProfile.CreateEphemeral();
        using PageLoader loader = new(profile.Network);

        _ = await loader.LoadAsync(PageRequest.ForUrl(server.Url("/page")));

        // The header as it arrived, not as a client object holds it: the session composes its own
        // messages, so only the wire says what a server sees.
        Assert.Equal(BroilerUserAgent.Value, Assert.Single(server.RequestsFor("/page")).Header("User-Agent"));
    }
}
