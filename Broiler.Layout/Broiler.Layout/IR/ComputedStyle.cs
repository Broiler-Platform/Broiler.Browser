using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// Resolved, typed CSS property values for a single element.
/// Produced by the style phase; consumed by layout and paint. Immutable once created.
/// </summary>
public sealed class ComputedStyle
{
    /// <summary>Semantic role of the element, derived from tag name during style resolution.</summary>
    public BoxKind Kind { get; init; } = BoxKind.Anonymous;

    /// <summary>
    /// Original HTML tag name (e.g. "html", "body", "div"), or <c>null</c>
    /// for anonymous boxes.  Used by canvas background propagation logic
    /// (CSS 2.1 §14.2) to identify the root and body elements.
    /// </summary>
    public string? TagName { get; init; }

    // --- Box model (raw CSS strings, matching CssBoxProperties conventions) ---

    public string Display { get; init; } = "inline";
    public string Position { get; init; } = "static";
    public string Float { get; init; } = "none";
    public string Clear { get; init; } = "none";
    public string Overflow { get; init; } = "visible";
    public string Visibility { get; init; } = "visible";
    public string Direction { get; init; } = "ltr";

    // --- Dimensions (raw CSS strings) ---

    public string Width { get; init; } = "auto";
    public string Height { get; init; } = "auto";
    public string MaxWidth { get; init; } = "none";

    // --- Computed dimensions (resolved device pixels) ---

    public double ActualWidth { get; init; }
    public double ActualHeight { get; init; }

    // --- Spacing (resolved device pixels) ---

    public BoxEdges Margin { get; init; } = BoxEdges.Zero;
    public BoxEdges Border { get; init; } = BoxEdges.Zero;
    public BoxEdges Padding { get; init; } = BoxEdges.Zero;

    // --- Corner radii ---

    public double ActualCornerNw { get; init; }
    public double ActualCornerNe { get; init; }
    public double ActualCornerSe { get; init; }
    public double ActualCornerSw { get; init; }
    public string CornerNwRadiusRaw { get; init; } = "0";
    public string CornerNeRadiusRaw { get; init; } = "0";
    public string CornerSeRadiusRaw { get; init; } = "0";
    public string CornerSwRadiusRaw { get; init; } = "0";

    // --- Typography ---

    public string FontFamily { get; init; } = string.Empty;
    public string FontSize { get; init; } = "medium";
    public string FontStyle { get; init; } = "normal";
    public string FontVariant { get; init; } = "normal";
    public string FontWeight { get; init; } = "normal";
    public string TextAlign { get; init; } = string.Empty;
    public string TextDecoration { get; init; } = string.Empty;
    public string TextDecorationStyle { get; init; } = "solid";
    public BColor ActualTextDecorationColor { get; init; }
    public string WhiteSpace { get; init; } = "normal";
    public string WordBreak { get; init; } = "normal";
    public string VerticalAlign { get; init; } = "baseline";
    public double ActualLineHeight { get; init; }
    public double ActualTextIndent { get; init; }
    public double ActualWordSpacing { get; init; }

    // --- Colors ---

    public BColor ActualColor { get; init; }
    public BColor ActualBackgroundColor { get; init; }
    public BColor ActualBackgroundGradient { get; init; }
    public double ActualBackgroundGradientAngle { get; init; }

    // --- Border colors ---

    public BColor ActualBorderTopColor { get; init; }
    public BColor ActualBorderRightColor { get; init; }
    public BColor ActualBorderBottomColor { get; init; }
    public BColor ActualBorderLeftColor { get; init; }

    // --- Border styles ---

    public string BorderTopStyle { get; init; } = "none";
    public string BorderRightStyle { get; init; } = "none";
    public string BorderBottomStyle { get; init; } = "none";
    public string BorderLeftStyle { get; init; } = "none";

    // --- Outline (CSS UI §2) — painted outside the border edge, no layout effect ---

    public string OutlineStyle { get; init; } = "none";
    public double OutlineWidth { get; init; }
    public double OutlineOffset { get; init; }
    public BColor ActualOutlineColor { get; init; }

    // --- Background ---

    public string BackgroundImage { get; init; } = "none";
    public string BackgroundPosition { get; init; } = "0% 0%";
    public string BackgroundRepeat { get; init; } = "repeat";
    public string BackgroundAttachment { get; init; } = "scroll";
    public string BackgroundOrigin { get; init; } = "padding-box";
    public string BackgroundSize { get; init; } = "auto";

    // --- List ---

    public string ListStyleType { get; init; } = "disc";
    public string ListStylePosition { get; init; } = "outside";
    public string ListStyleImage { get; init; } = string.Empty;
    public string ListStyle { get; init; } = string.Empty;

    // --- List attributes ---

    /// <summary>The <c>start</c> attribute of the parent <c>&lt;ol&gt;</c>, or null if not specified.</summary>
    public int? ListStart { get; init; }

    /// <summary>Whether the parent <c>&lt;ol&gt;</c> has the <c>reversed</c> attribute.</summary>
    public bool ListReversed { get; init; }

    // --- Image source ---

    /// <summary>The resolved <c>src</c> attribute for image elements, or null if not applicable.</summary>
    public string? ImageSource { get; init; }

    // --- Replaced content placement ---

    /// <summary>CSS Images 3 §5.5 <c>object-fit</c>. See <see cref="ObjectFitPlacement"/>.</summary>
    public string ObjectFit { get; init; } = "fill";

    /// <summary>CSS Images 3 §5.6 <c>object-position</c>. See <see cref="ObjectFitPlacement"/>.</summary>
    public string ObjectPosition { get; init; } = "50% 50%";

    // --- Opacity ---

    public string Opacity { get; init; } = "1";

    // --- Compositing ---

    public string MixBlendMode { get; init; } = "normal";
    public string BackgroundBlendMode { get; init; } = "normal";
    public string Filter { get; init; } = "none";
    public string Isolation { get; init; } = "auto";
    public string BackgroundClip { get; init; } = "border-box";
    public string ClipPath { get; init; } = "none";

    /// <summary>
    /// CSS Containment Module Level 2: the <c>contain</c> property.
    /// Used for background propagation suppression when value includes <c>paint</c>.
    /// </summary>
    public string Contain { get; init; } = "none";

    /// <summary>
    /// CSS Overflow §4: used <c>overflow-clip-margin</c> in px (≥0). Expands the
    /// overflow clip edge outward for boxes that clip without scrolling
    /// (<c>overflow: clip</c> or paint containment); ignored for scroll containers.
    /// </summary>
    public double OverflowClipMargin { get; init; }

    /// <summary>
    /// CSS Color Adjust Module Level 1: the <c>color-scheme</c> property.
    /// Used by canvas background painting (CSS Color Adjust §2.3): when the
    /// root's used color scheme is <c>dark</c>, the canvas is painted the UA
    /// dark backdrop colour instead of white.
    /// </summary>
    public string ColorScheme { get; init; } = "normal";

    /// <summary>
    /// CSS Containment Module Level 2: the <c>content-visibility</c> property.
    /// <c>hidden</c> makes the fragment builder skip the element's contents so
    /// they are not painted, while the element's own box still renders.
    /// </summary>
    public string ContentVisibility { get; init; } = "visible";
    public string Transform { get; init; } = "none";

    /// <summary>CSS Transforms 1 §8: the point <see cref="Transform"/> is applied about.</summary>
    public string TransformOrigin { get; init; } = "50% 50%";

    // --- Flex ---

    public string FlexDirection { get; init; } = "row";
    public string FlexGrow { get; init; } = "0";
    public string FlexShrink { get; init; } = "1";
    public string FlexBasis { get; init; } = "auto";
    public string FlexWrap { get; init; } = "nowrap";
    public string JustifyContent { get; init; } = "normal";
    public string AlignItems { get; init; } = "stretch";

    // --- Table ---

    public string BorderSpacing { get; init; } = "0";
    public string BorderCollapse { get; init; } = "separate";
    public string EmptyCells { get; init; } = "show";
    public double ActualBorderSpacingHorizontal { get; init; }
    public double ActualBorderSpacingVertical { get; init; }

    // --- Box shadow ---

    public string BoxShadow { get; init; } = "none";

    // --- Text shadow ---

    public string TextShadow { get; init; } = "none";

    // --- Zoom ---

    /// <summary>
    /// The box's compounded CSS <c>zoom</c> factor (<see cref="Engine.CssBoxProperties.EffectiveZoom"/>);
    /// <c>1.0</c> when the native-zoom engine is off. The layout read model already emits fully zoomed
    /// geometry, so this is the hook the paint layer uses to scale the few <em>paint-only</em> used lengths
    /// it resolves from raw strings itself (e.g. <c>text-shadow</c> offsets) rather than from the box tree.
    /// </summary>
    public double EffectiveZoom { get; init; } = 1.0;

    // --- Positioning ---

    public string Left { get; init; } = "auto";
    public string Top { get; init; } = "auto";

    // --- Content ---

    public string Content { get; init; } = "normal";

    // --- Page ---

    public string PageBreakInside { get; init; } = "auto";

    /// <summary>
    /// CSS Paged Media 3 §3.4 <c>page</c>: the page name this box declares, or <c>auto</c>. Carried
    /// into the IR so a paged renderer can tell which <c>@page</c> rule the content of each page
    /// takes its box from — the name is not otherwise recoverable from a laid-out fragment.
    /// </summary>
    public string Page { get; init; } = "auto";
}
