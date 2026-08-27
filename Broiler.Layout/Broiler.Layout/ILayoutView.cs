using System.Drawing;
using Broiler.Dom;

namespace Broiler.Layout;

/// <summary>
/// A headless, document-scoped view over the real layout tree that answers per-element
/// box-geometry queries without the caller taking a dependency on a graphics backend or
/// the HTML renderer. Implementations lay the bound document out at the requested viewport
/// and return a per-element <see cref="BoxGeometry"/> map keyed by the canonical
/// <see cref="DomElement"/>.
/// </summary>
/// <remarks>
/// This is the narrow contract that lets the script bridge (Broiler.HtmlBridge.Dom) read
/// accurate element geometry while depending only on the canonical layout read-model, not
/// on Broiler.HTML.Image. The concrete implementation lives below the HTML renderer (see
/// <c>Broiler.HTML.Headless.HeadlessLayoutView</c>) and is injected into the bridge.
/// Implementations own renderer resources, so callers dispose the view when the owning
/// document is torn down.
/// </remarks>
public interface ILayoutView : IDisposable
{
    /// <summary>
    /// Returns the per-element geometry map for <paramref name="document"/> at its current
    /// <see cref="DomDocument.Version"/> and the given <paramref name="viewport"/>. The
    /// implementation caches the map per <c>(document, version, viewport, baseUrl)</c> and
    /// re-lays out only when one of those changes.
    /// </summary>
    /// <param name="document">The canonical document to lay out.</param>
    /// <param name="viewport">The viewport size to lay out against.</param>
    /// <param name="baseUrl">The document base URL used for resource resolution.</param>
    /// <param name="contentDocumentResolver">
    /// Optional host callback (HtmlBridge Phase 4 P4.4b) mapping a nested-browsing-context
    /// container element (<c>&lt;iframe&gt;</c>/<c>&lt;object&gt;</c>/<c>&lt;frame&gt;</c>) to its
    /// referenced content <see cref="DomDocument"/>. When supplied, a sub-document that is no
    /// longer an in-tree child is projected into the box tree (and its geometry composed into the
    /// main frame). Null preserves the legacy in-tree-materialisation behaviour.
    /// </param>
    IReadOnlyDictionary<DomElement, BoxGeometry> GetGeometry(
        DomDocument document, SizeF viewport, string baseUrl,
        Func<DomElement, DomDocument?>? contentDocumentResolver = null);
}
