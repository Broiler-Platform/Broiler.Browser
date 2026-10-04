namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The window styles a page for its own page area, and scrolls to what a URL's fragment names.
/// </summary>
/// <remarks>
/// <para>
/// A page is parsed on the load worker, before the window lays it out, and its media queries are
/// resolved during that parse. The container had no viewport yet and answered its 99999px default, so
/// every page was styled for a 99999px screen: MediaWiki's skin laid its widest-screen grid into the
/// window. <see cref="BrowserViewport.PageAreaSize"/> is what the worker passes now.
/// </para>
/// <para>
/// Nothing scrolled to a fragment. Acid2's "Take The Acid2 Test" link, <c>href="#top"</c>, reloaded
/// the page at the top and left the test's face far below the window.
/// </para>
/// </remarks>
public class WindowFragmentAndViewportTests
{
    /// <summary>
    /// The window is 800px wide. A query no wider than 2000px matches it and one of at least 5000px
    /// does not; styled for 99999px it was the other way round.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Page_Is_Styled_For_The_Window_It_Is_Shown_In()
    {
        const string Page = """
            <!DOCTYPE html><html><head><style>
            #narrow, #wide { display: none }
            @media (max-width: 2000px) { #narrow { display: block } }
            @media (min-width: 5000px) { #wide { display: block } }
            </style></head><body><p id="narrow">narrowquery</p><p id="wide">widequery</p></body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", LoopbackHttpServer.Reply.Text(Page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        var painted = string.Concat(window.Texts().Select(static t => t.Text));
        Assert.Contains("narrowquery", painted, StringComparison.Ordinal);
        Assert.DoesNotContain("widequery", painted, StringComparison.Ordinal);
    }

    /// <summary>A page navigated to with a fragment opens scrolled to the element it names.</summary>
    [Fact(Timeout = 600000)]
    public void A_Page_Loaded_With_A_Fragment_Opens_At_Its_Element()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", LoopbackHttpServer.Reply.Text(TallPage));
        using var window = new TestWindow(server.Url("/page#target"));
        window.Settle();

        Assert.True(window.IsInView("targetmarker"), window.Describe());
        Assert.False(window.IsInView("topmarker"), window.Describe());
    }

    /// <summary>
    /// A link into the document on screen scrolls it to the element and loads nothing: the page is
    /// requested once, before the click and not after.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Link_To_A_Fragment_Scrolls_Without_Loading_The_Page_Again()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", LoopbackHttpServer.Reply.Text(TallPage));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        Assert.True(window.IsInView("topmarker"), window.Describe());

        // On the link's own text, where the window draws it.
        var link = window.Texts().First(static t => t.Text.Contains("topmarker", StringComparison.Ordinal)).At;
        window.Click(link.X + 4, link.Y + 4);
        window.Settle();

        Assert.True(window.IsInView("targetmarker"), window.Describe());
        Assert.Single(server.RequestsFor("/page"));
    }

    private const string TallPage = """
        <!DOCTYPE html><html><body style="margin: 0">
        <a href="#target">topmarker</a>
        <div style="height: 3000px"></div>
        <p id="target">targetmarker</p>
        <div style="height: 3000px"></div>
        </body></html>
        """;
}
