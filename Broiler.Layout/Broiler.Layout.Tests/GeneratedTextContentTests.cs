using System;
using System.Linq;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// <see cref="CssBox.SetGeneratedTextContent"/> is how a form control's value is
/// pushed into the box tree after parsing — the renderer uses it when an
/// <c>&lt;input&gt;</c> or <c>&lt;textarea&gt;</c> is edited.
/// <para>
/// Regression guard: assigning <see cref="CssBox.Text"/> clears the box's words,
/// and words are otherwise built only once, by the parser. Without re-splitting
/// inside <c>SetGeneratedTextContent</c> the new content had no words at all, so
/// paint and HTML serialization — both of which read <c>Words</c> — silently
/// dropped it and an edited field rendered blank.
/// </para>
/// </summary>
public sealed class GeneratedTextContentTests
{
    private static readonly Uri BaseUrl = new("file:///generated-text.html");

    private static CssBox CreateControlBox()
    {
        var box = new CssBox(null, new HtmlTag("input", isSingle: true), BaseUrl);
        box.InheritStyle();
        return box;
    }

    private static string[] WordsOfGeneratedChild(CssBox box) =>
        box.Boxes.Single(child => child.HtmlTag == null).Words.Select(word => word.Text).ToArray();

    [Fact(Timeout = 600000)]
    public void SetGeneratedTextContent_Produces_Words()
    {
        CssBox box = CreateControlBox();

        box.SetGeneratedTextContent("hello world");

        Assert.Equal(["hello", "world"], WordsOfGeneratedChild(box));
    }

    [Fact(Timeout = 600000)]
    public void SetGeneratedTextContent_Replaces_Previous_Words()
    {
        CssBox box = CreateControlBox();
        box.SetGeneratedTextContent("first");

        box.SetGeneratedTextContent("second");

        Assert.Equal(["second"], WordsOfGeneratedChild(box));
        Assert.Single(box.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void SetGeneratedTextContent_Clears_Words_For_Empty_Content()
    {
        CssBox box = CreateControlBox();
        box.SetGeneratedTextContent("typed");

        box.SetGeneratedTextContent(string.Empty);

        Assert.Empty(WordsOfGeneratedChild(box));
    }
}
