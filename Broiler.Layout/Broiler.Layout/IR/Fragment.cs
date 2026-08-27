using System.Drawing;

namespace Broiler.Layout.IR;

/// <summary>
/// Immutable layout result for a single box/fragment.
/// Produced by layout; consumed by paint.
/// </summary>
public sealed class Fragment
{
    /// <summary>Location in absolute coordinates.</summary>
    public PointF Location { get; init; }

    /// <summary>Size of the fragment's border-box.</summary>
    public SizeF Size { get; init; }

    /// <summary>Bounding rectangle (Location + Size).</summary>
    public RectangleF Bounds => new(Location, Size);

    /// <summary>Resolved margin edges.</summary>
    public BoxEdges Margin { get; init; } = BoxEdges.Zero;

    /// <summary>Resolved border-width edges.</summary>
    public BoxEdges Border { get; init; } = BoxEdges.Zero;

    /// <summary>Resolved padding edges.</summary>
    public BoxEdges Padding { get; init; } = BoxEdges.Zero;

    /// <summary>Inline line fragments (for boxes that generate line boxes).</summary>
    public IReadOnlyList<LineFragment>? Lines { get; init; }

    /// <summary>Child fragments.</summary>
    public IReadOnlyList<Fragment> Children { get; init; } = [];

    /// <summary>Back-reference to the computed style (for paint to pick colors etc.).</summary>
    public ComputedStyle Style { get; init; } = new();

    /// <summary>Whether this fragment creates a new stacking context.</summary>
    public bool CreatesStackingContext { get; init; }

    /// <summary>Stack level (z-index or implicit order).</summary>
    public int StackLevel { get; init; }

    /// <summary>
    /// Top-layer order (CSS Position 4 §top-layer) — non-null when this fragment's box is in
    /// the top layer (an open modal <c>&lt;dialog&gt;</c>, an open popover, or a synthesized
    /// <c>::backdrop</c>). Top-layer fragments paint above every ordinary stacking context in a
    /// final root-level pass ordered by this value (a later-added element paints over an earlier
    /// one), instead of participating in normal in-tree stacking. Null for ordinary fragments.
    /// The value is projected from the bridge's <c>data-broiler-top-layer</c> marker; it lets the
    /// paint replace the bridge's approximate very-large-z-index top-layer emulation with a real
    /// top-layer pass (HtmlBridge Phase 5 native dialog/backdrop track).
    /// </summary>
    public int? TopLayerOrder { get; init; }

    /// <summary>Whether this fragment or one of its ancestors establishes a transformed containing block.</summary>
    public bool HasTransformAncestor { get; init; }

    /// <summary>Platform-specific background image handle (Phase 3).</summary>
    public object? BackgroundImageHandle { get; init; }

    /// <summary>Platform-specific image handle for replaced elements like &lt;img&gt; (Phase 3).</summary>
    public object? ImageHandle { get; init; }

    /// <summary>Source rectangle for <see cref="ImageHandle"/> (Phase 3).</summary>
    public RectangleF ImageSourceRect { get; init; }

    /// <summary>
    /// Intrinsic size of <see cref="ImageHandle"/> in CSS px, or <see cref="SizeF.Empty"/> when the
    /// content has none (or there is no content). Paint needs it to resolve <c>object-fit</c>, which
    /// scales the content against its natural size rather than against the box — see
    /// <see cref="ObjectFitPlacement"/>. It is not the used size: the box is already laid out at
    /// that, and the two differ by exactly the ratio <c>object-fit</c> exists to control.
    /// </summary>
    public SizeF ImageIntrinsicSize { get; init; }

    /// <summary>
    /// Intrinsic aspect ratio (width ÷ height) of <see cref="ImageHandle"/>, or 0 when it has none.
    /// Separate from <see cref="ImageIntrinsicSize"/> because content can carry a ratio without a
    /// size — an SVG with only a <c>viewBox</c> — and <c>object-fit: contain</c>/<c>cover</c> are
    /// defined over the ratio, not the size.
    /// </summary>
    public float ImageIntrinsicRatio { get; init; }

    /// <summary>
    /// SVG markup for replaced elements (e.g. <c>&lt;object data="file.svg"&gt;</c>)
    /// that should be rendered via <c>SvgRenderer</c> instead of as a raster image.
    /// </summary>
    public string SvgContent { get; init; }

    /// <summary>
    /// HTML source of a nested browsing context — an <c>&lt;object type="text/html"&gt;</c>,
    /// <c>&lt;iframe&gt;</c>, or <c>&lt;frame&gt;</c> — loaded from the element's
    /// <c>data</c>/<c>src</c> attribute.  The image renderer rasterises this document
    /// at the element's content-box size and composites it over the box.  Null when
    /// the element is not an embedded document or the source could not be loaded.
    /// </summary>
    public string EmbeddedDocumentHtml { get; init; }

    /// <summary>
    /// Base URL of the embedded document (see <see cref="EmbeddedDocumentHtml"/>), used
    /// to resolve that document's own relative resources.
    /// </summary>
    public string EmbeddedDocumentBaseUrl { get; init; }

    /// <summary>
    /// Per-line-box rectangles for inline elements. Used by paint to render
    /// backgrounds and borders for inline boxes that span multiple line boxes.
    /// When non-empty, paint uses these instead of <see cref="Bounds"/>.
    /// </summary>
    public IReadOnlyList<RectangleF>? InlineRects { get; init; }
}

/// <summary>
/// A single line-box within a block container.
/// </summary>
public sealed class LineFragment
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Baseline { get; init; }
    public IReadOnlyList<InlineFragment> Inlines { get; init; } = [];
}

/// <summary>
/// An inline-level fragment within a line box (text run or inline box).
/// </summary>
public sealed class InlineFragment
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public string? Text { get; init; }
    public ComputedStyle Style { get; init; } = new();

    /// <summary>
    /// PROTOTYPE (vertical writing-mode flow): clockwise rotation in degrees
    /// applied to this run's glyphs (Stage 2 — <c>text-orientation: mixed</c>
    /// rotates Latin sideways).  0 for normal upright text.
    /// </summary>
    public float GlyphRotationDeg { get; init; }

    /// <summary>Platform-specific font handle resolved during layout (Phase 3).</summary>
    public object? FontHandle { get; init; }

    /// <summary>Whether this inline is selected (Phase 3).</summary>
    public bool Selected { get; init; }

    /// <summary>Horizontal start offset of partial selection within the inline, or -1 if fully selected (Phase 3).</summary>
    public double SelectedStartOffset { get; init; } = -1;

    /// <summary>Horizontal end offset of partial selection within the inline, or -1 if fully selected (Phase 3).</summary>
    public double SelectedEndOffset { get; init; } = -1;
}
