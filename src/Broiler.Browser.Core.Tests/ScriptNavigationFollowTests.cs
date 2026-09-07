using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HtmlBridge.Dom;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The browser's side of a script navigation: which requests it follows, and when it stops.
/// <para>
/// Stopping is the half with teeth. Following a page that re-submits itself is not merely wasted
/// work — google.de answered <c>429 Too Many Requests</c> to a chain that ran to the hop cap, and a
/// rate limiter is the polite version of what a server does about traffic like that. The guard is
/// pure, so it is tested directly rather than through a load.
/// </para>
/// </summary>
public class ScriptNavigationFollowTests
{
    private const string SearchPath = "https://www.google.de/search";

    private static bool Follows(
        NavigationRequest? navigation,
        string currentUrl,
        out PageRequest? next,
        int hop = 0,
        Dictionary<string, int>? loadsPerPath = null) =>
        BrowserApp.TryFollowScriptNavigation(
            navigation,
            currentUrl,
            hop,
            loadsPerPath ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            out next);

    private static Dictionary<string, int> Loaded(string path, int times) =>
        new(StringComparer.OrdinalIgnoreCase) { [path] = times };

    [Fact]
    public void NothingRequestedIsNothingFollowed()
    {
        Assert.False(Follows(null, SearchPath, out _));
    }

    [Fact]
    public void ASearchIsFollowed()
    {
        // The case the whole feature exists for: the homepage hands over to the results page.
        var pending = new NavigationRequest($"{SearchPath}?q=test", NavigationKind.Replace);

        Assert.True(Follows(pending, "https://www.google.de/", out PageRequest? next));
        Assert.Equal($"{SearchPath}?q=test", next!.Url);
        Assert.Equal(PageRequest.Get, next.Method);
    }

    [Fact]
    public void ARepeatOfTheDocumentAlreadyLoadedIsNotFollowed()
    {
        var pending = new NavigationRequest($"{SearchPath}?q=test", NavigationKind.Assign);

        Assert.False(Follows(pending, $"{SearchPath}?q=test", out _));
    }

    [Fact]
    public void ReloadOfTheDocumentAlreadyLoadedIsFollowed()
    {
        // Asking for the current document is the entire meaning of reload(), so the check above
        // must not swallow it.
        var pending = new NavigationRequest($"{SearchPath}?q=test", NavigationKind.Reload);

        Assert.True(Follows(pending, $"{SearchPath}?q=test", out PageRequest? next));
        Assert.Equal($"{SearchPath}?q=test", next!.Url);
    }

    [Fact]
    public void APageReSubmittingItselfWithAFreshTokenIsFollowedUpToTheBudget()
    {
        // Google's bootstrap re-navigates to the page it is already on with one more token in the
        // query each round — `sei`, then a `sg_ss` signal blob. Every hop is a URL nobody has seen,
        // so exact-URL equality never fires; the path is what repeats.
        var pending = new NavigationRequest($"{SearchPath}?q=test&sei=second", NavigationKind.Replace);

        Assert.True(Follows(pending, $"{SearchPath}?q=test", out _, loadsPerPath: Loaded(SearchPath, 1)));
        Assert.True(Follows(pending, $"{SearchPath}?q=test", out _, loadsPerPath: Loaded(SearchPath, BrowserApp.SamePathLoadLimit - 1)));
    }

    [Fact]
    public void APageReSubmittingItselfIsAbandonedOnceTheBudgetIsSpent()
    {
        // This is the 429: the round never converges for this engine, and following it to the hop
        // cap is a burst of requests at one endpoint.
        var pending = new NavigationRequest($"{SearchPath}?q=test&sg_ss=blob", NavigationKind.Replace);

        Assert.False(Follows(pending, $"{SearchPath}?q=test&sei=x", out _, loadsPerPath: Loaded(SearchPath, BrowserApp.SamePathLoadLimit)));
    }

    [Fact]
    public void AChainThatMovesOnIsNotChargedForAPathItLeft()
    {
        // The budget is per path, so a spent one must not block a genuine hop elsewhere — otherwise
        // the guard would break the redirect chains it is meant to allow.
        var pending = new NavigationRequest("https://www.google.de/sorry/index", NavigationKind.Replace);

        Assert.True(Follows(pending, $"{SearchPath}?q=test", out _, loadsPerPath: Loaded(SearchPath, 99)));
    }

    [Fact]
    public void TheHopCapStopsAChainThatKeepsMovingOn()
    {
        var pending = new NavigationRequest("https://www.google.de/somewhere-new", NavigationKind.Replace);

        Assert.False(Follows(pending, SearchPath, out _, hop: BrowserApp.MaxScriptNavigations));
    }

    [Theory]
    [InlineData("https://www.google.de/search?q=test#frag", "https://www.google.de/search")]
    [InlineData("https://www.google.de/search", "https://www.google.de/search")]
    [InlineData("https://www.google.de/", "https://www.google.de/")]
    public void ThePathKeyDropsTheQueryAndTheFragment(string url, string expected)
    {
        Assert.Equal(expected, BrowserApp.NavigationPathKey(url));
    }

    [Fact]
    public void ThePathKeyLeavesAnUnparseableUrlAlone()
    {
        // Better a key that only ever matches itself than one that collapses unrelated targets.
        Assert.Equal("not a url", BrowserApp.NavigationPathKey("not a url"));
    }
}
