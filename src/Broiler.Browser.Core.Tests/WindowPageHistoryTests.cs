using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The page's session history, its <c>javascript:</c> links, its scroll and its submissions, as the window
/// carries them: the address it shows and its back button follow the page's <c>pushState</c>, a
/// <c>javascript:</c> link runs in the page, the page's scroll and the window's follow each other, and a
/// submission carries its button and what its <c>formdata</c> listeners added.
/// </summary>
/// <remarks>
/// <para>
/// <b>None of these reached the window.</b> A page's <c>pushState</c> left the address and the back button
/// alone, so back left a single-page site altogether; a <c>javascript:</c> link was a navigation to an
/// error page; <c>scrollTo</c> moved nothing on screen and the page never knew where the user had scrolled;
/// a submission named neither its button nor what its listeners added; and a dialog's form navigated.
/// </para>
/// <para>
/// Markers are assembled at run time, so a marker in a script's source is never the one found painted.
/// </para>
/// </remarks>
public class WindowPageHistoryTests
{
    private const string Out = "position: absolute; left: 0; top: 0; margin: 0";

    private const string Button = "position: absolute; left: 0; top: 40px; width: 80px; height: 30px";

    private static void ClickPage(TestWindow window, double x, double y)
    {
        window.Click(window.PageArea.Left + x, window.PageArea.Top + y);
        window.Settle();
    }

    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    /// <summary>
    /// A page's <c>pushState</c> moves the window's address, and the window's back button goes to the page's
    /// previous entry -- the page hears <c>popstate</c> and nothing is loaded again.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_PushState_Is_The_Windows_Entry_Too()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><div id=\"go\" style=\"{Button}\">Go</div><script>" +
            "var out = document.getElementById('out');" +
            "document.getElementById('go').addEventListener('click', function () { history.pushState({ n: 1 }, '', '/page/two'); out.textContent = 'pushed' + 'marker ' + location.pathname; });" +
            "addEventListener('popstate', function (e) { out.textContent = 'popped' + 'marker ' + location.pathname + ' ' + JSON.stringify(e.state); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("pushedmarker /page/two", Painted(window));
        Assert.EndsWith("/page/two", window.Address);

        Assert.True(window.GoBack());
        window.Settle();

        Assert.Contains("poppedmarker /page null", Painted(window));
        Assert.EndsWith("/page", window.Address);
        Assert.Single(server.RequestsFor("/page"));
    }

    /// <summary>A <c>javascript:</c> link the user clicks runs its script in the page, and the page stays.</summary>
    [Fact(Timeout = 600000)]
    public void A_JavaScript_Link_Runs_In_The_Page()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p>" +
            $"<a href=\"javascript:document.getElementById('out').textContent = 'js' + 'marker ' + location.pathname\" style=\"{Button}; display: block\">Run</a>" +
            "<script>var ready = true;</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("jsmarker /page", Painted(window));
        Assert.EndsWith("/page", window.Address);
        Assert.Single(server.RequestsFor("/page"));
    }

    /// <summary>
    /// The page's own <c>scrollTo</c> scrolls the window, and the window's scroll -- a key here -- is the
    /// page's <c>scrollY</c>, with its <c>scroll</c> event.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_Page_And_The_Window_Scroll_Together()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            // The tall block comes first: the bridge's hit test takes the last element under the pointer in
            // document order, not the one painted on top, so a block after the button would take its click.
            "<!DOCTYPE html><html><body style=\"margin: 0\"><div style=\"height: 3000px\"></div>" +
            $"<p id=\"out\" style=\"{Out}\">before</p><div id=\"go\" style=\"{Button}\">Go</div>" +
            "<p id=\"far\" style=\"position: absolute; left: 0; top: 2000px; margin: 0\">far</p><script>" +
            "var far = document.getElementById('far');" +
            "document.getElementById('go').addEventListener('click', function () { scrollTo(0, 1950); far.textContent = 'far' + 'marker'; });" +
            "addEventListener('scroll', function () { document.getElementById('out').textContent = 'scrolled' + 'marker ' + Math.round(scrollY); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.True(window.IsInView("farmarker"), window.Describe());

        window.Key("Home", 0x24);
        window.Settle();
        Assert.Contains("scrolledmarker 0", Painted(window));
        Assert.False(window.IsInView("farmarker"), window.Describe());
    }

    /// <summary>
    /// The button that submits names itself in the submission and sends it to its <c>formaction</c>, with what
    /// the form's <c>formdata</c> listeners added.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Submission_Carries_Its_Button_And_Its_Formdata()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/other", Reply.Text("<!DOCTYPE html><html><body><p>othermarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<form id=\"f\" action=\"/sent\"><input name=\"q\" value=\"v\">" +
            $"<button name=\"act\" value=\"delete\" formaction=\"/other\" style=\"{Button}\">Delete</button></form>" +
            "<script>document.getElementById('f').addEventListener('formdata', function (e) { e.formData.append('extra', 'yes'); });</script>" +
            "</body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("othermarker", Painted(window));
        var sent = Assert.Single(server.RequestsFor("/other"));
        Assert.Equal("/other?q=v&act=delete&extra=yes", sent.Target);
        Assert.Empty(server.RequestsFor("/sent"));
    }

    /// <summary>
    /// A <c>method="dialog"</c> form's button closes its dialog, and the page stays. (The button is clicked
    /// by the page: the bridge's hit test does not find the content of an open dialog in the window.)
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Dialog_Form_Closes_Its_Dialog()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<dialog id=\"d\" open style=\"position: absolute; left: 0; top: 100px; margin: 0; padding: 0; border: 0\">" +
            "<p style=\"margin: 0\">dialogmarker</p><form method=\"dialog\"><button id=\"close\" value=\"done\">Close</button></form></dialog>" +
            $"<div id=\"go\" style=\"{Button}\">Go</div>" +
            "<script>document.getElementById('go').addEventListener('click', function () { document.getElementById('close').click(); });</script>" +
            "</body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        Assert.Contains("dialogmarker", Painted(window));
        ClickPage(window, 20, 55);

        Assert.DoesNotContain("dialogmarker", Painted(window));
        Assert.Single(server.RequestsFor("/page"));
    }
}
