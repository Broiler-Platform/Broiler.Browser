using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The top layer and anchor positioning in the window, as Chromium draws them (measured): a modal dialog over a
/// <c>rgba(0, 0, 0, 0.1)</c> backdrop that dims the page, above a
/// <c>z-index: 2147483647</c> box and out of an ancestor's clip and transform; a popover under the button that
/// opened it; and a popover or dialog of long text held inside the viewport.
/// </summary>
/// <remarks>
/// <b>The window ran none of it.</b> The scripting host's pass that marks the top layer, synthesizes the
/// backdrop and places anchored boxes was the WPT runner's, baking into the live page once; the window renders a
/// page that goes on running and never called it. So a modal dialog had no backdrop and painted where it stood in
/// the page, under a high <c>z-index</c> and inside its ancestors' clip, and an anchored popover sat in the middle
/// of the viewport. The pass now runs on the copy of the page the window draws.
/// </remarks>
public class WindowTopLayerTests
{
    private static TestWindow Open(LoopbackHttpServer server, string body, string script)
    {
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" + body + "<script>" + script + "</script></body></html>"));

        var window = new TestWindow(server.Url("/page"));
        window.Settle();
        return window;
    }

    private static int IndexOfText(IReadOnlyList<(string? Text, Broiler.Graphics.Geometry.BRect Rect, Broiler.Graphics.Color.BColor Color)> painting, string marker)
    {
        for (var i = 0; i < painting.Count; i++)
        {
            if (painting[i].Text?.Contains(marker, StringComparison.Ordinal) == true)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// A modal dialog's backdrop covers the page in 10% black, so a red box behind it is drawn dimmed rather than
    /// hidden; the dialog is drawn after it. There was no backdrop.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Modal_Dialog_Dims_The_Page_Under_Its_Backdrop()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            "<div style=\"position: absolute; left: 0; top: 0; width: 300px; height: 300px; background: red\"></div>" +
            "<dialog id=\"d\">modalmarker</dialog>",
            "document.getElementById('d').showModal();");

        var painting = window.Painting();
        var backdrop = painting.FindIndex(static p => p.Text is null && p.Color.A is 25 or 26 && p.Color.R == 0 && p.Color.G == 0 && p.Color.B == 0);
        var red = painting.FindIndex(static p => p.Text is null && p.Color.A == 255 && p.Color.R == 255 && p.Color.G == 0 && p.Color.B == 0);
        var dialog = IndexOfText(painting, "modalmarker");

        Assert.True(backdrop >= 0, "No 10% black backdrop was painted.");
        Assert.True(red >= 0 && red < backdrop, $"The red box ({red}) is not under the backdrop ({backdrop}).");
        Assert.True(dialog > backdrop, $"The dialog ({dialog}) is not over its backdrop ({backdrop}).");
        Assert.True(painting[backdrop].Rect.Width >= window.PageArea.Width - 1 && painting[backdrop].Rect.Height >= window.PageArea.Height - 1,
            $"The backdrop is {painting[backdrop].Rect.Width}x{painting[backdrop].Rect.Height}, the page {window.PageArea.Width}x{window.PageArea.Height}.");
    }

    /// <summary>
    /// A modal dialog inside a clipped, transformed ancestor, under a <c>z-index: 2147483647</c> box, is drawn whole,
    /// centred in the viewport and after the box. It was clipped to its ancestor, moved by its transform and covered.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Modal_Dialog_Is_Drawn_Above_Everything_And_Out_Of_Its_Clip()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            "<div style=\"overflow: hidden; height: 20px; width: 200px; transform: translateX(10px); position: relative; z-index: 1\">" +
            "<dialog id=\"d\">clippedmarker</dialog></div>" +
            "<div style=\"position: fixed; z-index: 2147483647; left: 0; top: 0; width: 100%; height: 100%; background: rgb(0, 0, 255)\"></div>",
            "document.getElementById('d').showModal();");

        var painting = window.Painting();
        var cover = painting.FindIndex(static p => p.Text is null && p.Color.A == 255 && p.Color.R == 0 && p.Color.G == 0 && p.Color.B == 255);
        var dialog = IndexOfText(painting, "clippedmarker");
        var at = window.Run("clippedmarker").At;

        Assert.True(cover >= 0 && dialog > cover, $"The dialog's text ({dialog}) is not drawn after the cover ({cover}).");
        Assert.InRange(at.X - window.PageArea.Left, window.PageArea.Width / 3, window.PageArea.Width * 2 / 3);
        Assert.InRange(at.Y - window.PageArea.Top, window.PageArea.Height / 3, window.PageArea.Height * 2 / 3);
    }

    /// <summary>
    /// A popover with <c>position-area: bottom</c> and no anchor of its own is drawn under the button that opened it,
    /// centred on it, and that is where the page's own measurement finds it (Chromium: centred under its invoker).
    /// It was drawn in the middle of the viewport, and the page measured it there.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Popover_Is_Drawn_Under_The_Button_That_Opened_It()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            "<style>#menu { position-area: bottom; margin: 0 }</style>" +
            // border-box, as Chromium's UA sheet makes a button (Broiler's makes it content-box: a separate gap).
            "<button id=\"open\" popovertarget=\"menu\" style=\"position: absolute; left: 100px; top: 100px; width: 120px; height: 30px; box-sizing: border-box\">open</button>" +
            "<div id=\"menu\" popover>menumarker</div>" +
            "<p id=\"out\" style=\"position: absolute; left: 0; bottom: 0; margin: 0\">waiting</p>",
            "var menu = document.getElementById('menu');" +
            "menu.addEventListener('toggle', function () { var r = menu.getBoundingClientRect();" +
            "  document.getElementById('out').textContent = 'rectmarker ' + [r.left, r.top, r.width].map(Math.round).join(' '); });");

        window.Click(window.PageArea.Left + 160, window.PageArea.Top + 115);
        window.Settle();

        var words = string.Join(" ", window.Texts().Select(static t => t.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(words, "rectmarker");
        Assert.True(at >= 0, string.Join(" ", words));
        var left = double.Parse(words[at + 1], System.Globalization.CultureInfo.InvariantCulture);
        var top = double.Parse(words[at + 2], System.Globalization.CultureInfo.InvariantCulture);
        var width = double.Parse(words[at + 3], System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(130, top, 0);
        Assert.InRange(left + width / 2, 158, 162);

        var drawn = window.Run("menumarker").At;
        Assert.InRange(drawn.X - window.PageArea.Left, left, left + 12);
        Assert.InRange(drawn.Y - window.PageArea.Top, 130, 150);
    }

    /// <summary>
    /// A popover and a modal dialog of long text are held inside the viewport, as Chromium wraps them (its popover is
    /// as wide as the viewport, its dialog 38px narrower in content). They were wider than the viewport by their
    /// padding and border, twice over.
    /// </summary>
    [Theory(Timeout = 600000)]
    [InlineData("<div id=\"box\" popover>", "</div>", "showPopover")]
    [InlineData("<dialog id=\"box\">", "</dialog>", "showModal")]
    public void A_Long_Popover_Or_Dialog_Stays_In_The_Viewport(string open, string close, string show)
    {
        using var server = new LoopbackHttpServer();
        var words = string.Join(" ", Enumerable.Repeat("word", 300));
        using var window = Open(server,
            open + words + close + "<p id=\"out\" style=\"position: absolute; left: 0; bottom: 0; margin: 0\">waiting</p>",
            "var box = document.getElementById('box'); box." + show + "(); var r = box.getBoundingClientRect();" +
            "document.getElementById('out').textContent = 'rectmarker ' + [r.left, r.width, innerWidth].map(Math.round).join(' ');");

        var words2 = string.Join(" ", window.Texts().Select(static t => t.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(words2, "rectmarker");
        Assert.True(at >= 0, string.Join(" ", words2));
        var left = double.Parse(words2[at + 1], System.Globalization.CultureInfo.InvariantCulture);
        var width = double.Parse(words2[at + 2], System.Globalization.CultureInfo.InvariantCulture);
        var viewport = double.Parse(words2[at + 3], System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(left >= 0 && left + width <= viewport + 1, $"The box is {left}+{width} in a {viewport}px viewport.");
        Assert.True(width >= viewport - 2, $"The box is {width}px wide: it should wrap at the viewport's edges.");
    }
}
