using Broiler.CSS;
using System.Globalization;

namespace Broiler.Layout.Engine;

/// <summary>
/// CSS Overflow 4 §5 <c>line-clamp</c> / <c>max-lines</c> / <c>block-ellipsis</c>,
/// and the legacy <c>-webkit-line-clamp</c> of CSS Overflow 3 §6.
/// </summary>
/// <remarks>
/// <para>
/// A clamp is applied <em>after</em> the container's children are laid out, in
/// <see cref="CssBox.PerformLayoutImp"/>: the line count it cuts on is not known
/// until the lines exist. Running it there — rather than while flowing — also
/// keeps the reduced height visible to the container's later siblings, which
/// <see cref="CssBox.LayoutBlockChildren"/> positions from the previous
/// sibling's <see cref="CssBoxProperties.ActualBottom"/>.
/// </para>
/// <para>
/// The lines being counted are not necessarily one box's. A <c>&lt;br&gt;</c>
/// splits inline content into anonymous block siblings, so a clamped container
/// often holds its lines across several descendants; the count walks the subtree
/// in layout order and cuts wherever the limit falls, which may be in the middle
/// of one of them.
/// </para>
/// </remarks>
internal partial class CssBox
{
    /// <summary>U+2026 HORIZONTAL ELLIPSIS — what <c>block-ellipsis: auto</c> places.</summary>
    private const string AutoBlockEllipsis = "…";

    /// <summary>
    /// The clamp in force on this box: how many line boxes it keeps, and the
    /// string to place at the end of the last one (empty for none). <c>null</c>
    /// when the box is not clamped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two spellings differ in what they require. <c>line-clamp</c> (and its
    /// <c>max-lines</c> longhand) applies to any block container. The legacy
    /// <c>-webkit-line-clamp</c> applies only inside a <c>-webkit-box</c> /
    /// <c>-webkit-inline-box</c> whose <c>-webkit-box-orient</c> is vertical —
    /// css-overflow/line-clamp asserts exactly that, with tests that set it on a
    /// plain block (webkit-line-clamp-001) or on a horizontal legacy box and
    /// expect <em>no</em> clamping.
    /// </para>
    /// <para>
    /// So when both are declared, the legacy one decides — including deciding
    /// <em>not</em> to clamp. In CSS Overflow 4 terms the two are shorthands that
    /// disagree about <c>continue</c>: <c>line-clamp</c> sets <c>discard</c>,
    /// <c>-webkit-line-clamp</c> sets <c>-webkit-discard</c>, which only becomes
    /// <c>discard</c> inside a legacy box and is otherwise <c>auto</c>.
    /// line-clamp-034 declares <c>line-clamp: 4</c> and <c>-webkit-line-clamp: 4</c>
    /// on a plain block and expects no clamping at all; line-clamp-019 declares
    /// <c>line-clamp: 2</c> then <c>-webkit-line-clamp: 4</c> on a legacy box and
    /// expects four lines.
    /// </para>
    /// <para>
    /// Deciding it by presence rather than by cascade order is deliberate: the
    /// cascade hands this layer a property-keyed map of winning declarations, not
    /// an ordered list, so "whichever was written last" is not knowable here. It
    /// also makes the engine's blanket <c>-webkit-*</c>-to-unprefixed aliasing
    /// harmless for this pair — that alias makes a lone <c>-webkit-line-clamp</c>
    /// arrive as <c>line-clamp</c> too, and letting the legacy spelling win means
    /// the echo cannot switch on the unprefixed behaviour. The one case it gets
    /// wrong is an author writing <c>-webkit-line-clamp</c> first and
    /// <c>line-clamp</c> second on a legacy box with different counts, which
    /// inverts the compatibility order everything is written in.
    /// </para>
    /// </remarks>
    internal (int MaxLines, string Ellipsis)? ResolveLineClamp()
    {
        // CSS Overflow 4 §5: max-lines applies to block containers. A flex or grid
        // container has no inline formatting context of its own to count lines in —
        // its children are items, not inline content — so a clamp declared on one
        // does nothing. line-clamp-029 sets `line-clamp: 4` on a horizontally
        // oriented `-webkit-box` (a legacy *flex* container) and expects all five
        // of its lines, unclamped and with no ellipsis.
        if (!IsBlockContainer)
            return null;

        if (TryParseLineCount(WebkitLineClamp, out int webkitLines))
        {
            return IsLegacyWebkitBox && CssUtils.IsVerticalWebkitBoxOrient(WebkitBoxOrient)
                ? (webkitLines, ResolveBlockEllipsis(auto: true))
                : null;
        }

        // `line-clamp: <integer>` is the shorthand: max-lines: <integer>,
        // block-ellipsis: auto, continue: discard.
        if (TryParseLineCount(LineClamp, out int lineClampLines))
            return (lineClampLines, ResolveBlockEllipsis(auto: true));

        // `max-lines` on its own clamps without implying an ellipsis, so
        // block-ellipsis keeps whatever it inherited (initially `none`).
        if (TryParseLineCount(MaxLines, out int maxLinesLines))
            return (maxLinesLines, ResolveBlockEllipsis(auto: false));

        return null;
    }

    /// <summary>
    /// Whether this box is a block container — a box that lays its in-flow
    /// content out as block boxes or as lines, which is what a clamp counts.
    /// Excludes the flex, grid and table wrappers, whose children are items or
    /// internal table boxes rather than inline content.
    /// </summary>
    private bool IsBlockContainer =>
        Display is CssConstants.Block or CssConstants.InlineBlock or "flow-root"
            or CssConstants.ListItem or CssConstants.TableCell or CssConstants.TableCaption;

    /// <summary>
    /// Whether this box is a legacy WebKit flexible box — the opt-in
    /// <c>-webkit-line-clamp</c> requires.
    /// </summary>
    /// <remarks>
    /// The direct evidence is <see cref="CssBoxProperties.LegacyWebkitBoxDisplay"/>,
    /// recorded when the <c>display</c> declaration is applied. It is only
    /// available when the CSS engine accepts the keyword, and the pinned
    /// <c>Broiler.CSS</c> rejects <c>display: -webkit-box</c> at validation, so the
    /// declaration is dropped before layout ever sees it (fixed by
    /// <c>patches/0136-css-accept-legacy-webkit-box-display.patch</c>). Until that
    /// patch is applied, fall back to <c>-webkit-box-orient</c>: it is a property
    /// of the legacy box and of nothing else, so a box carrying it alongside
    /// <c>-webkit-line-clamp</c> is one. The fallback retires itself — it is only
    /// consulted while the engine in the build is one that drops the keyword.
    /// </remarks>
    private bool IsLegacyWebkitBox =>
        LegacyWebkitBoxDisplay != null
        || (!LegacyWebkitBoxDisplayReachesLayout && CssUtils.IsVerticalWebkitBoxOrient(WebkitBoxOrient));

    /// <summary>
    /// Whether the CSS engine this build links against keeps a
    /// <c>display: -webkit-box</c> declaration rather than dropping it as an
    /// invalid value. Probed once against the shared declaration validator — the
    /// same check the cascade and the inline-style parser run — so the answer
    /// tracks the submodule actually pinned instead of being assumed.
    /// </summary>
    private static readonly bool LegacyWebkitBoxDisplayReachesLayout =
        Broiler.CSS.Dom.CssDeclarationValidator.IsAcceptableDeclarationValue("display", "-webkit-box");

    /// <summary>
    /// The string <c>block-ellipsis</c> asks for: nothing for <c>none</c>, U+2026
    /// for <c>auto</c>, or the given <c>&lt;string&gt;</c> with its quotes and
    /// escapes resolved. <paramref name="auto"/> is the value to use when the
    /// property is still at its initial <c>none</c> — the <c>line-clamp</c>
    /// shorthand sets it to <c>auto</c>, a bare <c>max-lines</c> does not.
    /// </summary>
    private string ResolveBlockEllipsis(bool auto)
    {
        string v = BlockEllipsis?.Trim();

        if (string.IsNullOrEmpty(v) || v.Equals(CssConstants.None, StringComparison.OrdinalIgnoreCase))
            return auto ? AutoBlockEllipsis : string.Empty;

        if (v.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase))
            return AutoBlockEllipsis;

        return UnquoteCssString(v);
    }

    /// <summary>
    /// Strips the quotes from a CSS <c>&lt;string&gt;</c> and resolves the
    /// escapes that can appear in one (<c>\"</c>, <c>\\</c> and the
    /// <c>\{1,6 hex}</c> code-point form). Returns the input unchanged when it is
    /// not quoted, so a malformed value degrades to something visible rather than
    /// to nothing.
    /// </summary>
    private static string UnquoteCssString(string value)
    {
        if (value.Length < 2)
            return value;

        char quote = value[0];
        if ((quote != '"' && quote != '\'') || value[^1] != quote)
            return value;

        string inner = value[1..^1];
        if (!inner.Contains('\\', StringComparison.Ordinal))
            return inner;

        var sb = new System.Text.StringBuilder(inner.Length);
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] != '\\' || i + 1 >= inner.Length)
            {
                sb.Append(inner[i]);
                continue;
            }

            i++;
            int hexStart = i;
            while (i < inner.Length && i - hexStart < 6 && Uri.IsHexDigit(inner[i]))
                i++;

            if (i > hexStart)
            {
                if (int.TryParse(inner[hexStart..i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp)
                    && cp > 0 && cp <= 0x10FFFF)
                {
                    sb.Append(char.ConvertFromUtf32(cp));
                }

                // A single whitespace character terminates a hex escape and is consumed.
                if (i < inner.Length && (inner[i] == ' ' || inner[i] == '\t' || inner[i] == '\n'))
                    i++;

                i--;
                continue;
            }

            sb.Append(inner[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a clamp line count. Only a positive integer clamps; <c>none</c>,
    /// zero, negatives and anything unparseable leave the box unclamped.
    /// </summary>
    private static bool TryParseLineCount(string value, out int lines)
    {
        lines = 0;
        string v = value?.Trim();

        if (string.IsNullOrEmpty(v) || v.Equals(CssConstants.None, StringComparison.OrdinalIgnoreCase))
            return false;

        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out lines) && lines > 0;
    }

    /// <summary>
    /// Applies this box's clamp, if it has one: discards every line box past the
    /// limit along with the boxes left empty by doing so, places the block
    /// ellipsis on the last line kept, and shrinks the auto heights that were
    /// sized to the discarded content.
    /// </summary>
    internal void ApplyLineClamp(ILayoutEnvironment g)
    {
        if (ResolveLineClamp() is not { } clamp)
            return;

        List<(CssBox Owner, CssLineBox Line)> lines = [];
        CollectClampedLines(this, lines);

        if (lines.Count <= clamp.MaxLines)
            return;

        for (int i = clamp.MaxLines; i < lines.Count; i++)
            DiscardLineBox(lines[i].Owner, lines[i].Line);

        MarkFullyClampedSubtree(this);

        var (lastOwner, lastLine) = lines[clamp.MaxLines - 1];
        if (clamp.Ellipsis.Length > 0)
            PlaceBlockEllipsis(g, lastOwner, lastLine, clamp.Ellipsis);

        TrimClampedHeight(this);
    }

    /// <summary>
    /// Collects the line boxes a clamp on <paramref name="root"/> counts, in
    /// layout order.
    /// </summary>
    /// <remarks>
    /// Recursion stops at anything that is not part of the container's own block
    /// flow: out-of-flow boxes (absolute, fixed, floated) are not line boxes of
    /// this container, and an atomic inline — an inline-block, inline-flex,
    /// inline-grid or inline-table — occupies a single line of its parent no
    /// matter how many lines it holds inside, so its own lines are not counted
    /// and never discarded. Non-atomic inline boxes carry no line boxes at all;
    /// their words live on the line boxes of the block container that flowed
    /// them, which is the box being visited.
    /// </remarks>
    private static void CollectClampedLines(CssBox root, List<(CssBox Owner, CssLineBox Line)> lines)
    {
        foreach (var lineBox in root.LineBoxes)
            lines.Add((root, lineBox));

        foreach (var child in root.Boxes)
        {
            if (child.Display == CssConstants.None
                || child.Position is CssConstants.Absolute or CssConstants.Fixed
                || child.Float != CssConstants.None
                || child.IsInline)
            {
                continue;
            }

            CollectClampedLines(child, lines);
        }
    }

    /// <summary>
    /// Detaches one line box from its owner, and from the per-box rectangle maps
    /// <see cref="CssLineBox.AssignRectanglesToBoxes"/> populated, so no part of
    /// the fragment tree can still reach it.
    /// </summary>
    private static void DiscardLineBox(CssBox owner, CssLineBox line)
    {
        owner.LineBoxes.Remove(line);

        foreach (var box in line.Rectangles.Keys)
            box.Rectangles.Remove(line);
    }

    /// <summary>
    /// Flags every descendant box whose content was wholly discarded, so the
    /// fragment tree skips it and its subtree. Returns whether
    /// <paramref name="box"/> still has anything to paint.
    /// </summary>
    /// <remarks>
    /// A box survives when it kept a line of its own, when a descendant did, or
    /// when it is out-of-flow (the clamp never counted its lines, so it never
    /// discarded them either). Anything else is what <c>continue: discard</c>
    /// asks to be dropped: not merely its text, but its background and borders
    /// too, which is why this hides the box rather than just emptying it.
    /// </remarks>
    private static bool MarkFullyClampedSubtree(CssBox box)
    {
        bool retained = box.LineBoxes.Count > 0;

        foreach (var child in box.Boxes)
        {
            if (child.Display == CssConstants.None)
                continue;

            if (child.Position is CssConstants.Absolute or CssConstants.Fixed
                || child.Float != CssConstants.None
                || child.IsInline)
            {
                // Not clamped away, and not evidence that this box kept a line
                // either — an inline box's words are on its container's lines,
                // which are counted where they live.
                continue;
            }

            retained |= MarkFullyClampedSubtree(child);
        }

        if (!retained && box.ParentBox != null)
            box.ClampedAway = true;

        return retained;
    }

    /// <summary>
    /// Shrinks the auto heights along the clamped subtree to the content that
    /// survived. Returns the bottom edge <paramref name="box"/> now occupies.
    /// </summary>
    /// <remarks>
    /// Only auto heights move, and only downwards: an explicit height is the used
    /// height whether or not the content was clamped (CSS2.1 §10.6.3), and
    /// growing a box here could only ever undo something another layout step
    /// decided.
    /// </remarks>
    private static double TrimClampedHeight(CssBox box)
    {
        double contentBottom = box.Location.Y + box.ActualPaddingTop + box.ActualBorderTopWidth;

        foreach (var lineBox in box.LineBoxes)
            contentBottom = Math.Max(contentBottom, LineBoxBottom(box, lineBox));

        foreach (var child in box.Boxes)
        {
            if (child.Display == CssConstants.None || child.ClampedAway)
                continue;

            if (child.Position is CssConstants.Absolute or CssConstants.Fixed || child.IsInline)
            {
                contentBottom = Math.Max(contentBottom, child.ActualBottom);
                continue;
            }

            contentBottom = Math.Max(contentBottom, TrimClampedHeight(child));
        }

        bool autoHeight = string.IsNullOrEmpty(box.Height) || box.Height == CssConstants.Auto;
        double clampedBottom = contentBottom + box.ActualPaddingBottom + box.ActualBorderBottomWidth;

        if (autoHeight && clampedBottom < box.ActualBottom)
            box.ActualBottom = clampedBottom;

        return box.ActualBottom;
    }

    /// <summary>
    /// Places the block ellipsis at the end of the last line a clamp kept.
    /// </summary>
    /// <remarks>
    /// CSS Overflow 4 §4 makes the ellipsis an anonymous inline child of the
    /// clamped container's root inline box, so it is owned by that container and
    /// takes its font and colour — block-ellipsis-002 clamps a teal bold italic
    /// container and expects the ellipsis in teal bold italic. It must also fit:
    /// §4 places it after the last break opportunity on the line that still
    /// leaves room, so trailing words are given back to the discarded content
    /// until it does (block-ellipsis-001).
    /// </remarks>
    private void PlaceBlockEllipsis(ILayoutEnvironment g, CssBox lineOwner, CssLineBox line, string ellipsis)
    {
        double ellipsisWidth = g.MeasureText(ActualFont, ellipsis).Width;
        double lineRight = lineOwner.ActualRight - lineOwner.ActualPaddingRight - lineOwner.ActualBorderRightWidth;

        // Trailing collapsible spaces do not hold the ellipsis off the line; drop
        // them first so it lands against the last glyph rather than after a gap.
        while (line.Words.Count > 0 && line.Words[^1].IsSpaces)
            line.Words.RemoveAt(line.Words.Count - 1);

        // Give words back until the ellipsis fits. The last word is always kept:
        // a line with room for nothing but the ellipsis still shows the ellipsis
        // (and dropping everything would leave an empty line box behind).
        while (line.Words.Count > 1 && LineContentRight(line) + ellipsisWidth > lineRight)
        {
            line.Words.RemoveAt(line.Words.Count - 1);

            while (line.Words.Count > 1 && line.Words[^1].IsSpaces)
                line.Words.RemoveAt(line.Words.Count - 1);
        }

        double left = LineContentRight(line);
        double top = line.Words.Count > 0 ? line.Words[^1].Top : line.LineBottom - ActualFont.Height;

        var word = new CssRectWord(this, ellipsis, hasSpaceBefore: false, hasSpaceAfter: false)
        {
            Left = left,
            Top = top,
            Width = ellipsisWidth,
            Height = ActualFont.Height,
        };

        line.Words.Add(word);
    }

    /// <summary>
    /// The bottom edge of one line box, measured the way
    /// <see cref="CssLayoutEngine.CreateLineBoxes"/> measures it when it sizes a
    /// block to its lines: the lowest inline content, but never less than the
    /// line's own top plus the block's line height (CSS2.1 §10.8 — the strut
    /// keeps a line box as tall as <c>line-height</c> even when its glyphs are
    /// shorter). Measuring only the content is what leaves a clamped block a
    /// half-leading too short.
    /// </summary>
    private static double LineBoxBottom(CssBox owner, CssLineBox line)
    {
        double bottom = 0;
        double top = double.MaxValue;

        foreach (var rect in line.Rectangles.Values)
        {
            bottom = Math.Max(bottom, rect.Bottom);
            top = Math.Min(top, rect.Top);
        }

        foreach (var word in line.Words)
        {
            bottom = Math.Max(bottom, word.Bottom);
            top = Math.Min(top, word.Top);
        }

        if (owner.ActualLineHeight > 0 && top < double.MaxValue)
            bottom = Math.Max(bottom, top + owner.ActualLineHeight);

        return bottom;
    }

    /// <summary>The right edge of the words currently on <paramref name="line"/>.</summary>
    private static double LineContentRight(CssLineBox line)
    {
        double right = 0;

        foreach (var word in line.Words)
            right = Math.Max(right, word.Right);

        return right;
    }
}
