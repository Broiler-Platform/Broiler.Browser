using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Dom;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// <c>form.submit()</c>: the navigation whose target the bridge cannot finish computing.
/// <para>
/// The bridge names the form by position and resolves its <c>action</c>; the host serializes the
/// data set with the same <c>HtmlFormSerializer</c> a click or an Enter key goes through. These
/// cover the seam — that the position survives the trip from one document to a re-parse of it, and
/// that what comes out the far end is the request the form describes.
/// </para>
/// </summary>
public class FormSubmitNavigationTests
{
    private const string PageUrl = "https://example.test/page";

    private static NavigationRequest? PendingAfter(string html, string script)
    {
        using var session = new ScriptEngine().ExecuteInteractive([script], [], html, PageUrl);
        Assert.NotNull(session);
        return session!.TakePendingNavigation();
    }

    [Fact]
    public void SubmitAsksForTheFormByPositionAndNamesItsAction()
    {
        const string html = """
            <html><body><form id="f" action="/search" method="get">
            <input name="q" value="test"></form></body></html>
            """;

        var pending = PendingAfter(html, "document.getElementById('f').submit();");

        Assert.NotNull(pending);
        Assert.Equal(NavigationKind.FormSubmit, pending!.Kind);
        Assert.Equal(0, pending.FormIndex);
        Assert.Equal("https://example.test/search", pending.Url);
    }

    [Fact]
    public void TheRightFormIsNamedWhenTheDocumentHasSeveral()
    {
        // Position is the whole identity, so counting has to agree with the host's re-parse. A form
        // with no id or name has nothing else to be found by.
        const string html = """
            <html><body>
            <form action="/first"><input name="a"></form>
            <form action="/second"><input name="b"></form>
            <form action="/third"><input name="c"></form>
            </body></html>
            """;

        var pending = PendingAfter(html, "document.forms[2].submit();");

        Assert.Equal(2, pending?.FormIndex);
        Assert.Equal("https://example.test/third", pending!.Url);
    }

    [Fact]
    public void AFormWithNoActionSubmitsToItsOwnPage()
    {
        const string html = "<html><body><form id=\"f\"><input name=\"q\" value=\"x\"></form></body></html>";

        Assert.Equal(PageUrl, PendingAfter(html, "document.getElementById('f').submit();")?.Url);
    }

    [Fact]
    public void PreventDefaultOnTheSubmitListenerStopsIt()
    {
        // The default action used to be nothing, which made preventDefault() a no-op cancelling a
        // no-op. It is now the difference between the form going and staying.
        const string html = "<html><body><form id=\"f\" action=\"/go\"><input name=\"q\"></form></body></html>";
        const string script = """
            var f = document.getElementById('f');
            f.addEventListener('submit', function (e) { e.preventDefault(); });
            f.submit();
            """;

        Assert.Null(PendingAfter(html, script));
    }

    [Fact]
    public void AFormNeverPutInTheDocumentAsksForNothing()
    {
        // Position would name a form the host cannot find, so nothing is asked for at all.
        const string html = "<html><body></body></html>";

        Assert.Null(PendingAfter(html, "document.createElement('form').submit();"));
    }

    [Fact]
    public void AGetSubmissionBecomesTheActionWithTheDataSetInItsQuery()
    {
        const string html = """
            <html><body><form action="/search" method="get">
            <input name="q" value="a b"><input name="hl" value="de"></form></body></html>
            """;
        var pending = new NavigationRequest("https://example.test/search", NavigationKind.FormSubmit) { FormIndex = 0 };

        PageRequest? request = BrowserApp.ToPageRequest(pending, html, PageUrl);

        Assert.Equal("https://example.test/search?q=a+b&hl=de", request!.Url);
        Assert.Equal(PageRequest.Get, request.Method);
        Assert.False(request.HasBody);
    }

    [Fact]
    public void APostSubmissionCarriesTheDataSetAsABody()
    {
        const string html = """
            <html><body><form action="/login" method="post">
            <input name="user" value="me"></form></body></html>
            """;
        var pending = new NavigationRequest("https://example.test/login", NavigationKind.FormSubmit) { FormIndex = 0 };

        PageRequest? request = BrowserApp.ToPageRequest(pending, html, PageUrl);

        Assert.Equal("https://example.test/login", request!.Url);
        Assert.Equal(PageRequest.Post, request.Method);
        Assert.Equal(PageRequest.FormUrlEncoded, request.ContentType);
        Assert.Equal("user=me", request.Body);
    }

    [Fact]
    public void AMultipartFormPostsBytesRatherThanAString()
    {
        const string html = """
            <html><body><form action="/upload" method="post" enctype="multipart/form-data">
            <input name="note" value="hi"></form></body></html>
            """;
        var pending = new NavigationRequest("https://example.test/upload", NavigationKind.FormSubmit) { FormIndex = 0 };

        PageRequest? request = BrowserApp.ToPageRequest(pending, html, PageUrl);

        Assert.StartsWith("multipart/form-data; boundary=", request!.ContentType);
        Assert.NotNull(request.BinaryBody);
    }

    [Fact]
    public void AFormThePageNoLongerHasYieldsNoRequest()
    {
        // The bridge asked for form 4 and the host's document has one. Refusing beats submitting
        // whichever form happens to be there instead.
        var pending = new NavigationRequest(PageUrl, NavigationKind.FormSubmit) { FormIndex = 4 };

        Assert.Null(BrowserApp.ToPageRequest(pending, "<html><body><form></form></body></html>", PageUrl));
    }

    [Fact]
    public void ASubmissionToTheDocumentsOwnUrlIsStillFollowed()
    {
        // The guard that refuses a repeat of the loaded document must not catch this: a form with no
        // action submits to its own page, and the request is not the one already made.
        var pending = new NavigationRequest(PageUrl, NavigationKind.FormSubmit) { FormIndex = 0 };

        Assert.True(BrowserApp.ShouldFollow(
            pending, PageUrl, 0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)));
    }
}
