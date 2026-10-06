using Broiler.Input.Text;
using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// What the window tells a page, and the renderer, about what the user did: the pages it has shown
/// (<c>:visited</c>), a link into the page (<c>:target</c>, <c>hashchange</c>), a submit button's click,
/// which is the page's to act on, and the editor's selection and composition in a field.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each of these was the window's alone.</b> It kept no history, so no link was ever
/// <c>:visited</c>; it scrolled to a link into the page without telling the page, so its
/// <c>location.hash</c> stayed stale and nothing was <c>:target</c>; a click on a form's submit button
/// submitted nothing, and the page heard no <c>submit</c>; and its editor kept the field's selection and
/// any composition to itself.
/// </para>
/// <para>
/// Markers are assembled at run time, so a marker in a script's source is never the one found painted.
/// </para>
/// </remarks>
public class WindowPageStateTests
{
    private const string Out = "position: absolute; left: 0; top: 300px; margin: 0";

    private static void ClickPage(TestWindow window, double x, double y)
    {
        window.Click(window.PageArea.Left + x, window.PageArea.Top + y);
        window.Settle();
    }

    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    private static string Colour(TestWindow window, string marker)
    {
        var color = window.Run(marker).Run.Color;
        return $"rgb({color.R}, {color.G}, {color.B})";
    }

    /// <summary>A link to a page the window has shown is painted in its <c>:visited</c> colour; the others in their <c>:link</c> one.</summary>
    [Fact(Timeout = 600000)]
    public void A_Link_To_A_Page_The_Window_Showed_Is_Visited()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/seen", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\"><a href=\"/page\" style=\"position: absolute; left: 0; top: 0\">go</a></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><head><style>a:link { color: rgb(0, 0, 255) } a:visited { color: rgb(128, 0, 128) }</style></head>" +
            "<body style=\"margin: 0\"><p><a href=\"/seen\">seenmarker</a></p><p><a href=\"/new\">newmarker</a></p></body></html>"));

        using var window = new TestWindow(server.Url("/seen"));
        window.Settle();
        ClickPage(window, 5, 5);

        Assert.Equal("rgb(128, 0, 128)", Colour(window, "seenmarker"));
        Assert.Equal("rgb(0, 0, 255)", Colour(window, "newmarker"));
    }

    private const string Sections =
        "<style>section:target { color: rgb(255, 0, 0) }</style>" +
        "<a href=\"#two\" style=\"position: absolute; left: 0; top: 0\">jump</a>" +
        "<section id=\"one\" style=\"position: absolute; left: 0; top: 40px\">onemarker</section>" +
        "<section id=\"two\" style=\"position: absolute; left: 0; top: 80px\">twomarker</section>";

    /// <summary>On a page no script runs, a link into it makes the element it names <c>:target</c>.</summary>
    [Fact(Timeout = 600000)]
    public void A_Link_Into_A_Page_Without_Scripts_Moves_Its_Target()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text("<!DOCTYPE html><html><body style=\"margin: 0\">" + Sections + "</body></html>"));

        using var window = new TestWindow(server.Url("/page") + "#one");
        window.Settle();
        Assert.Equal("rgb(255, 0, 0)", Colour(window, "onemarker"));

        ClickPage(window, 5, 5);

        Assert.Equal("rgb(255, 0, 0)", Colour(window, "twomarker"));
        Assert.NotEqual("rgb(255, 0, 0)", Colour(window, "onemarker"));
    }

    /// <summary>
    /// On a page whose scripts run, a link into it is the page's own fragment navigation: its
    /// <c>location.hash</c> moves, it hears <c>hashchange</c>, and the element named is <c>:target</c>.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Link_Into_A_Page_With_Scripts_Is_Its_Own_Navigation()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" + Sections +
            $"<p id=\"out\" style=\"{Out}\">before</p><script>" +
            "window.addEventListener('hashchange', function () { document.getElementById('out').textContent = 'hash' + 'marker ' + location.hash + ' ' + document.querySelector(':target').id; });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 5, 5);

        Assert.Contains("hashmarker #two two", Painted(window));
        Assert.Equal("rgb(255, 0, 0)", Colour(window, "twomarker"));
    }

    private const string Form =
        "<form id=\"f\" action=\"/sent\"><input id=\"field\" name=\"q\" required style=\"position: absolute; left: 0; top: 0; width: 200px; height: 30px\">" +
        "<button id=\"go\" style=\"position: absolute; left: 0; top: 50px; width: 80px; height: 30px\">Go</button></form>";

    /// <summary>
    /// A click on a submit button is the page's to act on: an invalid form is held back with its
    /// <c>invalid</c>, and a valid one whose <c>submit</c> the page cancels goes nowhere.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Submit_Button_Click_Is_The_Pages_To_Act_On()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/sent", Reply.Text("<!DOCTYPE html><html><body><p>sentmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" + Form +
            $"<p id=\"out\" style=\"{Out}\">before</p><script>var out = document.getElementById('out'), seen = [];" +
            "document.getElementById('field').addEventListener('invalid', function () { seen.push('invalid'); out.textContent = 'form' + 'marker ' + seen.join(','); });" +
            "document.getElementById('f').addEventListener('submit', function (e) { e.preventDefault(); seen.push('submit ' + e.submitter.id); out.textContent = 'form' + 'marker ' + seen.join(','); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 20, 65);
        Assert.Contains("formmarker invalid", Painted(window));
        Assert.DoesNotContain("sentmarker", Painted(window));

        ClickPage(window, 40, 15);
        window.Type("x");
        ClickPage(window, 20, 65);

        Assert.Contains("formmarker invalid,submit go", Painted(window));
        Assert.DoesNotContain("sentmarker", Painted(window));
    }

    /// <summary>
    /// A valid form whose button the user clicks is submitted, once: the page hears its <c>submit</c>,
    /// and the window follows the submission the page asked for and makes none of its own. The click
    /// submitted nothing before.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Valid_Form_Is_Submitted_Once()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/sent", Reply.Text("<!DOCTYPE html><html><body><p>sentmarker</p></body></html>"));
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" + Form.Replace(" required ", " value=\"q\" ") +
            "<script>document.getElementById('f').addEventListener('submit', function (e) { document.cookie = 'heard=' + e.submitter.id; });</script>" +
            "</body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 20, 65);

        Assert.Contains("sentmarker", Painted(window));
        var sent = Assert.Single(server.RequestsFor("/sent"));
        Assert.Contains("heard=go", sent.Cookie);
    }

    /// <summary>
    /// The page hears the selection the user makes in the window's editor, and the editor follows the
    /// one a script sets: a field whose focus listener selects all of it has its text replaced by what
    /// is typed next.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void The_Editors_Selection_And_The_Pages_Follow_Each_Other()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<input id=\"field\" value=\"hello\" style=\"position: absolute; left: 0; top: 0; width: 200px; height: 30px\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><script>var out = document.getElementById('out'), field = document.getElementById('field');" +
            "field.addEventListener('select', function () { out.textContent = 'select' + 'marker ' + field.selectionStart + ',' + field.selectionEnd + ' ' + field.selectionDirection; });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 190, 15);
        window.Key("End", 0x23);
        window.Key("ArrowLeft", 0x25, shift: true);
        window.Key("ArrowLeft", 0x25, shift: true);
        window.Settle();

        Assert.Contains("selectmarker 3,5 backward", Painted(window));
    }

    /// <summary>
    /// A script's selection reaches the editor: a key listener's <c>select()</c> selects the editor's
    /// text too, so what is typed next replaces it. (A <c>focus</c> listener's would not survive the
    /// click, whose release places the caret, in Chromium as here.)
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Scripts_Selection_Moves_The_Editors()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<input id=\"field\" value=\"hello\" style=\"position: absolute; left: 0; top: 0; width: 200px; height: 30px\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><script>var out = document.getElementById('out'), field = document.getElementById('field');" +
            "field.addEventListener('keydown', function (e) { if (e.key === 'F2') field.select(); });" +
            "field.addEventListener('input', function () { out.textContent = 'value' + 'marker ' + field.value; });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 40, 15);
        window.Key("F2", 0x71);
        window.Type("x");
        window.Settle();

        Assert.Contains("valuemarker x", Painted(window));
    }

    /// <summary>An input method's composition in the window's editor is heard by the page, its commit included.</summary>
    [Fact(Timeout = 600000)]
    public void A_Composition_Reaches_The_Page()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" +
            "<input id=\"field\" style=\"position: absolute; left: 0; top: 0; width: 200px; height: 30px\">" +
            $"<p id=\"out\" style=\"{Out}\">before</p><script>var out = document.getElementById('out'), field = document.getElementById('field'), seen = [];" +
            "['compositionstart', 'compositionupdate', 'compositionend'].forEach(function (type) { field.addEventListener(type, function (e) {" +
            " seen.push(type.slice(11) + ':' + e.data); out.textContent = 'compose' + 'marker ' + seen.join(',') + ' ' + field.value; }); });" +
            "</script></body></html>"));

        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        ClickPage(window, 40, 15);
        window.Compose(TextCompositionState.Started, string.Empty);
        window.Compose(TextCompositionState.Updated, "k");
        window.Compose(TextCompositionState.Updated, "か");
        window.Compose(TextCompositionState.Committed, "か");
        window.Settle();

        Assert.Contains("composemarker start:,update:k,update:か,end:か か", Painted(window));
    }
}
