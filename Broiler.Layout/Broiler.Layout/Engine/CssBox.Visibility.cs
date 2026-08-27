using System;
using System.Drawing;
using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// Native <c>position-visibility</c> resolution (Phase 5, P5.8d.2b) — the engine equivalent of
/// the bridge's <c>ResolvePositionVisibility</c>/<c>IsAnchorVisibleForTarget</c>. A root post-pass
/// (run after native anchor placement, so every box has final geometry) that hides an
/// anchor-positioned target whose anchor is not visible: <c>visibility:hidden</c>, missing
/// (<c>anchors-valid</c>), or scrolled out of an <em>intervening</em> clip container
/// (<c>anchors-visible</c>). Hiding is applied via <see cref="CssBox.PositionHidden"/> after
/// layout (the fragment builder skips the box), replacing the bridge's <c>display:none</c> write.
/// </summary>
/// <remarks>
/// The intervening-clip decision needs the target's containing block computed as if the bridge's
/// anchor-induced <c>position:relative</c> on a scroll container were absent (the bridge decides
/// visibility <em>before</em> it applies that relative — see the roadmap finding). The bridge
/// stamps those scrollers with <c>data-broiler-anchor-cb</c>; this pass treats a stamped scroller
/// as still-intervening (not the target's CB), so a static scroller (stamped) hides its scrolled-out
/// anchor's target while an authored <c>position:relative</c> scroller (not stamped, = the CB) does
/// not. Uses real box geometry (the anchor's scroll-shifted <see cref="Bounds"/> vs the clip rect)
/// rather than the bridge's offset estimator.
/// </remarks>
partial class CssBox
{
    private const string AnchorInducedCbAttr = "data-broiler-anchor-cb";

    internal static void ResolvePositionVisibility(CssBox root, AnchorRegistry registry)
        => ResolvePositionVisibilityTree(root, registry);

    private static void ResolvePositionVisibilityTree(CssBox box, AnchorRegistry registry)
    {
        box.ApplyPositionVisibility(registry);
        foreach (var child in box.Boxes)
            ResolvePositionVisibilityTree(child, registry);
    }

    private void ApplyPositionVisibility(AnchorRegistry registry)
    {
        var posAnchor = PositionAnchor;
        bool hasAnchor = !string.IsNullOrEmpty(posAnchor) && posAnchor != "auto";
        bool hasArea = !string.IsNullOrEmpty(PositionArea) && PositionArea != "none";

        // "normal" is the unset sentinel; an unset position-visibility on a position-area +
        // position-anchor target takes the implicit anchors-visible (position-visibility-initial),
        // while an explicit "always" never hides (position-visibility-remove-anchors-visible).
        string posVis = PositionVisibility;
        if (posVis == "normal" && hasAnchor && hasArea)
            posVis = "anchors-visible";

        if (string.Equals(posVis, "anchors-visible", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasAnchor)
                return;
            var cb = FindPositionedContainingBlock();
            Func<object?, bool> inScope = scope => scope is CssBox src && IsBoxDescendantOf(src, cb);
            var anchorBox = registry.ResolveScope<CssBox>(posAnchor, inScope);
            if (anchorBox == null || !IsAnchorVisibleFor(anchorBox))
                PositionHidden = true;
        }
        else if (string.Equals(posVis, "anchors-valid", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasValidAnchor(registry, posAnchor, hasAnchor))
                PositionHidden = true;
        }
    }

    /// <summary>
    /// Whether <paramref name="anchorBox"/> is "visible" for this target's
    /// <c>anchors-visible</c>: not <c>visibility:hidden</c>, not inside a hidden subtree, and not
    /// scrolled out of a clip container that clips the anchor but is not this target's (visibility)
    /// containing block.
    /// </summary>
    private bool IsAnchorVisibleFor(CssBox anchorBox)
    {
        // Authored visibility:hidden on the anchor or an ancestor. A visibility:hidden that the
        // bridge's scroll simulation injected to clip scrolled-out content (data-broiler-scroll-hidden)
        // is NOT authored — it means "scrolled out", which is subject to the intervening-clip / CB
        // exception below, so it is ignored here (matching the bridge, which decides visibility before
        // scroll simulation runs).
        for (var p = anchorBox; p != null; p = p.ParentBox)
            if (string.Equals(p.Visibility, "hidden", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(p.GetAttribute("data-broiler-scroll-hidden")))
                return false;

        // An anchor inside an already-hidden subtree (a chained anchor whose host was hidden) is
        // not visible.
        for (var p = anchorBox; p != null; p = p.ParentBox)
            if (p.PositionHidden)
                return false;

        // A fixed anchor is positioned against the viewport and is not clipped by ancestor
        // scroll containers, so it is never scrolled out.
        if (anchorBox.Position == CssConstants.Fixed)
            return true;

        // The target's containing block computed as if anchor-induced-relative scrollers were
        // static (the bridge's pre-position:relative view).
        var targetCb = FindVisibilityContainingBlock();

        // Whether the bridge's scroll simulation clipped the anchor out of its scroll container
        // (the authoritative "scrolled out" signal — the relative offset the sim applies is not
        // reflected in a box's Bounds, so this marker is more reliable than a geometry read).
        bool anchorScrollClipped = AnchorIsScrollClipped(anchorBox);

        for (var el = anchorBox.ParentBox; el != null; el = el.ParentBox)
        {
            if (!el.IsScrollClipContainer())
                continue;
            bool anchorInduced = el.IsAnchorInducedCb();
            // The clip container IS the target's CB (and is a genuine, authored CB) → there is no
            // intervening clip between anchor and target → visible.
            if (el == targetCb && !anchorInduced)
                return true;
            // Otherwise it is intervening (or an anchor-induced CB the bridge treats as static):
            // the anchor is hidden if it is scrolled entirely out of this container.
            if (anchorScrollClipped || AnchorScrolledOutOf(anchorBox, el))
                return false;
        }
        return true;
    }

    /// <summary>
    /// This target's containing block for visibility purposes: the nearest positioned ancestor that
    /// is not an anchor-induced <c>position:relative</c> scroll container (which the bridge applied
    /// only after deciding visibility, so it must not count as the CB here).
    /// </summary>
    private CssBox FindVisibilityContainingBlock()
    {
        for (var b = ParentBox; b != null; b = b.ParentBox)
        {
            if (b.ParentBox == null)
                return b;
            bool positioned = b.Position is CssConstants.Relative or CssConstants.Absolute or CssConstants.Fixed;
            if (positioned && !b.IsAnchorInducedCb())
                return b;
        }
        return ContainingBlock;
    }

    /// <summary>Whether the anchor's border box lies entirely outside the clip container's padding
    /// box on any axis (scrolled out). The anchor's <see cref="Bounds"/> already reflect scroll via
    /// the bridge's DOM-shift simulation.</summary>
    private static bool AnchorScrolledOutOf(CssBox anchor, CssBox scroller)
    {
        var clip = RectangleF.FromLTRB(
            (float)(scroller.Location.X + scroller.ActualBorderLeftWidth),
            (float)(scroller.Location.Y + scroller.ActualBorderTopWidth),
            (float)(scroller.ActualRight - scroller.ActualBorderRightWidth),
            (float)(scroller.ActualBottom - scroller.ActualBorderBottomWidth));
        var a = anchor.Bounds;
        const float eps = 0.5f;
        return a.Bottom <= clip.Top + eps || a.Top >= clip.Bottom - eps
            || a.Right <= clip.Left + eps || a.Left >= clip.Right - eps;
    }

    /// <summary>Whether this box clips its content (a scroll/clip container). Recognises the
    /// two-value <c>overflow</c> shorthand (e.g. <c>hidden scroll</c>, which the cascade stores
    /// un-normalised) by matching any token, so it is not limited to the single-value forms the
    /// paint clip check handles.</summary>
    private bool IsScrollClipContainer()
    {
        if (string.IsNullOrEmpty(Overflow) || Overflow == "visible")
            return false;
        return Overflow.Contains("hidden", StringComparison.Ordinal)
            || Overflow.Contains("scroll", StringComparison.Ordinal)
            || Overflow.Contains("auto", StringComparison.Ordinal)
            || Overflow.Contains("clip", StringComparison.Ordinal);
    }

    /// <summary>Whether the anchor (or an ancestor between it and its scroll container) was
    /// clipped out of view by the bridge's scroll simulation (marked
    /// <c>data-broiler-scroll-hidden</c>) — i.e. scrolled entirely out of its scroll container.</summary>
    private static bool AnchorIsScrollClipped(CssBox anchorBox)
    {
        for (var p = anchorBox; p != null; p = p.ParentBox)
            if (!string.IsNullOrEmpty(p.GetAttribute("data-broiler-scroll-hidden")))
                return true;
        return false;
    }

    private bool IsAnchorInducedCb()
        => !string.IsNullOrEmpty(GetAttribute(AnchorInducedCbAttr));

    /// <summary>
    /// <c>anchors-valid</c> validity: the target has at least one anchor reference that resolves to
    /// a registered anchor — an explicit <c>position-anchor</c>, or an <c>anchor()</c> function in a
    /// physical inset / size naming a registered anchor.
    /// </summary>
    private bool HasValidAnchor(AnchorRegistry registry, string posAnchor, bool hasAnchor)
    {
        if (hasAnchor && registry.Contains(posAnchor))
            return true;
        foreach (var value in new[] { Left, Right, Top, Bottom, Width, Height })
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf("anchor(", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (AnchorFunction.TryGetFirst(value, out var reference))
            {
                string name = string.IsNullOrEmpty(reference.Name) ? posAnchor : reference.Name!;
                if (!string.IsNullOrEmpty(name) && name != "auto" && registry.Contains(name))
                    return true;
            }
        }
        return false;
    }
}
