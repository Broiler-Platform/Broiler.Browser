using System.Net.Http.Headers;
using System.Text;

namespace Broiler.App.Rendering;

/// <summary>
/// Fetches page content over HTTP(S) or from the local filesystem
/// (<c>file://</c> URLs) using <see cref="HttpClient"/>.
/// </summary>
public sealed class PageLoader : IPageLoader
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

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
        ArgumentNullException.ThrowIfNull(request);

        string url = request.Url;
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(url);
            var localPath = uri.LocalPath;
            if (!File.Exists(localPath))
                throw new FileNotFoundException($"Local file not found: {localPath}", localPath);
            var fileText = await File.ReadAllTextAsync(localPath, cancellationToken);
            return (url, fileText);
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!request.HasBody)
        {
            var fetched = await httpClient.GetStringAsync(new Uri(url), cancellationToken);
            return (url, fetched);
        }

        using HttpContent content = CreateContent(request);
        using HttpRequestMessage message = new(new HttpMethod(request.Method), new Uri(url))
        {
            Content = content,
        };

        using HttpResponseMessage response = await httpClient
            .SendAsync(message, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // A submission usually redirects; report where it landed so relative URLs on
        // the resulting page resolve against the right base.
        string finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
        return (finalUrl, html);
    }

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
    public Task<(string NormalisedUrl, string Html)> FetchAsync(string url, CancellationToken cancellationToken = default) =>
        FetchAsync(PageRequest.ForUrl(url), cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }
}
