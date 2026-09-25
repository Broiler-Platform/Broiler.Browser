using System.Collections.Concurrent;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Rendering;
using Broiler.Input;
using Broiler.Input.Mouse;
using Broiler.UI;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// The window styles a page for its own page area, and scrolls to what a URL's fragment names.
/// </summary>
/// <remarks>
/// <para>
/// A page is parsed on the load worker, before the window lays it out, and its media queries are
/// resolved during that parse. The container had no viewport yet and answered its 99999px default, so
/// every page was styled for a 99999px screen: MediaWiki's skin laid its widest-screen grid into the
/// window. <see cref="BrowserViewport.PageAreaSize"/> is what the worker passes now.
/// </para>
/// <para>
/// Nothing scrolled to a fragment. Acid2's "Take The Acid2 Test" link, <c>href="#top"</c>, reloaded
/// the page at the top and left the test's face far below the window.
/// </para>
/// </remarks>
public class WindowFragmentAndViewportTests
{
    /// <summary>
    /// The window is 800px wide. A query no wider than 2000px matches it and one of at least 5000px
    /// does not; styled for 99999px it was the other way round.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Page_Is_Styled_For_The_Window_It_Is_Shown_In()
    {
        const string Page = """
            <!DOCTYPE html><html><head><style>
            #narrow, #wide { display: none }
            @media (max-width: 2000px) { #narrow { display: block } }
            @media (min-width: 5000px) { #wide { display: block } }
            </style></head><body><p id="narrow">narrowquery</p><p id="wide">widequery</p></body></html>
            """;

        using var server = new LoopbackHttpServer();
        server.Map("/page", LoopbackHttpServer.Reply.Text(Page));
        using var window = new Window(server.Url("/page"));
        window.Settle();

        var painted = string.Concat(window.Texts().Select(static t => t.Text));
        Assert.Contains("narrowquery", painted, StringComparison.Ordinal);
        Assert.DoesNotContain("widequery", painted, StringComparison.Ordinal);
    }

    /// <summary>A page navigated to with a fragment opens scrolled to the element it names.</summary>
    [Fact(Timeout = 600000)]
    public void A_Page_Loaded_With_A_Fragment_Opens_At_Its_Element()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", LoopbackHttpServer.Reply.Text(TallPage));
        using var window = new Window(server.Url("/page#target"));
        window.Settle();

        Assert.True(window.IsInView("targetmarker"), window.Describe());
        Assert.False(window.IsInView("topmarker"), window.Describe());
    }

    /// <summary>
    /// A link into the document on screen scrolls it to the element and loads nothing: the page is
    /// requested once, before the click and not after.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Link_To_A_Fragment_Scrolls_Without_Loading_The_Page_Again()
    {
        using var server = new LoopbackHttpServer();
        server.Map("/page", LoopbackHttpServer.Reply.Text(TallPage));
        using var window = new Window(server.Url("/page"));
        window.Settle();
        Assert.True(window.IsInView("topmarker"), window.Describe());

        // On the link's own text, where the window draws it.
        var link = window.Texts().First(static t => t.Text.Contains("topmarker", StringComparison.Ordinal)).At;
        window.Click(link.X + 4, link.Y + 4);
        window.Settle();

        Assert.True(window.IsInView("targetmarker"), window.Describe());
        Assert.Single(server.RequestsFor("/page"));
    }

    private const string TallPage = """
        <!DOCTYPE html><html><body style="margin: 0">
        <a href="#target">topmarker</a>
        <div style="height: 3000px"></div>
        <p id="target">targetmarker</p>
        <div style="height: 3000px"></div>
        </body></html>
        """;

    /// <summary>A whole browser over a headless host, 800×600, run until its page is done.</summary>
    private sealed class Window : IDisposable
    {
        private readonly ConcurrentQueue<Action> _posted = new();
        private readonly BrowserUiHost _host;
        private readonly BImageRenderer _renderer = new();
        private readonly BrowserApp _app;
        private BRenderList? _frame;
        private long _sequence;

        public Window(string url)
        {
            _host = new BrowserUiHost(
                static () => new BSize(800, 600),
                static () => 1.0,
                static () => { },
                static _ => { },
                action => { _posted.Enqueue(action); return true; });
            _app = new BrowserApp(_host, () => _renderer, url, static _ => { });
        }

        public void Settle()
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                while (_posted.TryDequeue(out Action? action))
                    action();

                if (_app.HasPendingWork)
                    _app.StepAnimation();

                if (_host.IsInvalidated)
                    _frame = _app.RenderFrame();

                if (string.Equals(_app.Status, "Done", StringComparison.Ordinal) && _posted.IsEmpty && !_host.IsInvalidated)
                    break;

                Thread.Sleep(5);
            }

            Assert.Equal("Done", _app.Status);
            _frame = _app.RenderFrame();
        }

        /// <summary>Every text run of the last frame, with where it was drawn in the window.</summary>
        public IReadOnlyList<(string Text, BPoint At)> Texts()
        {
            var texts = new List<(string, BPoint)>();
            var transforms = new Stack<BMatrix3x2>();
            var current = BMatrix3x2.Identity;
            foreach (var command in _frame?.Commands ?? [])
            {
                switch (command)
                {
                    case BRenderCommand.PushTransform push:
                        transforms.Push(current);
                        current = push.Transform * current;
                        break;
                    case BRenderCommand.PopTransform when transforms.Count > 0:
                        current = transforms.Pop();
                        break;
                    case BRenderCommand.DrawText text:
                        texts.Add((text.Text.Text, current.Transform(text.Origin)));
                        break;
                }
            }

            return texts;
        }

        /// <summary>Whether <paramref name="marker"/> was drawn inside the 600px-tall window.</summary>
        public bool IsInView(string marker) =>
            Texts().Any(t => t.Text.Contains(marker, StringComparison.Ordinal) && t.At.Y >= 0 && t.At.Y < 600);

        public string Describe() =>
            string.Join("; ", Texts().Where(static t => t.Text.Contains("marker", StringComparison.Ordinal)).Select(static t => $"{t.Text}@{t.At.Y:0}"));

        public void Click(double x, double y)
        {
            _app.Dispatch(Button(x, y, MouseButtons.Left, MouseButtonTransition.Down));
            _app.Dispatch(Button(x, y, MouseButtons.None, MouseButtonTransition.Up));
        }

        private UiInputEvent Button(double x, double y, MouseButtons held, MouseButtonTransition transition) =>
            UiInputEvent.FromMouseButton(new MouseButtonEvent(
                new InputEventHeader(
                    InputDeviceId.FromOpaqueValue("test-pointer"),
                    new InputTimestamp(++_sequence, TimeSpan.TicksPerSecond, "test"),
                    _sequence),
                InputPoint.ClientDeviceIndependentPixels(x, y),
                held,
                MouseButton.Left,
                transition,
                InputEventSource.Synthetic));

        public void Dispose()
        {
            _app.Dispose();
            _renderer.Dispose();
            _host.Dispose();
        }
    }
}
