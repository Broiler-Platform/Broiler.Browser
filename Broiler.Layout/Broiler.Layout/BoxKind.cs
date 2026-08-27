namespace Broiler.Layout;

/// <summary>
/// Classifies the semantic role of a CSS box, derived from the HTML tag name
/// during style resolution. Layout code uses this enum instead of checking
/// tag names directly, decoupling layout from the DOM. Relocated from the
/// renderer's IR into <c>Broiler.Layout</c> with the layout code (see
/// <c>Broiler.Layout/docs/roadmap.md</c>).
/// </summary>
public enum BoxKind
{
    /// <summary>Default value for anonymous boxes or unknown elements.</summary>
    Anonymous = 0,

    /// <summary>Generic block-level element (div, p, section, etc.).</summary>
    Block,

    /// <summary>Generic inline element (span, em, strong, etc.).</summary>
    Inline,

    /// <summary>Replaced image element (<c>&lt;img&gt;</c>).</summary>
    ReplacedImage,

    /// <summary>Replaced iframe element (<c>&lt;iframe&gt;</c>).</summary>
    ReplacedIframe,

    /// <summary>Table cell (<c>&lt;td&gt;</c> / <c>&lt;th&gt;</c>).</summary>
    TableCell,

    /// <summary>Table element (<c>&lt;table&gt;</c>).</summary>
    Table,

    /// <summary>Table row (<c>&lt;tr&gt;</c>).</summary>
    TableRow,

    /// <summary>List item (<c>&lt;li&gt;</c>).</summary>
    ListItem,

    /// <summary>Ordered list (<c>&lt;ol&gt;</c>).</summary>
    OrderedList,

    /// <summary>Unordered list (<c>&lt;ul&gt;</c>).</summary>
    UnorderedList,

    /// <summary>Horizontal rule (<c>&lt;hr&gt;</c>).</summary>
    HorizontalRule,

    /// <summary>Line break (<c>&lt;br&gt;</c>).</summary>
    LineBreak,

    /// <summary>Anchor / hyperlink (<c>&lt;a&gt;</c>).</summary>
    Anchor,

    /// <summary>Font element (<c>&lt;font&gt;</c>).</summary>
    Font,

    /// <summary>Form input element (<c>&lt;input&gt;</c>).</summary>
    Input,

    /// <summary>Heading elements (<c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c>).</summary>
    Heading,
}
