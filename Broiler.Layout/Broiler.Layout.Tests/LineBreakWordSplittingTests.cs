using System;
using System.Linq;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// Covers the character-level word splitting <see cref="CssBox.ParseToWords"/>
/// performs for <c>word-break: break-all</c> (CSS Text 3 §5.1) and
/// <c>line-break: anywhere</c> (§5.4): both introduce a soft-wrap opportunity
/// between every typographic character unit, so each character becomes its own
/// word. The splitting is pure (no font metrics), so the produced word list is
/// asserted directly. Also pins that a multi-character HTML entity is decoded as
/// a unit — a naive per-character split of the raw text would corrupt it.
/// </summary>
public sealed class LineBreakWordSplittingTests
{
    private static readonly Uri BaseUrl = new("file:///line-break.html");

    private static string[] SplitToWords(string text, string? lineBreak = null, string? wordBreak = null)
    {
        var box = new CssBox(null, null, BaseUrl);
        if (lineBreak != null) box.LineBreak = lineBreak;
        if (wordBreak != null) box.WordBreak = wordBreak;
        box.Text = text.AsMemory();
        box.ParseToWords();
        return box.Words.Select(w => w.Text).ToArray();
    }

    [Fact(Timeout = 600000)]
    public void Normal_Keeps_Runs_As_Single_Words()
    {
        Assert.Equal(new[] { "XXXX" }, SplitToWords("XXXX"));
    }

    [Fact(Timeout = 600000)]
    public void LineBreak_Anywhere_Splits_Every_Character()
    {
        Assert.Equal(new[] { "X", "X", "X", "X" }, SplitToWords("XXXX", lineBreak: "anywhere"));
    }

    [Fact(Timeout = 600000)]
    public void WordBreak_BreakAll_Splits_Every_Character()
    {
        Assert.Equal(new[] { "X", "X", "X", "X" }, SplitToWords("XXXX", wordBreak: "break-all"));
    }

    [Fact(Timeout = 600000)]
    public void LineBreak_Anywhere_Decodes_Multichar_Entity_As_One_Unit()
    {
        // U+FEFF (WORD JOINER / ZWNBSP) written as a numeric character reference.
        // A per-character split of the raw "XX&#xFEFF;XX" would emit "&", "#", …;
        // the entity must decode to a single zero-width code point between the X's.
        Assert.Equal(new[] { "X", "X", "﻿", "X", "X" },
            SplitToWords("XX&#xFEFF;XX", lineBreak: "anywhere"));
    }

    [Fact(Timeout = 600000)]
    public void LineBreak_Anywhere_Keeps_Surrogate_Pairs_Intact()
    {
        // U+1F600 GRINNING FACE is one typographic unit spanning two UTF-16 units.
        string emoji = char.ConvertFromUtf32(0x1F600);
        Assert.Equal(new[] { "A", emoji, "B" },
            SplitToWords("A" + emoji + "B", lineBreak: "anywhere"));
    }
}
