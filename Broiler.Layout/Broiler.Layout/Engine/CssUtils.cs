using Broiler.CSS;
using System.Text.RegularExpressions;


namespace Broiler.Layout.Engine;

internal static partial class CssUtils
{
    private static readonly Regex LengthAttrFunctionPattern = LengthAttrRegex();

    public static double WhiteSpace(ILayoutEnvironment g, CssBoxProperties box)
    {
        double w = g.GetWhitespaceWidth(box.ActualFont);

        if (!(string.IsNullOrEmpty(box.WordSpacing) || box.WordSpacing == CssConstants.Normal))
            // word-spacing is a used length: scale by the box's zoom (inert while NativeZoom is off).
            w += box.ApplyZoomToLength(box.WordSpacing, CssLengthParser.ParseLength(box.WordSpacing, 0, box.GetEmHeight(), true));

        return w;
    }

    public static string GetPropertyValue(CssBox cssBox, string propName)
    {
        return propName switch
        {
            "border-bottom-width" => cssBox.BorderBottomWidth,
            "border-left-width" => cssBox.BorderLeftWidth,
            "border-right-width" => cssBox.BorderRightWidth,
            "border-top-width" => cssBox.BorderTopWidth,
            "border-bottom-style" => cssBox.BorderBottomStyle,
            "border-left-style" => cssBox.BorderLeftStyle,
            "border-right-style" => cssBox.BorderRightStyle,
            "border-top-style" => cssBox.BorderTopStyle,
            "border-bottom-color" => cssBox.BorderBottomColor,
            "border-left-color" => cssBox.BorderLeftColor,
            "border-right-color" => cssBox.BorderRightColor,
            "border-top-color" => cssBox.BorderTopColor,
            "border-spacing" => cssBox.BorderSpacing,
            "border-collapse" => cssBox.BorderCollapse,
            "corner-radius" => cssBox.CornerRadius,
            "border-radius" => cssBox.CornerRadius,
            "opacity" => cssBox.Opacity,
            "box-shadow" => cssBox.BoxShadow,
            "text-shadow" => cssBox.TextShadow,
            "flex-direction" => cssBox.FlexDirection,
            "flex-grow" => cssBox.FlexGrow,
            "flex-shrink" => cssBox.FlexShrink,
            "flex-basis" => cssBox.FlexBasis,
            "flex-wrap" => cssBox.FlexWrap,
            "flex-flow" => (cssBox.FlexDirection + " " + cssBox.FlexWrap).Trim(),
            "flex" => string.Join(' ', cssBox.FlexGrow, cssBox.FlexShrink, cssBox.FlexBasis),
            "order" => cssBox.Order,
            "justify-content" => cssBox.JustifyContent,
            "justify-items" => cssBox.JustifyItems,
            "align-items" => cssBox.AlignItems,
            "corner-nw-radius" => cssBox.CornerNwRadius,
            "corner-ne-radius" => cssBox.CornerNeRadius,
            "corner-se-radius" => cssBox.CornerSeRadius,
            "corner-sw-radius" => cssBox.CornerSwRadius,
            "margin-bottom" => cssBox.MarginBottom,
            "margin-left" => cssBox.MarginLeft,
            "margin-right" => cssBox.MarginRight,
            "margin-top" => cssBox.MarginTop,
            "margin-trim" => cssBox.MarginTrim,
            "padding-bottom" => cssBox.PaddingBottom,
            "padding-left" => cssBox.PaddingLeft,
            "padding-right" => cssBox.PaddingRight,
            "padding-top" => cssBox.PaddingTop,
            "page-break-inside" => cssBox.PageBreakInside,
            "zoom" => cssBox.Zoom,
            "left" => cssBox.Left,
            "top" => cssBox.Top,
            "width" => cssBox.Width,
            "inline-size" => cssBox.InlineSize,
            "max-width" => cssBox.MaxWidth,
            "min-width" => cssBox.MinWidth,
            "height" => cssBox.Height,
            "block-size" => cssBox.BlockSize,
            "min-block-size" => cssBox.MinBlockSize,
            "max-block-size" => cssBox.MaxBlockSize,
            "min-inline-size" => cssBox.MinInlineSize,
            "max-inline-size" => cssBox.MaxInlineSize,
            "max-height" => cssBox.MaxHeight,
            "min-height" => cssBox.MinHeight,
            "background-color" => cssBox.BackgroundColor,
            "background-image" => cssBox.BackgroundImage,
            "background-position" => cssBox.BackgroundPosition,
            "background-repeat" => cssBox.BackgroundRepeat,
            "background-attachment" => cssBox.BackgroundAttachment,
            "background-origin" => cssBox.BackgroundOrigin,
            "background-size" => cssBox.BackgroundSize,
            "background-gradient" => cssBox.BackgroundGradient,
            "background-gradient-angle" => cssBox.BackgroundGradientAngle,
            "object-fit" => cssBox.ObjectFit,
            "object-position" => cssBox.ObjectPosition,
            "content" => cssBox.Content,
            "color" => cssBox.Color,
            "display" => cssBox.Display,
            "direction" => cssBox.Direction,
            "appearance" => cssBox.Appearance,
            "-webkit-appearance" => cssBox.Appearance,
            "empty-cells" => cssBox.EmptyCells,
            "caption-side" => cssBox.CaptionSide,
            "float" => cssBox.Float,
            "clear" => cssBox.Clear,
            "position" => cssBox.Position,
            "anchor-name" => cssBox.AnchorName,
            "position-anchor" => cssBox.PositionAnchor,
            "position-area" => cssBox.PositionArea,
            "position-try" => cssBox.PositionTry,
            "position-try-fallbacks" => cssBox.PositionTryFallbacks,
            "position-visibility" => cssBox.PositionVisibility,
            "line-height" => cssBox.LineHeight,
            "vertical-align" => cssBox.VerticalAlign,
            "text-indent" => cssBox.TextIndent,
            "text-align" => cssBox.TextAlign,
            "text-align-last" => cssBox.TextAlignLast,
            "text-decoration" => cssBox.TextDecoration,
            "text-decoration-line" => cssBox.TextDecoration,
            "text-decoration-style" => cssBox.TextDecorationStyle,
            "text-decoration-color" => cssBox.TextDecorationColor,
            "white-space" => cssBox.WhiteSpace,
            "break-before" => cssBox.BreakBefore,
            "page-break-before" => cssBox.BreakBefore,
            "break-after" => cssBox.BreakAfter,
            "page-break-after" => cssBox.BreakAfter,
            "page" => cssBox.Page,
            "line-clamp" => cssBox.LineClamp,
            "-webkit-line-clamp" => cssBox.WebkitLineClamp,
            "max-lines" => cssBox.MaxLines,
            "block-ellipsis" => cssBox.BlockEllipsis,
            "-webkit-box-orient" => cssBox.WebkitBoxOrient,
            "word-break" => cssBox.WordBreak,
            "line-break" => cssBox.LineBreak,
            "visibility" => cssBox.Visibility,
            "image-animation" => cssBox.ImageAnimation,
            "word-spacing" => cssBox.WordSpacing,
            "font-family" => cssBox.FontFamily,
            "font-feature-settings" => cssBox.FontFeatureSettings,
            "font-variant-alternates" => cssBox.FontVariantAlternates,
            "font-size" => cssBox.FontSize,
            "font-style" => cssBox.FontStyle,
            "font-variant" => cssBox.FontVariant,
            "font-weight" => cssBox.FontWeight,
            "list-style" => cssBox.ListStyle,
            "list-style-position" => cssBox.ListStylePosition,
            "list-style-image" => cssBox.ListStyleImage,
            "list-style-type" => cssBox.ListStyleType,
            "overflow" => cssBox.Overflow,
            "box-sizing" => cssBox.BoxSizing,
            "clip-path" => cssBox.ClipPath,
            "clip" => cssBox.Clip,
            "transform" => cssBox.Transform,
            "transform-origin" => cssBox.TransformOrigin,
            "will-change" => cssBox.WillChange,
            "align-content" => cssBox.AlignContent,
            "justify-self" => cssBox.JustifySelf,
            "align-self" => cssBox.AlignSelf,
            "unicode-bidi" => cssBox.UnicodeBidi,
            "writing-mode" => cssBox.WritingMode,
            "column-count" => cssBox.ColumnCount,
            "column-width" => cssBox.ColumnWidth,
            "column-fill" => cssBox.ColumnFill,
            "row-gap" or "grid-row-gap" => cssBox.RowGap,
            "column-gap" or "grid-column-gap" => cssBox.ColumnGap,
            "gap" or "grid-gap" => cssBox.RowGap == cssBox.ColumnGap ? cssBox.RowGap : $"{cssBox.RowGap} {cssBox.ColumnGap}",
            "break-inside" => cssBox.BreakInside,
            "grid-row" => cssBox.GridRow,
            "grid-column" => cssBox.GridColumn,
            "grid-template-columns" => cssBox.GridTemplateColumns,
            "grid-template-rows" => cssBox.GridTemplateRows,
            "grid-auto-flow" => cssBox.GridAutoFlow,
            "grid-auto-rows" => cssBox.GridAutoRows,
            "grid-auto-columns" => cssBox.GridAutoColumns,
            "contain" => cssBox.Contain,
            "contain-intrinsic-size" => JoinContainIntrinsicSize(cssBox),
            "contain-intrinsic-width" => cssBox.ContainIntrinsicWidth,
            "contain-intrinsic-height" => cssBox.ContainIntrinsicHeight,
            "contain-intrinsic-inline-size" => cssBox.ContainIntrinsicInlineSize,
            "contain-intrinsic-block-size" => cssBox.ContainIntrinsicBlockSize,
            "overflow-clip-margin" => cssBox.OverflowClipMargin,
            "content-visibility" => cssBox.ContentVisibility,
            "color-scheme" => cssBox.ColorScheme,
            _ => null,
        };
    }

    /// <summary>
    /// CSS Flexbox §5.1 <c>flex-flow</c>: a direction, a wrap, or both, in either order. Expanded
    /// here because nothing upstream expands it, and a container whose direction never arrives is
    /// laid out as a row — the wrong axis for everything in it.
    /// </summary>
    private static void SetFlexFlowShorthand(CssBox cssBox, string value)
    {
        foreach (var token in value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "row" or "row-reverse" or "column" or "column-reverse")
                cssBox.FlexDirection = token;
            else if (token is "nowrap" or "wrap" or "wrap-reverse")
                cssBox.FlexWrap = token;
        }
    }

    /// <summary>
    /// CSS Flexbox §7.1 <c>flex</c>: up to a grow factor, a shrink factor and a basis, in that
    /// order, with the unstated ones taking the shorthand's own defaults rather than the
    /// longhands' — <c>flex: 1</c> is <c>1 1 0%</c>, not <c>1 0 auto</c>.
    /// </summary>
    private static void SetFlexShorthand(CssBox cssBox, string value)
    {
        var v = value.Trim();

        if (v.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            cssBox.FlexGrow = "0";
            cssBox.FlexShrink = "0";
            cssBox.FlexBasis = CssConstants.Auto;
            return;
        }

        if (v.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase)
            || v.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            cssBox.FlexGrow = v.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase) ? "1" : "0";
            cssBox.FlexShrink = "1";
            cssBox.FlexBasis = CssConstants.Auto;
            return;
        }

        string? grow = null, shrink = null, basis = null;
        foreach (var token in v.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            bool isNumber = double.TryParse(
                token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _);

            if (isNumber && grow is null)
                grow = token;
            else if (isNumber && shrink is null)
                shrink = token;
            else
                basis ??= token;
        }

        if (grow is null)
            return;

        // CSS Flexbox §7.1: `<'flex-basis'>` is `content | <'width'>`, and a `<'width'>` is never a
        // bare non-zero number. A unitless zero *is* one — the spec's own note says a unitless zero
        // already preceded by two flex factors reads as the basis rather than a third factor — so
        // `flex: 0 0 0` is valid where `flex: 0 0 4` is not. An invalid component invalidates the
        // whole shorthand, which then leaves `flex` at the value it already had.
        //
        // Taking `4` as a basis instead made every item in the four WPT
        // css-flexbox/flexbox_flex-*-unitless-basis tests size to its content rather than keeping
        // the `width: 5em` the dropped declaration leaves in force.
        if (basis is not null && IsUnitlessNonZeroNumber(basis))
            return;

        cssBox.FlexGrow = grow;
        cssBox.FlexShrink = shrink ?? "1";
        cssBox.FlexBasis = basis ?? "0%";
    }

    /// <summary>
    /// Whether <paramref name="token"/> is a plain number with no unit and a non-zero value — the
    /// one shape that is a valid flex <em>factor</em> but never a valid <c>flex-basis</c>.
    /// </summary>
    private static bool IsUnitlessNonZeroNumber(string token) =>
        double.TryParse(
            token,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed)
        && parsed != 0;

    public static void SetPropertyValue(CssBox cssBox, string propName, string value)
    {
        value = ResolveLengthAttrFunctions(cssBox, value);

        if (propName.StartsWith("--", StringComparison.Ordinal))
        {
            cssBox.SetCustomProperty(propName, value);
            return;
        }

        switch (propName)
        {
            case "border-bottom-width":
                cssBox.BorderBottomWidth = value;
                break;
            case "border-left-width":
                cssBox.BorderLeftWidth = value;
                break;
            case "border-right-width":
                cssBox.BorderRightWidth = value;
                break;
            case "border-top-width":
                cssBox.BorderTopWidth = value;
                break;
            case "border-bottom-style":
                cssBox.BorderBottomStyle = value;
                break;
            case "border-left-style":
                cssBox.BorderLeftStyle = value;
                break;
            case "border-right-style":
                cssBox.BorderRightStyle = value;
                break;
            case "border-top-style":
                cssBox.BorderTopStyle = value;
                break;
            case "border-bottom-color":
                cssBox.BorderBottomColor = value;
                break;
            case "border-left-color":
                cssBox.BorderLeftColor = value;
                break;
            case "border-right-color":
                cssBox.BorderRightColor = value;
                break;
            case "border-top-color":
                cssBox.BorderTopColor = value;
                break;
            case "border-spacing":
                cssBox.BorderSpacing = value;
                break;
            case "outline-width":
                cssBox.OutlineWidth = value;
                break;
            case "outline-style":
                cssBox.OutlineStyle = value;
                break;
            case "outline-color":
                cssBox.OutlineColor = value;
                break;
            case "outline-offset":
                cssBox.OutlineOffset = value;
                break;
            case "border-collapse":
                cssBox.BorderCollapse = value;
                break;
            case "corner-radius":
                cssBox.CornerRadius = value;
                break;
            case "border-radius":
                cssBox.CornerRadius = value;
                break;
            case "opacity":
                cssBox.Opacity = value;
                break;
            case "mix-blend-mode":
                cssBox.MixBlendMode = value;
                break;
            case "background-blend-mode":
                cssBox.BackgroundBlendMode = value;
                break;
            case "filter":
                cssBox.Filter = value;
                break;
            case "isolation":
                cssBox.Isolation = value;
                break;
            case "box-sizing":
                cssBox.BoxSizing = value;
                break;
            case "background-clip":
                cssBox.BackgroundClip = value;
                break;
            case "clip-path":
                cssBox.ClipPath = value;
                break;
            case "clip":
                cssBox.Clip = value;
                break;
            case "transform":
                cssBox.Transform = value;
                break;
            case "transform-origin":
                cssBox.TransformOrigin = value;
                break;
            case "will-change":
                cssBox.WillChange = value;
                break;
            case "box-shadow":
                cssBox.BoxShadow = value;
                break;
            case "text-shadow":
                cssBox.TextShadow = value;
                break;
            case "text-fill-color":
            case "-webkit-text-fill-color":
                cssBox.Color = value;
                break;
            case "flex-direction":
                cssBox.FlexDirection = value;
                break;
            case "flex-grow":
                cssBox.FlexGrow = value;
                break;
            case "flex-shrink":
                cssBox.FlexShrink = value;
                break;
            case "flex-basis":
                cssBox.FlexBasis = value;
                break;
            case "flex-wrap":
                cssBox.FlexWrap = value;
                break;
            case "flex-flow":
                SetFlexFlowShorthand(cssBox, value);
                break;
            case "flex":
                SetFlexShorthand(cssBox, value);
                break;
            case "order":
                cssBox.Order = value;
                break;
            case "justify-content":
                cssBox.JustifyContent = value;
                break;
            case "justify-items":
                cssBox.JustifyItems = value;
                break;
            case "align-items":
                cssBox.AlignItems = value;
                break;
            case "align-content":
                cssBox.AlignContent = value;
                break;
            case "justify-self":
                cssBox.JustifySelf = value;
                break;
            case "align-self":
                cssBox.AlignSelf = value;
                break;
            case "unicode-bidi":
                cssBox.UnicodeBidi = value;
                break;
            case "writing-mode":
                cssBox.WritingMode = value;
                break;
            case "column-count":
                cssBox.ColumnCount = value;
                break;
            case "column-width":
                cssBox.ColumnWidth = value;
                break;
            case "column-fill":
                cssBox.ColumnFill = value;
                break;
            case "row-gap":
            // Legacy CSS Grid Level 1 aliases (still supported by browsers).
            case "grid-row-gap":
                cssBox.RowGap = value;
                break;
            case "column-gap":
            case "grid-column-gap":
                cssBox.ColumnGap = value;
                break;
            case "gap":
            case "grid-gap":
                {
                    var parts = value.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1)
                    {
                        cssBox.RowGap = parts[0];
                        cssBox.ColumnGap = parts[0];
                    }
                    else if (parts.Length >= 2)
                    {
                        cssBox.RowGap = parts[0];
                        cssBox.ColumnGap = parts[1];
                    }
                }
                break;
            case "break-inside":
                cssBox.BreakInside = value;
                break;
            case "grid-row":
                cssBox.GridRow = value;
                break;
            case "grid-column":
                cssBox.GridColumn = value;
                break;
            case "grid-area":
                ApplyGridAreaShorthand(cssBox, value);
                break;
            case "grid-row-start":
                SetGridLineSide(cssBox, isRow: true, isStart: true, value);
                break;
            case "grid-row-end":
                SetGridLineSide(cssBox, isRow: true, isStart: false, value);
                break;
            case "grid-column-start":
                SetGridLineSide(cssBox, isRow: false, isStart: true, value);
                break;
            case "grid-column-end":
                SetGridLineSide(cssBox, isRow: false, isStart: false, value);
                break;
            case "grid-template-columns":
                cssBox.GridTemplateColumns = value;
                break;
            case "grid-template-rows":
                cssBox.GridTemplateRows = value;
                break;
            case "grid-auto-flow":
                cssBox.GridAutoFlow = value;
                break;
            case "grid-auto-rows":
                cssBox.GridAutoRows = value;
                break;
            case "grid-auto-columns":
                cssBox.GridAutoColumns = value;
                break;
            case "grid":
                ApplyGridShorthand(cssBox, value, resetAutoTracks: true);
                break;
            case "grid-template":
                ApplyGridShorthand(cssBox, value, resetAutoTracks: false);
                break;
            case "contain":
                cssBox.Contain = value;
                break;
            case "contain-intrinsic-size":
                ApplyContainIntrinsicSizeShorthand(cssBox, value);
                break;
            case "contain-intrinsic-width":
                cssBox.ContainIntrinsicWidth = value;
                break;
            case "contain-intrinsic-height":
                cssBox.ContainIntrinsicHeight = value;
                break;
            case "contain-intrinsic-inline-size":
                cssBox.ContainIntrinsicInlineSize = value;
                break;
            case "contain-intrinsic-block-size":
                cssBox.ContainIntrinsicBlockSize = value;
                break;
            case "overflow-clip-margin":
                cssBox.OverflowClipMargin = value;
                break;
            case "content-visibility":
                cssBox.ContentVisibility = value;
                break;
            case "color-scheme":
                cssBox.ColorScheme = value;
                break;
            case "columns":
                // CSS Multi-column §3: 'columns' is a shorthand for
                // 'column-width' and 'column-count'.  A bare integer
                // value sets column-count; a length sets column-width.
                var colParts = value.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in colParts)
                {
                    if (part == "auto")
                        continue;
                    if (int.TryParse(part, out _))
                        cssBox.ColumnCount = part;
                    else
                        cssBox.ColumnWidth = part;
                }
                break;
            case "border-inline":
                ApplyLogicalBorderShorthand(cssBox, value, inlineAxis: true);
                break;
            case "border-block":
                ApplyLogicalBorderShorthand(cssBox, value, inlineAxis: false);
                break;
            case "border-inline-start-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: true, component: "color", value);
                break;
            case "border-inline-end-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: false, component: "color", value);
                break;
            case "border-block-start-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: true, component: "color", value);
                break;
            case "border-block-end-color":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: false, component: "color", value);
                break;
            case "border-inline-start-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: true, component: "style", value);
                break;
            case "border-inline-end-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: false, component: "style", value);
                break;
            case "border-block-start-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: true, component: "style", value);
                break;
            case "border-block-end-style":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: false, component: "style", value);
                break;
            case "border-inline-start-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: true, component: "width", value);
                break;
            case "border-inline-end-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: true, startSide: false, component: "width", value);
                break;
            case "border-block-start-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: true, component: "width", value);
                break;
            case "border-block-end-width":
                SetLogicalBorderComponent(cssBox, inlineAxis: false, startSide: false, component: "width", value);
                break;
            // The per-corner longhands had no arm at all, so `border-top-left-radius: 75px 50px`
            // was dropped and the corner did not round. NW/NE/SE/SW are top-left/top-right/
            // bottom-right/bottom-left.
            case "border-top-left-radius":
            case "corner-nw-radius":
                cssBox.CornerNwRadius = value;
                break;
            case "border-top-right-radius":
                cssBox.CornerNeRadius = value;
                break;
            case "border-bottom-right-radius":
                cssBox.CornerSeRadius = value;
                break;
            case "border-bottom-left-radius":
                cssBox.CornerSwRadius = value;
                break;
            case "corner-ne-radius":
                cssBox.CornerNeRadius = value;
                break;
            case "corner-se-radius":
                cssBox.CornerSeRadius = value;
                break;
            case "corner-sw-radius":
                cssBox.CornerSwRadius = value;
                break;
            case "margin-bottom":
                cssBox.MarginBottom = value;
                break;
            case "margin-left":
                cssBox.MarginLeft = value;
                break;
            case "margin-right":
                cssBox.MarginRight = value;
                break;
            case "margin-top":
                cssBox.MarginTop = value;
                break;
            case "margin-trim":
                cssBox.MarginTrim = value;
                break;
            case "padding-bottom":
                cssBox.PaddingBottom = value;
                break;
            case "padding-left":
                cssBox.PaddingLeft = value;
                break;
            case "padding-right":
                cssBox.PaddingRight = value;
                break;
            case "padding-top":
                cssBox.PaddingTop = value;
                break;
            case "page-break-inside":
                cssBox.PageBreakInside = value;
                break;
            case "zoom":
                cssBox.Zoom = value;
                break;
            case "left":
                cssBox.Left = value;
                break;
            case "top":
                cssBox.Top = value;
                break;
            case "right":
                cssBox.Right = value;
                break;
            case "bottom":
                cssBox.Bottom = value;
                break;
            case "width":
                cssBox.Width = value;
                break;
            case "inline-size":
                cssBox.InlineSize = value;
                break;
            case "max-width":
                cssBox.MaxWidth = value;
                break;
            case "min-width":
                cssBox.MinWidth = value;
                cssBox.IsMinWidthSpecified = true;
                break;
            case "height":
                cssBox.Height = value;
                break;
            case "block-size":
                cssBox.BlockSize = value;
                break;
            case "min-block-size":
                cssBox.MinBlockSize = value;
                break;
            case "max-block-size":
                cssBox.MaxBlockSize = value;
                break;
            case "min-inline-size":
                cssBox.MinInlineSize = value;
                break;
            case "max-inline-size":
                cssBox.MaxInlineSize = value;
                break;
            case "max-height":
                cssBox.MaxHeight = value;
                break;
            case "min-height":
                cssBox.MinHeight = value;
                cssBox.IsMinHeightSpecified = true;
                break;
            case "aspect-ratio":
                cssBox.AspectRatio = value;
                break;
            case "background-color":
                cssBox.BackgroundColor = value;
                break;
            case "background-image":
                // CSS Images 4 §5.4: `image-set()` is resolved to the one image it selects before
                // anything downstream reads the value. The background loader looks for `url(` and
                // the paint walker looks for `gradient(` to tell a fetched layer from a drawn one,
                // so an unresolved `image-set(...)` was neither — it loaded nothing and drew
                // nothing, whichever kind of image it held. Unwrapping it here leaves both of them
                // reading a value they already understand.
                //
                // An invalid function (a negative resolution) leaves the property alone rather than
                // overwriting it, which is what a dropped declaration does: the value already set
                // is the one that was cascaded under it.
                if (IR.CssImageSet.TryResolveLayers(value, out var resolvedImage))
                    cssBox.BackgroundImage = resolvedImage;
                break;
            case "background-position":
                cssBox.BackgroundPosition = value;
                break;
            case "object-fit":
                cssBox.ObjectFit = value;
                break;
            case "object-position":
                cssBox.ObjectPosition = value;
                break;
            case "background-repeat":
                cssBox.BackgroundRepeat = value;
                break;
            case "background-attachment":
                cssBox.BackgroundAttachment = value;
                break;
            case "background-origin":
                cssBox.BackgroundOrigin = value;
                break;
            case "background-size":
                cssBox.BackgroundSize = value;
                break;
            case "background-gradient":
                cssBox.BackgroundGradient = value;
                break;
            case "background-gradient-angle":
                cssBox.BackgroundGradientAngle = value;
                break;
            case "color":
                cssBox.Color = value;
                break;
            case "content":
                cssBox.Content = value;
                break;
            case "display":
                // Remember a legacy `-webkit-box` before it is mapped away: the
                // mapping depends on -webkit-box-orient, and the two declarations
                // arrive in author order, so `display` is frequently applied first.
                cssBox.LegacyWebkitBoxDisplay =
                    value.Trim().Equals("-webkit-box", StringComparison.OrdinalIgnoreCase) ? "-webkit-box"
                    : value.Trim().Equals("-webkit-inline-box", StringComparison.OrdinalIgnoreCase) ? "-webkit-inline-box"
                    : null;
                cssBox.Display = NormalizeDisplayValue(value, cssBox);
                break;
            case "direction":
                cssBox.Direction = value;
                break;
            case "appearance":
            case "-webkit-appearance":
                cssBox.Appearance = value;
                break;
            case "empty-cells":
                cssBox.EmptyCells = value;
                break;
            case "caption-side":
                cssBox.CaptionSide = value;
                break;
            case "float":
                cssBox.Float = value;
                break;
            case "clear":
                cssBox.Clear = value;
                break;
            case "position":
                cssBox.Position = value;
                break;
            case "anchor-name":
                cssBox.AnchorName = value;
                break;
            case "position-anchor":
                cssBox.PositionAnchor = value;
                break;
            case "position-area":
                cssBox.PositionArea = value;
                break;
            case "position-try":
                cssBox.PositionTry = value;
                break;
            case "position-try-fallbacks":
                cssBox.PositionTryFallbacks = value;
                break;
            case "position-visibility":
                cssBox.PositionVisibility = value;
                break;
            case "line-height":
                cssBox.LineHeight = value;
                break;
            case "vertical-align":
                cssBox.VerticalAlign = value;
                break;
            case "text-indent":
                cssBox.TextIndent = value;
                break;
            case "text-align":
                cssBox.TextAlign = value;
                break;
            case "text-align-last":
                cssBox.TextAlignLast = value;
                break;
            case "text-decoration":
                ApplyTextDecorationShorthand(cssBox, value);
                break;
            case "text-decoration-line":
                cssBox.TextDecoration = value;
                break;
            case "text-decoration-style":
                cssBox.TextDecorationStyle = value;
                break;
            case "text-decoration-color":
                cssBox.TextDecorationColor = value;
                break;
            case "white-space":
                cssBox.WhiteSpace = NormalizeWhiteSpaceValue(value);
                break;
            case "break-before":
            case "page-break-before":
                cssBox.BreakBefore = value;
                break;
            case "break-after":
            case "page-break-after":
                cssBox.BreakAfter = value;
                break;
            case "page":
                cssBox.Page = value;
                break;
            case "line-clamp":
                cssBox.LineClamp = value;
                break;
            case "-webkit-line-clamp":
                cssBox.WebkitLineClamp = value;
                break;
            case "max-lines":
                cssBox.MaxLines = value;
                break;
            case "block-ellipsis":
                cssBox.BlockEllipsis = value;
                break;
            case "-webkit-box-orient":
                cssBox.WebkitBoxOrient = value;
                // Orientation decides how a legacy box is approximated, so a
                // `display: -webkit-box` already applied on this box has to be
                // re-mapped now that the axis is known.
                if (cssBox.LegacyWebkitBoxDisplay is { } legacyBox)
                    cssBox.Display = NormalizeDisplayValue(legacyBox, cssBox);
                break;
            case "text-transform":
                cssBox.TextTransform = value;
                break;
            case "word-break":
                cssBox.WordBreak = value;
                break;
            case "line-break":
                cssBox.LineBreak = value;
                break;
            case "visibility":
                cssBox.Visibility = value;
                break;
            case "image-animation":
                cssBox.ImageAnimation = value;
                break;
            case "word-spacing":
                cssBox.WordSpacing = value;
                break;
            case "font-family":
                cssBox.FontFamily = value;
                break;
            case "font-feature-settings":
                cssBox.FontFeatureSettings = value;
                break;
            case "font-variant-alternates":
                cssBox.FontVariantAlternates = value;
                break;
            case "font-size":
                cssBox.FontSize = value;
                break;
            case "font-style":
                cssBox.FontStyle = value;
                break;
            case "font-variant":
                cssBox.FontVariant = value;
                break;
            case "font-weight":
                cssBox.FontWeight = value;
                break;
            case "list-style":
                ApplyListStyleShorthand(cssBox, value);
                break;
            case "list-style-position":
                cssBox.ListStylePosition = value;
                break;
            case "list-style-image":
                cssBox.ListStyleImage = value;
                break;
            case "list-style-type":
                cssBox.ListStyleType = value;
                break;
            case "overflow":
                cssBox.Overflow = value;
                break;
            case "z-index":
                cssBox.ZIndex = value;
                break;
            case "animation-name":
                cssBox.AnimationName = value;
                break;
            case "animation-duration":
                cssBox.AnimationDuration = value;
                break;
            case "animation-timing-function":
                cssBox.AnimationTimingFunction = value;
                break;
            case "animation-delay":
                cssBox.AnimationDelay = value;
                break;
            case "animation-iteration-count":
                cssBox.AnimationIterationCount = value;
                break;
            case "animation-direction":
                cssBox.AnimationDirection = value;
                break;
            case "animation-fill-mode":
                cssBox.AnimationFillMode = value;
                break;
            case "animation-play-state":
                cssBox.AnimationPlayState = value;
                break;
        }
    }

    /// <summary>
    /// CSS Grid §7.4/§7.8: expand the <c>grid</c> and <c>grid-template</c>
    /// shorthands into <c>grid-template-rows</c>/<c>grid-template-columns</c>.
    /// Only the <c>none</c> and <c>&lt;rows&gt; / &lt;columns&gt;</c> forms are
    /// expanded (they cover the common author usage, e.g.
    /// <c>grid: 50px 50px / 50px 50px</c>); the <c>auto-flow</c> and
    /// template-areas <c>&lt;string&gt;</c> forms are left untouched — their
    /// longhands keep their cascaded/initial values — rather than risk
    /// mis-parsing them. The full <c>grid</c> shorthand additionally resets the
    /// implicit-track longhands to their initial values when it applies, per the
    /// spec's reset semantics.
    /// </summary>
    /// <summary>
    /// CSS Sizing 4 §5: expands <c>contain-intrinsic-size</c> into
    /// <c>contain-intrinsic-width</c> and <c>contain-intrinsic-height</c>. One component sets
    /// both; two set width then height.
    /// </summary>
    /// <remarks>
    /// A component is <c>auto? [ none | &lt;length&gt; ]</c>, so the value cannot simply be split on
    /// whitespace: <c>auto 100px</c> is one component naming both axes, and
    /// <c>auto 100px auto 200px</c> is two. The <c>auto</c> keyword binds forward to the value
    /// after it, which is what tells the two apart.
    /// </remarks>
    private static void ApplyContainIntrinsicSizeShorthand(CssBox cssBox, string value)
    {
        var tokens = (value ?? string.Empty)
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        var components = new List<string>();

        for (int i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase)
                && i + 1 < tokens.Length)
            {
                components.Add(tokens[i] + " " + tokens[i + 1]);
                i++;
                continue;
            }

            components.Add(tokens[i]);
        }

        if (components.Count == 0)
            return;

        cssBox.ContainIntrinsicWidth = components[0];
        cssBox.ContainIntrinsicHeight = components.Count > 1 ? components[1] : components[0];

        // The shorthand names the physical longhands, so a flow-relative value cascaded earlier
        // would otherwise keep winning the ResolvePhysicalBound lookup and the shorthand would
        // appear to do nothing.
        cssBox.ContainIntrinsicInlineSize = string.Empty;
        cssBox.ContainIntrinsicBlockSize = string.Empty;
    }

    /// <summary>The <c>contain-intrinsic-size</c> shorthand rebuilt from its two longhands.</summary>
    private static string JoinContainIntrinsicSize(CssBox cssBox) =>
        cssBox.ContainIntrinsicWidth == cssBox.ContainIntrinsicHeight
            ? cssBox.ContainIntrinsicWidth
            : cssBox.ContainIntrinsicWidth + " " + cssBox.ContainIntrinsicHeight;

    private static void ApplyGridShorthand(CssBox cssBox, string value, bool resetAutoTracks)
    {
        string v = value.Trim();

        if (v.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            cssBox.GridTemplateRows = "none";
            cssBox.GridTemplateColumns = "none";
        
            if (resetAutoTracks) ResetGridAutoTracks(cssBox);
            return;
        }

        // The auto-flow and template-areas (<string>) forms are out of scope:
        // leave every longhand as the cascade already set it.
        if (v.Contains("auto-flow", StringComparison.OrdinalIgnoreCase)
            || v.Contains('"') || v.Contains('\''))
            return;

        int slash = TopLevelSlashIndex(v);
        if (slash < 0)
            return;
        
        string rows = v[..slash].Trim();
        string cols = v[(slash + 1)..].Trim();
        if (rows.Length == 0 || cols.Length == 0)
            return;

        cssBox.GridTemplateRows = rows;
        cssBox.GridTemplateColumns = cols;
        
        if (resetAutoTracks) ResetGridAutoTracks(cssBox);
    }

    private static void ResetGridAutoTracks(CssBox cssBox)
    {
        cssBox.GridAutoFlow = "row";
        cssBox.GridAutoRows = "auto";
        cssBox.GridAutoColumns = "auto";
    }

    /// <summary>
    /// CSS Grid §8.4: expand <c>grid-area</c>
    /// (<c>&lt;row-start&gt; / &lt;col-start&gt; / &lt;row-end&gt; / &lt;col-end&gt;</c>,
    /// 1–4 slash-separated components) into the <c>grid-row</c>/<c>grid-column</c>
    /// (<c>start / end</c>) longhands the placement reader consumes. An omitted end
    /// mirrors its start when the start is a custom-ident (a named line), else it is
    /// <c>auto</c>; a lone custom-ident sets all four edges (§8.4).
    /// </summary>
    private static void ApplyGridAreaShorthand(CssBox cssBox, string value)
    {
        var parts = value.Split('/');
        string rs = parts.Length > 0 ? parts[0].Trim() : "auto";
        string cs = parts.Length > 1 ? parts[1].Trim() : Mirror(rs);
        string re = parts.Length > 2 ? parts[2].Trim() : Mirror(rs);
        string ce = parts.Length > 3 ? parts[3].Trim() : Mirror(cs);
        
        if (rs.Length == 0) rs = "auto";
        
        cssBox.GridRow = CombineGridLine(rs, re);
        cssBox.GridColumn = CombineGridLine(cs, ce);

        // A custom-ident (named line) mirrors to the opposite edge; a number/span/
        // auto does not.
        static string Mirror(string s) => IsCustomIdent(s) ? s : "auto";
    }

    /// <summary>Set one edge (<paramref name="isStart"/>) of <c>grid-row</c>/
    /// <c>grid-column</c> from a <c>grid-*-start</c>/<c>grid-*-end</c> longhand,
    /// preserving the other edge already stored.</summary>
    private static void SetGridLineSide(CssBox cssBox, bool isRow, bool isStart, string value)
    {
        string cur = isRow ? cssBox.GridRow : cssBox.GridColumn;
        int slash = cur?.IndexOf('/') ?? -1;
        string start = slash >= 0 ? cur[..slash].Trim()
                     : string.IsNullOrWhiteSpace(cur) ? "auto" : cur.Trim();
        string end = slash >= 0 ? cur[(slash + 1)..].Trim() : "auto";
        if (isStart) start = value.Trim(); else end = value.Trim();
        string combined = CombineGridLine(start, end);
        if (isRow) cssBox.GridRow = combined; else cssBox.GridColumn = combined;
    }

    private static string CombineGridLine(string start, string end)
    {
        if (string.IsNullOrWhiteSpace(start)) start = "auto";
        return string.IsNullOrWhiteSpace(end) || end == "auto" ? start : start + " / " + end;
    }

    /// <summary>True for a grid-line custom-ident (a named line) — not a number,
    /// <c>auto</c>, or a <c>span</c> expression.</summary>
    private static bool IsCustomIdent(string s)
    {
        s = s?.Trim() ?? "";
        if (s.Length == 0 || s.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("span", StringComparison.OrdinalIgnoreCase))
            return false;
        
        char c = s[0];
        return !(char.IsDigit(c) || c == '-' || c == '+' || c == '.');
    }

    private static readonly string[] DisplayOutsideKeywords = ["block", "inline", "run-in"];

    /// <summary>
    /// Normalize a <c>display</c> value the layout engine consumes: collapse the
    /// CSS Display 3 two-value syntax (<c>inline grid</c>, <c>block flow-root</c>,
    /// …) to its legacy single keyword. The experimental CSS Grid Level 3
    /// <c>grid-lanes</c> &lt;display-inside&gt; is treated as an invalid,
    /// dropped declaration — the element falls back to its default display —
    /// matching reference browsers, which ship no unflagged grid-lanes support.
    /// </summary>
    internal static string NormalizeDisplayValue(string value, CssBox box = null)
    {
        string v = value.Trim();
        var parts = v.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        // `display: grid-lanes` / `<outside> grid-lanes` is invalid: no stable
        // browser ships the CSS Grid Level 3 grid-lanes keyword unflagged, so the
        // whole declaration is dropped and the element keeps its default display.
        // The pinned Broiler.CSS now rejects grid-lanes at validation (commit
        // 1f75198, formerly patches/0003-css-reject-display-grid-lanes.patch), so a
        // rejected grid-lanes never reaches this method — this branch is an inert
        // defensive no-op kept for builds against an older submodule pointer that
        // still forwards the value here.
        if (Array.Exists(parts, static p => p.Equals("grid-lanes", StringComparison.OrdinalIgnoreCase)))
            return DefaultDisplayForElement(box);

        // The legacy WebKit flexible box. Every browser still supports it, and it
        // is how `-webkit-line-clamp` is opted into, so it cannot be dropped the
        // way grid-lanes is — an unmapped value is neither IsBlock nor IsInline
        // and the box then lays out and paints nothing at all.
        //
        // It is approximated by a block container rather than by `flex`, because
        // the clamp opts in through `-webkit-box-orient: vertical`, and a vertical
        // legacy box stacks its children in the block direction exactly as a block
        // container does. That is also the shape the css-overflow/line-clamp
        // *references* assume: they draw the clamped result with a plain
        // `display: -webkit-box` (31 of them) or `display: inline-block`
        // (webkit-line-clamp-024-ref) and pre-typed ellipsis text. A horizontally
        // oriented legacy box keeps the closest flex mapping instead; orientation
        // is inherited, so it is read off the box rather than this value.
        if (parts.Length == 1)
        {
            bool vertical = IsVerticalWebkitBoxOrient(box?.WebkitBoxOrient);

            if (v.Equals("-webkit-box", StringComparison.OrdinalIgnoreCase))
                return vertical ? CssConstants.Block : "flex";

            if (v.Equals("-webkit-inline-box", StringComparison.OrdinalIgnoreCase))
                return vertical ? CssConstants.InlineBlock : "inline-flex";
        }

        if (parts.Length != 2)
            return v;

        string a = parts[0].ToLowerInvariant(), b = parts[1].ToLowerInvariant();
        string outside, inside;
        
        if (Array.IndexOf(DisplayOutsideKeywords, a) >= 0) { outside = a; inside = b; }
        else if (Array.IndexOf(DisplayOutsideKeywords, b) >= 0) { outside = b; inside = a; }
        else return v; // not a recognized two-value form; leave for the caller

        bool isInline = outside == "inline";
        return inside switch
        {
            "grid" => isInline ? "inline-grid" : "grid",
            "flex" => isInline ? "inline-flex" : "flex",
            "table" => isInline ? "inline-table" : "table",
            "flow-root" => isInline ? "inline-block" : "flow-root",
            "flow" => isInline ? "inline" : "block",
            _ => v,
        };
    }

    /// <summary>
    /// Normalizes a CSS Text 4 <c>white-space</c> shorthand value to the legacy
    /// single keyword the layout engine keys off (<c>normal</c>, <c>nowrap</c>,
    /// <c>pre</c>, <c>pre-wrap</c>, <c>pre-line</c>, or the passthrough
    /// <c>break-spaces</c>). <c>white-space</c> is a shorthand for
    /// <c>white-space-collapse</c> and <c>text-wrap-mode</c>; the two-longhand
    /// form <c>&lt;collapse&gt; || &lt;wrap-mode&gt;</c> and modern single
    /// keywords (<c>preserve</c>, <c>preserve-breaks</c>, …) carry the same
    /// meaning as a legacy keyword and are folded onto it so the existing
    /// white-space handling in the engine applies unchanged. Longhand
    /// combinations with no exact legacy equivalent are mapped to the closest
    /// keyword that preserves the dominant collapse behaviour. Legacy keywords
    /// and unrecognized values pass through untouched.
    /// </summary>
    internal static string NormalizeWhiteSpaceValue(string value)
    {
        string v = value.Trim().ToLowerInvariant();

        // Legacy single keywords already map directly onto the engine's model.
        if (v is CssConstants.Normal or CssConstants.NoWrap or CssConstants.Pre
            or CssConstants.PreWrap or CssConstants.PreLine)
            return v;

        string collapse = null, wrap = null;
        foreach (var token in v.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token)
            {
                case "collapse":
                case "preserve":
                case "preserve-breaks":
                case "preserve-spaces":
                case "break-spaces":
                    if (collapse != null) return v; // malformed; leave untouched
                    collapse = token;
                    break;

                case "wrap":
                case "nowrap":
                    if (wrap != null) return v;
                    wrap = token;
                    break;

                default:
                    return v; // not a recognized modern token; leave as-is
            }
        }

        if (collapse == null && wrap == null)
            return v;

        // Longhand initial values: white-space-collapse: collapse, text-wrap-mode: wrap.
        collapse ??= "collapse";
        wrap ??= "wrap";

        // break-spaces has no legacy keyword; keep the token so the engine can
        // special-case it (both wrap modes collapse to the same handling today).
        if (collapse == "break-spaces")
            return "break-spaces";

        return (collapse, wrap) switch
        {
            ("collapse", "wrap") => CssConstants.Normal,
            ("collapse", "nowrap") => CssConstants.NoWrap,
            ("preserve", "wrap") => CssConstants.PreWrap,
            ("preserve", "nowrap") => CssConstants.Pre,
            ("preserve-breaks", "wrap") => CssConstants.PreLine,
            // preserve-breaks + nowrap: preserves segment breaks with no exact
            // legacy keyword — pre-line keeps the newline preservation that
            // dominates the visual result.
            ("preserve-breaks", "nowrap") => CssConstants.PreLine,
            // preserve-spaces preserves spaces but collapses segment breaks; the
            // closest legacy keyword that preserves spaces is pre-wrap/pre.
            ("preserve-spaces", "nowrap") => CssConstants.Pre,
            ("preserve-spaces", "wrap") => CssConstants.PreWrap,
            _ => CssConstants.Normal,
        };
    }

    /// <summary>
    /// Whether a <c>-webkit-box-orient</c> value selects the block axis. The
    /// legacy property spells it <c>vertical</c>; the CSS-aligned aliases
    /// <c>block-axis</c> and <c>inline-axis</c> mean the same two things and are
    /// accepted alongside it. Anything else (including the initial
    /// <c>horizontal</c>) is the inline axis.
    /// </summary>
    internal static bool IsVerticalWebkitBoxOrient(string value)
    {
        string v = value?.Trim();
        return v != null
            && (v.Equals("vertical", StringComparison.OrdinalIgnoreCase)
                || v.Equals("block-axis", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The default (user-agent) display an element falls back to when its
    /// <c>display</c> declaration is invalid and dropped: <c>block</c> for
    /// block-level HTML elements, otherwise <c>inline</c> (inline elements and
    /// unknown/custom elements such as <c>&lt;grid&gt;</c>). Only used by the
    /// grid-lanes fallback in <see cref="NormalizeDisplayValue"/>.
    /// </summary>
    private static string DefaultDisplayForElement(CssBox box)
    {
        string name = box?.HtmlTag?.Name;
        return name != null && BlockLevelHtmlTags.Contains(name)
            ? CssConstants.Block
            : CssConstants.Inline;
    }

    /// <summary>HTML elements whose user-agent <c>display</c> is <c>block</c>
    /// (the ones exercised by the css-grid/grid-lanes tests plus the common flow
    /// block elements). Table and list-item elements are deliberately excluded —
    /// their UA display is not <c>block</c> — but no grid-lanes test targets them.</summary>
    private static readonly HashSet<string> BlockLevelHtmlTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "center", "dd", "details",
        "dialog", "dir", "div", "dl", "dt", "fieldset", "figcaption", "figure",
        "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup",
        "hr", "main", "menu", "nav", "ol", "p", "pre", "section", "summary", "ul",
    };

    /// <summary>
    /// Index of the row/column separator slash in a <c>grid</c>/<c>grid-template</c>
    /// value, ignoring any slash nested inside <c>()</c> or <c>[]</c> (a track list
    /// itself never contains a top-level slash). Returns -1 when there is none.
    /// </summary>
    private static int TopLevelSlashIndex(string v)
    {
        int paren = 0, bracket = 0;
        for (int i = 0; i < v.Length; i++)
        {
            char c = v[i];
            if (c == '(') paren++;
            else if (c == ')') { if (paren > 0) paren--; }
            else if (c == '[') bracket++;
            else if (c == ']') { if (bracket > 0) bracket--; }
            else if (c == '/' && paren == 0 && bracket == 0)
                return i;
        }
        return -1;
    }

    private static void ApplyLogicalBorderShorthand(CssBox cssBox, string value, bool inlineAxis)
    {
        var parts = value.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
        string? width = null;
        string? style = null;
        string? color = null;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "none" or "hidden" or "dotted" or "dashed" or "solid"
                or "double" or "groove" or "ridge" or "inset" or "outset")
            {
                style ??= part;
            }
            else if (lower is "thin" or "medium" or "thick" || CssLengthParser.IsValidLength(part))
            {
                width ??= part;
            }
            else
            {
                color ??= part;
            }
        }

        if (width != null)
        {
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: true, "width", width);
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: false, "width", width);
        }

        if (style != null)
        {
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: true, "style", style);
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: false, "style", style);
        }

        if (color != null)
        {
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: true, "color", color);
            SetLogicalBorderComponent(cssBox, inlineAxis, startSide: false, "color", color);
        }
    }

    /// <summary>
    /// CSS Lists 3 §5: <c>list-style</c> is a shorthand for <c>list-style-position</c>,
    /// <c>list-style-image</c> and <c>list-style-type</c>, in any order, and omitted
    /// components reset to their initial values. Only the whole value was being kept, so
    /// nothing read it and the longhands stayed at their defaults — <c>list-style: none</c>
    /// left the marker at <c>disc</c> and drew a bullet on every navigation item and menu
    /// entry an author had asked to go unmarked.
    /// </summary>
    /// <remarks>
    /// The one <c>none</c> ambiguity CSS calls out: it may set either the type or the image,
    /// so a lone <c>none</c> sets both, and <c>list-style: none disc</c> leaves the type as
    /// <c>disc</c> with only the image suppressed.
    /// </remarks>
    private static void ApplyListStyleShorthand(CssBox cssBox, string value)
    {
        cssBox.ListStyle = value;

        if (string.IsNullOrWhiteSpace(value))
            return;

        // Shorthand application resets omitted longhands back to their initial values.
        string type = CssConstants.Disc;
        string position = "outside";
        string image = CssConstants.None;
        bool sawType = false, sawImage = false, sawNone = false;

        foreach (var token in SplitListStyleComponents(value))
        {
            var lower = token.ToLowerInvariant();

            if (lower is "inside" or "outside")
            {
                position = lower;
            }
            else if (lower == CssConstants.None)
            {
                sawNone = true;
            }
            else if (lower.StartsWith("url(", StringComparison.Ordinal)
                || lower.StartsWith("linear-gradient(", StringComparison.Ordinal)
                || lower.StartsWith("radial-gradient(", StringComparison.Ordinal)
                || lower.StartsWith("image-set(", StringComparison.Ordinal))
            {
                image = token;
                sawImage = true;
            }
            else if (lower is "inherit" or "initial" or "unset" or "revert" or "revert-layer")
            {
                // A CSS-wide keyword is the whole value; hand it to each longhand as-is.
                cssBox.ListStyleType = token;
                cssBox.ListStylePosition = token;
                cssBox.ListStyleImage = token;
                return;
            }
            else
            {
                type = token;
                sawType = true;
            }
        }

        // `none` names whichever of type/image the rest of the value left unstated; when it
        // states neither, it states both.
        if (sawNone)
        {
            if (!sawType)
                type = CssConstants.None;
            if (!sawImage)
                image = CssConstants.None;
        }

        cssBox.ListStyleType = type;
        cssBox.ListStylePosition = position;
        cssBox.ListStyleImage = image;
    }

    /// <summary>
    /// Splits a <c>list-style</c> value on top-level whitespace, keeping a functional
    /// component such as <c>url(a b.png)</c> or <c>image-set(...)</c> in one piece.
    /// </summary>
    private static List<string> SplitListStyleComponents(string value)
    {
        var components = new List<string>(3);
        int depth = 0, start = -1;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '(')
                depth++;
            else if (c == ')' && depth > 0)
                depth--;

            if (depth == 0 && char.IsWhiteSpace(c))
            {
                if (start >= 0)
                {
                    components.Add(value[start..i]);
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
            components.Add(value[start..]);

        return components;
    }

    private static void ApplyTextDecorationShorthand(CssBox cssBox, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            cssBox.TextDecoration = string.Empty;
            return;
        }

        // Shorthand application resets omitted longhands back to their initial values.
        cssBox.TextDecoration = "none";
        cssBox.TextDecorationStyle = "solid";
        cssBox.TextDecorationColor = "currentcolor";

        var parts = value.Trim().Split([' '], StringSplitOptions.RemoveEmptyEntries);
        string? line = null;

        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (lower is "underline" or "overline" or "line-through" or "none")
            {
                line = part;
            }
            else if (lower is "solid" or "double" or "dotted" or "dashed" or "wavy")
            {
                cssBox.TextDecorationStyle = part;
            }
            else
            {
                cssBox.TextDecorationColor = part;
            }
        }

        if (line != null)
            cssBox.TextDecoration = line;
    }

    private static void SetLogicalBorderComponent(
        CssBox cssBox,
        bool inlineAxis,
        bool startSide,
        string component,
        string value)
    {
        var side = ResolveLogicalBorderSide(cssBox, inlineAxis, startSide);
        switch (side, component)
        {
            case ("top", "color"):
                cssBox.BorderTopColor = value;
                break;
            case ("right", "color"):
                cssBox.BorderRightColor = value;
                break;
            case ("bottom", "color"):
                cssBox.BorderBottomColor = value;
                break;
            case ("left", "color"):
                cssBox.BorderLeftColor = value;
                break;
            case ("top", "style"):
                cssBox.BorderTopStyle = value;
                break;
            case ("right", "style"):
                cssBox.BorderRightStyle = value;
                break;
            case ("bottom", "style"):
                cssBox.BorderBottomStyle = value;
                break;
            case ("left", "style"):
                cssBox.BorderLeftStyle = value;
                break;
            case ("top", "width"):
                cssBox.BorderTopWidth = value;
                break;
            case ("right", "width"):
                cssBox.BorderRightWidth = value;
                break;
            case ("bottom", "width"):
                cssBox.BorderBottomWidth = value;
                break;
            case ("left", "width"):
                cssBox.BorderLeftWidth = value;
                break;
        }
    }

    private static string ResolveLogicalBorderSide(CssBox cssBox, bool inlineAxis, bool startSide)
    {
        var writingMode = cssBox.WritingMode?.ToLowerInvariant() ?? "horizontal-tb";
        var direction = cssBox.Direction?.ToLowerInvariant() ?? "ltr";

        if (inlineAxis)
        {
            return writingMode switch
            {
                "vertical-rl" or "vertical-lr" or "sideways-rl" or "sideways-lr" => startSide ? "top" : "bottom",
                _ => startSide
                    ? (direction == "rtl" ? "right" : "left")
                    : (direction == "rtl" ? "left" : "right"),
            };
        }

        return writingMode switch
        {
            "vertical-rl" or "sideways-rl" => startSide ? "right" : "left",
            "vertical-lr" or "sideways-lr" => startSide ? "left" : "right",
            _ => startSide ? "top" : "bottom",
        };
    }

    private static string ResolveLengthAttrFunctions(CssBox cssBox, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOf("attr(", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return value;
        }

        return LengthAttrFunctionPattern.Replace(
            value,
            match =>
            {
                var attrName = match.Groups["name"].Value;
                var fallback = match.Groups["fallback"].Success
                    ? match.Groups["fallback"].Value.Trim()
                    : string.Empty;
                var attributeValue = cssBox.GetAttribute(attrName, string.Empty).Trim();

                if (!string.IsNullOrEmpty(attributeValue) &&
                    CssLengthParser.IsValidLength(attributeValue))
                {
                    return attributeValue;
                }

                if (!string.IsNullOrEmpty(fallback) &&
                    CssLengthParser.IsValidLength(fallback))
                {
                    return fallback;
                }

                return string.Empty;
            });
    }

    [GeneratedRegex(@"attr\(\s*(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+type\(\s*<length>\s*\)\s*(?:,\s*(?<fallback>[^)]+?))?\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LengthAttrRegex();
}
