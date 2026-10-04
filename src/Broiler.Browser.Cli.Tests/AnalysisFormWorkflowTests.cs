using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Broiler.Cli.Analysis;

namespace Broiler.Cli.Tests;

/// <summary>The consent and script-navigation workflow, without contacting an external service.</summary>
[Collection(CaptureCollection.Name)]
public sealed class AnalysisFormWorkflowTests
{
    [Fact(Timeout = 600000)]
    public async Task A_Selected_Form_Preserves_Cookies_And_Archives_The_Windows_429_Without_Executing_It()
    {
        using var pages = new TestPages();
        using var server = new ConsentServer();
        var output = pages.Output("consent-workflow");
        var console = new StringWriter();

        var result = await new PageAnalyzer(new PageAnalysisOptions
        {
            Url = server.Url("/consent"),
            OutputDirectory = output,
            SubmitForm = 0,
            Width = 320,
            Height = 240,
            TimeoutSeconds = 5,
            Watchdog = null,
        }, console).RunAsync();

        Assert.True(result == PageAnalyzer.Completed, console.ToString() + "\nReceived: " + string.Join("\n", server.Requests));
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "report.json")));
        Assert.Equal("Error loading page", report.RootElement.GetProperty("window").GetProperty("status").GetString());
        Assert.True(report.RootElement.GetProperty("window").GetProperty("settled").GetBoolean());
        Assert.Contains("/blocked", report.RootElement.GetProperty("scripting").GetProperty("navigationNotFollowed").GetString());

        using var network = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "network.json")));
        var requests = network.RootElement.EnumerateArray().ToArray();
        var analysis = requests.Where(static request => request.GetProperty("scope").GetString() == "analysis").ToArray();
        var window = requests.Where(static request => request.GetProperty("scope").GetString() == "window").ToArray();
        Assert.Equal(2, analysis.Length);
        Assert.Equal(3, window.Length);
        Assert.All(analysis, static request => Assert.Equal(200, request.GetProperty("status").GetInt32()));
        var refusal = Assert.Single(window, static request => request.GetProperty("status").GetInt32() == 429);
        Assert.Equal(server.Url("/blocked"), refusal.GetProperty("finalUrl").GetString());
        Assert.Equal("complete", refusal.GetProperty("body").GetString());
        Assert.Contains(refusal.GetProperty("responseHeaders").EnumerateArray(), static header =>
            header.GetProperty("key").GetString() == "Retry-After" && header.GetProperty("value").GetString() == "120");
        var savedAs = refusal.GetProperty("savedAs").GetString();
        Assert.NotNull(savedAs);
        Assert.Equal(ConsentServer.Refusal, await File.ReadAllTextAsync(Path.Combine(output, "resources", savedAs!)));
        Assert.Contains("bootstrap-marker", await File.ReadAllTextAsync(Path.Combine(output, "document-as-fetched.html")));
        Assert.DoesNotContain("error-document-executed", await File.ReadAllTextAsync(Path.Combine(output, "javascript-errors.log")));

        // Two fresh profiles each perform consent and POST/redirect. Only the window follows the
        // script replacement, and the 429 produces no retry despite its Retry-After header.
        var received = server.Requests;
        Assert.Equal(7, received.Count);
        Assert.Equal(2, received.Count(static request => request.Path == "/consent"));
        Assert.Equal(2, received.Count(static request => request.Path == "/bootstrap"));
        Assert.Single(received, static request => request.Path == "/blocked");
        var posts = received.Where(static request => request.Path == "/save").ToArray();
        Assert.Equal(2, posts.Length);
        Assert.All(posts, static request =>
        {
            Assert.Equal("POST", request.Method);
            Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
            Assert.Equal("choice=reject&continue=%2Fbootstrap%3Fq%3Dtest%26x%3D1", request.Body);
            Assert.StartsWith("probe=run", request.Cookie, StringComparison.Ordinal);
        });
        Assert.NotEqual(posts[0].Cookie, posts[1].Cookie);
    }

    private sealed class ConsentServer : IDisposable
    {
        internal const string Refusal = "<!doctype html><html><body><p>server-refusal-marker</p><script>throw new Error('error-document-executed');</script></body></html>";
        private const string Form = """
            <!doctype html><html><body><form action="/save" method="post">
            <input type="hidden" name="choice" value="reject">
            <input type="hidden" name="continue" value="/bootstrap?q=test&amp;x=1">
            <button type="submit" name="button" value="unused">Reject</button>
            </form></body></html>
            """;
        private const string Bootstrap = "<!doctype html><html><body><p>bootstrap-marker</p><script>location.replace('/blocked');</script></body></html>";
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly ConcurrentQueue<Request> _requests = new();
        private int _profiles;

        public ConsentServer()
        {
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        internal sealed record Request(string Method, string Path, string Cookie, string ContentType, string Body);
        public IReadOnlyList<Request> Requests => [.. _requests];
        public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stopping.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                    _ = ServeAsync(client);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) when (_stopping.IsCancellationRequested) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
                    var first = (await reader.ReadLineAsync(_stopping.Token))!.Split(' ');
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    while (await reader.ReadLineAsync(_stopping.Token) is { Length: > 0 } line)
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0)
                            headers[line[..colon]] = line[(colon + 1)..].Trim();
                    }
                    var count = headers.TryGetValue("Content-Length", out var length) ? int.Parse(length) : 0;
                    var chars = new char[count];
                    if (count > 0)
                        await reader.ReadBlockAsync(chars.AsMemory(), _stopping.Token);
                    var request = new Request(first[0], first[1].Split('?')[0], headers.GetValueOrDefault("Cookie", ""),
                        headers.GetValueOrDefault("Content-Type", ""), new string(chars));
                    _requests.Enqueue(request);

                    var (status, body, extra) = request.Path switch
                    {
                        "/consent" => (200, Form, $"Set-Cookie: probe=run{Interlocked.Increment(ref _profiles)}; Path=/; HttpOnly\r\n"),
                        "/save" when request.Method == "POST" && request.Cookie.StartsWith("probe=run", StringComparison.Ordinal)
                            && request.ContentType == "application/x-www-form-urlencoded"
                            && request.Body == "choice=reject&continue=%2Fbootstrap%3Fq%3Dtest%26x%3D1"
                            => (302, "", "Location: /bootstrap\r\n"),
                        "/bootstrap" => (200, Bootstrap, ""),
                        "/blocked" => (429, Refusal, "Retry-After: 120\r\n"),
                        _ => (400, "form-or-cookie-rejected", ""),
                    };
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Status\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\n{extra}Connection: close\r\n\r\n");
                    await stream.WriteAsync(head, _stopping.Token);
                    await stream.WriteAsync(bytes, _stopping.Token);
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            _stopping.Dispose();
        }
    }
}
