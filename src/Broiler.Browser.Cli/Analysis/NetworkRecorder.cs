using System.Diagnostics;
using System.Globalization;
using System.Net;
using Broiler.Net.Http;

namespace Broiler.Cli.Analysis;

// Disambiguate the unqualified `DateTime` type: the Broiler.JS engine exposes a top-level
// `Broiler.DateTime` namespace which, from this `Broiler.*` namespace, otherwise shadows
// System.DateTime by simple-name lookup.
using DateTime = System.DateTime;

/// <summary>What became of a response body.</summary>
internal enum BodyState
{
    /// <summary>No response arrived, or the body was never opened.</summary>
    NotRead,

    /// <summary>The reader stopped before the end of the body.</summary>
    Partial,

    /// <summary>The whole body was read, and captured.</summary>
    Complete,

    /// <summary>The whole body was read, but it was larger than the capture limit.</summary>
    TooLargeToKeep,
}

/// <summary>One request the page's profile sent, as <c>network.json</c> records it.</summary>
internal sealed record NetworkEntry
{
    public required int Id { get; init; }
    public required DateTime Started { get; init; }
    public required string Method { get; init; }
    public required string Url { get; init; }

    /// <summary>Fetch's destination: <c>document</c>, <c>script</c>, <c>style</c>, <c>image</c>, <c>font</c>, <c>empty</c> for fetch/XHR, …</summary>
    public required string Destination { get; init; }

    public required string Mode { get; init; }

    /// <summary>The document the request was made for, when it has one.</summary>
    public string? Initiator { get; init; }

    /// <summary>Whether a synchronous loader asked — the renderer's images, stylesheets and fonts.</summary>
    public bool Synchronous { get; init; }

    public IReadOnlyList<KeyValuePair<string, string>> RequestHeaders { get; init; } = [];
    public int? Status { get; init; }
    public string? StatusText { get; init; }
    public string? HttpVersion { get; init; }
    public string? FinalUrl { get; init; }

    /// <summary>Every URL the request passed through, the first included, when it was redirected.</summary>
    public IReadOnlyList<string> Redirects { get; init; } = [];

    public string? ContentType { get; init; }
    public long? ContentLength { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; } = [];
    public string? Tainting { get; init; }

    /// <summary>Milliseconds from the request to its response headers, or to its failure.</summary>
    public double HeadersMs { get; init; }

    /// <summary>Milliseconds from the request to the last byte of its body, once that was read.</summary>
    public double? CompleteMs { get; init; }

    public BodyState Body { get; init; }
    public long BodyBytes { get; init; }

    /// <summary>The body's file under <c>resources/</c>, when it was kept.</summary>
    public string? SavedAs { get; init; }

    /// <summary>Why the request produced no response, when it did not.</summary>
    public string? Error { get; init; }

    /// <summary>The transport's classification of <see cref="Error"/>, e.g. <c>Blocked</c> or <c>Cors</c>.</summary>
    public string? ErrorKind { get; init; }

    /// <summary>A failure while reading the body, after the headers arrived.</summary>
    public string? BodyError { get; init; }

    /// <summary>Whether the request failed or was answered with an HTTP error status.</summary>
    public bool IsFailure => Error is not null || Status is >= 400 || BodyError is not null;
}

/// <summary>
/// Records every request that goes through the page's network — the document, its scripts, its
/// stylesheets, its images and fonts, its <c>fetch()</c> and XHR calls — and keeps each response body.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it sits.</b> The profile's <see cref="BrowserNetworkSession"/> is the one transport every
/// load of a page goes through, whichever component asks: the page loader, the script extractor, the
/// DOM bridge's loaders, the layout view and the renderer's container. Wrapping it once is therefore
/// the whole network, with each request's <see cref="RequestDestination"/> saying what asked for it —
/// which no single component's own trace can do.
/// </para>
/// <para>
/// <b>It must not change what the page reads.</b> The obvious way to keep a body — buffer it before
/// handing the response on — changes behaviour: a streamed <c>fetch()</c> would wait for a stream
/// that never ends, and a large download would sit in memory before its reader saw a byte. So the
/// body is teed instead. The response goes back with its content wrapped, the reader reads exactly
/// what it would have read, and the bytes are copied aside as they pass — up to
/// <see cref="MaxCapturedBody"/> — and archived when the reader reaches the end. A body the page
/// never reads is recorded as never read, which is itself a finding.
/// </para>
/// </remarks>
internal sealed class NetworkRecorder
{
    /// <summary>Bodies up to this size are kept; a larger one is read through and counted, not kept.</summary>
    internal const long MaxCapturedBody = 32L * 1024 * 1024;

    /// <summary>Headers whose values are credentials, and are not written to disk.</summary>
    private static readonly HashSet<string> RedactedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cookie", "Set-Cookie", "Authorization", "Proxy-Authorization",
    };

    private readonly Lock _sync = new();
    private readonly List<NetworkEntry> _entries = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Func<NetworkEntry, byte[], string?>? _archive;

    /// <param name="archive">
    /// Keeps a complete body and returns the file it was kept as, or null when it was not kept.
    /// Called on whatever thread finished reading the body.
    /// </param>
    public NetworkRecorder(Func<NetworkEntry, byte[], string?>? archive = null) => _archive = archive;

    /// <summary>Raised when a request has its response headers, or has failed.</summary>
    public event Action<NetworkEntry>? Responded;

    /// <summary>Wraps <paramref name="inner"/> so that every request through it is recorded.</summary>
    public IBrowserRequestTransport Wrap(IBrowserRequestTransport inner) => new RecordingTransport(inner, this);

    /// <summary>The requests so far, in the order they were sent.</summary>
    public IReadOnlyList<NetworkEntry> Snapshot()
    {
        lock (_sync)
            return [.. _entries];
    }

    private double Now => _clock.Elapsed.TotalMilliseconds;

    private (int Index, double StartMs) Begin(HttpRequestMessage request, RequestContext context, bool synchronous)
    {
        var entry = new NetworkEntry
        {
            Id = 0,
            Started = DateTime.UtcNow,
            Method = request.Method.Method,
            Url = request.RequestUri?.AbsoluteUri ?? "(no URL)",
            Destination = context.Destination.ToString().ToLowerInvariant(),
            Mode = context.Mode.ToString().ToLowerInvariant(),
            Initiator = context.Client?.DocumentUrl?.AbsoluteUri,
            Synchronous = synchronous,
            RequestHeaders = Headers(request.Headers, request.Content?.Headers),
        };

        lock (_sync)
        {
            var index = _entries.Count;
            _entries.Add(entry with { Id = index + 1 });
            return (index, Now);
        }
    }

    private void Respond(int index, double startMs, TransportResponse response)
    {
        NetworkEntry updated;
        var message = response.Message;
        lock (_sync)
        {
            updated = _entries[index] with
            {
                Status = response.StatusCode,
                StatusText = message.ReasonPhrase,
                HttpVersion = "HTTP/" + message.Version.ToString(2),
                FinalUrl = response.FinalUrl.AbsoluteUri,
                Redirects = response.Redirected ? [.. response.UrlList.Select(static u => u.AbsoluteUri)] : [],
                ContentType = message.Content.Headers.ContentType?.ToString(),
                ContentLength = message.Content.Headers.ContentLength,
                ResponseHeaders = Headers(message.Headers, message.Content.Headers),
                Tainting = response.Tainting.ToString().ToLowerInvariant(),
                HeadersMs = Math.Round(Now - startMs, 1),
                Body = BodyState.NotRead,
            };
            _entries[index] = updated;
        }

        message.Content = new TeeContent(message.Content, new BodyTap(this, index, startMs));
        Responded?.Invoke(updated);
    }

    private void Fail(int index, double startMs, Exception exception)
    {
        NetworkEntry updated;
        lock (_sync)
        {
            updated = _entries[index] with
            {
                Error = ExceptionText.SafeMessage(exception),
                ErrorKind = exception switch
                {
                    TransportException transport => transport.Error.ToString(),
                    OperationCanceledException => "Canceled",
                    HttpRequestException { HttpRequestError: var error } => error.ToString(),
                    _ => exception.GetType().Name,
                },
                HeadersMs = Math.Round(Now - startMs, 1),
            };
            _entries[index] = updated;
        }

        Responded?.Invoke(updated);
    }

    private void BodyFinished(int index, double startMs, byte[]? captured, long total, BodyState state, string? error)
    {
        NetworkEntry entry;
        lock (_sync)
        {
            entry = _entries[index] = _entries[index] with
            {
                Body = state,
                BodyBytes = total,
                BodyError = error,
                CompleteMs = Math.Round(Now - startMs, 1),
            };
        }

        if (state != BodyState.Complete || captured is null || _archive is null)
            return;

        string? savedAs;
        try
        {
            savedAs = _archive(entry, captured);
        }
        catch (Exception)
        {
            savedAs = null;
        }

        if (savedAs is null)
            return;

        lock (_sync)
            _entries[index] = _entries[index] with { SavedAs = savedAs };
    }

    private static IReadOnlyList<KeyValuePair<string, string>> Headers(
        System.Net.Http.Headers.HttpHeaders headers,
        System.Net.Http.Headers.HttpHeaders? contentHeaders)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var header in headers.NonValidated)
            Add(list, header.Key, header.Value.ToString());
        if (contentHeaders is not null)
        {
            foreach (var header in contentHeaders.NonValidated)
                Add(list, header.Key, header.Value.ToString());
        }

        return list;

        static void Add(List<KeyValuePair<string, string>> list, string name, string value) =>
            list.Add(new(name, RedactedHeaders.Contains(name) ? "(redacted)" : value));
    }

    /// <summary>The profile's transport, with every request through it recorded.</summary>
    private sealed class RecordingTransport(IBrowserRequestTransport inner, NetworkRecorder recorder) : IBrowserRequestTransport
    {
        public async Task<TransportResponse> SendAsync(
            HttpRequestMessage request,
            RequestContext context,
            CancellationToken cancellationToken = default)
        {
            var (index, start) = recorder.Begin(request, context, synchronous: false);
            TransportResponse response;
            try
            {
                response = await inner.SendAsync(request, context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                recorder.Fail(index, start, ex);
                throw;
            }

            recorder.Respond(index, start, response);
            return response;
        }

        public TransportResponse Send(
            HttpRequestMessage request,
            RequestContext context,
            CancellationToken cancellationToken = default)
        {
            var (index, start) = recorder.Begin(request, context, synchronous: true);
            TransportResponse response;
            try
            {
                response = inner.Send(request, context, cancellationToken);
            }
            catch (Exception ex)
            {
                recorder.Fail(index, start, ex);
                throw;
            }

            recorder.Respond(index, start, response);
            return response;
        }
    }

    /// <summary>
    /// One response body on its way to its reader: counts and copies what passes, and reports the
    /// outcome once — at the end of the body, or when the reader lets go of it early.
    /// </summary>
    private sealed class BodyTap(NetworkRecorder recorder, int index, double startMs)
    {
        private readonly Lock _sync = new();
        private MemoryStream? _copy = new();
        private long _total;
        private int _finished;

        public Stream Wrap(Stream inner) => new TeeStream(inner, this);

        public void Observe(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
                return;

            lock (_sync)
            {
                _total += bytes.Length;
                if (_copy is null)
                    return;

                if (_copy.Length + bytes.Length > MaxCapturedBody)
                {
                    // Keep counting, stop copying: the reader still gets every byte.
                    _copy = null;
                    return;
                }

                _copy.Write(bytes);
            }
        }

        public void Ended() => Finish(atEnd: true, error: null);

        public void Failed(Exception exception) => Finish(atEnd: false, ExceptionText.SafeMessage(exception));

        public void Abandoned() => Finish(atEnd: false, error: null);

        private void Finish(bool atEnd, string? error)
        {
            if (Interlocked.Exchange(ref _finished, 1) == 1)
                return;

            byte[]? captured;
            long total;
            lock (_sync)
            {
                captured = _copy?.ToArray();
                total = _total;
                _copy = null;
            }

            var state = !atEnd
                ? (total == 0 && error is null ? BodyState.NotRead : BodyState.Partial)
                : captured is null ? BodyState.TooLargeToKeep : BodyState.Complete;

            recorder.BodyFinished(index, startMs, atEnd ? captured : null, total, state, error);
        }
    }

    /// <summary>
    /// The response content handed back to the caller: the original content, read through a
    /// <see cref="TeeStream"/> by every path a reader can take — a stream, a buffer, a string, a copy.
    /// </summary>
    private sealed class TeeContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly BodyTap _tap;

        public TeeContent(HttpContent inner, BodyTap tap)
        {
            _inner = inner;
            _tap = tap;
            foreach (var header in inner.Headers.NonValidated)
                Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            await using var source = await CreateContentReadStreamAsync(cancellationToken).ConfigureAwait(false);
            await source.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            using var source = CreateContentReadStream(cancellationToken);
            source.CopyTo(stream);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync() =>
            _tap.Wrap(await _inner.ReadAsStreamAsync().ConfigureAwait(false));

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            _tap.Wrap(await _inner.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));

        protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
            _tap.Wrap(_inner.ReadAsStream(cancellationToken));

        protected override bool TryComputeLength(out long length)
        {
            var known = _inner.Headers.ContentLength;
            length = known ?? 0;
            return known.HasValue;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tap.Abandoned();
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>A read-only pass-through stream that shows every byte it passes to a <see cref="BodyTap"/>.</summary>
    private sealed class TeeStream(Stream inner, BodyTap tap) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read;
            try
            {
                read = inner.Read(buffer);
            }
            catch (Exception ex)
            {
                tap.Failed(ex);
                throw;
            }

            return Passed(buffer[..read], buffer.Length);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read;
            try
            {
                read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                tap.Failed(ex);
                throw;
            }

            return Passed(buffer.Span[..read], buffer.Length);
        }

        private int Passed(ReadOnlySpan<byte> bytes, int requested)
        {
            tap.Observe(bytes);

            // A zero-byte read of a non-empty request is the end of the body.
            if (bytes.Length == 0 && requested > 0)
                tap.Ended();

            return bytes.Length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tap.Abandoned();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            tap.Abandoned();
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The requests as an HTTP Archive (HAR 1.2), the format browser developer tools import, so a
    /// run's network can be opened in the same waterfall a browser shows for the page.
    /// </summary>
    public static object ToHar(IReadOnlyList<NetworkEntry> entries, string pageUrl, DateTime pageStarted, string creatorVersion)
    {
        return new
        {
            log = new
            {
                version = "1.2",
                creator = new { name = "Broiler.Cli --analyze", version = creatorVersion },
                pages = new[]
                {
                    new
                    {
                        startedDateTime = pageStarted.ToString("o", CultureInfo.InvariantCulture),
                        id = "page_1",
                        title = pageUrl,
                        pageTimings = new { onContentLoad = -1, onLoad = -1 },
                    },
                },
                entries = entries.Select(static e => new
                {
                    pageref = "page_1",
                    startedDateTime = e.Started.ToString("o", CultureInfo.InvariantCulture),
                    time = e.CompleteMs ?? e.HeadersMs,
                    request = new
                    {
                        method = e.Method,
                        url = e.Url,
                        httpVersion = e.HttpVersion ?? "HTTP/1.1",
                        cookies = Array.Empty<object>(),
                        headers = e.RequestHeaders.Select(static h => new { name = h.Key, value = h.Value }),
                        queryString = QueryString(e.Url),
                        headersSize = -1,
                        bodySize = -1,
                    },
                    response = new
                    {
                        status = e.Status ?? 0,
                        statusText = e.StatusText ?? e.Error ?? string.Empty,
                        httpVersion = e.HttpVersion ?? "HTTP/1.1",
                        cookies = Array.Empty<object>(),
                        headers = e.ResponseHeaders.Select(static h => new { name = h.Key, value = h.Value }),
                        content = new
                        {
                            size = e.BodyBytes,
                            mimeType = e.ContentType ?? "x-unknown",
                        },
                        redirectURL = e.Redirects.Count > 1 ? e.FinalUrl ?? string.Empty : string.Empty,
                        headersSize = -1,
                        bodySize = e.Body == BodyState.NotRead ? -1 : e.BodyBytes,
                    },
                    cache = new { },
                    timings = new
                    {
                        send = 0,
                        wait = e.HeadersMs,
                        receive = e.CompleteMs is { } complete ? Math.Max(0, Math.Round(complete - e.HeadersMs, 1)) : 0,
                    },
                    _destination = e.Destination,
                    _initiator = e.Initiator,
                    _error = e.Error,
                    _bodyState = e.Body.ToString(),
                    _savedAs = e.SavedAs,
                }),
            },
        };
    }

    private static IEnumerable<object> QueryString(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Query.Length <= 1)
            return [];

        return uri.Query[1..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(static pair =>
            {
                var eq = pair.IndexOf('=');
                return (object)new
                {
                    name = WebUtility.UrlDecode(eq < 0 ? pair : pair[..eq]),
                    value = eq < 0 ? string.Empty : WebUtility.UrlDecode(pair[(eq + 1)..]),
                };
            });
    }
}
