using System.Drawing;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.HTML.Image;
using Broiler.Net.Http;
using static Broiler.Browser.Core.Tests.LoopbackHttpServer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The window paints the documents of a page's frames.
/// </summary>
/// <remarks>
/// The page's display list holds no frame's document. Broiler.HTML's image renderer and Broiler.Cli's
/// analysis composite one over its element's box; the window did not, so every frame was an empty box.
/// The reCAPTCHA checkbox on https://www.google.com/recaptcha/api2/demo is the whole of an iframe, and
/// the window showed a blank space where the analysis's screenshot showed the widget.
/// </remarks>
public class WindowFrameTests
{
    /// <summary>A frame's document is drawn inside its element's content box.</summary>
    [Fact(Timeout = 600000)]
    public void A_Frames_Document_Is_Painted_In_Its_Box()
    {
        const string Page = """
            <!DOCTYPE html><html><body style="margin: 0">
            <div style="height: 100px"></div>
            <iframe style="display: block; border: 0; margin-left: 50px; width: 300px; height: 100px"
                srcdoc="<body style='margin: 0'><p style='margin: 0'>framemarker</p></body>"></iframe>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(Page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        var (_, at) = window.Run("framemarker");
        Assert.InRange(at.X - window.PageArea.Left, 50, 350);
        Assert.InRange(at.Y - window.PageArea.Top, 100, 200);
    }

    /// <summary>
    /// A frame loaded from <c>src</c> shows its document as its own scripts left it, which is how the
    /// reCAPTCHA anchor frame builds its checkbox. The marker is assembled at run time: the
    /// frame's document keeps its script's source.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Frame_Shows_What_Its_Own_Scripts_Made_Of_It()
    {
        using var server = new LoopbackHttpServer();
        server
            .Map("/page", Reply.Text(FramedPage))
            .Map("/frame", Reply.Text(ScriptedFrame("")));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        var (_, at) = window.Run("scriptedmarker");
        Assert.InRange(at.Y - window.PageArea.Top, 0, 100);
    }

    /// <summary>
    /// A frame's stylesheet is a request of the frame's document on the window's profile: it carries
    /// the cookie the page set, which the cookie-less fallback clients would not send.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Frame_Loads_Its_Stylesheet_On_The_Windows_Profile()
    {
        using var server = new LoopbackHttpServer();
        server
            .Map("/page", Reply.Text(FramedPage, setCookies: ["sid=1; SameSite=Lax; Path=/"]))
            .Map("/frame", Reply.Text(ScriptedFrame("""<link rel="stylesheet" href="/frame.css">""")))
            .Map("/frame.css", request => Reply.Text(
                request.Cookie.Contains("sid=1", StringComparison.Ordinal)
                    ? "p { color: rgb(1, 2, 3) }"
                    : "p { color: rgb(200, 0, 0) }",
                "text/css"));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        var (run, _) = window.Run("scriptedmarker");
        Assert.Equal((1, 2, 3), (run.Color.R, run.Color.G, run.Color.B));
    }

    /// <summary>
    /// A web page's frame whose document has a <c>file:</c> URL is not painted: its document would
    /// load its stylesheets and images from the local file system.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Web_Pages_Frame_Is_Not_Painted_From_The_File_System()
    {
        const string Page = """
            <!DOCTYPE html><html><body style="margin: 0">
            <p style="margin: 0">pagemarker</p>
            <iframe style="display: block; border: 0; width: 300px; height: 100px"
                data-broiler-frame-document="<p style='margin: 0'>localmarker</p>"
                data-broiler-frame-base="file:///C:/"></iframe>
            </body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", Reply.Text(Page));
        using var window = new TestWindow(server.Url("/page"));
        window.Settle();

        window.Run("pagemarker");
        Assert.DoesNotContain(window.Texts(), static t => t.Text.Contains("localmarker", StringComparison.Ordinal));
    }

    /// <summary>
    /// A frame moves with the page: it is replayed at its box shifted by the scroll offset the page's
    /// own display list was built with.
    /// </summary>
    [Fact]
    public void A_Frame_Is_Placed_At_Its_Box_Shifted_By_The_Pages_Scroll()
    {
        const string Page = """
            <!DOCTYPE html><html><body style="margin: 0">
            <div style="height: 100px"></div>
            <iframe style="display: block; border: 0; margin-left: 50px; width: 300px; height: 100px"
                srcdoc="<p>frame</p>"></iframe>
            </body></html>
            """;

        using var page = CreateContainer(Page, "http://127.0.0.1/page", null, null, new SizeF(800, 600));
        page.PerformLayout(new RectangleF(0, 0, 800, 600));
        using var renderer = new BImageRenderer();
        using var frames = new FrameCompositor(CreateContainer);
        frames.Update(renderer, page);
        Assert.Equal(1, frames.Count);

        var list = new BRenderList();
        frames.Replay(list, new PointF(0, -40));

        var placed = Assert.IsType<BRenderCommand.PushTransform>(list.Commands[0]);
        Assert.Equal(new BPoint(50, 60), placed.Transform.Transform(new BPoint(0, 0)));
    }

    /// <summary>A page with one frame, which a script of the page reaches into, as reCAPTCHA's does.</summary>
    private const string FramedPage = """
        <!DOCTYPE html><html><body style="margin: 0">
        <iframe src="/frame" style="display: block; border: 0; width: 300px; height: 100px"></iframe>
        <script>var frameDocument = document.querySelector('iframe').contentDocument;</script>
        </body></html>
        """;

    private static string ScriptedFrame(string head) => $$"""
        <!DOCTYPE html><html><head>{{head}}</head><body style="margin: 0">
        <script>
        var p = document.createElement('p');
        p.style.margin = '0';
        p.textContent = 'scripted' + 'marker';
        document.body.appendChild(p);
        </script>
        </body></html>
        """;

    private static HtmlContainer CreateContainer(
        string html,
        string baseUrl,
        IBrowserRequestTransport? network,
        DocumentRequestContext? document,
        SizeF viewport)
    {
        var container = new HtmlContainer
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            BaseUrl = baseUrl,
            RequestTransport = network,
            DocumentContext = document,
            MaxSize = viewport,
        };
        container.SetHtmlWithStyleSet(html, baseUrl: baseUrl);
        return container;
    }
}
