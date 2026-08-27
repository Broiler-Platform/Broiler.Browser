using System.Globalization;
using System.Text.RegularExpressions;

namespace Broiler.Layout.IR;

/// <summary>
/// The element nesting <see cref="SvgRenderer"/>'s per-element-type regexes cannot see: which
/// start tags are inside a container that does not paint, and which presentation attributes each
/// one inherits from its ancestors.
/// </summary>
/// <remarks>
/// <para>
/// The renderer matches one regex per shape type over the whole markup, so every <c>&lt;rect&gt;</c>
/// in the document is drawn wherever it sits — including the ones inside <c>&lt;defs&gt;</c>,
/// <c>&lt;symbol&gt;</c> and <c>&lt;pattern&gt;</c>, which SVG 1.1 §5.5/§12.3/§13.3 render only
/// through a reference. That is what made <c>conformance-checkers/html-svg/struct-symbol-01-b</c>
/// paint a symbol's contents across the whole canvas, and what painted a pattern's tile once at the
/// origin. It equally cannot see that <c>&lt;g font-size="32"&gt;</c> sets the size for the
/// <c>&lt;text&gt;</c> inside it.
/// </para>
/// <para>
/// This scans the tags once with a stack and records, per start-tag offset, whether the element
/// paints, what it inherits and the transform it is under. The offsets are the ones
/// <see cref="Regex"/> reports as <see cref="Capture.Index"/> for the same element, so the existing
/// loops stay as they are and only consult this by index.
/// </para>
/// <para>
/// Two kinds of absence are told apart deliberately. Markup inside a comment or a CDATA section is
/// recorded as an inert span (<see cref="IsInert"/>) and does not paint. An offset that is neither
/// an element nor inert is markup the tag scan could not follow, and it keeps painting: a scanning
/// gap must not silently erase a shape the renderer used to draw.
/// </para>
/// </remarks>
internal sealed partial class SvgStructure
{
    /// <summary>
    /// Containers whose children are rendered only when something references them (SVG 1.1 §5.5),
    /// plus the elements whose content is not graphics at all. A start tag inside one of these does
    /// not paint where it stands.
    /// </summary>
    private static readonly HashSet<string> NonRenderingContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "defs", "symbol", "pattern", "mask", "marker", "clipPath", "filter",
        // <foreignObject> content is an HTML subtree the CSS box tree lays out and paints
        // (SvgForeignObjectBoxes); anything shape-shaped inside one must not also be drawn here.
        "foreignObject",
        "linearGradient", "radialGradient", "style", "script", "title", "desc", "metadata",
        "font", "font-face", "font-face-src", "font-face-uri", "glyph", "missing-glyph",
        "altGlyphDef", "view", "hatch", "animate", "animateMotion", "animateTransform", "set",
    };

    /// <summary>
    /// The presentation attributes SVG 1.1 §6.4 marks inheritable that this renderer models. Paint
    /// and font properties only: the geometric attributes (<c>x</c>, <c>width</c>, …) are element
    /// properties and never inherit.
    /// </summary>
    private static readonly string[] InheritableProperties =
    [
        "fill", "fill-opacity", "fill-rule",
        "stroke", "stroke-opacity", "stroke-width", "stroke-linecap", "stroke-linejoin",
        "stroke-dasharray", "stroke-dashoffset", "stroke-miterlimit",
        "color", "visibility", "text-anchor", "text-decoration", "direction", "writing-mode",
        "font-family", "font-size", "font-style", "font-weight", "font-variant", "font-stretch",
        "letter-spacing", "word-spacing",
    ];

    /// <summary>Start-tag offset → attributes inherited from that element's ancestors.</summary>
    private readonly Dictionary<int, IReadOnlyDictionary<string, string>> _rendered = [];

    /// <summary>
    /// Start-tag offset → the user-space transform in effect for that element: every ancestor's
    /// <c>transform</c> composed outermost-first, with the element's own applied last.
    /// </summary>
    private readonly Dictionary<int, SvgTransform> _transforms = [];

    /// <summary>
    /// Start-tag offset → the transform in effect for that element's <em>ancestors</em> alone,
    /// without its own. <c>transform-origin</c> conjugates an element's own transform and only its
    /// own, so the two halves have to be separable.
    /// </summary>
    private readonly Dictionary<int, SvgTransform> _ancestorTransforms = [];

    /// <summary>
    /// Start-tag offset → the element's own attributes, for every element the scan reached,
    /// painting or not.
    /// </summary>
    private readonly Dictionary<int, IReadOnlyDictionary<string, string>> _attributes = [];

    /// <summary><c>id</c> → the element that declares it, for <c>&lt;use href="#id"&gt;</c>.</summary>
    private readonly Dictionary<string, Element> _byId = new(StringComparer.Ordinal);

    /// <summary>
    /// The spans of the comments and CDATA sections in the markup, so a shape regex that matches
    /// inside one can be told apart from an element the scan simply could not follow.
    /// </summary>
    private readonly List<(int Start, int End)> _inertSpans = [];

    /// <summary>
    /// The <c>&lt;svg&gt;</c> elements below the root, in document order, and the content spans they
    /// own — see <see cref="NestedViewports"/>.
    /// </summary>
    private readonly List<NestedViewport> _nestedViewports = [];

    /// <summary>
    /// Every <c>&lt;svg&gt;</c> that is not the outermost one, each of which establishes a viewport
    /// of its own (SVG 1.1 §7.2) rather than drawing its children in the enclosing user space.
    /// </summary>
    /// <remarks>
    /// The renderer's shape passes sweep the whole markup, so before this the children of a nested
    /// <c>&lt;svg x y width height viewBox&gt;</c> were drawn in the <em>root's</em> coordinate
    /// system — every one of the six panels of
    /// <c>conformance-checkers/html-svg/coords-viewattr-03-b</c> landed on top of the first, at the
    /// root's scale. They are collected here so the renderer can render each one's content as its
    /// own document and skip it in the outer passes.
    /// </remarks>
    public IReadOnlyList<NestedViewport> NestedViewports => _nestedViewports;

    /// <summary>One nested <c>&lt;svg&gt;</c>: where it is, what it declares, and what it contains.</summary>
    /// <param name="Index">Its start-tag offset, which orders it against its siblings.</param>
    /// <param name="Attributes">Its own attributes — <c>x</c>/<c>y</c>/<c>width</c>/<c>height</c>,
    /// <c>viewBox</c> and <c>preserveAspectRatio</c>.</param>
    /// <param name="Inherited">The presentation attributes in force where it sits, which its
    /// content must keep inheriting although it is rendered as a separate document.</param>
    public readonly record struct NestedViewport(
        int Index, IReadOnlyDictionary<string, string> Attributes,
        IReadOnlyDictionary<string, string> Inherited,
        string Source, int ContentStart, int ContentEnd)
    {
        /// <summary>The markup this viewport contains.</summary>
        public string Content => ContentEnd > ContentStart
            ? Source[ContentStart..ContentEnd]
            : string.Empty;
    }

    /// <summary>One element the scan reached, addressable by <c>id</c>.</summary>
    /// <param name="Name">Its tag name.</param>
    /// <param name="Attributes">Its own attributes.</param>
    /// <param name="Source">The whole markup the scan ran over.</param>
    /// <param name="ContentStart">Where this element's content begins in <paramref name="Source"/>.</param>
    /// <param name="ContentEnd">Where it ends; equal to the start when the element is self-closing.</param>
    /// <remarks>
    /// The content is held as a range rather than a substring so that scanning costs nothing per
    /// element. Copying it eagerly is quadratic in the document — an element that contains a
    /// thousand id-bearing descendants copies its own body once per descendant — and a large SVG
    /// took gigabytes before this was a range.
    /// </remarks>
    public readonly record struct Element(
        string Name, IReadOnlyDictionary<string, string> Attributes,
        string Source, int ContentStart, int ContentEnd)
    {
        /// <summary>The element's content markup, empty when it is self-closing.</summary>
        public string Content => ContentEnd > ContentStart
            ? Source[ContentStart..ContentEnd]
            : string.Empty;
    }

    private SvgStructure() { }

    /// <summary>An empty scan: nothing paints. Used when the markup is empty.</summary>
    public static SvgStructure Empty { get; } = new();

    /// <summary>
    /// Whether the element starting at <paramref name="index"/> paints where it stands, and if so
    /// what it inherits.
    /// </summary>
    public bool TryGetRendered(int index, out IReadOnlyDictionary<string, string> inherited)
        => _rendered.TryGetValue(index, out inherited!);

    /// <summary>
    /// The user-space transform the element starting at <paramref name="index"/> is drawn under,
    /// or the identity when it is under none.
    /// </summary>
    public SvgTransform TransformAt(int index)
        => _transforms.TryGetValue(index, out var transform) ? transform : SvgTransform.Identity;

    /// <summary>
    /// The transform the element's ancestors put it under, without its own — the half a
    /// <c>transform-origin</c> leaves alone.
    /// </summary>
    public SvgTransform AncestorTransformAt(int index)
        => _ancestorTransforms.TryGetValue(index, out var transform) ? transform : SvgTransform.Identity;

    /// <summary>The attributes of the element starting at <paramref name="index"/>, painting or not.</summary>
    public bool TryGetAttributes(int index, out IReadOnlyDictionary<string, string> attributes)
        => _attributes.TryGetValue(index, out attributes!);

    /// <summary>
    /// Whether <paramref name="index"/> falls inside a comment or CDATA section — markup that looks
    /// like an element to a regex and is text to a parser.
    /// </summary>
    /// <remarks>
    /// Asked separately from <see cref="TryGetRendered"/> because the two absences mean opposite
    /// things: an element the scan could not follow must keep painting (a scanning gap must not
    /// erase a shape), while one inside a comment must not. Most of the SVG 1.1 suite carries a
    /// commented-out <c>&lt;g id="draft-watermark"&gt;</c> with a red bar and a DRAFT label.
    /// </remarks>
    public bool IsInert(int index)
    {
        // The spans are recorded in match order, so they are sorted and disjoint: a binary search
        // keeps this off the quadratic path when a document carries many comments and many shapes.
        int low = 0, high = _inertSpans.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            var (start, end) = _inertSpans[mid];
            if (index < start)
                high = mid - 1;
            else if (index >= end)
                low = mid + 1;
            else
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="index"/> falls inside a nested <c>&lt;svg&gt;</c>, whose content the
    /// nested-viewport pass renders as its own document and the outer shape passes must therefore
    /// leave alone.
    /// </summary>
    /// <remarks>
    /// Scanned rather than binary-searched because these spans nest inside one another, so they are
    /// not disjoint; a document has a handful of them at most.
    /// </remarks>
    public bool IsInNestedViewport(int index)
    {
        foreach (var viewport in _nestedViewports)
        {
            if (index > viewport.Index && index < viewport.ContentEnd)
                return true;
        }

        return false;
    }

    /// <summary>Resolves an <c>id</c> to the element that declares it.</summary>
    public bool TryGetById(string id, out Element element)
    {
        element = default;
        return !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out element);
    }

    /// <summary>
    /// Walks every start and end tag, tracking the inherited presentation attributes and the depth
    /// of non-rendering containers.
    /// </summary>
    /// <remarks>
    /// End tags are matched by name rather than trusted blindly: SVG in an HTML document reaches
    /// here through the box tree, which is well-formed, but an SVG loaded as an image is raw bytes.
    /// An unmatched end tag pops nothing rather than unbalancing the stack for the rest of the file.
    /// </remarks>
    public static SvgStructure Scan(string svgXml)
    {
        if (string.IsNullOrEmpty(svgXml))
            return Empty;

        var structure = new SvgStructure();
        var open = new List<(string Name, IReadOnlyDictionary<string, string> Inherited,
            bool Suppresses, SvgTransform Transform, IReadOnlyDictionary<string, string> Attributes,
            int ContentStart, int StartIndex, bool IsNestedViewport,
            IReadOnlyDictionary<string, string> ParentInherited)>();
        int nonRenderingDepth = 0;
        int svgDepth = 0;

        foreach (Match m in TagRegex().Matches(svgXml))
        {
            // A comment, CDATA section, processing instruction or doctype: not an element, and the
            // markup inside it is text. Matching them here is what keeps a commented-out
            // <g id="draft-watermark"> — which most of the SVG 1.1 test suite carries — unpainted.
            if (!m.Groups["name"].Success)
            {
                structure._inertSpans.Add((m.Index, m.Index + m.Length));
                continue;
            }

            string name = m.Groups["name"].Value;
            bool isEnd = m.Groups["close"].Value.Length > 0;

            if (isEnd)
            {
                int last = open.FindLastIndex(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (last < 0)
                    continue;

                for (int i = open.Count - 1; i >= last; i--)
                {
                    if (open[i].Suppresses)
                        nonRenderingDepth--;
                    if (open[i].Name.Equals("svg", StringComparison.OrdinalIgnoreCase))
                        svgDepth--;
                }

                // The element is complete, so its content span is known: record it under its id.
                var closed = open[last];
                if (closed.Attributes.TryGetValue("id", out var closedId) && closedId.Length > 0)
                {
                    structure._byId[closedId] = new Element(
                        closed.Name, closed.Attributes, svgXml, closed.ContentStart, m.Index);
                }

                // A nested <svg> that paints where it stands establishes its own viewport, so its
                // content is rendered separately rather than by the outer passes. One inside <defs>
                // is not recorded: it paints only through a <use>, which renders it itself.
                if (closed.IsNestedViewport)
                {
                    structure._nestedViewports.Add(new NestedViewport(
                        closed.StartIndex, closed.Attributes, closed.ParentInherited,
                        svgXml, closed.ContentStart, m.Index));
                }

                open.RemoveRange(last, open.Count - last);
                continue;
            }

            var inherited = open.Count > 0 ? open[^1].Inherited : EmptyAttributes;
            var ancestorTransform = open.Count > 0 ? open[^1].Transform : SvgTransform.Identity;
            var attributes = ParseAttributes(m.Groups["attrs"].Value);
            structure._attributes[m.Index] = attributes;

            // SVG 1.1 §7.4: an element's transform applies to it as well as to its children, so the
            // element itself is recorded under the composition that includes its own.
            var transform = ancestorTransform.Concat(
                SvgTransform.Parse(attributes.GetValueOrDefault("transform")));

            bool paints = nonRenderingDepth == 0 && !NonRenderingContainers.Contains(name)
                && !IsDisplayNone(attributes);
            if (paints)
            {
                structure._rendered[m.Index] = inherited;
                if (!transform.IsIdentity)
                    structure._transforms[m.Index] = transform;
                if (!ancestorTransform.IsIdentity)
                    structure._ancestorTransforms[m.Index] = ancestorTransform;
            }

            if (m.Groups["empty"].Value.Length > 0)
            {
                if (attributes.TryGetValue("id", out var emptyId) && emptyId.Length > 0)
                    structure._byId[emptyId] = new Element(name, attributes, svgXml, 0, 0);
                continue;
            }

            bool isSvg = name.Equals("svg", StringComparison.OrdinalIgnoreCase);
            bool nestedViewport = isSvg && svgDepth > 0 && paints;
            if (isSvg)
                svgDepth++;

            // Track the suppression on the open element rather than re-deriving it at the end tag:
            // an element nested inside a container that does not paint suppresses its own children
            // too, and matching that against the container list alone would decrement fewer times
            // than it incremented, leaving the rest of the document permanently suppressed.
            bool suppresses = !paints;
            if (suppresses)
                nonRenderingDepth++;

            open.Add((
                name, Extend(inherited, attributes), suppresses, transform,
                attributes, m.Index + m.Length, m.Index, nestedViewport, inherited));
        }

        // Recorded as each end tag is met, so an outer viewport lands after the inner ones it
        // contains; the renderer paints them in document order and needs them that way round.
        structure._nestedViewports.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        return structure;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The inherited set an element's children see: its parent's, overridden by whichever
    /// inheritable properties this element declares. Returns the parent's own map unchanged when
    /// the element declares none, so a deep tree of plain <c>&lt;g&gt;</c>s allocates nothing.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Extend(
        IReadOnlyDictionary<string, string> inherited, IReadOnlyDictionary<string, string> attributes)
    {
        Dictionary<string, string>? extended = null;
        foreach (var property in InheritableProperties)
        {
            string? value = ReadPresentation(attributes, property);
            if (value == null || (inherited.TryGetValue(property, out var existing) && existing == value))
                continue;

            extended ??= new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
            extended[property] = value;
        }

        // `opacity` is deliberately NOT in InheritableProperties above, because it does not inherit:
        // SVG 1.1 §14.5 applies it to the element it is written on, that element's children
        // included, as one composite. But this renderer draws leaves — it matches one regex per
        // shape type and has no group to hang a layer on — so a `<g opacity="0.5">` had nowhere to
        // put its value and dropped it. Carrying the running product of the ancestors' opacity
        // lets each shape apply its share of it.
        float declared = ParseOpacity(ReadPresentation(attributes, "opacity"));
        if (declared < 1f)
        {
            extended ??= new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
            extended[AncestorOpacityKey] =
                (AncestorOpacityOf(inherited) * declared).ToString(CultureInfo.InvariantCulture);
        }

        return extended ?? inherited;
    }

    /// <summary>
    /// The key the ancestors' composed <c>opacity</c> travels under in an element's inherited map.
    /// Not an SVG property — the leading marker keeps it out of the way of anything an author can
    /// write, and it is read back only by <see cref="AncestorOpacityOf"/>.
    /// </summary>
    internal const string AncestorOpacityKey = "-broiler-ancestor-opacity";

    /// <summary>
    /// The product of the <c>opacity</c> values on an element's ancestors, or <c>1</c> when none of
    /// them fades it.
    /// </summary>
    /// <remarks>
    /// Multiplying the group's opacity into each leaf is exact while the group's children do not
    /// overlap one another, which is the ordinary case and the one the SVG test suites are built
    /// from. Where they do overlap, true group compositing flattens the children first and fades
    /// the result once, so the overlap would come out slightly darker there than here. That is a
    /// far smaller error than dropping the group's opacity altogether, which is what happened
    /// before.
    /// </remarks>
    internal static float AncestorOpacityOf(IReadOnlyDictionary<string, string> attributes)
        => attributes.TryGetValue(AncestorOpacityKey, out var raw)
            && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : 1f;

    /// <summary>An SVG <c>&lt;alpha-value&gt;</c> — a number or a percentage — clamped to 0..1.</summary>
    private static float ParseOpacity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 1f;

        var value = raw.AsSpan().Trim();
        bool percent = value.Length > 0 && value[^1] == '%';
        if (percent)
            value = value[..^1];

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? Math.Clamp(percent ? parsed / 100f : parsed, 0f, 1f)
            : 1f;
    }

    /// <summary>
    /// A property from the element's <c>style</c> declaration if it is there, and from the
    /// presentation attribute of the same name otherwise — the order SVG 1.1 §6.4 gives them.
    /// </summary>
    private static string? ReadPresentation(IReadOnlyDictionary<string, string> attributes, string property)
    {
        if (attributes.TryGetValue("style", out var style) && !string.IsNullOrWhiteSpace(style))
        {
            foreach (var declaration in style.Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon > 0
                    && declaration.AsSpan(0, colon).Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
                {
                    var value = declaration.AsSpan(colon + 1).Trim();
                    if (!value.IsEmpty)
                        return value.ToString();
                }
            }
        }

        return attributes.TryGetValue(property, out var attribute) && !string.IsNullOrWhiteSpace(attribute)
            ? attribute
            : null;
    }

    private static bool IsDisplayNone(IReadOnlyDictionary<string, string> attributes)
    {
        string? display = ReadPresentation(attributes, "display");
        return display != null && display.Trim().Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseAttributes(string attrStr)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttributeRegex().Matches(attrStr))
        {
            var quoted = m.Groups["dq"];
            dict[m.Groups["name"].Value] = quoted.Success ? quoted.Value : m.Groups["sq"].Value;
        }
        return dict;
    }

    /// <summary>
    /// One markup construct: a comment/CDATA/PI/doctype (no <c>name</c> group, so it is skipped),
    /// or a start or end tag. Attribute values are matched as quoted runs so a <c>&gt;</c> inside
    /// one does not end the tag.
    /// </summary>
    [GeneratedRegex(
        """<!--.*?-->|<!\[CDATA\[.*?\]\]>|<\?.*?\?>|<!DOCTYPE(?:[^>"']|"[^"]*"|'[^']*')*>|<(?<close>/?)(?<name>[A-Za-z_][\w:.\-]*)(?<attrs>(?:[^>"']|"[^"]*"|'[^']*')*?)(?<empty>/?)>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex("""(?<name>[\w:.\-]+)\s*=\s*(?:"(?<dq>[^"]*)"|'(?<sq>[^']*)')""")]
    private static partial Regex AttributeRegex();
}
