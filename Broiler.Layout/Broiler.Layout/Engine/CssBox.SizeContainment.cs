using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// CSS Containment 2 §3.2 (size containment) and CSS Sizing 4 §5
/// (<c>contain-intrinsic-size</c>): a box whose size is contained is laid out as though it had no
/// contents, so nothing inside it can be observed from outside — its intrinsic sizes are the ones
/// an empty box would have, and <c>contain-intrinsic-size</c> is how an author states a stand-in
/// for the size the contents would have given it.
/// </summary>
/// <remarks>
/// <para>
/// The contents are still laid out and still painted; what containment removes is their
/// <em>contribution to the box's own size</em>. A contained box with no
/// <c>contain-intrinsic-size</c> is zero in both axes and everything in it overflows, which is the
/// whole point: it is what lets a page state a placeholder size for a subtree it has not measured
/// (and, paired with <c>content-visibility</c>, not rendered).
/// </para>
/// <para>
/// This is the sizing half of a property Broiler already parsed. <c>contain</c> reached the box —
/// background propagation reads it (CSS Backgrounds §2.11.1) and fragmentation reads it
/// (<see cref="IsMonolithicForFragmentation"/>) — but nothing acted on what it says about
/// <em>size</em>, so every one of the 96 assertions in WPT
/// <c>css-sizing/contain-intrinsic-size/contain-intrinsic-size-logical-003</c> (entry 21 of issue
/// #1774's top 30) measured the contents the containment was there to hide.
/// </para>
/// </remarks>
internal partial class CssBox
{
    /// <summary>
    /// CSS Containment 2 §3.2: whether size containment applies to this box — the <c>contain</c>
    /// value asks for it <em>and</em> the box is of a kind it can apply to.
    /// </summary>
    /// <remarks>
    /// The applicability list is not a detail: containment is defined to have no effect on a box
    /// whose size is not its own to decide. A non-atomic inline is sized by the line it sits on,
    /// and an internal table box by the table's row and column algorithm, so honouring
    /// <c>contain: size</c> on either would zero out a size the box never owned.
    /// </remarks>
    internal bool AppliesSizeContainment => AppliesSizeContainmentKeyword(Contain) && CanContainSize;

    /// <summary>
    /// CSS Containment 2 §2: whether a <c>contain</c> value asks for <b>size</b> containment.
    /// </summary>
    /// <remarks>
    /// <c>strict</c> is <c>size layout paint style</c> and does; <c>content</c> is
    /// <c>layout paint style</c> and deliberately does <b>not</b> — leaving a box's size to its
    /// contents is the difference between the two keywords, and the reason a page can use
    /// <c>content</c> without having to state a placeholder size. (The fragmentation predicate in
    /// <see cref="IsMonolithicForFragmentation"/> treats <c>content</c> as monolithic too, and is
    /// right to for its own question: paint containment clips the box, so a page break inside it
    /// would cut content that cannot show. Two questions, two predicates.)
    /// </remarks>
    private static bool AppliesSizeContainmentKeyword(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (var token in value.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("size", System.StringComparison.OrdinalIgnoreCase)
                || token.Equals("strict", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// CSS Containment 2 §3.2: the box types size containment applies to — everything except a
    /// <b>non-atomic</b> inline-level box, an internal table box, and an internal ruby box.
    /// </summary>
    /// <remarks>
    /// "Non-atomic" is the load-bearing word in the inline case. A replaced element generates an
    /// atomic inline-level box, and its computed <c>display</c> is still <c>inline</c> — that is the
    /// UA value for <c>&lt;img&gt;</c>, <c>&lt;svg&gt;</c>, <c>&lt;video&gt;</c> and the rest — so
    /// testing the keyword alone declines containment on exactly the elements
    /// <c>css-contain/contain-size-replaced-*</c> is written about. WPT
    /// <c>contain-size-replaced-002</c> is a bare <c>&lt;svg contain: size&gt;</c> with no
    /// <c>display</c> of its own.
    /// </remarks>
    private bool CanContainSize
    {
        get
        {
            string display = Display?.Trim().ToLowerInvariant() ?? string.Empty;

            return display switch
            {
                CssConstants.Inline => IsReplacedForContainment,
                CssConstants.TableRow or CssConstants.TableRowGroup or CssConstants.TableCell
                    or CssConstants.TableColumn or CssConstants.TableColumnGroup
                    or CssConstants.TableHeaderGroup or CssConstants.TableFooterGroup
                    or CssConstants.TableCaption => false,
                "ruby" or "ruby-base" or "ruby-text" or "ruby-base-container" or "ruby-text-container" => false,
                CssConstants.None => false,
                _ => true,
            };
        }
    }

    /// <summary>
    /// CSS Sizing 4 §5: the intrinsic <b>inline</b> extent a size-contained box reports — the
    /// content-box width <c>contain-intrinsic-width</c> names, or zero when it names <c>none</c>.
    /// </summary>
    internal double ContainedIntrinsicContentWidth =>
        ResolveContainIntrinsicLength(ContainIntrinsicWidth);

    /// <summary>
    /// CSS Sizing 4 §5: the intrinsic <b>block</b> extent a size-contained box reports; see
    /// <see cref="ContainedIntrinsicContentWidth"/>.
    /// </summary>
    internal double ContainedIntrinsicContentHeight =>
        ResolveContainIntrinsicLength(ContainIntrinsicHeight);

    /// <summary>
    /// One <c>contain-intrinsic-*</c> component — <c>auto? [ none | &lt;length&gt; ]</c> — as a
    /// content-box length. <c>none</c>, an absent value and anything that will not parse are all
    /// zero, which is the size an empty box has and therefore the right fallback for each.
    /// </summary>
    /// <remarks>
    /// Percentages are not part of the grammar, so the length is resolved against no basis; a
    /// value carrying one falls back to zero rather than resolving against something arbitrary.
    /// </remarks>
    private double ResolveContainIntrinsicLength(string declaration)
    {
        var value = declaration?.Trim();

        if (string.IsNullOrEmpty(value) || value.Equals("none", System.StringComparison.OrdinalIgnoreCase))
            return 0;

        // `auto <length>` asks for the last remembered size and falls back to the length; the
        // length is what a render that has never skipped this element sees either way.
        if (value.StartsWith("auto ", System.StringComparison.OrdinalIgnoreCase))
            value = value[5..].Trim();

        if (value.Equals(CssConstants.Auto, System.StringComparison.OrdinalIgnoreCase)
            || value.Equals("none", System.StringComparison.OrdinalIgnoreCase)
            || value.Contains('%'))
            return 0;

        double length = CssLengthParser.ParseLength(value, 0, GetEmHeight());

        return double.IsNaN(length) || double.IsInfinity(length) || length < 0 ? 0 : length;
    }

    /// <summary>
    /// Whether this box is replaced, and so generates an <em>atomic</em> inline-level box when its
    /// <c>display</c> is <c>inline</c> — the distinction <see cref="CanContainSize"/> turns on.
    /// </summary>
    /// <remarks>
    /// Read from the element rather than from a single engine flag because no one flag covers it:
    /// <c>IsImage</c> is only <c>&lt;img&gt;</c>, and <c>IntrinsicReplacedSize</c> is only what the
    /// renderer's <c>&lt;canvas&gt;</c> fix-up records. An <c>&lt;svg&gt;</c>, an
    /// <c>&lt;iframe&gt;</c> and a <c>&lt;video&gt;</c> are replaced too and carry neither.
    /// </remarks>
    private bool IsReplacedForContainment
    {
        get
        {
            if (IsImage || IntrinsicReplacedSize is not null)
                return true;

            var name = HtmlTag?.Name;

            return name is not null
                && (name.Equals("svg", System.StringComparison.OrdinalIgnoreCase)
                    || name.Equals("canvas", System.StringComparison.OrdinalIgnoreCase)
                    || name.Equals("video", System.StringComparison.OrdinalIgnoreCase)
                    || name.Equals("audio", System.StringComparison.OrdinalIgnoreCase)
                    || name.Equals("iframe", System.StringComparison.OrdinalIgnoreCase)
                    || name.Equals("embed", System.StringComparison.OrdinalIgnoreCase)
                    || name.Equals("object", System.StringComparison.OrdinalIgnoreCase)
                    || name.Equals("input", System.StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// HTML §14.4 ("Attributes for embedded content and images"): the UA stylesheet maps a replaced
    /// element's presentational <c>width</c> and <c>height</c> attributes to
    /// <c>aspect-ratio: attr(width) / attr(height)</c>. Read here only for a size-contained box.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction this exists for is the one WPT <c>css-flexbox/canvas-contain-size</c> is
    /// written to make, and it states it in its own <c>meta name="assert"</c>: the attributes "don't
    /// map to css width/height, but do map to css aspect ratio, which is honored by flexbox, even
    /// when the item has <c>contain: size</c>". Size containment removes an element's <em>natural</em>
    /// dimensions and natural ratio; it does not remove a <em>declaration</em>, and the UA stylesheet
    /// rule is one. So a <c>&lt;canvas width=20 height=20 style="contain: size; width: 100px"&gt;</c>
    /// is still 100px tall.
    /// </para>
    /// <para>
    /// Broiler carries the attributes as a natural size
    /// (<see cref="CssBoxProperties.IntrinsicReplacedSize"/>, set by the renderer's canvas fix-up)
    /// rather than as a declared ratio, so with the natural size suppressed the ratio went with it.
    /// Reading the attributes back is the same rule expressed on the side of the seam that can see
    /// them. Gated on containment so an uncontained box keeps taking its ratio from its natural
    /// size, which is the path every other replaced element in the engine goes through.
    /// </para>
    /// </remarks>
    internal bool TryGetContainedPresentationalRatio(out double ratio)
    {
        ratio = 0;

        if (!AppliesSizeContainment || HtmlTag is null)
            return false;

        if (!IsPresentationalRatioElement(HtmlTag.Name))
            return false;

        if (!TryParsePresentationalDimension(GetAttribute("width"), out double width)
            || !TryParsePresentationalDimension(GetAttribute("height"), out double height))
        {
            return false;
        }

        if (!(width > 0) || !(height > 0))
            return false;

        ratio = width / height;
        return true;
    }

    /// <summary>The elements the UA stylesheet gives the <c>attr()</c> ratio rule to.</summary>
    private static bool IsPresentationalRatioElement(string tagName) =>
        tagName is not null
        && (tagName.Equals("canvas", System.StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("img", System.StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("video", System.StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("input", System.StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// HTML §2.4.4.1: a presentational dimension attribute is a valid non-negative integer, and
    /// anything else leaves the rule unmatched rather than contributing a garbage ratio.
    /// </summary>
    private static bool TryParsePresentationalDimension(string value, out double parsed)
    {
        parsed = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return double.TryParse(
            value.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed);
    }

    /// <summary>
    /// CSS Containment 2 §3.2 in the inline axis: replaces a contained box's content-derived
    /// min-/max-content widths with the ones an empty box has, plus its own padding and border.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when size containment does not apply, leaving the caller to measure
    /// the contents as before.
    /// </returns>
    internal bool TryGetContainedIntrinsicWidth(out double minWidth, out double maxWidth)
    {
        minWidth = maxWidth = 0;

        if (!AppliesSizeContainment)
            return false;

        double borderBox = ContainedIntrinsicContentWidth
            + ActualPaddingLeft + ActualPaddingRight
            + ActualBorderLeftWidth + ActualBorderRightWidth;

        minWidth = maxWidth = borderBox;
        return true;
    }
}
