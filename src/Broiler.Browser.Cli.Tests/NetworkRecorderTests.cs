using System.Net;
using System.Text;
using System.Text.Json;
using Broiler.Cli.Analysis;
using Broiler.Net.Http;

namespace Broiler.Cli.Tests;

/// <summary>
/// The network recorder sees every request and keeps every body — without changing what the reader
/// of a response reads, or when.
/// </summary>
public sealed class NetworkRecorderTests
{
    private static readonly RequestContext Script = new() { Destination = RequestDestination.Script };

    [Fact(Timeout = 600000)]
    public async Task A_Read_Body_Is_Passed_Through_Unchanged_And_Archived()
    {
        var archived = new List<(NetworkEntry Entry, byte[] Bytes)>();
        var recorder = new NetworkRecorder((entry, bytes) =>
        {
            archived.Add((entry, bytes));
            return "0001-app.js";
        });
        var transport = recorder.Wrap(new FixedTransport("console.log('body-marker');"));

        using var response = await transport.SendAsync(Get("https://example.test/app.js"), Script);
        var text = await response.Message.Content.ReadAsStringAsync();

        Assert.Equal("console.log('body-marker');", text);
        var entry = Assert.Single(recorder.Snapshot());
        Assert.Equal("script", entry.Destination);
        Assert.Equal(200, entry.Status);
        Assert.Equal(BodyState.Complete, entry.Body);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), entry.BodyBytes);
        Assert.Equal("0001-app.js", entry.SavedAs);
        Assert.Equal(text, Encoding.UTF8.GetString(Assert.Single(archived).Bytes));
    }

    /// <summary>
    /// A body read through a stream is captured as it passes, which is what lets the recorder keep a
    /// body without buffering it first — a streamed response would otherwise never be handed on.
    /// </summary>
    [Fact(Timeout = 600000)]
    public void A_Streamed_Body_Is_Captured_As_It_Is_Read()
    {
        var recorder = new NetworkRecorder(static (_, _) => "kept");
        var transport = recorder.Wrap(new FixedTransport(new string('x', 100_000)));

        using var response = transport.Send(Get("https://example.test/big.txt"), Script);
        using (var stream = response.Message.Content.ReadAsStream())
        {
            var buffer = new byte[4096];
            while (stream.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        }

        var entry = Assert.Single(recorder.Snapshot());
        Assert.True(entry.Synchronous);
        Assert.Equal(BodyState.Complete, entry.Body);
        Assert.Equal(100_000, entry.BodyBytes);
    }

    [Fact(Timeout = 600000)]
    public async Task A_Body_Nobody_Finished_Reading_Is_Recorded_As_Partial()
    {
        var recorder = new NetworkRecorder(static (_, _) => "kept");
        var transport = recorder.Wrap(new FixedTransport(new string('y', 50_000)));

        using (var response = await transport.SendAsync(Get("https://example.test/partial.txt"), Script))
        using (var stream = await response.Message.Content.ReadAsStreamAsync())
        {
            var buffer = new byte[1000];
            Assert.True(await stream.ReadAsync(buffer) > 0);
        }

        var entry = Assert.Single(recorder.Snapshot());
        Assert.Equal(BodyState.Partial, entry.Body);
        Assert.Null(entry.SavedAs);
    }

    [Fact(Timeout = 600000)]
    public async Task A_Network_Error_Is_Recorded_And_Still_Thrown()
    {
        var recorder = new NetworkRecorder();
        var transport = recorder.Wrap(new FailingTransport());

        var thrown = await Assert.ThrowsAsync<TransportException>(() => transport.SendAsync(Get("https://example.test/blocked.js"), Script));

        var entry = Assert.Single(recorder.Snapshot());
        Assert.Equal(thrown.Message, entry.Error);
        Assert.Equal(nameof(TransportError.Blocked), entry.ErrorKind);
        Assert.True(entry.IsFailure);
    }

    [Fact(Timeout = 600000)]
    public async Task Credentials_Are_Not_Written_To_Disk()
    {
        var recorder = new NetworkRecorder();
        var transport = recorder.Wrap(new FixedTransport("ok", setCookie: "session=secret-marker"));
        var request = Get("https://example.test/login");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token-marker");

        using var response = await transport.SendAsync(request, Script);
        _ = await response.Message.Content.ReadAsStringAsync();

        var json = JsonSerializer.Serialize(recorder.Snapshot(), AnalysisJson.Options);
        Assert.DoesNotContain("secret-marker", json, StringComparison.Ordinal);
        Assert.DoesNotContain("token-marker", json, StringComparison.Ordinal);
        Assert.Contains("(redacted)", json, StringComparison.Ordinal);
    }

    [Fact(Timeout = 600000)]
    public async Task The_Journal_Is_Readable_Before_Disposal_And_Records_Archived_Bodies_Without_Credentials()
    {
        using var pages = new TestPages();
        var journal = pages.Output("network-events.jsonl");
        var recorder = new NetworkRecorder(static (_, _) => "body.html", journal);
        var transport = recorder.Wrap(new FixedTransport("body-marker", setCookie: "session=secret-marker"), "window");
        var request = Get("https://example.test/page");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer token-marker");
        request.Headers.TryAddWithoutValidation("Cookie", "input=secret-cookie");

        using var response = await transport.SendAsync(request, Script);
        Assert.Equal(2, File.ReadAllLines(journal).Length);
        _ = await response.Message.Content.ReadAsStringAsync();

        var text = await File.ReadAllTextAsync(journal);
        Assert.DoesNotContain("secret-marker", text, StringComparison.Ordinal);
        Assert.DoesNotContain("token-marker", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-cookie", text, StringComparison.Ordinal);
        var events = File.ReadAllLines(journal).Select(static line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        Assert.Equal(["request", "response", "body", "archived"], events.Select(static item => item.GetProperty("event").GetString()));
        var entry = events[^1].GetProperty("entry");
        Assert.Equal("window", entry.GetProperty("scope").GetString());
        Assert.Equal("complete", entry.GetProperty("body").GetString());
        Assert.Equal("body.html", entry.GetProperty("savedAs").GetString());
    }

    [Fact(Timeout = 600000)]
    public async Task A_Failed_Request_Is_Journaled_Before_It_Is_Thrown()
    {
        using var pages = new TestPages();
        var journal = pages.Output("network-failure.jsonl");
        var recorder = new NetworkRecorder(journalPath: journal);

        await Assert.ThrowsAsync<TransportException>(() => recorder.Wrap(new FailingTransport())
            .SendAsync(Get("https://example.test/blocked"), Script));

        var last = JsonSerializer.Deserialize<JsonElement>(File.ReadLines(journal).Last());
        Assert.Equal("failure", last.GetProperty("event").GetString());
        Assert.Equal("Blocked", last.GetProperty("entry").GetProperty("errorKind").GetString());
    }

    [Fact(Timeout = 600000)]
    public async Task Independent_Page_Runs_Are_Distinguished_In_Json_And_Har()
    {
        var recorder = new NetworkRecorder();
        foreach (var transport in new[] { recorder.Wrap(new FixedTransport("analysis")), recorder.Wrap(new FixedTransport("window"), "window") })
        {
            using var response = await transport.SendAsync(Get("https://example.test/page"), Script);
            _ = await response.Message.Content.ReadAsStringAsync();
        }

        Assert.Equal(["analysis", "window"], recorder.Snapshot().Select(static entry => entry.Scope));
        var har = JsonSerializer.SerializeToElement(
            NetworkRecorder.ToHar(recorder.Snapshot(), "https://example.test/page", System.DateTime.UtcNow, "1.0"),
            AnalysisJson.Options);
        var entries = har.GetProperty("log").GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal("analysis", entries[0].GetProperty("_scope").GetString());
        Assert.Equal("window", entries[1].GetProperty("_scope").GetString());
        Assert.Equal("before-session-processing", entries[1].GetProperty("_requestHeadersCapturePoint").GetString());
    }

    [Fact(Timeout = 600000)]
    public async Task The_Requests_Export_As_An_Http_Archive()
    {
        var recorder = new NetworkRecorder();
        var transport = recorder.Wrap(new FixedTransport("body"));
        using (var response = await transport.SendAsync(Get("https://example.test/page?a=1&b=two"), Script))
            _ = await response.Message.Content.ReadAsStringAsync();

        var har = JsonSerializer.SerializeToElement(
            NetworkRecorder.ToHar(recorder.Snapshot(), "https://example.test/page", System.DateTime.UtcNow, "1.0"),
            AnalysisJson.Options);

        var log = har.GetProperty("log");
        Assert.Equal("1.2", log.GetProperty("version").GetString());
        var entry = Assert.Single(log.GetProperty("entries").EnumerateArray());
        Assert.Equal(200, entry.GetProperty("response").GetProperty("status").GetInt32());
        Assert.Equal(2, entry.GetProperty("request").GetProperty("queryString").GetArrayLength());
    }

    private static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    /// <summary>Answers every request with the same body, as a stream, the way a socket does.</summary>
    private sealed class FixedTransport(string body, string? setCookie = null) : IBrowserRequestTransport
    {
        public Task<TransportResponse> SendAsync(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(Send(request, context, cancellationToken));

        public TransportResponse Send(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken = default)
        {
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body))),
                RequestMessage = request,
            };
            message.Content.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=utf-8");
            if (setCookie is not null)
                message.Headers.TryAddWithoutValidation("Set-Cookie", setCookie);

            return new TransportResponse(message, [request.RequestUri!], ResponseTainting.Basic);
        }
    }

    private sealed class FailingTransport : IBrowserRequestTransport
    {
        public Task<TransportResponse> SendAsync(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken = default) =>
            throw new TransportException(TransportError.Blocked, "blocked-marker");

        public TransportResponse Send(HttpRequestMessage request, RequestContext context, CancellationToken cancellationToken = default) =>
            throw new TransportException(TransportError.Blocked, "blocked-marker");
    }
}
