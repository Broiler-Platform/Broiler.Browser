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
            // The tall block comes after the button, as on a page with its content after a positioned
            // control: the press is the button's, which is painted on top of it.
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><div id=\"go\" style=\"{Button}\">Go</div><div style=\"height: 3000px\"></div>" +
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

    /// <summary>A <c>method="dialog"</c> form's button the user presses closes its dialog, and the page stays.</summary>
    [Fact(Timeout = 600000)]
    public void A_Dialog_Form_Closes_Its_Dialog()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<dialog id=\"d\" open style=\"position: absolute; left: 0; top: 100px; margin: 0; padding: 0; border: 0\">" +
            "<p style=\"margin: 0\">dialogmarker</p><form method=\"dialog\" style=\"margin: 0\"><button id=\"close\" value=\"done\" style=\"width: 80px; height: 30px\">Close</button></form></dialog>" +
            "<script>var ready = true;</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        Assert.Contains("dialogmarker", Painted(window));
        ClickPage(window, 20, window.Run("Close").At.Y - window.PageArea.Top + 5);

        Assert.DoesNotContain("dialogmarker", Painted(window));
        Assert.Single(server.RequestsFor("/page"));
    }

    /// <summary>
    /// Back to another page puts the view where it was when the user left it, as Chromium does, and the page
    /// hears that scroll.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Back_To_Another_Page_Restores_Its_Scroll()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/other", Reply.Text("<!DOCTYPE html><html><body><p>othermarker</p></body></html>"));
        server.Map("/long", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<div id=\"go\" style=\"{Button}\">Go</div><div style=\"height: 3000px\"></div>" +
            "<a href=\"/other\" style=\"position: absolute; left: 0; top: 1250px; display: block; width: 120px; height: 30px\">farmarker</a>" +
            "<script>document.getElementById('go').addEventListener('click', function () { scrollTo(0, 1200); });</script></body></html>"));

        using var window = new TestWindow(server.Url("/long"));
        window.Settle();
        ClickPage(window, 20, 55);
        Assert.True(window.IsInView("farmarker"), window.Describe());

        window.Click(window.Run("farmarker").At.X + 5, window.Run("farmarker").At.Y + 5);
        window.Settle();
        Assert.Contains("othermarker", Painted(window));

        Assert.True(window.GoBack());
        window.Settle();
        Assert.True(window.IsInView("farmarker"), window.Describe());
        Assert.Equal(2, server.RequestsFor("/long").Length);
    }

    /// <summary>
    /// A <c>javascript:</c> link whose script answers a string replaces the page with it, at the page's URL,
    /// with no new entry and nothing fetched (measured in Chromium).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_JavaScript_String_Replaces_The_Page()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<a href=\"javascript:'&lt;p&gt;replaced' + 'marker&lt;/p&gt;'\" style=\"{Button}; display: block\">Run</a>" +
            "<p style=\"position: absolute; left: 0; top: 200px; margin: 0\">pagemarker</p><script>var ready = true;</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("replacedmarker", Painted(window));
        Assert.DoesNotContain("pagemarker", Painted(window));
        Assert.EndsWith("/page", window.Address);
        Assert.False(window.GoBack());
        Assert.Single(server.RequestsFor("/page"));
    }

    /// <summary>A page with no script at all submits its form when the user presses its submit button.</summary>
    [Fact(Timeout = 600000)]
    public void A_Page_Without_Scripts_Submits_Its_Form()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/sent", Reply.Text("<!DOCTYPE html><html><body><p>sentmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<form action=\"/sent\"><input name=\"q\" value=\"v\" style=\"position: absolute; left: 200px; top: 0\"><button style=\"{Button}\">Send</button></form>" +
            "</body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("sentmarker", Painted(window));
        Assert.Equal("/sent?q=v", Assert.Single(server.RequestsFor("/sent")).Target);
    }

    /// <summary>
    /// A frame's <c>pushState</c> is an entry of the window's history too: the address stays the page's, and
    /// the window's back button takes the frame back, which hears <c>popstate</c>.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Frames_Entry_Is_The_Windows()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><div id=\"go\" style=\"{Button}\">Go</div>" +
            "<iframe id=\"f\" srcdoc=\"&lt;p&gt;frame&lt;/p&gt;\" style=\"position: absolute; left: 0; top: 200px\"></iframe><script>" +
            "var out = document.getElementById('out'), fw = document.getElementById('f').contentWindow;" +
            "fw.addEventListener('popstate', function (e) { out.textContent = 'frame' + 'popped ' + JSON.stringify(e.state); });" +
            "document.getElementById('go').addEventListener('click', function () { fw.history.pushState({ f: 1 }, ''); out.textContent = 'frame' + 'pushed ' + history.length; });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);
        Assert.Contains("framepushed 2", Painted(window));

        Assert.True(window.GoBack());
        window.Settle();
        Assert.Contains("framepopped null", Painted(window));
        Assert.EndsWith("/page", window.Address);
        Assert.Single(server.RequestsFor("/page"));
    }

    /// <summary>
    /// A choice in the select the window draws for the page's is the page's select's: its value moves and it
    /// hears <c>input</c> and <c>change</c>, as a user's choice (measured in Chromium).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Hosted_Select_Tells_The_Page()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p>" +
            "<select id=\"s\" style=\"position: absolute; left: 0; top: 40px; width: 120px; height: 24px\"><option value=\"a\">A</option><option value=\"b\">B</option></select><script>" +
            "var out = document.getElementById('out'), s = document.getElementById('s'), log = [];" +
            "['input', 'change'].forEach(function (t) { s.addEventListener(t, function (e) { log.push(t + ' ' + e.isTrusted + ' ' + s.value); out.textContent = 'select' + 'marker ' + log.join(' '); }); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        // The user opens the select, moves down one option and takes it.
        ClickPage(window, 20, 52);
        window.Key("ArrowDown", 0x28);
        window.Key("Enter", 0x0D);
        window.Settle();

        Assert.Contains("selectmarker input true b change true b", Painted(window));
    }

    /// <summary>
    /// A frame's GET form that targets the page loads the page at the URL its entries make, as a submission
    /// does; the bridge builds it, since the window knows only the page's own forms.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Frames_Form_Navigates_The_Page()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/top", Reply.Text("<!DOCTYPE html><html><body><p>topmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<div id=\"go\" style=\"{Button}\">Go</div>" +
            "<iframe id=\"f\" srcdoc=\"&lt;form id=&quot;up&quot; action=&quot;/top&quot; target=&quot;_top&quot;&gt;&lt;input name=&quot;x&quot; value=&quot;1&quot;&gt;&lt;/form&gt;\"" +
            " style=\"position: absolute; left: 0; top: 200px\"></iframe><script>" +
            "document.getElementById('go').addEventListener('click', function () { document.getElementById('f').contentDocument.getElementById('up').submit(); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("topmarker", Painted(window));
        Assert.Equal("/top?x=1", Assert.Single(server.RequestsFor("/top")).Target);
    }
}
