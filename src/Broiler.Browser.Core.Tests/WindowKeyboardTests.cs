using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The keyboard in the window reaches the page's scripts: keys while the page has focus, what is typed
/// into a field the window edits, Tab, Enter and Space as a browser handles them -- and a press on a
/// text field reaches the page whole, its release included.
/// </summary>
/// <remarks>
/// <para>
/// <b>No key reached a page.</b> The window scrolled on the arrow keys and edited a field in its own
/// editor, whose text stayed in the window: the page's <c>input</c> listeners never ran, a form's
/// <c>submit</c> listener never ran for Enter, and the page's own copy of the field kept the value it
/// was loaded with. The release of a press on a text field went to that editor, so the page never had
/// a <c>click</c> for it.
/// </para>
/// <para>
/// Markers are assembled at run time, so a marker in a script's source is never the one found painted.
/// </para>
/// </remarks>
public class WindowKeyboardTests
{
    private static void ClickPage(TestWindow window, double x, double y)
    {
        window.Click(window.PageArea.Left + x, window.PageArea.Top + y);
        window.Settle();
    }

    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    private const string Field = "position: absolute; left: 0; top: 0; width: 300px; height: 30px; margin: 0";
    private const string Out = "position: absolute; left: 0; top: 200px; margin: 0";

    private static TestWindow Open(LoopbackHttpServer server, string body, string script)
    {
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" + body +
            $"<p id=\"out\" style=\"{Out}\">before</p><script>var out = document.getElementById('out');" + script + "</script></body></html>"));
        var window = new TestWindow(server.Url("/page"));
        window.Settle();
        return window;
    }

    /// <summary>
    /// What the user types into a text field reaches the page as the field's value, with an
    /// <c>input</c> event for each character, and the window shows what the listener did.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Typing_In_A_Field_Reaches_The_Page()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            $"<input id=\"field\" style=\"{Field}\">",
            "document.getElementById('field').addEventListener('input', function (e) { out.textContent = 'typed' + 'marker ' + this.value + ' ' + e.inputType; });");

        ClickPage(window, 40, 15);
        window.Type("hi");
        window.Settle();

        Assert.Contains("typedmarker hi insertText", Painted(window));
    }

    /// <summary>A press on a text field is a click for the page: its release went to the window's editor.</summary>
    [Fact(Timeout = 600000)]
    public void A_Click_On_A_Text_Field_Is_A_Click_For_The_Page()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            $"<input id=\"field\" style=\"{Field}\">",
            "var seen = []; ['mousedown', 'mouseup', 'click'].forEach(function (type) {" +
            " document.getElementById('field').addEventListener(type, function (e) { seen.push(type + (e.isTrusted ? '' : '?')); out.textContent = 'events' + 'marker ' + seen.join(','); }); });");

        ClickPage(window, 40, 15);

        Assert.Contains("eventsmarker mousedown,mouseup,click", Painted(window));
    }

    /// <summary>A key the page cancels types nothing, in the page or in the window's editor.</summary>
    [Fact(Timeout = 600000)]
    public void A_Key_The_Page_Cancels_Types_Nothing()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            $"<input id=\"field\" style=\"{Field}\">",
            "var field = document.getElementById('field');" +
            "field.addEventListener('keydown', function (e) { if (e.key === 'q') e.preventDefault(); });" +
            "field.addEventListener('keyup', function (e) { out.textContent = 'value' + 'marker [' + field.value + ']'; });");

        ClickPage(window, 40, 15);
        window.Type("aqb");
        window.Settle();

        Assert.Contains("valuemarker [ab]", Painted(window));
    }

    /// <summary>
    /// Enter in a field of a form submits the form through the page -- its <c>submit</c> listener runs,
    /// and cancels the submission here -- with the value the user typed.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Enter_Submits_The_Form_Through_The_Page()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            $"<form id=\"form\"><input id=\"field\" name=\"q\" style=\"{Field}\"></form>",
            "document.getElementById('form').addEventListener('submit', function (e) { e.preventDefault();" +
            " out.textContent = 'submitted' + 'marker ' + document.getElementById('field').value; });");

        ClickPage(window, 40, 15);
        window.Type("go");
        window.Key("Enter", 0x0D);
        window.Settle();

        Assert.Contains("submittedmarker go", Painted(window));
    }

    /// <summary>Tab moves the page's focus to the next field, which the page hears.</summary>
    [Fact(Timeout = 600000)]
    public void Tab_Moves_The_Pages_Focus()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            $"<input id=\"first\" style=\"{Field}\"><input id=\"second\" style=\"{Field}; top: 60px\">",
            "document.getElementById('second').addEventListener('focus', function () { out.textContent = 'second' + 'marker ' + document.activeElement.id; });");

        ClickPage(window, 40, 15);
        window.Key("Tab", 0x09);
        window.Settle();

        Assert.Contains("secondmarker second", Painted(window));
    }

    /// <summary>With nothing focused, a key goes to the page's body, as <c>keydown</c> and <c>keypress</c>.</summary>
    [Fact(Timeout = 600000)]
    public void A_Key_With_Nothing_Focused_Reaches_The_Page()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            "<p style=\"margin: 0\">page</p>",
            "var keys = []; ['keydown', 'keypress', 'keyup'].forEach(function (type) { document.addEventListener(type, function (e) {" +
            " keys.push(type + ':' + e.key + ':' + e.keyCode + ':' + (e.target === document.body)); out.textContent = 'keys' + 'marker ' + keys.join(' '); }); });");

        ClickPage(window, 400, 100);
        window.Key("KeyX", 0x58, "x");
        window.Settle();

        Assert.Contains("keysmarker keydown:x:88:true keypress:x:120:true keyup:x:88:true", Painted(window));
    }

    /// <summary>
    /// A <c>:hover</c> rule applies while the pointer is over its element, and stops when it leaves: the
    /// window shows the page again as the pointer crosses into and out of it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Hover_Rule_Applies_While_The_Pointer_Is_Over_Its_Element()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            "<style>#hover:hover { color: rgb(255, 0, 0) }</style>" +
            $"<p id=\"hover\" style=\"{Field}\">hovermarker</p>",
            string.Empty);

        Graphics.Color.BColor Colour() => window.Run("hovermarker").Run.Color;
        var before = Colour();

        window.Move(window.PageArea.Left + 40, window.PageArea.Top + 15);
        window.Settle();
        var hovered = Colour();

        window.Move(window.PageArea.Left + 400, window.PageArea.Top + 300);
        window.Settle();

        Assert.Equal((255, 0, 0), (hovered.R, hovered.G, hovered.B));
        Assert.NotEqual(hovered, before);
        Assert.Equal(before, Colour());
    }
}
