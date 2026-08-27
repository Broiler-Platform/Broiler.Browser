using Broiler.CSS;
using Broiler.Graphics;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;


namespace Broiler.Layout.Engine;

internal abstract partial class CssBoxProperties
{
    internal const string InvalidCustomPropertySentinel = "\u0000";

    #region CSS Fields

    private string _borderTopWidth = "medium";
    private string _borderRightWidth = "medium";
    private string _borderBottomWidth = "medium";
    private string _borderLeftWidth = "medium";
    private string _borderTopColor = "black";
    private string _borderRightColor = "black";
    private string _borderBottomColor = "black";
    private string _borderLeftColor = "black";
    private string _outlineColor = string.Empty;
    private string _outlineWidth = "medium";
    private string _outlineStyle = "none";
    private string _outlineOffset = "0";
    private string _overflowClipMargin = "0px";
    private string _bottom = "auto";
    private string _color = "black";
    private string _cornerRadius = "0";
    private string _fontSize = "medium";
    private string _left = "auto";
    private string _lineHeight = "normal";
    private string _paddingLeft = "0";
    private string _paddingBottom = "0";
    private string _paddingRight = "0";
    private string _paddingTop = "0";
    private string _right = "auto";
    private string _width = "auto";
    private string _height = "auto";
    private string _maxWidth = "none";
    private string _minWidth = "0";
    private string _inlineSize = "auto";
    private string _blockSize = "auto";
    private string _writingMode = "horizontal-tb";
    private string _backgroundColor = "transparent";
    private string _backgroundImage = "none";
    private string _backgroundClip = "border-box";
    private string _clipPath = "none";
    private string _clip = "auto";
    private string _textIndent = "0";
    private string _textDecorationColor = "currentcolor";
    private string _top = "auto";
    private string _wordSpacing = "normal";

    #endregion


    #region Fields

    private PointF _location;
    private SizeF _size;

    private double _actualCornerNw = double.NaN;
    private double _actualCornerNe = double.NaN;
    private double _actualCornerSw = double.NaN;
    private double _actualCornerSe = double.NaN;
    private BColor _actualColor = BColor.Empty;
    private double _actualBackgroundGradientAngle = double.NaN;
    private double _actualHeight = double.NaN;
    private double _actualWidth = double.NaN;
    private double _actualPaddingTop = double.NaN;
    private double _actualPaddingBottom = double.NaN;
    private double _actualPaddingRight = double.NaN;
    private double _actualPaddingLeft = double.NaN;
    private double _actualMarginTop = double.NaN;
    private double _collapsedMarginTop = double.NaN;
    private double _actualMarginBottom = double.NaN;
    private double _actualMarginRight = double.NaN;
    private double _actualMarginLeft = double.NaN;

    // CSS2.1 §8.3: a used margin of `auto` resolves to 0, and the ActualMargin* getters below
    // rewrite the specified string `auto → "0"` (a caching side-effect). That loses the fact that
    // the margin was *specified* auto — which the §10.3.7/§10.6.4 abspos/fixed auto-margin centring
    // needs. Latch it here (set when a getter first sees `auto`, before the rewrite) so the
    // centring can ask <see cref="IsSpecifiedMarginLeftAuto"/> after the string has been zeroed.
    private bool _marginLeftWasAuto;
    private bool _marginRightWasAuto;
    private bool _marginTopWasAuto;
    private bool _marginBottomWasAuto;

    /// <summary>Whether <c>margin-left</c> was specified <c>auto</c> (survives the getter's used-value rewrite).</summary>
    internal bool IsSpecifiedMarginLeftAuto => _marginLeftWasAuto || MarginLeft == CssConstants.Auto;
    internal bool IsSpecifiedMarginRightAuto => _marginRightWasAuto || MarginRight == CssConstants.Auto;
    internal bool IsSpecifiedMarginTopAuto => _marginTopWasAuto || MarginTop == CssConstants.Auto;
    internal bool IsSpecifiedMarginBottomAuto => _marginBottomWasAuto || MarginBottom == CssConstants.Auto;

    /// <summary>Drops the cached used margins so a subsequent <c>ActualMargin*</c> read reflects a
    /// margin string just rewritten (e.g. auto-margin centring resolving an <c>auto</c> to a px value).</summary>
    internal void InvalidateActualMargins()
    {
        _actualMarginLeft = double.NaN;
        _actualMarginRight = double.NaN;
        _actualMarginTop = double.NaN;
        _actualMarginBottom = double.NaN;
    }
    private double _actualBorderTopWidth = double.NaN;
    private double _actualBorderLeftWidth = double.NaN;
    private double _actualBorderBottomWidth = double.NaN;
    private double _actualBorderRightWidth = double.NaN;
    private double _actualOutlineWidth = double.NaN;
    private double _actualOutlineOffset = double.NaN;
    private double _actualOverflowClipMargin = double.NaN;
    private BColor _actualOutlineColor = BColor.Empty;
    private double _actualMaxWidth = double.NaN;
    private double _actualMinWidth = double.NaN;

    /// <summary>
    /// the width of whitespace between words
    /// </summary>
    private double _actualLineHeight = double.NaN;
    private double _actualTextIndent = double.NaN;
    private double _actualBorderSpacingHorizontal = double.NaN;
    private double _actualBorderSpacingVertical = double.NaN;
    private BColor _actualBackgroundGradient = BColor.Empty;
    private BColor _actualBorderTopColor = BColor.Empty;
    private BColor _actualBorderLeftColor = BColor.Empty;
    private BColor _actualBorderBottomColor = BColor.Empty;
    private BColor _actualBorderRightColor = BColor.Empty;
    private BColor _actualTextDecorationColor = BColor.Empty;
    private BColor _actualBackgroundColor = BColor.Empty;
    private ILayoutFont _actualFont;

    #endregion


    #region CSS Properties

    public string BorderBottomWidth
    {
        get { return _borderBottomWidth; }
        set
        {
            _borderBottomWidth = value;
            _actualBorderBottomWidth = float.NaN;
        }
    }

    public string BorderLeftWidth
    {
        get { return _borderLeftWidth; }
        set
        {
            _borderLeftWidth = value;
            _actualBorderLeftWidth = float.NaN;
        }
    }

    public string BorderRightWidth
    {
        get { return _borderRightWidth; }
        set
        {
            _borderRightWidth = value;
            _actualBorderRightWidth = float.NaN;
        }
    }

    public string BorderTopWidth
    {
        get { return _borderTopWidth; }
        set
        {
            _borderTopWidth = value;
            _actualBorderTopWidth = float.NaN;
        }
    }

    private string _borderBottomStyle = "none";
    private string _borderLeftStyle = "none";
    private string _borderRightStyle = "none";
    private string _borderTopStyle = "none";

    /// <summary>CSS2.1 §8.5.3: Changing border-style affects the used border-width
    /// (style "none"/"hidden" forces width to zero), so invalidate the cached
    /// actual width whenever the style changes.</summary>
    public string BorderBottomStyle
    {
        get => _borderBottomStyle;
        set { _borderBottomStyle = value; _actualBorderBottomWidth = double.NaN; }
    }

    public string BorderLeftStyle
    {
        get => _borderLeftStyle;
        set { _borderLeftStyle = value; _actualBorderLeftWidth = double.NaN; }
    }

    public string BorderRightStyle
    {
        get => _borderRightStyle;
        set { _borderRightStyle = value; _actualBorderRightWidth = double.NaN; }
    }

    public string BorderTopStyle
    {
        get => _borderTopStyle;
        set { _borderTopStyle = value; _actualBorderTopWidth = double.NaN; }
    }

    public string BorderBottomColor
    {
        get { return ResolveCssVariables(_borderBottomColor); }
        set
        {
            _borderBottomColor = value;
            _actualBorderBottomColor = BColor.Empty;
        }
    }

    public string BorderLeftColor
    {
        get { return ResolveCssVariables(_borderLeftColor); }
        set
        {
            _borderLeftColor = value;
            _actualBorderLeftColor = BColor.Empty;
        }
    }

    public string BorderRightColor
    {
        get { return ResolveCssVariables(_borderRightColor); }
        set
        {
            _borderRightColor = value;
            _actualBorderRightColor = BColor.Empty;
        }
    }

    public string BorderTopColor
    {
        get { return ResolveCssVariables(_borderTopColor); }
        set
        {
            _borderTopColor = value;
            _actualBorderTopColor = BColor.Empty;
        }
    }

    public string BorderSpacing { get; set; } = "0";
    public string BorderCollapse { get; set; } = "separate";

    // CSS UI §2: outline is painted just outside the border edge and does not
    // affect layout. Stored uniformly (outline cannot be set per-side).
    // Backing fields invalidate the lazily-resolved used values on set, matching
    // the border-width/-colour caching pattern so repeated paint-time reads of
    // ActualOutline* don't re-parse the length/colour strings.
    public string OutlineWidth
    {
        get => _outlineWidth;
        set { _outlineWidth = value; _actualOutlineWidth = double.NaN; }
    }
    public string OutlineStyle
    {
        // CSS UI §2: outline-style none/hidden forces the used width to zero, so
        // invalidate the cached used width when the style changes.
        get => _outlineStyle;
        set { _outlineStyle = value; _actualOutlineWidth = double.NaN; }
    }
    public string OutlineColor
    {
        get => ResolveCssVariables(_outlineColor);
        set { _outlineColor = value ?? string.Empty; _actualOutlineColor = BColor.Empty; }
    }
    public string OutlineOffset
    {
        get => _outlineOffset;
        set { _outlineOffset = value; _actualOutlineOffset = double.NaN; }
    }

    /// <summary>Used outline width in px; 0 when the outline style is none/hidden.</summary>
    public double ActualOutlineWidth
    {
        get
        {
            if (double.IsNaN(_actualOutlineWidth))
            {
                if (string.IsNullOrEmpty(OutlineStyle) || OutlineStyle == CssConstants.None
                    || OutlineStyle.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                    _actualOutlineWidth = 0;
                else
                    // Paint-only used length: scale by zoom exactly like the border widths (absolute /
                    // thin·medium·thick keyword × EffectiveZoom, em rides the already-zoomed font).
                    _actualOutlineWidth = ApplyZoomToLength(OutlineWidth, CssLengthParser.GetActualBorderWidth(OutlineWidth, GetEmHeight()));
            }

            return _actualOutlineWidth;
        }
    }

    /// <summary>Used outline-offset in px (the gap between border edge and outline).</summary>
    public double ActualOutlineOffset
    {
        get
        {
            if (double.IsNaN(_actualOutlineOffset))
                _actualOutlineOffset = string.IsNullOrEmpty(OutlineOffset)
                    ? 0
                    // Paint-only used length (may be negative); absolute × EffectiveZoom, em rides the font.
                    : ApplyZoomToLength(OutlineOffset, CssLengthParser.ParseLength(OutlineOffset, 0, GetEmHeight()));

            return _actualOutlineOffset;
        }
    }

    /// <summary>
    /// CSS Overflow §4: the <c>overflow-clip-margin</c> property. Expands the
    /// overflow clip edge outward by a <c>&lt;length [0,∞]&gt;</c> for boxes that
    /// clip without scrolling (<c>overflow: clip</c> or paint containment). A
    /// leading <c>&lt;visual-box&gt;</c> keyword (content-/padding-/border-box) is
    /// accepted and ignored here — the clip base stays the padding box.
    /// </summary>
    public string OverflowClipMargin
    {
        get => _overflowClipMargin;
        set { _overflowClipMargin = value; _actualOverflowClipMargin = double.NaN; }
    }

    /// <summary>Used overflow-clip-margin in px (outward clip-edge expansion; never negative).</summary>
    public double ActualOverflowClipMargin
    {
        get
        {
            if (double.IsNaN(_actualOverflowClipMargin))
            {
                // Drop an optional leading <visual-box> keyword and keep the length token.
                var token = _overflowClipMargin;
                if (!string.IsNullOrEmpty(token))
                {
                    foreach (var part in token.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (part.Length > 0 && (char.IsDigit(part[0]) || part[0] is '.' or '+' or '-'))
                        {
                            token = part;
                            break;
                        }
                }

                var px = string.IsNullOrEmpty(token)
                    ? 0
                    // Paint-only used length; absolute × EffectiveZoom, em rides the font. Clamp to ≥0 per grammar.
                    : ApplyZoomToLength(token, CssLengthParser.ParseLength(token, 0, GetEmHeight()));
                _actualOverflowClipMargin = Math.Max(0, px);
            }

            return _actualOverflowClipMargin;
        }
    }

    /// <summary>
    /// Used outline colour. The initial value (<c>auto</c>/<c>invert</c>) and an
    /// unset colour resolve to <c>currentColor</c> (the element's text colour),
    /// matching modern browsers.
    /// </summary>
    public BColor ActualOutlineColor
    {
        get
        {
            string c = OutlineColor;
            if (string.IsNullOrEmpty(c)
                || c.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || c.Equals("invert", StringComparison.OrdinalIgnoreCase)
                || c.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
                return ActualColor; // cached element colour

            if (_actualOutlineColor.IsEmpty)
                _actualOutlineColor = GetActualColor(c);

            return _actualOutlineColor;
        }
    }

    public string CornerRadius
    {
        get { return _cornerRadius; }
        set
        {
            string raw = value ?? string.Empty;

            // `border-radius: <horizontal> / <vertical>` gives elliptical corners. The vertical half
            // used to be cut off here and thrown away, so `75px / 50px` rounded as if it were
            // `75px` — circular corners on a shape the author asked to be an ellipse. Each corner
            // now carries "<h> <v>", the same two-value form the per-corner longhands accept.
            int slashIndex = raw.IndexOf('/');
            string[] vertical = slashIndex >= 0
                ? raw[(slashIndex + 1)..].Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                : [];
            if (slashIndex >= 0)
                raw = raw[..slashIndex];

            string[] r = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            // border-radius lists its corners clockwise from the top-left, and fills a short list
            // by mirroring: 2 values are the two diagonals, 3 leave the bottom-left to match the
            // top-right. This used to read the list as if the first value were the top-RIGHT corner,
            // so `border-radius: 5px 60px 5px 5px` cut the top-left where CSS cuts the top-right,
            // and a 3-value list never assigned the bottom-left at all. Invisible while every corner
            // shares a radius, which is nearly every use of the property.
            static string[] ExpandCorners(string[] values) => values.Length switch
            {
                // top-left, top-right, bottom-right, bottom-left
                1 => [values[0], values[0], values[0], values[0]],
                2 => [values[0], values[1], values[0], values[1]],
                3 => [values[0], values[1], values[2], values[1]],
                _ => [values[0], values[1], values[2], values[3]],
            };

            if (r.Length is >= 1 and <= 4)
            {
                var horizontal = ExpandCorners(r);
                // The vertical list expands over the corners by the same rule, so each corner keeps
                // its own pair rather than borrowing a neighbour's.
                var verticalByCorner = vertical.Length is >= 1 and <= 4 ? ExpandCorners(vertical) : null;

                string Corner(int corner) => verticalByCorner is null
                    ? horizontal[corner]
                    : horizontal[corner] + " " + verticalByCorner[corner];

                CornerNwRadius = Corner(0);
                CornerNeRadius = Corner(1);
                CornerSeRadius = Corner(2);
                CornerSwRadius = Corner(3);
            }

            _cornerRadius = value;
        }
    }

    public string CornerNwRadius { get; set; } = "0";
    public string CornerNeRadius { get; set; } = "0";
    public string CornerSeRadius { get; set; } = "0";
    public string CornerSwRadius { get; set; } = "0";
    public string MarginBottom { get; set; } = "0";
    public string MarginLeft { get; set; } = "0";
    public string MarginRight { get; set; } = "0";
    public string MarginTop { get; set; } = "0";

    /// <summary>
    /// CSS Box Model 4 §6.2 <c>margin-trim</c>: controls trimming of a box's
    /// own margins adjacent to its content edges (e.g. the block-start margin
    /// of the first child and the block-end margin of the last child).
    /// Not inherited.  Default <c>none</c>.
    /// </summary>
    public string MarginTrim { get; set; } = "none";

    public string PaddingBottom
    {
        get { return _paddingBottom; }
        set
        {
            _paddingBottom = value;
            _actualPaddingBottom = double.NaN;
        }
    }

    public string PaddingLeft
    {
        get { return _paddingLeft; }
        set
        {
            _paddingLeft = value;
            _actualPaddingLeft = double.NaN;
        }
    }

    public string PaddingRight
    {
        get { return _paddingRight; }
        set
        {
            _paddingRight = value;
            _actualPaddingRight = double.NaN;
        }
    }

    public string PaddingTop
    {
        get { return _paddingTop; }
        set
        {
            _paddingTop = value;
            _actualPaddingTop = double.NaN;
        }
    }

    public string PageBreakInside { get; set; } = CssConstants.Auto;

    /// <summary>
    /// CSS Fragmentation 3 §3 <c>break-before</c>: whether a fragmentation break is forced
    /// immediately before this box. <c>auto</c> (the initial value) forces nothing; <c>page</c>,
    /// <c>always</c>, <c>left</c>, <c>right</c>, <c>recto</c> and <c>verso</c> force a page break.
    /// The legacy <c>page-break-before</c> is an alias and lands here too.
    /// </summary>
    public string BreakBefore { get; set; } = CssConstants.Auto;

    /// <summary>
    /// CSS Fragmentation 3 §3 <c>break-after</c> — the same, immediately after this box. Aliased by
    /// the legacy <c>page-break-after</c>.
    /// </summary>
    public string BreakAfter { get; set; } = CssConstants.Auto;

    /// <summary>
    /// CSS Paged Media 3 §3.4 <c>page</c>: the name of the page type this box is laid out on.
    /// <c>auto</c> (the initial value) is not inherited but resolves to the nearest ancestor's
    /// non-<c>auto</c> value — see <c>CssBox.UsedPageName</c>, which is what the fragmentation
    /// rule reads.
    /// </summary>
    public string Page { get; set; } = CssConstants.Auto;

    public string Left
    {
        get { return _left; }
        set
        {
            _left = value;

            if (Position == CssConstants.Fixed)
                _location = GetActualLocation(Left, Top);
        }
    }

    public string Top
    {
        get { return _top; }
        set
        {
            _top = value;

            if (Position == CssConstants.Fixed)
                _location = GetActualLocation(Left, Top);
        }
    }

    public string Right
    {
        get { return _right; }
        set { _right = value; }
    }

    public string Bottom
    {
        get { return _bottom; }
        set { _bottom = value; }
    }

    public string Width
    {
        get => ResolvePhysicalSize(_width, isWidth: true);
        set
        {
            _width = value;
            _actualWidth = double.NaN;
        }
    }
    public string MaxWidth
    {
        get => ResolvePhysicalBound(_maxWidth, MaxInlineSize, MaxBlockSize, "none");
        set { _maxWidth = value; _actualMaxWidth = double.NaN; }
    }
    public string MinWidth
    {
        get => ResolvePhysicalBound(_minWidth, MinInlineSize, MinBlockSize, "0");
        set { _minWidth = value; _actualMinWidth = double.NaN; }
    }
    internal bool IsMinWidthSpecified { get; set; }

    /// <summary>
    /// Resolves <see cref="MaxWidth"/> against a percentage basis, caching the
    /// result when it is basis-independent (an absolute length with no
    /// percentage term) so the repeated max-width clamps in width resolution do
    /// not re-tokenize a fixed cap such as <c>"300px"</c>. Percentage caps still
    /// resolve against the supplied basis every call.
    /// </summary>
    protected double ResolveMaxWidthLength(double basis)
        => ResolveCachedConstraintLength(MaxWidth, basis, ref _actualMaxWidth);

    /// <summary>Cached <see cref="MinWidth"/> resolution; see <see cref="ResolveMaxWidthLength"/>.</summary>
    protected double ResolveMinWidthLength(double basis)
        => ResolveCachedConstraintLength(MinWidth, basis, ref _actualMinWidth);

    private double ResolveCachedConstraintLength(string value, double basis, ref double cache)
    {
        if (!double.IsNaN(cache))
            return cache;

        // min-/max-width resolve against the containing block width (CSS2.1 §10.4), so a percentage
        // carries only the ancestor zoom and needs this box's own zoom to reach the effective factor.
        double resolved = ParseLengthWithLineHeight(value, basis, percentAgainstContainingBlock: true);

        // Only an absolute length (no percentage term) is independent of the
        // basis; cache it. Percentage / expression-with-% values differ per
        // basis and are re-resolved on every call.
        if (value != null && value.IndexOf('%') < 0)
            cache = resolved;

        return resolved;
    }
    /// <summary>
    /// True when this absolutely/fixed-positioned box has already had its
    /// <see cref="CssBoxProperties.Location"/> advanced to its final CSS
    /// <c>left</c>/<c>top</c> offset by the positioning pass, so its inline
    /// content flows at that final origin. <c>AdjustAbsolutePosition</c> must then
    /// NOT re-add the offset (it would double it). Boxes whose Location stays at
    /// the static position (e.g. native form controls) keep this false and still
    /// rely on <c>AdjustAbsolutePosition</c>.
    /// </summary>
    internal bool AbsposLocationFinalized { get; set; }

    /// <summary>
    /// The static position (CSS2.1 §10.3.7 / §10.6.4) an out-of-flow box would
    /// occupy in its inline formatting context — the inline cursor where FlowBox
    /// encountered it. Recorded by the inline layout so that when the box's block
    /// layout resolves its used position, an axis with <c>auto</c> insets falls
    /// back to this inline static position instead of the block-flow static
    /// (top of the containing block), which does not model inline placement.
    /// Null when the box was not flowed through an inline formatting context.
    /// </summary>
    internal PointF? InlineStaticPosition { get; set; }

    /// <summary>
    /// When this box is an absolutely-positioned grid item, the grid container's
    /// track-sizing pass records the item's resolved grid area here in absolute
    /// coordinates (X/Y = area origin, Width/Height = area size). CSS Grid §9
    /// makes that area — not the grid container's padding box — the box's
    /// containing block, so <see cref="CssBox.GetAbsoluteContainingBlockPaddingBox"/>
    /// returns it when set. Null for every other box.
    /// </summary>
    internal RectangleF? GridAreaContainingBlock { get; set; }

    /// <summary>
    /// CSS Grid Level 2 §7.3 (subgrid): when this box is a grid item whose
    /// <c>grid-template-columns</c> is <c>subgrid</c>, the parent grid's
    /// track-sizing pass records here the sizes (px) of the parent tracks this
    /// item's grid area spans, so the subgrid lays its own items out into the
    /// inherited tracks instead of parsing a template. Null when not a
    /// column-subgrid or not yet resolved by the parent. <see cref="SubgridRowSizes"/>
    /// is the analogous row-axis inheritance; the gaps carry the parent's gutters,
    /// which a subgridded axis adopts (CSS Grid L2 §7.3).
    /// </summary>
    internal double[] SubgridColumnSizes { get; set; }
    internal double[] SubgridRowSizes { get; set; }
    internal double? SubgridColumnGap { get; set; }
    internal double? SubgridRowGap { get; set; }

    public string Height
    {
        get => ResolvePhysicalSize(_height, isWidth: false);
        set
        {
            _height = value;
            _actualHeight = double.NaN;
        }
    }

    public string MaxHeight
    {
        get => ResolvePhysicalBound(_maxHeight, MaxBlockSize, MaxInlineSize, "none");
        set => _maxHeight = value;
    }

    public string MinHeight
    {
        get => ResolvePhysicalBound(_minHeight, MinBlockSize, MinInlineSize, "0");
        set => _minHeight = value;
    }

    private string _maxHeight = "none";
    private string _minHeight = "0";

    /// <summary>
    /// Whether the author declared <c>min-height</c> (or a logical <c>min-block-size</c>/
    /// <c>min-inline-size</c> that resolves to it) at all. The block-axis twin of
    /// <see cref="IsMinWidthSpecified"/>, and needed for the same reason: <see cref="MinHeight"/>
    /// defaults to <c>"0"</c>, so its value alone cannot tell an undeclared minimum from an
    /// explicit <c>min-height: 0</c>. CSS Flexbox §4.5 turns on exactly that distinction — an
    /// undeclared minimum on a flex item is <c>auto</c> and floors the item at its content, while
    /// <c>min-height: 0</c> is the idiom that deliberately lets a column flex item shrink past it.
    /// </summary>
    /// <remarks>
    /// The logical halves need no flag of their own: <see cref="MinBlockSize"/> and
    /// <see cref="MinInlineSize"/> default to the empty string rather than to <c>"0"</c>, so a
    /// value there already <em>is</em> the declaration. Which of the two lands on the physical
    /// block axis follows the writing mode, exactly as <see cref="MinHeight"/> resolves it.
    /// </remarks>
    internal bool IsMinHeightSpecified
    {
        get => _isMinHeightSpecified
            || !string.IsNullOrEmpty(IsVerticalWritingMode(WritingMode) ? MinInlineSize : MinBlockSize);
        set => _isMinHeightSpecified = value;
    }

    private bool _isMinHeightSpecified;

    /// <summary>
    /// CSS Logical 1 §4: the flow-relative minimum and maximum sizes. Stored on their own rather
    /// than folded into the physical longhands, for the same reason
    /// <see cref="BlockSize"/> is: which physical axis each names depends on the box's writing
    /// mode, and the mode is not settled while the cascade is applying declarations.
    /// </summary>
    public string MinBlockSize
    {
        get => _minBlockSize;
        set { _minBlockSize = value; _actualMinWidth = double.NaN; }
    }

    /// <inheritdoc cref="MinBlockSize"/>
    public string MaxBlockSize
    {
        get => _maxBlockSize;
        set { _maxBlockSize = value; _actualMaxWidth = double.NaN; }
    }

    /// <inheritdoc cref="MinBlockSize"/>
    public string MinInlineSize
    {
        get => _minInlineSize;
        set { _minInlineSize = value; _actualMinWidth = double.NaN; }
    }

    /// <inheritdoc cref="MinBlockSize"/>
    public string MaxInlineSize
    {
        get => _maxInlineSize;
        set { _maxInlineSize = value; _actualMaxWidth = double.NaN; }
    }

    private string _minBlockSize = string.Empty;
    private string _maxBlockSize = string.Empty;
    private string _minInlineSize = string.Empty;
    private string _maxInlineSize = string.Empty;

    /// <summary>CSS Sizing 4 <c>aspect-ratio</c> (raw value, e.g. <c>1 / 1</c>).
    /// Honoured for in-flow block-level boxes with an auto block size, which
    /// derive their used height from their used width and this ratio
    /// (<see cref="CssBox.TryResolveAspectRatioBlockHeight"/>).</summary>
    public string AspectRatio { get; set; } = "auto";

    /// <summary>
    /// CSS Images 3 §4: the <b>natural (intrinsic) size</b> of an atomic inline-level box that is a
    /// replaced element sized from something other than a decoded image — currently a
    /// <c>&lt;canvas&gt;</c>, whose bitmap is its <c>width</c>/<c>height</c> content attributes
    /// (HTML §4.12.5, defaulting to 300×150). <see langword="null"/> for every other box, which is
    /// what keeps the replaced sizing path off for the overwhelming majority.
    /// </summary>
    /// <remarks>
    /// This is what separates a replaced box from an ordinary <c>inline-block</c> that merely has a
    /// <c>width</c> and a <c>height</c>: an axis the author left <c>auto</c> takes the natural size
    /// rather than shrink-to-fit, and the two axes stay tied by the natural ratio when
    /// <c>min-*</c>/<c>max-*</c> clamp one of them (CSS2.1 §10.4 — see
    /// <see cref="ReplacedBoxSizing"/>). The renderer sets it during style resolution; layout never
    /// derives it.
    /// </remarks>
    public SizeF? IntrinsicReplacedSize { get; set; }

    /// <summary>
    /// CSS Images 3 §5.5 <c>object-fit</c> — how a replaced element's content is scaled into its
    /// content box. Resolved at paint time by <see cref="IR.ObjectFitPlacement"/>; it moves and
    /// resizes the drawn content only, never the box, so layout does not read it.
    /// </summary>
    public string ObjectFit { get; set; } = "fill";

    /// <summary>
    /// CSS Images 3 §5.6 <c>object-position</c> — where that content sits inside the content box
    /// once <see cref="ObjectFit"/> has sized it.
    /// </summary>
    public string ObjectPosition { get; set; } = "50% 50%";

    /// <summary>
    /// CSS2.1 §9.2.1.1: set on an <b>inline</b> element's box that the block-inside-inline
    /// correction has blockified in order to break it around a block-level child. The element is
    /// still inline as far as CSS is concerned — the block-level <c>display</c> is an artefact of
    /// how the split is modelled — so it is not a containing block a percentage resolves against,
    /// any more than the anonymous blocks the same split creates are.
    /// </summary>
    /// <remarks>
    /// WPT <c>css-sizing/block-image-percentage-max-height-inside-inline</c> is exactly this shape:
    /// a <c>display: block</c> <c>&lt;img&gt;</c> inside a <c>&lt;span&gt;</c> inside a
    /// <c>height: 100px</c> <c>&lt;div&gt;</c>. Stopping at the blockified span made the image's
    /// <c>max-height: 100%</c> resolve against an indefinite block size, so §10.7 turned it into
    /// <c>none</c> and the image kept its 1000px height.
    /// </remarks>
    public bool IsBlockifiedInlineSplit { get; set; }

    public string InlineSize
    {
        get => _inlineSize;
        set
        {
            _inlineSize = value;
            _actualWidth = double.NaN;
            _actualHeight = double.NaN;
        }
    }

    public string BlockSize
    {
        get => _blockSize;
        set
        {
            _blockSize = value;
            _actualWidth = double.NaN;
            _actualHeight = double.NaN;
        }
    }

    public string BackgroundColor
    {
        get => ResolveCssVariables(_backgroundColor);
        set
        {
            // CSS2.1 §6.2.1: `inherit` resolves to the parent's computed value. Fold it into the
            // parent's already-cascaded BackgroundColor here (like FontSize) so a chain of
            // `background-color: inherit` boxes each inherits the resolved colour — otherwise the
            // literal `inherit` reaches GetActualColor, which folds an unrecognised value to opaque
            // black (WPT css-view-transitions nested tests inherit a colour through group wrappers).
            if (value != null && value.Equals("inherit", StringComparison.OrdinalIgnoreCase) && GetParent() is { } bgParent)
                value = bgParent.BackgroundColor;
            _backgroundColor = value;
            _actualBackgroundColor = BColor.Empty;
        }
    }
    /// <remarks>
    /// The CSS Color 4 rewrite has to reach the gradient stops too, and a gradient's colours are
    /// never seen by <c>GetActualColor</c> — the paint walker parses the whole
    /// <c>linear-gradient(…)</c> string itself and resolves each stop with the canonical parser. A
    /// stop it cannot read is *dropped* rather than folded to black, so a single-stop
    /// <c>linear-gradient(to right in srgb, color(srgb none 0.5 0.5))</c> lost its only stop and
    /// painted nothing at all (WPT
    /// <c>css-images/gradient/gradient-single-stop-none-interpolation</c>). Normalising here hands
    /// the walker a stop list it already understands.
    /// </remarks>
    public string BackgroundImage
    {
        get
        {
            var value = ResolveCssVariables(_backgroundImage);
            return IR.CssColor4.NormalizeColorFunctions(value, ResolveCurrentColorFor(value));
        }
        set => _backgroundImage = value;
    }

    /// <summary>
    /// This element's resolved <c>color</c>, but only when <paramref name="value"/> actually names
    /// <c>currentcolor</c> inside a colour function — resolving it unconditionally would run the
    /// colour cascade for every background image in the document, and does so at points in box
    /// construction where there is no layout environment to run it against. Null when there is
    /// nothing to resolve or no basis to resolve it. Overridden by <c>CssBox</c>; the base has no
    /// cascade to read.
    /// </summary>
    protected virtual string? ResolveCurrentColorFor(string? value) => null;

    /// <summary>The cheap half of <see cref="ResolveCurrentColorFor"/>, shared by the override: a
    /// value with no parenthesised <c>currentcolor</c> in it can never need one.</summary>
    private protected static bool MayNeedCurrentColor(string? value) =>
        !string.IsNullOrEmpty(value) &&
        value.IndexOf('(') >= 0 &&
        value.IndexOf("currentcolor", StringComparison.OrdinalIgnoreCase) >= 0;

    public string BackgroundPosition { get; set; } = "0% 0%";
    public string BackgroundRepeat { get; set; } = "repeat";
    public string BackgroundAttachment { get; set; } = "scroll";
    public string BackgroundOrigin { get; set; } = "padding-box";
    public string BackgroundSize { get; set; } = "auto";
    public string BackgroundGradient { get; set; } = "none";
    public string BackgroundGradientAngle { get; set; } = "90";

    // CSS Animations §3: Animation properties for static keyframe resolution.
    public string AnimationName { get; set; } = "none";
    public string AnimationDuration { get; set; } = "0s";
    public string AnimationTimingFunction { get; set; } = "ease";
    public string AnimationDelay { get; set; } = "0s";
    public string AnimationIterationCount { get; set; } = "1";
    public string AnimationDirection { get; set; } = "normal";
    public string AnimationFillMode { get; set; } = "none";
    public string AnimationPlayState { get; set; } = "running";

    public string Color
    {
        get { return ResolveCssVariables(_color); }
        set
        {
            _color = value;
            _actualColor = BColor.Empty;
            ColorSpecified = true;
        }
    }

    /// <summary>
    /// Whether a declaration set this box's <c>color</c>, as opposed to the box taking its
    /// parent's through <see cref="InheritStyle"/>. Every specified route writes through the
    /// <see cref="Color"/> setter — the cascade projection, the shorthand expansion and the
    /// presentation attributes (<c>&lt;font color&gt;</c>, <c>text=</c>) — while inheritance
    /// assigns <c>_color</c> directly, which is what makes the setter a reliable place to record
    /// it. Read by <see cref="TablesInheritColorFromBodyQuirk"/>, which may only replace a colour
    /// the box inherited.
    /// </summary>
    internal bool ColorSpecified { get; private set; }

    /// <summary>
    /// Replaces the box's inherited <c>color</c> without marking it specified — the write
    /// inheritance itself would have made, had it come from somewhere else.
    /// </summary>
    internal void SetInheritedColor(string value)
    {
        _color = value;
        _actualColor = BColor.Empty;
    }

    /// <summary>The initial value of <c>color</c> — what a box that inherited nothing carries.</summary>
    internal const string InitialColor = "black";

    public string Content { get; set; } = "normal";
    public string Display { get; set; } = "inline";
    public string Direction { get; set; } = "ltr";
    // CSS Basic UI 'appearance'. Defaults to "auto" so a UA-styled control (e.g. a list box) keeps its
    // native rendering unless the author opts out with 'appearance: none'.
    public string Appearance { get; set; } = "auto";
    public string EmptyCells { get; set; } = "show";
    public string CaptionSide { get; set; } = "top";
    public string Float { get; set; } = "none";
    public string Clear { get; set; } = "none";
    public string Position { get; set; } = "static";

    // CSS Viewport `zoom`: the cascaded per-element zoom factor, surfaced on the box for the native zoom
    // model (HtmlBridge complexity-reduction roadmap Phase 5, the CSS-`zoom`/visual-viewport endgame).
    // Populated by CssUtils.SetPropertyValue from the declared cascade; consumed only when
    // NativeZoom.Enabled (via EffectiveZoom), so it is inert by default and the HtmlBridge serialization
    // bake continues to carry zoom as it does today. Initial value `normal` (== factor 1).
    public string Zoom { get; set; } = "normal";

    /// <summary>
    /// This box's specified <c>zoom</c> as a positive factor: a number (<c>zoom: 2</c>), a percentage
    /// (<c>zoom: 150%</c> → 1.5), or 1.0 for the initial/<c>normal</c>/<c>inherit</c>/unparseable value.
    /// Not itself inherited — each element has its own zoom; the multiplicative compounding across
    /// ancestors is expressed by <see cref="EffectiveZoom"/>.
    /// </summary>
    internal double OwnZoom
    {
        get
        {
            var z = Zoom?.Trim();
            if (string.IsNullOrEmpty(z)
                || z.Equals("normal", System.StringComparison.OrdinalIgnoreCase)
                || z.Equals("inherit", System.StringComparison.OrdinalIgnoreCase))
                return 1.0;
            if (z.EndsWith('%')
                && double.TryParse(z[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct)
                && pct > 0)
                return pct / 100.0;
            if (double.TryParse(z, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num) && num > 0)
                return num;
            return 1.0;
        }
    }

    /// <summary>
    /// The compounded (effective) zoom applied to this box's used values: the product of this box's own
    /// <see cref="OwnZoom"/> and every ancestor's, per CSS <c>zoom</c> (the factor compounds down the
    /// tree). Always <c>1.0</c> unless <see cref="NativeZoom.Enabled"/>, so the engine is zoom-neutral by
    /// default and this foundation is inert until the used-value increments consume it.
    /// </summary>
    public double EffectiveZoom =>
        !NativeZoom.Enabled ? 1.0 : (GetParent()?.EffectiveZoom ?? 1.0) * OwnZoom;

    /// <summary>
    /// The CSS <em>computed</em> font size in points — unaffected by <c>zoom</c> (which scales only used
    /// values, CSS Viewport). Font-size <c>%</c>/<c>em</c>/<c>larger</c>/<c>smaller</c> and inheritance
    /// resolve against this, so the ancestor zoom is applied exactly once (via
    /// <see cref="EffectiveZoom"/>) to the <em>used</em> size (<see cref="ActualFont"/>.Size) rather than
    /// compounding through the font-size chain. Only consulted on the native-zoom path (parent
    /// EffectiveZoom ≠ 1); off it, the existing <c>ActualFont.Size</c>-based resolution is unchanged.
    /// </summary>
    internal double ComputedFontSizePoints
    {
        get
        {
            if (string.IsNullOrEmpty(FontSize))
                return CssConstants.FontSize;

            double parentSize = GetParent() != null ? GetParent().ComputedFontSizePoints : CssConstants.FontSize;
            var fsize = FontSize switch
            {
                CssConstants.Medium => CssConstants.FontSize,
                CssConstants.XXSmall => CssConstants.FontSize - 4,
                CssConstants.XSmall => CssConstants.FontSize - 3,
                CssConstants.Small => CssConstants.FontSize - 2,
                CssConstants.Large => CssConstants.FontSize + 2,
                CssConstants.XLarge => CssConstants.FontSize + 3,
                CssConstants.XXLarge => CssConstants.FontSize + 4,
                CssConstants.Smaller => parentSize - 2,
                CssConstants.Larger => parentSize + 2,
                _ when IsMathFontSize(FontSize) => parentSize,
                _ => ResolveFontSizeLengthToPoints(FontSize, parentSize),
            };

            return fsize <= 0 ? 0.001 : fsize;
        }
    }

    /// <summary>
    /// <c>font-size: math</c> (MathML Core §the-math-script-level-property, CSS Fonts 4).
    /// <para>
    /// It computes to the inherited size scaled by the math scaling factor, and that factor is
    /// driven entirely by a <em>change</em> in <c>math-depth</c> — with no change it is 1, so the
    /// keyword is exactly <c>1em</c>. Broiler models no math depth, so the keyword is always that.
    /// </para>
    /// <para>
    /// Without this arm the keyword fell through to the length parser, which reads an unrecognised
    /// token as <c>0</c>, and the zero clamp turned that into a 0.001pt font — so it did not merely
    /// fail to scale, it collapsed the element and everything under it. WPT
    /// <c>css/css-fonts/math-script-level-and-math-style/font-size-math-001.tentative</c> (issue
    /// #1538 problem 30) nests it inside a chain of relative sizes precisely to catch that: its
    /// reference is the same document with <c>math</c> written as <c>1em</c>.
    /// </para>
    /// </summary>
    private static bool IsMathFontSize(string fontSize) =>
        fontSize.Equals("math", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A <c>font-size</c> length as a point count, resolved against a parent size also given in
    /// points.
    /// </summary>
    /// <remarks>
    /// The layout font is built at a point size, so this resolution has to end in points; it gets
    /// there by resolving the length in CSS pixels — the unit every entry in the parser's unit
    /// table is expressed in — and converting once at the end.
    /// <para>
    /// Asking the parser for points instead only ever worked for <c>px</c>, the single unit whose
    /// table entry consults that flag. Every other absolute unit came back in pixels and was then
    /// read as points, so it landed <c>96/72</c> too large: <c>1rem</c> drew at 21.3px rather than
    /// 16, and with it every <c>rem</c>-sized element on a page that sizes its type that way — on
    /// <c>www.mediawiki.org</c>, which sizes all of it that way, every line of the article broke a
    /// third of the way early. <c>em</c>, <c>%</c> and the other font-relative units were right
    /// only because their basis was passed in already-resolved, and they stay right here: the
    /// basis is converted with them.
    /// </para>
    /// </remarks>
    private static double ResolveFontSizeLengthToPoints(string fontSize, double parentSizePoints)
    {
        double parentSizePx = parentSizePoints * CssMetrics.PtToPx;

        double px = CssLengthParser.ParseLength(
            fontSize, parentSizePx, parentSizePx, null, fontAdjust: false, returnPoints: false);

        return px * CssMetrics.PxToPt;
    }

    // CSS Anchor Positioning: the cascaded values are surfaced on the box so the
    // layout engine's anchor-placement post-pass can read them (HtmlBridge
    // complexity-reduction roadmap Phase 5 item 3, P5.8b). They are populated by
    // CssUtils.SetPropertyValue from the declared cascade like any other longhand;
    // the engine does not yet consume them (that is gated behind the P5.8c post-pass).
    public string AnchorName { get; set; } = "none";
    public string PositionAnchor { get; set; } = "auto";
    public string PositionArea { get; set; } = "none";
    public string PositionTry { get; set; } = "normal";
    public string PositionTryFallbacks { get; set; } = "none";
    // Default "normal" is a sentinel for UNSET (the cascade only emits position-visibility
    // when authored, since it is not in CssComputedDefaults). It lets the visibility pass
    // distinguish an unset target — which, when it has position-area + position-anchor, takes
    // an implicit "anchors-visible" (the position-visibility-initial reftest) — from an
    // explicit "always" (position-visibility-remove-anchors-visible), which must never hide.
    public string PositionVisibility { get; set; } = "normal";

    /// <summary>
    /// Runtime flag set by the native anchor post-pass' <c>position-visibility</c> resolution
    /// (P5.8d.2b) to suppress an anchor-positioned box whose anchor is not visible (scrolled out
    /// of an intervening clip container, <c>visibility:hidden</c>, or — for <c>anchors-valid</c> —
    /// missing). Unlike <c>display:none</c> it is applied <em>after</em> layout, so the box keeps
    /// its geometry but <see cref="Broiler.Layout.IR.FragmentTreeBuilder"/> excludes it (and its
    /// subtree) from the paint fragment tree. Not a CSS property — never cascaded or copied.
    /// </summary>
    public bool PositionHidden { get; set; }

    /// <summary>
    /// Runtime flag marking a <c>&lt;foreignObject&gt;</c> box that
    /// <see cref="SvgForeignObjectBoxes"/> has already lifted out of its hidden SVG subtree and
    /// placed. The placement folds the ancestor <c>translate()</c> chain into the box's own
    /// <c>left</c>/<c>top</c> and re-parents the box onto the viewport, so those ancestors are no
    /// longer above it — running the pass a second time would recompute the offset as zero and move
    /// the box. This is what makes the pass idempotent. Not a CSS property — never cascaded or
    /// copied.
    /// </summary>
    public bool SvgForeignObjectPlaced { get; set; }

    public string LineHeight
    {
        get { return _lineHeight; }
        set
        {
            // CSS2.1 §10.8: Preserve "normal" and "inherit" keywords as-is
            if (string.IsNullOrEmpty(value) || value == "normal" || value == "inherit")
            {
                _lineHeight = value ?? "normal";
                return;
            }

            // Unitless numbers (line-height: <number>) should be treated as a
            // multiplier of the element's font-size. Store as "Nem" so
            // ActualLineHeight resolves with the correct em factor at layout time,
            // avoiding precision loss from premature conversion at parse time
            // (CSS2.1 §10.8.1).
            if (!value.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("em", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("ex", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("cm", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("mm", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("in", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith("pc", StringComparison.OrdinalIgnoreCase) &&
                !value.EndsWith('%') &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                _lineHeight = value + "em";
                return;
            }

            // CSS2.1 §10.8: For explicit length values (px, em, pt, etc.),
            // store the raw value and let ActualLineHeight resolve it at
            // layout time when the element's font-size is finalized.
            _lineHeight = value;
        }
    }

    public string VerticalAlign { get; set; } = "baseline";

    public string TextIndent
    {
        get { return _textIndent; }
        set { _textIndent = value; }
    }

    public string TextAlign { get; set; } = string.Empty;

    // CSS Text 4 §text-align-last: alignment of the block's last line (and lines
    // ending in a forced break).  Inherited; initial 'auto' (follows text-align,
    // except a justified block leaves its last line start-aligned).
    public string TextAlignLast { get; set; } = string.Empty;

    public string TextDecoration { get; set; } = string.Empty;
    public string TextDecorationStyle { get; set; } = "solid";
    public string TextDecorationColor
    {
        get => _textDecorationColor;
        set
        {
            _textDecorationColor = value;
            _actualTextDecorationColor = BColor.Empty;
        }
    }
    public string WhiteSpace { get; set; } = "normal";

    /// <summary>
    /// CSS Overflow 4 §5 <c>line-clamp</c>: the shorthand that clamps a block
    /// container to a number of lines. <c>none</c> (the initial value) does not
    /// clamp; a positive integer sets <c>max-lines</c> to that count,
    /// <c>block-ellipsis</c> to <c>auto</c> and <c>continue</c> to
    /// <c>discard</c>. Not inherited — the clamp applies to the block container
    /// it is set on, and the lines it counts are the ones in that container's
    /// own block formatting context (including those of its descendants).
    /// </summary>
    public string LineClamp { get; set; } = CssConstants.None;

    /// <summary>
    /// CSS Overflow 3 §6 <c>-webkit-line-clamp</c>: the legacy WebKit spelling.
    /// It clamps only when the box is a legacy <c>-webkit-box</c> /
    /// <c>-webkit-inline-box</c> whose <c>-webkit-box-orient</c> is vertical —
    /// unlike <see cref="LineClamp"/>, which needs no such opt-in. See
    /// <see cref="CssBox.ResolveLineClamp"/>.
    /// </summary>
    public string WebkitLineClamp { get; set; } = CssConstants.None;

    /// <summary>
    /// CSS Overflow 4 §5 <c>max-lines</c>: the longhand <see cref="LineClamp"/>
    /// expands to. Set on its own it clamps the line count without implying an
    /// ellipsis, so <see cref="BlockEllipsis"/> stays at its own initial value.
    /// </summary>
    public string MaxLines { get; set; } = CssConstants.None;

    /// <summary>
    /// CSS Overflow 4 §4 <c>block-ellipsis</c>: what to place at the end of the
    /// last line a clamp keeps. <c>none</c> places nothing, <c>auto</c> places
    /// U+2026 HORIZONTAL ELLIPSIS, and a <c>&lt;string&gt;</c> places that
    /// string. Inherited, per spec.
    /// </summary>
    public string BlockEllipsis { get; set; } = CssConstants.None;

    /// <summary>
    /// The legacy <c>-webkit-box-orient</c>: <c>horizontal</c>/<c>inline-axis</c>
    /// (the initial value) stacks a <c>-webkit-box</c>'s children along the
    /// inline axis, <c>vertical</c>/<c>block-axis</c> along the block axis.
    /// Inherited, matching the original WebKit property, and read both when
    /// approximating the legacy box display and when deciding whether
    /// <see cref="WebkitLineClamp"/> applies.
    /// </summary>
    public string WebkitBoxOrient { get; set; } = "horizontal";

    /// <summary>
    /// The legacy display keyword (<c>-webkit-box</c> / <c>-webkit-inline-box</c>)
    /// this box's <see cref="Display"/> was mapped from, or <c>null</c>. Kept
    /// because the mapping depends on <see cref="WebkitBoxOrient"/>, which the
    /// cascade often applies after <c>display</c>, and because
    /// <see cref="CssBox.ResolveLineClamp"/> needs to know the box opted into the
    /// legacy <c>-webkit-line-clamp</c> after the keyword itself is gone.
    /// </summary>
    public string LegacyWebkitBoxDisplay { get; set; }

    /// <summary>
    /// Set by <see cref="CssBox.ApplyLineClamp"/> on a box whose content was
    /// entirely discarded by a line clamp. The fragment tree skips such a box
    /// and its subtree, so nothing it would have painted — text, background,
    /// border — reaches the display list, which is what <c>continue: discard</c>
    /// asks for. Layout still ran for it: the clamp is applied after the
    /// container's children are laid out, because the line count it cuts on is
    /// only known then.
    /// </summary>
    public bool ClampedAway { get; set; }

    /// <summary>
    /// CSS Text 3 §2.1 <c>text-transform</c>. An inherited property applied to a
    /// box's text when its words are parsed (see <see cref="CssBox.ParseToWords"/>).
    /// The default <c>none</c> leaves text unchanged.
    /// </summary>
    public string TextTransform { get; set; } = "none";

    public string Visibility { get; set; } = "visible";

    /// <summary>
    /// CSS Image Animation 1 <c>image-animation</c>: whether the animated images this box paints
    /// advance with the document's timeline (<c>normal</c>, the initial value), hold the frame
    /// they had reached (<c>paused</c>), or reset to the first one (<c>stopped</c>). Inherited,
    /// so setting it on the root governs the whole document unless a descendant overrides it.
    /// Read by <see cref="CssBox.ImagePresentationTime"/>, which is what the box's image loads
    /// are pinned to.
    /// </summary>
    public string ImageAnimation { get; set; } = "normal";

    public string WordSpacing
    {
        get { return _wordSpacing; }
        set { _wordSpacing = value; }
    }

    public string WordBreak { get; set; } = "normal";
    public string LineBreak { get; set; } = "auto";
    public string Opacity { get; set; } = "1";
    public string ZIndex { get; set; } = CssConstants.Auto;

    // CSS Position 4 §top-layer: non-null when this box is in the top layer (an open modal
    // <dialog>, an open popover, or a synthesized ::backdrop). Projected onto
    // Fragment.TopLayerOrder so the paint lifts it above ordinary stacking. Carries the order for
    // boxes the renderer *generates* (a native ::backdrop has no element to hold the
    // data-broiler-top-layer attribute FragmentTreeBuilder reads for stamped elements); null for
    // ordinary boxes, which stack normally.
    public int? TopLayerOrder { get; set; }

    // The shadow shorthands carry a <color> the paint walker parses out of the whole string rather
    // than through GetActualColor, so they need the same CSS Color 4 rewrite BackgroundImage does.
    public string BoxShadow
    {
        get => IR.CssColor4.NormalizeColorFunctions(_boxShadow, ResolveCurrentColorFor(_boxShadow));
        set => _boxShadow = value;
    }

    public string TextShadow
    {
        get => IR.CssColor4.NormalizeColorFunctions(_textShadow, ResolveCurrentColorFor(_textShadow));
        set => _textShadow = value;
    }

    private string _boxShadow = "none";
    private string _textShadow = "none";
    public string MixBlendMode { get; set; } = "normal";
    public string BackgroundBlendMode { get; set; } = "normal";
    public string Filter { get; set; } = "none";
    public string Isolation { get; set; } = "auto";
    public string BoxSizing { get; set; } = "content-box";

    public string BackgroundClip
    {
        get
        {
            if (_backgroundClip.Equals("inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null)
                return GetParent().BackgroundClip;

            return _backgroundClip;
        }
        set
        {
            _backgroundClip = value ?? "border-box";
        }
    }

    public string ClipPath
    {
        get => _clipPath;
        set => _clipPath = value ?? "none";
    }

    /// <summary>
    /// CSS 2.1 §11.1.2 <c>clip</c>, as written: <c>auto</c>, or
    /// <c>rect(&lt;top&gt;, &lt;right&gt;, &lt;bottom&gt;, &lt;left&gt;)</c> — the legacy spelling
    /// of a rectangular clip on an absolutely positioned element's border box.
    /// <see cref="Broiler.Layout.IR.ClipRect"/> resolves it against that box.
    /// </summary>
    public string Clip
    {
        get => _clip;
        set => _clip = value ?? "auto";
    }

    /// <summary>
    /// CSS Containment Module Level 2: the <c>contain</c> property.
    /// Values include <c>none</c>, <c>strict</c>, <c>content</c>,
    /// <c>size</c>, <c>layout</c>, <c>style</c>, <c>paint</c>,
    /// or a space-separated combination of the last four keywords.
    /// Used by background propagation (CSS Backgrounds §2.11.1):
    /// <c>contain: paint</c> on html or body suppresses canvas
    /// background propagation.
    /// </summary>
    public string Contain { get; set; } = "none";

    /// <summary>
    /// CSS Sizing 4 §5: <c>contain-intrinsic-width</c> — the size a size-contained box reports as
    /// its intrinsic inline extent in a horizontal writing mode, standing in for the contents that
    /// containment made unobservable. <c>none</c> (the initial value) leaves that extent at zero.
    /// </summary>
    /// <remarks>
    /// Only read when size containment actually applies; without it a box is sized by its contents
    /// and this says nothing. The <c>auto</c> prefix of the grammar (<c>auto &lt;length&gt;</c>) is
    /// parsed and dropped: it asks for the element's *last remembered size* when it has been
    /// rendered before, which needs the skipped-contents bookkeeping <c>content-visibility: auto</c>
    /// would bring, and the stated length is what the spec falls back to until then — the only
    /// state a still render ever observes.
    /// </remarks>
    public string ContainIntrinsicWidth
    {
        get => ResolveContainIntrinsicAxis(_containIntrinsicWidth, isWidth: true);
        set => _containIntrinsicWidth = value;
    }

    /// <summary>CSS Sizing 4 §5: <c>contain-intrinsic-height</c>; see <see cref="ContainIntrinsicWidth"/>.</summary>
    public string ContainIntrinsicHeight
    {
        get => ResolveContainIntrinsicAxis(_containIntrinsicHeight, isWidth: false);
        set => _containIntrinsicHeight = value;
    }

    /// <summary>
    /// Picks the <c>contain-intrinsic-*</c> value for one axis of the frame the box is laid out
    /// in, following <see cref="ResolvePhysicalSize"/> rather than
    /// <see cref="ResolvePhysicalBound"/>.
    /// </summary>
    /// <remarks>
    /// The distinction is the vertical-flow prototype's logical frame: a transposed box is laid out
    /// horizontally with its <em>inline</em> extent as the frame width and its <em>block</em> extent
    /// as the frame height, and the rotation swaps them into physical space afterwards. These values
    /// are consumed by the sizing code inside that frame — the shrink-to-fit width in
    /// <c>GetMinMaxWidth</c>, the atomic-inline height in <c>FlowInlineBlock</c> — so they have to
    /// arrive in frame terms, exactly as <c>Width</c> and <c>Height</c> do. Mapping them the way
    /// <c>min-width</c> is mapped instead put <c>contain-intrinsic-inline-size</c> on the physical
    /// width and the rotation then turned it into the height (WPT
    /// <c>contain-intrinsic-size-logical-003</c>'s eight <c>vertical-lr</c> cases come out
    /// transposed).
    /// </remarks>
    private string ResolveContainIntrinsicAxis(string physical, bool isWidth)
    {
        static bool Declared(string value) => !string.IsNullOrEmpty(value);

        if (IsVerticalWritingMode(WritingMode)
            && VerticalFlowPrototype.Enabled
            && WillBeVerticalTransposed())
        {
            string frameLogical = isWidth ? ContainIntrinsicInlineSize : ContainIntrinsicBlockSize;
            if (Declared(frameLogical))
                return frameLogical;

            string swappedPhysical = isWidth ? _containIntrinsicHeight : _containIntrinsicWidth;
            return Declared(swappedPhysical) ? swappedPhysical : physical;
        }

        if (Declared(physical))
            return physical;

        bool vertical = IsVerticalWritingMode(WritingMode);
        string logical = isWidth
            ? (vertical ? ContainIntrinsicBlockSize : ContainIntrinsicInlineSize)
            : (vertical ? ContainIntrinsicInlineSize : ContainIntrinsicBlockSize);

        return Declared(logical) ? logical : physical;
    }

    /// <summary>
    /// CSS Sizing 4 §5: the flow-relative spellings, stored on their own for the same reason
    /// <see cref="MinBlockSize"/> is — which physical axis each names depends on the writing mode,
    /// and the mode is not settled while the cascade is applying declarations. Empty means
    /// undeclared, which is what lets the physical longhand above win when both are absent.
    /// </summary>
    public string ContainIntrinsicInlineSize { get; set; } = "";

    /// <summary>CSS Sizing 4 §5: <c>contain-intrinsic-block-size</c>; see <see cref="ContainIntrinsicInlineSize"/>.</summary>
    public string ContainIntrinsicBlockSize { get; set; } = "";

    // Empty rather than "none" so an undeclared physical longhand can be told from a declared
    // `none`, which is what lets a flow-relative one win the lookup above. Both read as zero.
    private string _containIntrinsicWidth = "";
    private string _containIntrinsicHeight = "";

    /// <summary>
    /// CSS Color Adjust Module Level 1: the <c>color-scheme</c> property.
    /// A space-separated list of the color schemes the element can render in
    /// (<c>normal</c>, <c>light</c>, <c>dark</c>, optionally prefixed by
    /// <c>only</c>). Used by canvas background painting (CSS Color Adjust
    /// §2.3): when the root's used color scheme is <c>dark</c>, the canvas is
    /// painted the UA dark backdrop colour rather than white.
    /// </summary>
    public string ColorScheme { get; set; } = "normal";

    /// <summary>
    /// CSS Containment Module Level 2: the <c>content-visibility</c> property.
    /// Values: <c>visible</c> (default), <c>hidden</c>, <c>auto</c>.
    /// <c>hidden</c> skips the element's contents (they are not painted and are
    /// subject to layout/size containment), while the element's own box —
    /// background, border, and box-model size — still renders.
    /// </summary>
    public string ContentVisibility { get; set; } = "visible";
    public string Transform { get; set; } = "none";

    /// <summary>
    /// CSS Transforms 1 §8: <c>transform-origin</c> — the point <see cref="Transform"/> is applied
    /// about, relative to the box's border box. The initial value is the box's centre.
    /// </summary>
    /// <remarks>
    /// Kept as the declaration rather than a resolved point, because the point depends on the used
    /// border box and that is not settled while the cascade is applying declarations.
    /// <see cref="IR.CssTransformOrigin"/> resolves it once the box has one.
    /// </remarks>
    public string TransformOrigin { get; set; } = "50% 50%";

    /// <summary>
    /// CSS Will Change Module Level 1: the <c>will-change</c> property. A
    /// comma-separated hint list (<c>auto</c> by default). Consumed only by the
    /// native anchor-placement containing-block resolution: <c>will-change: transform</c>
    /// (and other values that would create one) establishes a containing block for
    /// absolutely-positioned descendants — see
    /// <see cref="CssBox.EstablishesNonPositionAbsPosContainingBlock"/>. No other layout
    /// or paint behaviour reads it today.
    /// </summary>
    public string WillChange { get; set; } = "auto";

    public string FlexDirection { get; set; } = "row";
    public string FlexGrow { get; set; } = "0";
    public string FlexShrink { get; set; } = "1";
    public string FlexBasis { get; set; } = "auto";
    public string FlexWrap { get; set; } = "nowrap";

    /// <summary>
    /// CSS Display §3 / Flexbox §5.4 <c>order</c>: an integer (initial <c>0</c>) that places a flex
    /// or grid item into an ordinal group. Items are laid out and painted in <em>order-modified
    /// document order</em> — ascending <c>order</c>, document order within a group — rather than
    /// plain document order.
    /// </summary>
    public string Order { get; set; } = "0";

    // CSS Box Alignment §8: the initial value of justify-content is 'normal',
    // not 'flex-start'. In a flex container 'normal' behaves as 'flex-start'
    // (packed at the main-start edge), so flex layout is unchanged; in a grid
    // container 'normal' triggers the default stretch of auto tracks, which
    // 'flex-start' (a packing distribution) suppresses.
    public string JustifyContent { get; set; } = "normal";
    public string JustifyItems { get; set; } = "normal";
    public string AlignItems { get; set; } = "stretch";
    public string AlignContent { get; set; } = "normal";
    public string JustifySelf { get; set; } = "auto";
    public string AlignSelf { get; set; } = "auto";
    public string UnicodeBidi { get; set; } = "normal";
    public string WritingMode
    {
        get => _writingMode;
        set
        {
            _writingMode = value;
            _actualWidth = double.NaN;
            _actualHeight = double.NaN;
        }
    }
    public string ColumnCount { get; set; } = "auto";
    public string ColumnWidth { get; set; } = "auto";
    public string ColumnFill { get; set; } = "balance";
    public string RowGap { get; set; } = "normal";
    public string ColumnGap { get; set; } = "normal";
    public string BreakInside { get; set; } = "auto";
    public string GridRow { get; set; } = "auto";
    public string GridColumn { get; set; } = "auto";
    // CSS Grid Level 1 §7/§8: explicit track lists and implicit-track/flow
    // controls consumed by the definite-track grid layout pass
    // (CssBoxGrid.TryApplyGridTrackLayout). "none"/empty means no explicit grid.
    public string GridTemplateColumns { get; set; } = "none";
    public string GridTemplateRows { get; set; } = "none";
    public string GridAutoFlow { get; set; } = "row";
    public string GridAutoRows { get; set; } = "auto";
    public string GridAutoColumns { get; set; } = "auto";
    public string FontFamily { get; set; }

    /// <summary>
    /// Raw CSS <c>font-feature-settings</c> value (e.g. <c>"ss05" on, "liga" off</c>),
    /// inherited.  Resolved to enabled OpenType feature tags by
    /// <see cref="GetEnabledFontFeatureTags"/>.
    /// </summary>
    public string FontFeatureSettings { get; set; }

    /// <summary>
    /// Raw CSS <c>font-variant-alternates</c> value (e.g.
    /// <c>styleset(crossed-doubleu)</c>), inherited.  Resolved against
    /// <c>@font-feature-values</c> into concrete feature tags after the cascade.
    /// </summary>
    public string FontVariantAlternates { get; set; }

    /// <summary>
    /// Parses <see cref="FontFeatureSettings"/> into a space-separated list of
    /// the OpenType feature tags that are switched on, or <c>null</c> when none.
    /// </summary>
    protected string GetEnabledFontFeatureTags()
    {
        string value = FontFeatureSettings;
        if (string.IsNullOrWhiteSpace(value) || value == "normal")
            return null;

        var tags = new System.Text.StringBuilder();
        foreach (var part in value.Split(','))
        {
            var item = part.Trim();
            if (item.Length == 0)
                continue;

            // "<tag>" [ <integer> | on | off ]; a 4-char quoted tag, optionally
            // followed by an on/off/value flag (default = on).
            bool firstQuote = item.Contains('"');
            bool altQuote = item.Contains('\'');
            char quote = firstQuote ? '"' : (altQuote ? '\'' : '\0');
            string tag;
            string flag;
            
            if (quote != '\0')
            {
                int start = item.IndexOf(quote);
                int endq = item.IndexOf(quote, start + 1);

                if (endq <= start)
                    continue;

                tag = item.Substring(start + 1, endq - start - 1).Trim();
                flag = item[(endq + 1)..].Trim();
            }
            else
            {
                var sp = item.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                tag = sp[0];
                flag = sp.Length > 1 ? sp[1].Trim() : string.Empty;
            }

            if (tag.Length != 4)
                continue;

            bool enabled = flag.Length == 0
                || flag.Equals("on", StringComparison.OrdinalIgnoreCase)
                || flag == "1"
                || (int.TryParse(flag, out int v) && v != 0);

            if (enabled)
            {
                if (tags.Length > 0)
                    tags.Append(' ');

                tags.Append(tag);
            }
        }

        return tags.Length > 0 ? tags.ToString() : null;
    }

    public string FontSize
    {
        get { return _fontSize; }
        set
        {
            // CSS2.1 §6.2.1: 'inherit' resolves to the parent's computed value.
            if (value != null && value.Equals("inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null)
            {
                _fontSize = GetParent().FontSize;
                InvalidateFontDependentValues();
                return;
            }

            // CSS2.1 §15.7: a percentage font-size resolves against the PARENT's
            // computed font-size.  Resolve it to an absolute length immediately so
            // that descendants which inherit this computed value (InheritStyle copies
            // the string verbatim) do not re-apply the percentage and compound it —
            // e.g. body/div/span all set to 800% must each be 8× the root, not 8×8×8×.
            var trimmedValue = value?.Trim();
            if (trimmedValue != null
                && trimmedValue.EndsWith('%')
                && GetParent() != null
                && double.TryParse(trimmedValue[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                // Percentage font-size resolves against the parent's COMPUTED (unzoomed) font size on the
                // native-zoom path, so `zoom` applies once (via EffectiveZoom on the used size) rather than
                // compounding through the font-size chain; off it, the existing used-size basis is kept.
                double parentBasis = GetParent().EffectiveZoom != 1.0 ? GetParent().ComputedFontSizePoints : GetParent().ActualFont.Size;
                double resolvedPoints = CssLengthParser.ParseNumber(trimmedValue, parentBasis);
                _fontSize = resolvedPoints.ToString("0.0###", CultureInfo.InvariantCulture) + "pt";
                InvalidateFontDependentValues();
                
                if (this is CssBox percentBox)
                {
                    foreach (var child in percentBox.Boxes)
                        child.InvalidateFontDependentSubtree();
                }
                
                return;
            }

            string length = RegexParserUtils.Search(RegexParserUtils.CssLengthRegex(), value);

            if (length != null)
            {
                string computedValue;
                CssLength len = new(length);

                if (len.HasError)
                {
                    computedValue = "medium";
                }
                else if (len.Unit == CssUnit.Em && GetParent() != null)
                {
                    // em font-size resolves against the parent's COMPUTED (unzoomed) size on the
                    // native-zoom path (see the percentage branch above).
                    double parentBasis = GetParent().EffectiveZoom != 1.0 ? GetParent().ComputedFontSizePoints : GetParent().ActualFont.Size;
                    computedValue = len.ConvertEmToPoints(parentBasis).ToString();
                }
                else
                {
                    computedValue = len.ToString();
                }

                _fontSize = computedValue;
            }
            else
            {
                _fontSize = value;
            }

            InvalidateFontDependentValues();

            if (this is CssBox cssBox)
            {
                foreach (var child in cssBox.Boxes)
                    child.InvalidateFontDependentSubtree();
            }
        }
    }

    public string FontStyle { get; set; } = "normal";
    public string FontVariant { get; set; } = "normal";
    public string FontWeight { get; set; } = "normal";
    public string ListStyle { get; set; } = string.Empty;
    public string Overflow { get; set; } = "visible";
    public string ListStylePosition { get; set; } = "outside";
    public string ListStyleImage { get; set; } = string.Empty;
    public string ListStyleType { get; set; } = "disc";

    /// <summary>Semantic role of the element, set during style resolution from tag name.</summary>
    public BoxKind Kind { get; set; } = BoxKind.Anonymous;

    /// <summary>The <c>start</c> attribute of an <c>&lt;ol&gt;</c>, or null if not specified.</summary>
    public int? ListStart { get; set; }

    /// <summary>Whether an <c>&lt;ol&gt;</c> has the <c>reversed</c> attribute.</summary>
    public bool ListReversed { get; set; }

    /// <summary>The resolved <c>src</c> attribute for image elements, or null if not applicable.</summary>
    public string? ImageSource { get; set; }

    #endregion CSS Properties

    public PointF Location
    {
        get
        {
            if (_location.IsEmpty && Position == CssConstants.Fixed)
                _location = GetActualLocation(Left, Top);

            return _location;
        }
        set
        {
            _location = value;
        }
    }

    public SizeF Size
    {
        get { return _size; }
        set { _size = value; }
    }

    public RectangleF Bounds => new(Location, Size);

    public double AvailableWidth => Size.Width - ActualBorderLeftWidth - ActualPaddingLeft - ActualPaddingRight - ActualBorderRightWidth;

    public double ActualRight
    {
        get { return Location.X + Size.Width; }
        set { Size = new SizeF((float)(value - Location.X), Size.Height); }
    }

    public double ActualBottom
    {
        get { return Location.Y + Size.Height; }
        set { Size = new SizeF(Size.Width, (float)(value - Location.Y)); }
    }

    public double ClientLeft => Location.X + ActualBorderLeftWidth + ActualPaddingLeft;
    public double ClientTop => Location.Y + ActualBorderTopWidth + ActualPaddingTop;
    public double ClientRight => ActualRight - ActualPaddingRight - ActualBorderRightWidth;
    public double ClientBottom => ActualBottom - ActualPaddingBottom - ActualBorderBottomWidth;
    public RectangleF ClientRectangle => RectangleF.FromLTRB((float)ClientLeft, (float)ClientTop, (float)ClientRight, (float)ClientBottom);

    public double ActualHeight
    {
        get
        {
            if (double.IsNaN(_actualHeight))
            {
                _actualHeight = string.Equals(Height, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                    ? GetParent().ActualHeight
                    : ParseLengthWithLineHeight(Height, Size.Height);
            }

            return _actualHeight;
        }
    }

    public double ActualWidth
    {
        get
        {
            if (double.IsNaN(_actualWidth))
            {
                _actualWidth = string.Equals(Width, "inherit", StringComparison.OrdinalIgnoreCase) && GetParent() != null
                    ? GetParent().ActualWidth
                    : ParseLengthWithLineHeight(Width, Size.Width);
            }

            return _actualWidth;
        }
    }

    public double ActualPaddingTop
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FramePadding('T');

            if (double.IsNaN(_actualPaddingTop))
                _actualPaddingTop = ParseLengthWithLineHeight(PaddingTop, Size.Width);

            return _actualPaddingTop;
        }
    }

    public double ActualPaddingLeft
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FramePadding('L');

            if (double.IsNaN(_actualPaddingLeft))
                _actualPaddingLeft = ParseLengthWithLineHeight(PaddingLeft, Size.Width);

            return _actualPaddingLeft;
        }
    }

    public double ActualPaddingBottom
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FramePadding('B');

            if (double.IsNaN(_actualPaddingBottom))
                _actualPaddingBottom = ParseLengthWithLineHeight(PaddingBottom, Size.Width);

            return _actualPaddingBottom;
        }
    }

    public double ActualPaddingRight
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FramePadding('R');

            if (double.IsNaN(_actualPaddingRight))
                _actualPaddingRight = ParseLengthWithLineHeight(PaddingRight, Size.Width);

            return _actualPaddingRight;
        }
    }

    public double ActualMarginTop
    {
        get
        {
            if (double.IsNaN(_actualMarginTop))
            {
                if (MarginTop == CssConstants.Auto)
                {
                    _marginTopWasAuto = true;
                    MarginTop = "0";
                }

                var actualMarginTop = ParseLengthWithLineHeight(MarginTop, Size.Width);

                if (MarginTop.EndsWith('%'))
                    return actualMarginTop;

                _actualMarginTop = actualMarginTop;
            }

            return _actualMarginTop;
        }
    }

    public double CollapsedMarginTop
    {
        get { return double.IsNaN(_collapsedMarginTop) ? 0 : _collapsedMarginTop; }
        set { _collapsedMarginTop = value; }
    }

    public double ActualMarginLeft
    {
        get
        {
            if (double.IsNaN(_actualMarginLeft))
            {
                if (MarginLeft == CssConstants.Auto)
                {
                    _marginLeftWasAuto = true;
                    MarginLeft = "0";
                }

                var actualMarginLeft = ParseLengthWithLineHeight(MarginLeft, Size.Width);

                if (MarginLeft.EndsWith('%'))
                    return actualMarginLeft;

                _actualMarginLeft = actualMarginLeft;
            }
            return _actualMarginLeft;
        }
    }

    public double ActualMarginBottom
    {
        get
        {
            if (double.IsNaN(_actualMarginBottom))
            {
                if (MarginBottom == CssConstants.Auto)
                {
                    _marginBottomWasAuto = true;
                    MarginBottom = "0";
                }

                var actualMarginBottom = ParseLengthWithLineHeight(MarginBottom, Size.Width);

                if (MarginBottom.EndsWith('%'))
                    return actualMarginBottom;

                _actualMarginBottom = actualMarginBottom;
            }

            return _actualMarginBottom;
        }
    }

    public double ActualMarginRight
    {
        get
        {
            if (double.IsNaN(_actualMarginRight))
            {
                if (MarginRight == CssConstants.Auto)
                {
                    _marginRightWasAuto = true;
                    MarginRight = "0";
                }

                var actualMarginRight = ParseLengthWithLineHeight(MarginRight, Size.Width);

                if (MarginRight.EndsWith('%'))
                    return actualMarginRight;

                _actualMarginRight = actualMarginRight;
            }

            return _actualMarginRight;
        }
    }

    public double ActualBorderTopWidth
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FrameBorderWidth('T');

            if (double.IsNaN(_actualBorderTopWidth))
            {
                _actualBorderTopWidth = ApplyZoomToLength(BorderTopWidth, CssLengthParser.GetActualBorderWidth(BorderTopWidth, GetEmHeight()));

                if (string.IsNullOrEmpty(BorderTopStyle) || BorderTopStyle == CssConstants.None)
                    _actualBorderTopWidth = 0f;
            }

            return _actualBorderTopWidth;
        }
    }

    public double ActualBorderLeftWidth
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FrameBorderWidth('L');

            if (double.IsNaN(_actualBorderLeftWidth))
            {
                _actualBorderLeftWidth = ApplyZoomToLength(BorderLeftWidth, CssLengthParser.GetActualBorderWidth(BorderLeftWidth, GetEmHeight()));

                if (string.IsNullOrEmpty(BorderLeftStyle) || BorderLeftStyle == CssConstants.None)
                    _actualBorderLeftWidth = 0f;
            }

            return _actualBorderLeftWidth;
        }
    }

    public double ActualBorderBottomWidth
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FrameBorderWidth('B');

            if (double.IsNaN(_actualBorderBottomWidth))
            {
                _actualBorderBottomWidth = ApplyZoomToLength(BorderBottomWidth, CssLengthParser.GetActualBorderWidth(BorderBottomWidth, GetEmHeight()));

                if (string.IsNullOrEmpty(BorderBottomStyle) || BorderBottomStyle == CssConstants.None)
                    _actualBorderBottomWidth = 0f;
            }

            return _actualBorderBottomWidth;
        }
    }

    public double ActualBorderRightWidth
    {
        get
        {
            if (UsesLogicalFrameInsets())
                return FrameBorderWidth('R');

            if (double.IsNaN(_actualBorderRightWidth))
            {
                _actualBorderRightWidth = ApplyZoomToLength(BorderRightWidth, CssLengthParser.GetActualBorderWidth(BorderRightWidth, GetEmHeight()));

                if (string.IsNullOrEmpty(BorderRightStyle) || BorderRightStyle == CssConstants.None)
                    _actualBorderRightWidth = 0f;
            }

            return _actualBorderRightWidth;
        }
    }

    public BColor ActualBorderTopColor
    {
        get
        {
            if (_actualBorderTopColor.IsEmpty)
                _actualBorderTopColor = GetActualColor(BorderTopColor);

            return _actualBorderTopColor;
        }
    }

    protected abstract PointF GetActualLocation(string X, string Y);

    protected abstract BColor GetActualColor(string colorStr);

    protected virtual bool TryGetCustomPropertyValue(string propertyName, out string value)
    {
        value = string.Empty;
        return false;
    }

    private string ResolveCssVariables(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0)
            return value;

        string resolved = value;
        for (int i = 0; i < 8 && resolved.Contains("var(", StringComparison.OrdinalIgnoreCase); i++)
        {
            resolved = CssRegex().Replace(resolved, match =>
                {
                    var propertyName = match.Groups[1].Value;
                    if (TryGetCustomPropertyValue(propertyName, out var propertyValue))
                    {
                        if (propertyValue == InvalidCustomPropertySentinel)
                            return match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;

                        return propertyValue;
                    }

                    return match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
                });
        }

        return resolved;
    }

    public BColor ActualBorderLeftColor
    {
        get
        {
            if (_actualBorderLeftColor.IsEmpty)
                _actualBorderLeftColor = GetActualColor(BorderLeftColor);

            return _actualBorderLeftColor;
        }
    }

    public BColor ActualBorderBottomColor
    {
        get
        {
            if (_actualBorderBottomColor.IsEmpty)
                _actualBorderBottomColor = GetActualColor(BorderBottomColor);

            return _actualBorderBottomColor;
        }
    }

    public BColor ActualBorderRightColor
    {
        get
        {
            if (_actualBorderRightColor.IsEmpty)
                _actualBorderRightColor = GetActualColor(BorderRightColor);

            return _actualBorderRightColor;
        }
    }

    public BColor ActualTextDecorationColor
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TextDecorationColor) ||
                TextDecorationColor.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
            {
                return ActualColor;
            }

            if (_actualTextDecorationColor.IsEmpty)
                _actualTextDecorationColor = GetActualColor(TextDecorationColor);

            return _actualTextDecorationColor;
        }
    }

    /// <summary>
    /// The physical minimum or maximum size a box uses: its own physical longhand when that has
    /// been given a value, and otherwise whichever flow-relative longhand names the same axis under
    /// the box's writing mode. <paramref name="horizontalTb"/> is the logical property that is
    /// physical in <c>horizontal-tb</c> and <paramref name="vertical"/> the one that takes over in a
    /// vertical mode, so the caller states the mapping once and this only picks between them.
    /// </summary>
    private string ResolvePhysicalBound(
        string physical, string horizontalTb, string vertical, string initial)
    {
        if (!string.IsNullOrEmpty(physical)
            && !physical.Equals(initial, StringComparison.OrdinalIgnoreCase))
        {
            return physical;
        }

        var logical = IsVerticalWritingMode(WritingMode) ? vertical : horizontalTb;
        return string.IsNullOrEmpty(logical) ? physical : logical;
    }

    private string ResolvePhysicalSize(string explicitPhysicalValue, bool isWidth)
    {
        // PROTOTYPE (BROILER_VERTICAL_FLOW): a vertical-writing-mode box that
        // WILL be transposed by the post-layout rotation is laid out in a logical
        // (horizontal) frame whose frame-width is the box's INLINE size and
        // frame-height its BLOCK size; the rotation then swaps them into physical
        // space.  For a vertical writing mode physical 'width' is the block-size
        // and physical 'height' the inline-size, so the frame dimensions come
        // from the *swapped* physical properties (frame-width ← CSS height,
        // frame-height ← CSS width).  Without this an explicitly-sized box lays
        // out un-swapped and the rotation transposes it (a 20×80 inner → 80×20).
        // Gated on WillBeVerticalTransposed so a vertical box that is NOT actually
        // rotated (e.g. an abspos item nested in a vertical container, which the
        // runtime excludes from its container's rotation) is left untouched.
        // The cheap IsVerticalWritingMode check short-circuits the common
        // horizontal-tb case before the parent-chain walk.
        if (IsVerticalWritingMode(WritingMode)
            && VerticalFlowPrototype.Enabled
            && WillBeVerticalTransposed())
        {
            string logical = isWidth ? InlineSize : BlockSize;
            if (HasExplicitSize(logical))
                return logical;
            string swappedPhysical = isWidth ? _height : _width;
            return HasExplicitSize(swappedPhysical) ? swappedPhysical : explicitPhysicalValue;
        }

        if (HasExplicitSize(explicitPhysicalValue))
            return explicitPhysicalValue;

        // Legacy path (prototype disabled): vertical-writing-mode boxes swap
        // inline/block onto physical width/height directly (no post-layout
        // rotation).
        bool vertical = IsVerticalWritingMode(WritingMode) && !VerticalFlowPrototype.Enabled;
        var logicalValue = isWidth
            ? (vertical ? BlockSize : InlineSize)
            : (vertical ? InlineSize : BlockSize);

        return HasExplicitSize(logicalValue) ? logicalValue : explicitPhysicalValue;
    }

    /// <summary>
    /// PROTOTYPE (BROILER_VERTICAL_FLOW): whether this box will be transposed by
    /// the post-layout vertical-flow rotation — i.e. it lies inside a vertical
    /// rotation root (a vertical-writing-mode box whose parent is not vertical),
    /// reached without first crossing an out-of-flow box that establishes its own
    /// (non-transposed) rotation context.  Overridden in <see cref="CssBox"/>,
    /// which has the parent chain; the property base has no parent so it cannot
    /// be transposed.
    /// </summary>
    protected virtual bool WillBeVerticalTransposed() => false;

    private static bool HasExplicitSize(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Equals("auto", StringComparison.OrdinalIgnoreCase);

    internal static bool IsVerticalWritingMode(string? writingMode)
    {
        var normalized = writingMode?.Trim().ToLowerInvariant();
        return normalized is "vertical-rl" or "vertical-lr" or "sideways-rl" or "sideways-lr";
    }

    /// <summary>
    /// PROTOTYPE (BROILER_VERTICAL_FLOW): a mirrored vertical writing mode
    /// (<c>vertical-rl</c> / <c>sideways-rl</c>) runs its block flow right→left,
    /// so the post-layout rotation (<see cref="CssBox.ApplyVerticalWritingModeFlow"/>)
    /// flips the block axis. Mirrors that transform's own <c>mirror</c> flag.
    /// </summary>
    private bool IsMirroredVerticalWritingMode()
    {
        var normalized = WritingMode?.Trim().ToLowerInvariant();
        return normalized is "vertical-rl" or "sideways-rl";
    }

    // PROTOTYPE (BROILER_VERTICAL_FLOW): depth of the logical (horizontal) frame
    // layout currently in progress for a vertical-writing-mode rotation root.
    // Non-zero only between a rotation root entering PerformLayout and its
    // post-layout ApplyVerticalWritingModeFlow, i.e. while its subtree is laid
    // out in the swapped frame — the window in which a transposed box's physical
    // border/padding insets must be read as LOGICAL (frame) insets so they land
    // on the correct axis after rotation. Cleared for the later paint pass, which
    // reads the box's authored PHYSICAL borders (writing-mode never rotates a
    // box's own borders). ThreadStatic so an unrelated document laid out on
    // another thread is unaffected; layout on a given thread is synchronous.
    [ThreadStatic] private static int _verticalFrameLayoutDepth;

    internal static bool InVerticalFrameLayout => _verticalFrameLayoutDepth > 0;
    internal static void PushVerticalFrameLayout() => _verticalFrameLayoutDepth++;
    internal static void PopVerticalFrameLayout() => _verticalFrameLayoutDepth--;

    /// <summary>
    /// PROTOTYPE (BROILER_VERTICAL_FLOW): whether this box's physical border and
    /// padding insets must be remapped onto the logical (frame) axes for the
    /// in-progress vertical-flow frame layout. True only for a transposed
    /// vertical-writing-mode box while its rotation root's subtree is being laid
    /// out. The physical→frame edge mapping (see <see cref="FrameBorderWidth"/> /
    /// <see cref="FramePadding"/>) makes a box's authored border-top/bottom
    /// contribute to its <em>inline</em> (post-rotation vertical) extent and its
    /// border-left/right to its <em>block</em> (post-rotation horizontal) extent,
    /// rather than the frame reading them un-rotated (which inflated the block
    /// extent by the inline-axis borders — a table-caption's blue box rendered
    /// ~2.3× too wide).
    /// </summary>
    private bool UsesLogicalFrameInsets() =>
        InVerticalFrameLayout
        && IsVerticalWritingMode(WritingMode)
        && WillBeVerticalTransposed();

    private double RawActualBorderWidth(string widthValue, string styleValue)
    {
        if (string.IsNullOrEmpty(styleValue) || styleValue == CssConstants.None)
            return 0f;
        return CssLengthParser.GetActualBorderWidth(widthValue, GetEmHeight());
    }

    // Frame edges are the logical (horizontal-tb LTR) frame's physical edges;
    // each maps to the authored physical side that must land there so the
    // post-layout rotation places it on the correct physical edge. Block-axis
    // edges (frame top/bottom) flip for a mirrored (rl) writing mode; inline-axis
    // edges (frame left/right, from the box's physical top/bottom) do not.
    private double FrameBorderWidth(char frameEdge)
    {
        bool mirror = IsMirroredVerticalWritingMode();
        return frameEdge switch
        {
            'T' => mirror ? RawActualBorderWidth(BorderRightWidth, BorderRightStyle)
                          : RawActualBorderWidth(BorderLeftWidth, BorderLeftStyle),
            'B' => mirror ? RawActualBorderWidth(BorderLeftWidth, BorderLeftStyle)
                          : RawActualBorderWidth(BorderRightWidth, BorderRightStyle),
            'L' => RawActualBorderWidth(BorderTopWidth, BorderTopStyle),
            'R' => RawActualBorderWidth(BorderBottomWidth, BorderBottomStyle),
            _ => 0f,
        };
    }

    private double FramePadding(char frameEdge)
    {
        bool mirror = IsMirroredVerticalWritingMode();
        return frameEdge switch
        {
            'T' => ParseLengthWithLineHeight(mirror ? PaddingRight : PaddingLeft, Size.Width),
            'B' => ParseLengthWithLineHeight(mirror ? PaddingLeft : PaddingRight, Size.Width),
            'L' => ParseLengthWithLineHeight(PaddingTop, Size.Width),
            'R' => ParseLengthWithLineHeight(PaddingBottom, Size.Width),
            _ => 0f,
        };
    }

    /// <summary>The horizontal component of a corner radius: the first of its one or two
    /// space-separated values.</summary>
    internal static string FirstCornerRadiusComponent(string radius)
    {
        if (string.IsNullOrWhiteSpace(radius))
            return radius;

        var trimmed = radius.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    private double ParseCornerRadius(string radius)
    {
        // A corner radius may carry two values — `border-top-left-radius: 75px 50px` is an ellipse,
        // horizontal radius then vertical. Only the horizontal one is resolved here; the vertical is
        // derived at paint time, where the box height is known. Handing the whole pair to the
        // single-length parser made it fail and return 0, so a two-value corner did not round at all.
        radius = FirstCornerRadiusComponent(radius);

        double basis = radius != null && radius.Contains('%', StringComparison.Ordinal)
            ? Math.Max(0, Size.Width)
            : 0;

        // Paint-only used length (increment 5): border-radius scales with the box's zoom — absolute
        // radii × EffectiveZoom, a `%` radius resolves against the box's own (already zoom-scaled) border
        // box so it carries the full factor already (percentAgainstContainingBlock: false). The paint
        // walker derives the Y radius proportionally from this X value, so it scales with no further work.
        return ApplyZoomToLength(radius, CssLengthParser.ParseLength(radius, basis, GetEmHeight()), percentAgainstContainingBlock: false);
    }

    public double ActualCornerNw
    {
        get
        {
            if (CornerNwRadius != null && CornerNwRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerNwRadius);

            if (double.IsNaN(_actualCornerNw))
                _actualCornerNw = ParseCornerRadius(CornerNwRadius);

            return _actualCornerNw;
        }
    }

    public double ActualCornerNe
    {
        get
        {
            if (CornerNeRadius != null && CornerNeRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerNeRadius);

            if (double.IsNaN(_actualCornerNe))
                _actualCornerNe = ParseCornerRadius(CornerNeRadius);

            return _actualCornerNe;
        }
    }

    public double ActualCornerSe
    {
        get
        {
            if (CornerSeRadius != null && CornerSeRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerSeRadius);

            if (double.IsNaN(_actualCornerSe))
                _actualCornerSe = ParseCornerRadius(CornerSeRadius);

            return _actualCornerSe;
        }
    }

    public double ActualCornerSw
    {
        get
        {
            if (CornerSwRadius != null && CornerSwRadius.Contains('%', StringComparison.Ordinal))
                return ParseCornerRadius(CornerSwRadius);

            if (double.IsNaN(_actualCornerSw))
                _actualCornerSw = ParseCornerRadius(CornerSwRadius);

            return _actualCornerSw;
        }
    }

    public bool IsRounded => ActualCornerNe > 0f || ActualCornerNw > 0f || ActualCornerSe > 0f || ActualCornerSw > 0f;

    /// <summary>
    /// Whether geometry anti-aliasing should be avoided. Returns false by
    /// default; subclasses may override to provide container-specific behavior.
    /// </summary>
    public virtual bool AvoidGeometryAntialias => false;

    public double ActualWordSpacing { get; private set; } = double.NaN;

    public BColor ActualColor
    {
        get
        {
            if (_actualColor.IsEmpty)
                _actualColor = GetActualColor(Color);

            return _actualColor;
        }
    }

    public BColor ActualBackgroundColor
    {
        get
        {
            if (_actualBackgroundColor.IsEmpty)
                _actualBackgroundColor = GetActualColor(BackgroundColor);

            return _actualBackgroundColor;
        }
    }

    public BColor ActualBackgroundGradient
    {
        get
        {
            if (_actualBackgroundGradient.IsEmpty)
            {
                // "none" is the initial value and means no gradient; resolve to
                // fully-transparent so callers can simply check A > 0.  Without
                // this guard, GetActualColor("none") falls back to Color.Black
                // (opaque), which would cause EmitBackground to paint unintended
                // black fills.
                if (string.IsNullOrEmpty(BackgroundGradient) ||
                    string.Equals(BackgroundGradient, "none", StringComparison.OrdinalIgnoreCase))
                    _actualBackgroundGradient = BColor.FromArgb(0, 0, 0, 0);
                else
                    _actualBackgroundGradient = GetActualColor(BackgroundGradient);
            }

            return _actualBackgroundGradient;
        }
    }

    public double ActualBackgroundGradientAngle
    {
        get
        {
            if (double.IsNaN(_actualBackgroundGradientAngle))
                _actualBackgroundGradientAngle = CssLengthParser.ParseNumber(BackgroundGradientAngle, 360f);

            return _actualBackgroundGradientAngle;
        }
    }

    public ILayoutFont ActualFont
    {
        get
        {
            if (_actualFont != null)
                return _actualFont;

            if (string.IsNullOrEmpty(FontFamily))
                FontFamily = CssConstants.DefaultFont;

            if (string.IsNullOrEmpty(FontSize))
                FontSize = CssConstants.FontSize.ToString(CultureInfo.InvariantCulture) + "pt";

            LayoutFontStyle st = LayoutFontStyle.Regular;

            if (FontStyle == CssConstants.Italic || FontStyle == CssConstants.Oblique)
                st |= LayoutFontStyle.Italic;

            if (IsBoldWeight(FontWeight, GetParent()))
                st |= LayoutFontStyle.Bold;

            double fsize;
            if (EffectiveZoom != 1.0)
            {
                // Native CSS `zoom`: the used font size is the computed (unzoomed) size scaled by the
                // effective zoom. `em`/`%`/inheritance resolve against the computed size (unaffected by
                // zoom), so they compound the ancestor zoom exactly once, through EffectiveZoom.
                fsize = ComputedFontSizePoints * EffectiveZoom;
            }
            else
            {
                double parentSize = CssConstants.FontSize;

                if (GetParent() != null)
                    parentSize = GetParent().ActualFont.Size;

                fsize = FontSize switch
                {
                    CssConstants.Medium => CssConstants.FontSize,
                    CssConstants.XXSmall => CssConstants.FontSize - 4,
                    CssConstants.XSmall => CssConstants.FontSize - 3,
                    CssConstants.Small => CssConstants.FontSize - 2,
                    CssConstants.Large => CssConstants.FontSize + 2,
                    CssConstants.XLarge => CssConstants.FontSize + 3,
                    CssConstants.XXLarge => CssConstants.FontSize + 4,
                    CssConstants.Smaller => parentSize - 2,
                    CssConstants.Larger => parentSize + 2,
                    _ when IsMathFontSize(FontSize) => parentSize,
                    _ => ResolveFontSizeLengthToPoints(FontSize, parentSize),
                };
            }

            // CSS 2.1 §15.4: font-size: 0 results in a zero-size em box.
            // Use a tiny positive value so the font object remains valid
            // while producing near-zero word dimensions in the layout engine.
            if (fsize <= 0)
                fsize = 0.001;

            _actualFont = GetCachedFont(FontFamily, fsize, st, GetEnabledFontFeatureTags());

            return _actualFont;
        }
    }

    protected abstract ILayoutFont GetCachedFont(string fontFamily, double fsize, LayoutFontStyle st, string fontFeatures);

    public double ActualLineHeight
    {
        get
        {
            if (double.IsNaN(_actualLineHeight))
            {
                // CSS2.1 §10.8: "normal" line-height uses a UA-chosen value.
                // Prefer the font's own line metrics so layout matches browser
                // line boxes more closely than a fixed 1.2× fallback.
                if (LineHeight == "normal" || string.IsNullOrEmpty(LineHeight))
                    _actualLineHeight = GetNormalLineHeight();
                else
                    _actualLineHeight = ParseLineHeightLength(LineHeight, Size.Height);
            }

            return _actualLineHeight;
        }
    }

    public double ActualTextIndent
    {
        get
        {
            if (double.IsNaN(_actualTextIndent))
                _actualTextIndent = ParseLengthWithLineHeight(TextIndent, Size.Width);

            return _actualTextIndent;
        }
    }

    public double ActualBorderSpacingHorizontal
    {
        get
        {
            if (double.IsNaN(_actualBorderSpacingHorizontal))
            {
                MatchCollection matches = RegexParserUtils.Match(RegexParserUtils.CssLengthRegex(), BorderSpacing);

                if (matches.Count == 0)
                {
                    _actualBorderSpacingHorizontal = 0;
                }
                else if (matches.Count > 0)
                {
                    _actualBorderSpacingHorizontal = ParseLengthWithLineHeight(matches[0].Value, 1);
                }
            }

            return _actualBorderSpacingHorizontal;
        }
    }

    public double ActualBorderSpacingVertical
    {
        get
        {
            if (double.IsNaN(_actualBorderSpacingVertical))
            {
                MatchCollection matches = RegexParserUtils.Match(RegexParserUtils.CssLengthRegex(), BorderSpacing);

                if (matches.Count == 0)
                {
                    _actualBorderSpacingVertical = 0;
                }
                else if (matches.Count == 1)
                {
                    _actualBorderSpacingVertical = ParseLengthWithLineHeight(matches[0].Value, 1);
                }
                else
                {
                    _actualBorderSpacingVertical = ParseLengthWithLineHeight(matches[1].Value, 1);
                }
            }

            return _actualBorderSpacingVertical;
        }
    }

    protected abstract CssBoxProperties GetParent();

    public double GetEmHeight() => ActualFont.Size * CssMetrics.PtToPx;

    /// <summary>
    /// The width of the "0" (ZERO, U+0030) glyph in this box's font — the CSS
    /// definition of the <c>ch</c> unit (CSS Values 3 §5.1.1). Returns
    /// <see cref="double.NaN"/> when no measuring environment is available yet,
    /// so callers fall back to the font-relative approximation. Overridden by
    /// <see cref="CssBox"/> to measure against the real font.
    /// </summary>
    protected virtual double GetChWidth() => double.NaN;

    /// <summary>
    /// Native CSS <c>zoom</c>: scales a resolved absolute length by <see cref="EffectiveZoom"/> (CSS
    /// Viewport — <c>zoom</c> multiplies used values). Applied per unit: absolute lengths
    /// (<c>px</c>/<c>pt</c>/<c>cm</c>/<c>mm</c>/<c>in</c>/<c>pc</c>/<c>q</c>), root-relative <c>rem</c>/
    /// <c>rlh</c>, keyword widths and unitless values scale by the full effective zoom; font-relative
    /// units (<c>em</c>/<c>ex</c>/<c>ch</c>/<c>ic</c>/non-root <c>lh</c>) are already scaled through the
    /// zoomed font metrics (<see cref="ActualFont"/>) and are left unchanged; viewport units are
    /// unaffected. Percentages and <c>calc()</c> resolve against their (already-zoomed) basis and are not
    /// re-scaled here — full <c>%</c>/<c>calc()</c> zoom is a follow-up. No-op unless
    /// <see cref="NativeZoom"/> is enabled and this box is zoomed, so it is inert by default.
    /// </summary>
    /// <param name="percentAgainstContainingBlock">
    /// Whether a <c>%</c> length was resolved against the containing block (ancestor-zoomed) rather than
    /// this box's own already-<see cref="EffectiveZoom"/>-scaled size. When the basis is the box's own
    /// size (padding/margin/…), the percentage already carries the full effective zoom, so it is not
    /// re-scaled; when it is the containing block (width/height/insets), the percentage carries only the
    /// ancestor zoom and needs the box's own <see cref="OwnZoom"/> to reach the effective factor.
    /// </param>
    internal double ApplyZoomToLength(string length, double resolved, bool percentAgainstContainingBlock = false)
    {
        if (!NativeZoom.Enabled)
            return resolved;
        if (EffectiveZoom == 1.0 && OwnZoom == 1.0)
            return resolved;

        var t = length?.Trim();
        if (string.IsNullOrEmpty(t) || t == "0" || t.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return resolved;
        if (t.Contains('('))
            return resolved; // calc()/math — handled by CssLengthParser (increment 3 calc patch)

        var lower = t.ToLowerInvariant();
        if (lower.EndsWith('%'))
            return percentAgainstContainingBlock ? resolved * OwnZoom : resolved;
        if ((lower.EndsWith("em") && !lower.EndsWith("rem"))
            || lower.EndsWith("ex") || lower.EndsWith("ch") || lower.EndsWith("ic")
            || (lower.EndsWith("lh") && !lower.EndsWith("rlh")))
            return resolved; // font-relative — already scaled via the zoomed font metrics
        if (lower.EndsWith("vw") || lower.EndsWith("vh") || lower.EndsWith("vmin") || lower.EndsWith("vmax"))
            return resolved; // viewport-relative — unaffected by element zoom

        return resolved * EffectiveZoom; // absolute / rem / keyword / unitless
    }

    /// <summary>
    /// Resolves a used length (an inset, or an abspos/percentage block size / min-/max-size) against its
    /// basis via the direct <see cref="Broiler.CSS.CssLengthParser.ParseLength(string,double,double)"/> —
    /// i.e. without the line-height context of <see cref="ParseLengthWithLineHeight(string,double,bool)"/> —
    /// and applies native <c>zoom</c> the same way <see cref="ApplyZoomToLength"/> does: absolute lengths
    /// scale by <see cref="EffectiveZoom"/>, <c>em</c>/<c>ch</c> ride the already-zoomed font, viewport units
    /// are untouched, and percentages scale by this box's own zoom when the basis is the (ancestor-zoomed)
    /// containing block. Inert unless <see cref="NativeZoom"/> is enabled and the box is zoomed — flag-off it
    /// is byte-identical to a bare <c>ParseLength</c>.
    /// </summary>
    /// <param name="percentAgainstContainingBlock">
    /// <c>true</c> (the default) when <paramref name="basis"/> is the containing block's size — the usual
    /// case for insets and percentage/abspos block sizes; <c>false</c> when it is the box's own already
    /// effective-zoom-scaled size (e.g. a relative-positioning offset resolved against <c>Size</c>).
    /// </param>
    private protected double ParseUsedLength(string value, double basis, bool percentAgainstContainingBlock = true)
    {
        if (NeedsCalcZoomScope(value))
        {
            CssLengthParser.SetElementZoom(EffectiveZoom, percentAgainstContainingBlock ? OwnZoom : 1.0);
            try { return ApplyZoomToLength(value, CssLengthParser.ParseLength(value, basis, GetEmHeight()), percentAgainstContainingBlock); }
            finally { CssLengthParser.SetElementZoom(1.0, 1.0); }
        }
        return ApplyZoomToLength(value, CssLengthParser.ParseLength(value, basis, GetEmHeight()), percentAgainstContainingBlock);
    }

    /// <summary>
    /// True when a length must be parsed under the CSS length parser's element-<c>zoom</c> scope
    /// (<see cref="Broiler.CSS.CssLengthParser.SetElementZoom"/>): a <c>calc()</c>/<c>min()</c>/<c>max()</c>
    /// mixes units that the post-hoc <see cref="ApplyZoomToLength"/> cannot scale (absolute vs <c>%</c> vs
    /// font-/viewport-relative), so the per-unit factors are set on the parser for the parse instead
    /// (increment-3 calc wiring, the parent side of the <c>Broiler.CSS</c> calc patch). No-op unless native
    /// <c>zoom</c> is on, the box is zoomed, and the length actually contains a <c>(</c> — so flag-off it is
    /// byte-identical.
    /// </summary>
    private bool NeedsCalcZoomScope(string? length)
        => NativeZoom.Enabled
           && (EffectiveZoom != 1.0 || OwnZoom != 1.0)
           && !string.IsNullOrEmpty(length)
           && length.Contains('(');

    /// <param name="percentAgainstContainingBlock">
    /// Set when <paramref name="hundredPercent"/> is the containing block's (ancestor-zoomed) size —
    /// e.g. resolving <c>width</c>/<c>height</c>/insets — so a <c>%</c> length picks up this box's own
    /// zoom to reach the effective factor. Leave <c>false</c> when the basis is the box's own already
    /// effective-zoom-scaled size (padding/margin), where the percentage already carries the full factor.
    /// </param>
    protected double ParseLengthWithLineHeight(string length, double hundredPercent, bool percentAgainstContainingBlock = false)
    {
        if (!string.IsNullOrWhiteSpace(length) &&
            length.EndsWith("rem", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(length[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var rem))
        {
            return ApplyZoomToLength(length, rem * GetRootEmHeight(), percentAgainstContainingBlock);
        }

        // CSS Values 3 §5.1.1: 1ch is the advance measure of the "0" glyph in the
        // element's font. Resolve it from the real font metrics when a measuring
        // environment is available (e.g. Ahem's "0" is a full 1em, not the 0.5em
        // approximation the generic length parser uses). Fall back to the parser
        // for calc()/unavailable-metrics cases.
        if (!string.IsNullOrWhiteSpace(length)
            && length.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            && !length.EndsWith("rch", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(length[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var chCount))
        {
            double chWidth = GetChWidth();
            if (!double.IsNaN(chWidth) && chWidth > 0)
                return ApplyZoomToLength(length, chCount * chWidth, percentAgainstContainingBlock);
        }

        if (NeedsCalcZoomScope(length))
        {
            CssLengthParser.SetElementZoom(EffectiveZoom, percentAgainstContainingBlock ? OwnZoom : 1.0);
            try
            {
                return ApplyZoomToLength(length, CssLengthParser.ParseLength(
                    length, hundredPercent, GetEmHeight(), null, false, false,
                    ActualLineHeight, GetRootLineHeight()), percentAgainstContainingBlock);
            }
            finally { CssLengthParser.SetElementZoom(1.0, 1.0); }
        }

        return ApplyZoomToLength(length, CssLengthParser.ParseLength(
            length,
            hundredPercent,
            GetEmHeight(),
            null,
            false,
            false,
            ActualLineHeight,
            GetRootLineHeight()), percentAgainstContainingBlock);
    }

    private double ParseLineHeightLength(string length, double hundredPercent)
    {
        var parentLineHeight = GetParent()?.ActualLineHeight ?? GetNormalLineHeight();
        double resolved = CssLengthParser.ParseLength(
            length,
            hundredPercent,
            GetEmHeight(),
            null,
            false,
            false,
            parentLineHeight,
            GetRootLineHeight());
        return ApplyZoomToLineHeight(length, resolved);
    }

    /// <summary>
    /// Native <c>zoom</c> for an explicit <c>line-height</c> used value (increment-2 companion). Only an
    /// <em>absolute</em>-length line-height (<c>px</c>/<c>pt</c>/<c>cm</c>/<c>mm</c>/<c>in</c>/<c>pc</c>/<c>q</c>)
    /// scales by <see cref="EffectiveZoom"/> — matching the serialization bake, which multiplied
    /// <c>line-height</c> by the used zoom. A unitless number, a <c>%</c>, or a font-relative
    /// (<c>em</c>/<c>ex</c>/<c>ch</c>/<c>rem</c>) line-height already rides the zoomed font (its basis is the
    /// already-<see cref="EffectiveZoom"/>-scaled font size or root font size), so re-scaling it would
    /// double-count. Inert unless <see cref="NativeZoom"/> is enabled and the box is zoomed — flag-off it is
    /// byte-identical.
    /// </summary>
    private double ApplyZoomToLineHeight(string length, double resolved)
    {
        if (!NativeZoom.Enabled || EffectiveZoom == 1.0)
            return resolved;
        var t = length?.Trim();
        if (string.IsNullOrEmpty(t) || t == "0" || t.Equals("normal", StringComparison.OrdinalIgnoreCase))
            return resolved;
        if (t.Contains('('))
            return resolved; // calc()/math — mixed units ride their own bases; not scaled here
        var lower = t.ToLowerInvariant();
        if (lower.EndsWith("px") || lower.EndsWith("pt") || lower.EndsWith("cm") || lower.EndsWith("mm")
            || lower.EndsWith("in") || lower.EndsWith("pc") || lower.EndsWith('q'))
            return resolved * EffectiveZoom; // absolute physical length — scales like every other used length
        return resolved; // unitless / % / em / rem / viewport — already carried by the zoomed font basis
    }

    private double GetRootLineHeight()
    {
        var root = GetEffectiveRootBoxProperties();

        if (!ReferenceEquals(root, this))
            return root.ActualLineHeight;

        if (!double.IsNaN(_actualLineHeight))
            return _actualLineHeight;

        if (LineHeight == "normal" || string.IsNullOrEmpty(LineHeight))
            return GetNormalLineHeight();

        return CssLengthParser.ParseLength(
            LineHeight,
            Size.Height,
            GetEmHeight(),
            null,
            false,
            false,
            GetNormalLineHeight(),
            GetNormalLineHeight());
    }

    private double GetNormalLineHeight()
    {
        // ActualFont.Height is already expressed in CSS pixels (the font
        // compat factory bakes the pt→px ratio into the returned metric), so
        // it must NOT be scaled by 96/72 again here — doing so inflated the
        // 'normal' line height by ~1.33x (e.g. Arial 32px produced a 50px
        // line box instead of the ~37px browsers use).
        // Rounded UP, which is measurably not what the reference engine does at most sizes — and
        // is nonetheless kept. Flooring instead matches Chromium on 12 of 19 font sizes swept from
        // 8px to 48px where this matches 6 (it would fix 16px to 18, 24px to 27, 32px to 37), but
        // measured over the WPT suite it *lost*: css-values `lh` unit and
        // css-overflow clip-border-box-with-size regressed while css-align
        // safe-justify-self-vrl recovered, a net −1. Whole-page rendering is the authority here,
        // not a single metric compared in isolation. Closing the gap properly needs real per-size
        // ascent/descent from the font backend rather than a rounding mode over this one number.
        double fontHeight = ActualFont.Height;
        return fontHeight > 0 ? Math.Ceiling(fontHeight) : GetEmHeight() * CssMetrics.NormalLineHeightFactor;
    }

    private double GetRootEmHeight()
    {
        var root = GetEffectiveRootBoxProperties();

        const double baseRootEmHeight = CssMetrics.DefaultFontSizePx;
        if (!string.IsNullOrWhiteSpace(root.FontSize))
        {
            var resolved = CssLengthParser.ParseLength(
                root.FontSize,
                baseRootEmHeight,
                baseRootEmHeight,
                null,
                false,
                false,
                baseRootEmHeight * CssMetrics.NormalLineHeightFactor,
                baseRootEmHeight * CssMetrics.NormalLineHeightFactor);

            if (!double.IsNaN(resolved) && resolved > 0)
                return resolved;
        }

        return root.GetEmHeight();
    }

    private CssBoxProperties GetEffectiveRootBoxProperties()
    {
        CssBoxProperties root = this;
        while (root.GetParent() != null)
            root = root.GetParent();

        if (root is CssBox cssRoot)
        {
            while (cssRoot.HtmlTag == null && cssRoot.Boxes.Count == 1)
            {
                var child = cssRoot.Boxes[0];
                if (ReferenceEquals(child, cssRoot))
                    break;

                cssRoot = child;
            }

            root = cssRoot;
        }

        return root;
    }

    /// <summary>
    /// Resolves a CSS font-weight value to a numeric weight (100–900)
    /// per CSS 2.1 §15.6. Handles keywords <c>normal</c>, <c>bold</c>,
    /// <c>bolder</c>, <c>lighter</c>, and numeric strings.
    /// </summary>
    internal static int ResolveNumericFontWeight(string fontWeight, CssBoxProperties parent)
    {
        if (string.IsNullOrEmpty(fontWeight) || fontWeight == CssConstants.Normal || fontWeight == CssConstants.Inherit)
            return 400;

        if (fontWeight == CssConstants.Bold)
            return 700;

        if (int.TryParse(fontWeight, out int numeric))
            return Math.Clamp(numeric, 100, 900);

        if (fontWeight == CssConstants.Bolder || fontWeight == CssConstants.Lighter)
        {
            int parentWeight = 400;
            if (parent != null)
                parentWeight = ResolveNumericFontWeight(parent.FontWeight, parent.GetParent());

            return fontWeight == CssConstants.Bolder
                ? ResolveBolder(parentWeight)
                : ResolveLighter(parentWeight);
        }

        // Any other non-empty, non-normal value is treated as bold
        return 700;
    }

    /// <summary>
    /// CSS 2.1 §15.6: <c>bolder</c> selects the next weight above the inherited value.
    /// </summary>
    private static int ResolveBolder(int parentWeight)
    {
        if (parentWeight < 400) return 400;
        if (parentWeight < 600) return 700;

        return 900;
    }

    /// <summary>
    /// CSS 2.1 §15.6: <c>lighter</c> selects the next weight below the inherited value.
    /// </summary>
    private static int ResolveLighter(int parentWeight)
    {
        if (parentWeight > 700) return 400;
        if (parentWeight > 500) return 400;

        return 100;
    }

    /// <summary>
    /// Returns <c>true</c> when the resolved numeric font weight is 600 or above,
    /// meaning the font should use a bold face.
    /// </summary>
    private static bool IsBoldWeight(string fontWeight, CssBoxProperties parent)
    {
        if (string.IsNullOrEmpty(fontWeight) || fontWeight == CssConstants.Normal || fontWeight == CssConstants.Inherit)
            return false;

        return ResolveNumericFontWeight(fontWeight, parent) >= 600;
    }

    protected void SetAllBorders(string style = null, string width = null, string color = null)
    {
        if (style != null)
            BorderLeftStyle = BorderTopStyle = BorderRightStyle = BorderBottomStyle = style;

        if (width != null)
            BorderLeftWidth = BorderTopWidth = BorderRightWidth = BorderBottomWidth = width;

        if (color != null)
            BorderLeftColor = BorderTopColor = BorderRightColor = BorderBottomColor = color;
    }

    protected internal void MeasureWordSpacing(ILayoutEnvironment g)
    {
        if (!double.IsNaN(ActualWordSpacing))
            return;

        ActualWordSpacing = CssUtils.WhiteSpace(g, this);

        if (WordSpacing == CssConstants.Normal)
            return;

        string len = RegexParserUtils.Search(RegexParserUtils.CssLengthRegex(), WordSpacing);
        // word-spacing is a used length: scale it by the box's zoom (absolute × EffectiveZoom; an em term
        // already rides the zoomed font via GetEmHeight). Inert while NativeZoom is off — byte-identical.
        ActualWordSpacing += ApplyZoomToLength(len, CssLengthParser.ParseLength(len, 1, GetEmHeight()));
    }

    protected void InheritStyle(CssBox p, bool everything)
    {
        if (p == null)
            return;

        BorderSpacing = p.BorderSpacing;
        BorderCollapse = p.BorderCollapse;
        _color = p._color;
        // A box's cascade starts here, so anything a previous pass recorded about *this* box's
        // own colour is stale by definition — the flag describes declarations, and none have been
        // applied yet.
        ColorSpecified = false;
        EmptyCells = p.EmptyCells;
        CaptionSide = p.CaptionSide;
        WhiteSpace = p.WhiteSpace;
        TextTransform = p.TextTransform;
        Visibility = p.Visibility;
        ImageAnimation = p.ImageAnimation;
        // CSS Color Adjust §2.1: `color-scheme` is an inherited property. It was read only off the
        // root element (for the canvas backdrop), which never inherits anything, so its absence
        // here went unnoticed — until an <iframe> had to be asked for its *own* used scheme to
        // decide whether the frame's canvas is opaque (§2.4, Engine.EmbeddedCanvas). Without this
        // an iframe under `html { color-scheme: dark }` reported `normal`, and WPT
        // color-scheme-iframe-background-mismatch-opaque-cross-origin-002 painted the frame
        // transparent when the schemes genuinely did differ.
        ColorScheme = p.ColorScheme;
        _textIndent = p._textIndent;
        TextAlign = p.TextAlign;
        TextAlignLast = p.TextAlignLast;
        FontFamily = p.FontFamily;
        FontFeatureSettings = p.FontFeatureSettings;
        FontVariantAlternates = p.FontVariantAlternates;
        _fontSize = p._fontSize;
        FontStyle = p.FontStyle;
        FontVariant = p.FontVariant;
        FontWeight = p.FontWeight;
        ListStyleImage = p.ListStyleImage;
        ListStylePosition = p.ListStylePosition;
        ListStyleType = p.ListStyleType;
        ListStyle = p.ListStyle;
        _lineHeight = p._lineHeight;
        WordBreak = p.WordBreak;
        LineBreak = p.LineBreak;
        Direction = p.Direction;
        WritingMode = p.WritingMode;
        TextShadow = p.TextShadow;
        // CSS Overflow 4 §4: block-ellipsis is inherited, so the string a clamp
        // places is the one in force at the clamped container. -webkit-box-orient
        // is inherited too, matching the original WebKit property.
        BlockEllipsis = p.BlockEllipsis;
        WebkitBoxOrient = p.WebkitBoxOrient;

        // HTML Rendering §Tables: in quirks mode a <table> starts the font and text properties from
        // their initial values instead of inheriting them. Applied at the end of the inherited-value
        // copy so that the element's own declarations, which the cascade applies next, still win —
        // the spec states the reset as a UA-origin rule — and so that a descendant cell's percentage
        // font-size, resolved eagerly when it is set, resolves against the reset size.
        //
        // Deliberately not on the `everything` path. That one is a *clone* rather than an
        // inheritance: the inline-splitting code copies an already-cascaded box's own values onto
        // the two halves it splits it into, carrying the original's HtmlTag with them, so re-running
        // the reset there would throw away the author values the original had ended up with.
        if (!everything)
        {
            if (this is CssBox self && TableFontInheritanceQuirk.AppliesTo(self))
                TableFontInheritanceQuirk.Apply(self);

            return;
        }

        BackgroundColor = p.BackgroundColor;
        BackgroundGradient = p.BackgroundGradient;
        BackgroundGradientAngle = p.BackgroundGradientAngle;
        BackgroundImage = p.BackgroundImage;
        BackgroundPosition = p.BackgroundPosition;
        BackgroundRepeat = p.BackgroundRepeat;
        BackgroundAttachment = p.BackgroundAttachment;
        BackgroundOrigin = p.BackgroundOrigin;
        BackgroundSize = p.BackgroundSize;
        _borderTopWidth = p._borderTopWidth;
        _borderRightWidth = p._borderRightWidth;
        _borderBottomWidth = p._borderBottomWidth;
        _borderLeftWidth = p._borderLeftWidth;
        _borderTopColor = p._borderTopColor;
        _borderRightColor = p._borderRightColor;
        _borderBottomColor = p._borderBottomColor;
        _borderLeftColor = p._borderLeftColor;
        OutlineWidth = p.OutlineWidth;
        OutlineStyle = p.OutlineStyle;
        _outlineColor = p._outlineColor;
        OutlineOffset = p.OutlineOffset;
        BorderTopStyle = p.BorderTopStyle;
        BorderRightStyle = p.BorderRightStyle;
        BorderBottomStyle = p.BorderBottomStyle;
        BorderLeftStyle = p.BorderLeftStyle;
        _bottom = p._bottom;
        CornerNwRadius = p.CornerNwRadius;
        CornerNeRadius = p.CornerNeRadius;
        CornerSeRadius = p.CornerSeRadius;
        CornerSwRadius = p.CornerSwRadius;
        _cornerRadius = p._cornerRadius;
        Display = p.Display;
        Float = p.Float;
        BlockSize = p.BlockSize;
        MinBlockSize = p.MinBlockSize;
        MaxBlockSize = p.MaxBlockSize;
        MinInlineSize = p.MinInlineSize;
        MaxInlineSize = p.MaxInlineSize;
        Height = p.Height;
        InlineSize = p.InlineSize;
        MarginBottom = p.MarginBottom;
        MarginLeft = p.MarginLeft;
        MarginRight = p.MarginRight;
        MarginTop = p.MarginTop;
        MarginTrim = p.MarginTrim;
        _left = p._left;
        _lineHeight = p._lineHeight;
        Overflow = p.Overflow;
        _paddingLeft = p._paddingLeft;
        _paddingBottom = p._paddingBottom;
        _paddingRight = p._paddingRight;
        _paddingTop = p._paddingTop;
        _right = p._right;
        TextDecoration = p.TextDecoration;
        TextDecorationStyle = p.TextDecorationStyle;
        TextDecorationColor = p.TextDecorationColor;
        _top = p._top;
        Position = p.Position;
        VerticalAlign = p.VerticalAlign;
        Width = p.Width;
        MaxWidth = p.MaxWidth;
        MinWidth = p.MinWidth;
        IsMinWidthSpecified = p.IsMinWidthSpecified;
        MinHeight = p.MinHeight;
        IsMinHeightSpecified = p.IsMinHeightSpecified;
        MaxHeight = p.MaxHeight;
        IntrinsicReplacedSize = p.IntrinsicReplacedSize;
        ObjectFit = p.ObjectFit;
        ObjectPosition = p.ObjectPosition;
        _wordSpacing = p._wordSpacing;
        Opacity = p.Opacity;
        BoxShadow = p.BoxShadow;
        MixBlendMode = p.MixBlendMode;
        BackgroundBlendMode = p.BackgroundBlendMode;
        Filter = p.Filter;
        Isolation = p.Isolation;
        BoxSizing = p.BoxSizing;
        BackgroundClip = p.BackgroundClip;
        ClipPath = p.ClipPath;
        Clip = p.Clip;
        FlexDirection = p.FlexDirection;
        FlexGrow = p.FlexGrow;
        FlexShrink = p.FlexShrink;
        FlexBasis = p.FlexBasis;
        FlexWrap = p.FlexWrap;
        Order = p.Order;
        JustifyContent = p.JustifyContent;
        JustifyItems = p.JustifyItems;
        AlignItems = p.AlignItems;
        AlignContent = p.AlignContent;
        JustifySelf = p.JustifySelf;
        AlignSelf = p.AlignSelf;
        RowGap = p.RowGap;
        ColumnGap = p.ColumnGap;
    }

    protected void InvalidateFontDependentValues()
    {
        _actualFont = null;
        _actualHeight = double.NaN;
        _actualWidth = double.NaN;
        _actualPaddingTop = double.NaN;
        _actualPaddingBottom = double.NaN;
        _actualPaddingRight = double.NaN;
        _actualPaddingLeft = double.NaN;
        _actualMarginTop = double.NaN;
        _actualMarginBottom = double.NaN;
        _actualMarginRight = double.NaN;
        _actualMarginLeft = double.NaN;
        _actualLineHeight = double.NaN;
        _actualTextIndent = double.NaN;
        _actualBorderTopWidth = double.NaN;
        _actualBorderRightWidth = double.NaN;
        _actualBorderBottomWidth = double.NaN;
        _actualBorderLeftWidth = double.NaN;
        // Outline width/offset resolve against the em height, so they are
        // font-dependent and must be re-resolved when the font changes.
        _actualOutlineWidth = double.NaN;
        _actualOutlineOffset = double.NaN;
        // A cached em/ex-based min/max width is font-dependent too.
        _actualMaxWidth = double.NaN;
        _actualMinWidth = double.NaN;
        _actualCornerNw = double.NaN;
        _actualCornerNe = double.NaN;
        _actualCornerSw = double.NaN;
        _actualCornerSe = double.NaN;
        _actualBorderSpacingHorizontal = double.NaN;
        _actualBorderSpacingVertical = double.NaN;
    }

    [GeneratedRegex(@"var\(\s*(--[A-Za-z0-9_-]+)\s*(?:,\s*([^)]+))?\)", RegexOptions.IgnoreCase)]
    private static partial Regex CssRegex();
}
