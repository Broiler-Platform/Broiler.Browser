using System;
using Broiler.Layout.Engine;

namespace Broiler.Layout.IR;

/// <summary>
/// Creates a <see cref="ComputedStyle"/> snapshot from a <see cref="CssBoxProperties"/> instance.
/// This factory captures the current lazy-parsed computed values.
/// </summary>
internal static class ComputedStyleBuilder
{
    /// <summary>
    /// CSS Overflow 3 §3.3 / CSS 2.1 §11.1.1: the root element's overflow is
    /// applied to the viewport, and the root element's own used overflow
    /// becomes <c>visible</c>.  A clipping value (<c>hidden</c>/<c>scroll</c>/
    /// <c>auto</c>) on <c>&lt;html&gt;</c> must therefore not clip the element's
    /// own box — which, for a short document (e.g. an empty body with only
    /// absolutely positioned children), is much smaller than the viewport and
    /// would wrongly clip content that belongs on the canvas.  The viewport
    /// (== the render surface) provides the clip instead.
    /// </summary>
    private static string RootOverflowUsedValue(string overflow, string? tagName)
    {
        if (tagName != null
            && tagName.Equals("html", StringComparison.OrdinalIgnoreCase)
            && overflow is "hidden" or "scroll" or "auto")
        {
            return "visible";
        }

        return overflow;
    }

    /// <summary>
    /// Snapshots the computed style of a CssBox, capturing all resolved actual values.
    /// </summary>
    /// <param name="isolationObservable">
    /// Whether an <c>isolation: isolate</c> on this box can make any difference to the picture —
    /// see <c>FragmentTreeBuilder.DocumentHasBlending</c>. When it cannot, the box computes to
    /// <c>isolation: auto</c> so paint does not open a compositing group whose only possible
    /// contribution is the one thing nothing in the document asks for.
    /// </param>
    public static ComputedStyle FromBox(CssBoxProperties box, string? tagName = null, bool isolationObservable = true)
    {
        return new ComputedStyle
        {
            // Phase 2: Element classification
            Kind = box.Kind,

            // HTML tag name for canvas background propagation (CSS 2.1 §14.2)
            TagName = tagName,

            // Box model
            Display = box.Display,
            Position = box.Position,
            Float = box.Float,
            Clear = box.Clear,
            Overflow = RootOverflowUsedValue(box.Overflow, tagName),
            Visibility = box.Visibility,
            Direction = box.Direction,

            // Dimensions (raw)
            Width = box.Width,
            Height = box.Height,
            MaxWidth = box.MaxWidth,

            // Computed dimensions
            ActualWidth = box.ActualWidth,
            ActualHeight = box.ActualHeight,

            // Spacing
            Margin = new BoxEdges(
                box.ActualMarginTop,
                box.ActualMarginRight,
                box.ActualMarginBottom,
                box.ActualMarginLeft),
            Border = new BoxEdges(
                box.ActualBorderTopWidth,
                box.ActualBorderRightWidth,
                box.ActualBorderBottomWidth,
                box.ActualBorderLeftWidth),
            Padding = new BoxEdges(
                box.ActualPaddingTop,
                box.ActualPaddingRight,
                box.ActualPaddingBottom,
                box.ActualPaddingLeft),

            // Corner radii
            ActualCornerNw = box.ActualCornerNw,
            ActualCornerNe = box.ActualCornerNe,
            ActualCornerSe = box.ActualCornerSe,
            ActualCornerSw = box.ActualCornerSw,
            CornerNwRadiusRaw = box.CornerNwRadius,
            CornerNeRadiusRaw = box.CornerNeRadius,
            CornerSeRadiusRaw = box.CornerSeRadius,
            CornerSwRadiusRaw = box.CornerSwRadius,

            // Typography
            FontFamily = box.FontFamily ?? string.Empty,
            FontSize = box.FontSize ?? "medium",
            FontStyle = box.FontStyle,
            FontVariant = box.FontVariant,
            FontWeight = box.FontWeight,
            TextAlign = box.TextAlign,
            TextDecoration = box.TextDecoration,
            TextDecorationStyle = box.TextDecorationStyle,
            ActualTextDecorationColor = box.ActualTextDecorationColor,
            WhiteSpace = box.WhiteSpace,
            WordBreak = box.WordBreak,
            VerticalAlign = box.VerticalAlign,
            ActualLineHeight = box.ActualLineHeight,
            ActualTextIndent = box.ActualTextIndent,
            ActualWordSpacing = box.ActualWordSpacing,

            // Colors
            ActualColor = box.ActualColor,
            ActualBackgroundColor = box.ActualBackgroundColor,
            ActualBackgroundGradient = box.ActualBackgroundGradient,
            ActualBackgroundGradientAngle = box.ActualBackgroundGradientAngle,

            // Border colors
            ActualBorderTopColor = box.ActualBorderTopColor,
            ActualBorderRightColor = box.ActualBorderRightColor,
            ActualBorderBottomColor = box.ActualBorderBottomColor,
            ActualBorderLeftColor = box.ActualBorderLeftColor,

            // Border styles
            BorderTopStyle = box.BorderTopStyle,
            BorderRightStyle = box.BorderRightStyle,
            BorderBottomStyle = box.BorderBottomStyle,
            BorderLeftStyle = box.BorderLeftStyle,

            // Outline (CSS UI §2)
            OutlineStyle = box.OutlineStyle,
            OutlineWidth = box.ActualOutlineWidth,
            OutlineOffset = box.ActualOutlineOffset,
            ActualOutlineColor = box.ActualOutlineColor,

            // Background
            BackgroundImage = box.BackgroundImage,
            BackgroundPosition = box.BackgroundPosition,
            BackgroundRepeat = box.BackgroundRepeat,
            BackgroundAttachment = box.BackgroundAttachment,
            BackgroundOrigin = box.BackgroundOrigin,
            BackgroundSize = box.BackgroundSize,

            // List
            ListStyleType = box.ListStyleType,
            ListStylePosition = box.ListStylePosition,
            ListStyleImage = box.ListStyleImage,
            ListStyle = box.ListStyle,

            // Phase 2: List attributes
            ListStart = box.ListStart,
            ListReversed = box.ListReversed,

            // Phase 2: Image source
            ImageSource = box.ImageSource,

            // Replaced content placement
            ObjectFit = box.ObjectFit,
            ObjectPosition = box.ObjectPosition,

            // Opacity
            Opacity = box.Opacity,

            // Compositing
            MixBlendMode = box.MixBlendMode,
            BackgroundBlendMode = box.BackgroundBlendMode,
            Filter = box.Filter,
            Isolation = isolationObservable ? box.Isolation : "auto",
            BackgroundClip = box.BackgroundClip,
            // CSS 2.1 §11.1.2's `clip` is the same rectangular clip `clip-path: inset()` names, so
            // it is resolved into one here rather than applied a second time in paint.
            ClipPath = ClipRect.EffectiveClipPath(box),
            Contain = box.Contain,
            OverflowClipMargin = box.ActualOverflowClipMargin,
            ContentVisibility = box.ContentVisibility,
            ColorScheme = box.ColorScheme,
            Transform = box.Transform,
            TransformOrigin = box.TransformOrigin,

            // Flex
            FlexDirection = box.FlexDirection,
            FlexGrow = box.FlexGrow,
            FlexShrink = box.FlexShrink,
            FlexBasis = box.FlexBasis,
            FlexWrap = box.FlexWrap,
            JustifyContent = box.JustifyContent,
            AlignItems = box.AlignItems,

            // Table
            BorderSpacing = box.BorderSpacing,
            BorderCollapse = box.BorderCollapse,
            EmptyCells = box.EmptyCells,
            ActualBorderSpacingHorizontal = box.ActualBorderSpacingHorizontal,
            ActualBorderSpacingVertical = box.ActualBorderSpacingVertical,

            // Box shadow
            BoxShadow = box.BoxShadow,

            // Text shadow
            TextShadow = box.TextShadow,

            // Zoom hook for paint-only lengths the paint layer resolves itself (e.g. text-shadow offsets).
            EffectiveZoom = box.EffectiveZoom,

            // Positioning
            Left = box.Left,
            Top = box.Top,

            // Content
            Content = box.Content,

            // Page
            PageBreakInside = box.PageBreakInside,
            Page = box.Page,
        };
    }
}
