using System;
using System.Linq;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Text 3 §White Space Processing: U+00A0 NO-BREAK SPACE (<c>&amp;nbsp;</c>) is
/// <em>not</em> a collapsible white-space character — only U+0020/tab/newline/CR/FF
/// are. nbsp carries its own advance width and must survive white-space collapsing,
/// so a run of nbsp (even one that is the sole content of an inline box) generates a
/// word and therefore an inline box that paints its background and box-shadow.
/// <para>
/// Regression guard for <see cref="CssBox.ParseToWords"/>: it previously used
/// <c>char.IsWhiteSpace</c>, which returns true for nbsp, so a <c>&amp;nbsp;</c>-only
/// span collapsed to nothing and dropped its background/box-shadow (WPT
/// <c>css/css-view-transitions/inline-child-with-overflow-shadow</c>).
/// </para>
/// </summary>
public sealed class NbspWhiteSpaceTests
{
    private static readonly Uri BaseUrl = new("file:///nbsp.html");
    private const string Nbsp = "\u00A0";

    private static string[] Words(string text)
    {
        var box = new CssBox(null, null, BaseUrl);
        box.Text = text.AsMemory();
        box.ParseToWords();
        return box.Words.Select(w => w.Text).ToArray();
    }

    [Fact(Timeout = 600000)]
    public void Nbsp_Only_Run_Survives_Collapsing_As_A_Word()
    {
        // The sole content is nbsp: it must still produce a word (hence an inline
        // box that paints background/box-shadow), not collapse away to nothing.
        Assert.Equal(new[] { Nbsp + Nbsp + Nbsp }, Words(Nbsp + Nbsp + Nbsp));
    }

    [Fact(Timeout = 600000)]
    public void Nbsp_Joins_Neighbours_Into_One_Word()
    {
        // nbsp is a non-breaking space: it neither collapses nor introduces a
        // soft-wrap opportunity, so it stays inside a single word.
        Assert.Equal(new[] { "A" + Nbsp + Nbsp + "B" }, Words("A" + Nbsp + Nbsp + "B"));
    }

    [Fact(Timeout = 600000)]
    public void Ascii_Space_Runs_Still_Collapse()
    {
        // Control: ordinary U+0020 runs remain collapsible word separators, so the
        // narrowed predicate did not over-reach into real white space.
        Assert.Equal(new[] { "A", "B" }, Words("A   B"));
    }
}
