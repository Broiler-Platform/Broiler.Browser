using Broiler.Graphics;
using System.Drawing;

namespace Broiler.Layout;

/// <summary>
/// Host-injected services that layout needs during box construction and flow:
/// font resolution and text metrics, replaced-element intrinsics, colour
/// parsing, and a relayout/refresh callback.
/// </summary>
/// <remarks>
/// This is the single seam that replaces layout's current direct use of
/// <c>RGraphics</c>, <c>RFont</c>, <c>RImage</c> and <c>IHtmlContainerInt</c>
/// (see <c>Broiler.Layout/docs/roadmap.md</c>). It deliberately
/// exposes a narrow layout-metrics surface rather than the full renderer
/// adapter/container, and uses only BCL geometry primitives so
/// <c>Broiler.Layout</c> stays free of any graphics backend.
/// </remarks>
public interface ILayoutEnvironment
{
    /// <summary>
    /// Resolves (and caches, host-side) a font for the given family, size and style.
    /// </summary>
    ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null);

    /// <summary>Measures the full size of <paramref name="text"/> in <paramref name="font"/>.</summary>
    SizeF MeasureText(ILayoutFont font, string text);

    /// <summary>
    /// Measures how much of <paramref name="text"/> fits within
    /// <paramref name="maxWidth"/>, for line breaking.
    /// </summary>
    /// <param name="charFit">Number of characters that fit.</param>
    /// <param name="charFitWidth">Width consumed by those characters.</param>
    void MeasureText(ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth);

    /// <summary>Width of a single whitespace glyph in <paramref name="font"/>, for word spacing and baselines.</summary>
    double GetWhitespaceWidth(ILayoutFont font);

    /// <summary>
    /// Returns the intrinsic dimensions of a replaced-element image handle
    /// (the host's opaque image object), for replaced-box sizing.
    /// </summary>
    ImageIntrinsics GetImageIntrinsics(object imageHandle);

    /// <summary>Parses a CSS colour string into a concrete colour.</summary>
    BColor ParseColor(string value);

    /// <summary>
    /// Asks the host to refresh; when <paramref name="relayout"/> is <c>true</c>
    /// a full layout pass is requested (e.g. after a late image load).
    /// </summary>
    void RequestRefresh(bool relayout);

    /// <summary>
    /// Dimensions of the viewport (initial containing block), in CSS pixels.
    /// Layout input for viewport units and percentage-height resolution.
    /// </summary>
    SizeF ViewportSize { get; }

    /// <summary>Origin of the root box, used to measure the document's actual extent.</summary>
    PointF RootLocation { get; }

    /// <summary>
    /// The document's actual laid-out size. Layout accumulates the bottom-right
    /// extent here (read-modify-write) so the host knows the scrollable size.
    /// </summary>
    SizeF ActualSize { get; set; }

    /// <summary>
    /// Whether the host prefers geometry to be drawn without anti-aliasing
    /// (affects how layout rounds certain box edges).
    /// </summary>
    bool AvoidGeometryAntialias { get; }

    /// <summary>Page dimensions used for pagination / page-break calculations.</summary>
    SizeF PageSize { get; }

    /// <summary>Top margin of the page box, used for page-break calculations.</summary>
    int MarginTop { get; }

    /// <summary>
    /// Reports a non-fatal layout error to the host (always classified as a
    /// layout-stage error).
    /// </summary>
    void ReportLayoutError(string message, Exception? exception = null);

    /// <summary>Whether the host wants images loaded synchronously (no async loading).</summary>
    bool AvoidAsyncImagesLoading { get; }

    /// <summary>Whether the host wants to avoid late (deferred) image loading.</summary>
    bool AvoidImagesLateLoading { get; }

    /// <summary>
    /// Creates a host image loader whose completion callback receives the loaded
    /// image handle (or <c>null</c> on failure), the source rectangle, and whether
    /// the load completed asynchronously.
    /// </summary>
    ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete);

    /// <summary>
    /// Formats a list-item marker number for the given <c>list-style-type</c>
    /// (e.g. decimal/roman/alpha/armenian/…). Host-side because it carries the
    /// non-Latin numbering tables.
    /// </summary>
    string FormatListMarker(int number, string style);
}
