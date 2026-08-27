#nullable disable
using System.Drawing;
using System.Text;

namespace Broiler.Layout.IR;

/// <summary>
/// The nested <c>&lt;svg&gt;</c> element, which establishes a viewport of its own.
/// </summary>
/// <remarks>
/// SVG 1.1 §7.2: an <c>&lt;svg&gt;</c> inside another one gives its children a new viewport at its
/// <c>x</c>/<c>y</c>/<c>width</c>/<c>height</c>, with its own <c>viewBox</c> mapping. The renderer's
/// shape passes sweep the whole markup and knew only the root's mapping, so the children of a
/// nested <c>&lt;svg&gt;</c> were drawn at the root's scale and origin — the six panels of
/// <c>conformance-checkers/html-svg/coords-viewattr-03-b</c> all landed on the first one, and
/// <c>struct-image-02-b</c>'s aqua panel filled the canvas instead of its own quarter of it.
/// </remarks>
internal static partial class SvgRenderer
{
    /// <summary>How deep nested viewports are followed before the recursion is cut off.</summary>
    private const int MaxViewportDepth = 6;

    [ThreadStatic]
    private static int _viewportDepth;

    /// <summary>Renders each nested <c>&lt;svg&gt;</c>'s content into the viewport it establishes.</summary>
    private static void EmitNestedViewports(
        string svgXml, RectangleF bounds, SvgPaintOrder order, SvgStructure structure,
        float sx, float sy, float tx, float ty, float pctW, float pctH)
    {
        if (structure.NestedViewports.Count == 0 || _viewportDepth >= MaxViewportDepth)
            return;

        // A reference inside a nested viewport may point at a definition declared outside it, so the
        // enclosing document's <clipPath>s travel with the content into its own document — where the
        // recursive scan is the only thing that can resolve them.
        string definitions = CollectReferencedDefinitions(svgXml);

        foreach (var viewport in structure.NestedViewports)
        {
            // A viewport inside another one is rendered by that one's own recursive pass, not twice
            // from here.
            if (structure.IsInNestedViewport(viewport.Index))
                continue;

            string content = viewport.Content;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var attrs = new Dictionary<string, string>(viewport.Attributes, StringComparer.OrdinalIgnoreCase);

            // SVG 1.1 §7.2: x/y default to 0 and width/height to 100% of the enclosing viewport.
            float x = GetLength(attrs, "x", pctW);
            float y = GetLength(attrs, "y", pctH);
            float width = attrs.ContainsKey("width") ? GetLength(attrs, "width", pctW) : pctW;
            float height = attrs.ContainsKey("height") ? GetLength(attrs, "height", pctH) : pctH;
            if (width <= 0 || height <= 0)
                continue;

            var destination = new RectangleF(
                bounds.X + x * sx + tx, bounds.Y + y * sy + ty, width * sx, height * sy);

            // With no viewBox the child user space is the viewport's own, which a view box matching
            // it expresses exactly — the same equivalence EmitSymbolUse relies on.
            string viewBox = attrs.GetValueOrDefault("viewBox");
            if (string.IsNullOrWhiteSpace(viewBox))
                viewBox = FormattableString.Invariant($"0 0 {width} {height}");

            var items = order.Open(viewport.Index);
            _viewportDepth++;
            try
            {
                AddMapped(
                    items,
                    RenderSvgContent(
                        WrapViewportDocument(viewBox, attrs.GetValueOrDefault("preserveAspectRatio"),
                            viewport.Inherited, definitions, content),
                        destination),
                    structure.TransformAt(viewport.Index).ToPageSpace(sx, sy, bounds.X + tx, bounds.Y + ty));
            }
            finally
            {
                _viewportDepth--;
            }
        }
    }

    /// <summary>
    /// The nested viewport's content as a standalone document: a root <c>&lt;svg&gt;</c> carrying
    /// the view-box mapping, and the presentation attributes in force outside it so its content
    /// keeps inheriting them across the boundary.
    /// </summary>
    private static string WrapViewportDocument(
        string viewBox, string preserveAspectRatio,
        IReadOnlyDictionary<string, string> inherited, string definitions, string content)
    {
        var sb = new StringBuilder("<svg viewBox=\"").Append(viewBox).Append('"');

        if (!string.IsNullOrWhiteSpace(preserveAspectRatio))
            sb.Append(" preserveAspectRatio=\"").Append(preserveAspectRatio).Append('"');

        if (inherited != null)
        {
            foreach (var (property, value) in inherited)
            {
                // A value holding a double quote came from single-quoted source; it would need
                // escaping to survive re-serialisation, and dropping it only loses an inherited
                // default the content can still declare for itself.
                if (value.IndexOf('"') >= 0)
                    continue;

                sb.Append(' ').Append(property).Append("=\"").Append(value).Append('"');
            }
        }

        sb.Append('>');

        if (!string.IsNullOrEmpty(definitions))
            sb.Append("<defs>").Append(definitions).Append("</defs>");

        return sb.Append(content).Append("</svg>").ToString();
    }

    /// <summary>
    /// The enclosing document's <c>&lt;clipPath&gt;</c> definitions, concatenated, so a nested
    /// viewport rendered as its own document can still resolve a <c>clip-path</c> that points at
    /// one of them. Returns an empty string when there are none, which is the usual case.
    /// </summary>
    private static string CollectReferencedDefinitions(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml)
            || svgXml.IndexOf("<clippath", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (System.Text.RegularExpressions.Match cm in ClipPathBlockRegex().Matches(svgXml))
            sb.Append(cm.Value);

        return sb.ToString();
    }
}
