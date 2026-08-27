using System;
using System.Collections.Generic;
using System.Text;

namespace Broiler.Render.Stage.Benchmarks;

/// <summary>
/// The fixed page set every measurement in this project runs on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Generated in code rather than checked in as files, and that is deliberate.</b> The profile has
/// to be reproducible from one command on a bare checkout, and the alternative sources available —
/// <c>tests/wpt</c>, <c>acid/</c>, <c>css2/</c> — are either an external checkout that may be absent
/// or pages written to test conformance rather than to load a stage. Generation from constants also
/// makes the page size a named number instead of a property of a file nobody re-reads.
/// </para>
/// <para>
/// <b>Nothing here is random.</b> Every string is drawn from a fixed word list by index, so two runs
/// on two machines render byte-identical documents and a profile is comparable across them. A seeded
/// <c>Random</c> would also be reproducible, but only for as long as nobody changes the call order.
/// </para>
/// <para>
/// <b>The five pages are chosen to separate the stages, not to look like the web.</b> A single
/// "representative page" produces one blended number and answers nothing: the roadmap's open
/// questions are *which* stage dominates *which* kind of page — whether the cascade's linear rule
/// scan (multithreading item #11) actually dominates a rule-heavy page, and whether the scanline
/// rasterizer (items #3/#5) actually dominates a paint-heavy one. Pages that load one stage each are
/// what makes those separable. <see cref="Mixed"/> is the blended control that says what a page with
/// no particular emphasis costs.
/// </para>
/// </remarks>
internal static class Corpus
{
    /// <summary>Viewport the profile and the end-to-end benchmarks render at.</summary>
    public const int ViewportWidth = 1280;

    /// <summary>Viewport height. 1280x1024 is the size the roadmap names for the raster item.</summary>
    public const int ViewportHeight = 1024;

    /// <summary>
    /// Word source for every generated document. Latin so the shaper takes its simple path — a page
    /// that also measured complex-script shaping would fold two stages into one number.
    /// </summary>
    private static readonly string[] Words =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et",
        "dolore", "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis",
        "nostrud", "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea",
    ];

    /// <summary>Deterministic word by absolute index.</summary>
    private static string Word(int i) => Words[(int)((uint)i % (uint)Words.Length)];

    /// <summary>Deterministic run of <paramref name="count"/> words starting at <paramref name="seed"/>.</summary>
    private static string Sentence(int seed, int count)
    {
        var sb = new StringBuilder(count * 8);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(Word(seed + i * 7));
        }

        return sb.ToString();
    }

    /// <summary>A page in the corpus: a name, the stage it is built to load, and its source.</summary>
    /// <param name="Name">Stable identifier; the profile and the benchmark parameters both key on it.</param>
    /// <param name="Loads">What the page is built to load, in prose. Appears in the profile.</param>
    /// <param name="Css">
    /// The page's stylesheet on its own. Kept separate from <paramref name="Html"/> — which embeds
    /// the same text in a <c>&lt;style&gt;</c> element — so <see cref="CascadeBenchmarks"/> can hand
    /// it to <c>CssStyleEngine</c> directly. Re-extracting it from the markup with a scanner would
    /// be a second, worse copy of the same string.
    /// </param>
    /// <param name="Html">The full document, stylesheet included.</param>
    internal sealed record Page(string Name, string Loads, string Css, string Html)
    {
        /// <summary>Source length in characters — the one size figure a profile row can be read against.</summary>
        public int SourceLength => Html.Length;
    }

    /// <summary>Wraps a stylesheet and a body into the document every page shares.</summary>
    private static string Document(string css, string body) =>
        "<!DOCTYPE html><html><head><style>" + css + "</style></head><body>" + body + "</body></html>";

    private static Page[]? _pages;

    /// <summary>The corpus, built once per process and cached.</summary>
    public static IReadOnlyList<Page> Pages => _pages ??=
    [
        Text(),
        Rules(),
        Boxes(),
        Paint(),
        Mixed(),
    ];

    /// <summary>Looks a page up by name, case-insensitively.</summary>
    public static Page ByName(string name)
    {
        foreach (var page in Pages)
        {
            if (string.Equals(page.Name, name, StringComparison.OrdinalIgnoreCase))
                return page;
        }

        throw new ArgumentOutOfRangeException(nameof(name), name, "No such corpus page.");
    }

    /// <summary>Names of every page, for BenchmarkDotNet parameter sources.</summary>
    public static string[] Names()
    {
        var names = new string[Pages.Count];
        for (var i = 0; i < Pages.Count; i++)
            names[i] = Pages[i].Name;
        return names;
    }

    private const int TextParagraphs = 240;
    private const int TextWordsPerParagraph = 45;

    /// <summary>
    /// Prose under a handful of rules: line breaking, text measurement, and glyph rasterization,
    /// with as little cascade and box construction as a real page can have.
    /// </summary>
    private static Page Text()
    {
        const string css =
            "body{font:16px/1.5 sans-serif;margin:0;padding:24px;color:#222}"
            + "p{margin:0 0 12px}h2{font-size:22px;margin:24px 0 8px}em{font-style:italic}";

        var sb = new StringBuilder(TextParagraphs * TextWordsPerParagraph * 8);
        for (var i = 0; i < TextParagraphs; i++)
        {
            if (i % 12 == 0)
                sb.Append("<h2>").Append(Sentence(i, 5)).Append("</h2>");

            sb.Append("<p>").Append(Sentence(i * 31, TextWordsPerParagraph / 2))
              .Append(" <em>").Append(Sentence(i * 13, 4)).Append("</em> ")
              .Append(Sentence(i * 17, TextWordsPerParagraph / 2)).Append("</p>");
        }

        return new Page("text", "line breaking, text measurement, glyph raster",
            css, Document(css, sb.ToString()));
    }

    private const int RuleCount = 900;
    private const int RuleElements = 700;
    private const int RuleClasses = 40;

    /// <summary>
    /// Many rules over many elements, which is the shape multithreading item #11 is about:
    /// <c>CssStyleEngine.CollectFromRules</c> scans every rule of every sheet for every element, so
    /// this page's cost is quadratic in a way none of the others are.
    /// </summary>
    /// <remarks>
    /// The selectors are deliberately a mix of tag, class, descendant, and attribute forms. A sheet
    /// of nothing but class selectors would be the easiest possible case for the rule index that
    /// item #11 proposes and would overstate what it can win.
    /// </remarks>
    private static Page Rules()
    {
        var sb = new StringBuilder(RuleCount * 64);
        sb.Append("body{font:14px/1.4 sans-serif;margin:0;padding:8px}");
        for (var i = 0; i < RuleCount; i++)
        {
            var cls = i % RuleClasses;
            switch (i % 5)
            {
                case 0:
                    sb.Append(".c").Append(cls).Append('{');
                    break;
                case 1:
                    sb.Append("div .c").Append(cls).Append('{');
                    break;
                case 2:
                    sb.Append("span.c").Append(cls).Append(" > b{");
                    break;
                case 3:
                    sb.Append("[data-k=\"").Append(i % 17).Append("\"]{");
                    break;
                default:
                    sb.Append("div.c").Append(cls).Append(":not(.c").Append((cls + 1) % RuleClasses).Append("){");
                    break;
            }

            sb.Append("color:#").Append((i % 9) + 1).Append((i % 7) + 1).Append((i % 5) + 1);
            sb.Append(";padding-left:").Append(i % 6).Append("px}");
        }

        var css = sb.ToString();

        var body = new StringBuilder(RuleElements * 96);
        for (var i = 0; i < RuleElements; i++)
        {
            body.Append("<div class=\"c").Append(i % RuleClasses).Append(" c").Append((i * 3) % RuleClasses)
                .Append("\" data-k=\"").Append(i % 17).Append("\">")
                .Append("<span class=\"c").Append((i * 7) % RuleClasses).Append("\"><b>")
                .Append(Sentence(i, 6)).Append("</b></span></div>");
        }

        return new Page("rules", "cascade: per-element rule scan (item #11)",
            css, Document(css, body.ToString()));
    }

    private const int BoxRows = 90;
    private const int BoxNestingDepth = 6;

    /// <summary>
    /// A deep, wide box tree with flex and grid containers and few rules: the page whose cost is
    /// <c>CssBox.PerformLayout</c> rather than the cascade or the rasterizer.
    /// </summary>
    private static Page Boxes()
    {
        const string css =
            "body{font:13px/1.3 sans-serif;margin:0}"
            + ".row{display:flex;gap:4px;margin:2px 0}"
            + ".col{flex:1 1 auto;border:1px solid #ccd;padding:2px}"
            + ".grid{display:grid;grid-template-columns:repeat(4,1fr);gap:2px}"
            + ".cell{border:1px solid #dde;padding:1px}";

        var sb = new StringBuilder(BoxRows * BoxNestingDepth * 128);
        for (var r = 0; r < BoxRows; r++)
        {
            sb.Append("<div class=\"row\">");
            for (var d = 0; d < BoxNestingDepth; d++)
                sb.Append("<div class=\"col\">");

            sb.Append("<div class=\"grid\">");
            for (var c = 0; c < 8; c++)
                sb.Append("<div class=\"cell\">").Append(Sentence(r * 8 + c, 3)).Append("</div>");
            sb.Append("</div>");

            for (var d = 0; d < BoxNestingDepth; d++)
                sb.Append("</div>");
            sb.Append("</div>");
        }

        return new Page("boxes", "layout: nested block/flex/grid tree", css, Document(css, sb.ToString()));
    }

    private const int PaintBoxes = 1400;

    /// <summary>
    /// Overlapping absolutely-positioned boxes with borders, radii, gradients, and translucency:
    /// almost no layout, almost no cascade, and a display list whose replay is the whole cost. This
    /// is the page multithreading items #3 and #5 are aimed at.
    /// </summary>
    private static Page Paint()
    {
        const string css =
            "body{margin:0;font:12px sans-serif}"
            + ".b{position:absolute;border-radius:6px;border:2px solid rgba(0,0,0,.25)}";

        var sb = new StringBuilder(PaintBoxes * 96);
        for (var i = 0; i < PaintBoxes; i++)
        {
            // Deterministic sweep across the viewport; the strides are coprime with the viewport
            // dimensions so the boxes overlap heavily instead of tiling.
            var x = (i * 37) % (ViewportWidth - 120);
            var y = (i * 53) % (ViewportHeight - 90);
            var w = 60 + (i % 60);
            var h = 40 + (i % 45);
            var hue = (i * 11) % 360;
            sb.Append("<div class=\"b\" style=\"left:").Append(x).Append("px;top:").Append(y)
              .Append("px;width:").Append(w).Append("px;height:").Append(h)
              .Append("px;background:linear-gradient(").Append(i % 180).Append("deg,hsla(")
              .Append(hue).Append(",70%,60%,.55),hsla(").Append((hue + 90) % 360).Append(",70%,45%,.55))\"></div>");
        }

        return new Page("paint", "raster: overlapping gradients, borders, alpha",
            css, Document(css, sb.ToString()));
    }

    private const int TableRows = 70;
    private const int TableCols = 10;

    /// <summary>
    /// The blended control: a bordered table with text, some rules, and some painting. No stage is
    /// loaded on purpose, so its row is what a page with no particular emphasis costs.
    /// </summary>
    private static Page Mixed()
    {
        const string css =
            "body{font:13px/1.4 sans-serif;margin:0;padding:12px;background:#fafafa}"
            + "table{border-collapse:collapse;width:100%}"
            + "td,th{border:1px solid #bbb;padding:3px 6px}"
            + "tr:nth-child(2n){background:#f0f0f4}"
            + "th{background:#e2e2ea;font-weight:bold}"
            + "h1{font-size:24px;margin:0 0 12px}";

        var sb = new StringBuilder(TableRows * TableCols * 48);
        sb.Append("<h1>").Append(Sentence(3, 6)).Append("</h1><table><tr>");
        for (var c = 0; c < TableCols; c++)
            sb.Append("<th>").Append(Word(c * 3)).Append("</th>");
        sb.Append("</tr>");
        for (var r = 0; r < TableRows; r++)
        {
            sb.Append("<tr>");
            for (var c = 0; c < TableCols; c++)
                sb.Append("<td>").Append(Sentence(r * TableCols + c, 3)).Append("</td>");
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return new Page("mixed", "blended control: table, borders, text", css, Document(css, sb.ToString()));
    }
}
