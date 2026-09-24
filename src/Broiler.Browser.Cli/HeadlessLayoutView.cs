using System;
using System.Collections.Generic;
using System.Drawing;
using Broiler.HTML.Image;
using Broiler.Layout;
using BDom = Broiler.Dom;

namespace Broiler.Cli;

/// <summary>
/// <see cref="ILayoutView"/> implementation that drives the renderer's real layout engine
/// headlessly (no paint backend) to answer the script bridge's element-geometry queries,
/// replacing the coarse estimators. The canonical document is laid out once per
/// (document, version, viewport, baseUrl) snapshot and the per-element
/// <see cref="BoxGeometry"/> map is cached; layout re-runs only when one of those changes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it came from.</b> This was <c>Broiler.HTML.Headless.HeadlessLayoutView</c>, and the
/// command line was the one host that registered it. Broiler.HTML deleted the project in 603c8083
/// (2026-09-15) because nothing it could see referenced it: the command line was still in the
/// Broiler repository. It lives beside that host now, and reaches the bridge through
/// <c>DomBridgeSessionOptions.LayoutViewFactory</c> instead of the static the old registration set.
/// </para>
/// <para>
/// <b>What it no longer does.</b> The Broiler.HTML copy switched on the layout engine's native
/// anchor-positioning pass around each layout and handed it the document's <c>@position-try</c>
/// rules, and it put the bridge's visual-viewport scale into the snapshot key. All three go through
/// <c>Broiler.Layout.Engine.NativeAnchorPlacement</c>, which is internal to Broiler.Layout, and the
/// package grants its internals to <c>Broiler.Cli.Tests</c> but not to <c>Broiler.Cli</c>. So a
/// script's geometry for an anchor-positioned box is its static placement here, not the resolved
/// one. The scale only changes with a pinch zoom, which a headless capture never does.
/// </para>
/// </remarks>
internal sealed class HeadlessLayoutView : ILayoutView
{
    private readonly HtmlContainer _container = new()
    {
        AvoidAsyncImagesLoading = true,
        AvoidImagesLateLoading = true,
    };

    private IReadOnlyDictionary<BDom.DomElement, BoxGeometry>? _snapshot;
    private BDom.DomDocument? _snapshotDocument;
    private ulong _snapshotVersion;
    private SizeF _snapshotViewport;
    private string? _snapshotBaseUrl;
    private bool _hasSnapshot;
    private bool _disposed;

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
