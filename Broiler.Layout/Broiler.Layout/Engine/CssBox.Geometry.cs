using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal partial class CssBox : CssBoxProperties, IDisposable
{
    /// <summary>
    /// CSS Box Alignment Level 3 §"Aligning Abspos"
    /// (https://drafts.csswg.org/css-align-3/#align-abspos): resolves
    /// justify-self / align-self for an absolutely-positioned box along ONE
    /// axis. The box is aligned within its inset-modified containing block
    /// (IMCB), then — in the DEFAULT overflow mode (no safe/unsafe keyword) —
    /// its final position is clamped to the range that <b>encloses both the
    /// IMCB and the actual containing block (CB)</b>, with the start edge
    /// winning when the two conflict:
    /// <code>
    ///   p0 = imcbStart + alignmentOffset                 // unsafe align in IMCB
    ///   lo = min(imcbStart, cbStart)
    ///   hi = max(imcbStart + imcbSize, cbStart + cbSize) - boxSize
    ///   pos = startIsLow ? max(lo, min(p0, hi))          // start = low  edge
    ///                    : min(hi, max(p0, lo))          // start = high edge
    /// </code>
    /// Explicit <c>unsafe</c> → align per keyword, no clamping. Explicit
    /// <c>safe</c> → on overflow, fall back to start alignment within the IMCB
    /// (the legacy behaviour, preserved unchanged).
    ///
    /// <para>
    /// DIAGNOSTIC NOTE (WPT issue #1100, css-align/abspos cluster): if abspos
    /// elements carrying align-self/justify-self render in the wrong place, or
    /// the <c>css-align/abspos/{align,justify}-self-default-overflow-*</c> WPT
    /// family regresses, THIS clamp and the IMCB construction at the four call
    /// sites (the "Post-layout self-alignment for absolutely positioned
    /// elements" block above) are the first suspects. The historic bug was
    /// clamping to the IMCB alone instead of the IMCB∪CB union, which placed the
    /// box correctly only when it fit inside the IMCB. The eight pinned offsets
    /// of <c>justify-self-default-overflow-htb-ltr-htb.html</c> live in
    /// <c>Broiler.Layout.Tests.AbsposSelfAlignmentTests</c> — run those first.
    /// </para>
    /// </summary>
    /// <param name="alignment">The justify-self/align-self value (may carry a
    /// leading <c>safe</c>/<c>unsafe</c> keyword).</param>
    /// <param name="imcbStart">Start coordinate of the IMCB along this axis
    /// (absolute layout coords).</param>
    /// <param name="imcbSize">Size of the IMCB along this axis.</param>
    /// <param name="cbStart">Start coordinate of the actual containing-block
    /// padding box along this axis.</param>
    /// <param name="cbSize">Size of the containing-block padding box along this
    /// axis.</param>
    /// <param name="boxSize">Border-box size of the aligned box along this
    /// axis.</param>
    /// <param name="isRtl">Whether the box's inline direction is RTL — flips
    /// start/end keyword resolution. (Does not affect <c>center</c>.)</param>
    /// <param name="startIsLow">Whether the containing block's start edge is the
    /// LOW coordinate edge along this axis (true for LTR inline / top-down
    /// block; false e.g. for an RTL inline axis or a vertical-rl block axis).
    /// Drives the overflow tie-break.</param>
    /// <returns>Offset to add to <paramref name="imcbStart"/>; the caller
    /// computes <c>pos = imcbStart + returnValue + leading margin</c>.</returns>
    /// <summary>
    /// Drops a leading <c>safe</c>/<c>unsafe</c> overflow keyword from an
    /// alignment value so the static-position path can force its own
    /// (always-unsafe) overflow handling by re-prefixing <c>unsafe</c>.
    /// </summary>
    private static string StripSafeUnsafe(string value)
    {
        if (value.StartsWith("safe ", StringComparison.OrdinalIgnoreCase))
            return value[5..].Trim();

        if (value.StartsWith("unsafe ", StringComparison.OrdinalIgnoreCase))
            return value[7..].Trim();

        return value;
    }

    internal static double ResolveAbsposSelfAlignment(
        string alignment,
        double imcbStart, double imcbSize,
        double cbStart, double cbSize,
        double boxSize, bool isRtl, bool startIsLow,
        bool? selfStartIsHigh = null)
    {
        double freeSpace = imcbSize - boxSize;

        // `start`/`end` resolve against the alignment CONTAINER's start edge
        // (whether that is the high/far edge of the axis is `isRtl`).
        // `self-start`/`self-end` resolve against the BOX's OWN start edge, which
        // differs when the box's writing-mode/direction differ from the container's
        // (WPT css-align/abspos/align-self-* mixed-writing-mode tests). When the
        // caller does not supply the box-relative flip we fall back to the
        // container's (correct whenever the two share a writing mode).
        bool selfHigh = selfStartIsHigh ?? isRtl;

        // Strip an explicit safe/unsafe prefix. Track whether one was present:
        // the abspos DEFAULT mode is neither plain "safe" nor "unsafe" — it
        // aligns unsafely in the IMCB and then clamps to the IMCB∪CB union.
        string a = alignment;
        bool hasKeyword = false;
        bool isSafe = true;

        if (a.StartsWith("safe ", StringComparison.OrdinalIgnoreCase))
        {
            a = a[5..].Trim();
            hasKeyword = true;
            isSafe = true;
        }
        else if (a.StartsWith("unsafe ", StringComparison.OrdinalIgnoreCase))
        {
            a = a[7..].Trim();
            hasKeyword = true;
            isSafe = false;
        }

        var dx = a switch
        {
            "center" => freeSpace / 2,
            "end" or "flex-end" or "last baseline" or "last-baseline" => isRtl ? 0 : freeSpace,
            "self-end" => selfHigh ? 0 : freeSpace,
            "right" => freeSpace,
            "start" or "flex-start" or "baseline" or "first baseline" or "first-baseline" => isRtl ? freeSpace : 0,
            "self-start" => selfHigh ? freeSpace : 0,
            "left" => 0,
            _ => 0,// fallback: start-aligned
        };

        // Explicit safe/unsafe: preserve the legacy single-box behaviour.
        if (hasKeyword)
        {
            // Safe overflow: clamp so the element does not overflow the
            // strong (start) edge of the IMCB in the writing direction.
            if (isSafe)
            {
                if (isRtl)
                    dx = Math.Min(dx, Math.Max(freeSpace, 0)); // strong edge = right
                else
                    dx = Math.Max(dx, 0);                       // strong edge = left
            }
            // Unsafe: honour the alignment with no clamping.
            return dx;
        }

        // DEFAULT overflow mode: clamp the resolved POSITION to the union of the
        // IMCB and the actual containing block, with the start edge winning when
        // the box is larger than the union (lo > hi). See the summary/diagnostic
        // note above; validated against the htb-ltr-htb WPT offsets.
        double pos = imcbStart + dx;
        double lo = Math.Min(imcbStart, cbStart);
        double hi = Math.Max(imcbStart + imcbSize, cbStart + cbSize) - boxSize;
        pos = startIsLow
            ? Math.Max(lo, Math.Min(pos, hi))
            : Math.Min(hi, Math.Max(pos, lo));

        return pos - imcbStart;
    }

    internal void OffsetTop(double amount)
    {
        List<CssLineBox> lines = [.. Rectangles.Keys];

        foreach (CssLineBox line in lines)
        {
            RectangleF r = Rectangles[line];
            Rectangles[line] = new RectangleF(r.X, (float)(r.Y + amount), r.Width, r.Height);
        }

        foreach (CssRect word in Words)
            word.Top += amount;

        foreach (CssBox b in Boxes)
        {
            // CSS2.1 §9.6.1: position:fixed elements are positioned relative
            // to the viewport and must not be shifted by ancestor offsets
            // (e.g. a parent's position:relative visual offset).
            if (b.Position != CssConstants.Fixed)
                b.OffsetTop(amount);
        }

        _listItemBox?.OffsetTop(amount);

        Location = new PointF(Location.X, (float)(Location.Y + amount));
    }

    internal void OffsetLeft(double amount)
    {
        List<CssLineBox> lines = [.. Rectangles.Keys];

        foreach (CssLineBox line in lines)
        {
            RectangleF r = Rectangles[line];
            Rectangles[line] = new RectangleF((float)(r.X + amount), r.Y, r.Width, r.Height);
        }

        foreach (CssRect word in Words)
            word.Left += amount;

        foreach (CssBox b in Boxes)
        {
            // CSS2.1 §9.6.1: position:fixed elements are positioned relative
            // to the viewport and must not be shifted by ancestor offsets.
            if (b.Position != CssConstants.Fixed)
                b.OffsetLeft(amount);
        }

        _listItemBox?.OffsetLeft(amount);

        Location = new PointF((float)(Location.X + amount), Location.Y);
    }

    internal void OffsetRectangle(CssLineBox lineBox, double gap)
    {
        if (Rectangles.TryGetValue(lineBox, out RectangleF r))
            Rectangles[lineBox] = new RectangleF(r.X, (float)(r.Y + gap), r.Width, r.Height);
    }

    internal void RectanglesReset() => Rectangles.Clear();

}
