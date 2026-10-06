using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// Where the window draws an open popover and a modal dialog: in the middle of the viewport, as Chromium does --
/// by HTML's <c>[popover] { position: fixed; inset: 0; margin: auto; width/height: fit-content }</c>, with which
/// an author's own insets combine.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window drew an open popover and a dialog where they stood in the flow</b>, as wide as the page: the
/// geometry the scripting host gives the top layer is written only by a pass the window never runs, and the
/// renderer had no rule of its own. With the rules, two Layout bugs showed: a box sized by its content was centred
/// only along the line, and a scroll container sized <c>fit-content</c> -- a popover -- lost its content's height.
/// </para>
/// <para>Measured in Chromium; the page reports what it measures itself.</para>
/// </remarks>
public class WindowPopoverPlacementTests
{
    private static string Painted(TestWindow window) => string.Join(" ", window.Texts().Select(static t => t.Text));

    /// <summary>The page's report: <c>rectmarker left top width height innerWidth innerHeight</c> of each element measured.</summary>
    private static double[] Report(TestWindow window, string marker)
    {
        // The report is painted a word at a time: the numbers are the words after the marker.
        var words = Painted(window).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(words, marker);
        if (at < 0)
            throw new Xunit.Sdk.XunitException($"No '{marker}' was painted: {Painted(window)}");

        var numbers = new List<double>();
        for (var i = at + 1; i < words.Length &&
             double.TryParse(words[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n); i++)
            numbers.Add(n);
        return [.. numbers];
    }

    private static TestWindow Open(LoopbackHttpServer server, string body, string script)
    {
        server.Map("/page", Reply.Text(
            "<!DOCTYPE html><html><body style=\"margin: 0\">" + body +
            "<p id=\"out\" style=\"position: absolute; left: 0; bottom: 0; margin: 0\">waiting</p><script>" +
            "function measure(id) { var r = document.getElementById(id).getBoundingClientRect();" +
            "  return [r.left, r.top, r.width, r.height, innerWidth, innerHeight].map(function (n) { return Math.round(n * 10) / 10; }).join(' '); }" +
            script + "</script></body></html>"));

        var window = new TestWindow(server.Url("/page"));
        window.Settle();
        return window;
    }

    /// <summary>A popover is centred in the viewport and drawn there. It was drawn at the top-left corner.</summary>
    [Fact(Timeout = 600000)]
    public void A_Popover_Is_Centred_In_The_Viewport()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server, "<div id=\"p\" popover>popmarker</div>",
            "document.getElementById('p').showPopover(); document.getElementById('out').textContent = 'rectmarker ' + measure('p');");

        var r = Report(window, "rectmarker");
        Assert.True(r[3] > 10, $"The popover is {r[3]}px tall: its text is gone.");
        Assert.Equal((r[4] - r[2]) / 2, r[0], 0);
        Assert.Equal((r[5] - r[3]) / 2, r[1], 0);

        var painted = window.Run("popmarker").At;
        Assert.True(painted.X - window.PageArea.Left > r[4] / 3 && painted.Y - window.PageArea.Top > r[5] / 3,
            $"popmarker was painted at {painted.X - window.PageArea.Left},{painted.Y - window.PageArea.Top}.");
    }

    /// <summary>
    /// An author's <c>top</c> and <c>left</c> leave the other insets 0 and the margins auto, so the popover is
    /// centred in what remains (Chromium: 20 + half the rest across, 50 + half the rest down); with the insets
    /// reset it is where they say.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Author_Insets_Combine_With_The_User_Agents()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            "<style>#a { top: 50px; left: 20px } #b { inset: auto; top: 50px; left: 20px; margin: 0 }</style>" +
            "<div id=\"a\" popover=\"manual\">offset</div><div id=\"b\" popover=\"manual\">placed</div>",
            "document.getElementById('a').showPopover(); document.getElementById('b').showPopover();" +
            "document.getElementById('out').textContent = 'rectmarker ' + measure('a') + ' ' + measure('b');");

        var r = Report(window, "rectmarker");
        Assert.Equal(20 + (r[4] - 20 - r[2]) / 2, r[0], 0);
        Assert.Equal(50 + (r[5] - 50 - r[3]) / 2, r[1], 0);
        Assert.Equal(20, r[6], 0);
        Assert.Equal(50, r[7], 0);
    }

    /// <summary>
    /// A modal dialog sized by its content is centred down as well as across, and an open one across where it
    /// stands, the paragraph after it starting there too. A modal one stood at the top as wide as the page, and an
    /// open one was a block in the flow (measured).
    /// </summary>
    [Fact(Timeout = 600000)]
    public void Dialogs_Are_Where_Chromium_Puts_Them()
    {
        using var server = new LoopbackHttpServer();
        using var window = Open(server,
            "<p id=\"before\" style=\"margin: 0; height: 40px\">before</p><dialog id=\"n\">open</dialog><p id=\"after\" style=\"margin: 0\">after</p>" +
            "<dialog id=\"d\">dialogtext</dialog>",
            "document.getElementById('n').show(); document.getElementById('d').showModal();" +
            "document.getElementById('out').textContent = 'rectmarker ' + measure('d') + ' ' + measure('n') + ' ' + measure('after');");

        var r = Report(window, "rectmarker");
        Assert.Equal((r[4] - r[2]) / 2, r[0], 0);
        Assert.Equal((r[5] - r[3]) / 2, r[1], 0);
        Assert.Equal((r[4] - r[8]) / 2, r[6], 0);
        Assert.Equal(40, r[7], 0);
        Assert.Equal(40, r[13], 0);
    }
}
