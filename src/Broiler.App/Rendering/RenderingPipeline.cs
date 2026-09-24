using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Scripting;
using Broiler.Net.Http;

namespace Broiler.App.Rendering;

/// <summary>
/// Orchestrates the page rendering flow:
/// fetch HTML → extract scripts → render HTML → execute scripts.
/// </summary>
/// <param name="pageLoader">Loads the document itself.</param>
/// <param name="scriptEngine">Runs the document's scripts.</param>
/// <param name="network">
/// The profile's request transport the document's external scripts are fetched through, as the
/// document's own requests, or <see langword="null"/> to fetch them over the script extractor's
/// cookie-less fallback client. The pipeline never disposes it.
/// </param>
public sealed class RenderingPipeline(
    IPageLoader pageLoader,
    IScriptEngine scriptEngine,
    IBrowserRequestTransport? network = null) : IDisposable
{
    /// <summary>
    /// Load a page from <paramref name="url"/>, extract inline scripts,
    /// and return a <see cref="PageContent"/> ready for rendering.
    /// The normalised URL (with scheme) is included in the result tuple.
    /// Uses <see cref="ScriptExtractionService.ExtractAll(string, string?, ContentSecurityPolicy?, ScriptFetchContext?)"/>
    /// so that deferred and external scripts are also captured, matching the CLI's behaviour.
    /// </summary>
    public Task<(string NormalisedUrl, PageContent Content)> LoadPageAsync(
        string url,
        CancellationToken cancellationToken = default) =>
        LoadPageAsync(PageRequest.ForUrl(url), cancellationToken);

    /// <summary>
    /// Load a page for <paramref name="request"/> — a plain navigation, or a form
    /// submission carrying a request body — and return a <see cref="PageContent"/>
    /// ready for rendering. The URL returned is the one the document was served from.
    /// </summary>
    public async Task<(string NormalisedUrl, PageContent Content)> LoadPageAsync(
        PageRequest request,
        CancellationToken cancellationToken = default)
    {
        LoadedPage page = await LoadAsync(request, cancellationToken).ConfigureAwait(false);
        return (page.FinalUrl, page.Content);
    }

    /// <summary>
    /// Load the document for <paramref name="request"/>, identify it, and extract its scripts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The document is where the response came from.</b> Its request context is built from the
    /// final URL of the load, not the URL first asked for: after a redirect, that is the document's
    /// origin, the site its requests are judged against and the base its scripts resolve from.
    /// </para>
    /// <para>
    /// <b>Its scripts are its own requests.</b> Every external classic script and module root is
    /// fetched through <c>network</c> with that context as the client, so it carries the cookies a
    /// request from this document may carry, under the <c>crossorigin</c> rules of its element, and
    /// <paramref name="cancellationToken"/> — the navigation's — abandons the fetches when the
    /// navigation is stopped or replaced.
    /// </para>
    /// <para>
    /// <b>The <c>Content-Security-Policy</c> response header is not enforced here.</b> HtmlBridge's
    /// source matching understands <c>*</c>, keywords, schemes and absolute URLs but not CSP3 host
    /// sources — a bare host (<c>script-src github.githubassets.com</c>), a <c>*.</c> subdomain
    /// wildcard, a host with a port or a path — which are what real headers list, so enforcing it
    /// refused the very scripts those headers allow. It was also enforced only on the parser-inserted
    /// scripts the extractor sees, not on scripts the page inserts later. It is enforced once host
    /// sources are matched, and then for the script engine as well. A policy the markup declares is
    /// enforced as before. <see cref="PageLoadResult.ContentSecurityPolicy"/> still carries the header.
    /// </para>
    /// </remarks>
    public async Task<LoadedPage> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        PageLoadResult response = await pageLoader.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        string url = response.FinalUrl;
        DocumentRequestContext document = CreateDocumentContext(url);
        ScriptFetchContext? fetch = network is null ? null : new ScriptFetchContext(network, document, cancellationToken);

        var result = ScriptExtractionService.ExtractAll(response.Html, url, deliveredPolicy: null, fetch);
        cancellationToken.ThrowIfCancellationRequested();

        var executableScripts = result.AsyncScripts.Count == 0
            ? result.Scripts
            : result.Scripts.Concat(result.AsyncScripts).ToArray();

        // ES modules (Phase 7 item 6 / tail): the authorised module roots run through the engine's own module
        // machinery when it binds imports (EngineModuleSupport.Available, gated inside ScriptEngine). The
        // string-rewriting EsModuleLinker fallback was retired, so the roots are the sole module input.
        var content = new PageContent(response.Html, executableScripts, url, result.DeferredScripts, result.ModuleRoots);
        return new LoadedPage(response, document, content);
    }

    /// <summary>
    /// Starts an interactive script-execution session for
    /// <paramref name="content"/>.  Scripts and deferred scripts execute
    /// immediately but pending timer / rAF callbacks are <b>not</b> flushed.
    /// The returned <see cref="InteractiveSession"/> lets the caller step
    /// through callbacks one batch at a time, re-rendering after each step
    /// to display animations interactively.
    /// Returns <c>null</c> when the page has no scripts.
    /// The caller must dispose the session when finished.
    /// </summary>
    public InteractiveSession? ExecuteScriptsInteractive(PageContent content)
    {
        return scriptEngine.ExecuteInteractive(
            content.Scripts, content.DeferredScripts, content.Html, content.Url, content.ModuleRoots);
    }

    /// <summary>
    /// The request context of the top-level document served from <paramref name="documentUrl"/>. A
    /// <c>file:</c> document gets one too: its URL is not HTTP(S), so it is cookie-averse.
    /// </summary>
    internal static DocumentRequestContext CreateDocumentContext(string documentUrl) =>
        DocumentRequestContext.CreateTopLevel(
            Uri.TryCreate(documentUrl, UriKind.Absolute, out Uri? url) ? url : new Uri("about:blank"));

    public void Dispose() => pageLoader.Dispose();
}

/// <summary>
/// A document the pipeline loaded: the response it came from, its identity for requests and cookies,
/// and its markup and scripts ready to run.
/// </summary>
public sealed class LoadedPage
{
    internal LoadedPage(PageLoadResult response, DocumentRequestContext document, PageContent content)
    {
        Response = response;
        Document = document;
        Content = content;
    }

    /// <summary>What the page load returned.</summary>
    public PageLoadResult Response { get; }

    /// <summary>
    /// The document's request context, from <see cref="FinalUrl"/>: the client of every request the
    /// document makes, and the initiator of every navigation it starts.
    /// </summary>
    public DocumentRequestContext Document { get; }

    /// <summary>The document's markup and the scripts extracted from it.</summary>
    public PageContent Content { get; }

    /// <summary>The URL the document was served from, after every redirect.</summary>
    public string FinalUrl => Response.FinalUrl;
}
