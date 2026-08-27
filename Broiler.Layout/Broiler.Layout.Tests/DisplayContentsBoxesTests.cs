using System;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// CSS Display 3 §3.1 <c>display: contents</c>: the element generates no box, and its children are
/// laid out as children of its parent.
/// </summary>
/// <remarks>
/// Before the splice, <c>contents</c> was neither block nor inline, so <c>PerformLayout</c> took
/// the else-branch and everything under such a box disappeared. duckduckgo.com's start page writes
/// its logo wrapper and its button labels that way, so the page lost its wordmark and the text of
/// every button that used the pattern.
/// </remarks>
public sealed class DisplayContentsBoxesTests
{
    private static readonly Uri BaseUrl = new("file:///contents.html");

    private const string Contents = "contents";

    [Fact(Timeout = 600000)]
    public void The_Childrens_Boxes_Become_The_Parents()
    {
        var root = Box(null, "div", CssConstants.Block);
        var wrapper = Box(root, "span", Contents);
        var first = Box(wrapper, "em", CssConstants.Inline);
        var second = Box(wrapper, "strong", CssConstants.Inline);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal([first, second], root.Boxes);
        Assert.Equal(root, first.ParentBox);
        Assert.Equal(root, second.ParentBox);
        Assert.Empty(wrapper.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void Document_Order_Is_Kept_Around_The_Spliced_Children()
    {
        var root = Box(null, "div", CssConstants.Block);
        var before = Box(root, "i", CssConstants.Inline);
        var wrapper = Box(root, "span", Contents);
        var inner = Box(wrapper, "em", CssConstants.Inline);
        var after = Box(root, "b", CssConstants.Inline);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal([before, inner, after], root.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void Nested_Contents_Boxes_Collapse_All_The_Way_Up()
    {
        var root = Box(null, "div", CssConstants.Block);
        var outer = Box(root, "span", Contents);
        var inner = Box(outer, "span", Contents);
        var leaf = Box(inner, "em", CssConstants.Inline);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal([leaf], root.Boxes);
        Assert.Equal(root, leaf.ParentBox);
    }

    // CSS Display 3 §3.1: on an element whose rendering is not purely CSS-based the value computes
    // to `none` instead. Chromium reports exactly this set from getComputedStyle.
    [Theory]
    [InlineData("img")]
    [InlineData("input")]
    [InlineData("textarea")]
    [InlineData("select")]
    [InlineData("canvas")]
    [InlineData("iframe")]
    [InlineData("video")]
    [InlineData("audio")]
    [InlineData("progress")]
    [InlineData("meter")]
    [InlineData("object")]
    [InlineData("br")]
    public void A_Replaced_Element_Or_Form_Control_Computes_To_None(string tag)
    {
        var root = Box(null, "div", CssConstants.Block);
        var control = Box(root, tag, Contents);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal([control], root.Boxes);
        Assert.Equal(CssConstants.None, control.Display);
    }

    // …and the elements Chromium keeps `contents` on are spliced like any other.
    [Theory]
    [InlineData("button")]
    [InlineData("a")]
    [InlineData("label")]
    [InlineData("li")]
    [InlineData("fieldset")]
    [InlineData("table")]
    public void An_Ordinary_Element_Is_Spliced_Even_When_It_Is_Interactive(string tag)
    {
        var root = Box(null, "div", CssConstants.Block);
        var wrapper = Box(root, tag, Contents);
        var inner = Box(wrapper, "span", CssConstants.Inline);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal([inner], root.Boxes);
    }

    // An <svg>'s children are shapes, not boxes: hoisting them into an HTML parent would put them
    // in a formatting context that cannot lay them out, so the subtree is left exactly as it was.
    [Fact(Timeout = 600000)]
    public void A_Foreign_Subtree_Is_Left_Alone()
    {
        var root = Box(null, "div", CssConstants.Block);
        var svg = Box(root, "svg", Contents);
        var shape = Box(svg, "path", CssConstants.Inline);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal([svg], root.Boxes);
        Assert.Equal([shape], svg.Boxes);
        Assert.Equal(Contents, svg.Display);
    }

    // CSS Display 3 §2.3: the root element's display is blockified, and it has no parent to splice
    // into — leaving it would drop the document.
    [Fact(Timeout = 600000)]
    public void The_Root_Element_Is_Blockified_Rather_Than_Spliced()
    {
        var root = Box(null, "html", Contents);
        var child = Box(root, "div", CssConstants.Block);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal(CssConstants.Block, root.Display);
        Assert.Equal([child], root.Boxes);
    }

    // …and it is still the root element when the pass is handed the *document* box, which is what
    // the renderer hands it: HtmlParser.ParseDocument creates an anonymous block and appends <html>
    // to it, so blockifying only the box we were given left the real root element to be spliced out
    // like any other wrapper. What goes with it is the canvas: the canvas background is the root
    // element's (CSS Backgrounds §2.11.2), so `:root { display: contents; background: … }` painted
    // nothing at all — the shape of css/css-display/display-contents-root-background, which asks
    // for a green page and got a white one.
    [Fact(Timeout = 600000)]
    public void The_Root_Element_Is_Blockified_Under_The_Anonymous_Document_Box()
    {
        var document = Box(null, "div", CssConstants.Block);
        var html = Box(document, "html", Contents);
        var body = Box(html, "body", CssConstants.Block);

        DisplayContentsBoxes.Generate(document);

        Assert.Equal([html], document.Boxes);
        Assert.Equal(CssConstants.Block, html.Display);
        Assert.Equal([body], html.Boxes);
    }

    // The rule is the document's, not the outermost box's: a sub-document's root element — an
    // <iframe>'s, projected under a sub-viewport box — is entitled to the same blockification.
    [Fact(Timeout = 600000)]
    public void A_Sub_Documents_Root_Element_Is_Blockified_Too()
    {
        var document = Box(null, "div", CssConstants.Block);
        var html = Box(document, "html", CssConstants.Block);
        var subViewport = Box(html, "div", CssConstants.Block);
        var subHtml = Box(subViewport, "html", Contents);
        var subBody = Box(subHtml, "body", CssConstants.Block);

        DisplayContentsBoxes.Generate(document);

        Assert.Equal([subHtml], subViewport.Boxes);
        Assert.Equal(CssConstants.Block, subHtml.Display);
        Assert.Equal([subBody], subHtml.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void Running_The_Pass_Twice_Changes_Nothing()
    {
        var root = Box(null, "div", CssConstants.Block);
        var wrapper = Box(root, "span", Contents);
        var inner = Box(wrapper, "em", CssConstants.Inline);

        DisplayContentsBoxes.Generate(root);
        DisplayContentsBoxes.Generate(root);

        Assert.Equal([inner], root.Boxes);
    }

    [Fact(Timeout = 600000)]
    public void A_Tree_Without_Contents_Is_Untouched()
    {
        var root = Box(null, "div", CssConstants.Block);
        var first = Box(root, "span", CssConstants.Inline);
        var second = Box(root, "span", CssConstants.Inline);
        var grandchild = Box(first, "em", CssConstants.Inline);

        DisplayContentsBoxes.Generate(root);

        Assert.Equal([first, second], root.Boxes);
        Assert.Equal([grandchild], first.Boxes);
    }

    private static CssBox Box(CssBox parent, string tag, string display) =>
        new(parent, new HtmlTag(tag, isSingle: false), BaseUrl) { Display = display };
}
