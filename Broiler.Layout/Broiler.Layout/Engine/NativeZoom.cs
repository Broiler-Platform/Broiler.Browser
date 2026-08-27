namespace Broiler.Layout.Engine;

/// <summary>
/// Feature flag gating the native CSS <c>zoom</c> model (HtmlBridge complexity-reduction roadmap Phase 5,
/// the visual-viewport / CSS-<c>zoom</c> endgame — blocker (b2)). Default off: while off, the layout
/// engine ignores the <c>zoom</c> property (<see cref="CssBoxProperties.EffectiveZoom"/> is always
/// <c>1.0</c>) and the HtmlBridge serialization bake
/// (<c>DomBridge.ApplyZoomSerializationStyles</c>) continues to pre-multiply length properties by the
/// compounded used zoom as it does today.
/// </summary>
/// <remarks>
/// <para>
/// Thread-static so a test (or a future per-document caller) can enable it for one layout without
/// affecting concurrently-running layouts, mirroring <see cref="NativeAnchorPlacement.Enabled"/>.
/// </para>
/// <para>
/// This is the foundation increment: the engine gains a per-box <c>zoom</c> property and the compounding
/// <see cref="CssBoxProperties.EffectiveZoom"/> it implies. Applying that factor to used-value resolution
/// (font size, then absolute lengths — the latter via a <c>Broiler.CSS</c> length-parser change) and
/// retiring the serialization bake are subsequent increments, at which point the flag flips on for real.
/// </para>
/// </remarks>
internal static class NativeZoom
{
    [System.ThreadStatic]
    public static bool Enabled;
}
