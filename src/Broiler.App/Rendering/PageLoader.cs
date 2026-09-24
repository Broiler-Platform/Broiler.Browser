using System.Net.Http.Headers;
using System.Text;
using Broiler.Net.Cookies;
using Broiler.Net.Http;

namespace Broiler.App.Rendering;

/// <summary>
/// Fetches page content over HTTP(S) or from the local filesystem (<c>file://</c> URLs).
/// </summary>
/// <remarks>
/// <para>
/// <b>The browser loads pages through its profile's transport</b>
/// (<see cref="PageLoader(IBrowserRequestTransport)"/>): Broiler.Net's network session, which follows
/// redirects itself, sends the profile's cookies on every hop and stores every <c>Set-Cookie</c> it
/// receives — on a redirect and on an error response too — in the one store every other loader of the
/// profile uses. Each page request is a top-level navigation whose client is the document that started
/// it (<see cref="PageRequest.Initiator"/>), so the transport can tell a same-site navigation from a
/// cross-site one.
/// </para>
/// <para>
/// <b>A loader over a plain <see cref="HttpClient"/></b> (<see cref="PageLoader(HttpClient, bool)"/>)
/// remains for hosts without a profile; whatever that client does with cookies and redirects is its
/// own business.
/// </para>
/// <para>
/// <b>Error statuses.</b> A response outside 2xx still fails the load with
/// <see cref="HttpRequestException"/>, as it always has — but only once the response has been
/// received, so its cookies are already stored. A consent or login flow that answers an error while
/// setting the cookie it needs keeps working.
/// </para>
/// </remarks>
public sealed class PageLoader : IPageLoader
{
    /// <summary>
    /// How long a page load through the transport may take, from sending the request to the last
    /// byte of the document: the 100 seconds <see cref="HttpClient.Timeout"/> gave the loader before.
    /// </summary>
    internal static readonly TimeSpan DefaultNavigationTimeout = TimeSpan.FromSeconds(100);

    private readonly IBrowserRequestTransport? transport;
    private readonly HttpClient? httpClient;
    private readonly bool ownsHttpClient;
    private readonly TimeSpan navigationTimeout = DefaultNavigationTimeout;

    /// <summary>
    /// Creates a loader that sends every page request through <paramref name="transport"/>, the
    /// profile's network session. The loader never disposes it: the profile owns it and it outlives
    /// every navigation.
    /// </summary>
    public PageLoader(IBrowserRequestTransport transport)
        : this(transport, DefaultNavigationTimeout)
    {
    }

    /// <summary>
    /// A loader over <paramref name="transport"/> whose loads fail with <see cref="TimeoutException"/>
    /// once <paramref name="navigationTimeout"/> has passed without the whole document.
    /// </summary>
    internal PageLoader(IBrowserRequestTransport transport, TimeSpan navigationTimeout)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(navigationTimeout, TimeSpan.Zero);
        this.transport = transport;
        this.navigationTimeout = navigationTimeout;
    }

    /// <summary>
    /// Creates a new <see cref="PageLoader"/> over <paramref name="httpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The client page requests are issued on.  Callers should reuse a single long-lived
    /// instance: it is the connection pool, so one per navigation both re-opens a
    /// connection to a host already connected to and churns the pool.
    /// </param>
    /// <param name="ownsHttpClient">
    /// Whether <see cref="Dispose"/> disposes <paramref name="httpClient"/>.  The default is
    /// <see langword="false"/> — the client belongs to the caller and outlives the loader.
    /// Disposing a shared client is not a leak-free tidy-up: it tears down the connection
    /// pool, which closes pooled connections the pool's scavenger may have armed with a
    /// pending zero-byte read-ahead, and that read then fails with
    /// <see cref="System.Net.Sockets.SocketError.OperationAborted"/> — see
    /// <c>docs/browser-connection-pool-aborts.md</c>.  Pass <see langword="true"/> only for a
    /// client created solely for this loader.
    /// </param>
    public PageLoader(HttpClient httpClient, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
    }

    /// <inheritdoc />
    public async Task<(string NormalisedUrl, string Html)> FetchAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        PageLoadResult result = await LoadAsync(request, cancellationToken).ConfigureAwait(false);
        return (result.FinalUrl, result.Html);
    }

    /// <inheritdoc />
    public Task<(string NormalisedUrl, string Html)> FetchAsync(string url, CancellationToken cancellationToken = default) =>
        FetchAsync(PageRequest.ForUrl(url), cancellationToken);

    /// <inheritdoc />
    public async Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string url = request.Url;
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            // Local, and so outside the network entirely: no cookies are sent or stored, and the
            // document it makes is cookie-averse.
            var uri = new Uri(url);

            // Only the user, or a page that is itself a local file, opens a local file; and only the
            // user a file on a share, which Windows would otherwise reach by opening an SMB session to
            // a host a page named, with the user's credentials.
            if (request.Initiator is { } initiator && (!initiator.DocumentUrl.IsFile || uri.IsUnc))
                throw new UnauthorizedAccessException($"A page may not navigate to the local file {url}.");

            var localPath = uri.LocalPath;
            if (!File.Exists(localPath))
                throw new FileNotFoundException($"Local file not found: {localPath}", localPath);
            var fileText = await File.ReadAllTextAsync(localPath, cancellationToken).ConfigureAwait(false);
            return new PageLoadResult
            {
                FinalUrl = url,
                Html = fileText,
                Method = request.Method,
                RedirectChain = [uri],
            };
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        Uri target = new(url);
        return transport is not null
            ? await LoadThroughTransportAsync(transport, target, request, navigationTimeout, cancellationToken).ConfigureAwait(false)
            : await LoadThroughClientAsync(httpClient!, target, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The top-level navigation request for <paramref name="request"/>: its client is the document
    /// that started it, or none for the browser's own UI.
    /// </summary>
    /// <remarks>
    /// A reload from the browser's UI has no initiator to judge by, so RFC 6265bis (section 5.2) makes
    /// it as same-site as the navigation that loaded the document being reloaded, which the history
    /// entry recorded. An entry that never finished loading has no record, and is then replayed as the
    /// navigation that created it.
    /// </remarks>
    internal static RequestContext NavigationContext(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.NavigationType == PageNavigationType.Reload && request.RecordedSameSite is { } recorded)
        {
            return RequestContext.TopLevelNavigation(initiator: null) with
            {
                IsUserReload = true,
                ReloadWasSameSite = recorded == SameSiteStatus.SameSite,
            };
        }

        return RequestContext.TopLevelNavigation(request.Initiator);
    }

    /// <remarks>
    /// <para>
    /// <b>One budget for the whole document.</b> The transport's own timeout ends at the final
    /// response's headers; the body is read afterwards, and a server that sends the headers and then
    /// stalls would otherwise hold the navigation at "Loading…" until the user stopped it. The budget
    /// runs from the send to the last byte, as the <see cref="HttpClient"/> this loader used before
    /// gave it, and running out is a <see cref="TimeoutException"/>, which the browser shows as a
    /// failed load. Only <paramref name="cancellationToken"/> — the navigation's own — cancels.
    /// </para>
    /// </remarks>
    private static async Task<PageLoadResult> LoadThroughTransportAsync(
        IBrowserRequestTransport transport,
        Uri url,
        PageRequest request,
        TimeSpan navigationTimeout,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(navigationTimeout);

        try
        {
            return await LoadThroughTransportCoreAsync(transport, url, request, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{url} did not load within {navigationTimeout.TotalSeconds:0.#} seconds.", ex);
        }
    }

    private static async Task<PageLoadResult> LoadThroughTransportCoreAsync(
        IBrowserRequestTransport transport,
        Uri url,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = CreateMessage(url, request);
        using TransportResponse response = await transport
            .SendAsync(message, NavigationContext(request), cancellationToken)
            .ConfigureAwait(false);

        // The transport stored this response's cookies, and every redirect hop's, before it returned.
        response.Message.EnsureSuccessStatusCode();

        string html = await response.Message.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new PageLoadResult
        {
            FinalUrl = response.FinalUrl.AbsoluteUri,
            Html = html,
            StatusCode = response.StatusCode,
            Method = FinalMethod(response.Message, message),
            // A navigation's response is basic, so this is every field but Set-Cookie and Set-Cookie2.
            Headers = response.GetScriptVisibleHeaders(),
            RedirectChain = response.UrlList,
            SameSite = response.SameSite,
        };
    }

    private static async Task<PageLoadResult> LoadThroughClientAsync(
        HttpClient client,
        Uri url,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = CreateMessage(url, request);
        using HttpResponseMessage response = await client
            .SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // A client that follows redirects itself points the response's request at where it landed.
        Uri finalUrl = response.RequestMessage?.RequestUri ?? url;
        IEnumerable<KeyValuePair<string, string>> fields = response.Headers.NonValidated
            .Concat(response.Content.Headers.NonValidated)
            .SelectMany(field => field.Value.Select(value => new KeyValuePair<string, string>(field.Key, value)));

        return new PageLoadResult
        {
            FinalUrl = finalUrl.AbsoluteUri,
            Html = html,
            StatusCode = (int)response.StatusCode,
            Method = FinalMethod(response, message),
            Headers = FetchHeaders.FilterResponseHeaders([.. fields], ResponseTainting.Basic, credentialed: true),
            RedirectChain = finalUrl == url ? [url] : [url, finalUrl],
        };
    }

    private static HttpRequestMessage CreateMessage(Uri url, PageRequest request)
    {
        // A request without a body is a GET, whatever it names: that is what a link or a GET form
        // makes, and it is what this loader has always sent for one.
        if (!request.HasBody)
            return new HttpRequestMessage(HttpMethod.Get, url);

        return new HttpRequestMessage(new HttpMethod(request.Method), url)
        {
            Content = CreateContent(request),
        };
    }

    /// <summary>
    /// The method of the request that got <paramref name="response"/>: the handler records the last
    /// hop's request on the response, which a redirect may have turned from a <c>POST</c> into a
    /// <c>GET</c>. Without one, the request as it was sent.
    /// </summary>
    private static string FinalMethod(HttpResponseMessage response, HttpRequestMessage sent) =>
        (response.RequestMessage?.Method ?? sent.Method).Method.ToUpperInvariant();

    private static HttpContent CreateContent(PageRequest request)
    {
        string mediaType = request.ContentType ?? PageRequest.FormUrlEncoded;

        // A multipart body carries file bytes verbatim, so it must not be re-encoded
        // as text. Its media type already includes the boundary parameter, which
        // StringContent's constructor would reject anyway.
        if (request.BinaryBody is { } bytes)
            return WithContentType(new ByteArrayContent(bytes), mediaType);

        return WithContentType(new StringContent(request.Body ?? string.Empty, Encoding.UTF8), mediaType);
    }

    /// <summary>
    /// Makes <paramref name="mediaType"/> the <em>only</em> <c>Content-Type</c> on
    /// <paramref name="content"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StringContent"/>'s constructor already sets one — <c>text/plain; charset=utf-8</c>
    /// for the <see cref="Encoding.UTF8"/> overload — and <c>TryAddWithoutValidation</c>
    /// <em>appends</em> to a header rather than replacing it. A urlencoded form therefore went out
    /// as the two-valued
    /// <c>Content-Type: text/plain; charset=utf-8, application/x-www-form-urlencoded</c>,
    /// and a server reading the first value sees <c>text/plain</c>, never decodes the body, and
    /// rejects the submission: google's cookie-consent dialog answered
    /// <c>POST https://consent.google.de/save</c> with <c>400 Bad Request</c>, which stranded a
    /// search behind the consent page. Removing the default first leaves the media type the form
    /// actually chose.
    /// </para>
    /// <para>
    /// The bytes stay UTF-8 whatever the media type says, so dropping the constructor's
    /// <c>charset=utf-8</c> does not change what is on the wire — and a bare
    /// <c>application/x-www-form-urlencoded</c> is what browsers send for a urlencoded form.
    /// Parsing into <see cref="System.Net.Http.Headers.HttpContentHeaders.ContentType"/> keeps
    /// parameters such as multipart's <c>boundary</c>; a value too malformed to parse is sent
    /// verbatim rather than costing the whole request.
    /// </para>
    /// </remarks>
    private static T WithContentType<T>(T content, string mediaType)
        where T : HttpContent
    {
        content.Headers.Remove("Content-Type");

        if (MediaTypeHeaderValue.TryParse(mediaType, out var parsed))
            content.Headers.ContentType = parsed;
        else
            content.Headers.TryAddWithoutValidation("Content-Type", mediaType);

        return content;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient?.Dispose();
    }
}
