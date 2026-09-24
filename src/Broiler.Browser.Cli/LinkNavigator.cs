using Broiler.HTML.Core.Entities;
using Broiler.HTML.Image;
using System.Drawing;

namespace Broiler.Cli;

/// <summary>
/// Provides utilities for extracting and following links in rendered HTML.
/// Supports the Acid2 navigation pattern where a landing page link must be
/// followed to reach the actual test content.
/// </summary>
/// <remarks>
/// This only decides where to go. The followed page is loaded by the same pipeline as the one it
/// was linked from — as a navigation the landing page started, so the network judges it as that
/// page's request — and a diagnostics bundle records it the way it records every document.
/// </remarks>
public static class LinkNavigator
{
    /// <summary>
    /// Extracts all links from the given HTML by parsing it with html-renderer.
    /// </summary>
    /// <param name="html">The HTML content to extract links from.</param>
    /// <returns>A list of link data including href and bounding rectangle.</returns>
    public static List<LinkElementData<RectangleF>> ExtractLinks(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        using var container = new HtmlContainer();
        container.SetHtmlWithStyleSet(html);
        return container.GetLinks();
    }

    /// <summary>
    /// Extracts the href of the first link found in the given HTML.
    /// Returns <c>null</c> if no links are found.
    /// </summary>
    /// <param name="html">The HTML content to search for links.</param>
    /// <returns>The href of the first link, or <c>null</c> if none found.</returns>
    public static string? ExtractFirstLinkHref(string html)
    {
        var links = ExtractLinks(html);
        return links.Count > 0 ? links[0].Href : null;
    }

    /// <summary>
    /// Resolves a potentially relative URL against a base URL.
    /// </summary>
    /// <param name="baseUrl">The base URL to resolve against.</param>
    /// <param name="relativeUrl">The URL to resolve (may be absolute or relative).</param>
    /// <returns>The resolved absolute URL.</returns>
    public static string ResolveUrl(string baseUrl, string relativeUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(relativeUrl);

        // Only treat as absolute if it has a scheme (http://, https://, file://, etc.)
        if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absUri)
            && !string.IsNullOrEmpty(absUri.Scheme)
            && absUri.Scheme != "file")
        {
            return relativeUrl;
        }

        var baseUri = new Uri(baseUrl);
        var resolved = new Uri(baseUri, relativeUrl);
        return resolved.AbsoluteUri;
    }

    /// <summary>
    /// The absolute URL of the first link in <paramref name="html"/>, or <c>null</c> when there is
    /// nothing to follow: no link, a link within the page (<c>#top</c>), or a local file the page may
    /// not reach.
    /// </summary>
    /// <param name="html">The landing page HTML.</param>
    /// <param name="baseUrl">The landing page's URL, which relative links resolve against.</param>
    /// <remarks>
    /// A local file is followed only from a local page, and only within the landing page's own
    /// directory tree, which keeps a test landing page from sending the capture to a file elsewhere on
    /// the disk. A page on the network may not reach a local file at all — the loader refuses that for
    /// any page's navigation, and this does not ask it to.
    /// </remarks>
    public static string? ResolveFirstLink(string html, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(baseUrl);

        var firstHref = ExtractFirstLinkHref(html);
        if (string.IsNullOrEmpty(firstHref) || firstHref.StartsWith('#'))
            return null;

        var resolvedUrl = ResolveUrl(baseUrl, firstHref);
        var uri = new Uri(resolvedUrl);

        if (uri.IsFile)
        {
            var baseUri = new Uri(baseUrl);
            if (!baseUri.IsFile)
                return null;

            var baseDir = Path.GetDirectoryName(Path.GetFullPath(baseUri.LocalPath)) ?? string.Empty;
            var targetPath = Path.GetFullPath(uri.LocalPath);
            if (!targetPath.StartsWith(baseDir, StringComparison.Ordinal))
                return null;
        }

        return resolvedUrl;
    }
}
