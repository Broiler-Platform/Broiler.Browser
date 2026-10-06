using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A page's script clicking its own controls gets what a user's click gets: a submit button submits its
/// form, and a link is followed, by the window.
/// </summary>
/// <remarks>
/// A script's <c>click()</c> on a submit button fired an untrusted <c>submit</c> of its own and handed the
/// window nothing, and one on a link did nothing at all, so a page whose own button or link forwards its
/// click to another -- a styled control standing in for the real one -- went nowhere.
/// </remarks>
public class WindowScriptActivationTests
{
    private const string Proxy =
        "<div id=\"proxy\" style=\"position: absolute; left: 0; top: 100px; width: 80px; height: 30px\">Proxy</div>";

    private static void ClickPage(TestWindow window, double x, double y)
    {
        window.Click(window.PageArea.Left + x, window.PageArea.Top + y);
        window.Settle();
    }

    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    /// <summary>
    /// A button the user clicks whose listener clicks the form's submit button submits the form: the page
    /// hears a trusted <c>submit</c> naming that button, and the window follows the submission.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Scripts_Click_On_A_Submit_Button_Submits_Its_Form()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/sent", Reply.Text("<!DOCTYPE html><html><body><p>sentmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<form id=\"f\" action=\"/sent\"><input name=\"q\" value=\"x\"><button id=\"go\" style=\"display: none\">Go</button></form>" + Proxy +
            "<script>document.getElementById('proxy').addEventListener('click', function () { document.getElementById('go').click(); });" +
            "document.getElementById('f').addEventListener('submit', function (e) { document.cookie = 'heard=' + e.submitter.id + '-' + e.isTrusted; });</script>" +
            "</body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 115);

        Assert.Contains("sentmarker", Painted(window));
        var sent = Assert.Single(server.RequestsFor("/sent"));
        Assert.Contains("heard=go-true", sent.Cookie);
    }

    /// <summary>A link a script clicks is followed.</summary>
    [Fact(Timeout = 600000)]
    public void A_Scripts_Click_On_A_Link_Follows_It()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/elsewhere", Reply.Text("<!DOCTYPE html><html><body><p>elsewheremarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\"><a id=\"away\" href=\"/elsewhere\" style=\"display: none\">away</a>" + Proxy +
            "<script>document.getElementById('proxy').addEventListener('click', function () { document.getElementById('away').click(); });</script>" +
            "</body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 115);

        Assert.Contains("elsewheremarker", Painted(window));
        Assert.Single(server.RequestsFor("/elsewhere"));
    }
}
