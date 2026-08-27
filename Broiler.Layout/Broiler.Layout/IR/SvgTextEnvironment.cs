using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// The host font services <see cref="SvgRenderer"/> needs to draw an SVG <c>&lt;text&gt;</c>,
/// published for the duration of one render pass.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SvgRenderer"/> turns markup into display items with no access to the box tree, so it
/// has nowhere to ask for a font — and a <c>DrawSvgTextItem</c> whose <c>FontHandle</c> is null is
/// silently dropped by the raster backend, which is why SVG text used to paint nothing at all. The
/// paint walker that calls <see cref="SvgRenderer.RenderSvgContent"/> lives in another component, so
/// the environment is published here by <see cref="FragmentTreeBuilder.Build"/> instead of being
/// threaded through that call.
/// </para>
/// <para>
/// Thread-static for the same reason <see cref="SvgFilterTable"/> and <see cref="SvgClipPathTable"/>
/// are: concurrent renders on different threads must not share a document's state. Unset is a
/// supported state that resolves to no font, which is the pre-existing behaviour rather than a new
/// failure — and <see cref="SvgImageRaster"/> deliberately clears it, so an SVG rasterised as an
/// image never depends on which thread decoded it (see the remarks there).
/// </para>
/// </remarks>
public static class SvgTextEnvironment
{
    [ThreadStatic]
    private static ILayoutEnvironment? _environment;

    /// <summary>Publishes the environment for this render pass; <c>null</c> clears it.</summary>
    internal static void Reset(ILayoutEnvironment? environment)
    {
        _environment = environment;
        AmbientRenderState.MarkEstablished(AmbientRenderState.Slots.SvgTables);
    }

    /// <summary>
    /// <see cref="Reset"/>, reachable from <see cref="AmbientRenderState.Establish"/> outside this
    /// namespace. A worker thread establishes the slot by clearing it: no font resolution is the
    /// safe default, and the thread that actually renders publishes its own environment.
    /// </summary>
    public static void ResetForThread() => Reset(null);

    /// <summary>The environment for this pass, or <see langword="null"/> when none was published.</summary>
    internal static ILayoutEnvironment? Current => _environment;

    /// <summary>
    /// Resolves a font for SVG text. <paramref name="sizePx"/> is in CSS pixels (SVG user units
    /// already scaled to the viewport); the environment takes points, the unit
    /// <see cref="Engine.CssBoxProperties.ActualFont"/> resolves in.
    /// </summary>
    internal static ILayoutFont? GetFont(string family, double sizePx, LayoutFontStyle style)
    {
        var environment = _environment;
        if (environment == null || sizePx <= 0)
            return null;

        return environment.GetFont(family, sizePx * CSS.CssMetrics.PxToPt, style);
    }

    /// <summary>Measures <paramref name="text"/>, for <c>text-anchor</c> placement.</summary>
    internal static float MeasureWidth(ILayoutFont? font, string text)
    {
        var environment = _environment;
        if (environment == null || font == null || string.IsNullOrEmpty(text))
            return 0f;

        return environment.MeasureText(font, text).Width;
    }
}
