using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Broiler.Net.Http;

namespace Broiler.Cli.Tests;

/// <summary>
/// Every request a capture makes says who is making it.
/// </summary>
/// <remarks>
/// <para>
/// <c>HttpClient</c> sends no <c>User-Agent</c> unless one is configured, and a missing header is
/// not a milder version of an unrecognised one — it is grounds for refusal. Wikimedia's User-Agent
/// policy answers an unidentified request <c>403 Forbidden</c> without looking further, so
/// <c>https://www.mediawiki.org/wiki/MediaWiki</c> failed instantly in both the command line and the
/// browser, and went on failing per sub-resource once the document was fixed.
/// </para>
/// <para>
/// A capture sends its requests through the same profile network as the window, which sets the
/// header on every request it makes. So the assertion is per resource kind rather than per call
/// site: a header set on the document's request and nowhere else looks fixed from the outside and
/// still leaves a page script-less.
/// </para>
/// </remarks>
[Collection(CaptureCollection.Name)]
public sealed class UserAgentHeaderTests : IDisposable
{
    private readonly RecordingOrigin _origin = new();
    private readonly TestPages _pages = new();

    public void Dispose()
    {
        _origin.Dispose();
        _pages.Dispose();
    }

    private async Task CaptureAsync(string path)
    {
        await new CaptureService().CaptureAsync(new CaptureOptions
        {
            Url = _origin.Url(path),
            OutputPath = _pages.Output("out.html"),
        });
    }

    /// <summary>
    /// The reported bug exactly: the top-level document. This is the request that answered 403
    /// before Broiler had parsed a single byte.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task The_Document_Request_Is_Identified()
    {
        _origin.Serve("page.html", "text/html", "<!DOCTYPE html><html><body>hi</body></html>");

        await CaptureAsync("page.html");

        AssertEveryRequestIdentified("/page.html");
    }

    /// <summary>
    /// The sub-resource half. mediawiki.org served the document once the header was on that one
    /// request and still refused its <c>load.php</c> bootstrap, which is what left its modules
    /// unloaded.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task Script_Requests_Are_Identified()
    {
        _origin.Serve("app.js", "text/javascript", "document.getElementById('out').textContent = 'loaded';");
        _origin.Serve(
            "page.html",
            "text/html",
            """<!DOCTYPE html><html><body><div id="out"></div><script src="app.js"></script></body></html>""");

        await CaptureAsync("page.html");

        AssertEveryRequestIdentified("/app.js");
        Assert.Equal("loaded", CaptureServiceTests.TextOf(await File.ReadAllTextAsync(_pages.Output("out.html")), "out"));
    }

    /// <summary>
    /// The stylesheets a page's scripts can see are loaded by the page's bridge, on the same network.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task Stylesheet_Requests_Are_Identified()
    {
        _origin.Serve("sheet.css", "text/css", "#out { width: 10px; }");
        _origin.Serve(
            "page.html",
            "text/html",
            """<!DOCTYPE html><html><head><link rel="stylesheet" href="sheet.css"></head><body><div id="out"></div><script>document.getElementById('out').textContent = getComputedStyle(document.getElementById('out')).width;</script></body></html>""");

        await CaptureAsync("page.html");

        AssertEveryRequestIdentified("/sheet.css");
    }

    /// <summary>
    /// A page that reads <c>navigator.userAgent</c> and a server that reads the header are asking
    /// the same question, so they get the same answer. Two literals is how they drift apart.
    /// </summary>
    [Fact(Timeout = 600000)]
    public async Task What_Script_Is_Told_Is_What_The_Network_Is_Told()
    {
        _origin.Serve("page.html", "text/html", "<!DOCTYPE html><html><body></body></html>");
        var report = _pages.Output("report.json");

        await new CaptureService().EvaluatePageAsync(new PageEvaluationOptions
        {
            Url = _origin.Url("page.html"),
            OutputPath = report,
            Expressions = ["navigator.userAgent"],
        });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(report));
        var told = document.RootElement.GetProperty("evaluations")[0].GetProperty("value").GetString();

        Assert.Equal(Assert.Single(_origin.UserAgentsFor("/page.html")), told);
    }

    /// <summary>
    /// Asserts <paramref name="path"/> was requested, and that <em>every</em> request for it carried
    /// the header the profile's network sends.
    /// </summary>
    /// <remarks>
    /// Not "exactly one request": a script the speculative preload scan has already asked for may be
    /// asked for again, and how many round trips a resource costs is a different question from
    /// whether they identify themselves.
    /// </remarks>
    private void AssertEveryRequestIdentified(string path)
    {
        var seen = _origin.UserAgentsFor(path);

        Assert.NotEmpty(seen);
        Assert.All(seen, agent => Assert.Equal(BroilerUserAgent.Value, agent));
    }

    /// <summary>A loopback origin that answers a fixed set of paths and records each request's
    /// <c>User-Agent</c>.</summary>
    private sealed class RecordingOrigin : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<string, (string ContentType, string Body)> _routes = new();
        private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _userAgents = new();
        private readonly string _prefix;

        public RecordingOrigin()
        {
            // HttpListener cannot report an OS-assigned port, so one is taken by binding a socket
            // and closing it.
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _prefix = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(_prefix);
            _listener.Start();
            _ = Task.Run(Serve);
        }

        public string Url(string path) => _prefix + path;

        public void Serve(string path, string contentType, string body) =>
            _routes["/" + path] = (contentType, body);

        /// <summary>
        /// Every <c>User-Agent</c> seen on <paramref name="path"/>. A request that carried none
        /// contributes the empty string rather than nothing, so "the header was missing" fails the
        /// assertion instead of emptying the collection and passing a sloppier one.
        /// </summary>
        public string[] UserAgentsFor(string path) =>
            _userAgents.TryGetValue(path, out var seen) ? [.. seen] : [];

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    return; // Disposed.
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => Answer(context));
            }
        }

        private void Answer(HttpListenerContext context)
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            _userAgents.GetOrAdd(path, _ => new ConcurrentQueue<string>())
                .Enqueue(context.Request.Headers["User-Agent"] ?? string.Empty);

            try
            {
                if (_routes.TryGetValue(path, out var route))
                {
                    var bytes = Encoding.UTF8.GetBytes(route.Body);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = route.ContentType;
                    context.Response.ContentLength64 = bytes.Length;
                    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }

                context.Response.Close();
            }
            catch (HttpListenerException)
            {
                // The client gave up on the response; the header it sent is already recorded.
            }
        }

        public void Dispose()
        {
            _listener.Close();
            ((IDisposable)_listener).Dispose();
        }
    }
}
