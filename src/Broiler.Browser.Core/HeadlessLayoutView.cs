using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout;
using Broiler.Net.Http;
using BDom = Broiler.Dom;

namespace Broiler.Browser;

/// <summary>
/// <see cref="ILayoutView"/> implementation that drives the renderer's real layout engine
/// headlessly (no paint backend) to answer the script bridge's element-geometry queries,
/// replacing the coarse estimators.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both hosts give it to their bridge</b>, through <see cref="BrowserApp.BridgeOptions"/>: the
/// window, and the command line in src/Broiler.Browser.Cli. So a script asking how big an element is
/// — <c>getBoundingClientRect</c>, <c>offsetWidth</c>, <c>clientHeight</c>, the scroll sizes — gets
/// the size the page is laid out at. Until the window took it up it had never registered a layout
/// view, not since the initial checkin, and its scripts were answered by the bridge's null view, which
/// says 0 to every one of those questions.
/// </para>
/// <para>
/// <b>It loads on the profile's network.</b> A layout needs the page's stylesheets, fonts and images,
/// and they go out as the document's sub-resource requests through the profile's session — with its
/// cookies, and from the container's in-memory cache after the first layout — the way the window's
/// render container loads them. Without a transport the container falls back to process-wide
/// clients that carry no cookies, and on mediawiki.org its first layout then took 22 seconds instead
/// of 0.2 (measured 2026-09-24).
/// </para>
/// <para>
/// <b>What it costs, and where.</b> Nothing until a script asks: the bridge makes the view the first
/// time it needs geometry. After that, the whole document is set up and laid out again every time the
/// bridge builds its geometry snapshot, on the thread the asking script runs on — the load worker
/// while the load window settles, the UI thread when the viewport steps the page afterwards. The
/// bridge passes a content-document resolver on every call, which bypasses the snapshot cache below,
/// so the bridge's own per-pass snapshot is what bounds how often that happens.
/// </para>
/// <para>
/// <b>Where it came from.</b> This was <c>Broiler.HTML.Headless.HeadlessLayoutView</c>, and the
/// Broiler repository's command line was the one host that registered it. Broiler.HTML deleted the
/// project in 603c8083 (2026-09-15) because nothing it could see referenced it. It came back with the
/// command line and moved here when the window took it up.
/// </para>
/// <para>
/// <b>What it does not do.</b> The Broiler.HTML copy switched on the layout engine's native
/// anchor-positioning pass around each layout and handed it the document's <c>@position-try</c>
/// rules. Both go through <c>Broiler.Layout.Engine.NativeAnchorPlacement</c>, which is internal to
/// Broiler.Layout, and the package grants its internals to <c>Broiler.Cli.Tests</c>, not to
/// <c>Broiler.Browser.Core</c>. So a script's geometry for an anchor-positioned box is its static
/// placement, not the resolved one. That copy also put the bridge's visual-viewport scale into its
/// snapshot key, which this one cannot read either; while the bridge bypasses the cache on every call
/// that makes no difference.
/// </para>
/// </remarks>
internal sealed class HeadlessLayoutView : ILayoutView
{
    private readonly HtmlContainer _container = new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
    };

    private readonly Func<Uri, DocumentRequestContext> _documents;

    private IReadOnlyDictionary<BDom.DomElement, BoxGeometry>? _snapshot;
    private BDom.DomDocument? _snapshotDocument;
    private ulong _snapshotVersion;
    private SizeF _snapshotViewport;
    private string? _snapshotBaseUrl;
    private bool _hasSnapshot;
    private bool _disposed;

    /// <param name="network">The profile's network session, which the layout's loads go through.</param>
    /// <param name="documents">
    /// The request context of the document at a URL: the one the pipeline built for the page, so the
    /// layout's loads are that document's requests. The bridge names the page by its URL.
    /// </param>
    public HeadlessLayoutView(IBrowserRequestTransport network, Func<Uri, DocumentRequestContext> documents)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(documents);
        _container.RequestTransport = network;
        _documents = documents;
    }

    /// <summary>
    /// Returns the per-element geometry map for <paramref name="document"/> at its current
    /// <see cref="BDom.DomDocument.Version"/> and the given <paramref name="viewport"/>,
    /// laying out only when the cached snapshot is stale for the
    /// (document, version, viewport, baseUrl) key.
    /// </summary>
    public IReadOnlyDictionary<BDom.DomElement, BoxGeometry> GetGeometry(
        BDom.DomDocument document, SizeF viewport, string baseUrl,
        Func<BDom.DomElement, BDom.DomDocument?>? contentDocumentResolver = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // When a content-document resolver is present (materialised iframe/object sub-documents),
        // the (document, version, viewport, baseUrl) key does not capture sub-document mutations —
        // a severed sub-document has its own DomDocument.Version, invisible to the main document's.
        // Bypass the snapshot cache in that case so a mutated sub-document always re-lays-out; the
        // bridge still caps this to at most once per read pass via its own per-pass snapshot.
        if (contentDocumentResolver is null
            && _hasSnapshot
            && ReferenceEquals(_snapshotDocument, document)
            && _snapshotVersion == document.Version
            && _snapshotViewport == viewport
            && string.Equals(_snapshotBaseUrl, baseUrl, StringComparison.Ordinal))
        {
            return _snapshot!;
        }

        // Set only when it changes: the container drops its in-memory cache of what it has loaded
        // whenever its document context does, and the page is the same document call after call.
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var documentUrl)
            && _documents(documentUrl) is var context
            && !ReferenceEquals(_container.DocumentContext, context))
        {
            _container.DocumentContext = context;
        }

        // A layout fault is not swallowed here: it propagates to the caller (the bridge's
        // BuildSharedGeometrySnapshot degrades the whole pass to an empty map), so the cause
        // is not hidden behind a per-provider catch-all. The cache is only updated on
        // success, so a transient failure does not poison a later query.
        _container.ContentDocumentResolver = contentDocumentResolver;
        _container.SetDocumentWithStyleSet(document, baseUrl: baseUrl);
        var snapshot = _container.GetLayoutGeometry(viewport);

        if (contentDocumentResolver is null)
        {
            _snapshot = snapshot;
            _snapshotDocument = document;
            _snapshotVersion = document.Version;
            _snapshotViewport = viewport;
            _snapshotBaseUrl = baseUrl;
            _hasSnapshot = true;
        }
        else
        {
            // Do not serve a resolver-built snapshot from the plain-document cache on a later pass.
            _hasSnapshot = false;
        }

        return snapshot;
    }

    /// <summary>Releases the internal renderer container.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _snapshot = null;
        _snapshotDocument = null;
        _container.Dispose();
    }
}
