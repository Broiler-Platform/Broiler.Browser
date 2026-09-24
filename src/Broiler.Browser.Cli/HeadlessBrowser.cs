using Broiler.App.Rendering;
using Broiler.Browser;
using Broiler.HtmlBridge;
using Broiler.HtmlBridge.Core.Diagnostics;
using Broiler.HtmlBridge.Dom;
using Broiler.HtmlBridge.Scripting;
using Broiler.Net.Http;

namespace Broiler.Cli;

/// <summary>
/// One headless browsing session: a private profile, and a page pipeline composed from the pieces
/// the browser window composes its own from.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is shared with the window.</b> The document is loaded by <see cref="PageLoader"/> over
/// the profile's <c>BrowserNetworkSession</c>; its scripts are extracted and fetched by
/// <see cref="RenderingPipeline"/> on the same network, as the document's own requests; they run on
/// the engine <see cref="BrowserApp.NewScriptEngine"/> picks for the build configuration, over a
/// bridge made from <see cref="BrowserApp.BridgeOptions"/>; and the load window settles through
/// <see cref="InteractiveSession.SettleLoadWindow(CancellationToken)"/>. That is the path the
/// window's load worker takes, so a page the command line captures has run the way it runs on
/// screen.
/// </para>
/// <para>
/// <b>What the command line adds, and each is on purpose.</b>
/// </para>
/// <list type="bullet">
/// <item><description>
/// Its bridge has the <see cref="HeadlessLayoutView"/>, so a script's geometry questions are
/// answered by a real layout. The window has never had one, and answers them from the bridge's
/// null view.
/// </description></item>
/// <item><description>
/// It follows none of the page's own navigations — a script assigning <c>location</c>, a refresh
/// <c>meta</c> — because a capture is of the document asked for. <c>--follow-first-link</c> is the
/// one navigation it makes, and that is the caller's request, not the page's.
/// </description></item>
/// <item><description>
/// It keeps the engine's microtask queue current while it settles and evaluates, as the Broiler
/// repository's command line always did: a promise created by a timer callback then settles at the
/// next checkpoint on this thread instead of on the thread pool.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class HeadlessBrowser : IDisposable
{
    private readonly BrowserProfile _profile;
    private readonly BridgeRecorder _bridges;
    private readonly RenderingPipeline _pipeline;
    private DocumentRequestContext? _currentDocument;

    /// <param name="navigationTimeout">
    /// How long a document may take from the request to its last byte. Sub-resources are not
    /// bounded by it, as they are not in the window.
    /// </param>
    public HeadlessBrowser(TimeSpan navigationTimeout)
    {
        _profile = BrowserProfile.CreateEphemeral();
        _bridges = new BridgeRecorder(new DomBridgeFactory(
            BrowserApp.BridgeOptions(_profile, DocumentFor, static () => new HeadlessLayoutView())));
        Engine = BrowserApp.NewScriptEngine(_bridges);
        _pipeline = new RenderingPipeline(
            new TracedPageLoader(new PageLoader(_profile.Network, navigationTimeout)),
            Engine,
            _profile.Network);
    }

    /// <summary>The engine the page's scripts run on.</summary>
    public IScriptEngine Engine { get; }

    /// <summary>
    /// The document the current load produced, for the bridge the engine attaches to it — the
    /// window's load worker answers the same question the same way.
    /// </summary>
    private DocumentRequestContext DocumentFor(Uri url) =>
        _currentDocument is { } document && document.DocumentUrl == url
            ? document
            : DocumentRequestContext.CreateTopLevel(url);

    /// <summary>
    /// Loads <paramref name="url"/> as a navigation the user typed, and then, with
    /// <paramref name="followFirstLink"/>, the first link on it as a navigation that page started.
    /// </summary>
    public async Task<LoadedPage> LoadAsync(string url, bool followFirstLink, CancellationToken cancellationToken = default)
    {
        LoadedPage page = await LoadAsync(PageRequest.ForUrl(url), cancellationToken).ConfigureAwait(false);

        if (followFirstLink && LinkNavigator.ResolveFirstLink(page.Content.Html, page.FinalUrl) is { } next)
        {
            page = await LoadAsync(
                PageRequest.ForUrl(next) with
                {
                    Initiator = page.Document,
                    NavigationType = PageNavigationType.Link,
                },
                cancellationToken).ConfigureAwait(false);
        }

        return page;
    }

    private async Task<LoadedPage> LoadAsync(PageRequest request, CancellationToken cancellationToken)
    {
        LoadedPage page = await _pipeline.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        _currentDocument = page.Document;
        return page;
    }

    /// <summary>
    /// Runs <paramref name="page"/>'s scripts and settles its load window.
    /// </summary>
    /// <param name="page">The page to run.</param>
    /// <param name="needsRealm">
    /// Whether the caller will evaluate in the page's realm afterwards. A page with no scripts gets no
    /// realm from the pipeline, and "the page had no scripts" is something an evaluation has to be
    /// able to report rather than a reason for it not to run, so such a page is run with one empty
    /// script instead.
    /// </param>
    /// <param name="cancellationToken">Stops the settle.</param>
    public ScriptedPage Run(LoadedPage page, bool needsRealm = false, CancellationToken cancellationToken = default)
    {
        PageContent content = page.Content;
        ArchiveScripts(content);

        bool hasScripts = content.Scripts.Count > 0 || content.DeferredScripts.Count > 0 || content.ModuleRoots.Count > 0;
        if (needsRealm && !hasScripts)
            content = new PageContent(content.Html, [string.Empty], content.Url, [], []);

        InteractiveSession? session = _pipeline.ExecuteScriptsInteractive(content);
        var scripted = new ScriptedPage(
            session,
            session is null ? null : _bridges.Last,
            Engine.MicroTasks,
            page.FinalUrl,
            page.Content.Html,
            firstEvaluationLabel: page.Content.Scripts.Count);

        scripted.Settle(cancellationToken);
        return scripted;
    }

    /// <summary>
    /// Archives the program text of everything about to run, under the label the engine will log it
    /// by — <c>inline-7</c>, <c>deferred-0</c>, <c>module-{key}</c> — so "Script inline-7 failed"
    /// points at a file. External scripts were recorded as fetched too; the diagnostics sink stores
    /// identical bytes once, so the second entry costs a manifest row and yields the label-to-URL
    /// mapping. Off unless a diagnostics bundle is being written.
    /// </summary>
    private static void ArchiveScripts(PageContent content)
    {
        if (!ResourceTrace.IsActive)
            return;

        for (var i = 0; i < content.Scripts.Count; i++)
            RecordExecutedScript(content.Url, ScriptLabel.Inline(i), content.Scripts[i]);
        for (var i = 0; i < content.DeferredScripts.Count; i++)
            RecordExecutedScript(content.Url, ScriptLabel.Deferred(i), content.DeferredScripts[i]);
        foreach (var root in content.ModuleRoots)
            RecordExecutedScript(content.Url, ScriptLabel.Module(root.Key), root.Source);
    }

    private static void RecordExecutedScript(string pageUrl, string label, string source) =>
        ResourceTrace.RecordBody(ResourceTraceKind.ExecutedScript, $"{pageUrl}#{label}", source, label);

    public void Dispose()
    {
        _pipeline.Dispose();
        _profile.Dispose();
    }

    /// <summary>
    /// The bridge factory the engine is given, remembering the last bridge it made: the one the
    /// page's scripts ran against, whose realm an evaluation runs in afterwards.
    /// </summary>
    private sealed class BridgeRecorder(IDomBridgeRuntimeFactory inner) : IDomBridgeRuntimeFactory
    {
        public IDomBridgeRuntime? Last { get; private set; }

        public IDomBridgeRuntime Create() => Last = inner.Create();
    }

    /// <summary>
    /// Records every document the pipeline loads when a diagnostics bundle is being written. The
    /// page is the one resource nothing below the command line sees being fetched, so it can only be
    /// archived from here.
    /// </summary>
    private sealed class TracedPageLoader(IPageLoader inner) : IPageLoader
    {
        public Task<(string NormalisedUrl, string Html)> FetchAsync(
            PageRequest request,
            CancellationToken cancellationToken = default) =>
            inner.FetchAsync(request, cancellationToken);

        public async Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
        {
            var attempt = ResourceTrace.Begin(ResourceTraceKind.Document, request.Url);
            try
            {
                PageLoadResult result = await inner.LoadAsync(request, cancellationToken).ConfigureAwait(false);
                attempt.Completed(
                    result.Html,
                    result.StatusCode,
                    result.GetHeaderValues("Content-Type").FirstOrDefault(),
                    string.Equals(result.Method, PageRequest.Get, StringComparison.OrdinalIgnoreCase) ? null : result.Method);
                return result;
            }
            catch (Exception ex)
            {
                attempt.Failed(ex);
                throw;
            }
        }

        public void Dispose() => inner.Dispose();
    }
}
