using System;
using System.Collections.Generic;
using Broiler.CSS;

namespace Broiler.Layout;

/// <summary>
/// The used geometry of a CSS anchor element (an element carrying an
/// <c>anchor-name</c>) in the layout coordinate space: its border-box rectangle.
/// </summary>
public readonly record struct AnchorRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>
/// A registry of laid-out anchor elements keyed by <c>anchor-name</c>, and the
/// entry point for placing an anchor-positioned box against a named anchor. It
/// composes the pure geometry primitives (<see cref="PositionAreaGrid"/>,
/// <see cref="AnchorGeometry"/>) with the name→rect lookup so a single call resolves
/// an anchored box's grid cell or an <c>anchor()</c> / <c>anchor-size()</c> query.
/// </summary>
/// <remarks>
/// The engine-facing anchor-placement facade for HtmlBridge complexity-reduction
/// roadmap Phase 5 work item 3 — the piece the layout engine lacks today (it has no
/// anchor-name→box index). It is pure and additive: it holds no DOM, cascade, or box
/// tree; a caller registers the anchors it has already laid out and then queries.
/// Wiring this into the layout engine's absolute-positioning post-pass (so anchored
/// boxes are placed natively rather than pre-baked by the bridge) is a later
/// increment; this establishes the API that pass will call.
/// </remarks>
public sealed class AnchorRegistry
{
    // Anchor names are custom idents — case-sensitive (ordinal). Each name keeps ALL
    // registered candidates in registration (tree/document) order so a query can bind to
    // the acceptable anchor in its own scope when several elements share a name; a
    // scope-free lookup uses the last one (last-wins). Each candidate carries an opaque
    // caller-supplied scope token (e.g. the source box) the caller's scope predicate
    // interprets — the registry stays free of any box-tree/DOM knowledge.
    private readonly Dictionary<string, List<(AnchorRect Rect, object? Scope)>> _anchors =
        new(StringComparer.Ordinal);

    /// <summary>Number of distinct registered anchor names.</summary>
    public int Count => _anchors.Count;

    /// <summary>
    /// Registers the border-box rectangle of an anchor named <paramref name="anchorName"/>,
    /// with an optional opaque <paramref name="scope"/> token (interpreted only by the
    /// scope predicate a caller passes to the resolve methods). Multiple registrations for
    /// one name are all kept, in order; a scope-free query uses the last.
    /// </summary>
    public void Register(string anchorName, AnchorRect rect, object? scope = null)
    {
        ArgumentNullException.ThrowIfNull(anchorName);
        if (!_anchors.TryGetValue(anchorName, out var list))
            _anchors[anchorName] = list = [];
        list.Add((rect, scope));
    }

    /// <summary>Looks up a registered anchor's rectangle (last registration wins).</summary>
    public bool TryGet(string anchorName, out AnchorRect rect)
    {
        if (_anchors.TryGetValue(anchorName, out var list) && list.Count > 0)
        {
            rect = list[^1].Rect;
            return true;
        }
        rect = default;
        return false;
    }

    /// <summary>
    /// Resolves the acceptable anchor rectangle for a query. When several elements share
    /// the name and <paramref name="inScope"/> is supplied, binds to the LAST candidate
    /// (registration order) whose scope token satisfies the predicate — the query's own
    /// scope; otherwise (no predicate, single candidate, or none in scope) the last
    /// registration wins. Mirrors the bridge's <c>ResolveAnchorForElement</c>.
    /// </summary>
    private bool TryResolve(string anchorName, Func<object?, bool>? inScope, out AnchorRect rect)
    {
        if (!_anchors.TryGetValue(anchorName, out var list) || list.Count == 0)
        {
            rect = default;
            return false;
        }
        if (inScope != null && list.Count > 1)
        {
            AnchorRect? scoped = null;
            foreach (var (r, s) in list)
                if (inScope(s))
                    scoped = r; // keep the last in-scope candidate
            if (scoped is { } sr)
            {
                rect = sr;
                return true;
            }
        }
        rect = list[^1].Rect;
        return true;
    }

    /// <summary>
    /// Resolves the acceptable anchor rectangle for a query in its own scope (per
    /// <paramref name="inScope"/>, else last-wins). Public wrapper over the scope
    /// resolution for callers that need the raw rect (e.g. resolving <c>anchor()</c>
    /// insets against a caller-computed containing-block frame). Returns <c>false</c>
    /// when the anchor is not registered.
    /// </summary>
    public bool TryResolveRect(string anchorName, Func<object?, bool>? inScope, out AnchorRect rect)
        => TryResolve(anchorName, inScope, out rect);

    /// <summary>Whether the name has at least one registered candidate.</summary>
    public bool Contains(string anchorName) => _anchors.ContainsKey(anchorName);

    /// <summary>
    /// Resolves the acceptable anchor's registered <b>scope token</b> for a query (the source
    /// box the caller passed to <see cref="Register"/>), using the same scope binding as the rect
    /// resolution. Lets the position-visibility pass reach the anchor's own box (to test whether
    /// it is scrolled out / <c>visibility:hidden</c>). Returns <c>null</c> when unregistered or
    /// the token is not of type <typeparamref name="T"/>.
    /// </summary>
    public T? ResolveScope<T>(string anchorName, Func<object?, bool>? inScope) where T : class
    {
        if (!_anchors.TryGetValue(anchorName, out var list) || list.Count == 0)
            return null;
        (AnchorRect Rect, object? Scope)? chosen = null;
        if (inScope != null && list.Count > 1)
            foreach (var c in list)
                if (inScope(c.Scope))
                    chosen = c;
        chosen ??= list[^1];
        return chosen.Value.Scope as T;
    }

    /// <summary>
    /// Resolves the <c>position-area</c> grid cell for an anchored box against the
    /// named anchor (in the query's scope, per <paramref name="inScope"/>) and the box's
    /// containing-block frame. Returns <c>null</c> when the anchor is not registered (the
    /// caller falls back to normal positioning).
    /// </summary>
    public PositionAreaCell? ResolvePositionAreaCell(
        string anchorName, PositionAreaValue area,
        double cbX, double cbY, double cbWidth, double cbHeight,
        Func<object?, bool>? inScope = null)
    {
        if (!TryResolve(anchorName, inScope, out var a))
            return null;
        return PositionAreaGrid.ComputeCell(
            cbX, cbY, cbWidth, cbHeight, a.Left, a.Top, a.Right, a.Bottom, area);
    }

    /// <summary>
    /// Resolves an <c>anchor(&lt;side&gt;)</c> reference against the named anchor (in the
    /// query's scope). Returns <c>null</c> when the anchor is not registered.
    /// </summary>
    public double? ResolveAnchorEdge(
        string anchorName, AnchorSide side,
        double scrollAdjX, double scrollAdjY,
        AnchorInsetProperty property, double cbWidth, double cbHeight,
        Func<object?, bool>? inScope = null)
    {
        if (!TryResolve(anchorName, inScope, out var a))
            return null;
        return AnchorGeometry.ResolveEdge(
            a.Left, a.Top, a.Right, a.Bottom, side, scrollAdjX, scrollAdjY, property, cbWidth, cbHeight);
    }

    /// <summary>
    /// Resolves an <c>anchor-size(&lt;dimension&gt;)</c> reference against the named
    /// anchor (in the query's scope). Returns <c>null</c> when the anchor is not registered.
    /// </summary>
    public double? ResolveAnchorSize(string anchorName, AnchorSizeDimension dimension,
        Func<object?, bool>? inScope = null)
    {
        if (!TryResolve(anchorName, inScope, out var a))
            return null;
        return AnchorGeometry.ResolveSize(dimension, a.Width, a.Height);
    }
}
