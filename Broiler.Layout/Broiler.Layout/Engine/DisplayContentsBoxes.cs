using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// CSS Display 3 §3.1 <c>display: contents</c>: the element generates no box of its own, and its
/// children are laid out and painted as though they were children of its parent.
/// </summary>
/// <remarks>
/// <para>
/// Broiler had the box tree but not the splice, and the consequence was not a mis-styled subtree
/// but a missing one. <c>contents</c> is neither <see cref="CssBox.IsBlock"/> nor
/// <see cref="CssBox.IsInline"/>, so <see cref="CssBox.PerformLayout"/> took the else-branch: the
/// box resolved no size and never laid its children out, and everything under it disappeared.
/// </para>
/// <para>
/// The keyword is not exotic — it is how a component library writes a wrapper that must not appear
/// in its parent's flex or grid line. duckduckgo.com's start page loses its DuckDuckGo logo
/// (<c>.themedImage_wrapper{display:contents}</c> around the <c>&lt;picture&gt;</c>), the header's
/// "Duck.ai" link label and the "Set As Default Search" button label
/// (<c>.link-button_buttonLabel{display:inline-flex}</c> overridden by
/// <c>@supports(display:contents){…{display:contents}}</c>) to it, and the page reaches that
/// override precisely because Broiler answers <c>CSS.supports('display','contents')</c> with
/// <see langword="true"/>.
/// </para>
/// <para>
/// Splicing the box out — rather than teaching layout to descend through it — is what the spec
/// describes, and it is also what keeps the rest of the engine honest: the children become real
/// children of the parent, so they are flex/grid items of it (CSS Flexbox §4), take part in its
/// inline/block corrections, and inherit the containing block they would have had. Style
/// inheritance is unaffected: the cascade has already run when this pass does, so the children
/// carry the values they inherited *through* the <c>contents</c> element.
/// </para>
/// <para>
/// The pass runs with the box fix-ups, ahead of <see cref="FlexGridItemBlockification"/>, so a
/// spliced-in child is blockified as the flex/grid item it has become.
/// </para>
/// </remarks>
internal static class DisplayContentsBoxes
{
    private const string Contents = "contents";

    /// <summary>
    /// Splices every <c>display: contents</c> box under <paramref name="root"/> out of the tree.
    /// Idempotent: a tree that holds none is walked and left alone, so a second call costs one
    /// walk and changes nothing.
    /// </summary>
    internal static void Generate(CssBox root)
    {
        if (root == null)
            return;

        // CSS Display 3 §2.3: the root element's display is blockified, so `contents` on it is
        // `block`. It has no parent to splice into, and leaving it would drop the whole document.
        if (root.Display == Contents)
            root.Display = CssConstants.Block;

        Flatten(root);
    }

    /// <summary>
    /// Whether this box is a document's root element, which CSS Display 3 §2.3 blockifies — so
    /// <c>contents</c> on it is <c>block</c> and it keeps its box.
    /// </summary>
    /// <remarks>
    /// The box <see cref="Generate"/> is handed is the anonymous <em>document</em> box the parse
    /// creates, not the root element: <c>&lt;html&gt;</c> is a child of it. Blockifying only the box
    /// we were given therefore left the actual root element to be spliced out like any other
    /// wrapper — and with it went the canvas, because the canvas background is the root element's
    /// (CSS Backgrounds §2.11.2). The test that names this reads
    /// <c>:root { display: contents; background-image: url(…) }</c> and asks for a green page;
    /// Broiler painted the page's text on white
    /// (<c>css/css-display/display-contents-root-background</c>). Recognised by tag rather than by
    /// position so a sub-document's root element — an <c>&lt;iframe&gt;</c>'s, projected under a
    /// sub-viewport box — is covered by the same rule, which is the rule its own document is
    /// entitled to.
    /// </remarks>
    private static bool IsBlockifiedRootElement(CssBox box) => CssBoxHelper.IsRootElement(box);

    private static void Flatten(CssBox box)
    {
        // Depth first: a chain of nested `contents` boxes collapses from the inside out, so by the
        // time this box's own children are spliced each of them is already a real box.
        for (int i = 0; i < box.Boxes.Count; i++)
            Flatten(box.Boxes[i]);

        bool anyContents = false;
        foreach (var child in box.Boxes)
        {
            if (child.Display != Contents)
                continue;

            if (IsBlockifiedRootElement(child))
            {
                child.Display = CssConstants.Block;
                continue;
            }

            // CSS Display 3 §3.1: on an element whose rendering is not purely CSS-based —
            // a replaced element or a form control — `contents` computes to `none` instead.
            // Chromium reports exactly this set from getComputedStyle: <img>, <input>,
            // <textarea>, <select>, <canvas>, <iframe>, <br>, <video>, <audio>, <progress>,
            // <meter> and <object> answer `none`, while <button>, <hr>, <fieldset>, <table>,
            // <li>, <details>, <summary>, <label> and <a> answer `contents`.
            if (IsBoxless(child))
            {
                child.Display = CssConstants.None;
                continue;
            }

            if (HasForeignContent(child))
                continue;

            anyContents = true;
        }

        if (!anyContents)
            return;

        // Rebuilt rather than spliced in place: the ParentBox setter appends to the new parent's
        // list, so re-adding every child in document order is what keeps that order.
        var children = new List<CssBox>(box.Boxes);
        box.Boxes.Clear();

        foreach (var child in children)
        {
            if (child.Display != Contents || IsBoxless(child) || HasForeignContent(child))
            {
                child.ParentBox = box;
                continue;
            }

            foreach (var grandchild in new List<CssBox>(child.Boxes))
                grandchild.ParentBox = box;

            child.ParentBox = null;
        }
    }

    /// <summary>
    /// Whether <c>display: contents</c> computes to <c>none</c> on this box's element, because the
    /// element does not render as a CSS box in the first place.
    /// </summary>
    private static bool IsBoxless(CssBox box)
    {
        var name = box.HtmlTag?.Name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.Equals("img", StringComparison.OrdinalIgnoreCase)
            || name.Equals("input", StringComparison.OrdinalIgnoreCase)
            || name.Equals("textarea", StringComparison.OrdinalIgnoreCase)
            || name.Equals("select", StringComparison.OrdinalIgnoreCase)
            || name.Equals("canvas", StringComparison.OrdinalIgnoreCase)
            || name.Equals("iframe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("frame", StringComparison.OrdinalIgnoreCase)
            || name.Equals("frameset", StringComparison.OrdinalIgnoreCase)
            || name.Equals("embed", StringComparison.OrdinalIgnoreCase)
            || name.Equals("object", StringComparison.OrdinalIgnoreCase)
            || name.Equals("video", StringComparison.OrdinalIgnoreCase)
            || name.Equals("audio", StringComparison.OrdinalIgnoreCase)
            || name.Equals("progress", StringComparison.OrdinalIgnoreCase)
            || name.Equals("meter", StringComparison.OrdinalIgnoreCase)
            || name.Equals("br", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the box roots a subtree that is not laid out by CSS box rules at all — an
    /// <c>&lt;svg&gt;</c> or <c>&lt;math&gt;</c>. Chromium keeps <c>contents</c> computed on those,
    /// but their children are shapes rather than boxes, so hoisting them into an HTML parent would
    /// put content into a formatting context that cannot lay it out. Left alone, they render
    /// exactly as they did before this pass existed.
    /// </summary>
    private static bool HasForeignContent(CssBox box)
    {
        var name = box.HtmlTag?.Name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.Equals("svg", StringComparison.OrdinalIgnoreCase)
            || name.Equals("math", StringComparison.OrdinalIgnoreCase);
    }
}
