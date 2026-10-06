using Broiler.UI.ComboBox.Standard;
using Broiler.UI.ListView.Standard;
using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// What the user does in the window reaches the page as Chromium's would: a link with an <c>id</c>, a link
/// placed beside a collapsing margin, the options of a multiple select, the files of a file input, a frame's
/// <c>post</c> into the page, a popover and its invoker, and Tab in a modal dialog.
/// </summary>
/// <remarks>
/// Each was found in round six and measured in Chromium: the window followed no
/// link with an <c>id</c>, drew an absolutely positioned link 16px below Chromium's when a later paragraph's
/// margin collapsed through the body, gave the page one option of a multiple select and none of the chosen
/// files, dropped a frame's <c>post</c> into the page, drew every closed popover, and let Tab leave a modal
/// dialog.
/// </remarks>
public class WindowChoicesTests
{
    private const string Out = "position: absolute; left: 0; top: 0; margin: 0";

    private const string Button = "position: absolute; left: 0; top: 40px; width: 80px; height: 30px; display: block";

    private static void ClickPage(TestWindow window, double x, double y)
    {
        window.Click(window.PageArea.Left + x, window.PageArea.Top + y);
        window.Settle();
    }

    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    /// <summary>A click on a link with an <c>id</c> follows it. It followed nothing: Layout took an <c>id</c> for a named anchor's.</summary>
    [Fact(Timeout = 600000)]
    public void A_Link_With_An_Id_Is_Followed()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/next", Reply.Text("<!DOCTYPE html><html><body><p>nextmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<a id=\"next\" href=\"/next\" style=\"{Button}\">Next</a><script>var ready = true;</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("nextmarker", Painted(window));
        Assert.Single(server.RequestsFor("/next"));
    }

    /// <summary>
    /// A link placed by <c>top: 40px</c> before a paragraph whose margin collapses through the body is drawn
    /// 40px down and clicked there, as in Chromium. It was drawn 56px down, and a click at 45px missed it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Positioned_Link_Is_Where_Its_Top_Puts_It()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/next", Reply.Text("<!DOCTYPE html><html><body><p>nextmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<a href=\"/next\" style=\"{Button}\">Next</a><p>pagemarker</p></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        Assert.Equal(40, window.Run("Next").At.Y - window.PageArea.Top, 1);
        ClickPage(window, 20, 45);

        Assert.Contains("nextmarker", Painted(window));
    }

    /// <summary>
    /// The options chosen in the window's list for a multiple select are the page's: it hears one <c>input</c>
    /// and one <c>change</c> with them all, and its submission carries each. It heard only the first.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Multiple_Select_Tells_The_Page_Every_Option()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/result", Reply.Text("<!DOCTYPE html><html><body><p>resultmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p>" +
            "<form action=\"/result\"><select id=\"m\" name=\"m\" multiple style=\"position: absolute; left: 0; top: 40px; width: 120px; height: 80px\">" +
            "<option value=\"a\">A</option><option value=\"b\" selected>B</option><option value=\"c\">C</option></select>" +
            "<button id=\"go\" style=\"position: absolute; left: 200px; top: 40px; width: 80px; height: 30px\">Go</button></form><script>" +
            "var out = document.getElementById('out'), m = document.getElementById('m'), log = [];" +
            "['input', 'change'].forEach(function (t) { m.addEventListener(t, function (e) {" +
            "  log.push(t + ' ' + Array.from(m.selectedOptions).map(function (o) { return o.value; }).join('+')); out.textContent = 'select' + 'marker ' + log.join(' '); }); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        var list = window.HostedControls.OfType<StandardListView>().Single();
        list.SetSelectedItems(["0", "2"]);
        window.Settle();

        Assert.Contains("selectmarker input a+c change a+c", Painted(window));
        ClickPage(window, 220, 55);
        Assert.Equal("/result?m=a&m=c", Assert.Single(server.RequestsFor("/result")).Target);
    }

    /// <summary>
    /// A select a page empties with <c>options.length = 0</c> and fills again with <c>add(new Option(text, value))</c>
    /// -- the common way to do it -- shows the new options in the window's drop-down, on the one the page selected,
    /// and submits it. <c>new Option</c> did not exist and <c>options.length = 0</c> removed nothing (measured in
    /// Chromium).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Select_The_Page_Fills_Again_Shows_Its_New_Options()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/result", Reply.Text("<!DOCTYPE html><html><body><p>resultmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<form action=\"/result\"><select id=\"s\" name=\"s\" style=\"position: absolute; left: 0; top: 40px; width: 120px\">" +
            "<option value=\"old1\">Old one</option><option value=\"old2\" selected>Old two</option></select>" +
            "<button id=\"go\" style=\"position: absolute; left: 200px; top: 40px; width: 80px; height: 30px\">Go</button></form><script>" +
            "var s = document.getElementById('s'); s.options.length = 0;" +
            "s.add(new Option('New A', 'a')); s.add(new Option('New B', 'b', false, true)); s.add(new Option('New C', 'c'));" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        var combo = window.HostedControls.OfType<StandardComboBox>().Single();

        Assert.Equal(1, combo.SelectedIndex);
        ClickPage(window, 220, 55);
        Assert.Equal("/result?s=b", Assert.Single(server.RequestsFor("/result")).Target);
    }

    /// <summary>
    /// The file chosen in the window's picker is the page's: <c>files</c> lists it, with its name, size, type
    /// and bytes, and the page hears <c>change</c>; a picker closed without one is the page's <c>cancel</c>.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Chosen_File_Is_The_Pages()
    {
        var path = Path.Combine(Path.GetTempPath(), $"broiler-pick-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "hello");
        try
        {
            using var server = new LoopbackHttpServer();
            server.Map("/page", Reply.Text(
                "<!DOCTYPE html><html><body style=\"margin: 0\">" +
                $"<p id=\"out\" style=\"{Out}\">before</p>" +
                "<input type=\"file\" id=\"up\" style=\"position: absolute; left: 0; top: 40px; width: 150px; height: 26px\">" +
                "<input type=\"file\" id=\"other\" style=\"position: absolute; left: 0; top: 100px; width: 150px; height: 26px\"><script>" +
                "var out = document.getElementById('out'), log = [];" +
                "function note(t) { log.push(t); out.textContent = 'file' + 'marker ' + log.join(' '); }" +
                "var up = document.getElementById('up');" +
                "up.addEventListener('change', function () { var f = up.files[0];" +
                "  f.text().then(function (text) { note('change ' + f.size + ' ' + f.type + ' ' + text + ' ' + (up.value.indexOf('C:\\\\fakepath\\\\broiler-pick-') === 0)); }); });" +
                "document.getElementById('other').addEventListener('cancel', function () { note('cancel'); });" +
                "</script></body></html>"));

            using var window = new TestWindow(server.Url("/page"));
            window.Settle();
            window.ChooseFile = _ => path;
            ClickPage(window, 20, 53);
            window.ChooseFile = _ => null;
            ClickPage(window, 20, 113);

            Assert.Contains("filemarker change 5 text/plain hello true cancel", Painted(window));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A frame's <c>post</c> form that targets the page loads the page with what the form posts, as a
    /// submission does. It was dropped: the window submits the page's own forms only.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Frames_Post_Navigates_The_Page()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/top", Reply.Text("<!DOCTYPE html><html><body><p>topmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<div id=\"go\" style=\"{Button}\">Go</div>" +
            "<iframe id=\"f\" srcdoc=\"&lt;form id=&quot;up&quot; action=&quot;/top&quot; method=&quot;post&quot; target=&quot;_top&quot;&gt;&lt;input name=&quot;x&quot; value=&quot;1 2&quot;&gt;&lt;/form&gt;\"" +
            " style=\"position: absolute; left: 0; top: 200px\"></iframe><script>" +
            "document.getElementById('go').addEventListener('click', function () { document.getElementById('f').contentDocument.getElementById('up').submit(); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);

        Assert.Contains("topmarker", Painted(window));
        var posted = Assert.Single(server.RequestsFor("/top"));
        Assert.Equal("POST", posted.Method);
        Assert.Equal("x=1+2", posted.Body);
    }

    /// <summary>
    /// A closed popover is not drawn; a click on its invoker draws it, and a press outside it takes it away
    /// again. Every closed popover was drawn where it stood in the flow.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Popover_Is_Drawn_Only_While_It_Is_Open()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<button popovertarget=\"p\" style=\"{Button}\">Open</button>" +
            "<p id=\"else\" style=\"position: absolute; left: 0; top: 300px; margin: 0\">elsewhere</p>" +
            "<div id=\"p\" popover>popmarker</div><script>var ready = true;</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        Assert.DoesNotContain("popmarker", Painted(window));

        ClickPage(window, 20, 55);
        Assert.Contains("popmarker", Painted(window));

        ClickPage(window, 20, 305);
        Assert.DoesNotContain("popmarker", Painted(window));
    }

    /// <summary>
    /// A click on a button that submits no form runs the page's listener and nothing else. The window took it
    /// for a link to the page's own URL and loaded the page again under every such click.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Button_Outside_A_Form_Does_Not_Load_The_Page_Again()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><button id=\"b\" style=\"{Button}\">Press</button>" +
            "<form><button type=\"button\" id=\"plain\" style=\"position: absolute; left: 0; top: 100px; width: 80px; height: 30px\">Plain</button></form><script>" +
            "var out = document.getElementById('out'), log = [];" +
            "['b', 'plain'].forEach(function (id) { document.getElementById(id).addEventListener('click', function () { log.push(id); out.textContent = 'click' + 'marker ' + log.join(' '); }); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 55);
        ClickPage(window, 20, 115);

        Assert.Contains("clickmarker b plain", Painted(window));
        Assert.Single(server.RequestsFor("/page"));
    }

    /// <summary>
    /// Focus goes into a modal dialog as it opens, and Tab goes round its buttons and never to the page behind
    /// it; a press behind it falls on the dialog, which takes focus. Tab reached the page's button, and the
    /// dialog opened with focus outside it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Tab_Stays_In_A_Modal_Dialog()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><button id=\"behind\">behind</button>" +
            "<dialog id=\"d\"><button id=\"one\">one</button><button id=\"two\">two</button></dialog><script>" +
            "var out = document.getElementById('out'), log = [];" +
            "document.addEventListener('focusin', function (e) { log.push(e.target.id); out.textContent = 'focus' + 'marker ' + log.join(' '); });" +
            "document.getElementById('d').showModal();" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 300, 450);
        for (var i = 0; i < 3; i++)
            window.Key("Tab", 0x09);
        window.Settle();

        Assert.Contains("focusmarker one d one two one", Painted(window));
    }
}
