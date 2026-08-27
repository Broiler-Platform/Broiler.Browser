using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using System.Diagnostics;


namespace Broiler.Layout.Engine;

internal static class CssBoxHelper
{
    public static CssBox CreateBox(HtmlTag tag, Uri baseUrl, CssBox parent = null)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Name == HtmlConstants.Img)
        {
            return new CssBoxImage(parent, tag, baseUrl);
        }
        else if (tag.Name.Equals("object", StringComparison.OrdinalIgnoreCase) &&
                 tag.TryGetAttribute("data") is { } data &&
                 data.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            // <object data="data:image/..."> — treat as a replaced image element.
            // Any nested fallback content will be removed by CorrectObjectBoxes.
            return new CssBoxImage(parent, tag, baseUrl);
        }
        else if (tag.Name == HtmlConstants.Iframe)
        {
            return new CssBox(parent, tag, baseUrl);
        }
        else if (tag.Name == HtmlConstants.Hr)
        {
            return new CssBoxHr(parent, tag, baseUrl);
        }
        else
        {
            return new CssBox(parent, tag, baseUrl);
        }
    }

    public static CssBox CreateBox(CssBox parent, Uri baseUrl, HtmlTag tag = null, CssBox before = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var newBox = new CssBox(parent, tag, baseUrl);
        newBox.InheritStyle();

        // Anonymous boxes (tag == null) are fragments of their parent's inline
        // formatting context — e.g. the wrappers created when content is split
        // around a <br> or a block-level child.  'unicode-bidi' is not inherited,
        // so InheritStyle() does not carry it; without this an anonymous block
        // wrapping a 'unicode-bidi: plaintext' element's content would fall back
        // to 'normal' and mis-resolve per-line bidi direction.  Explicit CSS on
        // pseudo-elements is applied later and still overrides this default.
        if (tag == null)
            newBox.UnicodeBidi = parent.UnicodeBidi;

        if (before != null)
            newBox.SetBeforeBox(before);

        return newBox;
    }

    public static CssBox CreateBlock(Uri baseUrl) => new(null, null, baseUrl) { Display = CssConstants.Block };

    public static CssBox CreateBlock(CssBox parent, Uri baseUrl, HtmlTag tag = null, CssBox before = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var newBox = CreateBox(parent, baseUrl, tag, before);
        newBox.Display = CssConstants.Block;

        return newBox;
    }

    internal static CssRect FirstWordOccourence(CssBox b, CssLineBox line)
    {
        if (b.Words.Count == 0 && b.Boxes.Count == 0)
            return null;

        if (b.Words.Count > 0)
        {
            foreach (CssRect word in b.Words)
            {
                if (line.Words.Contains(word))
                    return word;
            }

            return null;
        }
        else
        {
            foreach (CssBox bb in b.Boxes)
            {
                CssRect w = FirstWordOccourence(bb, line);

                if (w != null)
                    return w;
            }

            return null;
        }
    }

    /// <summary>
    /// A text box that held nothing but collapsible whitespace: its words were collapsed away, so
    /// it looks empty to the intrinsic passes even though it still separates its siblings on the
    /// line. Preserved whitespace (<c>pre</c>, <c>pre-wrap</c>, <c>break-spaces</c>) keeps its
    /// words and is measured normally, so it is deliberately not matched here.
    /// </summary>
    private static bool IsCollapsedWhitespaceSeparator(CssBox box)
    {
        if (box.Words.Count > 0 || box.Boxes.Count > 0 || box.HtmlTag != null)
            return false;

        if (box.WhiteSpace == CssConstants.Pre || box.WhiteSpace == CssConstants.PreWrap
            || box.WhiteSpace == CssConstants.PreLine)
            return false;

        var text = box.Text.Span;
        if (text.Length == 0)
            return false;

        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
                return false;
        }

        return true;
    }

    public static void GetMinimumWidth_LongestWord(CssBox box, ref double maxWidth, ref CssRect maxWidthWord)
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicVisits);

        // A display:none box generates no boxes at all (CSS 2.1 §9.2.4), so it contributes nothing
        // to an intrinsic size. Without this the UA-hidden elements that carry *text* — <style>,
        // <script>, <title> — were measured, and their source text set the min/max-content width of
        // any shrink-to-fit ancestor. A <div style="display:inline-block"> holding one <li> and a
        // stylesheet measured 861px wide instead of 65px.
        if (box.Display == CssConstants.None)
            return;

        if (box.Words.Count > 0)
        {
            foreach (CssRect cssRect in box.Words)
            {
                if (cssRect.Width > maxWidth)
                {
                    maxWidth = cssRect.Width;
                    maxWidthWord = cssRect;
                }
            }
        }
        else
        {
            foreach (CssBox childBox in box.Boxes)
                GetMinimumWidth_LongestWord(childBox, ref maxWidth, ref maxWidthWord);
        }
    }

    public static double GetWidthMarginDeep(CssBox box)
    {
        double sum = 0f;

        if (box.Size.Width > 90999 || (box.ParentBox != null && box.ParentBox.Size.Width > 90999))
        {
            while (box != null)
            {
                sum += box.ActualMarginLeft + box.ActualMarginRight;
                box = box.ParentBox;
            }
        }

        return sum;
    }

    internal static double GetMaximumBottom(CssBox startBox, double currentMaxBottom)
    {
        foreach (var line in startBox.Rectangles.Keys)
            currentMaxBottom = Math.Max(currentMaxBottom, startBox.Rectangles[line].Bottom);

        foreach (var b in startBox.Boxes)
        {
            currentMaxBottom = Math.Max(currentMaxBottom, b.ActualBottom + b.ActualMarginBottom);
            currentMaxBottom = Math.Max(currentMaxBottom, GetMaximumBottom(b, currentMaxBottom));
        }

        return currentMaxBottom;
    }

    /// <summary>An atomic inline-level box (inline-block / inline-table /
    /// inline-flex / inline-grid): inline-level, so it shares a line with siblings
    /// for max-content sizing rather than starting a new one.</summary>
    internal static bool IsAtomicInlineLevel(string display) =>
        display is "inline-block" or "inline-table" or "inline-flex" or "inline-grid";

    /// <summary>
    /// Whether <paramref name="box"/> begins a fresh max-content line rather than continuing the
    /// running one: block-level boxes do, inline-level content — real inline boxes, the atomic
    /// inline-level displays, floats, and anything under <c>white-space: nowrap</c> — does not.
    /// </summary>
    private static bool StartsNewMaxContentLine(CssBox box) =>
        box.Display != CssConstants.Inline
        && box.Display != CssConstants.TableCell
        && !IsAtomicInlineLevel(box.Display)
        && box.WhiteSpace != CssConstants.NoWrap
        && box.Float == CssConstants.None;

    public static void GetMinMaxSumWords(CssBox box, ref double min, ref double maxSum, ref double paddingSum, ref double marginSum, CssBox suppressExplicitWidthFor = null)
    {
        LayoutWorkTrace.Count(LayoutWorkTrace.Counters.IntrinsicVisits);

        // See GetMinimumWidth_LongestWord: a display:none box generates no boxes, so it adds
        // nothing to the running max-content line. This is the max-content half of the same fix.
        if (box.Display == CssConstants.None)
            return;

        double? oldSum = null;

        // Block-level boxes start a new line, so max-content resets the running sum
        // to this line. Inline-*level* content stays on the line and accumulates:
        // real inline boxes (CSS2.1 §10.3.7 floats likewise contribute to the same
        // "line") and — the reason this guard also lists them — **atomic inline-level
        // boxes** (inline-block / inline-table / inline-flex / inline-grid), which are
        // inline-level and sit side by side on one line. Treating them as block reset
        // the line, so N inline-blocks in a row measured as the *widest* one instead
        // of their *sum* (e.g. two 40px inline-blocks → max-content 40, not 80),
        // under-sizing every shrink-to-fit and collapsing max-content/fit-content
        // grid tracks that hold them.
        if (StartsNewMaxContentLine(box))
        {
            oldSum = maxSum;
            maxSum = marginSum;
        }

        // When measuring a grid item's content contribution, its own explicit
        // width is ignored (a percentage width resolves against the track being
        // sized, so it is treated as auto/content — CSS Grid §11.5); descendants'
        // explicit widths still count.
        //
        // CSS Sizing 3 §5.1: more generally, a *percentage* width resolves against
        // the size we are computing (the container's intrinsic width), so it is
        // treated as auto for intrinsic sizing regardless of grid — measuring the
        // box's content instead of the percentage. Without this a `width:100%`
        // child resolves against the containing block and reports the full
        // available width, ballooning a float/inline-block/abspos shrink-to-fit to
        // the container (e.g. a float wrapping a `width:100%` block, or an
        // auto-fill grid item sized 100%, pinned to the viewport not its content).
        bool widthIsPercentage = !string.IsNullOrEmpty(box.Width)
            && box.Width.EndsWith('%')
            && !box.Width.Contains('(');
        bool useExplicitWidth = box != suppressExplicitWidthFor && !widthIsPercentage;

        // CSS2.1 §10.3.5/§10.3.7: When a floated child has an explicit width,
        // use the declared width directly for shrink-to-fit calculation
        // instead of measuring content words.
        if (useExplicitWidth
            && box.Float != CssConstants.None
            && box.Width != CssConstants.Auto
            && !string.IsNullOrEmpty(box.Width))
        {
            double explicitWidth = CssLengthParser.ParseLength(
                box.Width, box.ContainingBlock?.Size.Width ?? 0, box.GetEmHeight());
            paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth
                        + box.ActualPaddingRight + box.ActualPaddingLeft;
            maxSum += explicitWidth;
            min = Math.Max(min, explicitWidth);

            if (oldSum.HasValue)
                maxSum = Math.Max(maxSum, oldSum.Value);
            return;
        }

        // CSS2.1 §17.5.2: Non-floated block-level children (e.g. display:table
        // or display:list-item inside an anonymous table-cell) with explicit
        // width contribute that width to the intrinsic minimum/maximum.
        if (useExplicitWidth
            && box.Display != CssConstants.Inline
            && box.Display != CssConstants.TableCell
            && box.Float == CssConstants.None
            && box.Width != CssConstants.Auto
            && !string.IsNullOrEmpty(box.Width))
        {
            double explicitWidth = CssLengthParser.ParseLength(
                box.Width, box.ContainingBlock?.Size.Width ?? 0, box.GetEmHeight());
            if (explicitWidth > 0)
            {
                paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth
                            + box.ActualPaddingRight + box.ActualPaddingLeft;
                maxSum += explicitWidth;
                min = Math.Max(min, explicitWidth);

                if (oldSum.HasValue)
                    maxSum = Math.Max(maxSum, oldSum.Value);
                return;
            }
        }

        // add the padding 
        paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualPaddingLeft;


        // for tables the padding also contains the spacing between cells
        if (box.Display == CssConstants.Table)
            paddingSum += CssLayoutEngineTable.GetTableSpacing(box);

        // CSS Sizing 3 §5.2.1: a replaced box's intrinsic inline size is its own — it has no
        // contents to walk for one. An <img> carries a word to be measured, but a <canvas>, a
        // <video> or an <svg> carries none: this walk found nothing in it and contributed zero, so
        // a shrink-to-fit box around one came out as wide as its other content and no wider. The
        // canvas of css-sizing/intrinsic-percent-replaced-001 sizes itself correctly from its
        // `height: 100%` and its ratio; it was the float around it that collapsed to nothing.
        if (box.Words.Count == 0
            && box.IntrinsicReplacedSize is { Width: > 0, Height: > 0 } natural)
        {
            box.ResolveReplacedContentSize(natural, box.ContainingBlock?.Size.Width ?? 0,
                out double replacedContentWidth, out _);

            maxSum += replacedContentWidth;
            min = Math.Max(min, replacedContentWidth);
            return;
        }

        if (box.Words.Count > 0)
        {
            // calculate the min and max sum for all the words in the box
            foreach (CssRect word in box.Words)
            {
                maxSum += word.FullWidth + (word.HasSpaceBefore ? word.OwnerBox.ActualWordSpacing : 0);
                min = Math.Max(min, word.Width);
            }

            // remove the last word padding
            if (box.Words.Count > 0 && !box.Words[^1].HasSpaceAfter)
                maxSum -= box.Words[^1].ActualWordSpacing;
        }
        else
        {
            // recursively on all the child boxes
            for (int i = 0; i < box.Boxes.Count; i++)
            {
                CssBox childBox = box.Boxes[i];

                // A <br> forces a line break, so max-content is the widest line:
                // close the running line here and start a fresh one. Otherwise the
                // recursion below (a <br> computes to a block box, which resets and
                // then restores the running sum) leaves the following inline content
                // accumulating on the same line — `A<br>B` would measure as A + B
                // instead of max(A, B), doubling a multi-line item's width.
                if (childBox.IsBrElement)
                {
                    oldSum = oldSum.HasValue ? Math.Max(oldSum.Value, maxSum) : maxSum;
                    maxSum = marginSum;
                    continue;
                }

                // A collapsible space *between* two atomic inline-level siblings is a real advance
                // on the line: `<span>A</span> <span>B</span>` is one space wider than the same
                // markup with no space. But that space is a text box of its own, and collapsing
                // clears its words (a space is normally carried as a HasSpaceBefore/After flag on
                // an adjacent word, and here the neighbours are in *other* boxes), so the recursion
                // measured it as zero. The shrink-to-fit container then came out exactly one space
                // too narrow and the last item wrapped — two 10px inline-blocks measured 20px and
                // stacked, where they need 24px to sit side by side. Layout itself lays the space
                // out; only the intrinsic measurement was missing it.
                if (IsCollapsedWhitespaceSeparator(childBox))
                {
                    double space = childBox.ActualWordSpacing;
                    if (double.IsNaN(space))
                        space = box.ActualWordSpacing;
                    if (!double.IsNaN(space))
                        maxSum += space;
                    continue;
                }

                marginSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;

                // CSS Sizing 3 §5: an inline-level child sits on the running line, so its own
                // horizontal margins advance that line and belong in the sum. Only the
                // block-level case was covered — a block child restarts its line at marginSum,
                // which carries them — so the margins of an inline box, and of an inline
                // *replaced* box in particular, contributed nothing. A shrink-to-fit container
                // around one then came out exactly those margins too narrow: MediaWiki wraps
                // every thumbnail in a `display: table` figure whose image carries `margin: 3px`,
                // so the figure measured 6px under, and `max-width` scaled the photo down to fit
                // a box that should have fitted it exactly.
                if (!StartsNewMaxContentLine(childBox) && childBox.Display != CssConstants.None)
                    maxSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;

                GetMinMaxSumWords(childBox, ref min, ref maxSum, ref paddingSum, ref marginSum);

                marginSum -= childBox.ActualMarginLeft + childBox.ActualMarginRight;
            }
        }

        // max sum is max of all the lines in the box
        if (oldSum.HasValue)
            maxSum = Math.Max(maxSum, oldSum.Value);
    }

    /// <summary>
    /// CSS Text 3 §4.1.1: collapsible white space at the very start of a
    /// formatting context's first line and the very end of its last line is
    /// removed.  Broiler carries a collapsed space as word-spacing on the
    /// neighbouring word (<c>HasSpaceBefore</c>/<c>HasSpaceAfter</c>), so the
    /// preferred-width sum in <see cref="GetMinMaxSumWords"/> double-counts the
    /// leading space-before of <paramref name="box"/>'s first content word and
    /// the trailing space-after of its last word.  Returns the total of those
    /// two edge spacings (in CSS px) so the caller can subtract them from the
    /// shrink-to-fit width; a box that begins/ends on a real word (no edge
    /// space) contributes nothing.
    /// </summary>
    internal static double EdgeWhitespaceSpacing(CssBox box)
    {
        double sum = 0;

        var first = FirstContentWord(box);
        if (first != null && first.HasSpaceBefore && !first.IsImage && first.OwnerBox != null)
            sum += first.OwnerBox.ActualWordSpacing;

        var last = LastContentWord(box);
        if (last != null && last.HasSpaceAfter && !last.IsImage && last.OwnerBox != null)
            sum += last.OwnerBox.ActualWordSpacing;

        return sum > 0 ? sum : 0;
    }

    /// <summary>
    /// Returns the first word in document order within <paramref name="box"/>'s
    /// in-flow inline content, or <c>null</c> if it has none.  Out-of-flow
    /// children (<c>display:none</c>) do not form the inline edge.
    /// </summary>
    private static CssRect FirstContentWord(CssBox box)
    {
        if (box.Words.Count > 0)
            return box.Words[0];

        foreach (CssBox child in box.Boxes)
        {
            if (child.Display == CssConstants.None)
                continue;
            var w = FirstContentWord(child);
            if (w != null)
                return w;
        }

        return null;
    }

    /// <summary>
    /// Returns the last word in document order within <paramref name="box"/>'s
    /// in-flow inline content, or <c>null</c> if it has none.
    /// </summary>
    private static CssRect LastContentWord(CssBox box)
    {
        if (box.Words.Count > 0)
            return box.Words[^1];

        for (int i = box.Boxes.Count - 1; i >= 0; i--)
        {
            if (box.Boxes[i].Display == CssConstants.None)
                continue;
            var w = LastContentWord(box.Boxes[i]);
            if (w != null)
                return w;
        }

        return null;
    }

    /// <summary>
    /// CSS2.1 §9.5.2: Returns the maximum bottom outer edge of preceding
    /// floats that the given box needs to clear, considering the box's
    /// <c>clear</c> direction (<c>left</c>, <c>right</c>, or <c>both</c>).
    /// </summary>
    public static double GetMaxFloatBottom(CssBox box)
    {
        double maxBottom = 0;
        List<(string tag, double bottom)> considered = null;

        if (box.ParentBox == null)
            return maxBottom;

        string clearDir = box.Clear;

        // Walk up the ancestor chain to find floats in the same block
        // formatting context (BFC).  Floats from ancestor-level siblings
        // are relevant for clearance even when the cleared element is
        // nested deeper (CSS2.1 §9.5.2).
        CssBox current = box;
        while (current.ParentBox != null)
        {
            foreach (var sibling in current.ParentBox.Boxes)
            {
                if (sibling == current) break;
                CollectMaxFloatBottom(sibling, clearDir, ref maxBottom, ref considered);
            }

            // Stop at BFC boundaries — floats in an outer BFC don't
            // participate in clearance for elements in an inner BFC.
            if (EstablishesBfc(current.ParentBox))
                break;

            current = current.ParentBox;
        }

        if (considered != null && considered.Count > 0)
        {
            Debug.WriteLine($"[ClearFloat] Clearance for <{box.HtmlTag?.Name ?? "?"}> clear={box.Clear}: " +
                $"considered {considered.Count} float(s), maxBottom={maxBottom:F1}");
            foreach (var (tag, bottom) in considered)
                Debug.WriteLine($"  - <{tag}> bottom={bottom:F1}");
        }

        return maxBottom;
    }

    /// <summary>
    /// Collects the maximum bottom coordinate of floats in the same
    /// block formatting context (BFC) that match the <paramref name="clearDir"/>
    /// direction.  Floated elements establish a new BFC, so their descendant
    /// floats are excluded from clearance calculations outside.
    /// </summary>
    private static void CollectMaxFloatBottom(CssBox box, string clearDir, ref double maxBottom, ref List<(string tag, double bottom)> considered)
    {
        if (box.Float != CssConstants.None)
        {
            // CSS2.1 §9.5.2: Only consider floats in the matching direction.
            // clear:left → only left floats, clear:right → only right floats,
            // clear:both → all floats.
            bool matchesDirection = clearDir == "both"
                || string.Equals(box.Float, clearDir, StringComparison.OrdinalIgnoreCase);

            if (!matchesDirection)
                return;

            // Compute the float's margin-box bottom ("bottom outer edge"
            // per CSS2.1 §9.5.2) so that clearance positions the cleared
            // element below the float's full margin box.
            // CSS2.1 §10.5: Percentage heights resolve to auto when the
            // containing block's height is not explicitly specified —
            // use ActualBottom (layout-computed) in that case.
            double bottom;
            bool hasExplicitHeight = box.Height != CssConstants.Auto && !string.IsNullOrEmpty(box.Height);

            if (hasExplicitHeight && !box.HeightPercentageResolvesToAuto())
                bottom = box.Location.Y + box.ActualHeight
                    + box.ActualPaddingTop + box.ActualPaddingBottom
                    + box.ActualBorderTopWidth + box.ActualBorderBottomWidth
                    + box.ActualMarginBottom;
            else
                bottom = box.ActualBottom
                    + box.ActualMarginBottom;

            maxBottom = Math.Max(maxBottom, bottom);

            considered ??= [];
            considered.Add((box.HtmlTag?.Name ?? box.Display, bottom));

            // Float establishes a new BFC – don't recurse into descendants.
            return;
        }

        foreach (var child in box.Boxes)
            CollectMaxFloatBottom(child, clearDir, ref maxBottom, ref considered);
    }

    /// <summary>
    /// Whether <paramref name="box"/> is the document's root element box — the
    /// <c>&lt;html&gt;</c> box the anonymous document box wraps.
    /// </summary>
    /// <remarks>
    /// CSS2.1 §8.3.1 exempts the root element from margin collapsing, which is what keeps
    /// the body's bottom margin inside the document's height: the canvas is the root's
    /// margin box (§11.1.1), so a collapsed-through body margin shortens the whole page.
    /// Acid1's <c>--full-page</c> capture came out 405px tall instead of 420 for exactly
    /// that reason — the black body border ended up flush with the bottom edge of the image
    /// with none of the blue canvas below it.
    /// </remarks>
    internal static bool IsRootElement(CssBox box) =>
        box.HtmlTag is { } tag && tag.Name.Equals("html", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the effective bottom margin for a box, accounting for
    /// parent-child bottom-margin collapse (CSS 2.1 §8.3.1).
    /// When a box has no bottom border, no bottom padding, and auto height,
    /// the last in-flow block-level child's bottom margin collapses with
    /// the box's own bottom margin.  This is applied recursively.
    /// </summary>
    internal static double GetPropagatedMarginBottom(CssBox box)
    {
        double mb = box.ActualMarginBottom;

        if (box.ActualBorderBottomWidth > 0.1 || box.ActualPaddingBottom > 0.1)
            return mb;

        // CSS2.1 §8.3.1: "Margins of the root element's box do not collapse." The root
        // keeps its own margin and its last child's margin stays inside its height, so
        // nothing propagates out. See IsRootElement.
        if (IsRootElement(box))
            return mb;

        if (box.Height != CssConstants.Auto && !string.IsNullOrEmpty(box.Height))
        {
            bool resolvedToAuto = box.Height.Contains('%')
                && (box.ContainingBlock.Height == CssConstants.Auto
                    || string.IsNullOrEmpty(box.ContainingBlock.Height));
            if (!resolvedToAuto)
                return mb;
        }

        // Find last in-flow block-level child (CSS 2.1 §8.3.1).
        CssBox? lastInFlow = null;
        foreach (var child in box.Boxes)
        {
            if (child.Float != CssConstants.None
                || child.Position == CssConstants.Absolute
                || child.Position == CssConstants.Fixed)
                continue;

            if (child.Display == CssConstants.Inline
                || child.Display == CssConstants.InlineBlock)
                continue;

            // CSS2.1 §9.2.4 again: a box that is not generated cannot be the last in-flow child
            // whose bottom margin propagates out of its parent.
            if (child.Display == CssConstants.None)
                continue;

            lastInFlow = child;
        }

        if (lastInFlow == null)
            return mb;

        double childMb = GetPropagatedMarginBottom(lastInFlow);

        // Collapse: max(positives,0) + min(negatives,0)
        double maxPos = Math.Max(Math.Max(mb, 0), Math.Max(childMb, 0));
        double minNeg = Math.Min(Math.Min(mb, 0), Math.Min(childMb, 0));
        return maxPos + minNeg;
    }

    /// <summary>
    /// Computes the vertical offset applied by <c>position: relative</c>.
    /// CSS2.1 §9.4.3: <c>top</c> takes precedence over <c>bottom</c>.
    /// Returns 0 if the element is not relatively positioned or has no offset.
    /// </summary>
    internal static double GetRelativeOffsetY(CssBoxProperties box)
    {
        bool hasTop = box.Top != null && box.Top != CssConstants.Auto;
        bool hasBottom = box.Bottom != null && box.Bottom != CssConstants.Auto;

        if (hasTop)
            return CssLengthParser.ParseLength(box.Top, box.Size.Height, box.GetEmHeight());

        if (hasBottom)
            return -CssLengthParser.ParseLength(box.Bottom, box.Size.Height, box.GetEmHeight());

        return 0;
    }

    /// <summary>
    /// Computes the horizontal offset applied by <c>position: relative</c>.
    /// CSS2.1 §9.4.3: <c>left</c> takes precedence over <c>right</c> (in LTR).
    /// Returns 0 if the element is not relatively positioned or has no offset.
    /// </summary>
    internal static double GetRelativeOffsetX(CssBoxProperties box)
    {
        bool hasLeft = box.Left != null && box.Left != CssConstants.Auto;
        bool hasRight = box.Right != null && box.Right != CssConstants.Auto;

        if (hasLeft)
            return CssLengthParser.ParseLength(box.Left, box.Size.Width, box.GetEmHeight());

        if (hasRight)
            return -CssLengthParser.ParseLength(box.Right, box.Size.Width, box.GetEmHeight());

        return 0;
    }

    /// <summary>
    /// Collects all float boxes in the same block formatting context that
    /// precede <paramref name="box"/> in the DOM tree. This includes floats
    /// nested inside non-BFC siblings (e.g., floated <c>li</c> elements
    /// inside a non-floated <c>ul</c>) and floats that are siblings of
    /// ancestor elements when those ancestors do not establish a new BFC
    /// (CSS2.1 §9.4.1).
    /// </summary>
    internal static List<CssBox> CollectPrecedingFloatsInBfc(CssBox box)
    {
        var result = new List<CssBox>();
        if (box.ParentBox == null) return result;

        // Collect preceding sibling floats (and their non-BFC subtrees).
        foreach (var sibling in box.ParentBox.Boxes)
        {
            if (sibling == box) break;
            CollectFloatsInSubtree(sibling, result);
        }

        // Walk up ancestor chain: collect floats from each ancestor's
        // preceding siblings while the ancestor does not establish a BFC.
        var current = box.ParentBox;
        while (current != null && current.ParentBox != null)
        {
            if (EstablishesBfc(current))
                break;

            foreach (var sibling in current.ParentBox.Boxes)
            {
                if (sibling == current) break;
                CollectFloatsInSubtree(sibling, result);
            }

            current = current.ParentBox;
        }

        return result;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="box"/> establishes a new
    /// block formatting context (CSS2.1 §9.4.1, CSS Box Alignment §5.4).
    /// <para>
    /// The single source of truth for the question. Four near-copies of this
    /// list had drifted apart across the engine (one had never gained the
    /// flex/grid displays), so a display that establishes a BFC was recognised
    /// in some float calculations and not others — which is how
    /// <c>display: flow-root</c>, whose <em>only</em> job is to establish one,
    /// came to establish none. The callers that guard on <c>float</c>/
    /// <c>position</c> themselves before asking still do; those conditions are
    /// simply redundant there, not wrong.
    /// </para>
    /// </summary>
    internal static bool EstablishesBfc(CssBox box)
    {
        return box.Float != CssConstants.None
            || box.Display == CssConstants.InlineBlock
            || box.Display == CssConstants.TableCell
            // CSS Display 3 §2.5: `flow-root` is exactly "block box that
            // establishes a new block formatting context".
            || box.Display == "flow-root"
            || box.Display is "flex" or "inline-flex" or "grid" or "inline-grid"
            || box.Position == CssConstants.Absolute
            || box.Position == CssConstants.Fixed
            || (box.Overflow != null && box.Overflow != CssConstants.Visible)
            || (box.AlignContent != null && box.AlignContent != "normal");
    }

    private static void CollectFloatsInSubtree(CssBox root, List<CssBox> result)
    {
        if (root.Float != CssConstants.None && root.Display != CssConstants.None)
        {
            result.Add(root);
            // Float establishes a new BFC – don't recurse into descendants.
            return;
        }

        // CSS2.1 §9.5: Don't recurse into elements that establish a new
        // block formatting context — their inner floats don't participate
        // in the parent BFC's float list.
        if (EstablishesBfc(root))
            return;

        foreach (var child in root.Boxes)
            CollectFloatsInSubtree(child, result);
    }

    /// <summary>
    /// CSS2.1 §8.3.1: Returns <c>true</c> if a box is "empty" — its own
    /// top and bottom margins are adjoining and collapse through.
    /// Conditions: min-height is zero, no top/bottom borders or padding,
    /// height is 0 or auto (or percentage that resolves to auto), no line
    /// boxes, and all in-flow children's margins also collapse.
    /// </summary>
    internal static bool IsEmptyCollapsible(CssBox box)
    {
        // CSS2.1 §8.3.1: a box that establishes a new block formatting context
        // (float, inline-block, grid/flex, overflow≠visible, abspos, table-cell)
        // does not collapse its top and bottom margins through itself — even with
        // no content — so an empty such box still separates its neighbours by both
        // its margins. Chromium keeps an empty overflow:hidden / grid container's
        // top and bottom margins distinct; only a plain in-flow block collapses
        // through. (Floats/abspos are already excluded from flow collapsing, but
        // covering them here keeps the predicate self-consistent.)
        if (EstablishesBfc(box))
            return false;

        if (box.ActualBorderTopWidth > 0.1 || box.ActualBorderBottomWidth > 0.1)
            return false;

        if (box.ActualPaddingTop > 0.1 || box.ActualPaddingBottom > 0.1)
            return false;

        // Check if height resolves to zero/auto
        if (box.Height != CssConstants.Auto && !string.IsNullOrEmpty(box.Height))
        {
            bool resolvedToAuto = box.Height.Contains('%')
                && (box.ContainingBlock.Height == CssConstants.Auto
                    || string.IsNullOrEmpty(box.ContainingBlock.Height));

            if (!resolvedToAuto)
            {
                double h = CssLengthParser.ParseLength(box.Height, box.Size.Height, box.GetEmHeight());
                if (h > 0.1)
                    return false;
            }
        }

        // Zero content height — ActualBottom should equal Location.Y
        // (tolerance 0.5 accounts for sub-pixel rounding in layout)
        if (Math.Abs(box.ActualBottom - box.Location.Y) > 0.5)
            return false;

        // Must not contain any line boxes with actual content.
        // CreateLineBoxes always creates at least one CssLineBox for any
        // element that enters the inline-formatting path, even if the
        // element is empty.  An empty line box (no words) does not
        // constitute "content" for margin-through-collapse purposes.
        //
        // CSS2.1 §8.3.1: When height is explicitly 0, line boxes contain
        // overflowing content that doesn't prevent margin collapse.  Only
        // check for line-box content when height is auto.
        bool hasExplicitZeroHeight = box.Height != CssConstants.Auto
            && !string.IsNullOrEmpty(box.Height);

        if (!hasExplicitZeroHeight)
        {
            foreach (var lb in box.LineBoxes)
            {
                if (lb.Words.Count > 0)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Collects the maximum positive and minimum negative margins from an
    /// empty collapsible box and all its in-flow children (recursively for
    /// children that are also empty and collapsible).
    /// </summary>
    internal static void CollectEmptyBoxMargins(CssBox box, ref double maxPos, ref double maxNeg)
    {
        maxPos = Math.Max(maxPos, Math.Max(box.ActualMarginTop, 0));
        maxPos = Math.Max(maxPos, Math.Max(box.ActualMarginBottom, 0));
        maxNeg = Math.Min(maxNeg, Math.Min(box.ActualMarginTop, 0));
        maxNeg = Math.Min(maxNeg, Math.Min(box.ActualMarginBottom, 0));

        foreach (var child in box.Boxes)
        {
            if (child.Float != CssConstants.None
                || child.Position == CssConstants.Absolute
                || child.Position == CssConstants.Fixed)
                continue;

            // CSS2.1 §9.2.4: a display:none element generates no box, so it has no margins to
            // collapse through. Collecting them anyway hands a hidden element's margin to the
            // next visible sibling: www.mediawiki.org's empty .vector-column-start holds two
            // display:none pinned containers with `margin-bottom: 32px`, and that 32px was
            // separating the site notice from the article.
            if (child.Display == CssConstants.None)
                continue;

            maxPos = Math.Max(maxPos, Math.Max(child.ActualMarginTop, 0));
            maxPos = Math.Max(maxPos, Math.Max(child.ActualMarginBottom, 0));
            maxNeg = Math.Min(maxNeg, Math.Min(child.ActualMarginTop, 0));
            maxNeg = Math.Min(maxNeg, Math.Min(child.ActualMarginBottom, 0));

            if (IsEmptyCollapsible(child))
                CollectEmptyBoxMargins(child, ref maxPos, ref maxNeg);
        }
    }

    /// <summary>
    /// Returns the effective bottom margin for a box, accounting for margins
    /// that collapse through the box when it is "empty" per CSS2.1 §8.3.1.
    /// For non-empty boxes returns <see cref="CssBoxProperties.ActualMarginBottom"/>.
    /// </summary>
    internal static double GetEffectiveMarginBottom(CssBox box)
    {
        if (!IsEmptyCollapsible(box))
            return box.ActualMarginBottom;

        double maxPos = 0, maxNeg = 0;
        CollectEmptyBoxMargins(box, ref maxPos, ref maxNeg);

        double collapsed = maxPos + maxNeg;
        return collapsed - box.CollapsedMarginTop;
    }
}
