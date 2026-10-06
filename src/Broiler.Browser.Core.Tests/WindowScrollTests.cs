using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The wheel scrolls the window's view, and the page's scripts learn where it went: a page that listens
/// hears <c>scroll</c> in the next frame, and a page that does not is not stepped for it.
/// </summary>
/// <remarks>
/// After the window started telling the page where the user scrolled, each wheel notch over
/// html5test.com cost seconds. The bridge laid the page out again to clamp the position the window had
/// already clamped, and the window then stepped the page, serializing its whole document, for a scroll
/// no script listened for. Broiler.HtmlBridge 0.1.0-preview.16 takes the position as it is and queues
/// nothing for a page that does not listen.
/// </remarks>
public class WindowScrollTests
{
    /// <summary>Everything the window painted, one run after another: a run is a word, so a phrase spans several.</summary>
    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    // Tall enough to scroll, with the paragraph a script writes to still in view after one notch (60px).
    private static string Page(string script) => $$"""
        <!DOCTYPE html><html><body style="margin: 0; height: 3000px">
        <p id="out" style="position: absolute; left: 0; top: 100px; width: 300px; height: 40px; margin: 0">before</p>
        <script>{{script}}</script>
        </body></html>
        """;

    /// <summary>A page listening for <c>scroll</c> hears the wheel, and reads where the view went.</summary>
    [Fact(Timeout = 600000)]
    public void A_Page_That_Listens_Hears_The_Wheel()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(Page(
            "addEventListener('scroll', function () {" +
            "  document.getElementById('out').textContent = 'heard' + 'marker ' + scrollY; });")));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        window.Wheel(window.PageArea.Left + 200, window.PageArea.Top + 200, -1);
        window.Settle();

        Assert.Contains("heardmarker 60", Painted(window));
    }

    /// <summary>
    /// A page with scripts but no <c>scroll</c> listener is not stepped for the wheel -- and still reads
    /// where the view went when its next task runs.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Page_That_Does_Not_Listen_Is_Not_Stepped_For_The_Wheel()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(Page(
            "document.getElementById('out').addEventListener('click', function () {" +
            "  this.textContent = 'clicked' + 'marker ' + scrollY; });")));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        window.Wheel(window.PageArea.Left + 200, window.PageArea.Top + 200, -1);
        window.Paint();

        Assert.False(window.PageHasWork);

        // The paragraph at page y 100 is drawn at 40 now.
        window.Click(window.PageArea.Left + 20, window.PageArea.Top + 55);
        window.Settle();
        Assert.Contains("clickedmarker 60", Painted(window));
    }
}
