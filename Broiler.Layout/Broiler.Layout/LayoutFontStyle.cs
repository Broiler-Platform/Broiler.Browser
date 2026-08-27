namespace Broiler.Layout;

/// <summary>
/// Font style flags used when resolving a font through
/// <see cref="ILayoutEnvironment.GetFont"/>. Declared by <c>Broiler.Layout</c>
/// so the component does not depend on the renderer's font-style enum; the
/// renderer-side environment implementation maps between the two. Flag values
/// mirror the conventional <c>FontStyle</c> layout so the mapping is identity.
/// </summary>
[Flags]
public enum LayoutFontStyle
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
    Strikeout = 8,
}
