using Broiler.Net.Http;

namespace Broiler.Browser;

/// <summary>
/// The window's network as its pages see it, noting each URL a frame or an object navigates to as
/// visited, the way a top-level navigation is.
/// </summary>
/// <remarks>
/// A page's frames load through Broiler.HtmlBridge's own fetch on this network, not through the
/// window's navigation, so <c>:visited</c> never matched a link to a document a frame had shown.
/// Acid3 loads its link's URL into an iframe and expects the link to be painted as visited, white on
/// white; Broiler painted "YOU SHOULD NOT SEE THIS AT ALL" in red. Every URL of a redirect chain is
/// noted, as Chromium records the chain, and only once a response arrives, whatever its status: a
/// frame that shows a 404 page has still navigated there.
/// </remarks>
internal sealed class FrameVisitTransport(IBrowserRequestTransport inner, Action<string> noteVisited) : IBrowserRequestTransport
{
    public async Task<TransportResponse> SendAsync(
        HttpRequestMessage request,
        RequestContext context,
        CancellationToken cancellationToken = default)
    {
        var response = await inner.SendAsync(request, context, cancellationToken).ConfigureAwait(false);
        Note(context, response);
        return response;
    }

    public TransportResponse Send(
        HttpRequestMessage request,
        RequestContext context,
        CancellationToken cancellationToken = default)
    {
        var response = inner.Send(request, context, cancellationToken);
        Note(context, response);
        return response;
    }

    private void Note(RequestContext context, TransportResponse response)
    {
        if (!context.IsNavigation || context.IsTopLevelNavigation)
            return;
        foreach (var url in response.UrlList)
            noteVisited(url.AbsoluteUri);
    }
}
