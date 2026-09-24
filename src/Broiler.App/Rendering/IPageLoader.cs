namespace Broiler.App.Rendering;

/// <summary>
/// Abstraction for fetching page content from a URI.
/// </summary>
public interface IPageLoader : IDisposable
{
    /// <summary>
    /// Fetch the raw HTML for the given request — a plain GET for a link, or a
    /// request with a body for a form that submits one.
    /// If the URL lacks a scheme, <c>https://</c> is prepended.
    /// Returns a tuple of (normalisedUrl, html), where normalisedUrl is the URL the
    /// document was served from after any redirect.
    /// </summary>
    Task<(string NormalisedUrl, string Html)> FetchAsync(PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the raw HTML for the given URL. Shorthand for a GET
    /// <see cref="PageRequest"/>.
    /// </summary>
    Task<(string NormalisedUrl, string Html)> FetchAsync(string url, CancellationToken cancellationToken = default) =>
        FetchAsync(PageRequest.ForUrl(url), cancellationToken);

    /// <summary>
    /// Load the document for <paramref name="request"/> and report what the response said about it:
    /// the final URL, the status, the headers the host may read, the redirect chain and the same-site
    /// status the transport computed.
    /// </summary>
    /// <remarks>
    /// A loader that only implements <see cref="FetchAsync(PageRequest, CancellationToken)"/> gets
    /// this for free, with a result that knows only the URL and the markup.
    /// </remarks>
    async Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        var (url, html) = await FetchAsync(request, cancellationToken).ConfigureAwait(false);
        return new PageLoadResult
        {
            FinalUrl = url,
            Html = html,
            Method = request.Method,
            RedirectChain = Uri.TryCreate(url, UriKind.Absolute, out Uri? final) ? [final] : [],
        };
    }
}
