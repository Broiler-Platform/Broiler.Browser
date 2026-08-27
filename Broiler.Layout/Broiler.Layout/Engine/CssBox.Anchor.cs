using System.Collections.Generic;
using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// Native CSS anchor-positioning placement (Phase 5 item 3, P5.8c). A root
/// post-pass — modelled on <c>LayoutNestedBrowsingContexts</c>, run after the main
/// single-pass layout so every anchor already has final geometry — that builds an
/// <see cref="AnchorRegistry"/> from the box tree and re-places anchor-positioned
/// boxes using the geometry primitives promoted in P5.4–P5.7.
/// </summary>
/// <remarks>
/// Gated by <see cref="NativeAnchorPlacement.Enabled"/> (default off), so this is
/// inert until the P5.8d cutover. The pass handles <c>position-area</c> with an explicit
/// <c>position-anchor</c> and a registered anchor. For a childless, content-box box with
/// no percentage box properties it also applies the grid-derived used <b>size</b>
/// (fill-the-cell / explicit / percentage width+height — the P5.8d.2b sizing expansion);
/// every other box keeps the reposition-only behaviour (its already-laid-out size is
/// kept). <c>box-sizing: border-box</c>, percentage margin/padding/inset box props,
/// inline-CB promotion, scrolling, <c>anchor()</c> insets and position-try are still out
/// of scope and stay on the bridge path until later P5.8d expansions.
/// </remarks>
partial class CssBox
{
    /// <summary>
    /// Entry point for the anchor-placement post-pass, invoked from
    /// <c>PerformLayout</c> at the document root when the flag is on.
    /// </summary>
    internal static void RunNativeAnchorPlacement(CssBox root)
    {
        var registry = BuildAnchorRegistry(root);
        if (registry.Count == 0)
            return;
        ApplyNativeAnchorPlacement(root, registry);
        // position-visibility (P5.8d.2b): hide anchor-positioned targets whose anchor is not
        // visible. Runs after placement so the anchor's final (scroll-shifted) geometry is known.
        ResolvePositionVisibility(root, registry);
    }

    /// <summary>
    /// Walks the box tree collecting every element carrying an <c>anchor-name</c> into
    /// a name → document-absolute border-box registry (last in tree order wins).
    /// </summary>
    internal static AnchorRegistry BuildAnchorRegistry(CssBox root)
    {
        var registry = new AnchorRegistry();
        CollectAnchors(root, registry);
        return registry;
    }

    private static void CollectAnchors(CssBox box, AnchorRegistry registry)
    {
        var name = box.AnchorName;
        if (!string.IsNullOrEmpty(name) && name != "none")
        {
            var b = box.Bounds;
            // The anchor box itself is the scope token: a query binds to the candidate in
            // its own containing block when several elements share a name (see
            // ResolveScopedAnchorTarget). Registration order is tree/document order.
            registry.Register(name, new AnchorRect(b.X, b.Y, b.Width, b.Height), box);
        }

        foreach (var child in box.Boxes)
            CollectAnchors(child, registry);
    }

    /// <summary>
    /// Whether <paramref name="descendant"/> is a strict box-tree descendant of
    /// <paramref name="ancestor"/> (used as the anchor-name scope test: an anchor is in a
    /// query's scope when its box is inside the query's containing block).
    /// </summary>
    private static bool IsBoxDescendantOf(CssBox descendant, CssBox ancestor)
    {
        for (var p = descendant.ParentBox; p != null; p = p.ParentBox)
            if (p == ancestor)
                return true;
        return false;
    }

    private static void ApplyNativeAnchorPlacement(CssBox box, AnchorRegistry registry)
    {
        if (box.PositionArea != "none" && box.TryResolvePositionAreaTarget(registry, out var target, out var cell))
        {
            // Percentage box props (margin/padding/inset resolved against the cell): the
            // box's margin box stretches to fill the inset-modified containing block, so
            // its geometry is derived differently — handle it in its own path (which also
            // writes the resolved px padding so the content-box background paints right).
            if (box.CanApplyNativeAnchorSize() && box.HasPercentBoxProps())
            {
                box.ApplyPercentBoxPropsPlacement(target, cell);
                foreach (var pchild in box.Boxes)
                    ApplyNativeAnchorPlacement(pchild, registry);
                return;
            }

            // Apply the used SIZE first (P5.8d.2b sizing slices) for the boxes the engine
            // can size without re-flowing a subtree: a childless box (no in-flow children
            // or words). For such a box the grid-derived used size fills the cell / honours
            // an explicit or percentage length exactly as the baked bridge path does;
            // setting its border box repaints correctly with no children to re-flow. Every
            // other box keeps the reposition-only behaviour (its already-laid-out size is
            // preserved).
            if (box.CanApplyNativeAnchorSize())
            {
                // The box's own padding + border, read font-free (px + the CSS
                // thin/medium/thick keyword map, style:none → 0 — matching the bridge's
                // ResolveBorderWidth and avoiding the layout font the Actual* getters
                // resolve; percentage padding is excluded by the gate).
                double padBorderW =
                    box.NativeBorderPx(box.BorderLeftWidth, box.BorderLeftStyle)
                    + box.NativePaddingPx(box.PaddingLeft) + box.NativePaddingPx(box.PaddingRight)
                    + box.NativeBorderPx(box.BorderRightWidth, box.BorderRightStyle);
                double padBorderH =
                    box.NativeBorderPx(box.BorderTopWidth, box.BorderTopStyle)
                    + box.NativePaddingPx(box.PaddingTop) + box.NativePaddingPx(box.PaddingBottom)
                    + box.NativeBorderPx(box.BorderBottomWidth, box.BorderBottomStyle);

                // box-sizing: content-box (default) — target.Width/Height are the content
                // size, so the border box adds padding+border. box-sizing: border-box —
                // target.Width/Height already ARE the (cell-clamped/filled) border box, so
                // use them directly, clamped to at least padding+border so the content box
                // stays non-negative (mirrors the bridge's BorderBoxToContentSize + re-add).
                bool borderBox = string.Equals(box.BoxSizing, "border-box", StringComparison.OrdinalIgnoreCase);
                double borderBoxW = borderBox
                    ? System.Math.Max(target.Width, padBorderW)
                    : target.Width + padBorderW;
                double borderBoxH = borderBox
                    ? System.Math.Max(target.Height, padBorderH)
                    : target.Height + padBorderH;
                box.Size = new System.Drawing.SizeF((float)borderBoxW, (float)borderBoxH);
            }

            // Reposition the box (and its subtree) so its border-box origin lands at the
            // grid-resolved position.
            box.OffsetLeft(target.Left - box.Location.X);
            box.OffsetTop(target.Top - box.Location.Y);
        }
        else if (box.PositionArea == "none")
        {
            // anchor-size() sizing (P5.8d.2b anchor-size() expansion): size a childless box
            // to the anchor's width/height first, so any anchor()-inset placement that
            // follows repositions against the resolved size.
            box.TryApplyNativeAnchorSizing(registry);
            // anchor()/anchor-size() inset placement (P5.8d.2b anchor()-insets expansion):
            // a box positioned by anchor() functions in its left/right/top/bottom rather
            // than position-area.
            box.TryApplyAnchorInsetPlacement(registry);
            // align-self/justify-self: anchor-center (P5.8d.2b anchor-center expansion): centre
            // the box on its anchor in the block/inline axis (a box with position-anchor but no
            // position-area).
            box.TryApplyAnchorCenter(registry);
        }

        // position-try fallback (P5.8d.2b position-try expansion): after the base placement
        // above, if the box's base overflows its containing block, replace it with the first
        // @position-try fallback that fits. Reads the rule bodies from the out-of-band channel
        // (NativeAnchorPlacement.PositionTryRules); a no-op when the box has no position-try or
        // no rules are available.
        box.TryApplyPositionTryFallback(registry);

        foreach (var child in box.Boxes)
            ApplyNativeAnchorPlacement(child, registry);
    }

    /// <summary>
    /// Whether the native placement post-pass may set this box's used size directly
    /// (rather than only repositioning). True when doing so cannot mis-render: the box has
    /// no in-flow children or words to re-flow. Both <c>content-box</c> and
    /// <c>border-box</c> sizing and percentage margin/padding/inset box props are handled
    /// (the caller forms the border box / margin-box-fills-IMCB geometry accordingly).
    /// Matches the boxes for which the engine's grid-derived size equals the baked bridge
    /// result.
    /// </summary>
    private bool CanApplyNativeAnchorSize() => Boxes.Count == 0 && Words.Count == 0;

    /// <summary>
    /// Whether the box carries a percentage margin, padding, or inset — the CSS that
    /// resolves against the position-area cell and drives the <c>place-self: stretch</c>
    /// margin-box-fills-IMCB path (mirrors the bridge's <c>hasPercentBoxProps</c>).
    /// </summary>
    private bool HasPercentBoxProps() =>
        HasPercent(MarginTop) || HasPercent(MarginRight) || HasPercent(MarginBottom) || HasPercent(MarginLeft)
        || HasPercent(PaddingTop) || HasPercent(PaddingRight) || HasPercent(PaddingBottom) || HasPercent(PaddingLeft)
        || HasPercent(Top) || HasPercent(Right) || HasPercent(Bottom) || HasPercent(Left);

    /// <summary>
    /// Places a childless <c>position-area</c> box whose margin/padding/inset carry
    /// percentages (<see cref="HasPercentBoxProps"/>). Mirrors the bridge's
    /// <c>hasPercentBoxProps</c> branch: percentage margins and padding resolve against the
    /// cell inline size, the element's margin box stretches to fill the inset-modified
    /// containing block (IMCB), and the content size is the IMCB minus margin+border+padding
    /// (<see cref="PositionAreaGrid.ContentSizeFillingImcb"/>). The resolved padding is
    /// written back as px so the content-box background paints at the right place; the
    /// border box is set from the content size + padding + border, positioned so the margin
    /// box's origin sits at the IMCB origin.
    /// </summary>
    private void ApplyPercentBoxPropsPlacement(PositionAreaBox target, PositionAreaCell cell)
    {
        // CSS Writing Modes §7.4: percentage margins and padding resolve against the
        // INLINE size of the containing block. The containing block here is the
        // position-area cell in the containing block's writing mode, so the inline size is
        // the cell width for a horizontal CB and the cell HEIGHT for a vertical one.
        // (Insets keep resolving against their own physical axis — done in ResolveInset.)
        var cbBox = FindPositionedContainingBlock();
        double inlineSize = cbBox != null && IsVerticalWritingMode(cbBox.WritingMode)
            ? cell.Height
            : cell.Width;

        // Margins and padding: percentage against the cell inline size, else px (matching
        // the bridge's ResolvePctOrPx — but the bridge always used the width, so this
        // diverges from the baked path only for a vertical containing block, which the
        // bridge gets wrong).
        double mT = ResolveEdgeAgainstCell(MarginTop, inlineSize), mR = ResolveEdgeAgainstCell(MarginRight, inlineSize);
        double mB = ResolveEdgeAgainstCell(MarginBottom, inlineSize), mL = ResolveEdgeAgainstCell(MarginLeft, inlineSize);
        double pT = ResolveEdgeAgainstCell(PaddingTop, inlineSize), pR = ResolveEdgeAgainstCell(PaddingRight, inlineSize);
        double pB = ResolveEdgeAgainstCell(PaddingBottom, inlineSize), pL = ResolveEdgeAgainstCell(PaddingLeft, inlineSize);
        double bT = NativeBorderPx(BorderTopWidth, BorderTopStyle), bR = NativeBorderPx(BorderRightWidth, BorderRightStyle);
        double bB = NativeBorderPx(BorderBottomWidth, BorderBottomStyle), bL = NativeBorderPx(BorderLeftWidth, BorderLeftStyle);

        var (contentW, contentH) = PositionAreaGrid.ContentSizeFillingImcb(
            target.ImcbWidth, target.ImcbHeight,
            new PositionAreaEdges(mT, mR, mB, mL),
            new PositionAreaEdges(bT, bR, bB, bL),
            new PositionAreaEdges(pT, pR, pB, pL));

        // Write the resolved margins/padding as px (the bridge writes these inline too) so
        // the box's own box-model reads the cell-resolved used values, not percentages
        // re-resolved against the box's own width. Padding is what the content-box
        // background/ClientRectangle depend on.
        var px = System.Globalization.CultureInfo.InvariantCulture;
        MarginTop = mT.ToString(px) + "px"; MarginRight = mR.ToString(px) + "px";
        MarginBottom = mB.ToString(px) + "px"; MarginLeft = mL.ToString(px) + "px";
        PaddingTop = pT.ToString(px) + "px"; PaddingRight = pR.ToString(px) + "px";
        PaddingBottom = pB.ToString(px) + "px"; PaddingLeft = pL.ToString(px) + "px";

        // Border box = content + padding + border; margin box origin sits at the IMCB origin.
        double borderBoxW = contentW + pL + pR + bL + bR;
        double borderBoxH = contentH + pT + pB + bT + bB;
        Size = new System.Drawing.SizeF((float)borderBoxW, (float)borderBoxH);

        double borderBoxLeft = target.ImcbLeft + mL;
        double borderBoxTop = target.ImcbTop + mT;
        OffsetLeft(borderBoxLeft - Location.X);
        OffsetTop(borderBoxTop - Location.Y);
    }

    /// <summary>Resolves a margin/padding component against the cell inline size:
    /// a percentage → that fraction of <paramref name="cellW"/>, a px length (or bare
    /// number) → itself, anything else → 0. Mirrors the bridge's <c>ResolvePctOrPx</c>.</summary>
    private static double ResolveEdgeAgainstCell(string? value, double cellW)
    {
        if (!string.IsNullOrWhiteSpace(value) && value!.TrimEnd().EndsWith('%')
            && double.TryParse(value.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return cellW * pct / 100.0;
        return ParsePxOnly(value) ?? 0;
    }

    private static bool HasPercent(string? value) => value != null && value.Contains('%');

    /// <summary>
    /// Font-free used border width for one side: <c>0</c> when the border style is
    /// absent/<c>none</c>; otherwise the CSS <c>thin</c>/<c>medium</c>/<c>thick</c>
    /// keywords are resolved through the canonical
    /// <see cref="CssLengthParser.GetActualBorderWidth"/> (1/3/5 px, default <c>medium</c>)
    /// and a pixel length is taken as-is. The keyword widths are the single source of
    /// truth shared with the bridge anchor resolver so the native and baked paths agree.
    /// </summary>
    private double NativeBorderPx(string? width, string? style)
    {
        if (string.IsNullOrEmpty(style) || string.Equals(style, "none", StringComparison.OrdinalIgnoreCase))
            return 0;
        var w = width?.Trim();
        if (string.IsNullOrEmpty(w) ||
            string.Equals(w, "medium", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(w, "thin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(w, "thick", StringComparison.OrdinalIgnoreCase))
            return CssLengthParser.GetActualBorderWidth(
                string.IsNullOrEmpty(w) ? "medium" : w.ToLowerInvariant(), 0);
        return ParsePxOnly(w) ?? CssLengthParser.GetActualBorderWidth("medium", 0);
    }

    /// <summary>Font-free used padding for one side: a pixel length (or bare number),
    /// else <c>0</c>. Percentage padding is excluded upstream by the size gate.</summary>
    private double NativePaddingPx(string? value) => ParsePxOnly(value) ?? 0;

    private static double? ParsePxOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value!.Trim();
        if (v.Contains('%')) return null;
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            v = v[..^2];
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px)
            ? px
            : null;
    }

    /// <summary>
    /// Computes where this <c>position-area</c> box's border box should sit against its
    /// named anchor. Returns <c>false</c> (leave the box where it is) when the box is
    /// not an MVP anchor-positioned box or its anchor is not registered.
    /// </summary>
    internal bool TryResolvePositionAreaTarget(AnchorRegistry registry, out PositionAreaBox target, out PositionAreaCell cell)
    {
        target = default;
        cell = default;

        var area = PositionArea;
        if (string.IsNullOrEmpty(area) || area == "none")
            return false;

        var anchorName = PositionAnchor;
        if (string.IsNullOrEmpty(anchorName) || anchorName == "auto")
            return false; // MVP requires an explicit position-anchor.

        // Containing-block padding box, in document coordinates.
        var cb = FindPositionedContainingBlock();
        GetAbsoluteContainingBlockPaddingBox(cb, out double cbX, out double cbY, out double cbW, out double cbH);

        var parsed = PositionAreaValue.Parse(area);
        // Scope resolution: when several elements share this anchor-name, bind to the
        // candidate inside the query's own containing block (the anchor box a descendant of
        // `cb`), mirroring the bridge's ResolveAnchorForElement; unique names are unaffected.
        var resolved = registry.ResolvePositionAreaCell(anchorName, parsed, cbX, cbY, cbW, cbH,
            inScope: scope => scope is CssBox src && IsBoxDescendantOf(src, cb));
        if (resolved is not { } c)
            return false; // Anchor not registered.
        cell = c;

        // Width/height inputs to the grid resolution. For a size-apply box (childless,
        // content-box, no percentage box props — see CanApplyNativeAnchorSize) feed the
        // box's authored CSS width/height so the used size fills the cell / honours an
        // explicit or percentage length exactly as the baked bridge path does. For every
        // other box keep the reposition-only MVP inputs (the already-laid-out border-box
        // size), so its alignment position is byte-identical to the shipped behaviour.
        double? explicitW, percentW, explicitH, percentH;
        if (CanApplyNativeAnchorSize())
        {
            (explicitW, percentW) = ParseSizeComponent(Width);
            (explicitH, percentH) = ParseSizeComponent(Height);
        }
        else
        {
            (explicitW, percentW) = (Size.Width, null);
            (explicitH, percentH) = (Size.Height, null);
        }

        target = PositionAreaGrid.ResolveElementBox(
            c,
            ResolveInset(Top, c.Height), ResolveInset(Right, c.Width),
            ResolveInset(Bottom, c.Height), ResolveInset(Left, c.Width),
            explicitWidth: explicitW, percentWidth: percentW,
            explicitHeight: explicitH, percentHeight: percentH,
            parsed);
        return true;
    }

    /// <summary>
    /// Parses a CSS <c>width</c>/<c>height</c> component into the (explicit-px, percent)
    /// pair <see cref="PositionAreaGrid.ResolveElementBox"/> expects: a pixel length (or
    /// bare number) → <c>(px, null)</c>; a percentage like <c>50%</c> → <c>(null, 50)</c>;
    /// <c>auto</c>/unparseable/other units → <c>(null, null)</c> (fill the cell). Mirrors
    /// the bridge's <c>TryParsePx</c>/<c>TryParsePercent</c> exactly for parity.
    /// </summary>
    internal static (double? explicitPx, double? percent) ParseSizeComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);
        var v = value!.Trim();
        if (v.EndsWith('%'))
        {
            return double.TryParse(v[..^1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct)
                ? (null, pct)
                : (null, null);
        }
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            v = v[..^2];
        return double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var px)
            ? (px, null)
            : (null, null);
    }

    /// <summary>
    /// Resolves a single position-area inset value (<c>px</c> or <c>%</c> of
    /// <paramref name="basis"/>) to pixels; <c>auto</c>/unparseable/other units → 0
    /// (matching the bridge's inset handling for the MVP subset).
    /// </summary>
    internal static double ResolveInset(string value, double basis)
    {
        if (string.IsNullOrEmpty(value) || value == "auto")
            return 0;
        var len = new CssLength(value);
        if (len.HasError)
            return 0;
        // CssLength stores a percentage as a fraction (0.25 for "25%").
        if (len.IsPercentage)
            return basis * len.Number;
        return len.Unit == CssUnit.Px ? len.Number : 0;
    }

    /// <summary>Whether a raw CSS value carries an <c>anchor-size()</c> function.</summary>
    private static bool HasAnchorSize(string? value) =>
        value != null && value.Contains("anchor-size(", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sizes a childless box whose <c>width</c>/<c>height</c> use <c>anchor-size()</c> to the
    /// named anchor's dimension (P5.8d.2b anchor-size() expansion) — the engine equivalent of
    /// the bridge's <c>ResolveAnchorSizeFunctions</c> pre-bake. The resolved value is the used
    /// content or border size per <c>box-sizing</c> (mirroring how the bridge writes the
    /// property and the renderer applies it); the box is grown in place from its current
    /// origin, so any following <c>anchor()</c>-inset placement repositions against the new
    /// size. Returns <c>false</c> (size unchanged) when it is not an MVP anchor-size box or the
    /// anchor is unregistered.
    /// </summary>
    internal bool TryApplyNativeAnchorSizing(AnchorRegistry registry)
    {
        if (Boxes.Count != 0 || Words.Count != 0)
            return false; // childless only (a re-flow would be needed otherwise)
        if (Position != CssConstants.Absolute && Position != CssConstants.Fixed)
            return false;
        bool wAnchor = HasAnchorSize(Width), hAnchor = HasAnchorSize(Height);
        if (!wAnchor && !hAnchor)
            return false;

        var cb = FindPositionedContainingBlock();
        Func<object?, bool> inScope = scope => scope is CssBox src && IsBoxDescendantOf(src, cb);

        double? contentW = wAnchor ? ResolveAnchorSizeComponent(Width, registry, inScope) : null;
        double? contentH = hAnchor ? ResolveAnchorSizeComponent(Height, registry, inScope) : null;
        if (wAnchor && contentW is null) return false;
        if (hAnchor && contentH is null) return false;

        // box-sizing: content-box (default) — the resolved value is the content size, so add
        // padding+border for the border box. border-box — it already is the border box (clamp
        // to at least padding+border so the content stays non-negative). Font-free px, matching
        // the bridge and the position-area sizing path.
        bool borderBox = string.Equals(BoxSizing, "border-box", StringComparison.OrdinalIgnoreCase);
        double padBorderW =
            NativeBorderPx(BorderLeftWidth, BorderLeftStyle) + NativePaddingPx(PaddingLeft)
            + NativePaddingPx(PaddingRight) + NativeBorderPx(BorderRightWidth, BorderRightStyle);
        double padBorderH =
            NativeBorderPx(BorderTopWidth, BorderTopStyle) + NativePaddingPx(PaddingTop)
            + NativePaddingPx(PaddingBottom) + NativeBorderPx(BorderBottomWidth, BorderBottomStyle);

        float newW = Size.Width, newH = Size.Height;
        if (contentW is double cw)
            newW = (float)(borderBox ? System.Math.Max(cw, padBorderW) : cw + padBorderW);
        if (contentH is double ch)
            newH = (float)(borderBox ? System.Math.Max(ch, padBorderH) : ch + padBorderH);
        Size = new System.Drawing.SizeF(newW, newH);
        return true;
    }

    /// <summary>
    /// Resolves a <c>width</c>/<c>height</c> value that uses <c>anchor-size()</c> to the
    /// anchor's dimension (px). The default anchor is the box's <c>position-anchor</c> when the
    /// function names none. Returns <c>null</c> for an unregistered anchor / non-numeric result.
    /// </summary>
    private double? ResolveAnchorSizeComponent(
        string? value, AnchorRegistry registry, Func<object?, bool> inScope)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        string rewritten = AnchorFunction.RewriteSize(value, r =>
        {
            string name = string.IsNullOrEmpty(r.Name) ? (PositionAnchor ?? string.Empty) : r.Name!;
            if (string.IsNullOrEmpty(name) || name == "auto")
                return "0px";
            var size = registry.ResolveAnchorSize(name, r.Dimension, inScope);
            return size is double s ? s.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px" : "0px";
        });
        var len = new CssLength(rewritten);
        if (len.HasError || len.IsPercentage)
            return null;
        return len.Unit == CssUnit.Px ? len.Number : null;
    }

    /// <summary>Whether a CSS <c>width</c>/<c>height</c> value is <c>auto</c> (or unset), so an
    /// opposing pair of insets determines the used size.</summary>
    private static bool IsAutoLength(string? value) =>
        string.IsNullOrEmpty(value) || value == CssConstants.Auto;

    /// <summary>Whether a raw CSS inset value carries an <c>anchor()</c> function.</summary>
    private static bool InsetHasAnchor(string? value) =>
        value != null && value.Contains("anchor(", System.StringComparison.OrdinalIgnoreCase)
        && !value.Contains("anchor-size(", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Places a box positioned by <c>anchor()</c> functions in its physical insets
    /// (<c>left</c>/<c>right</c>/<c>top</c>/<c>bottom</c>) — the engine equivalent of the
    /// bridge's <c>ResolveAnchorFunctions</c> pre-bake (P5.8d.2b anchor()-insets expansion).
    /// MVP: reposition-only (the box's already-laid-out size is kept), at most one inset per
    /// axis (opposing-inset sizing stays on the bridge path). For each present inset it
    /// resolves the anchor() reference to a CB-frame inset value via the promoted
    /// <see cref="AnchorGeometry.ResolveEdge"/> (mirroring the bridge exactly), converts it to
    /// the box's document-absolute margin edge, and offsets the border box there. Returns
    /// <c>false</c> (leaves the box put) when it is not an anchor()-inset box or its anchor is
    /// unregistered.
    /// </summary>
    internal bool TryApplyAnchorInsetPlacement(AnchorRegistry registry)
    {
        if (Position != CssConstants.Absolute && Position != CssConstants.Fixed)
            return false;
        bool anchorLeft = InsetHasAnchor(Left), anchorRight = InsetHasAnchor(Right);
        bool anchorTop = InsetHasAnchor(Top), anchorBottom = InsetHasAnchor(Bottom);
        if (!anchorLeft && !anchorRight && !anchorTop && !anchorBottom)
            return false;

        var cb = FindPositionedContainingBlock();
        GetAbsoluteContainingBlockPaddingBox(cb, out double cbX, out double cbY, out double cbW, out double cbH);
        // Scope resolution mirrors the position-area path: bind a shared anchor-name to the
        // candidate inside the query's own containing block.
        Func<object?, bool> inScope = scope => scope is CssBox src && IsBoxDescendantOf(src, cb);

        double? leftInset = ResolveInsetMaybeAnchor(Left, AnchorInsetProperty.Left, registry, inScope, cbX, cbY, cbW, cbH);
        double? rightInset = ResolveInsetMaybeAnchor(Right, AnchorInsetProperty.Right, registry, inScope, cbX, cbY, cbW, cbH);
        double? topInset = ResolveInsetMaybeAnchor(Top, AnchorInsetProperty.Top, registry, inScope, cbX, cbY, cbW, cbH);
        double? bottomInset = ResolveInsetMaybeAnchor(Bottom, AnchorInsetProperty.Bottom, registry, inScope, cbX, cbY, cbW, cbH);

        // MVP requires an anchor-resolved inset to have succeeded on the anchored axis.
        if (anchorLeft && leftInset is null) return false;
        if (anchorRight && rightInset is null) return false;
        if (anchorTop && topInset is null) return false;
        if (anchorBottom && bottomInset is null) return false;

        double bw = Size.Width, bh = Size.Height;

        // Opposing-inset sizing (P5.8d.2b): both insets present on an axis + an auto length +
        // a childless box → the used size is determined by the two insets (CSS 2.1 §10.3.7 /
        // §10.6.4: the border box fills the inset-modified containing block minus margins).
        // Childless-only so no subtree needs a re-flow; an explicit length keeps its size
        // (the reposition-only branch below positions by the start inset). Grow from the
        // resolved size before positioning.
        bool childless = Boxes.Count == 0 && Words.Count == 0;
        if (childless && leftInset is double li && rightInset is double ri && IsAutoLength(Width))
            bw = System.Math.Max(0, cbW - li - ri - ActualMarginLeft - ActualMarginRight);
        if (childless && topInset is double ti && bottomInset is double bi && IsAutoLength(Height))
            bh = System.Math.Max(0, cbH - ti - bi - ActualMarginTop - ActualMarginBottom);
        if (bw != Size.Width || bh != Size.Height)
            Size = new System.Drawing.SizeF((float)bw, (float)bh);

        // Horizontal: the inset is measured from the CB padding edge to the box's MARGIN
        // edge; add/subtract the used margin to reach the border box. When only one inset is
        // present the laid-out (or opposing-inset-resolved) size is kept. Prefer left; else right.
        double? borderLeft = null;
        if (leftInset is double L)
            borderLeft = cbX + L + ActualMarginLeft;
        else if (rightInset is double R)
            borderLeft = cbX + cbW - R - ActualMarginRight - bw;

        double? borderTop = null;
        if (topInset is double T)
            borderTop = cbY + T + ActualMarginTop;
        else if (bottomInset is double B)
            borderTop = cbY + cbH - B - ActualMarginBottom - bh;

        if (borderLeft is double bl)
            OffsetLeft((float)(bl - Location.X));
        if (borderTop is double bt)
            OffsetTop((float)(bt - Location.Y));
        return borderLeft != null || borderTop != null;
    }

    /// <summary>
    /// Resolves one physical inset that may contain an <c>anchor()</c> function to a CB-frame
    /// length (px). A plain length/percentage resolves normally; an <c>anchor()</c> reference
    /// resolves against the named anchor's document-absolute rect (converted to the CB frame)
    /// via <see cref="AnchorGeometry.ResolveEdge"/> — the same math the bridge bakes. Returns
    /// <c>null</c> for <c>auto</c>/absent/unparseable/unregistered, or a value the reposition
    /// MVP does not model (e.g. a <c>calc()</c> the rewrite leaves non-numeric).
    /// </summary>
    private double? ResolveInsetMaybeAnchor(
        string? value, AnchorInsetProperty property, AnchorRegistry registry,
        Func<object?, bool> inScope, double cbX, double cbY, double cbW, double cbH)
    {
        if (string.IsNullOrEmpty(value) || value == CssConstants.Auto)
            return null;

        double basis = property is AnchorInsetProperty.Left or AnchorInsetProperty.Right ? cbW : cbH;

        if (!InsetHasAnchor(value))
        {
            var plain = new CssLength(value);
            if (plain.HasError) return null;
            if (plain.IsPercentage) return basis * plain.Number;
            return plain.Unit == CssUnit.Px ? plain.Number : null;
        }

        // Rewrite each anchor() reference to a px string against the resolved anchor rect,
        // then parse the (now numeric) result. The default anchor is the box's
        // position-anchor when the function names none.
        string rewritten = AnchorFunction.Rewrite(value, r =>
        {
            string name = string.IsNullOrEmpty(r.Name) ? (PositionAnchor ?? string.Empty) : r.Name!;
            if (string.IsNullOrEmpty(name) || name == "auto" ||
                !registry.TryResolveRect(name, inScope, out var a))
                return r.Fallback ?? "0px";

            double v = AnchorGeometry.ResolveEdge(
                a.Left - cbX, a.Top - cbY, a.Right - cbX, a.Bottom - cbY,
                r.Side, 0, 0, property, cbW, cbH);
            return v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
        });

        var len = new CssLength(rewritten);
        if (len.HasError) return null;
        if (len.IsPercentage) return basis * len.Number;
        return len.Unit == CssUnit.Px ? len.Number : null;
    }

    /// <summary>
    /// Centres a box on its anchor per <c>align-self: anchor-center</c> (block axis) /
    /// <c>justify-self: anchor-center</c> (inline axis) — the engine equivalent of the bridge's
    /// <c>ResolveAnchorCenter</c> (P5.8d.2b anchor-center expansion). Applies only to an
    /// absolutely/fixed-positioned box with <c>position-anchor</c> and no <c>position-area</c>
    /// (position-area does its own alignment). Repositions the centred axis so the box's centre
    /// aligns with the anchor's centre; the other axis keeps its laid-out position. The anchor rect
    /// is the same registered (scroll-shifted) geometry the placement passes use, so the centring is
    /// scroll-correct. Returns <c>false</c> when it is not an anchor-center box or the anchor is
    /// unregistered.
    /// </summary>
    internal bool TryApplyAnchorCenter(AnchorRegistry registry)
    {
        if (Position != CssConstants.Absolute && Position != CssConstants.Fixed)
            return false;
        if (PositionArea != "none")
            return false;
        var anchorName = PositionAnchor;
        if (string.IsNullOrEmpty(anchorName) || anchorName == "auto")
            return false;
        bool alignCenter = string.Equals(AlignSelf, "anchor-center", StringComparison.OrdinalIgnoreCase);
        bool justifyCenter = string.Equals(JustifySelf, "anchor-center", StringComparison.OrdinalIgnoreCase);
        if (!alignCenter && !justifyCenter)
            return false;

        var cb = FindPositionedContainingBlock();
        Func<object?, bool> inScope = scope => scope is CssBox src && IsBoxDescendantOf(src, cb);
        if (!registry.TryResolveRect(anchorName, inScope, out var a))
            return false;

        // align-self centres in the block axis (Y for horizontal writing modes), justify-self in
        // the inline axis (X). The non-centred axis keeps its laid-out position.
        if (alignCenter)
        {
            double centredTop = (a.Top + a.Bottom) / 2 - Size.Height / 2.0;
            OffsetTop((float)(centredTop - Location.Y));
        }
        if (justifyCenter)
        {
            double centredLeft = (a.Left + a.Right) / 2 - Size.Width / 2.0;
            OffsetLeft((float)(centredLeft - Location.X));
        }
        return true;
    }

    /// <summary>
    /// The fallback-name list a position-try box carries — <c>position-try-fallbacks</c>
    /// when set, else the <c>position-try</c> shorthand (mirrors the bridge's
    /// <c>position-try-fallbacks ?? position-try</c>). <c>null</c> when neither is set
    /// (the projected defaults are <c>none</c>/<c>normal</c>).
    /// </summary>
    private string? PositionTryFallbackList()
    {
        if (!string.IsNullOrWhiteSpace(PositionTryFallbacks) && PositionTryFallbacks != "none")
            return PositionTryFallbacks;
        if (!string.IsNullOrWhiteSpace(PositionTry) && PositionTry != "normal")
            return PositionTry;
        return null;
    }

    /// <summary>
    /// Native <c>@position-try</c> fallback selection (P5.8d.2b position-try expansion) — the
    /// engine equivalent of the bridge's <c>TryApplyFallback</c>. Runs after the base
    /// placement: if the box's base overflows its containing block, it walks the box's
    /// <c>position-try-fallbacks</c> list and applies the first <c>@position-try</c> rule
    /// whose resolved position/size fits the containing block. The rule bodies come from the
    /// out-of-band <see cref="NativeAnchorPlacement.PositionTryRules"/> channel (a stylesheet
    /// at-rule never reaches the cascaded box properties). Reposition + (childless) resize,
    /// matching the bridge; a no-op when the box has no position-try, no rules are available,
    /// or the base already fits. Returns <c>false</c> in those no-op cases, <c>true</c> when a
    /// fallback was applied.
    /// </summary>
    internal bool TryApplyPositionTryFallback(AnchorRegistry registry)
    {
        if (Position != CssConstants.Absolute && Position != CssConstants.Fixed)
            return false;
        var fallbackList = PositionTryFallbackList();
        if (fallbackList is null)
            return false;
        var rules = NativeAnchorPlacement.PositionTryRules;
        if (rules is null || rules.Count == 0)
            return false;

        var cb = FindPositionedContainingBlock();
        GetAbsoluteContainingBlockPaddingBox(cb, out double cbX, out double cbY, out double cbW, out double cbH);
        Func<object?, bool> inScope = scope => scope is CssBox src && IsBoxDescendantOf(src, cb);

        // Base placement geometry, in the containing-block frame. baseLeft/baseTop are the
        // already-placed used position; baseRight/baseBottom are the box's numeric right/bottom
        // insets (px only, else 0 — an anchor()/auto inset is already reflected in the used
        // position). Mirrors the bridge's IMCB computation (cb − left − right per axis).
        double baseLeft = Bounds.X - cbX;
        double baseTop = Bounds.Y - cbY;
        double baseWidth = Bounds.Width;
        double baseHeight = Bounds.Height;
        double baseRight = ParsePxOnly(Right) ?? 0;
        double baseBottom = ParsePxOnly(Bottom) ?? 0;
        double imcbWidth = cbW - baseLeft - baseRight;
        double imcbHeight = cbH - baseTop - baseBottom;

        if (!AnchorGeometry.Overflows(baseLeft, baseTop, baseWidth, baseHeight, cbW, cbH, imcbWidth, imcbHeight))
            return false; // Base fits — no fallback needed.

        foreach (var name in PositionTryRule.ParseFallbackList(fallbackList))
        {
            if (!rules.TryGetValue(name, out var tryProps))
                continue;

            // Overlay the fallback's declarations on the box's own position/size CSS.
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["left"] = Left ?? "auto",
                ["right"] = Right ?? "auto",
                ["top"] = Top ?? "auto",
                ["bottom"] = Bottom ?? "auto",
                ["width"] = Width ?? "auto",
                ["height"] = Height ?? "auto",
            };
            foreach (var kv in tryProps)
                merged[kv.Key] = kv.Value;

            // inset: auto resets any inset the fallback didn't itself set, so the base
            // insets don't leak through (mirrors the bridge).
            if (merged.TryGetValue("inset", out var insetVal) && insetVal.Trim() == "auto")
            {
                foreach (var side in new[] { "left", "right", "top", "bottom" })
                    if (!tryProps.ContainsKey(side))
                        merged[side] = "auto";
            }

            // Resolve explicit width/height first so left-from-right / top-from-bottom use the
            // right element size; else keep the base used size.
            double tryWidth = baseWidth, tryHeight = baseHeight;
            if (merged.TryGetValue("width", out var wv) && ParsePxOnly(wv) is double pw) tryWidth = pw;
            if (merged.TryGetValue("height", out var hv) && ParsePxOnly(hv) is double ph) tryHeight = ph;

            double tryLeft = 0, tryTop = 0;
            if (IsPresentInset(merged, "left", out var lv))
                tryLeft = ResolveTryEdge(lv, AnchorInsetProperty.Left, registry, inScope, cbX, cbY, cbW, cbH) ?? 0;
            if (IsPresentInset(merged, "right", out var rv) &&
                ResolveTryEdge(rv, AnchorInsetProperty.Right, registry, inScope, cbX, cbY, cbW, cbH) is double rightPx)
            {
                if (!IsPresentInset(merged, "left", out _))
                    tryLeft = cbW - rightPx - tryWidth;   // no left → left from right + width
                else
                    tryWidth = cbW - tryLeft - rightPx;   // both → width from the two insets
            }
            if (IsPresentInset(merged, "top", out var tv))
                tryTop = ResolveTryEdge(tv, AnchorInsetProperty.Top, registry, inScope, cbX, cbY, cbW, cbH) ?? 0;
            if (IsPresentInset(merged, "bottom", out var bv) &&
                ResolveTryEdge(bv, AnchorInsetProperty.Bottom, registry, inScope, cbX, cbY, cbW, cbH) is double bottomPx)
            {
                if (!IsPresentInset(merged, "top", out _))
                    tryTop = cbH - bottomPx - tryHeight;
                else
                    tryHeight = cbH - tryTop - bottomPx;
            }

            if (!AnchorGeometry.Fits(tryLeft, tryTop, tryWidth, tryHeight, cbW, cbH))
                continue;

            // Apply: reposition to the CB-frame candidate, and resize a childless box (a
            // childful box keeps its laid-out size — a re-flow would be needed).
            if (Boxes.Count == 0 && Words.Count == 0 &&
                (tryWidth != Size.Width || tryHeight != Size.Height))
                Size = new System.Drawing.SizeF((float)tryWidth, (float)tryHeight);
            OffsetLeft((float)(cbX + tryLeft - Location.X));
            OffsetTop((float)(cbY + tryTop - Location.Y));
            return true;
        }

        return false;
    }

    /// <summary>Whether a merged inset entry is present and not <c>auto</c>.</summary>
    private static bool IsPresentInset(Dictionary<string, string> merged, string key, out string value)
    {
        value = merged.GetValueOrDefault(key) ?? "auto";
        return !string.IsNullOrWhiteSpace(value) && value.Trim() != "auto";
    }

    /// <summary>
    /// Resolves one <c>@position-try</c> inset declaration (a plain length/percentage or an
    /// <c>anchor()</c> reference) to a containing-block-frame length (px). Mirrors the base
    /// anchor()-inset resolution (<see cref="ResolveInsetMaybeAnchor"/>): the anchor rect is
    /// taken document-absolute and converted to the CB frame, with no scroll adjustment (the
    /// fallback path, like the bridge's, uses none). Returns <c>null</c> for
    /// unparseable/unregistered.
    /// </summary>
    private double? ResolveTryEdge(
        string? value, AnchorInsetProperty property, AnchorRegistry registry,
        Func<object?, bool> inScope, double cbX, double cbY, double cbW, double cbH)
    {
        if (string.IsNullOrEmpty(value) || value == CssConstants.Auto)
            return null;
        double basis = property is AnchorInsetProperty.Left or AnchorInsetProperty.Right ? cbW : cbH;

        if (!value.Contains("anchor(", System.StringComparison.OrdinalIgnoreCase))
            return AsCbLength(new CssLength(value), basis);

        string rewritten = AnchorFunction.Rewrite(value, r =>
        {
            string name = string.IsNullOrEmpty(r.Name) ? (PositionAnchor ?? string.Empty) : r.Name!;
            if (string.IsNullOrEmpty(name) || name == "auto" ||
                !registry.TryResolveRect(name, inScope, out var a))
                return r.Fallback ?? "0px";
            double v = AnchorGeometry.ResolveEdge(
                a.Left - cbX, a.Top - cbY, a.Right - cbX, a.Bottom - cbY,
                r.Side, 0, 0, property, cbW, cbH);
            return v.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
        });

        return AsCbLength(new CssLength(rewritten), basis);
    }

    /// <summary>
    /// Projects a parsed <c>@position-try</c> inset length onto a containing-block-frame px value:
    /// a percentage of <paramref name="basis"/>, a <c>px</c> length, or <c>0</c> in any unit
    /// (a unitless/zero length is zero — <c>CssLength</c> parses bare <c>0</c> with
    /// <see cref="CssUnit.None"/>, and <c>right: 0</c> is common in fallback rules). Any other
    /// unit (em/vw/… with no font/viewport context here) or a parse error yields <c>null</c>.
    /// </summary>
    private static double? AsCbLength(CssLength len, double basis)
    {
        if (len.HasError) return null;
        if (len.IsPercentage) return basis * len.Number;
        return len.Unit == CssUnit.Px || len.Number == 0 ? len.Number : null;
    }
}
