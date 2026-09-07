using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HtmlBridge.Dom;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// <c>&lt;meta http-equiv="refresh"&gt;</c>: the navigation a document declares in markup.
/// <para>
/// It reaches the host as the same <see cref="NavigationRequest"/> a script navigation does, so
/// what is specific to it is the reading — a <c>content</c> attribute whose forms vary more than
/// its one job suggests — and the wait, which is the only part of a navigation request a host has
/// to weigh rather than act on.
/// </para>
/// </summary>
public class MetaRefreshDiscoveryTests
{
    private const string PageUrl = "https://example.test/interstitial";

    private static NavigationRequest? Parse(string content)
    {
        MetaRefreshDiscovery.TryParseContent(content, PageUrl, out var request);
        return request;
    }

    [Theory]
    [InlineData("0;url=/next")]
    [InlineData("0; url=/next")]
    [InlineData("0,url=/next")]
    [InlineData("0, URL=/next")]
    [InlineData("0;url='/next'")]
    [InlineData("0;url=\"/next\"")]
    [InlineData("  0 ; url = /next  ")]
    [InlineData("0.0;url=/next")]
    public void TheFormsDocumentsActuallyUseAllRead(string content)
    {
        // One job, and every separator, casing and quoting style in the wild for it.
        var request = Parse(content);

        Assert.NotNull(request);
        Assert.Equal("https://example.test/next", request!.Url);
        Assert.Equal(NavigationKind.MetaRefresh, request.Kind);
        Assert.Equal(TimeSpan.Zero, request.Delay);
    }

    [Fact]
    public void TheDelayIsCarriedRatherThanActedOn()
    {
        // The binding does not decide whether to wait; it reports what was asked. See
        // TheHostDeclinesAWaitItCannotHonour.
        Assert.Equal(TimeSpan.FromSeconds(30), Parse("30;url=/next")!.Delay);
    }

    [Fact]
    public void AnAbsoluteTargetIsLeftAlone()
    {
        Assert.Equal("https://other.test/page", Parse("0;url=https://other.test/page")!.Url);
    }

    [Fact]
    public void NoTargetIsTheDocumentAskingForItself()
    {
        // A page that polls by reloading. It still reaches the host, which refuses it as the
        // document already loaded rather than pretending it was never asked for.
        Assert.Equal(PageUrl, Parse("5")!.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("url=/next")]
    [InlineData("soon;url=/next")]
    public void ContentWithoutATimeIsNotARefresh(string content)
    {
        // The time is the one part the syntax requires. A `content` without one is likelier to
        // belong to a different http-equiv than to be a refresh someone meant.
        Assert.Null(Parse(content));
    }

    [Fact]
    public void TheMetaIsFoundInADocumentWithNoScriptAtAll()
    {
        // The reason discovery is not on the bridge: a page with no scripts never gets one, and a
        // refresh interstitial is usually exactly that page.
        const string html = """
            <html><head><meta http-equiv="Refresh" content="0; url=/destination"></head>
            <body>Redirecting…</body></html>
            """;

        var request = MetaRefreshDiscovery.Find(html, PageUrl);

        Assert.Equal("https://example.test/destination", request?.Url);
    }

    [Fact]
    public void AMetaInACommentIsNotAMeta()
    {
        // Tokenizer output rather than a regex scan, which is what makes this true.
        const string html = "<html><head><!-- <meta http-equiv=\"refresh\" content=\"0;url=/nope\"> --></head></html>";

        Assert.Null(MetaRefreshDiscovery.Find(html, PageUrl));
    }

    [Fact]
    public void ADocumentWithoutOneDeclaresNothing()
    {
        Assert.Null(MetaRefreshDiscovery.Find("<html><head><meta charset=\"utf-8\"></head></html>", PageUrl));
    }

    [Fact]
    public void TheFirstRefreshWins()
    {
        const string html = """
            <meta http-equiv="refresh" content="0;url=/first">
            <meta http-equiv="refresh" content="0;url=/second">
            """;

        Assert.Equal("https://example.test/first", MetaRefreshDiscovery.Find(html, PageUrl)?.Url);
    }

    [Fact]
    public void TheHostFollowsAPromptRefresh()
    {
        var request = new NavigationRequest("https://example.test/next", NavigationKind.MetaRefresh, TimeSpan.Zero);

        Assert.True(BrowserApp.TryFollowNavigation(
            request, PageUrl, 0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), out PageRequest? next));
        Assert.Equal("https://example.test/next", next!.Url);
    }

    [Fact]
    public void TheHostDeclinesAWaitItCannotHonour()
    {
        // Following a thirty-second notice immediately would take away the thing it exists to show.
        // Nothing here schedules one for later, so it is declined rather than deferred.
        var request = new NavigationRequest(
            "https://example.test/next",
            NavigationKind.MetaRefresh,
            BrowserApp.MetaRefreshFollowLimit + TimeSpan.FromSeconds(1));

        Assert.False(BrowserApp.TryFollowNavigation(
            request, PageUrl, 0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), out _));
    }

    [Fact]
    public void ARefreshChainIsBoundedLikeAnyOther()
    {
        // Meta refresh reaches the host as an ordinary NavigationRequest, so the loop guards that
        // stopped the search re-submission apply to a refresh loop without knowing it is one.
        var request = new NavigationRequest("https://example.test/interstitial?n=2", NavigationKind.MetaRefresh);

        Assert.False(BrowserApp.TryFollowNavigation(
            request,
            PageUrl,
            0,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [BrowserApp.NavigationPathKey(PageUrl)] = BrowserApp.SamePathLoadLimit,
            },
            out _));
    }
}
