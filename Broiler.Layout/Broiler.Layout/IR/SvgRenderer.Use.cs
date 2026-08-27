#nullable disable
using System.Drawing;
using System.Text.RegularExpressions;

namespace Broiler.Layout.IR;

/// <summary>
/// <c>&lt;use&gt;</c>, and the <c>&lt;symbol&gt;</c> viewport it establishes.
/// </summary>
/// <remarks>
/// A <c>&lt;symbol&gt;</c> is never drawn where it is declared — only through a reference — and
/// nothing here drew the reference, so <c>conformance-checkers/html-svg/struct-symbol-01-b</c>
/// painted the symbol's own contents over the whole canvas and nothing at either place the test
/// puts them.
/// </remarks>
internal static partial class SvgRenderer
{
    /// <summary>How deep a <c>&lt;use&gt;</c> may reference another.</summary>
    private const int MaxUseDepth = 4;

    [ThreadStatic]
    private static int _useDepth;

    /// <summary>Draws every <c>&lt;use&gt;</c> that paints where it stands.</summary>
    private static void EmitUseElements(
        string svgXml, RectangleF bounds, SvgPaintOrder order, SvgStructure structure,
        float sx, float sy, float tx, float ty, float pctW, float pctH)
    {
        if (_useDepth >= MaxUseDepth
            || svgXml.IndexOf("<use", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        foreach (Match m in UseElementRegex().Matches(svgXml))
        {
            if (!TryResolveElement(structure, m, out var attrs))
                continue;

            string href = attrs.GetValueOrDefault("href") ?? attrs.GetValueOrDefault("xlink:href");
            if (string.IsNullOrEmpty(href) || href.Length < 2 || href[0] != '#')
                continue;

            if (!structure.TryGetById(href[1..], out var target))
                continue;

            float x = GetLength(attrs, "x", pctW);
            float y = GetLength(attrs, "y", pctH);

            // SVG 1.1 §5.6: <use> on a <symbol> (or an <svg>) makes a viewport of its width/height,
            // both defaulting to 100% of the current viewport; on anything else it is a translated
            // clone and width/height have no effect.
            bool establishesViewport =
                target.Name.Equals("symbol", StringComparison.OrdinalIgnoreCase)
                || target.Name.Equals("svg", StringComparison.OrdinalIgnoreCase);

            var items = order.Open(m.Index);
            var elementTransform = PageTransformOf(structure, m, bounds, sx, sy, tx, ty);
            _useDepth++;
            try
            {
                if (establishesViewport)
                    EmitSymbolUse(items, bounds, target, attrs, x, y, sx, sy, tx, ty, pctW, pctH, elementTransform);
                else
                    EmitCloneUse(items, bounds, target, x, y, sx, sy, tx, ty, elementTransform);
            }
            finally
            {
                _useDepth--;
            }
        }
    }

    /// <summary>
    /// Renders a referenced <c>&lt;symbol&gt;</c> into the viewport the <c>&lt;use&gt;</c> gives it,
    /// carrying the symbol's own <c>viewBox</c> and <c>preserveAspectRatio</c>.
    /// </summary>
    private static void EmitSymbolUse(
        List<DisplayItem> items, RectangleF bounds, SvgStructure.Element target,
        Dictionary<string, string> useAttrs, float x, float y,
        float sx, float sy, float tx, float ty, float pctW, float pctH, SvgTransform pageTransform)
    {
        float width = useAttrs.TryGetValue("width", out _) ? GetLength(useAttrs, "width", pctW) : pctW;
        float height = useAttrs.TryGetValue("height", out _) ? GetLength(useAttrs, "height", pctH) : pctH;
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(target.Content))
            return;

        var viewport = new RectangleF(
            bounds.X + x * sx + tx, bounds.Y + y * sy + ty, width * sx, height * sy);

        // A symbol with no viewBox does not scale its content; its user space is the viewport's,
        // which a view box matching the viewport expresses exactly.
        string viewBox = target.Attributes.GetValueOrDefault("viewBox");
        if (string.IsNullOrWhiteSpace(viewBox))
            viewBox = FormattableString.Invariant($"0 0 {width} {height}");

        string preserve = target.Attributes.GetValueOrDefault("preserveAspectRatio");
        string document = "<svg viewBox=\"" + viewBox + "\""
            + (string.IsNullOrWhiteSpace(preserve) ? string.Empty : " preserveAspectRatio=\"" + preserve + "\"")
            + ">" + target.Content + "</svg>";

        AddMapped(items, RenderSvgContent(document, viewport), pageTransform);
    }

    /// <summary>
    /// Renders a referenced element that is not a viewport-establishing one: a deep clone offset by
    /// the <c>&lt;use&gt;</c>'s <c>x</c>/<c>y</c>.
    /// </summary>
    private static void EmitCloneUse(
        List<DisplayItem> items, RectangleF bounds, SvgStructure.Element target,
        float x, float y, float sx, float sy, float tx, float ty, SvgTransform pageTransform)
    {
        string markup = RenderMarkupOf(target);
        if (string.IsNullOrWhiteSpace(markup))
            return;

        // The clone shares the referencing document's user space, so it renders against the same
        // origin and scale — expressed as a view box over the whole viewport — shifted by x/y.
        var clone = RenderSvgContent(
            FormattableString.Invariant(
                $"<svg viewBox=\"{-x} {-y} {bounds.Width / sx} {bounds.Height / sy}\" preserveAspectRatio=\"none\">{markup}</svg>"),
            new RectangleF(bounds.X + tx, bounds.Y + ty, bounds.Width, bounds.Height));

        AddMapped(items, clone, pageTransform);
    }

    private static void AddMapped(
        List<DisplayItem> items, List<DisplayItem> produced, SvgTransform pageTransform)
        => items.AddRange(pageTransform.IsIdentity
            ? produced
            : SvgItemTransformer.Map(produced, pageTransform));

    /// <summary>The referenced element re-serialised as markup, so it can be rendered as content.</summary>
    private static string RenderMarkupOf(SvgStructure.Element target)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('<').Append(target.Name);
        foreach (var (name, value) in target.Attributes)
        {
            if (name is "id" or "href" or "xlink:href")
                continue;
            sb.Append(' ').Append(name).Append("=\"").Append(value).Append('"');
        }

        if (string.IsNullOrEmpty(target.Content))
            return sb.Append("/>").ToString();

        return sb.Append('>').Append(target.Content).Append("</").Append(target.Name).Append('>').ToString();
    }

    [GeneratedRegex(@"<use\b((?:[^>""']|""[^""]*""|'[^']*')*?)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex UseElementRegex();
}
