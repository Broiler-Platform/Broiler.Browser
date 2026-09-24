using Broiler.Net.Cookies;

namespace Broiler.App.Rendering;

/// <summary>
/// What a page load produced: the document's markup, the URL it was actually served from, and the
/// parts of the response the host acts on.
/// </summary>
/// <remarks>
/// <para>
/// <b>The final URL, for every method.</b> A navigation that was redirected is at the URL the chain
/// ended on: that is the document's URL, the base its relative links resolve against, what the
/// address bar shows and what history records. The loader used to report it only for a request with
/// a body, so a <c>GET</c> that was redirected was treated as the page it was redirected away from.
/// </para>
/// <para>
/// <b>Headers the host may read.</b> <see cref="Headers"/> never holds <c>Set-Cookie</c> or
/// <c>Set-Cookie2</c>: the transport has already stored those, and nothing downstream — a page's
/// script least of all — gets to read them.
/// </para>
/// </remarks>
public sealed class PageLoadResult
{
    /// <summary>The URL the document was served from, after every redirect.</summary>
    public required string FinalUrl { get; init; }

    /// <summary>The document's markup.</summary>
    public required string Html { get; init; }

    /// <summary>The HTTP status of the final response; 200 for a local file.</summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>
    /// The method of the request that produced the final response: a <c>POST</c> that was
    /// redirected with <c>303</c> (or <c>301</c>/<c>302</c>) ends as a <c>GET</c>.
    /// </summary>
    public string Method { get; init; } = PageRequest.Get;

    /// <summary>
    /// The final response's header fields in the order received, repeated names kept, without
    /// <c>Set-Cookie</c> and <c>Set-Cookie2</c>.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>The URL first requested followed by every redirect target; its last entry is <see cref="FinalUrl"/>.</summary>
    public IReadOnlyList<Uri> RedirectChain { get; init; } = [];

    /// <summary>
    /// The same-site status the transport computed for the final hop, or <see langword="null"/> when
    /// the page did not come through the profile's transport (a local file, a loader over a plain
    /// <see cref="HttpClient"/>).
    /// </summary>
    public SameSiteStatus? SameSite { get; init; }

    /// <summary>Whether the request was redirected on its way to <see cref="FinalUrl"/>.</summary>
    public bool Redirected => RedirectChain.Count > 1;

    /// <summary>
    /// The policy the document's <c>Content-Security-Policy</c> response header delivered: the first
    /// non-empty field, or <see langword="null"/> when there is none. <c>-Report-Only</c> is not a
    /// delivered policy and is ignored.
    /// </summary>
    public string? ContentSecurityPolicy =>
        GetHeaderValues("Content-Security-Policy").FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>Every value of the header field <paramref name="name"/>, in the order received.</summary>
    public IEnumerable<string> GetHeaderValues(string name) =>
        Headers.Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value);
}
