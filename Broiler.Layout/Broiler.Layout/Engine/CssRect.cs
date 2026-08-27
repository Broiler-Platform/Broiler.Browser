using System.Drawing;

namespace Broiler.Layout.Engine;

internal abstract class CssRect(CssBox owner)
{
    private RectangleF _rect;

    public CssBox OwnerBox { get; } = owner;

    public RectangleF Rectangle
    {
        get { return _rect; }
        set { _rect = value; }
    }

    public double Left
    {
        get { return _rect.X; }
        set { _rect.X = (float)value; }
    }

    public double Top
    {
        get { return _rect.Y; }
        set { _rect.Y = (float)value; }
    }

    public double Width
    {
        get { return _rect.Width; }
        set { _rect.Width = (float)value; }
    }

    public double FullWidth => _rect.Width + ActualWordSpacing;

    public double ActualWordSpacing => OwnerBox != null ? (HasSpaceAfter ? OwnerBox.ActualWordSpacing : 0) + (IsImage ? OwnerBox.ActualWordSpacing : 0) : 0;

    public double Height
    {
        get { return _rect.Height; }
        set { _rect.Height = (float)value; }
    }

    public double Right
    {
        get { return Rectangle.Right; }
        set { Width = value - Left; }
    }

    public double Bottom
    {
        get { return Rectangle.Bottom; }
        set { Height = value - Top; }
    }

    public ISelectionHandler Selection { get; set; }

    public virtual bool HasSpaceBefore => false;

    public virtual bool HasSpaceAfter => false;

    public virtual object Image
    {
        get { return null; }
        set { }
    }

    public virtual bool IsImage => false;
    public virtual bool IsSpaces => true;
    public virtual bool IsLineBreak => false;
    public virtual string Text => null;
    public bool Selected => Selection != null;
    public int SelectedStartIndex => Selection != null ? Selection.GetSelectingStartIndex(this) : -1;
    public int SelectedEndIndexOffset => Selection != null ? Selection.GetSelectedEndIndexOffset(this) : -1;
    public double SelectedStartOffset => Selection != null ? Selection.GetSelectedStartOffset(this) : -1;
    public double SelectedEndOffset => Selection != null ? Selection.GetSelectedEndOffset(this) : -1;
    internal double LeftGlyphPadding => OwnerBox != null ? OwnerBox.ActualFont.LeftPadding : 0;
    public override string ToString() => $"{Text.Replace(' ', '-').Replace("\n", "\\n")} ({Text.Length} char{(Text.Length != 1 ? "s" : string.Empty)})";

    public bool BreakPage()
    {
        var container = OwnerBox.LayoutEnvironment;

        if (Height >= container.PageSize.Height)
            return false;

        var remTop = (Top - container.MarginTop) % container.PageSize.Height;
        var remBottom = (Bottom - container.MarginTop) % container.PageSize.Height;

        if (remTop > remBottom)
        {
            Top += container.PageSize.Height - remTop + 1;
            return true;
        }

        return false;
    }
}