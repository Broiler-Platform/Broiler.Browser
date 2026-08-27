using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using System.Drawing;
using System.Globalization;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    private bool UsesBorderBoxSizing =>
        BoxSizing != null && BoxSizing.Equals("border-box", StringComparison.OrdinalIgnoreCase);

    private double ResolveSpecifiedWidthToBorderBox(double cssWidth)
    {
        if (!UsesBorderBoxSizing)
            cssWidth += ActualPaddingLeft + ActualPaddingRight + ActualBorderLeftWidth + ActualBorderRightWidth;

        return Math.Max(0, cssWidth);
    }

    /// <summary>CSS Sizing 3: <c>true</c> for a content-based intrinsic width
    /// keyword (<c>min-content</c>, <c>max-content</c>, <c>fit-content</c> /
    /// <c>fit-content()</c>) that resolves to the box's content size rather than a
    /// length against the containing block.</summary>
    private static bool IsIntrinsicSizingWidthKeyword(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        string v = value.Trim();
        return v.Equals("min-content", StringComparison.OrdinalIgnoreCase)
            || v.Equals("max-content", StringComparison.OrdinalIgnoreCase)
            || v.Equals("fit-content", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("fit-content(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CSS Sizing 3: <c>true</c> for a content-based intrinsic <c>height</c>
    /// keyword. A block box's min-/max-/fit-content block size is its content
    /// height, so such a height must not be treated as a specified length (which,
    /// under <c>box-sizing:border-box</c>, would wrongly reinterpret the already
    /// content-derived height as a border-box value and drop the border/padding);
    /// leave the content-computed <c>ActualBottom</c> in place and let the §10.7
    /// min-/max-height clamp apply.</summary>
    private static bool IsIntrinsicSizingHeightKeyword(string value) =>
        IsIntrinsicSizingWidthKeyword(value);

    private double ResolveSpecifiedHeightToBorderBox(double cssHeight)
    {
        if (!UsesBorderBoxSizing)
            cssHeight += ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth;

        return Math.Max(0, cssHeight);
    }

    /// <summary>The inverse of <see cref="ResolveSpecifiedHeightToBorderBox"/>: the content-box
    /// height a specified <c>height</c> names. A percentage child resolves against this, not against
    /// the border box (CSS2.1 §10.5 — percentages are relative to the containing block's
    /// <em>content</em> height).</summary>
    private double ResolveSpecifiedHeightToContentBox(double cssHeight)
    {
        if (UsesBorderBoxSizing)
            cssHeight -= ActualPaddingTop + ActualPaddingBottom + ActualBorderTopWidth + ActualBorderBottomWidth;

        return Math.Max(0, cssHeight);
    }

    /// <summary>
    /// CSS2.1 §10.7: clamp a specified (author-declared) height to
    /// <c>min-height</c>/<c>max-height</c> in the same box-sizing frame (both share
    /// it), returning the clamped specified value — the caller normalizes to the
    /// border box via <see cref="ResolveSpecifiedHeightToBorderBox"/>. A percentage
    /// min-/max-height against an indefinite (auto-height) flow containing block is
    /// treated as its initial value (<c>0</c>/<c>none</c>), per §10.7.
    /// </summary>
    private double ClampSpecifiedHeightToMinMax(double specifiedHeight) =>
        ResolveBlockSizeBounds().Clamp(specifiedHeight);

    /// <summary>
    /// CSS2.1 §10.4: this box's <c>min-width</c>/<c>max-width</c> as used lengths, in the frame
    /// <c>box-sizing</c> names (the caller converts). A percentage resolves against the containing
    /// block's inline size, which is always definite; <c>max-width: none</c> is
    /// <see cref="double.PositiveInfinity"/>.
    /// </summary>
    internal ReplacedBoxSizing.Bounds ResolveInlineSizeBounds()
    {
        double cbWidth = ContainingBlock?.Size.Width ?? 0;
        double em = GetEmHeight();

        // CSS Sizing 3 §5.1: an inline-axis percentage resolves against a containing block width
        // that is not known yet whenever this box is being measured to *decide* that width — a
        // shrink-to-fit float, an inline-block, a table cell. Such a percentage takes the
        // property's initial value (0 for min-width, none for max-width) rather than resolving
        // against nothing, which is the rule the block axis already follows below.
        //
        // Resolving it anyway is not merely imprecise, it inverts the result: the basis is zero,
        // so `max-width: calc(100% - 8px)` parses to a *negative* length that clamps to zero and
        // the box disappears. That is the MediaWiki thumbnail — the skin caps the photograph at
        // `calc(100% - (2 * 3px) - (2 * 1px))` inside a `float: right` figure, so the image came
        // out zero pixels wide, the figure shrank to its caption, and the article reflowed
        // around a thumbnail that was not there. A plain `max-width: 100%` reached zero the same
        // way but clamped to the same zero the box already had, which is why only the calc()
        // form was visible.
        bool inlineBasisIsDefinite = cbWidth > 0;

        double min = 0;
        if (IsSizeConstraintLength(MinWidth) && (inlineBasisIsDefinite || !MinWidth.Contains('%')))
        {
            double v = CssLengthParser.ParseLength(MinWidth, cbWidth, em);
            if (v > 0 && !double.IsNaN(v))
                min = v;
        }

        double max = double.PositiveInfinity;
        if (IsSizeConstraintLength(MaxWidth) && (inlineBasisIsDefinite || !MaxWidth.Contains('%')))
        {
            double v = CssLengthParser.ParseLength(MaxWidth, cbWidth, em);
            if (v >= 0 && !double.IsNaN(v))
                max = v;
        }

        return new ReplacedBoxSizing.Bounds(min, max);
    }

    /// <summary>
    /// Whether a <c>min-*</c>/<c>max-*</c> value is a length, percentage or math function this can
    /// resolve — as opposed to a keyword (<c>none</c>, <c>auto</c>, <c>fit-content</c>,
    /// <c>min-content</c>, <c>stretch</c>, <c>inherit</c>, …), which is left unconstrained.
    /// </summary>
    /// <remarks>
    /// The guard is load-bearing, not defensive: <see cref="CssLengthParser.ParseLength"/> resolves
    /// an unrecognised unit to <c>0px</c>, so handing it <c>max-width: fit-content</c> would clamp
    /// the box to nothing rather than leave it alone.
    /// </remarks>
    private static bool IsSizeConstraintLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string t = value.Trim();

        // `none` is max-*'s initial value. Zero is *not* excluded: it is min-*'s initial value and
        // a no-op there, but `max-height: 0` is a real clamp (WPT CSS2/normal-flow/max-height-101).
        if (t.Equals("none", StringComparison.OrdinalIgnoreCase))
            return false;

        char c0 = t[0];
        if (char.IsAsciiDigit(c0) || c0 == '.')
            return true;

        if ((c0 == '+' || c0 == '-') && t.Length > 1 && (char.IsAsciiDigit(t[1]) || t[1] == '.'))
            return true;

        return t.StartsWith("calc(", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("min(", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("max(", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// CSS2.1 §10.7: this box's <c>min-height</c>/<c>max-height</c> as used lengths, in the frame
    /// <c>box-sizing</c> names (the caller converts). A percentage against an <em>indefinite</em>
    /// containing block takes the property's initial value — <c>0</c> for <c>min-height</c>,
    /// <c>none</c> for <c>max-height</c> — so an unresolvable percentage never clamps anything.
    /// </summary>
    internal ReplacedBoxSizing.Bounds ResolveBlockSizeBounds()
    {
        double em = GetEmHeight();
        double? percentBasis = null;

        double min = 0;
        if (IsSizeConstraintLength(MinHeight)
            && TryResolveBlockSizeLength(MinHeight, em, ref percentBasis, out double minLength)
            && minLength > 0)
        {
            min = minLength;
        }

        double max = double.PositiveInfinity;
        if (IsSizeConstraintLength(MaxHeight)
            && TryResolveBlockSizeLength(MaxHeight, em, ref percentBasis, out double maxLength)
            && maxLength >= 0)
        {
            max = maxLength;
        }

        return new ReplacedBoxSizing.Bounds(min, max);
    }

    /// <summary>Resolves one block-axis length, looking the percentage basis up at most once (it
    /// walks the ancestor chain) and reporting <see langword="false"/> when a percentage has no
    /// definite basis to resolve against.</summary>
    private bool TryResolveBlockSizeLength(string value, double em, ref double? percentBasis, out double length)
    {
        length = 0;

        if (value.Contains('%'))
        {
            if (percentBasis is null)
                percentBasis = TryGetPercentageBlockSizeBasis(out double basis) ? basis : double.NaN;

            if (double.IsNaN(percentBasis.Value))
                return false;
        }

        length = CssLengthParser.ParseLength(value, percentBasis ?? 0, em);
        return !double.IsNaN(length) && !double.IsInfinity(length);
    }

    /// <summary>Moves a min/max bound out of the frame <c>box-sizing</c> names and into the content
    /// box, so it can clamp a content-box size. A no-op under the default <c>content-box</c>.</summary>
    private ReplacedBoxSizing.Bounds ToContentBoxBounds(ReplacedBoxSizing.Bounds bounds, double borderAndPadding)
    {
        if (borderAndPadding <= 0 || !UsesBorderBoxSizing)
            return bounds;

        return new ReplacedBoxSizing.Bounds(
            Math.Max(0, bounds.Min - borderAndPadding),
            double.IsPositiveInfinity(bounds.Max) ? bounds.Max : Math.Max(0, bounds.Max - borderAndPadding));
    }

    /// <summary>
    /// The used content width this box's specified <c>width</c> names, or <see langword="false"/>
    /// when there is none to use — <c>auto</c>, empty, or an intrinsic-sizing keyword. A replaced
    /// box then takes its inline axis from its natural size, or from the block axis through its
    /// ratio.
    /// </summary>
    private bool TryResolveSpecifiedReplacedContentWidth(double availableInlineSize, out double contentWidth)
    {
        contentWidth = 0;

        if (Width == CssConstants.Auto || string.IsNullOrEmpty(Width) || IsIntrinsicSizingWidthKeyword(Width))
            return false;

        double specified = CssLengthParser.ParseLength(Width, availableInlineSize, GetEmHeight());
        if (double.IsNaN(specified) || double.IsInfinity(specified) || specified < 0)
            return false;

        contentWidth = UsesBorderBoxSizing
            ? Math.Max(0, specified - ActualBorderLeftWidth - ActualBorderRightWidth
                          - ActualPaddingLeft - ActualPaddingRight)
            : specified;
        return true;
    }

    /// <summary>
    /// The used content height this box's specified <c>height</c> names, or <see langword="false"/>
    /// when there is none to use — <c>auto</c>, an intrinsic-sizing keyword, or (CSS2.1 §10.5) a
    /// percentage with no definite basis to resolve against, which computes to <c>auto</c>. A
    /// replaced box then takes its block axis from its natural size, or from the inline axis
    /// through its ratio.
    /// </summary>
    internal bool TryResolveSpecifiedReplacedContentHeight(out double contentHeight)
    {
        contentHeight = 0;

        if (Height == CssConstants.Auto || string.IsNullOrEmpty(Height) || IsIntrinsicSizingHeightKeyword(Height))
            return false;

        double basis = 0;
        if (Height.Contains('%') && !TryGetPercentageBlockSizeBasis(out basis))
            return false;

        double specified = CssLengthParser.ParseLength(Height, basis, GetEmHeight());
        if (double.IsNaN(specified) || double.IsInfinity(specified) || specified < 0)
            return false;

        contentHeight = ResolveSpecifiedHeightToContentBox(specified);
        return true;
    }

    /// <summary>
    /// CSS2.1 §10.3.2/§10.6.2 then §10.4: the used <em>content</em> size of a replaced box that
    /// carries a natural size (<see cref="CssBoxProperties.IntrinsicReplacedSize"/> — a
    /// <c>&lt;canvas&gt;</c>). An axis the author left <c>auto</c> comes from the natural size, or
    /// from the other axis through the ratio when that one is stated; then the min/max constraints
    /// are applied to both together.
    /// </summary>
    /// <remarks>
    /// Shared by the atomic inline-level path (<see cref="CssLayoutEngine"/>'s inline flow) and the
    /// block-level one (<see cref="ResolveBlockUsedWidth"/> / <see cref="ResolveUsedBlockHeight"/>),
    /// so a <c>display: block</c> canvas is sized by the same rules as an inline one.
    /// </remarks>
    internal void ResolveReplacedContentSize(
        SizeF natural, double availableInlineSize, out double contentWidth, out double contentHeight)
    {
        bool widthIsAuto = !TryResolveSpecifiedReplacedContentWidth(availableInlineSize, out contentWidth);
        bool heightIsAuto = !TryResolveSpecifiedReplacedContentHeight(out contentHeight);

        if (widthIsAuto)
            contentWidth = natural.Width;
        if (heightIsAuto)
            contentHeight = natural.Height;

        // CSS Sizing 4 §4: an author `aspect-ratio` is a *preferred* ratio that replaces the
        // natural one for filling in whichever axis is auto.
        double ratio = natural.Height > 0 ? natural.Width / natural.Height : 0;
        if (TryParseAspectRatio(AspectRatio, out double preferred) && preferred > 0)
            ratio = preferred;

        if (ratio > 0)
        {
            if (widthIsAuto && !heightIsAuto)
                contentWidth = contentHeight * ratio;
            else if (heightIsAuto && !widthIsAuto)
                contentHeight = contentWidth / ratio;
        }

        ReplacedBoxSizing.ApplyMinMax(
            ref contentWidth, ref contentHeight, widthIsAuto, heightIsAuto, ratio,
            ToContentBoxBounds(ResolveInlineSizeBounds(),
                ActualBorderLeftWidth + ActualBorderRightWidth + ActualPaddingLeft + ActualPaddingRight),
            ToContentBoxBounds(ResolveBlockSizeBounds(),
                ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom));
    }

    /// <summary>
    /// The natural (intrinsic) size of a replaced box, from whichever source it has one: the
    /// <c>width</c>/<c>height</c> content attributes a <c>&lt;canvas&gt;</c> records in
    /// <see cref="CssBoxProperties.IntrinsicReplacedSize"/>, or the decoded bitmap behind an
    /// <c>&lt;img&gt;</c>'s word. <see langword="false"/> for every non-replaced box, and for a
    /// replaced one whose content has not loaded (which has no natural size to offer).
    /// </summary>
    private bool TryGetNaturalReplacedSize(out SizeF natural)
    {
        // CSS Containment 2 §3.2: a size-contained replaced element "must be treated as having no
        // natural dimensions and no natural aspect ratio" — the bitmap or the canvas attributes are
        // contents like any other, and just as unobservable from outside.
        if (AppliesSizeContainment)
        {
            natural = default;
            return false;
        }

        if (IntrinsicReplacedSize is { Width: > 0, Height: > 0 } declared)
        {
            natural = declared;
            return true;
        }

        if (LayoutEnvironment != null
            && Words.Count == 1 && Words[0] is CssRectImage { Image: not null } imageWord
            && LayoutEnvironment.GetImageIntrinsics(imageWord.Image) is { Width: > 0, Height: > 0 } bitmap)
        {
            natural = new SizeF((float)bitmap.Width, (float)bitmap.Height);
            return true;
        }

        natural = default;
        return false;
    }

    /// <summary>
    /// CSS2.1 §10.3.4/§10.6.5: the used border-box size of a <em>block-level or out-of-flow</em>
    /// replaced box. Both axes come from the replaced rules (§10.3.2/§10.6.2), never from the
    /// containing block's width or from an inset constraint equation — an
    /// <c>&lt;img position:absolute; left:4em; right:0; top:4em; bottom:0&gt;</c> keeps its natural
    /// size and lets <c>right</c>/<c>bottom</c> give way, rather than stretching across the inset
    /// box (WPT CSS2/positioning/abspos-025).
    /// </summary>
    internal bool TryResolveReplacedBorderBoxSize(double availableInlineSize, out double width, out double height)
    {
        width = 0;
        height = 0;

        if (!TryGetNaturalReplacedSize(out SizeF natural))
            return false;

        ResolveReplacedContentSize(natural, availableInlineSize, out double contentWidth, out double contentHeight);

        // ResolveReplacedContentSize answers in *content* sizes — that is the frame the natural
        // size and the ratio are stated in, and the one its min/max clamp works in. So the border
        // box is the content box plus the edges, always. Converting with
        // ResolveSpecifiedWidthToBorderBox instead treated the number as an author-specified
        // length, which under `box-sizing: border-box` is already inclusive — so the padding and
        // border were dropped: a 16x16 image with `padding: 1px 2px 3px 4px; box-sizing:
        // border-box` reported a 16x16 border box where its content alone is 16x16 and the box is
        // 22x20 (css-flexbox/image-as-flexitem-size-007), and one sized from the other axis
        // through its ratio came out the content size in both.
        width = Math.Max(0, contentWidth
            + ActualBorderLeftWidth + ActualBorderRightWidth + ActualPaddingLeft + ActualPaddingRight);
        height = Math.Max(0, contentHeight
            + ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom);
        return true;
    }

    internal double GetMinimumWidth()
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicCalls);
        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic);

        double maxWidth = 0;
        CssRect maxWidthWord = null;
        CssBoxHelper.GetMinimumWidth_LongestWord(this, ref maxWidth, ref maxWidthWord);

        double padding = 0f;
        if (maxWidthWord != null)
        {
            var box = maxWidthWord.OwnerBox;
            while (box != null)
            {
                padding += box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualBorderLeftWidth + box.ActualPaddingLeft;
                box = box != this ? box.ParentBox : null;
            }
        }

        return maxWidth + padding;
    }

    /// <summary>
    /// Min/max-content width measured from this box's <em>content</em>, ignoring
    /// its own explicit width (a percentage width is resolved against the caller's
    /// context, so for grid track sizing it is treated as auto). Descendants'
    /// explicit widths are honoured. Used by the grid track-sizing algorithm.
    /// </summary>
    internal void GetContentMinMaxWidth(out double minWidth, out double maxWidth)
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicCalls);
        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic);

        // CSS Containment 2 §3.2: a size-contained box measures as though it were empty, so there
        // are no contents here to measure.
        if (TryGetContainedIntrinsicWidth(out minWidth, out maxWidth))
            return;

        double min = 0f;
        double maxSum = 0f;
        double paddingSum = 0f;
        double marginSum = 0f;

        CssBoxHelper.GetMinMaxSumWords(this, ref min, ref maxSum, ref paddingSum, ref marginSum, suppressExplicitWidthFor: this);

        maxWidth = paddingSum + maxSum;
        minWidth = paddingSum + (min < 90999 ? min : 0);
        maxWidth -= CssBoxHelper.EdgeWhitespaceSpacing(this);
        if (maxWidth < minWidth)
            maxWidth = minWidth;
    }

    internal void GetMinMaxWidth(out double minWidth, out double maxWidth)
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicCalls);
        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic);

        // CSS Containment 2 §3.2: a size-contained box measures as though it were empty. Ahead of
        // the grid branch below for the same reason it is ahead of the word walk: a contained
        // grid's tracks are contents too, and are exactly as unobservable as its text.
        if (TryGetContainedIntrinsicWidth(out minWidth, out maxWidth))
            return;

        // A grid with a fixed track template contributes its physical-width track
        // sum (+ gaps + own border/padding) as both min- and max-content, rather
        // than the intrinsic width of its inline content — so a shrink-to-fit grid
        // (or a nested grid item) sizes to its tracks, not its (often empty) text.
        if (TryComputeGridIntrinsicContentWidth(useMax: false, out double gridMin)
            && TryComputeGridIntrinsicContentWidth(useMax: true, out double gridMax))
        {
            double pb = ActualBorderLeftWidth + ActualBorderRightWidth
                      + ActualPaddingLeft + ActualPaddingRight;
            minWidth = gridMin + pb;
            maxWidth = gridMax + pb;
            return;
        }

        double min = 0f;
        double maxSum = 0f;
        double paddingSum = 0f;
        double marginSum = 0f;

        CssBoxHelper.GetMinMaxSumWords(this, ref min, ref maxSum, ref paddingSum, ref marginSum);

        maxWidth = paddingSum + maxSum;
        minWidth = paddingSum + (min < 90999 ? min : 0);

        // CSS Text 3 §4.1.1 (phase II): a collapsible space sequence at the
        // start of the first line / end of the last line of a formatting
        // context is removed and contributes no width.  Broiler models a
        // collapsed space as word-spacing carried on the neighbouring word
        // (HasSpaceBefore / HasSpaceAfter); GetMinMaxSumWords counts that
        // spacing for every word, so the leading space-before of the box's
        // first content word and the trailing space-after of its last word
        // inflate the preferred width by one space each.  This is the box's
        // own formatting-context edge (GetMinMaxWidth is only queried for
        // shrink-to-fit roots — table cells, floats, inline-blocks,
        // abspos), and the paint path already drops those edge spaces, so
        // the width must match.  Subtracting them makes a whitespace-padded
        // table cell (e.g. <td> Cell </td>) shrink to the same width as the
        // tight cell, so adjacent cells abut as in a real <table>
        // (CSS2 tables/table-anonymous-objects-*).
        maxWidth -= CssBoxHelper.EdgeWhitespaceSpacing(this);
        if (maxWidth < minWidth)
            maxWidth = minWidth;
    }

    /// <summary>
    /// CSS2.1 §10.3.7: Computes the shrink-to-fit width for an auto-width
    /// absolutely positioned element by independently measuring each direct
    /// child's total width and returning the maximum.
    /// Each block or float child is its own "line"; the preferred width is
    /// the widest line.  This avoids the incorrect accumulation that occurs
    /// when <see cref="CssBoxHelper.GetMinMaxSumWords"/> sums float widths
    /// with preceding block widths.
    /// </summary>
    private double ComputeShrinkToFitWidth()
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicCalls);
        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Intrinsic);

        // A grid with a fixed track template shrink-to-fits to its physical-width
        // track sum (+ gaps), not the max-content of its inline content — an empty
        // or small-item grid would otherwise collapse (fit-content / float /
        // inline-grid grids). Content-box width; the caller adds border/padding.
        if (TryComputeGridIntrinsicContentWidth(useMax: true, out double gridMaxContent))
            return gridMaxContent;

        double maxLineWidth = 0;
        // Running width of a horizontal run of adjacent floated children. At
        // max-content the container is under no width constraint, so a run of
        // float:left/right children lays out side by side and their widths ADD;
        // a non-floated (block) child ends the run and starts its own line.
        double floatRunWidth = 0;

        foreach (var child in Boxes)
        {
            double childWidth;

            if (child.Width != CssConstants.Auto && !string.IsNullOrEmpty(child.Width)
                && !IsPercentageWidth(child.Width))
            {
                // Explicit (definite) width: use declared width + borders/padding
                double containingBlockWidth = Size.Width > 0 && !double.IsNaN(Size.Width) ? Size.Width : 0;
                childWidth = child.ParseLengthWithLineHeight(child.Width, containingBlockWidth)
                           + child.ActualBorderLeftWidth + child.ActualBorderRightWidth
                           + child.ActualPaddingLeft + child.ActualPaddingRight;
            }
            else
            {
                // Auto- or percentage-width child: compute its intrinsic
                // preferred width. CSS Sizing 3 §5.1: a child's percentage width
                // resolves against the size we are *computing*, so it is treated
                // as auto for the container's max-content — otherwise a
                // width:100% child resolves against the container's current
                // (available) width and balloons the shrink-to-fit result to the
                // full container (e.g. a float or auto-fill grid item sized 100%
                // pins the float to the viewport instead of its content).
                // Guard against NaN from unmeasured words in deeply nested
                // inline elements (e.g. Acid2 .eyes → #eyes-a → <object>).
                child.GetMinMaxWidth(out _, out double childMax);
                childWidth = double.IsNaN(childMax) ? 0 : childMax;
            }

            childWidth += child.ActualMarginLeft + child.ActualMarginRight;
            if (double.IsNaN(childWidth))
                continue;

            // CSS Sizing 3 §5: the max-content width of a container is the widest
            // of its lines with no wrapping. Inline-level content stays on the line
            // and accumulates — adjacent floats (WPT floats-143: a <ul> of two
            // float:left <li> would otherwise shrink to one child's width and wrap
            // the second below the first) and **atomic inline-level boxes**
            // (inline-block / inline-table / inline-flex / inline-grid), which sit
            // side by side, so two 40px inline-blocks contribute 80, not 40. Only a
            // block-level child ends the run and starts its own line.
            if (child.Float != CssConstants.None
                || CssBoxHelper.IsAtomicInlineLevel(child.Display))
            {
                floatRunWidth += childWidth;
                maxLineWidth = Math.Max(maxLineWidth, floatRunWidth);
            }
            else
            {
                floatRunWidth = 0;
                maxLineWidth = Math.Max(maxLineWidth, childWidth);
            }
        }

        return maxLineWidth;
    }

    // ─────────────────────── CSS Sizing 4: aspect-ratio ───────────────────────

    /// <summary>
    /// CSS Sizing 4 §4: resolve the used border-box block (height) size of a box
    /// whose height is <c>auto</c> from its already-resolved used inline (width)
    /// size and its preferred <c>aspect-ratio</c>. The caller applies this only to
    /// in-flow block-level boxes, whose used width fills the containing block and
    /// so does not itself depend on the aspect ratio, making the transfer
    /// unambiguous.
    /// <para>The reference browser drops the experimental <c>display: grid-lanes</c>
    /// keyword to the element's default display (block; issue #1218) but still
    /// honours <c>aspect-ratio</c>, so a dropped grid-lanes container with an auto
    /// height is sized to a square — the <c>css-grid/grid-lanes/track-sizing/
    /// auto-repeat</c> cluster expects exactly this. Broiler previously ignored
    /// <c>aspect-ratio</c> on ordinary boxes and rendered a viewport-wide,
    /// min-height-tall bar, matching those references by only ~8%.</para>
    /// <para>Returns the transferred border-box height; the caller then applies the
    /// CSS2.1 §10.7 min-/max-height clamp (so a <c>min-height</c> floors the
    /// square). Returns <c>false</c> when there is no preferred aspect ratio,
    /// leaving every aspect-ratio-less box (the overwhelming majority) untouched.</para>
    /// </summary>
    /// <summary>
    /// CSS Sizing 4 §4: whether this box's <c>auto</c> block (height) axis may be
    /// derived from its used inline (width) size and its preferred aspect ratio.
    /// <para>The transfer is only unambiguous when the used width does not itself
    /// depend on the height being transferred. That holds for an in-flow
    /// block-level box (whose auto width fills the containing block) and for
    /// <em>any</em> box carrying a specified, non-<c>auto</c> width — including a
    /// float or an inline-block, which would otherwise shrink-to-fit. The latter is
    /// what sizes an outer <c>&lt;svg&gt;</c>: SVG 2 §8.2 resolves its auto width to
    /// <c>100%</c>, and its viewBox ratio then gives the height (see
    /// <c>DomParser.ApplySvgReplacedSizing</c>).</para>
    /// <para>Absolutely positioned boxes stay out: their block size comes from the
    /// §10.6.4 inset constraint above, and their percentage heights always resolve
    /// against a definite containing block.</para>
    /// </summary>
    /// <summary>
    /// CSS Sizing 4 §5.1: whether this box's <c>min-height: auto</c> resolves to its content-based
    /// minimum, which is what stops a preferred aspect ratio from sizing a box shorter than the
    /// content it holds. An explicit <c>min-height</c> replaces the automatic minimum, and a scroll
    /// container's content scrolls instead of pushing its box out.
    /// </summary>
    private bool AutomaticMinimumSizeApplies
    {
        get
        {
            var minimum = MinHeight?.Trim();
            if (!string.IsNullOrEmpty(minimum)
                && !minimum.Equals("0", StringComparison.Ordinal)
                && !minimum.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrEmpty(Overflow)
                || Overflow.Equals(CssConstants.Visible, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool CanTransferAspectRatioToBlockHeight
    {
        get
        {
            // CSS 2.1 §10.5: a percentage height whose containing block has no definite block size
            // *computes to* auto, so it qualifies for the transfer exactly as a written `auto` does.
            // An outer `<svg width="100%" height="100%">` — every conformance-checkers/html-svg page
            // — is that box, and without this arm it got no height at all once the percentage
            // stopped being (wrongly) resolved against the containing block's width.
            if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height)
                && !HeightPercentageResolvesToAuto())
            {
                return false;
            }

            if (Position == CssConstants.Absolute || Position == CssConstants.Fixed)
                return false;
            if (IsImage)
                return false;

            if (!string.IsNullOrEmpty(Width) && Width != CssConstants.Auto)
                return true;

            return Display == CssConstants.Block && Float == CssConstants.None;
        }
    }

    /// <summary>
    /// The preferred aspect ratio the block-axis transfer uses: the box's own <c>aspect-ratio</c>,
    /// an outer <c>&lt;svg&gt;</c>'s <c>viewBox</c> ratio (SVG 2 §8.2), or — for a size-contained
    /// replaced element — the ratio its presentational <c>width</c>/<c>height</c> attributes declare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three are not interchangeable under CSS Containment 2 §3.2, which is the only reason this
    /// is a method rather than one expression. Size containment removes an element's <b>natural</b>
    /// ratio and leaves its <b>declared</b> one alone. A <c>viewBox</c> states a natural ratio, so it
    /// goes; the UA stylesheet's <c>aspect-ratio: attr(width) / attr(height)</c> (HTML §14.4) is a
    /// declaration, so it stays — WPT <c>css-contain/contain-size-replaced-002</c> and
    /// <c>css-flexbox/canvas-contain-size</c> are the two halves of that, and they pull in opposite
    /// directions.
    /// </para>
    /// <para>
    /// The awkward case is the middle one: Broiler's style pass records an outer <c>&lt;svg&gt;</c>'s
    /// <c>viewBox</c> ratio *into* <see cref="CssBoxProperties.AspectRatio"/> when the height is
    /// <c>auto</c>, so by the time it is read here a natural ratio is indistinguishable from a
    /// declared one. Comparing it back against the <c>viewBox</c> is what separates them, and it
    /// misreads only an author who declares the ratio their own <c>viewBox</c> already implies —
    /// where the two answers coincide anyway.
    /// </para>
    /// </remarks>
    private bool TryGetPreferredRatioForBlockTransfer(out double ratio)
    {
        bool contained = AppliesSizeContainment;
        bool hasViewBoxRatio = TryGetSvgViewBoxRatio(out double viewBoxRatio);

        if (TryParseAspectRatio(AspectRatio, out ratio) && ratio > 0)
        {
            bool isRecordedViewBoxRatio =
                hasViewBoxRatio && viewBoxRatio > 0 && Math.Abs(viewBoxRatio - ratio) < 1e-6;

            if (!contained || !isRecordedViewBoxRatio)
                return true;
        }

        if (!contained && hasViewBoxRatio && viewBoxRatio > 0)
        {
            ratio = viewBoxRatio;
            return true;
        }

        return TryGetContainedPresentationalRatio(out ratio) && ratio > 0;
    }

    private bool TryResolveAspectRatioBlockHeight(out double borderBoxHeight)
    {
        borderBoxHeight = 0;

        if (!TryGetPreferredRatioForBlockTransfer(out double ratio))
            return false;

        double borderBoxWidth = Size.Width;
        if (!(borderBoxWidth > 0))
            return false;

        // aspect-ratio relates the two sizes of the box named by box-sizing
        // (CSS Sizing 4 §4): the border box under `box-sizing: border-box`,
        // otherwise the content box. Transfer width→height in that box (ratio is
        // width/height), then map back to a border-box height for ActualBottom.
        double specifiedHeight;
        if (UsesBorderBoxSizing)
        {
            specifiedHeight = borderBoxWidth / ratio;
        }
        else
        {
            double contentWidth = borderBoxWidth
                - ActualPaddingLeft - ActualPaddingRight
                - ActualBorderLeftWidth - ActualBorderRightWidth;
            if (!(contentWidth > 0))
                return false;
            specifiedHeight = contentWidth / ratio;
        }

        borderBoxHeight = ResolveSpecifiedHeightToBorderBox(specifiedHeight);
        return borderBoxHeight > 0
            && !double.IsNaN(borderBoxHeight) && !double.IsInfinity(borderBoxHeight);
    }

    /// <summary>
    /// CSS Sizing 4 §4 in the other direction: the used border-box <em>width</em> this box's
    /// <c>auto</c> inline axis takes from a definite block size and its preferred aspect ratio.
    /// </summary>
    /// <remarks>
    /// <para><see cref="TryResolveAspectRatioBlockHeight"/> is the width→height transfer, which
    /// is the one an ordinary in-flow box needs: its auto inline size fills the containing
    /// block, so the width is known first. This is the inverse, and it applies wherever the
    /// inline axis is <em>not</em> filled and so has no size of its own — a grid item whose
    /// <c>justify-self</c> is positional rather than stretching being the case it was written
    /// for. Until now only a replaced box could transfer this way
    /// (<see cref="ResolveReplacedContentSize"/>).</para>
    /// <para><paramref name="borderBoxHeight"/> is the block size the caller has already
    /// settled — for a grid item, the one its area gave it — because this transfer is only
    /// unambiguous when the block axis does not itself depend on the width being derived.</para>
    /// </remarks>
    internal bool TryResolveAspectRatioInlineWidth(double borderBoxHeight, out double borderBoxWidth)
    {
        borderBoxWidth = 0;

        if (!TryParseAspectRatio(AspectRatio, out double ratio) || !(ratio > 0))
            return false;

        if (!(borderBoxHeight > 0))
            return false;

        // The ratio relates the two sizes of the box named by box-sizing, so the transfer
        // happens in that box and the result is mapped back to a border box — the mirror of
        // TryResolveAspectRatioBlockHeight, and the reason a bordered `box-sizing: content-box`
        // item cannot simply multiply.
        double specifiedWidth;
        if (UsesBorderBoxSizing)
        {
            specifiedWidth = borderBoxHeight * ratio;
        }
        else
        {
            double contentHeight = borderBoxHeight
                - ActualPaddingTop - ActualPaddingBottom
                - ActualBorderTopWidth - ActualBorderBottomWidth;
            if (!(contentHeight > 0))
                return false;
            specifiedWidth = contentHeight * ratio;
        }

        // CSS2.1 §10.4: the derived size is still subject to min-/max-width, and the clamp
        // happens in the box `box-sizing` names — the same frame the bounds are stated in —
        // before the result is mapped back to a border box. Clamping the border-box width
        // instead double-counts padding against the bound: a `content-box` item with
        // `padding: 0 20px; max-width: 60px` and a 100px transferred content width is 100px
        // wide (60 + 40), not the 60 a border-box clamp leaves.
        borderBoxWidth = ResolveSpecifiedWidthToBorderBox(ResolveInlineSizeBounds().Clamp(specifiedWidth));

        return borderBoxWidth > 0
            && !double.IsNaN(borderBoxWidth) && !double.IsInfinity(borderBoxWidth);
    }

    /// <summary>
    /// CSS Sizing 4 §4: the used border-box width an <c>auto</c> inline axis takes from this
    /// box's own definite block size through its preferred aspect ratio — the case
    /// <see cref="TryResolveAspectRatioInlineWidth"/> serves for a grid item, resolved here
    /// from the box's own <c>height</c> rather than from one a caller has settled.
    /// </summary>
    /// <remarks>
    /// <para>This is the transfer that stops a block-level box stretching. A block's
    /// <c>auto</c> width normally fills its containing block, and
    /// <see cref="TryResolveAspectRatioBlockHeight"/> then derives the height from it; when the
    /// block size is definite the dependency runs the other way and the ratio sizes the inline
    /// axis instead, so <c>&lt;div style="height: 100px; aspect-ratio: 1/1"&gt;</c> is a 100px
    /// square rather than a viewport-wide 100px band. Every engine does this, and the WPT
    /// <c>css-sizing/aspect-ratio</c> tests state it directly.</para>
    /// <para>The block size is the box's own specified <c>height</c>, clamped by
    /// <c>min-height</c>/<c>max-height</c> — the transfer reads the <em>used</em> block size, so
    /// <c>height: 100px; min-height: 200px; aspect-ratio: 1/1</c> is a 200px square. A
    /// percentage height counts only when it resolves against a definite containing block
    /// (CSS2.1 §10.5); otherwise it computes to <c>auto</c> and there is nothing to transfer.</para>
    /// <para>Replaced boxes stay out: an <c>&lt;img&gt;</c> sizes from its natural size and ratio
    /// through <see cref="ResolveReplacedContentSize"/>, which already resolves both axes
    /// together.</para>
    /// </remarks>
    internal bool TryResolveAspectRatioAutoInlineWidth(out double borderBoxWidth)
    {
        borderBoxWidth = 0;

        if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width))
            return false;

        if (!TryParseAspectRatio(AspectRatio, out double ratio) || !(ratio > 0))
            return false;

        if (IsImage || TryGetNaturalReplacedSize(out _))
            return false;

        if (!TryGetDefiniteSpecifiedBorderBoxHeight(out double borderBoxHeight))
            return false;

        return TryResolveAspectRatioInlineWidth(borderBoxHeight, out borderBoxWidth);
    }

    /// <summary>
    /// The used border-box height this box's own <c>height</c> declares, or <c>false</c> when
    /// the block axis is indefinite. Mirrors the height resolution in
    /// <c>ResolveUsedBlockHeight</c>, which runs too late to size the inline axis from.
    /// </summary>
    private bool TryGetDefiniteSpecifiedBorderBoxHeight(out double borderBoxHeight)
    {
        borderBoxHeight = 0;

        if (Height == CssConstants.Auto || string.IsNullOrEmpty(Height))
            return false;

        if (IsIntrinsicSizingHeightKeyword(Height)
            || string.Equals(Height, "inherit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // CSS2.1 §10.5: a percentage against an auto-height containing block computes to auto.
        if (HeightPercentageResolvesToAuto())
            return false;

        double specifiedHeight = Height.Contains('%')
            ? ParseUsedLength(Height, PercentageHeightContainingBlockHeight())
            : ParseLengthWithLineHeight(Height, 0);

        if (double.IsNaN(specifiedHeight) || double.IsInfinity(specifiedHeight) || !(specifiedHeight > 0))
            return false;

        borderBoxHeight = ResolveSpecifiedHeightToBorderBox(ClampSpecifiedHeightToMinMax(specifiedHeight));

        return borderBoxHeight > 0;
    }

    /// <summary>Parses an <c>aspect-ratio</c> value (<c>&lt;number&gt; [ /
    /// &lt;number&gt; ]?</c>, ignoring a leading/trailing <c>auto</c> keyword)
    /// into a width/height ratio. Returns <c>false</c> for <c>auto</c>/<c>none</c>
    /// or a non-positive ratio.</summary>
    internal static bool TryParseAspectRatio(string value, out double ratio)
    {
        ratio = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        double w = double.NaN, h = 1;

        // The `/` is a token of the grammar, not a character of the numbers around it, so
        // whitespace may sit on either side of it — `1 / 2` and `1/2` are the same value, and
        // `1 / 2` is how most of the WPT css-sizing tests spell it. Splitting on whitespace can
        // therefore hand this loop the slash attached to the numerator, attached to the
        // denominator, in the middle of both, or entirely on its own; only the last of those has
        // no number in it at all, and rejecting that token used to reject the whole declaration.
        bool sawSlash = false;

        foreach (var token in value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || token.Equals("none", StringComparison.OrdinalIgnoreCase))
                continue;

            int slash = token.IndexOf('/');
            if (slash >= 0)
            {
                string numerator = token[..slash].Trim();
                string denominator = token[(slash + 1)..].Trim();

                if (numerator.Length > 0
                    && !double.TryParse(numerator, NumberStyles.Float, CultureInfo.InvariantCulture, out w))
                {
                    return false;
                }

                sawSlash = true;

                if (denominator.Length > 0
                    && !double.TryParse(denominator, NumberStyles.Float, CultureInfo.InvariantCulture, out h))
                {
                    return false;
                }
            }
            else if (double.IsNaN(w) && !sawSlash)
            {
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out w))
                    return false;
            }
            else
            {
                // A bare number after the numerator is the denominator (`1 / 1` split on space).
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out h))
                    return false;
            }
        }
        if (double.IsNaN(w) || !(w > 0) || !(h > 0))
            return false;

        ratio = w / h;
        return true;
    }

    /// <summary>
    /// CSS Sizing 3 §5.1: <c>true</c> when <paramref name="width"/> is one of
    /// the intrinsic sizing keywords (<c>min-content</c>, <c>max-content</c>,
    /// <c>fit-content</c>).
    /// </summary>
    private static bool IsIntrinsicWidthKeyword(string width) =>
        string.Equals(width, "min-content", StringComparison.OrdinalIgnoreCase)
        || string.Equals(width, "max-content", StringComparison.OrdinalIgnoreCase)
        || string.Equals(width, "fit-content", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CSS Sizing 3 §5.1: <c>true</c> when <paramref name="width"/> is a plain
    /// percentage (e.g. <c>100%</c>). Such a width resolves against the size being
    /// computed during a container's intrinsic (shrink-to-fit / max-content) pass,
    /// so it must be treated as <c>auto</c> there rather than resolved against the
    /// container's tentative width.
    /// </summary>
    private static bool IsPercentageWidth(string width) =>
        !string.IsNullOrEmpty(width)
        && width.EndsWith('%')
        && !width.Contains('(');

    /// <summary>
    /// CSS Sizing 3 §5: Resolves an intrinsic-keyword width to a used
    /// border-box width.  <c>min-content</c> uses the largest child
    /// min-content contribution, <c>max-content</c> the largest max-content
    /// contribution, and <c>fit-content</c> clamps the max-content size into
    /// the available space (but never below min-content).
    /// </summary>
    private double ResolveIntrinsicWidth(ILayoutEnvironment g, string keyword, double availableContentWidth)
    {
        EnsureDescendantWordsMeasured(g);

        double available = availableContentWidth - ActualMarginLeft - ActualMarginRight;
        double content;
        
        if (string.Equals(keyword, "min-content", StringComparison.OrdinalIgnoreCase))
        {
            content = ComputeIntrinsicInlineSize(useMin: true);
        }
        else if (string.Equals(keyword, "max-content", StringComparison.OrdinalIgnoreCase))
        {
            content = ComputeIntrinsicInlineSize(useMin: false);
        }
        else // fit-content
        {
            double max = ComputeIntrinsicInlineSize(useMin: false);
            double min = ComputeIntrinsicInlineSize(useMin: true);

            content = Math.Min(Math.Max(min, available), max);
        }

        if (double.IsNaN(content) || content < 0)
            content = 0;

        return ResolveSpecifiedWidthToBorderBox(content);
    }

    /// <summary>
    /// Computes the intrinsic inline size (content width) as the widest direct
    /// child contribution.  Each block/float child forms its own line, so the
    /// container's intrinsic size is the maximum child width rather than the
    /// sum.  When <paramref name="useMin"/> is set, auto-width children
    /// contribute their min-content width; otherwise their max-content width.
    /// </summary>
    private double ComputeIntrinsicInlineSize(bool useMin)
    {
        double maxLineWidth = 0;

        foreach (var child in Boxes)
        {
            double childWidth;

            if (child.Width != CssConstants.Auto && !string.IsNullOrEmpty(child.Width)
                && !IsIntrinsicWidthKeyword(child.Width))
            {
                double containingBlockWidth = Size.Width > 0 && !double.IsNaN(Size.Width) ? Size.Width : 0;
                childWidth = child.ParseLengthWithLineHeight(child.Width, containingBlockWidth)
                           + child.ActualBorderLeftWidth + child.ActualBorderRightWidth
                           + child.ActualPaddingLeft + child.ActualPaddingRight;
            }
            else
            {
                child.GetMinMaxWidth(out double childMin, out double childMax);
                double intrinsic = useMin ? childMin : childMax;
                childWidth = double.IsNaN(intrinsic) ? 0 : intrinsic;
            }

            childWidth += child.ActualMarginLeft + child.ActualMarginRight;

            if (!double.IsNaN(childWidth))
                maxLineWidth = Math.Max(maxLineWidth, childWidth);
        }

        return maxLineWidth;
    }

    /// <summary>
    /// Computes the shrink-to-fit content width of this box: the maximum
    /// right edge of all child boxes (relative to this box) plus padding
    /// and border.  Used for abspos self-alignment where the box size is
    /// content-driven rather than stretched.
    /// </summary>
    private double GetShrinkToFitWidth()
    {
        // If there's an explicit CSS width, use it (plus border/padding).
        if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width))
            return Size.Width;

        double maxRight = 0;

        foreach (var child in Boxes)
        {
            if (child.Display == CssConstants.None) 
                continue;
            
            double childRight = (child.Location.X - Location.X)
                                + child.Size.Width
                                + child.ActualMarginRight;
            maxRight = Math.Max(maxRight, childRight);
        }

        if (maxRight <= 0) 
            return Size.Width;

        return maxRight + ActualPaddingRight + ActualBorderRightWidth;
    }

    /// <summary>
    /// Computes the shrink-to-fit content height of this box: the maximum
    /// bottom edge of all child boxes (relative to this box) plus padding
    /// and border.  Used for abspos self-alignment where the box size is
    /// content-driven rather than stretched.
    /// </summary>
    private double GetShrinkToFitHeight()
    {
        // If there's an explicit CSS height, use it (plus border/padding).
        if (Height != CssConstants.Auto && !string.IsNullOrEmpty(Height))
        {
            double h = ActualBottom - Location.Y;
            return h > 0 ? h : Size.Height;
        }

        double maxBottom = 0;
        
        foreach (var child in Boxes)
        {
            if (child.Display == CssConstants.None) 
                continue;
            
            double childBottom = (child.Location.Y - Location.Y)
                                 + (child.ActualBottom - child.Location.Y)
                                 + child.ActualMarginBottom;
            
            maxBottom = Math.Max(maxBottom, childBottom);
        }

        if (maxBottom <= 0)
        {
            double h = ActualBottom - Location.Y;
            return h > 0 ? h : Size.Height;
        }

        return maxBottom + ActualPaddingBottom + ActualBorderBottomWidth;
    }

    /// <summary>
    /// Recursively finds the maximum bottom edge of any float in the
    /// subtree, stopping at nested BFC boundaries.  Used by the BFC
    /// root height calculation so that grandchild (and deeper) floats
    /// are properly contained.
    /// </summary>
    private static void FindMaxDescendantFloatBottom(CssBox box, ref double maxBottom)
    {
        foreach (var child in box.Boxes)
        {
            if (child.Float != CssConstants.None && child.Display != CssConstants.None)
            {
                maxBottom = Math.Max(maxBottom, child.ActualBottom + child.ActualMarginBottom);
            }

            // Don't recurse into nested BFC roots — their floats are
            // contained by them, not by the outer BFC.
            if (!CssBoxHelper.EstablishesBfc(child))
                FindMaxDescendantFloatBottom(child, ref maxBottom);
        }
    }

    /// <summary>
    /// SVG 2 §8.2: the natural ratio (width ÷ height) an outer <c>&lt;svg&gt;</c>'s <c>viewBox</c>
    /// establishes, for the paths that need it when the style pass did not record it as an
    /// <c>aspect-ratio</c>. The attribute is four unitless numbers; anything else — a wrong count or
    /// a non-positive extent — is no ratio at all, the same reading the style pass takes.
    /// </summary>
    private bool TryGetSvgViewBoxRatio(out double ratio)
    {
        ratio = 0;
        if (HtmlTag == null || !HtmlTag.Name.Equals("svg", StringComparison.OrdinalIgnoreCase))
            return false;

        var viewBox = HtmlTag.TryGetAttribute("viewBox");
        if (string.IsNullOrWhiteSpace(viewBox))
            return false;

        var parts = viewBox.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
            || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
            || !(width > 0) || !(height > 0))
        {
            return false;
        }

        ratio = width / height;
        return true;
    }
}
