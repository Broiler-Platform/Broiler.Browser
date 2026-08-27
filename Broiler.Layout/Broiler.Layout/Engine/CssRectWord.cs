namespace Broiler.Layout.Engine;

internal sealed class CssRectWord(CssBox owner, string text, bool hasSpaceBefore, bool hasSpaceAfter) : CssRect(owner)
{
    public override bool HasSpaceBefore => hasSpaceBefore;
    public override bool HasSpaceAfter => hasSpaceAfter;

    public override bool IsSpaces
    {
        get
        {
            foreach (var c in Text)
            {
                if (!char.IsWhiteSpace(c))
                    return false;
            }

            return true;
        }
    }

    public override bool IsLineBreak => Text == "\n";
    public override string Text => text;
    public override string ToString() => $"{Text.Replace(' ', '-').Replace("\n", "\\n")} ({Text.Length} char{(Text.Length != 1 ? "s" : string.Empty)})";
}