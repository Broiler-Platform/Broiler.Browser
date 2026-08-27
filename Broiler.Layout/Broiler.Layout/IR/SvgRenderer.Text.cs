#nullable disable
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.CSS;
using Broiler.Graphics;

namespace Broiler.Layout.IR;

/// <summary>
/// SVG <c>&lt;text&gt;</c> painting, and the element lookup the shape loops share with it.
/// </summary>
internal static partial class SvgRenderer
{
    /// <summary>
    /// The fraction of a font's line height that sits above the baseline. The same approximation
    /// <c>CssLayoutEngine</c> uses to place a line box's baseline, reused here because SVG gives
    /// <c>y</c> as the baseline while the backend's <c>DrawString</c> takes the top of the ascent box.
    /// </summary>
    private const double TypicalAscentRatio = 0.8;

    /// <summary>
    /// The page-space transform the matched element is drawn under: its own and its ancestors'
    /// <c>transform</c> attributes, conjugated by the view-box mapping so it acts on the
    /// already-converted page coordinates the display items carry.
    /// </summary>
    private static SvgTransform PageTransformOf(
        SvgStructure structure, Match match, RectangleF bounds,
        float sx, float sy, float tx, float ty)
        => structure.TransformAt(match.Index).ToPageSpace(sx, sy, bounds.X + tx, bounds.Y + ty);

    /// <summary>
    /// Resolves one matched element against the scanned structure: <see langword="false"/> when it
    /// sits inside a container that does not paint, otherwise its own attributes with the
    /// inheritable ones its ancestors declared filled in.
    /// </summary>
    /// <remarks>
    /// An element the scan never reached — markup whose shape the tag regex could not follow — is
    /// treated as painting with nothing inherited, which is what this renderer did before the scan
    /// existed, so a scanning gap cannot silently erase a shape. A match inside a comment or CDATA
    /// section is the one absence that does mean "does not paint", and the scan records those spans
    /// explicitly for exactly that reason.
    /// </remarks>
    private static bool TryResolveElement(
        SvgStructure structure, Match match, out Dictionary<string, string> attrs)
    {
        bool paints = structure.TryGetRendered(match.Index, out var inherited);
        if (!paints && (structure.TryGetAttributes(match.Index, out _) || structure.IsInert(match.Index)))
        {
            attrs = null;
            return false;
        }

        // Inside a nested <svg> the element belongs to that viewport's own coordinate system, and
        // the nested-viewport pass renders it there. Drawing it here as well would put a second copy
        // on the canvas at the outer scale.
        if (structure.IsInNestedViewport(match.Index))
        {
            attrs = null;
            return false;
        }

        attrs = ParseAttributes(match.Groups[1].Value);
        if (inherited != null)
        {
            foreach (var (property, value) in inherited)
                attrs.TryAdd(property, value);
        }

        return true;
    }

    /// <summary>
    /// Emits the <c>&lt;text&gt;</c> elements that lay their glyphs out along the normal text
    /// direction — every one that is not a <c>&lt;textPath&gt;</c>, which the caller handles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>DrawSvgTextItem</c> is only drawn when it carries a resolved font
    /// (<c>RGraphicsRasterBackend.RenderSvgText</c> returns early otherwise), and nothing ever set
    /// one, so every SVG <c>&lt;text&gt;</c> in the engine painted nothing at all. The font comes
    /// from <see cref="SvgTextEnvironment"/>, published for the pass by the fragment-tree build.
    /// </para>
    /// <para>
    /// Child markup is flattened to its text: a <c>&lt;tspan&gt;</c> with its own position is drawn
    /// in the flow rather than at that position, which is closer than dropping it and much closer
    /// than drawing the raw <c>&lt;tspan …&gt;</c> tag as literal glyphs — what the old code did,
    /// since it never stripped the children it captured.
    /// </para>
    /// </remarks>
    private static void EmitTextElements(
        string svgXml, RectangleF bounds, SvgPaintOrder order, SvgStructure structure,
        float sx, float sy, float tx, float ty, float pctW, float pctH, float pctD)
    {
        foreach (Match m in TextElementRegex().Matches(svgXml))
        {
            if (ParseTextPathRegex().IsMatch(m.Groups[2].Value))
                continue;

            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            string text = FlattenTextContent(m.Groups[2].Value);
            if (text.Length == 0)
                continue;

            var items = order.Open(m.Index);
            float scale = Math.Max(sx, sy);
            float fontSize = GetLength(attrs, "font-size", pctD, 16) * scale;
            var font = ResolveFont(attrs, fontSize);

            float x = GetLength(attrs, "x", pctW) * sx + tx;
            float y = GetLength(attrs, "y", pctH) * sy + ty;

            AddShape(
                items, bounds, attrs, PageTransformOf(structure, m, bounds, sx, sy, tx, ty),
                new DrawSvgTextItem
                {
                    Bounds = bounds,
                    X = x - AnchorOffset(attrs, font, text),
                    // SVG 1.1 §10.4 places the glyphs on the baseline at y; the backend draws from
                    // the top of the ascent box, so the item carries the top instead.
                    Y = y - (float)AscentOf(font, fontSize),
                    FontSize = fontSize,
                    FontFamily = attrs.GetValueOrDefault("font-family") ?? "Arial",
                    FontHandle = font,
                    Fill = GetPaint(attrs, "fill", BColor.Black) is { IsEmpty: false } fill
                        ? fill
                        : BColor.Black,
                    Text = text,
                });
        }
    }

    /// <summary>
    /// Resolves the font for one text element from its (already inheritance-merged) presentation
    /// attributes. Returns <see langword="null"/> when no environment is published for this pass —
    /// an SVG rasterised as a standalone image — leaving the text undrawn as before.
    /// </summary>
    private static ILayoutFont ResolveFont(Dictionary<string, string> attrs, float fontSizePx)
    {
        var style = LayoutFontStyle.Regular;

        string fontStyle = GetPresentationValue(attrs, "font-style");
        if (fontStyle != null
            && (fontStyle.Equals("italic", StringComparison.OrdinalIgnoreCase)
                || fontStyle.Equals("oblique", StringComparison.OrdinalIgnoreCase)))
        {
            style |= LayoutFontStyle.Italic;
        }

        if (IsBoldWeight(GetPresentationValue(attrs, "font-weight")))
            style |= LayoutFontStyle.Bold;

        string family = GetPresentationValue(attrs, "font-family");
        return SvgTextEnvironment.GetFont(
            string.IsNullOrWhiteSpace(family) ? CssConstants.DefaultFont : family, fontSizePx, style);
    }

    /// <summary>
    /// CSS Fonts 4 §2.4: <c>bold</c>, <c>bolder</c> and any numeric weight of 600 or more are bold.
    /// <c>bolder</c> resolves against the inherited weight, which this renderer does not track, so it
    /// is read as plain bold — the value it takes from every weight up to 500.
    /// </summary>
    private static bool IsBoldWeight(string weight)
    {
        if (string.IsNullOrWhiteSpace(weight))
            return false;

        var trimmed = weight.Trim();
        if (trimmed.Equals("bold", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("bolder", StringComparison.OrdinalIgnoreCase))
            return true;

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric)
            && numeric >= 600;
    }

    /// <summary>Distance from the top of the ascent box to the baseline, in CSS pixels.</summary>
    private static double AscentOf(ILayoutFont font, float fontSizePx)
        => font != null
            ? font.Height * CssMetrics.PtToPx * TypicalAscentRatio
            : fontSizePx * TypicalAscentRatio;

    /// <summary>
    /// How far left of <c>x</c> the run starts, per <c>text-anchor</c> (SVG 1.1 §10.9.1): nothing
    /// for <c>start</c>, half the advance for <c>middle</c>, all of it for <c>end</c>.
    /// </summary>
    private static float AnchorOffset(Dictionary<string, string> attrs, ILayoutFont font, string text)
    {
        string anchor = GetPresentationValue(attrs, "text-anchor")?.Trim();
        if (string.IsNullOrEmpty(anchor)
            || anchor.Equals("start", StringComparison.OrdinalIgnoreCase))
            return 0f;

        bool middle = anchor.Equals("middle", StringComparison.OrdinalIgnoreCase);
        if (!middle && !anchor.Equals("end", StringComparison.OrdinalIgnoreCase))
            return 0f;

        float width = SvgTextEnvironment.MeasureWidth(font, text);
        return middle ? width / 2f : width;
    }

    /// <summary>
    /// The rendered characters of a text element: child markup removed, entity references resolved,
    /// and whitespace collapsed the way <c>xml:space="default"</c> asks for (SVG 1.1 §10.15) — every
    /// run of white space becomes one space, and leading and trailing space is dropped.
    /// </summary>
    private static string FlattenTextContent(string content)
    {
        string stripped = DrawSvgTextRegex().Replace(content, string.Empty);
        var sb = new StringBuilder(stripped.Length);
        bool pendingSpace = false;

        foreach (char ch in DecodeEntities(stripped))
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Resolves the XML entity references the SVG serializer writes (<c>&amp;amp;</c>,
    /// <c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;quot;</c>, <c>&amp;apos;</c>) plus numeric
    /// character references. Left as-is when the markup carries none, which is the common case.
    /// </summary>
    private static string DecodeEntities(string text)
    {
        if (text.IndexOf('&') < 0)
            return text;

        return EntityRegex().Replace(text, static m =>
        {
            string name = m.Groups["name"].Value;
            switch (name)
            {
                case "amp": return "&";
                case "lt": return "<";
                case "gt": return ">";
                case "quot": return "\"";
                case "apos": return "'";
                case "nbsp": return " ";
            }

            var numeric = m.Groups["dec"];
            var hex = m.Groups["hex"];
            int code;
            if (numeric.Success && int.TryParse(numeric.Value, out code))
                return char.ConvertFromUtf32(code);
            if (hex.Success && int.TryParse(hex.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                return char.ConvertFromUtf32(code);

            return m.Value;
        });
    }

    /// <summary>
    /// A <c>&lt;text&gt;</c> element and its content. Unlike the attribute-requiring pattern this
    /// replaced, it also matches a bare <c>&lt;text&gt;</c>.
    /// </summary>
    [GeneratedRegex(@"<text\b([^>]*)>(.*?)</text\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TextElementRegex();

    [GeneratedRegex(@"&(?:(?<name>[A-Za-z][A-Za-z0-9]*)|#(?<dec>[0-9]+)|#[xX](?<hex>[0-9A-Fa-f]+));")]
    private static partial Regex EntityRegex();
}
