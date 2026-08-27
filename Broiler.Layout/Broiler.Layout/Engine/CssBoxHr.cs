using Broiler.CSS;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal sealed class CssBoxHr : CssBox
{
    public CssBoxHr(CssBox parent, HtmlTag tag, Uri baseUrl) : base(parent, tag, baseUrl) => Display = CssConstants.Block;

    protected override void PerformLayoutImp(ILayoutEnvironment g)
    {
        // This override does not chain to the base, so it needs its own reset — see
        // CssBox.ResetCollapsedMarginState. It calls MarginTopCollapse below like any other block.
        ResetCollapsedMarginState();

        if (Display == CssConstants.None)
            return;

        RectanglesReset();

        var prevSibling = LayoutBoxUtils.GetPreviousSibling(this);
        double left = ContainingBlock.Location.X + ContainingBlock.ActualPaddingLeft + ActualMarginLeft + ContainingBlock.ActualBorderLeftWidth;
        double marginCollapse = MarginTopCollapse(prevSibling);
        double top = (prevSibling == null && ParentBox != null ? ParentBox.ClientTop : ParentBox == null ? Location.Y : 0) + marginCollapse + (prevSibling != null ? prevSibling.ActualBottom + prevSibling.ActualBorderBottomWidth : 0);

        Location = new PointF((float)left, (float)top);
        ActualBottom = top;

        //width at 100% (or auto)
        double minwidth = GetMinimumWidth();
        double width = ContainingBlock.Size.Width
                       - ContainingBlock.ActualPaddingLeft - ContainingBlock.ActualPaddingRight
                       - ContainingBlock.ActualBorderLeftWidth - ContainingBlock.ActualBorderRightWidth
                       - ActualMarginLeft - ActualMarginRight - ActualBorderLeftWidth - ActualBorderRightWidth;

        //Check width if not auto
        if (Width != CssConstants.Auto && !string.IsNullOrEmpty(Width))
            width = CssLengthParser.ParseLength(Width, width, GetEmHeight());

        if (width < minwidth || width >= 9999)
            width = minwidth;

        double height = ActualHeight;

        if (height < 1)
            height = Size.Height + ActualBorderTopWidth + ActualBorderBottomWidth;

        if (height < 1)
            height = 2;

        if (height <= 2 && ActualBorderTopWidth < 1 && ActualBorderBottomWidth < 1)
        {
            BorderTopStyle = BorderBottomStyle = CssConstants.Solid;
            BorderTopWidth = "1px";
            BorderBottomWidth = "1px";
        }

        Size = new SizeF((float)width, (float)height);

        ActualBottom = Location.Y + ActualPaddingTop + ActualPaddingBottom + height;
    }
}