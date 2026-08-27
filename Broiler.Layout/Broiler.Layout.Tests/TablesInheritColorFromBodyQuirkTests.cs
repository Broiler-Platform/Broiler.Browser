using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The Quirks Mode Standard's
/// <see href="https://quirks.spec.whatwg.org/#the-tables-inherit-color-from-body-quirk">tables
/// inherit color from body quirk</see>: in quirks mode a table that specifies no <c>color</c>
/// takes the document's body element's, and — with no body element — the initial value rather than
/// its parent's.
/// </summary>
/// <remarks>
/// The no-body half is asserted here and only here. WPT covers it
/// (<c>tables-inherit-color-from-body-quirk-004</c>…<c>-007</c>), but those four cannot reach it
/// through the WPT runner: the runner serialises the script-mutated DOM to HTML and re-parses it,
/// and HTML tree construction unconditionally creates a <c>&lt;body&gt;</c> — so a document a
/// script left without one comes back with one, and the `body { color: red }` those tests declare
/// then matches it. That is a round-trip fidelity limit in the runner, not in this rule, which is
/// why the rule is pinned directly on the box tree instead of being left to the reftests.
/// </remarks>
public sealed class TablesInheritColorFromBodyQuirkTests
{
    private static readonly Uri BaseUrl = new("file:///quirk.html");

    [Fact(Timeout = 600000)]
    public void A_Table_Takes_The_Bodys_Colour_Rather_Than_Its_Parents()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var body = Child(html, "body", color: "green");
            var div = Child(body, "div", color: "red");
            var table = Child(div, "table");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("green", table.Color);
            // Only the table is redirected; the div keeps what it declared.
            Assert.Equal("red", div.Color);
        });
    }

    // The colour that is actually painted belongs to the cell, which inherited the table's before
    // the quirk moved it — so the new value has to be pushed down the subtree.
    [Fact(Timeout = 600000)]
    public void The_New_Colour_Reaches_The_Cells_Inside_The_Table()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var body = Child(html, "body", color: "green");
            var table = Child(Child(body, "div", color: "red"), "table");
            var cell = Child(Child(table, "tr"), "td");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("green", cell.Color);
        });
    }

    // The case the whole design is shaped around: the body element is the *document's*, so a table
    // that is not inside it — and here is laid out before it — still takes its colour. Reading the
    // body during the top-down cascade could not answer this, because body has not cascaded yet.
    [Fact(Timeout = 600000)]
    public void A_Table_Before_The_Body_In_Document_Order_Still_Takes_Its_Colour()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var table = Child(Child(html, "div", color: "red"), "table");
            Child(html, "body", color: "green");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("green", table.Color);
        });
    }

    // With no body element the quirk resolves to the initial colour — NOT to the parent's, which
    // is what plain inheritance would have given and what makes this more than a redirect.
    [Fact(Timeout = 600000)]
    public void With_No_Body_Element_A_Table_Takes_The_Initial_Colour()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var table = Child(Child(html, "div", color: "red"), "table");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal(CssBox.InitialColor, table.Color);
            Assert.NotEqual("red", table.Color);
        });
    }

    [Fact(Timeout = 600000)]
    public void A_Table_That_Declares_Its_Own_Colour_Keeps_It()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var body = Child(html, "body", color: "green");
            var table = Child(body, "table", color: "blue");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("blue", table.Color);
        });
    }

    // A descendant that declared its own colour keeps it, and so does everything below it — the
    // push-down must stop at that branch rather than flatten the subtree.
    [Fact(Timeout = 600000)]
    public void A_Declared_Colour_Inside_The_Table_Survives_The_Push_Down()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var body = Child(html, "body", color: "green");
            var table = Child(body, "table");
            var declared = Child(table, "td", color: "blue");
            var inside = Child(declared, "span");
            inside.SetInheritedColor("blue");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("green", table.Color);
            Assert.Equal("blue", declared.Color);
            Assert.Equal("blue", inside.Color);
        });
    }

    [Fact(Timeout = 600000)]
    public void Nothing_Happens_In_Standards_Mode()
    {
        var previous = DocumentModeContext.CurrentQuirksMode;
        try
        {
            DocumentModeContext.CurrentQuirksMode = false;

            var (root, html) = Document();
            var body = Child(html, "body", color: "green");
            var div = Child(body, "div", color: "red");
            var table = Child(div, "table");
            table.SetInheritedColor("red");   // what ordinary inheritance gave it

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("red", table.Color);
        }
        finally
        {
            DocumentModeContext.CurrentQuirksMode = previous;
        }
    }

    [Fact(Timeout = 600000)]
    public void Non_Table_Elements_Are_Untouched()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var body = Child(html, "body", color: "green");
            var div = Child(body, "div", color: "red");
            var span = Child(div, "span");
            span.SetInheritedColor("red");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("red", span.Color);
        });
    }

    // The quirk resolves per table element, so a table nested inside another one takes the body's
    // colour directly rather than whatever the outer table settled on.
    [Fact(Timeout = 600000)]
    public void A_Nested_Table_Resolves_Against_The_Body_Too()
    {
        InQuirksMode(() =>
        {
            var (root, html) = Document();
            var body = Child(html, "body", color: "green");
            var outer = Child(body, "table");
            var cell = Child(outer, "td", color: "blue");
            var inner = Child(cell, "table");

            TablesInheritColorFromBodyQuirk.Apply(root);

            Assert.Equal("green", outer.Color);
            Assert.Equal("blue", cell.Color);
            Assert.Equal("green", inner.Color);
        });
    }

    private static void InQuirksMode(Action body)
    {
        var previous = DocumentModeContext.CurrentQuirksMode;
        try
        {
            DocumentModeContext.CurrentQuirksMode = true;
            body();
        }
        finally
        {
            DocumentModeContext.CurrentQuirksMode = previous;
        }
    }

    private static (CssBox Root, CssBox Html) Document()
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new StubLayoutEnvironment(),
        };

        return (root, Child(root, "html"));
    }

    /// <summary>
    /// A child box for <paramref name="tagName"/>. A non-null <paramref name="color"/> goes
    /// through the <c>Color</c> setter, which is what a declaration does and what marks the box as
    /// having specified one; boxes without it are left carrying whatever they inherited.
    /// </summary>
    private static CssBox Child(CssBox parent, string tagName, string? color = null)
    {
        var box = new CssBox(parent, new HtmlTag(tagName, isSingle: false, attributes: null), BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
            Display = CssConstants.Block,
            FontSize = "16px",
        };

        box.SetInheritedColor(parent.Color);
        if (color is not null)
            box.Color = color;

        return box;
    }

    private sealed class StubLayoutEnvironment : ILayoutEnvironment
    {
        public Broiler.Graphics.ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new StubFont(size);
        public SizeF MeasureText(Broiler.Graphics.ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(Broiler.Graphics.ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(Broiler.Graphics.ILayoutFont font) => 0;
        public ImageIntrinsics GetImageIntrinsics(object imageHandle) => default;
        public Broiler.Graphics.BColor ParseColor(string value) => default;
        public void RequestRefresh(bool relayout) { }
        public SizeF ViewportSize => new(1000, 1000);
        public PointF RootLocation => PointF.Empty;
        public SizeF ActualSize { get; set; }
        public bool AvoidGeometryAntialias => false;
        public SizeF PageSize => new(1000, 1000);
        public int MarginTop => 0;
        public void ReportLayoutError(string message, Exception? exception = null) { }
        public bool AvoidAsyncImagesLoading => true;
        public bool AvoidImagesLateLoading => true;
        public ILayoutImageLoader CreateImageLoader(Action<object?, RectangleF, bool> onComplete) => null!;
        public string FormatListMarker(int number, string style) => string.Empty;
    }

    private sealed class StubFont(double size) : Broiler.Graphics.ILayoutFont
    {
        public double Size { get; } = size;
        public double Height => size;
        public double UnderlineOffset => 0;
        public double LeftPadding => 0;
        public string? FontFeatures => null;
    }
}
