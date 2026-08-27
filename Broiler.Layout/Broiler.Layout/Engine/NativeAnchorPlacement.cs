using System.Collections.Generic;

namespace Broiler.Layout.Engine;

/// <summary>
/// Feature flag gating the native CSS anchor-positioning placement post-pass
/// (HtmlBridge complexity-reduction roadmap Phase 5 item 3, P5.8c). Default off:
/// while off, the layout engine ignores anchor properties and the HtmlBridge
/// anchor resolver continues to pre-bake anchor positions as it does today.
/// </summary>
/// <remarks>
/// Thread-static so a test (or a future per-document caller) can enable it for one
/// layout without affecting concurrently-running layouts. The cutover that flips it
/// on for real (and makes the bridge stop pre-baking the matching boxes) is P5.8d.
/// </remarks>
internal static class NativeAnchorPlacement
{
    [System.ThreadStatic]
    public static bool Enabled;

    /// <summary>
    /// The document's parsed <c>@position-try</c> at-rules — a name → declaration map —
    /// for the native position-try fallback pass (P5.8d.2b position-try expansion). The
    /// engine consumes cascaded box properties, never the stylesheet, so the
    /// rule <em>bodies</em> (a stylesheet at-rule, not a per-element cascaded property)
    /// must be handed in out-of-band. The caller that enables <see cref="Enabled"/> for a
    /// layout also sets this (parsed via the canonical <c>Broiler.CSS.PositionTryRule</c>
    /// model) and restores it afterward; <c>null</c>/empty means no fallback rules are
    /// available, so a position-try box keeps its base placement.
    /// </summary>
    [System.ThreadStatic]
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? PositionTryRules;

    /// <summary>
    /// The document-root visual-viewport / root-`zoom` uniform scale factor for the geometry read
    /// model (Phase 5 LayoutSnapshot endgame, blocker (b) — visual-viewport). A pinch-zoom (or
    /// `html { zoom }`) is a <em>uniform</em> scale of the whole document, so rather than baking
    /// scaled lengths into the DOM (the retiring <c>DomBridge.ApplyZoomSerializationStyles</c> /
    /// <c>ApplyVisualViewportSerializationState</c> path) the box tree is laid out at unit scale and
    /// the factor is applied where geometry <em>leaves</em> the tree — the extracted
    /// <c>BoxGeometry</c> rects are multiplied by this factor (all three box-model levels scale
    /// uniformly, which is correct for a uniform zoom). Thread-static, so a caller enables it for one
    /// layout/extraction and restores it. <c>0</c> (the default) and <c>1</c> both mean "no scale".
    /// Only a uniform <em>root</em> zoom belongs here; mid-tree <c>zoom: N</c> reflows and stays on
    /// the general engine-zoom path (blocker (b2)).
    /// </summary>
    [System.ThreadStatic]
    public static double VisualViewportScale;
}
