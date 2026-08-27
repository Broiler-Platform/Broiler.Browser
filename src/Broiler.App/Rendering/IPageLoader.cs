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
    /// Returns a tuple of (normalisedUrl, html).
    /// </summary>
    Task<(string NormalisedUrl, string Html)> FetchAsync(PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the raw HTML for the given URL. Shorthand for a GET
    /// <see cref="PageRequest"/>.
    /// </summary>
    Task<(string NormalisedUrl, string Html)> FetchAsync(string url, CancellationToken cancellationToken = default) =>
        FetchAsync(PageRequest.ForUrl(url), cancellationToken);
}
