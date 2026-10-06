using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// A click in the window reaches the page's scripts: hit-tested against the page's layout, into a
/// frame where it lands in one, and delivered as the trusted events a browser fires.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing the user did reached a page's scripts.</b> The window selected text and followed links
/// itself, and it threw a page's script session away once loading settled, so there was nothing left
/// to deliver a click to. reCAPTCHA's checkbox -- a <c>span</c> in its frame, listening for the click
/// -- drew, and did nothing when clicked.
/// </para>
/// <para>
/// Markers are assembled at run time, so a marker in a script's source is never the one found painted.
/// </para>
/// </remarks>
public class WindowClickTests
{
    /// <summary>Clicks <paramref name="window"/>'s page at (<paramref name="x"/>, <paramref name="y"/>) in the page's own coordinates.</summary>
    private static void ClickPage(TestWindow window, double x, double y)
    {
        window.Click(window.PageArea.Left + x, window.PageArea.Top + y);
        window.Settle();
    }

    private const string Box = "position: absolute; left: 0; top: 0; width: 300px; height: 100px; margin: 0";

    /// <summary>Everything the window painted, one run after another: a run is a word, so a phrase spans several.</summary>
    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    /// <summary>
    /// A click on an element runs its click listener, with a trusted event at the point clicked, and
    /// the window shows what the listener did.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Click_Runs_The_Elements_Click_Listener()
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <p id="target" style="{{Box}}">before</p>
            <script>
            document.getElementById('target').addEventListener('click', function (e) {
              this.textContent = 'clicked' + 'marker ' + e.isTrusted + ' ' + e.clientX + ',' + e.clientY;
            });
            </script>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 40, 30);

        Assert.Contains("clickedmarker true 40,30", Painted(window));
    }

    /// <summary>
    /// A click in a frame runs the frame's own listener, as the frame's script, and the window shows
    /// the frame as the listener left it -- the shape of reCAPTCHA's checkbox: a page's script embeds
    /// a frame, the frame's script builds a checkbox out of a <c>span</c> and listens for its click.
    /// </summary>
    [Theory(Timeout = 600000)]
    [InlineData("<script>var embedded = document.querySelector('iframe');</script>")]
    [InlineData("")]
    public void A_Click_In_A_Frame_Runs_The_Frames_Listener(string pageScript)
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <iframe src="/frame" style="display: block; border: 0; margin: 50px 0 0 100px; width: 300px; height: 100px"></iframe>
            {{pageScript}}
            </body></html>
            """;
        const string Frame = """
            <!DOCTYPE html><html><body style="margin: 0">
            <script>
            var box = document.createElement('span');
            box.style.display = 'block';
            box.style.width = '200px';
            box.style.height = '50px';
            box.textContent = 'un' + 'checked';
            document.body.appendChild(box);
            box.addEventListener('click', function (e) {
              this.textContent = 'checked' + 'marker ' + (e.view === window) + ' ' + e.clientX + ',' + e.clientY;
            });
            </script>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page)).Map("/frame", Reply.Text(Frame));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        Assert.Contains("unchecked", Painted(window));

        ClickPage(window, 120, 60);

        Assert.Contains("checkedmarker true 20,10", Painted(window));
    }

    /// <summary>
    /// A link whose click the page cancels is not followed; one it does not cancel is.
    /// </summary>
    [Theory(Timeout = 600000)]
    [InlineData("return false", false)]
    [InlineData("return true", true)]
    public void A_Link_Whose_Click_The_Page_Cancels_Is_Not_Followed(string handler, bool followed)
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <a href="/next" onclick="{{handler}}" style="{{Box}}; display: block">go</a>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page)).Map("/next", Reply.Text("<p>next</p>"));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 10, 10);

        Assert.Equal(followed, server.RequestsFor("/next").Length > 0);
    }

    /// <summary>
    /// A click handler that throws does not cancel the click, as in a browser: the link is still
    /// followed, and the window carries on.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Link_Whose_Click_Handler_Throws_Is_Still_Followed()
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <a href="/next" onclick="throw new Error('boom')" style="{{Box}}; display: block">go</a>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page)).Map("/next", Reply.Text("<p>next" + "page</p>"));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 10, 10);

        Assert.Contains("nextpage", Painted(window));
    }

    /// <summary>
    /// What a click's listener schedules runs after the load has long settled -- here a timer three
    /// seconds on -- and the window shows it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Clicks_Timers_Run_After_The_Load()
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <p id="target" style="{{Box}}">waiting</p>
            <script>
            setTimeout(function () {}, 4000);
            document.getElementById('target').addEventListener('click', function () {
              var target = this;
              setTimeout(function () { target.textContent = 'later' + 'marker'; }, 3000);
            });
            </script>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 40, 30);

        Assert.Contains("latermarker", Painted(window));
    }

    /// <summary>A click whose listener navigates the page is followed.</summary>
    [Fact(Timeout = 600000)]
    public void A_Click_That_Navigates_Is_Followed()
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <p id="target" style="{{Box}}">here</p>
            <script>
            document.getElementById('target').addEventListener('click', function () { location.href = '/next'; });
            </script>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page)).Map("/next", Reply.Text("<p>next" + "page</p>"));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 40, 30);

        Assert.Contains("nextpage", Painted(window));
    }

    /// <summary>
    /// A page with no script but an <c>onclick</c> attribute runs it: it is given a realm for it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void An_Onclick_Attribute_Runs_On_A_Page_Without_Scripts()
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <p style="{{Box}}" onclick="this.textContent = 'inline' + 'marker'">before</p>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 40, 30);

        Assert.Contains("inlinemarker", Painted(window));
    }

    /// <summary>
    /// A page's scripts measure the window's page area as their viewport, not the bridge's default
    /// 1024×768: a click is hit-tested against the layout the user sees, which depends on it.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Pages_Scripts_Measure_The_Windows_Page_Area()
    {
        const string Page = """
            <!DOCTYPE html><html><body style="margin: 0">
            <p id="out"></p>
            <script>document.getElementById('out').textContent = 'viewport' + 'marker ' + innerWidth + 'x' + innerHeight;</script>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(Page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        var expected = $"viewportmarker {Math.Round(window.PageArea.Width)}x{Math.Round(window.PageArea.Height)}";
        Assert.Contains(expected, Painted(window));
    }

    /// <summary>
    /// Moving the pointer over an element runs its hover listeners, and the window shows what they
    /// did: nothing told a page where the pointer was, so a menu opened on <c>mouseenter</c> never
    /// opened.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Moving_The_Pointer_Over_An_Element_Runs_Its_Hover_Listeners()
    {
        string page = $$"""
            <!DOCTYPE html><html><body style="margin: 0">
            <p style="{{Box}}" onmouseenter="this.textContent = 'entered' + 'marker'"
               onmousemove="this.title = event.clientX">before</p>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();
        Assert.Contains("before", Painted(window));

        window.Move(window.PageArea.Left + 40, window.PageArea.Top + 30);
        window.Settle();

        Assert.Contains("enteredmarker", Painted(window));
    }

    /// <summary>
    /// A press on a text field reaches the page before the window starts editing it: the page sees
    /// the field focused, and the window shows what its focus listener did. The window began editing
    /// without telling the page.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Press_On_A_Text_Field_Focuses_It_For_The_Page()
    {
        const string Page = """
            <!DOCTYPE html><html><body style="margin: 0">
            <input id="field" style="position: absolute; left: 0; top: 0; width: 300px; height: 30px; margin: 0">
            <p id="out" style="position: absolute; left: 0; top: 100px; margin: 0">before</p>
            <script>
            document.getElementById('field').addEventListener('focus', function () {
              document.getElementById('out').textContent = 'focused' + 'marker ' + (document.activeElement === this);
            });
            </script>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(Page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        ClickPage(window, 40, 15);
        window.Move(window.PageArea.Left + 40, window.PageArea.Top + 200);
        window.Settle();

        Assert.Contains("focusedmarker true", Painted(window));
    }
}
