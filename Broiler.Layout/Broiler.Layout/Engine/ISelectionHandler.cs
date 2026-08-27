namespace Broiler.Layout.Engine;

internal interface ISelectionHandler
{
    int GetSelectingStartIndex(CssRect word);
    int GetSelectedEndIndexOffset(CssRect word);
    double GetSelectedStartOffset(CssRect word);
    double GetSelectedEndOffset(CssRect word);
}
