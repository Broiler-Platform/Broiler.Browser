using System.Drawing;
using System.Globalization;
using System.Text;
using Broiler.Graphics.Color;
using Broiler.HTML.Image;
using Broiler.Layout;
using Broiler.Layout.IR;
using BDom = Broiler.Dom;

namespace Broiler.Cli.Analysis;

/// <summary>One element the layout placed somewhere a reader should look at.</summary>
/// <param name="Element">The element, as <c>tag#id.class</c>.</param>
/// <param name="Path">Its nearest ancestors, outermost first, so it can be found in the document.</param>
/// <param name="Box">Its border box, <c>x,y w×h</c>, in document coordinates.</param>
/// <param name="Detail">What is notable about it.</param>
internal sealed record LayoutFinding(string Element, string Path, string Box, string Detail);

/// <summary>What the layout of the page says about itself.</summary>
internal sealed record LayoutReport
{
    public int Boxes { get; init; }
    public int TextRuns { get; init; }
    public int MaxDepth { get; init; }
    public string ContentSize { get; init; } = string.Empty;
    public double ContentHeight { get; init; }
    public IReadOnlyDictionary<string, int> BoxesByDisplay { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<string> InvariantViolations { get; init; } = [];

    /// <summary>Elements that reach past the right edge of the viewport, which is what gives a page a horizontal scrollbar.</summary>
    public IReadOnlyList<LayoutFinding> HorizontalOverflow { get; init; } = [];

    /// <summary>Elements with text of their own that were laid out with no width or no height.</summary>
    public IReadOnlyList<LayoutFinding> CollapsedWithText { get; init; } = [];

    /// <summary>Elements laid out entirely above or to the left of the page.</summary>
    public IReadOnlyList<LayoutFinding> OffPage { get; init; } = [];

    /// <summary>
    /// Boxes far taller than everything inside them together — the boxes a runaway page height starts
    /// from, rather than the containers that are tall only because they hold one.
    /// </summary>
    public IReadOnlyList<LayoutFinding> TallerThanContent { get; init; } = [];

    /// <summary>Files of the layout that could not be written, and why.</summary>
    public IReadOnlyList<string> DumpErrors { get; init; } = [];

    /// <summary>Elements in the body the layout made no box for, by tag — <c>display: none</c> or not rendered.</summary>
    public IReadOnlyDictionary<string, int> UnboxedElementsByTag { get; init; } = new Dictionary<string, int>();

    /// <summary>The font-family lists the rendered text asked for, with how many text runs asked for each.</summary>
    public IReadOnlyDictionary<string, int> FontFamilies { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Reads a render's layout: dumps its fragment tree for a reader, outlines its boxes on the
/// screenshot, and finds the elements whose placement usually means a rendering problem.
/// </summary>
/// <remarks>
/// <para>
/// <b>Findings are attributed to elements, not to boxes.</b> A fragment knows its tag but not its
/// element, so "a div overflows" is all a fragment tree can say. The element geometry
/// (<see cref="HtmlContainer.GetLayoutGeometry"/>) is keyed by the DOM element instead, so each
/// finding can name <c>div#main.hero</c> and the ancestors it sits in — which is what a reader
/// searches the document for.
/// </para>
/// <para>
/// <b>Each list is a lead, not a verdict.</b> A box past the right edge may be a carousel that clips
/// it, and a box at <c>-9999px</c> is the usual way to hide a "skip to content" link. The lists are
/// short and sorted so the real problem, when there is one, is near the top.
/// </para>
/// </remarks>
internal static class LayoutInspector
{
    /// <summary>How many elements each list keeps.</summary>
    internal const int MaxFindings = 25;

    private static readonly HashSet<string> NeverBoxed = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "script", "style", "template", "meta", "link", "title", "base", "noscript", "br", "wbr",
        "source", "track", "param", "datalist", "option", "optgroup", "col", "colgroup",
    };

    /// <summary>Summarises the layout of a render.</summary>
    /// <param name="fragments">The fragment tree of the viewport layout.</param>
    /// <param name="invariantViolations">What the layout's own checker reported.</param>
    /// <param name="contentSize">The laid-out document size.</param>
    /// <param name="geometry">The element geometry of the same document, or null when it could not be taken.</param>
    /// <param name="viewportWidth">The viewport width the layout was made at.</param>
    public static LayoutReport Inspect(
        Fragment? fragments,
        IReadOnlyList<string> invariantViolations,
        SizeF contentSize,
        IReadOnlyDictionary<BDom.DomElement, BoxGeometry>? geometry,
        BDom.DomDocument? document,
        int viewportWidth,
        int viewportHeight)
    {
        var byDisplay = new Dictionary<string, int>(StringComparer.Ordinal);
        var fonts = new Dictionary<string, int>(StringComparer.Ordinal);
        var tall = new List<(float Excess, Fragment Fragment, string Path)>();
        var tallThreshold = Math.Max(4000f, viewportHeight * 5f);
        var boxes = 0;
        var textRuns = 0;
        var maxDepth = 0;

        if (fragments is not null)
        {
            Walk(fragments, 0, string.Empty);

            void Walk(Fragment fragment, int depth, string path)
            {
                boxes++;
                var here = path.Length == 0 ? Tag(fragment) : path + " > " + Tag(fragment);
                if (TallerThanContent(fragment, tallThreshold) is { } excess)
                    tall.Add((excess, fragment, path));

                maxDepth = Math.Max(maxDepth, depth);
                var display = fragment.Style.Display ?? "(none)";
                byDisplay[display] = byDisplay.GetValueOrDefault(display) + 1;

                if (fragment.Lines is { } lines)
                {
                    foreach (var line in lines)
                    {
                        foreach (var inline in line.Inlines)
                        {
                            if (string.IsNullOrWhiteSpace(inline.Text))
                                continue;

                            textRuns++;
                            var family = inline.Style.FontFamily;
                            if (!string.IsNullOrWhiteSpace(family))
                                fonts[family] = fonts.GetValueOrDefault(family) + 1;
                        }
                    }
                }

                foreach (var child in fragment.Children)
                    Walk(child, depth + 1, here);
            }
        }

        var report = new LayoutReport
        {
            Boxes = boxes,
            TextRuns = textRuns,
            MaxDepth = maxDepth,
            ContentSize = string.Create(
                CultureInfo.InvariantCulture, $"{contentSize.Width:0.#}×{contentSize.Height:0.#}"),
            ContentHeight = contentSize.Height,
            TallerThanContent = [.. tall
                .OrderByDescending(static t => t.Excess)
                .Take(MaxFindings)
                .Select(t => TallFinding(t.Fragment, t.Path, t.Excess, geometry))],
            BoxesByDisplay = byDisplay.OrderByDescending(static p => p.Value).ToDictionary(static p => p.Key, static p => p.Value),
            InvariantViolations = invariantViolations,
            FontFamilies = fonts.OrderByDescending(static p => p.Value).ToDictionary(static p => p.Key, static p => p.Value),
        };

        return geometry is null || document is null ? report : WithElementFindings(report, geometry, document, viewportWidth);
    }

    private static LayoutReport WithElementFindings(
        LayoutReport report,
        IReadOnlyDictionary<BDom.DomElement, BoxGeometry> geometry,
        BDom.DomDocument document,
        int viewportWidth)
    {
        var overflow = new List<(float Amount, LayoutFinding Finding)>();
        var collapsed = new List<LayoutFinding>();
        var offPage = new List<LayoutFinding>();
        var unboxed = new Dictionary<string, int>(StringComparer.Ordinal);

        var body = FindBody(document);
        if (body is null)
            return report;

        foreach (var element in Descendants(body))
        {
            var tag = element.LocalName.ToLowerInvariant();
            if (!geometry.TryGetValue(element, out var box))
            {
                if (!NeverBoxed.Contains(tag) && !InsideUnrendered(element))
                    unboxed[tag] = unboxed.GetValueOrDefault(tag) + 1;
                continue;
            }

            var border = box.BorderBox;
            var right = border.Right;
            if (right > viewportWidth + 0.5f)
            {
                // Only the element that starts the overflow: its ancestors reach as far only because
                // they contain it, and listing every one of them would bury it.
                var parentRight = element.ParentElement is { } parent && geometry.TryGetValue(parent, out var parentBox)
                    ? parentBox.BorderBox.Right
                    : 0f;
                if (parentRight < right - 0.5f)
                {
                    overflow.Add((right - viewportWidth, Finding(element, border,
                        string.Create(CultureInfo.InvariantCulture, $"right edge at {right:0.#}px, {right - viewportWidth:0.#}px past the viewport"))));
                }
            }

            // The outermost collapsed element only: everything inside a zero-size box is zero-size
            // with it, and would otherwise bury the one element that collapsed. An svg's content is
            // left out: the SVG renderer draws it from its own attributes, not from boxes, and the
            // layout gives each element in it a zero-size box, whether it is drawn, like a <text>, or
            // is never drawn, like an icon's <title> or <desc>.
            if ((border.Width <= 0 || border.Height <= 0)
                && OwnText(element) is { Length: > 0 } text
                && !InsideCollapsed(element, geometry)
                && !InsideSvg(element))
            {
                collapsed.Add(Finding(element, border,
                    $"laid out {Size(border)} but holds text: \"{AnalysisConsole.OneLine(text, 60)}\""));
            }

            if (border.Width > 0 && border.Height > 0 && (border.Right <= 0 || border.Bottom <= 0))
                offPage.Add(Finding(element, border, "entirely above or left of the page"));
        }

        return report with
        {
            HorizontalOverflow = [.. overflow.OrderByDescending(static o => o.Amount).Take(MaxFindings).Select(static o => o.Finding)],
            CollapsedWithText = [.. collapsed.Take(MaxFindings)],
            OffPage = [.. offPage.Take(MaxFindings)],
            UnboxedElementsByTag = unboxed.OrderByDescending(static p => p.Value).ToDictionary(static p => p.Key, static p => p.Value),
        };
    }

    /// <summary>
    /// How much taller <paramref name="fragment"/> is than everything in it put together, when that is
    /// more than <paramref name="threshold"/> and more than three times its content — or null.
    /// </summary>
    /// <remarks>
    /// Summing the heights of the children and the line boxes, rather than measuring where they sit,
    /// is deliberate: it needs no assumption about which coordinate space an inline fragment is in, and
    /// a container is never flagged for holding a tall child, because the child's height is in the
    /// sum. A multi-column container, whose children sit side by side, sums to more than itself and is
    /// never flagged either.
    /// </remarks>
    internal static float? TallerThanContent(Fragment fragment, float threshold)
    {
        var height = fragment.Size.Height;
        if (height <= threshold)
            return null;

        var content = 0f;
        foreach (var child in fragment.Children)
            content += Math.Max(0f, child.Size.Height);
        if (fragment.Lines is { } lines)
        {
            foreach (var line in lines)
                content += Math.Max(0f, line.Height);
        }

        var excess = height - content;
        return excess > threshold && height > content * 3f ? excess : null;
    }

    private static LayoutFinding TallFinding(
        Fragment fragment,
        string path,
        float excess,
        IReadOnlyDictionary<BDom.DomElement, BoxGeometry>? geometry)
    {
        var bounds = fragment.Bounds;
        var detail = string.Create(
            CultureInfo.InvariantCulture,
            $"{bounds.Height:N0}px tall, {excess:N0}px more than all its children and lines together");

        // The fragment knows only its tag; the element geometry of the same layout knows which element
        // has this box, so the finding can name it the way a reader searches the document.
        if (geometry is not null)
        {
            foreach (var (element, box) in geometry)
            {
                var border = box.BorderBox;
                if (string.Equals(element.LocalName, fragment.Style.TagName, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(border.X - bounds.X) < 1 && Math.Abs(border.Y - bounds.Y) < 1
                    && Math.Abs(border.Height - bounds.Height) < 1)
                {
                    return new LayoutFinding(Describe(element), PathOf(element), Size(bounds, withOrigin: true), detail);
                }
            }
        }

        return new LayoutFinding(Tag(fragment), path.Length == 0 ? "(root)" : "… > " + LastSegments(path, 4), Size(bounds, withOrigin: true), detail);
    }

    private static string Tag(Fragment fragment) => fragment.Style.TagName ?? "anonymous";

    private static string LastSegments(string path, int count)
    {
        var parts = path.Split(" > ");
        return string.Join(" > ", parts[Math.Max(0, parts.Length - count)..]);
    }

    /// <summary>Writes the fragment tree as indented text, one box per line, for a reader to scan.</summary>
    public static void WriteFragmentTree(Fragment root, string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Layout fragment tree of the viewport layout: one box per line, border box in document px.");
        builder.AppendLine("# <tag> kind display [position] [overflow] x,y w×h [margin/border/padding when set] [first text]");
        Append(root, 0);
        File.WriteAllText(path, builder.ToString());

        void Append(Fragment fragment, int depth)
        {
            var style = fragment.Style;
            builder.Append(' ', depth * 2)
                .Append('<').Append(style.TagName ?? "anonymous").Append('>')
                .Append(' ').Append(style.Kind)
                .Append(' ').Append(style.Display);

            if (!string.Equals(style.Position, "static", StringComparison.Ordinal))
                builder.Append(" pos:").Append(style.Position);
            if (!string.Equals(style.Overflow, "visible", StringComparison.Ordinal))
                builder.Append(" overflow:").Append(style.Overflow);
            if (!string.Equals(style.Visibility, "visible", StringComparison.Ordinal))
                builder.Append(" visibility:").Append(style.Visibility);

            builder.Append(' ').Append(Size(fragment.Bounds, withOrigin: true));
            AppendEdges(builder, "m", fragment.Margin);
            AppendEdges(builder, "b", fragment.Border);
            AppendEdges(builder, "p", fragment.Padding);

            if (FirstText(fragment) is { } text)
                builder.Append(" \"").Append(AnalysisConsole.OneLine(text, 60)).Append('"');

            builder.AppendLine();
            foreach (var child in fragment.Children)
                Append(child, depth + 1);
        }
    }

    private static void AppendEdges(StringBuilder builder, string label, BoxEdges edges)
    {
        if (edges.Top == 0 && edges.Right == 0 && edges.Bottom == 0 && edges.Left == 0)
            return;

        builder.Append(' ').Append(label).Append(':').Append(string.Create(
            CultureInfo.InvariantCulture,
            $"{edges.Top:0.#},{edges.Right:0.#},{edges.Bottom:0.#},{edges.Left:0.#}"));
    }

    private static string? FirstText(Fragment fragment)
    {
        if (fragment.Lines is not { } lines)
            return null;

        foreach (var line in lines)
        {
            foreach (var inline in line.Inlines)
            {
                if (!string.IsNullOrWhiteSpace(inline.Text))
                    return inline.Text.Trim();
            }
        }

        return null;
    }

    private static LayoutFinding Finding(BDom.DomElement element, RectangleF border, string detail) =>
        new(Describe(element), PathOf(element), Size(border, withOrigin: true), detail);

    /// <summary><c>tag#id.class.class</c>, the way a reader would write a selector for it.</summary>
    internal static string Describe(BDom.DomElement element)
    {
        var builder = new StringBuilder(element.LocalName.ToLowerInvariant());
        if (element.Id is { Length: > 0 } id)
            builder.Append('#').Append(id);

        if (element.GetAttribute("class") is { Length: > 0 } classes)
        {
            foreach (var name in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(3))
                builder.Append('.').Append(name);
        }

        return builder.Length > 80 ? builder.ToString(0, 80) + "…" : builder.ToString();
    }

    private static string PathOf(BDom.DomElement element)
    {
        var chain = new List<string>();
        for (var current = element.ParentElement; current is not null && chain.Count < 4; current = current.ParentElement)
        {
            var tag = current.LocalName.ToLowerInvariant();
            if (tag is "html" or "body")
                break;

            chain.Add(Describe(current));
        }

        chain.Reverse();
        return chain.Count == 0 ? "body" : "… > " + string.Join(" > ", chain);
    }

    private static string Size(RectangleF box, bool withOrigin = false) => withOrigin
        ? string.Create(CultureInfo.InvariantCulture, $"{box.X:0.#},{box.Y:0.#} {box.Width:0.#}×{box.Height:0.#}")
        : string.Create(CultureInfo.InvariantCulture, $"{box.Width:0.#}×{box.Height:0.#}");

    /// <summary>The element's own text — its text children, not its descendants'.</summary>
    private static string? OwnText(BDom.DomElement element)
    {
        StringBuilder? builder = null;
        foreach (var child in element.ChildNodes)
        {
            if (child.NodeType == BDom.DomNodeType.Text && child.NodeValue is { } value && !string.IsNullOrWhiteSpace(value))
                (builder ??= new StringBuilder()).Append(value.Trim()).Append(' ');
        }

        return builder?.ToString().Trim();
    }

    private static bool InsideCollapsed(BDom.DomElement element, IReadOnlyDictionary<BDom.DomElement, BoxGeometry> geometry)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (geometry.TryGetValue(current, out var box) && (box.BorderBox.Width <= 0 || box.BorderBox.Height <= 0))
                return true;
        }

        return false;
    }

    private static bool InsideUnrendered(BDom.DomElement element)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (NeverBoxed.Contains(current.LocalName))
                return true;
        }

        return false;
    }

    private static bool InsideSvg(BDom.DomElement element)
    {
        for (var current = element.ParentElement; current is not null; current = current.ParentElement)
        {
            if (string.Equals(current.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static BDom.DomElement? FindBody(BDom.DomDocument document)
    {
        foreach (var element in Descendants(document))
        {
            if (string.Equals(element.LocalName, "body", StringComparison.OrdinalIgnoreCase))
                return element;
        }

        return null;
    }

    /// <summary>Every element below <paramref name="root"/>, in document order, without recursion.</summary>
    internal static IEnumerable<BDom.DomElement> Descendants(BDom.DomNode root)
    {
        var stack = new Stack<BDom.DomNode>();
        for (var i = root.ChildNodes.Count - 1; i >= 0; i--)
            stack.Push(root.ChildNodes[i]);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is BDom.DomElement element)
                yield return element;

            for (var i = node.ChildNodes.Count - 1; i >= 0; i--)
                stack.Push(node.ChildNodes[i]);
        }
    }
}

/// <summary>
/// Draws every layout box's border box over a copy of the screenshot, coloured by depth, with the
/// boxes that reach past the viewport's right edge in red — the picture that shows where the layout
/// put things, which the screenshot alone cannot when a box has no background.
/// </summary>
internal static class BoxOutlines
{
    private static readonly BColor[] DepthColours =
    [
        BColor.FromArgb(170, 31, 119, 180),
        BColor.FromArgb(170, 44, 160, 44),
        BColor.FromArgb(170, 148, 103, 189),
        BColor.FromArgb(170, 255, 127, 14),
        BColor.FromArgb(170, 23, 190, 207),
        BColor.FromArgb(170, 188, 189, 34),
    ];

    private static readonly BColor Overflowing = BColor.FromArgb(230, 214, 39, 40);

    public static void Draw(BBitmap bitmap, Fragment root, int viewportWidth)
    {
        Walk(root, 0);

        void Walk(Fragment fragment, int depth)
        {
            var bounds = fragment.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var colour = bounds.Right > viewportWidth + 0.5f ? Overflowing : DepthColours[depth % DepthColours.Length];
                Rectangle(bitmap, bounds, colour);
            }

            foreach (var child in fragment.Children)
                Walk(child, depth + 1);
        }
    }

    private static void Rectangle(BBitmap bitmap, RectangleF box, BColor colour)
    {
        var left = (int)Math.Floor(box.Left);
        var top = (int)Math.Floor(box.Top);
        var right = (int)Math.Ceiling(box.Right) - 1;
        var bottom = (int)Math.Ceiling(box.Bottom) - 1;

        for (var x = left; x <= right; x++)
        {
            Blend(bitmap, x, top, colour);
            Blend(bitmap, x, bottom, colour);
        }

        for (var y = top + 1; y < bottom; y++)
        {
            Blend(bitmap, left, y, colour);
            Blend(bitmap, right, y, colour);
        }
    }

    private static void Blend(BBitmap bitmap, int x, int y, BColor colour)
    {
        if ((uint)x >= (uint)bitmap.Width || (uint)y >= (uint)bitmap.Height)
            return;

        bitmap.SetPixel(x, y, Over(colour, bitmap.GetPixel(x, y)));
    }

    /// <summary>Porter-Duff source-over on straight alpha, as <see cref="HtmlRender"/> composites frames.</summary>
    internal static BColor Over(BColor source, BColor destination)
    {
        if (source.A == 255 || destination.A == 0)
            return source;
        if (source.A == 0)
            return destination;

        var sa = source.A / 255f;
        var da = destination.A / 255f;
        var outA = sa + da * (1f - sa);
        if (outA <= 0f)
            return BColor.FromArgb(0, 0, 0, 0);

        int Channel(byte s, byte d) =>
            Math.Clamp((int)Math.Round((s * sa + d * da * (1f - sa)) / outA), 0, 255);

        return BColor.FromArgb(
            Math.Clamp((int)Math.Round(outA * 255f), 0, 255),
            Channel(source.R, destination.R),
            Channel(source.G, destination.G),
            Channel(source.B, destination.B));
    }
}
