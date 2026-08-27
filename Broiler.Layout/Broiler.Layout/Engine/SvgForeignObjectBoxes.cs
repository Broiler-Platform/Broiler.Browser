using System.Globalization;
using Broiler.CSS;

namespace Broiler.Layout.Engine;

/// <summary>
/// SVG 2 §12.1 <c>&lt;foreignObject&gt;</c>: the one place an SVG subtree re-enters CSS layout, and
/// so the one box under an <c>&lt;svg&gt;</c> viewport that must not stay hidden.
/// </summary>
/// <remarks>
/// <para>
/// An SVG subtree is not laid out by CSS box rules here — it is serialised back to markup and drawn
/// by <see cref="Broiler.Layout.IR.SvgRenderer"/> — so the style pass sets every child box of an
/// outermost <c>&lt;svg&gt;</c> to <c>display: none</c>. That is right for shapes and wrong for
/// <c>&lt;foreignObject&gt;</c>, whose content is a CSS-laid-out HTML subtree positioned at the
/// element's viewport rect. Hidden along with the shapes, that content had no box at all: a
/// <c>&lt;div&gt;</c> inside one reported a <c>getBoundingClientRect</c> of <c>0,0,0,0</c> and an
/// <c>offsetWidth</c>/<c>offsetHeight</c> of <c>0</c>, so <c>elementFromPoint</c> over the child
/// answered the <c>&lt;foreignObject&gt;</c>. The element itself always had a rect — it resolves
/// from its own geometry attributes like any other shape — which is why the gap was in the subtree
/// rather than in the element.
/// </para>
/// <para>
/// This pass lifts each <c>&lt;foreignObject&gt;</c> back out: it becomes an absolutely positioned
/// block at its user-space <c>x</c>/<c>y</c>, sized from its <c>width</c>/<c>height</c>, inside the
/// viewport box — which the pass makes a containing block by giving it <c>position: relative</c>,
/// and only when it actually holds one, so a document without a <c>&lt;foreignObject&gt;</c> lays
/// out exactly as it did. The HTML children keep the styles the cascade already gave them (the
/// cascade descends through hidden boxes; only layout skips them) and lay out by the ordinary rules,
/// with no special case below this point.
/// </para>
/// <para>
/// One reached through a <c>&lt;g&gt;</c> chain is re-parented onto the viewport box rather than
/// having the chain un-hidden, so the hiding of every other box is left byte-identical. Only the
/// moved subtree changes place, and only in the box tree: the serialised markup keeps the whole
/// subtree either way, and <c>SvgStructure</c> lists <c>foreignObject</c> as a non-painting
/// container so nothing inside one is also drawn by the renderer's shape passes.
/// </para>
/// <para>
/// It is driven from <see cref="FlexGridItemBlockification.Generate"/> rather than from its own line
/// in <c>DomParser</c>, for the same reason <see cref="DisplayContentsBoxes"/> is: that method is in
/// the <c>Broiler.HTML</c> submodule, which this session cannot push to. The box fix-up sequence
/// runs after the cascade and before layout, which is exactly where the placement belongs. The pass
/// is idempotent — see <see cref="CssBoxProperties.SvgForeignObjectPlaced"/> — so adding a direct
/// call later is a no-op here.
/// </para>
/// <para>
/// <b>What is modelled and what is not.</b> The viewport mapping modelled here is the identity — one
/// user unit is one CSS pixel. A <c>viewBox</c> that maps user space is not: its scale is a function
/// of the viewport's <em>used</em> size, which a pass running before layout does not have. Under one,
/// a <c>&lt;foreignObject&gt;</c> keeps no box, exactly as before — a placement that is absent rather
/// than confidently wrong. The same holds inside a nested <c>&lt;svg&gt;</c> viewport, whose own box
/// position is not SVG-accurate to begin with. Of the ancestor <c>transform</c> functions only
/// <c>translate()</c> is accumulated, by the same rule and the same parser the bridge's SVG geometry
/// uses (<see cref="TryParseLoneTranslate"/>), so the element's own rect and its content's cannot
/// disagree about which offsets counted.
/// </para>
/// </remarks>
internal static class SvgForeignObjectBoxes
{
    /// <summary>
    /// Places the <c>&lt;foreignObject&gt;</c> content under every outermost <c>&lt;svg&gt;</c> in
    /// the tree. Idempotent: a tree whose foreign objects are already placed is left untouched.
    /// </summary>
    internal static void Generate(CssBox root)
    {
        if (root != null)
            Walk(root);
    }

    /// <summary>
    /// The offset of a transform list that is a lone <c>translate(x[, y])</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow, and shared with the bridge's SVG client-rect resolution so the two
    /// cannot disagree: a list containing anything else answers <see langword="false"/> and
    /// contributes no offset, rather than having some functions applied and others dropped — which
    /// would place a subtree confidently and wrongly.
    /// </remarks>
    internal static bool TryParseLoneTranslate(string transformList, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (string.IsNullOrWhiteSpace(transformList))
            return false;

        var value = transformList.Trim();
        if (!value.StartsWith("translate", StringComparison.OrdinalIgnoreCase))
            return false;

        var open = value.IndexOf('(');
        var close = value.IndexOf(')');
        if (open < 0 || close <= open)
            return false;

        // One function only: anything after the closing paren is a list this does not model.
        if (value[(close + 1)..].Trim().Length > 0)
            return false;

        var parts = value[(open + 1)..close]
            .Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2)
            return false;

        // A CSS `transform: translate(10px, 20px)` reaches here too, since the computed value is
        // preferred over the attribute; strip the unit that an SVG attribute never carries.
        var offsets = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var scalar = parts[i].EndsWith("px", StringComparison.OrdinalIgnoreCase)
                ? parts[i][..^2]
                : parts[i];
            if (!double.TryParse(scalar, NumberStyles.Float, CultureInfo.InvariantCulture, out offsets[i])
                || !double.IsFinite(offsets[i]))
            {
                return false;
            }
        }

        x = offsets[0];
        if (offsets.Length == 2)
            y = offsets[1];

        return true;
    }

    /// <summary>
    /// Finds the outermost <c>&lt;svg&gt;</c> boxes. An <c>&lt;svg&gt;</c> is not descended into —
    /// a nested viewport is not modelled — but each foreign object it places is, because that
    /// subtree is HTML and may hold an <c>&lt;svg&gt;</c> of its own that is outermost in it.
    /// </summary>
    private static void Walk(CssBox box)
    {
        if (SvgLocalName(box) == "svg")
        {
            foreach (var placed in PlaceForeignObjects(box))
                Walk(placed);

            return;
        }

        // The placement re-parents onto the viewport, which cannot affect this level, but iterate
        // over a snapshot anyway so the walk does not depend on that.
        foreach (var child in box.Boxes.ToArray())
            Walk(child);
    }

    /// <summary>
    /// Turns every placeable <c>&lt;foreignObject&gt;</c> under <paramref name="svgBox"/> into a
    /// laid-out box, and answers the ones it placed.
    /// </summary>
    private static List<CssBox> PlaceForeignObjects(CssBox svgBox)
    {
        var placed = new List<CssBox>();
        if (HasViewBox(svgBox))
            return placed;

        List<(CssBox Box, double X, double Y)> found = [];
        CollectForeignObjects(svgBox, 0, 0, found);
        if (found.Count == 0)
            return placed;

        foreach (var (box, x, y) in found)
        {
            // The setter detaches from the old parent and appends to the new one.
            if (!ReferenceEquals(box.ParentBox, svgBox))
                box.ParentBox = svgBox;

            Place(box, x, y);
            placed.Add(box);
        }

        if (string.IsNullOrEmpty(svgBox.Position)
            || svgBox.Position.Equals("static", StringComparison.OrdinalIgnoreCase))
        {
            svgBox.Position = CssConstants.Relative;
        }

        return placed;
    }

    /// <summary>
    /// Walks the SVG subtree for <c>&lt;foreignObject&gt;</c> boxes that can be placed, carrying the
    /// accumulated <c>translate()</c> down with them. A nested <c>&lt;svg&gt;</c> is a viewport of
    /// its own and is not descended into; a <c>&lt;foreignObject&gt;</c>'s own subtree is HTML and
    /// is left to the ordinary rules.
    /// </summary>
    private static void CollectForeignObjects(CssBox box, double x, double y,
        List<(CssBox Box, double X, double Y)> found)
    {
        foreach (var child in box.Boxes)
        {
            var name = SvgLocalName(child);
            if (name is null || name == "svg")
                continue;

            var (childX, childY) = (x, y);
            if (TryParseLoneTranslate(TransformOf(child), out var dx, out var dy))
                (childX, childY) = (x + dx, y + dy);

            if (name == "foreignobject")
            {
                if (!child.SvgForeignObjectPlaced && HasResolvableSize(child))
                    found.Add((child, childX, childY));

                continue;
            }

            CollectForeignObjects(child, childX, childY, found);
        }
    }

    /// <summary>
    /// Turns a collected <c>&lt;foreignObject&gt;</c> into an absolutely positioned block at its
    /// place in the viewport.
    /// </summary>
    private static void Place(CssBox box, double x, double y)
    {
        // CSS wins over the presentation attributes, the same order the style pass' SVG replaced
        // sizing takes: an attribute only fills in an axis the cascade left auto.
        if (IsAuto(box.Width))
            box.Width = GeometryLength(box, "width");
        if (IsAuto(box.Height))
            box.Height = GeometryLength(box, "height");

        box.Display = CssConstants.Block;
        box.Position = CssConstants.Absolute;
        box.Left = Offset(GeometryLength(box, "x"), x);
        box.Top = Offset(GeometryLength(box, "y"), y);

        // The ancestor translate is already in Left/Top; leaving the element's own transform on the
        // box as well would apply it a second time.
        box.Transform = CssConstants.None;

        // SVG 2 §12.1: the content is clipped to the element's rect.
        if (string.IsNullOrEmpty(box.Overflow) || box.Overflow == CssConstants.Visible)
            box.Overflow = CssConstants.Hidden;

        box.SvgForeignObjectPlaced = true;
    }

    /// <summary>
    /// Whether both axes have a size this pass can give the box. SVG 2 §12.1 does not render a
    /// <c>&lt;foreignObject&gt;</c> with an absent or zero <c>width</c> or <c>height</c>, and a box
    /// with an auto size would take the containing block's rather than none.
    /// </summary>
    private static bool HasResolvableSize(CssBox box) =>
        (!IsAuto(box.Width) || !IsAuto(GeometryLength(box, "width")))
        && (!IsAuto(box.Height) || !IsAuto(GeometryLength(box, "height")));

    /// <summary>
    /// One SVG geometry attribute as a CSS length. A bare number is user units, which under the
    /// identity viewport mapping are CSS pixels; every other spelling is a CSS length already and is
    /// passed through untouched, so a percentage resolves against the containing block — which, for
    /// a box placed inside the viewport, is the viewport.
    /// </summary>
    private static string GeometryLength(CssBox box, string attributeName)
    {
        var raw = box.HtmlTag?.TryGetAttribute(attributeName);
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        raw = raw.Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? FormatPixels(number)
            : raw;
    }

    /// <summary>A CSS length shifted by a user-space offset, as a length the cascade can resolve.</summary>
    private static string Offset(string length, double offset)
    {
        if (offset == 0)
            return string.IsNullOrEmpty(length) ? FormatPixels(0) : length;

        if (string.IsNullOrEmpty(length))
            return FormatPixels(offset);

        // Adding the offset numerically keeps the common case a plain length; anything else — a
        // percentage, an em, a calc() — is summed by the layout engine instead.
        return TryParsePixels(length, out var pixels)
            ? FormatPixels(pixels + offset)
            : $"calc({length} + {FormatPixels(offset)})";
    }

    /// <summary>The element's transform, preferring the cascaded value over the presentation
    /// attribute.</summary>
    private static string TransformOf(CssBox box)
    {
        var computed = box.Transform;
        if (!string.IsNullOrWhiteSpace(computed)
            && !computed.Equals(CssConstants.None, StringComparison.OrdinalIgnoreCase))
        {
            return computed;
        }

        return box.HtmlTag?.TryGetAttribute("transform");
    }

    /// <summary>The box's tag name without an <c>svg:</c> prefix, lowercased, or
    /// <see langword="null"/> for a box that is not an element.</summary>
    private static string SvgLocalName(CssBox box)
    {
        var tag = box.HtmlTag?.Name;
        if (string.IsNullOrEmpty(tag))
            return null;

        var colon = tag.LastIndexOf(':');
        if (colon >= 0)
            tag = tag[(colon + 1)..];

        return tag.ToLowerInvariant();
    }

    /// <summary>Whether an <c>&lt;svg&gt;</c> maps its user space with a <c>viewBox</c>.</summary>
    private static bool HasViewBox(CssBox box) =>
        !string.IsNullOrWhiteSpace(box.HtmlTag?.TryGetAttribute("viewBox"));

    private static bool IsAuto(string value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Trim().Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase);

    private static bool TryParsePixels(string value, out double pixels)
    {
        pixels = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^2].Trim();

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out pixels)
            && double.IsFinite(pixels);
    }

    private static string FormatPixels(double pixels) =>
        pixels.ToString("0.####", CultureInfo.InvariantCulture) + "px";
}
