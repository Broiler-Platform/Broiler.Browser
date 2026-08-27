using System;
using System.Drawing;
using Broiler.CSS;
using Broiler.Graphics;
using Broiler.Layout.Engine;
using Xunit;

namespace Broiler.Layout.Tests;

/// <summary>
/// The HTML Standard's quirks-mode table font reset (Rendering §Tables): a <c>&lt;table&gt;</c> in a
/// quirks-mode document starts <c>font-weight</c>, <c>font-style</c>, <c>font-variant</c>,
/// <c>font-size</c>, <c>line-height</c>, <c>white-space</c> and <c>text-align</c> from their initial
/// values instead of inheriting its parent's.
/// </summary>
/// <remarks>
/// The font-size half is the one with teeth, and the shape that motivated it is www.7-zip.org:
/// a quirks-mode page whose sheet says <c>BODY { font-size: 80% }</c> and <c>TD { font-size: 80% }</c>
/// while nesting tables three deep. Without the reset the 80% compounds once per level and the page
/// ends up in a ~6px font; with it every cell is 12.8px at every depth, which is what browsers show.
/// Both are asserted here through <c>ActualFont.Size</c>, using the echoing font environment so the
/// numbers are the requested sizes rather than whatever a real font rounds to.
/// </remarks>
public sealed class TableFontInheritanceQuirkTests
{
    private static readonly Uri BaseUrl = new("file:///table-font-quirk.html");

    [Fact(Timeout = 600000)]
    public void A_Table_Does_Not_Inherit_The_Parents_Font_Size()
    {
        InQuirksMode(() =>
        {
            var (_, body) = Document();
            body.FontSize = "80%";

            var table = Cascade(body, "table");

            Assert.Equal(CssConstants.Medium, table.FontSize);
        });
    }

    // The 7-Zip shape. Three nested tables, each cell asking for 80%: every cell resolves against
    // its table's reset size, so all three land on the same 12.8px rather than 10.24 / 8.19 / 6.55.
    [Fact(Timeout = 600000)]
    public void A_Cells_Percentage_Does_Not_Compound_Through_Nested_Tables()
    {
        InQuirksMode(() =>
        {
            var (root, body) = Document();
            root.FontSize = "16px";
            body.FontSize = "80%";

            var cell1 = Cell(body);
            var cell2 = Cell(cell1);
            var cell3 = Cell(cell2);

            var expected = body.ActualFont.Size;   // 80% of the root: the size every cell should get
            Assert.Equal(expected, cell1.ActualFont.Size, 4);
            Assert.Equal(expected, cell2.ActualFont.Size, 4);
            Assert.Equal(expected, cell3.ActualFont.Size, 4);
        });
    }

    // The same document in standards mode is the control: there the table inherits, so the 80%
    // compounds and each level is 80% of the one above it.
    [Fact(Timeout = 600000)]
    public void The_Same_Nesting_Still_Compounds_In_Standards_Mode()
    {
        InStandardsMode(() =>
        {
            var (root, body) = Document();
            root.FontSize = "16px";
            body.FontSize = "80%";

            var cell1 = Cell(body);
            var cell2 = Cell(cell1);

            Assert.Equal(0.8, cell1.ActualFont.Size / body.ActualFont.Size, 4);
            Assert.Equal(0.8, cell2.ActualFont.Size / cell1.ActualFont.Size, 4);
        });
    }

    [Fact(Timeout = 600000)]
    public void The_Other_Six_Properties_Are_Reset_Too()
    {
        InQuirksMode(() =>
        {
            var (_, body) = Document();
            body.FontWeight = "bold";
            body.FontStyle = CssConstants.Italic;
            body.FontVariant = "small-caps";
            body.LineHeight = "3em";
            body.WhiteSpace = "pre";
            body.TextAlign = CssConstants.Right;

            var table = Cascade(body, "table");

            Assert.Equal(CssConstants.Normal, table.FontWeight);
            Assert.Equal(CssConstants.Normal, table.FontStyle);
            Assert.Equal(CssConstants.Normal, table.FontVariant);
            Assert.Equal(CssConstants.Normal, table.LineHeight);
            Assert.Equal(CssConstants.Normal, table.WhiteSpace);
            Assert.Equal(string.Empty, table.TextAlign);
        });
    }

    // font-family is not in the spec's list, so a quirks-mode table keeps the page's typeface and
    // only loses the size. 7-Zip's tables still render in the body's Verdana.
    [Fact(Timeout = 600000)]
    public void Font_Family_Is_Still_Inherited()
    {
        InQuirksMode(() =>
        {
            var (_, body) = Document();
            body.FontFamily = "Verdana";

            var table = Cascade(body, "table");

            Assert.Equal("Verdana", table.FontFamily);
        });
    }

    // The spec states the reset as a UA-origin rule, and the cascade applies the element's own
    // declarations after inheriting — so an author font-size on the table itself still wins.
    [Fact(Timeout = 600000)]
    public void An_Author_Declaration_On_The_Table_Overrides_The_Reset()
    {
        InQuirksMode(() =>
        {
            var (_, body) = Document();
            body.FontSize = "80%";

            var table = Cascade(body, "table");
            table.FontSize = "20px";   // what a `table { font-size: 20px }` declaration does

            // Asserted as the used size rather than the declaration text, which the setter
            // normalises: 20px is 15pt, where the reset would have left it at medium's 12pt.
            Assert.Equal(15.0, table.ActualFont.Size, 4);
        });
    }

    [Fact(Timeout = 600000)]
    public void Nothing_Happens_In_Standards_Mode()
    {
        InStandardsMode(() =>
        {
            var (_, body) = Document();
            body.FontSize = "10px";
            body.WhiteSpace = "pre";

            var table = Cascade(body, "table");

            Assert.Equal(body.FontSize, table.FontSize);
            Assert.Equal("pre", table.WhiteSpace);
        });
    }

    // The selector in the spec's rule is an element selector, so a box that merely lays out as a
    // table is unaffected — as is any other element.
    [Fact(Timeout = 600000)]
    public void Only_The_Table_Element_Is_Reset()
    {
        InQuirksMode(() =>
        {
            var (_, body) = Document();
            body.FontSize = "10px";

            var div = Cascade(body, "div");
            div.Display = CssConstants.Table;
            var cell = Cascade(body, "td");

            Assert.Equal(body.FontSize, div.FontSize);
            Assert.Equal(body.FontSize, cell.FontSize);
        });
    }

    // `everything: true` is the inline-splitting path, which clones an already-cascaded box onto
    // its two halves — HtmlTag included. Re-running the reset there would throw away the author
    // values the original ended up with, so it must not fire.
    [Fact(Timeout = 600000)]
    public void The_Clone_Path_Keeps_The_Originals_Values()
    {
        InQuirksMode(() =>
        {
            var (_, body) = Document();

            var table = Cascade(body, "table");
            table.FontSize = "20px";
            table.WhiteSpace = "pre";

            var half = new CssBox(body, new HtmlTag("table", isSingle: false, attributes: null), BaseUrl);
            half.InheritStyle(table, everything: true);

            Assert.Equal(table.FontSize, half.FontSize);
            Assert.NotEqual(CssConstants.Medium, half.FontSize);
            Assert.Equal("pre", half.WhiteSpace);
        });
    }

    /// <summary>A child box whose inherited values come through <c>InheritStyle</c>, the way the
    /// cascade produces them, so the quirk runs exactly where it does in a real render.</summary>
    private static CssBox Cascade(CssBox parent, string tagName)
    {
        var box = new CssBox(parent, new HtmlTag(tagName, isSingle: false, attributes: null), BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 0),
        };

        box.InheritStyle(parent);
        return box;
    }

    /// <summary>A <c>&lt;table&gt;</c>/<c>&lt;td&gt;</c> pair with the cell asking for 80%, which is
    /// how the pages this quirk exists for are written.</summary>
    private static CssBox Cell(CssBox parent)
    {
        var cell = Cascade(Cascade(parent, "table"), "td");
        cell.FontSize = "80%";
        return cell;
    }

    private static (CssBox Root, CssBox Body) Document()
    {
        var root = new CssBox(null, null, BaseUrl)
        {
            Location = new PointF(0, 0),
            Size = new SizeF(400, 400),
            Display = CssConstants.Block,
            FontSize = "16px",
            LayoutEnvironment = new EchoFontEnvironment(),
        };

        var html = Cascade(root, "html");
        return (root, Cascade(html, "body"));
    }

    private static void InQuirksMode(Action body) => WithDocumentMode(true, body);

    private static void InStandardsMode(Action body) => WithDocumentMode(false, body);

    private static void WithDocumentMode(bool quirks, Action body)
    {
        var previous = DocumentModeContext.CurrentQuirksMode;
        try
        {
            DocumentModeContext.CurrentQuirksMode = quirks;
            body();
        }
        finally
        {
            DocumentModeContext.CurrentQuirksMode = previous;
        }
    }

    /// <summary>Reports the requested point size back verbatim, so a test can assert on the size a
    /// box asked for without a real font's rounding in the way.</summary>
    private sealed class EchoFontEnvironment : ILayoutEnvironment
    {
        public ILayoutFont GetFont(string family, double size, LayoutFontStyle style, string? fontFeatures = null) => new EchoFont(size);
        public SizeF MeasureText(ILayoutFont font, string text) => SizeF.Empty;
        public void MeasureText(ILayoutFont font, string text, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public double GetWhitespaceWidth(ILayoutFont font) => 0;
        public ImageIntrinsics GetImageIntrinsics(object imageHandle) => default;
        public BColor ParseColor(string value) => default;
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

    private sealed class EchoFont(double size) : ILayoutFont
    {
        public double Size { get; } = size;
        public double Height => size;
        public double UnderlineOffset => 0;
        public double LeftPadding => 0;
        public string? FontFeatures => null;
    }
}
