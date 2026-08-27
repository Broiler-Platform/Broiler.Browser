using System.Drawing;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// Renders an SVG document that is being used <em>as an image</em> — the target of a
/// <c>background-image</c>, an <c>&lt;img src&gt;</c>, a <c>list-style-image</c> — through the same
/// <see cref="SvgRenderer"/> that draws inline <c>&lt;svg&gt;</c> markup, and replays the result onto a
/// caller-supplied surface via the caller's <see cref="IRasterBackend"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>NOT YET THE LIVE PATH.</b> The image backend still draws SVG with its own renderer; this type is
/// the seam that will replace it, landed ahead of the switch because the switch is blocked on one
/// missing capability. <see cref="SvgRenderer"/> does not resolve <b>percentage lengths</b> — neither
/// on the root <c>&lt;svg width="25%"&gt;</c> nor on a child's <c>width="100%"</c> — and the image
/// backend's renderer does. Measured over 3 974 reftests, switching today is <b>+108 / −80</b>: the
/// gains are documents the image backend could not draw, and the losses are ~70
/// <c>css-backgrounds/background-size/vector</c> tests that render blank once percentages stop
/// resolving. Teach <see cref="SvgRenderer"/> percentages against the viewport it is already handed as
/// <c>bounds</c>, re-run that sweep, and this becomes a strict improvement. See issue #1627.
/// </para>
/// <para>
/// <b>Why it exists.</b> Broiler has two SVG renderers. Inline markup goes through
/// <see cref="SvgRenderer"/>; an SVG loaded as an image goes through a second, regex-based renderer in
/// the image backend whose element set is rect, circle, ellipse, line, path and text. Anything else —
/// <c>&lt;polygon&gt;</c> most visibly — is silently dropped and the bitmap comes back fully
/// transparent, so the same file renders inline and vanishes as an image. That is a bug class rather
/// than a bug: every element added to one renderer and not the other reopens it, and the percentage
/// gap above is the same divergence pointing the other way. This type is the single seam, so that
/// there is one SVG renderer again.
/// </para>
/// <para>
/// <b>Why the replay is the caller's.</b> The display list is backend-neutral by design and
/// <c>Broiler.Layout</c> owns no surface. The image backend already has the
/// <see cref="IRasterBackend"/> that replays a display list onto its own canvas — the same one the
/// inline path uses — so it passes that in rather than this assembly growing a rasteriser it would
/// have to keep in step. Keeping the type here and the call in the image backend is also what keeps
/// the backend's side of the change to a couple of lines.
/// </para>
/// </remarks>
public static class SvgImageRaster
{
    /// <summary>
    /// Converts SVG source into the display list that draws it inside <paramref name="bounds"/>.
    /// </summary>
    /// <param name="svgXml">The SVG document source.</param>
    /// <param name="bounds">
    /// The destination rectangle in surface coordinates. A <c>viewBox</c>'d document is fitted into it
    /// under the default <c>xMidYMid meet</c>; a document without one maps its user units to pixels.
    /// </param>
    /// <param name="effectiveZoom">
    /// The compounded CSS <c>zoom</c> of the element the image is painted for; <c>1.0</c> for an image
    /// decoded on its own, which is the usual case for a background or an <c>&lt;img&gt;</c> whose box
    /// the layout has already sized.
    /// </param>
    /// <returns>
    /// The display list, empty when <paramref name="svgXml"/> is null, empty, or contains nothing this
    /// renderer draws. An empty list is not an error — it means the document painted nothing.
    /// </returns>
    /// <remarks>
    /// <b>An image raster resolves no fonts, deliberately.</b> <see cref="SvgTextEnvironment"/> is
    /// published for a document's <em>paint</em> pass, and an image raster is not that pass — an
    /// <c>&lt;img src&gt;</c> is decoded during layout, and <see cref="ImagePrefetch"/> may decode it
    /// on a worker thread, where the thread-affine slot is deliberately empty. Reading whatever the
    /// slot happens to hold would make an SVG image's text depend on which thread decoded it and on
    /// what that pooled thread rendered last: <c>ImagePrefetchTests</c>'s "svg images" case compares
    /// a serial decode against a concurrent one and catches exactly that. So the slot is cleared for
    /// the duration, and an SVG image renders the same everywhere — which for text means not at all,
    /// the behaviour it has always had. Inline <c>&lt;svg&gt;</c>, which does go through the paint
    /// pass, is unaffected.
    /// </remarks>
    public static DisplayList BuildDisplayList(string svgXml, RectangleF bounds, double effectiveZoom = 1.0)
    {
        var published = SvgTextEnvironment.Current;
        SvgTextEnvironment.Reset(null);
        try
        {
            var items = SvgRenderer.RenderSvgContent(svgXml, bounds, effectiveZoom);

            // CSS Backgrounds §2.11.2: the root element's background paints the canvas, and for an
            // SVG document used as an image that canvas is the destination box. Nothing painted it:
            // `<svg style="background: black">` came back transparent, so a file that draws tinted
            // shapes over a dark ground rendered them on white.
            //
            // It belongs here rather than in SvgRenderer because SvgRenderer serves both callers,
            // and the *inline* one must not do this: an inline <svg> is an ordinary element whose
            // CSS box already paints its background, so painting it again would double a
            // translucent colour over itself. Only an image has no box to have done it.
            // Emitted as the same rect primitive every other shape uses, so it replays through the
            // backend arm that is already there rather than needing a new one.
            if (SvgRenderer.TryGetRootBackgroundColor(svgXml, out var canvas))
            {
                items.Insert(0, new DrawSvgRectItem
                {
                    Bounds = bounds,
                    X = bounds.X,
                    Y = bounds.Y,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Fill = canvas,
                    Stroke = BColor.Empty,
                    StrokeWidth = 0,
                });
            }

            return new() { Items = items };
        }
        finally
        {
            SvgTextEnvironment.Reset(published);
        }
    }

    /// <summary>
    /// Draws SVG source onto <paramref name="surface"/> through <paramref name="backend"/>.
    /// </summary>
    /// <param name="svgXml">The SVG document source.</param>
    /// <param name="bounds">The destination rectangle in surface coordinates.</param>
    /// <param name="backend">The caller's display-list replay, e.g. the image backend's raster backend.</param>
    /// <param name="surface">
    /// The surface <paramref name="backend"/> understands. Passed through untouched — this type never
    /// inspects it, which is what keeps the layout assembly free of any surface type.
    /// </param>
    /// <param name="effectiveZoom">See <see cref="BuildDisplayList"/>.</param>
    /// <returns>
    /// <c>true</c> when at least one primitive was drawn; <c>false</c> when the document produced no
    /// display items, in which case <paramref name="backend"/> is never called and the surface is left
    /// exactly as it was found.
    /// </returns>
    public static bool Render(
        string svgXml,
        RectangleF bounds,
        IRasterBackend backend,
        object surface,
        double effectiveZoom = 1.0)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(surface);

        DisplayList list = BuildDisplayList(svgXml, bounds, effectiveZoom);
        if (list.Items.Count == 0)
            return false;

        backend.Render(list, surface);
        return true;
    }
}
