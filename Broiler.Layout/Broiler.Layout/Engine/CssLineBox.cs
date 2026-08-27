using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal sealed class CssLineBox
{
    public CssLineBox(CssBox ownerBox)
    {
        Rectangles = [];
        RelatedBoxes = [];
        Words = [];
        OwnerBox = ownerBox;
        OwnerBox.LineBoxes.Add(this);
    }

    public List<CssBox> RelatedBoxes { get; }
    public List<CssRect> Words { get; }
    public CssBox OwnerBox { get; }
    public Dictionary<CssBox, RectangleF> Rectangles { get; }

    public double LineBottom
    {
        get
        {
            double bottom = 0;

            foreach (var rect in Rectangles)
                bottom = Math.Max(bottom, rect.Value.Bottom);

            return bottom;
        }
    }

    internal void ReportExistanceOf(CssRect word)
    {
        if (!Words.Contains(word))
            Words.Add(word);

        if (!RelatedBoxes.Contains(word.OwnerBox))
            RelatedBoxes.Add(word.OwnerBox);
    }

    internal List<CssRect> WordsOf(CssBox box)
    {
        List<CssRect> r = [];

        foreach (CssRect word in Words)
            if (word.OwnerBox.Equals(box))
                r.Add(word);

        return r;
    }

    internal void UpdateRectangle(CssBox box, double x, double y, double r, double b)
    {
        double leftspacing = box.ActualBorderLeftWidth + box.ActualPaddingLeft;
        double rightspacing = box.ActualBorderRightWidth + box.ActualPaddingRight;
        double topspacing = box.ActualBorderTopWidth + box.ActualPaddingTop;
        double bottomspacing = box.ActualBorderBottomWidth + box.ActualPaddingTop;

        if ((box.FirstHostingLineBox != null && box.FirstHostingLineBox.Equals(this)) || box.IsImage)
            x -= leftspacing;

        if ((box.LastHostingLineBox != null && box.LastHostingLineBox.Equals(this)) || box.IsImage)
            r += rightspacing;

        if (!box.IsImage)
        {
            y -= topspacing;
            b += bottomspacing;
        }

        if (!Rectangles.TryGetValue(box, out RectangleF f))
        {
            Rectangles.Add(box, RectangleF.FromLTRB((float)x, (float)y, (float)r, (float)b));
        }
        else
        {
            Rectangles[box] = RectangleF.FromLTRB(
                (float)Math.Min(f.X, x), (float)Math.Min(f.Y, y),
                (float)Math.Max(f.Right, r), (float)Math.Max(f.Bottom, b));
        }

        if (box.ParentBox != null && box.ParentBox.IsInline)
            UpdateRectangle(box.ParentBox, x, y, r, b);
    }

    /// <summary>
    /// Projects this line's per-box rectangles onto the boxes, as the per-line map
    /// the paint walker and <c>FragmentTreeBuilder</c> read back.
    /// </summary>
    /// <remarks>
    /// The write overwrites rather than inserts, because the projection has to be
    /// idempotent: a line box can reach this twice. <c>CreateLineBoxes</c> empties
    /// <see cref="CssBox.LineBoxes"/> when it starts, so a layout pass that lands
    /// inside another one for the same block — a host callback made from inside the
    /// flow (text measurement, an image that completes synchronously) that re-enters
    /// <c>PerformLayout</c> — leaves the inner pass's already-assigned lines in the
    /// list, and the outer pass then walks the same list and re-projects them. An
    /// insert throws <see cref="ArgumentException"/> on the second one, which
    /// <c>PerformLayout</c> catches as a layout error, so the block loses the rest of
    /// its lines and everything below it lays out from a half-finished pass. The
    /// outer pass has just recomputed each line's rectangles (BubbleRectangles and
    /// vertical alignment run immediately before this), so overwriting is also what
    /// leaves the boxes agreeing with the line boxes they came from.
    /// </remarks>
    internal void AssignRectanglesToBoxes()
    {
        foreach (CssBox b in Rectangles.Keys)
            b.Rectangles[this] = Rectangles[b];
    }

    internal void SetBaseLine(CssBox b, double baseline)
    {
        //TODO: Aqui me quede, checar poniendo "by the" con un font-size de 3em
        List<CssRect> ws = WordsOf(b);

        if (!Rectangles.TryGetValue(b, out RectangleF r))
            return;

        // CSS 2.1 §10.8.1: For inline-block boxes, vertical-align adjusts
        // the position of the entire atomic box.  Move the box's rectangle
        // and its Location/ActualBottom directly.
        if (b.Display == CssConstants.InlineBlock)
        {
            bool usesDefaultBaseline = string.IsNullOrEmpty(b.VerticalAlign)
                || b.VerticalAlign == CssConstants.Baseline;

            // The default `baseline` case used to return here, on the premise — written down in
            // the <img> branch below — that an inline-block's flow position is already on the
            // baseline. It is not: the flow puts it at the top of the line, exactly like an image.
            // That is only harmless while the box's baseline really is at its top, so the boxes
            // this skipped came out top-aligned; two inline-blocks of different heights on one
            // line, and an inline <svg> beside a taller one, sat with their tops flush instead of
            // their bottoms. Only a box whose baseline *is* its bottom margin edge is moved here
            // (CssBox.UsesBottomMarginEdgeBaseline) — an inline-block that draws its own text
            // keeps the position it has always had, because its baseline is not modelled.
            if (usesDefaultBaseline && !b.UsesBottomMarginEdgeBaseline)
                return;

            double inlineBlockShift = baseline - r.Top;
            if (Math.Abs(inlineBlockShift) > 0.01)
            {
                // Moving the box has to move what is in it. Descendant positions are absolute, so
                // rewriting this box's own rectangle and Location alone left its content behind —
                // which no existing case noticed only because an inline-block being aligned had
                // nothing inside it to leave. OffsetTop walks the subtree; Location and
                // ActualBottom are then set to the aligned position, as they were before, because
                // the box's own placement is what the rest of the pass reads back.
                b.OffsetTop(inlineBlockShift);
                Rectangles[b] = new RectangleF(r.X, (float)baseline, r.Width, r.Height);
                b.Location = new PointF(b.Location.X, (float)baseline);
                b.ActualBottom = baseline + r.Height;
            }
            return;
        }

        // CSS2.1 §10.8: an inline replaced element is an atomic box too, and aligning it means
        // moving the box — paint reads an <img>'s geometry off the box (FragmentTreeBuilder reads
        // Location/ActualBottom and only the *source* rect off the word), so moving the word alone
        // moved nothing on screen. An image is placed by the flow at the line's top and always
        // needs this: a 30px and a 90px image on one line came out top-aligned rather than
        // standing on a shared baseline. The same is true of an atomic inline-block, which the
        // branch above now moves for the same reason. Re-running the alignment is
        // idempotent, because an atomic box that has been moved reports the same baseline it was
        // aligned to.
        if (b.IsImage)
        {
            double shift = baseline - r.Top;
            if (Math.Abs(shift) > 0.01)
            {
                Rectangles[b] = new RectangleF(r.X, (float)baseline, r.Width, r.Height);
                b.Location = new PointF(b.Location.X, (float)baseline);
                b.ActualBottom = baseline + r.Height;
                foreach (var word in ws)
                    word.Top += shift;
            }
            return;
        }

        //Save top of words related to the top of rectangle
        double gap = 0f;

        if (ws.Count > 0)
        {
            gap = ws[0].Top - r.Top;
        }
        else
        {
            CssRect firstw = CssBoxHelper.FirstWordOccourence(b, this);

            if (firstw != null)
                gap = firstw.Top - r.Top;
        }

        // The `baseline` parameter is the desired word.Top (visual text
        // top coordinate) already computed by ApplyVerticalAlignment.
        double newtop = baseline;

        if (b.ParentBox != null && b.ParentBox.Rectangles.ContainsKey(this) && r.Height < b.ParentBox.Rectangles[this].Height)
        {
            //Do this only if rectangle is shorter than parent's
            double recttop = newtop - gap;
            RectangleF newr = new(r.X, (float)recttop, r.Width, r.Height);
            
            Rectangles[b] = newr;
            b.OffsetRectangle(this, gap);
        }

        foreach (var word in ws)
        {
            if (!word.IsImage)
                word.Top = newtop;
        }
    }

    public override string ToString()
    {
        string[] ws = new string[Words.Count];

        for (int i = 0; i < ws.Length; i++)
            ws[i] = Words[i].Text;

        return string.Join(" ", ws);
    }
}
